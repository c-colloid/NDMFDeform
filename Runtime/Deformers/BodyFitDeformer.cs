using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;
using float3 = Unity.Mathematics.float3;
using float4 = Unity.Mathematics.float4;
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

		public enum FitMode
		{
			/// <summary>
			/// パーツ円柱: ヒューマノイド骨格のパーツごとに、ボーン軸からの放射方向へ移動する。
			/// 装飾は下地との相対オフセットを保ち、腕の装飾が胴へ吸われない(推奨。ヒューマノイド必須)
			/// </summary>
			PartCylinder = 0,

			/// <summary>最近接表面: 体表面の最近接点へ向けて移動する(骨格が無い場合のフォールバック)</summary>
			NearestSurface = 1,
		}

		public enum PartGrouping
		{
			None = 0,

			/// <summary>3D の連結成分(位置で溶接)ごとに所属パーツを揃える</summary>
			ConnectedComponents = 1,

			/// <summary>UV 島ごとに所属パーツを揃える</summary>
			UVIslands = 2,
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

		[SerializeField, Tooltip("フィット方式。PartCylinder はヒューマノイド骨格のパーツ軸から放射状に動かす(推奨)。骨格が無ければ NearestSurface に自動で切り替わる")]
		private FitMode fitMode = FitMode.PartCylinder;

		[SerializeField, Tooltip("小さな装飾(紐・リボン)の所属パーツを揃える単位。連結成分 / UV 島ごとに多数決で 1 パーツにする")]
		private PartGrouping decorationGrouping = PartGrouping.ConnectedComponents;

		[SerializeField, Min(0f), Tooltip("所属パーツを揃える装飾の大きさ上限(バウンズ対角、m)。これより大きい成分(服本体)は揃えない")]
		private float decorationMaxSize = 0.25f;

		[SerializeField, Tooltip("NearestSurface でも、自分のパーツの体表面だけを探す(腕の装飾が胴へ吸われるのを防ぐ)")]
		private bool partFilter = true;

		[SerializeField, Min(0f), Tooltip("衣装アーマチュアの関節をアバターの関節へ対応付ける許容距離(m)")]
		private float jointTolerance = 0.03f;

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
		public FitMode Mode { get => fitMode; set => fitMode = value; }
		public PartGrouping DecorationGrouping { get => decorationGrouping; set => decorationGrouping = value; }
		public float DecorationMaxSize { get => decorationMaxSize; set => decorationMaxSize = Mathf.Max(0f, value); }
		public bool PartFilter { get => partFilter; set => partFilter = value; }
		public float JointTolerance { get => jointTolerance; set => jointTolerance = Mathf.Max(0f, value); }

		/// <summary>骨格の差し替え(テスト用。null なら体 / 衣装の親の Animator から作る)</summary>
		[System.NonSerialized] public HumanoidSkeleton SkeletonOverride;

		/// <summary>直近の PrepareBake でパーツ情報が使えたか</summary>
		public bool PartsAvailable => _partsReady;

		/// <summary>直近の PrepareBake で実際に使う方式</summary>
		public FitMode EffectiveMode => _effectiveMode;

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

		// パーツ情報(骨格が使える場合)。_costumeParts は骨格が無くても頂点数分確保する(全 None)
		[System.NonSerialized] private BodyPartProfiles _profiles;
		[System.NonSerialized] private bool _partsReady;
		[System.NonSerialized] private FitMode _effectiveMode = FitMode.NearestSurface;
		[System.NonSerialized] private NativeArray<PartWeights> _costumeParts;
		[System.NonSerialized] private int _costumePartsKey;
		[System.NonSerialized] private MeshAdjacency _adjacencyManaged;
		[System.NonSerialized] private HumanoidSkeleton _cachedSkeleton;
		[System.NonSerialized] private Animator _cachedAnimator;
		[System.NonSerialized] private Avatar _cachedAvatar;

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
			if (_costumeParts.IsCreated) _costumeParts.Dispose();
			_adjacencyMesh = null;
			_adjacencyManaged = null;
			_adjacencyVertexCount = 0;
			_cachedSkeleton = null;
			_cachedAnimator = null;
			_cachedAvatar = null;
			_costumePartsKey = 0;
			_surfaceReady = false;
			_partsReady = false;
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

			// パーツ情報: ヒューマノイド骨格が見つかれば、体の表面にパーツマスクと半径プロファイルを付ける
			var skeleton = ResolveSkeleton();
			var request = skeleton != null
				? new PartRequest { Skeleton = skeleton, JointTolerance = jointTolerance }
				: null;
			_surfaceReady = ReferenceSurfaceCache.TryGet(body, useBodyBlendShapes, flipBodyNormals, request,
				out _surface, out _profiles);
			if (!_surfaceReady)
				return;
			_partsReady = request != null && _profiles.IsCreated;
			_effectiveMode = fitMode == FitMode.PartCylinder && _partsReady
				? FitMode.PartCylinder
				: FitMode.NearestSurface;

			EnsureAdjacency(source, _partsReady && decorationGrouping == PartGrouping.ConnectedComponents);
			EnsureCostumeParts(source, _partsReady ? skeleton : null);

			if (!_baseDisplacement.IsCreated || _baseDisplacement.Length != _vertexCount)
			{
				if (_baseDisplacement.IsCreated)
					_baseDisplacement.Dispose();
				_baseDisplacement = new NativeArray<float3>(_vertexCount, Allocator.Persistent,
					NativeArrayOptions.UninitializedMemory);
			}
		}

		/// <summary>
		/// パーツ軸に使うヒューマノイド Animator: 体の親 → 衣装(親スタック)の親 の順に探す。
		/// 見つからなければ null(インスペクタの状態表示も同じ判定を使う)。
		/// </summary>
		public Animator FindHumanoidAnimator()
		{
			var animator = body != null ? body.GetComponentInParent<Animator>() : null;
			if (animator == null || !animator.isHuman)
			{
				var own = GetOwnRenderer();
				animator = own != null ? own.GetComponentInParent<Animator>() : null;
			}
			return animator != null && animator.isHuman ? animator : null;
		}

		/// <summary>
		/// 骨格の解決: 差し替え → FindHumanoidAnimator。
		/// 同じ Animator(と Avatar)の間は骨格を再利用し、現在のボーン位置だけ読み直す
		/// (PrepareBake はプレビューのホットパスから毎回呼ばれるため、全ボーンの再列挙は避ける)。
		/// </summary>
		private HumanoidSkeleton ResolveSkeleton()
		{
			if (SkeletonOverride != null)
				return SkeletonOverride;
			var animator = FindHumanoidAnimator();
			if (animator == null)
			{
				_cachedSkeleton = null;
				_cachedAnimator = null;
				_cachedAvatar = null;
				return null;
			}
			if (_cachedSkeleton != null && _cachedAnimator == animator && _cachedAvatar == animator.avatar)
			{
				_cachedSkeleton.Refresh();
				return _cachedSkeleton;
			}
			_cachedSkeleton = HumanoidSkeleton.FromAnimator(animator);
			_cachedAnimator = animator;
			_cachedAvatar = animator.avatar;
			return _cachedSkeleton;
		}

		private void EnsureAdjacency(Mesh source, bool force)
		{
			if (smoothIterations <= 0 && !force)
				return;
			if (_adjStart.IsCreated && _adjacencyManaged != null && _adjacencyMesh == source &&
			    _adjacencyVertexCount == source.vertexCount)
				return;

			if (_adjStart.IsCreated) _adjStart.Dispose();
			if (_adjList.IsCreated) _adjList.Dispose();

			var adjacency = MeshAdjacency.Build(source.vertices, ReferenceSurfaceUtility.CollectTriangles(source));
			_adjStart = new NativeArray<int>(adjacency.Start, Allocator.Persistent);
			_adjList = new NativeArray<int>(adjacency.Neighbors, Allocator.Persistent);
			_adjacencyManaged = adjacency;
			_adjacencyMesh = source;
			_adjacencyVertexCount = source.vertexCount;
		}

		/// <summary>
		/// 衣装頂点のパーツ所属を用意する。骨格が無ければ全頂点 None(マスク 0 = 絞り込みなし)。
		/// ソースメッシュ・骨格・設定が変わらない限り再利用する。
		/// </summary>
		private void EnsureCostumeParts(Mesh source, HumanoidSkeleton skeleton)
		{
			var n = source.vertexCount;
			int key;
			unchecked
			{
				key = 17;
				key = key * 31 + source.GetInstanceID();
				key = key * 31 + n;
				key = key * 31 + (skeleton != null ? skeleton.StateHash : 0);
				key = key * 31 + (int)decorationGrouping;
				key = key * 31 + decorationMaxSize.GetHashCode();
				key = key * 31 + jointTolerance.GetHashCode();
				var own = GetOwnRenderer() as SkinnedMeshRenderer;
				key = key * 31 + (own != null ? own.GetInstanceID() : 0);
			}
			if (_costumeParts.IsCreated && _costumeParts.Length == n && _costumePartsKey == key)
				return;

			if (_costumeParts.IsCreated)
				_costumeParts.Dispose();
			var weights = skeleton != null ? BuildCostumePartWeights(source, skeleton) : new PartWeights[n];
			_costumeParts = new NativeArray<PartWeights>(weights, Allocator.Persistent);
			_costumePartsKey = key;
		}

		private PartWeights[] BuildCostumePartWeights(Mesh source, HumanoidSkeleton skeleton)
		{
			var n = source.vertexCount;
			var own = GetOwnRenderer();
			var triangles = ReferenceSurfaceUtility.CollectTriangles(source);
			PartWeights[] weights;
			if (own is SkinnedMeshRenderer smr && source.GetBonesPerVertex().Length == n)
			{
				var boneParts = skeleton.MapBones(smr.bones, jointTolerance);
				weights = PartAssignment.FromBoneWeights(source, boneParts);
			}
			else
			{
				// ウェイトの無い衣装: 連結成分の重心に最も近い軸区間へ
				weights = new PartWeights[n];
				var world = source.vertices;
				if (own != null)
					ReferenceSurfaceUtility.SkinToWorld(own.transform, source, world);
				var adjacency = _adjacencyManaged ?? MeshAdjacency.Build(source.vertices, triangles);
				var groups = PartAssignment.ConnectedComponents(adjacency, triangles, out var groupCount);
				PartAssignment.AssignGroupsBySegment(weights, world, groups, groupCount, skeleton);
			}

			if (decorationGrouping != PartGrouping.None)
			{
				int[] groups;
				int groupCount;
				if (decorationGrouping == PartGrouping.UVIslands)
				{
					groups = PartAssignment.UVIslandGroups(source, out groupCount);
				}
				else
				{
					var adjacency = _adjacencyManaged ?? MeshAdjacency.Build(source.vertices, triangles);
					groups = PartAssignment.ConnectedComponents(adjacency, triangles, out groupCount);
				}
				PartAssignment.ConsolidateGroups(weights, source.vertices, groups, groupCount, decorationMaxSize);
			}
			return weights;
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

			if (!_costumeParts.IsCreated || _costumeParts.Length != n)
				return dependency;
			if (_effectiveMode == FitMode.PartCylinder)
				return SchedulePartCylinder(in buffers, in space, dependency);

			var delta = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var weight = new NativeArray<float>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var valid = new NativeArray<byte>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var usePartFilter = partFilter && _partsReady ? 1 : 0;

			var handle = new QueryJob
			{
				surface = _surface,
				vertices = buffers.Vertices,
				parts = _costumeParts,
				usePartFilter = usePartFilter,
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
					parts = _costumeParts,
					usePartFilter = usePartFilter,
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

		/// <summary>
		/// パーツ円柱モードのジョブチェーン:
		/// CylinderCoordJob(頂点をパーツ軸の円柱座標へ)→ RadialFieldJob(格子ごとの最内層半径から
		/// 放射変位場 Δr(h, θ) を作り、補間・平滑化)→ RadialApplyJob(各頂点の所属パーツで Δr をサンプル)
		/// → ApplyJob → EnforceMinGapJob(パーツ制限付きの最近接点で minGap を保証)
		/// </summary>
		private JobHandle SchedulePartCylinder(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			var n = buffers.Length;
			var cellCount = HumanoidSkeleton.PartCount * BodyPartProfiles.HCount * BodyPartProfiles.ThetaCount;
			var coords = new NativeArray<float4>(n * 4, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var radialDirs = new NativeArray<float3>(n * 4, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var binPart = new NativeArray<int>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var weight = new NativeArray<float>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var valid = new NativeArray<byte>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var delta = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var grid = new NativeArray<float>(cellCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var scratch = new NativeArray<float>(cellCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

			var handle = new CylinderCoordJob
			{
				vertices = buffers.Vertices,
				parts = _costumeParts,
				profiles = _profiles,
				meshToAxis = space.MeshToAxis,
				wholeMesh = region == FitRegion.WholeMesh ? 1 : 0,
				innerRadius = innerRadius,
				outerRadius = outerRadius,
				coords = coords,
				radialDirs = radialDirs,
				binPart = binPart,
				weight = weight,
			}.Schedule(n, 64, dependency);

			handle = new RadialFieldJob
			{
				coords = coords,
				binPart = binPart,
				weight = weight,
				profiles = _profiles,
				minGap = minGap,
				maxGap = pullIn ? Mathf.Max(maxGap, minGap) : float.MaxValue,
				smoothIterations = smoothIterations,
				smoothStrength = smoothStrength,
				grid = grid,
				scratch = scratch,
			}.Schedule(handle);

			handle = new RadialApplyJob
			{
				coords = coords,
				radialDirs = radialDirs,
				parts = _costumeParts,
				grid = grid,
				delta = delta,
				valid = valid,
			}.Schedule(n, 64, handle);

			handle = new ApplyJob
			{
				delta = delta,
				weight = weight,
				factor = factor,
				vertices = buffers.Vertices,
				displacement = _baseDisplacement,
			}.Schedule(n, 128, handle);

			if (enforceMinGap)
			{
				handle = new EnforceMinGapJob
				{
					surface = _surface,
					parts = _costumeParts,
					usePartFilter = 1,
					weight = weight,
					factor = factor,
					minGap = minGap,
					searchDistance = searchDistance,
					vertices = buffers.Vertices,
					displacement = _baseDisplacement,
				}.Schedule(n, 32, handle);
			}

			handle = coords.Dispose(handle);
			handle = radialDirs.Dispose(handle);
			handle = binPart.Dispose(handle);
			handle = weight.Dispose(handle);
			handle = valid.Dispose(handle);
			handle = delta.Dispose(handle);
			handle = grid.Dispose(handle);
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
			[ReadOnly] public NativeArray<PartWeights> parts;
			public int usePartFilter;
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

				var mask = usePartFilter != 0 ? PartMaskOf(parts[index]) : 0;
				if (!surface.FindClosest(p, searchDistance, mask, out var hit))
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
			[ReadOnly] public NativeArray<PartWeights> parts;
			public int usePartFilter;
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
				var mask = usePartFilter != 0 ? PartMaskOf(parts[index]) : 0;
				if (!surface.FindClosest(p, searchDistance, mask, out var hit))
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

		/// <summary>頂点をパーツ軸の円柱座標へ分解する(所属スロットごと)。領域重みと最内層判定用の支配パーツも出す</summary>
		[BurstCompile]
		public struct CylinderCoordJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float3> vertices;
			[ReadOnly] public NativeArray<PartWeights> parts;
			public BodyPartProfiles profiles;
			public float4x4 meshToAxis;
			public int wholeMesh;
			public float innerRadius;
			public float outerRadius;

			/// <summary>
			/// 頂点 × 4 スロット: (h, θ, r, スロット重み)。使わないスロットは重み 0。
			/// 要素 index*4 〜 index*4+3 へ書くため、並列ジョブの「自分の index のみ」制限を外す
			/// (各頂点が書く範囲は重ならない)。
			/// </summary>
			[WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float4> coords;

			[WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> radialDirs;

			/// <summary>最内層の集計に使う支配パーツ(重み 0.5 以上・軸が使える場合のみ。それ以外は 0)</summary>
			[WriteOnly] public NativeArray<int> binPart;

			[WriteOnly] public NativeArray<float> weight;

			public void Execute(int index)
			{
				var p = vertices[index];
				weight[index] = RegionWeight(p, meshToAxis, wholeMesh, innerRadius, outerRadius);
				var pw = parts[index];
				var dominant = 0;
				for (var s = 0; s < 4; s++)
				{
					var part = pw.Parts[s];
					var sw = pw.Weights[s];
					if (part == 0 || sw <= 0f || !profiles.IsUsable(part))
					{
						coords[index * 4 + s] = float4.zero;
						radialDirs[index * 4 + s] = float3.zero;
						continue;
					}
					var axis = profiles.Axes[part];
					axis.Decompose(p, out var h, out var theta, out var r, out var dir);
					coords[index * 4 + s] = new float4(h, theta, r, sw);
					radialDirs[index * 4 + s] = dir;
					if (s == 0 && sw >= 0.5f)
						dominant = part;
				}
				binPart[index] = dominant;
			}
		}

		/// <summary>
		/// パーツごとの放射変位場 Δr(h, θ) を作る(単一スレッド):
		/// 1. 格子ごとに衣装の最内層半径 r_min(支配パーツの頂点の最小 r)
		/// 2. Δr = clamp(r_min, R + minGap, R + maxGap) − r_min(R は体の半径プロファイル)
		/// 3. 値の無い格子を近傍平均で 2 周だけ埋め(双線形補間の縁取り)、残りは 0
		/// 4. 3×3 平均への補間で平滑化(θ 方向は周期)
		/// </summary>
		[BurstCompile]
		public struct RadialFieldJob : IJob
		{
			[ReadOnly] public NativeArray<float4> coords;
			[ReadOnly] public NativeArray<int> binPart;
			[ReadOnly] public NativeArray<float> weight;
			public BodyPartProfiles profiles;
			public float minGap;
			public float maxGap;
			public int smoothIterations;
			public float smoothStrength;
			public NativeArray<float> grid;
			public NativeArray<float> scratch;

			public void Execute()
			{
				const int H = BodyPartProfiles.HCount;
				const int T = BodyPartProfiles.ThetaCount;
				const int P = HumanoidSkeleton.PartCount;
				var cellCount = P * H * T;

				for (var c = 0; c < cellCount; c++)
					grid[c] = float.PositiveInfinity;

				// 1. 最内層半径
				var n = binPart.Length;
				for (var i = 0; i < n; i++)
				{
					var part = binPart[i];
					if (part == 0 || weight[i] <= 0f)
						continue;
					var c = coords[i * 4];
					var hi = (int)floor((c.x - BodyPartProfiles.HStart) / (BodyPartProfiles.HEnd - BodyPartProfiles.HStart) * H);
					if (hi < 0 || hi >= H)
						continue;
					var ti = (int)floor((c.y + PI) / (2f * PI) * T);
					ti = ((ti % T) + T) % T;
					var idx = BodyPartProfiles.CellIndex(part, hi, ti);
					grid[idx] = min(grid[idx], c.z);
				}

				// 2. 放射変位
				for (var part = 1; part < P; part++)
				{
					var usable = profiles.Usable[part] != 0;
					for (var hi = 0; hi < H; hi++)
					for (var ti = 0; ti < T; ti++)
					{
						var idx = BodyPartProfiles.CellIndex(part, hi, ti);
						var rMin = grid[idx];
						var radius = profiles.Radius[idx];
						if (!usable || isinf(rMin) || isnan(radius))
						{
							grid[idx] = float.NaN;
							continue;
						}
						var target = clamp(rMin, radius + minGap, radius + maxGap);
						grid[idx] = target - rMin;
					}
				}

				// 3. 縁取り
				for (var pass = 0; pass < 2; pass++)
				{
					for (var c = 0; c < cellCount; c++)
						scratch[c] = grid[c];
					for (var part = 1; part < P; part++)
					for (var hi = 0; hi < H; hi++)
					for (var ti = 0; ti < T; ti++)
					{
						var idx = BodyPartProfiles.CellIndex(part, hi, ti);
						if (!isnan(scratch[idx]))
							continue;
						var sum = 0f;
						var count = 0;
						if (hi > 0) Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi - 1, ti)], ref sum, ref count);
						if (hi < H - 1) Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi + 1, ti)], ref sum, ref count);
						Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi, (ti + T - 1) % T)], ref sum, ref count);
						Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi, (ti + 1) % T)], ref sum, ref count);
						if (count > 0)
							grid[idx] = sum / count;
					}
				}
				for (var c = 0; c < cellCount; c++)
				{
					if (isnan(grid[c]))
						grid[c] = 0f;
				}

				// 4. 平滑化
				for (var it = 0; it < smoothIterations; it++)
				{
					for (var c = 0; c < cellCount; c++)
						scratch[c] = grid[c];
					for (var part = 1; part < P; part++)
					for (var hi = 0; hi < H; hi++)
					for (var ti = 0; ti < T; ti++)
					{
						var idx = BodyPartProfiles.CellIndex(part, hi, ti);
						var sum = 0f;
						var count = 0;
						if (hi > 0) Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi - 1, ti)], ref sum, ref count);
						if (hi < H - 1) Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi + 1, ti)], ref sum, ref count);
						Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi, (ti + T - 1) % T)], ref sum, ref count);
						Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi, (ti + 1) % T)], ref sum, ref count);
						if (count > 0)
							grid[idx] = lerp(scratch[idx], sum / count, smoothStrength);
					}
				}
			}

			private static void Accumulate(float value, ref float sum, ref int count)
			{
				if (isnan(value))
					return;
				sum += value;
				count++;
			}
		}

		/// <summary>各頂点の所属スロットごとに Δr をサンプルし、放射方向の変位に合成する</summary>
		[BurstCompile]
		public struct RadialApplyJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float4> coords;
			[ReadOnly] public NativeArray<float3> radialDirs;
			[ReadOnly] public NativeArray<PartWeights> parts;
			[ReadOnly] public NativeArray<float> grid;
			[WriteOnly] public NativeArray<float3> delta;
			[WriteOnly] public NativeArray<byte> valid;

			public void Execute(int index)
			{
				var sum = float3.zero;
				byte any = 0;
				var pw = parts[index];
				for (var s = 0; s < 4; s++)
				{
					var c = coords[index * 4 + s];
					if (c.w <= 0f)
						continue;
					var part = pw.Parts[s];
					var d = BodyPartProfiles.SampleGrid(in grid, part, c.x, c.y);
					if (isnan(d))
						continue;
					sum += radialDirs[index * 4 + s] * (d * c.w);
					any = 1;
				}
				delta[index] = sum;
				valid[index] = any;
			}
		}

		// ---- ジョブ共通の数式 ----

		/// <summary>頂点の所属パーツから検索マスクを作る(重み 0.25 以上 + 支配パーツ。所属なしは 0 = 絞らない)</summary>
		public static int PartMaskOf(in PartWeights pw)
		{
			var mask = pw.Mask(0.25f);
			if (pw.Parts.x != 0)
				mask |= 1 << pw.Parts.x;
			return mask;
		}

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
