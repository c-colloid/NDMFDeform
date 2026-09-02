using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;
using float3 = Unity.Mathematics.float3;
using float4x4 = Unity.Mathematics.float4x4;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 参照した体のメッシュ(Body)に沿って衣装を寄せる / 離すデフォーマ(非対応衣装の体合わせ)。
	///
	/// 各頂点について体表面の最近接点と符号付き距離 d(外側が正)を求め、
	/// 体との隙間が [minGap, maxGap] の帯に収まるよう法線方向へ動かす:
	/// - d &lt; minGap(めり込み・近すぎ)→ minGap まで押し出す
	/// - d &gt; maxGap(離れすぎ)→ maxGap まで引き寄せる(pullIn が真のときのみ)
	/// - 帯の中はそのまま
	/// ぴったりした衣装は minGap = maxGap(帯幅 0)、ブカッとした衣装は pullIn を切るか
	/// maxGap を大きく取り「めり込みだけ直す」設定にする。
	///
	/// 適用範囲は二重球(innerRadius の内側で 100%、outerRadius まで滑らかに減衰)で指定する。
	/// 最近接点写像は凹んだ部位(脇・股)で隣り合う頂点が離れた体表面へ写り布が折れるため、
	/// 変位ベクトルを衣装メッシュの隣接で平滑化してから適用し、最後にもう一度
	/// minGap を保証する(enforceMinGap)。
	///
	/// ブレンドシェイプ: 既定(FixedDisplacement)では基本形状で求めた変位をそのまま
	/// 各シェイプフレームにも足す(衣装のシェイプ形状を維持し、ベイクも軽い)。
	/// RefitEachFrame はフレームごとに体へ再フィットする(衣装のシェイプが体の基本形状へ潰れる)。
	/// 体側のブレンドシェイプは useBodyBlendShapes でレンダラーの現在の重みを反映する。
	///
	/// ボーンウェイトは変更しない(衣装の元のウェイトのまま)。ウェイト転写は別機能。
	/// </summary>
	[DeformerMeta(Name = "Body Fit", Category = DeformerCategory.Shape,
	              Description = "参照した体のメッシュに沿って衣装を寄せる / 離す(非対応衣装の体合わせ)")]
	[AddComponentMenu("NDMF Deform/Deformers/Body Fit")]
	public class BodyFitDeformer : DeformerBase, IRendererReferences
	{
		public enum FitRegion
		{
			/// <summary>二重球の内側にのみ適用(内半径で 100%、外半径まで減衰)</summary>
			Sphere = 0,

			/// <summary>メッシュ全体に適用</summary>
			WholeMesh = 1,
		}

		public enum BlendShapeFitMode
		{
			/// <summary>基本形状で求めた変位を各シェイプフレームにもそのまま足す(シェイプ形状を維持)</summary>
			FixedDisplacement = 0,

			/// <summary>シェイプフレームごとに体へ再フィットする</summary>
			RefitEachFrame = 1,
		}

		[SerializeField, Tooltip("沿わせる体のレンダラー(SkinnedMeshRenderer / MeshRenderer)。衣装と同じアバター上のものを指定する")]
		private Renderer body;

		[SerializeField, Tooltip("体のレンダラーに設定されている現在のブレンドシェイプ重みを体の形状に反映する")]
		private bool useBodyBlendShapes = true;

		[SerializeField, Range(0f, 1f), Tooltip("全体の効き。0.5 なら目標位置までの半分だけ動く")]
		private float factor = 1f;

		[SerializeField, Tooltip("適用範囲。Sphere は二重球の内側のみ、WholeMesh はメッシュ全体")]
		private FitRegion region = FitRegion.Sphere;

		[SerializeField, Min(0f), Tooltip("100% 適用する球の半径(シーンでは実線)")]
		private float innerRadius = 0.15f;

		[SerializeField, Min(0f), Tooltip("適用が 0 になる球の半径(シーンでは点線)。内半径との間で滑らかに減衰する")]
		private float outerRadius = 0.25f;

		[SerializeField, Tooltip("体との最小の隙間(m)。これより近い / めり込んでいる頂点を押し出す")]
		private float minGap = 0.005f;

		[SerializeField, Tooltip("離れすぎた頂点を体へ引き寄せる。切るとめり込みの解消だけを行う(ブカッとした衣装向け)")]
		private bool pullIn = true;

		[SerializeField, Tooltip("体との最大の隙間(m)。これより遠い頂点を引き寄せる(pullIn が有効なとき)")]
		private float maxGap = 0.005f;

		[SerializeField, Min(0f), Tooltip("体表面を探す距離の上限(m)。これより体から離れた頂点は対象外(上限の 75% から滑らかに効きが減る)")]
		private float searchDistance = 0.1f;

		[SerializeField, Range(0, 30), Tooltip("変位の平滑化回数。凹んだ部位で布が折れるのを抑える(0 で無効)")]
		private int smoothIterations = 4;

		[SerializeField, Range(0f, 1f), Tooltip("平滑化 1 回あたりの強さ")]
		private float smoothStrength = 0.5f;

		[SerializeField, Tooltip("平滑化後にもう一度 minGap を保証する(めり込みを残さない)")]
		private bool enforceMinGap = true;

		[SerializeField, Tooltip("ブレンドシェイプの扱い。FixedDisplacement は衣装のシェイプ形状を維持、RefitEachFrame はフレームごとに再フィット")]
		private BlendShapeFitMode blendShapes = BlendShapeFitMode.FixedDisplacement;

		[SerializeField, Tooltip("体の表裏を反転する(法線が内向きのメッシュ用。負のスケールによる反転は自動で補正される)")]
		private bool flipBodyNormals;

		[SerializeField] private Transform axisOverride;

		public Renderer Body { get => body; set => body = value; }
		public bool UseBodyBlendShapes { get => useBodyBlendShapes; set => useBodyBlendShapes = value; }
		public float Factor { get => factor; set => factor = Mathf.Clamp01(value); }
		public FitRegion Region { get => region; set => region = value; }
		public float InnerRadius { get => innerRadius; set => innerRadius = Mathf.Max(0f, value); }
		public float OuterRadius { get => outerRadius; set => outerRadius = Mathf.Max(0f, value); }
		public float MinGap { get => minGap; set => minGap = value; }
		public bool PullIn { get => pullIn; set => pullIn = value; }
		public float MaxGap { get => maxGap; set => maxGap = value; }
		public float SearchDistance { get => searchDistance; set => searchDistance = Mathf.Max(0f, value); }
		public int SmoothIterations { get => smoothIterations; set => smoothIterations = Mathf.Clamp(value, 0, 30); }
		public float SmoothStrength { get => smoothStrength; set => smoothStrength = Mathf.Clamp01(value); }
		public bool EnforceMinGap { get => enforceMinGap; set => enforceMinGap = value; }
		public BlendShapeFitMode BlendShapes { get => blendShapes; set => blendShapes = value; }
		public bool FlipBodyNormals { get => flipBodyNormals; set => flipBodyNormals = value; }

		public override Transform Axis => axisOverride != null ? axisOverride : transform;

		public override DeformDataFlags DataFlags => DeformDataFlags.Vertices;

		// ---- ベイク用キャッシュ(シリアライズ対象外) ----

		// 参照表面(ReferenceSurfaceCache が所有。PrepareBake で取得し直す)
		[System.NonSerialized] private MeshSurfaceData _surface;
		[System.NonSerialized] private bool _surfaceReady;

		// 衣装メッシュの隣接(平滑化用。ソースメッシュが変わらない限り再利用)
		[System.NonSerialized] private Mesh _adjacencyMesh;
		[System.NonSerialized] private int _adjacencyVertexCount;
		[System.NonSerialized] private NativeArray<int> _adjStart;
		[System.NonSerialized] private NativeArray<int> _adjList;

		// 基本形状パスで求めた変位(重み込み・factor 抜き)。FixedDisplacement のシェイプフレームで使う
		[System.NonSerialized] private NativeArray<float3> _baseDisplacement;
		[System.NonSerialized] private int _passIndex;
		[System.NonSerialized] private int _vertexCount;

		private void OnValidate()
		{
			innerRadius = Mathf.Max(0f, innerRadius);
			outerRadius = Mathf.Max(innerRadius, outerRadius);
			maxGap = Mathf.Max(minGap, maxGap);
			searchDistance = Mathf.Max(0f, searchDistance);
		}

		protected virtual void Reset()
		{
			AutoDetectBody();
			FitSphereToParentStack();
		}

		private void OnDisable()
		{
			DisposeNative();
		}

		private void OnDestroy()
		{
			DisposeNative();
		}

		private void DisposeNative()
		{
			if (_adjStart.IsCreated) _adjStart.Dispose();
			if (_adjList.IsCreated) _adjList.Dispose();
			if (_baseDisplacement.IsCreated) _baseDisplacement.Dispose();
			_adjacencyMesh = null;
			_adjacencyVertexCount = 0;
			_surfaceReady = false;
		}

#if UNITY_EDITOR
		// ドメインリロード前に、生きているインスタンスの常駐 NativeArray を回収する
		[UnityEditor.InitializeOnLoadMethod]
		private static void HookEditorLifecycle()
		{
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeAllInstances;
			UnityEditor.EditorApplication.quitting += DisposeAllInstances;
		}

		private static void DisposeAllInstances()
		{
			foreach (var deformer in Resources.FindObjectsOfTypeAll<BodyFitDeformer>())
				deformer.DisposeNative();
		}
#endif

		public void CollectReferencedRenderers(List<Renderer> results)
		{
			if (body != null)
				results.Add(body);
		}

		/// <summary>親の DeformStack が付いているレンダラー(衣装自身)</summary>
		public Renderer GetOwnRenderer()
		{
			var stack = GetComponentInParent<DeformStack>();
			if (stack == null)
				return null;
			stack.TryGetComponent<Renderer>(out var renderer);
			return renderer;
		}

		/// <summary>
		/// 同じアバター(Transform ルート)配下から体のレンダラーを推定して設定する。
		/// VRChat の慣例に従い、名前が "Body" の SkinnedMeshRenderer を優先し、
		/// 無ければ名前に "body" を含むもの、それも無ければ最も頂点数の多い
		/// SkinnedMeshRenderer を使う。衣装自身は除外する。
		/// </summary>
		public bool AutoDetectBody()
		{
			var own = GetOwnRenderer();
			var root = transform.root;
			SkinnedMeshRenderer exact = null;
			SkinnedMeshRenderer partial = null;
			SkinnedMeshRenderer largest = null;
			var largestCount = 0;
			foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (smr == own || smr.sharedMesh == null)
					continue;
				var n = smr.name;
				if (string.Equals(n, "Body", System.StringComparison.OrdinalIgnoreCase))
				{
					exact = smr;
					break;
				}
				if (partial == null && n.IndexOf("body", System.StringComparison.OrdinalIgnoreCase) >= 0)
					partial = smr;
				if (smr.sharedMesh.vertexCount > largestCount)
				{
					largest = smr;
					largestCount = smr.sharedMesh.vertexCount;
				}
			}
			var found = exact != null ? exact : (partial != null ? partial : largest);
			if (found == null)
				return false;
			body = found;
			return true;
		}

		/// <summary>
		/// 二重球を衣装(親スタックのレンダラー)全体を覆う大きさ・位置に合わせる。
		/// 内半径は見た目のバウンズの半対角、外半径はその 1.25 倍。
		/// </summary>
		public bool FitSphereToParentStack()
		{
			var stack = GetComponentInParent<DeformStack>();
			if (stack == null)
				return false;

			Bounds bounds;
			Matrix4x4 meshToWorld;
			if (stack.TryGetComponent<SkinnedMeshRenderer>(out var smr) && smr.sharedMesh != null)
			{
				var baked = new Mesh();
				smr.BakeMesh(baked, true);
				baked.RecalculateBounds();
				bounds = baked.bounds;
				DestroyImmediate(baked);
				meshToWorld = stack.transform.localToWorldMatrix;
			}
			else if (stack.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
			{
				bounds = mf.sharedMesh.bounds;
				meshToWorld = stack.transform.localToWorldMatrix;
			}
			else
			{
				return false;
			}

			var worldCenter = meshToWorld.MultiplyPoint3x4(bounds.center);
			var worldExtents = Vector3.Scale(meshToWorld.lossyScale, bounds.extents);
			var radius = Mathf.Max(worldExtents.magnitude, 0.01f);

			if (axisOverride == null)
				transform.position = worldCenter;

			// 半径は軸空間で解釈されるため、軸のスケールで割ってワールド半径に合わせる
			var axisScale = Axis.lossyScale;
			var scale = Mathf.Max(Mathf.Abs(axisScale.x), Mathf.Max(Mathf.Abs(axisScale.y), Mathf.Abs(axisScale.z)));
			if (scale > 1e-6f)
				radius /= scale;
			innerRadius = radius;
			outerRadius = radius * 1.25f;
			return true;
		}

#if UNITY_EDITOR
		public override void DescribeHandles(IHandleBuilder h)
		{
			if (region != FitRegion.Sphere)
				return;

			// SphereMask と同じ二重球。inner/outer をペア宣言し、ホバー矢印が互いのリングを指すようにする
			h.RadiusSlider(nameof(innerRadius), HandleAxis.Y, HandleLineStyle.Solid, 1f,
				pairProperty: nameof(outerRadius));
			h.RadiusSlider(nameof(outerRadius), HandleAxis.Y, HandleLineStyle.Dotted, 1f,
				pairProperty: nameof(innerRadius));
			h.Circle(HandleAxis.X, 0f, nameof(innerRadius));
			h.Circle(HandleAxis.Y, 0f, nameof(innerRadius));
			h.Circle(HandleAxis.Z, 0f, nameof(innerRadius));
			h.Circle(HandleAxis.X, 0f, nameof(outerRadius), HandleLineStyle.Dotted);
			h.Circle(HandleAxis.Y, 0f, nameof(outerRadius), HandleLineStyle.Dotted);
			h.Circle(HandleAxis.Z, 0f, nameof(outerRadius), HandleLineStyle.Dotted);
		}
#endif

		public override void PrepareBake(Mesh source)
		{
			_passIndex = 0;
			_surfaceReady = false;
			_vertexCount = source != null ? source.vertexCount : 0;

			if (body == null || source == null || _vertexCount == 0)
				return;

			// 自分自身(衣装のレンダラー)を参照している場合は何もしない
			if (body == GetOwnRenderer())
				return;

			_surfaceReady = ReferenceSurfaceCache.TryGet(body, useBodyBlendShapes, flipBodyNormals, out _surface);
			if (!_surfaceReady)
				return;

			EnsureAdjacency(source);

			if (!_baseDisplacement.IsCreated || _baseDisplacement.Length != _vertexCount)
			{
				if (_baseDisplacement.IsCreated)
					_baseDisplacement.Dispose();
				_baseDisplacement = new NativeArray<float3>(_vertexCount, Allocator.Persistent,
					NativeArrayOptions.UninitializedMemory);
			}
		}

		private void EnsureAdjacency(Mesh source)
		{
			if (smoothIterations <= 0)
				return;
			if (_adjStart.IsCreated && _adjacencyMesh == source && _adjacencyVertexCount == source.vertexCount)
				return;

			if (_adjStart.IsCreated) _adjStart.Dispose();
			if (_adjList.IsCreated) _adjList.Dispose();

			var adjacency = MeshAdjacency.Build(source.vertices, ReferenceSurfaceUtility.CollectTriangles(source));
			_adjStart = new NativeArray<int>(adjacency.Start, Allocator.Persistent);
			_adjList = new NativeArray<int>(adjacency.Neighbors, Allocator.Persistent);
			_adjacencyMesh = source;
			_adjacencyVertexCount = source.vertexCount;
		}

		public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			if (factor <= 0f || !_surfaceReady || !_surface.IsCreated)
				return dependency;
			if (buffers.Length != _vertexCount || !_baseDisplacement.IsCreated)
				return dependency;

			var pass = _passIndex++;
			var n = buffers.Length;

			// シェイプフレーム(2 パス目以降)は、FixedDisplacement なら基本形状の変位を足すだけ
			if (pass > 0 && blendShapes == BlendShapeFitMode.FixedDisplacement)
			{
				return new AddDisplacementJob
				{
					displacement = _baseDisplacement,
					factor = factor,
					vertices = buffers.Vertices,
				}.Schedule(n, 128, dependency);
			}

			var delta = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var weight = new NativeArray<float>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var valid = new NativeArray<byte>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

			var handle = new QueryJob
			{
				surface = _surface,
				vertices = buffers.Vertices,
				meshToAxis = space.MeshToAxis,
				wholeMesh = region == FitRegion.WholeMesh ? 1 : 0,
				innerRadius = innerRadius,
				outerRadius = outerRadius,
				minGap = minGap,
				maxGap = pullIn ? Mathf.Max(maxGap, minGap) : float.MaxValue,
				searchDistance = searchDistance,
				delta = delta,
				weight = weight,
				valid = valid,
			}.Schedule(n, 32, dependency);

			// 変位の平滑化(ピンポンバッファ)
			var current = delta;
			var scratch = default(NativeArray<float3>);
			var smoothed = false;
			if (smoothIterations > 0 && smoothStrength > 0f && _adjList.IsCreated && _adjList.Length > 0 &&
			    _adjStart.Length == n + 1)
			{
				scratch = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
				var other = scratch;
				for (var i = 0; i < smoothIterations; i++)
				{
					handle = new SmoothJob
					{
						adjStart = _adjStart,
						adjList = _adjList,
						valid = valid,
						input = current,
						output = other,
						strength = smoothStrength,
					}.Schedule(n, 64, handle);
					var tmp = current;
					current = other;
					other = tmp;
				}
				smoothed = true;
			}

			handle = new ApplyJob
			{
				delta = current,
				weight = weight,
				factor = factor,
				vertices = buffers.Vertices,
				displacement = _baseDisplacement,
			}.Schedule(n, 128, handle);

			if (smoothed && enforceMinGap)
			{
				handle = new EnforceMinGapJob
				{
					surface = _surface,
					weight = weight,
					factor = factor,
					minGap = minGap,
					searchDistance = searchDistance,
					vertices = buffers.Vertices,
					displacement = _baseDisplacement,
				}.Schedule(n, 32, handle);
			}

			handle = delta.Dispose(handle);
			handle = weight.Dispose(handle);
			handle = valid.Dispose(handle);
			if (scratch.IsCreated)
				handle = scratch.Dispose(handle);
			return handle;
		}

		// ---- ジョブ ----

		/// <summary>
		/// 頂点ごとに領域重みと目標変位を求める。
		/// delta = 「体表面から法線方向に目標の隙間だけ離れた点」− 現在位置(重み・factor は掛けない)。
		/// 対象外(領域外・探索距離外)は delta = 0、valid = 0。帯の中は delta = 0 だが valid = 1
		/// (平滑化で「動かない頂点」として周囲の変位をなだらかにする)。
		/// </summary>
		[BurstCompile]
		public struct QueryJob : IJobParallelFor
		{
			// 内包する NativeArray は MeshSurfaceData 側で [ReadOnly] 宣言済み
			public MeshSurfaceData surface;
			[ReadOnly] public NativeArray<float3> vertices;
			public float4x4 meshToAxis;
			public int wholeMesh;
			public float innerRadius;
			public float outerRadius;
			public float minGap;
			public float maxGap;
			public float searchDistance;
			[WriteOnly] public NativeArray<float3> delta;
			[WriteOnly] public NativeArray<float> weight;
			[WriteOnly] public NativeArray<byte> valid;

			public void Execute(int index)
			{
				var p = vertices[index];
				var w = RegionWeight(p, meshToAxis, wholeMesh, innerRadius, outerRadius);
				weight[index] = w;
				if (w <= 0f)
				{
					delta[index] = float3.zero;
					valid[index] = 0;
					return;
				}

				if (!surface.FindClosest(p, searchDistance, out var hit))
				{
					delta[index] = float3.zero;
					valid[index] = 0;
					return;
				}

				// 探索距離の境界で段差が出ないよう、上限の 75% から 100% にかけて効きを減衰させる
				w *= SearchFalloff(hit.Distance, searchDistance);
				weight[index] = w;
				if (w <= 0f)
				{
					delta[index] = float3.zero;
					valid[index] = 0;
					return;
				}

				valid[index] = 1;
				var d = hit.SignedDistance;
				var target = clamp(d, minGap, maxGap);
				if (target == d)
				{
					delta[index] = float3.zero;
					return;
				}

				var dir = OutwardDirection(p, hit);
				delta[index] = hit.Point + dir * target - p;
			}
		}

		/// <summary>
		/// 変位ベクトルの平滑化 1 回分(有効な隣接の平均への補間)。
		/// 対象外の頂点(valid = 0)は変位 0 のまま動かさず、平均にも含めない
		/// (領域外・探索圏外の頂点が境界の変位を引きずらないようにする)。
		/// 隣接リストは位置で溶接した代表頂点を指すため、シーム分割された頂点でも結果が一致する。
		/// </summary>
		[BurstCompile]
		public struct SmoothJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<int> adjStart;
			[ReadOnly] public NativeArray<int> adjList;
			[ReadOnly] public NativeArray<byte> valid;
			[ReadOnly] public NativeArray<float3> input;
			[WriteOnly] public NativeArray<float3> output;
			public float strength;

			public void Execute(int index)
			{
				var value = input[index];
				if (valid[index] == 0)
				{
					output[index] = value;
					return;
				}

				var start = adjStart[index];
				var end = adjStart[index + 1];
				var sum = float3.zero;
				var count = 0;
				for (var i = start; i < end; i++)
				{
					var j = adjList[i];
					if (valid[j] == 0)
						continue;
					sum += input[j];
					count++;
				}
				if (count == 0)
				{
					output[index] = value;
					return;
				}
				output[index] = lerp(value, sum / count, strength);
			}
		}

		/// <summary>変位を重み・factor 付きで適用し、重み込みの変位を記録する</summary>
		[BurstCompile]
		public struct ApplyJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float3> delta;
			[ReadOnly] public NativeArray<float> weight;
			public float factor;
			public NativeArray<float3> vertices;
			[WriteOnly] public NativeArray<float3> displacement;

			public void Execute(int index)
			{
				var d = delta[index] * weight[index];
				displacement[index] = d;
				vertices[index] += d * factor;
			}
		}

		/// <summary>
		/// 平滑化で崩れた minGap をもう一度保証する(めり込み・近すぎのみ押し出す。平滑化なし)。
		/// </summary>
		[BurstCompile]
		public struct EnforceMinGapJob : IJobParallelFor
		{
			public MeshSurfaceData surface;
			[ReadOnly] public NativeArray<float> weight;
			public float factor;
			public float minGap;
			public float searchDistance;
			public NativeArray<float3> vertices;
			public NativeArray<float3> displacement;

			public void Execute(int index)
			{
				var w = weight[index];
				if (w <= 0f)
					return;

				var p = vertices[index];
				if (!surface.FindClosest(p, searchDistance, out var hit))
					return;

				var d = hit.SignedDistance;
				if (d >= minGap)
					return;

				var correction = OutwardDirection(p, hit) * (minGap - d) * w;
				vertices[index] = p + correction * factor;
				displacement[index] += correction;
			}
		}

		/// <summary>FixedDisplacement のシェイプフレーム用: 基本形状の変位をそのまま足す</summary>
		[BurstCompile]
		public struct AddDisplacementJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float3> displacement;
			public float factor;
			public NativeArray<float3> vertices;

			public void Execute(int index)
			{
				vertices[index] += displacement[index] * factor;
			}
		}

		// ---- ジョブ共通の数式 ----

		/// <summary>
		/// 二重球の領域重み(軸空間)。内半径の内側で 1、外半径で 0、間は smoothstep で減衰。
		/// </summary>
		public static float RegionWeight(float3 worldPoint, float4x4 meshToAxis, int wholeMesh,
			float innerRadius, float outerRadius)
		{
			if (wholeMesh != 0)
				return 1f;

			var dist = length(mul(meshToAxis, float4(worldPoint, 1f)).xyz);
			if (dist >= outerRadius)
				return 0f;
			if (dist <= innerRadius)
				return 1f;
			return smoothstep(0f, 1f, unlerp(outerRadius, innerRadius, dist));
		}

		/// <summary>探索距離の上限付近(75%〜100%)で 1 → 0 へ滑らかに減衰する係数</summary>
		public static float SearchFalloff(float distance, float searchDistance)
		{
			var fadeStart = searchDistance * 0.75f;
			if (searchDistance <= 0f || distance <= fadeStart)
				return 1f;
			return 1f - smoothstep(fadeStart, searchDistance, distance);
		}

		/// <summary>
		/// 最近接点から見た「体の外向き」の単位ベクトル。
		/// 頂点が表面から離れていれば (p − q) を符号で外向きに揃えたもの、
		/// 表面上なら擬似法線を使う。
		/// </summary>
		public static float3 OutwardDirection(float3 p, in MeshSurfaceHit hit)
		{
			if (hit.Distance > 1e-6f)
				return (p - hit.Point) * (hit.Sign / hit.Distance);
			return hit.Normal;
		}
	}
}
