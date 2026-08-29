// 移植元: dev ブランチ ExDeform/UVIslandMask.cs + UVIslandSelector.cs(自作コード)。
// UV ポリゴン方式をやめ、UV 島選択 → 頂点重みバッファ生成方式に再設計
// (設計ドキュメント §4.3: v = lerp(v_snapshot, v_deformed, w))。
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 選択した UV 島への参照。島 ID はメッシュ変更で不安定なため、
	/// 代表 UV とサブメッシュ番号で保存し、解析時に再解決する。
	/// </summary>
	[System.Serializable]
	public struct IslandSeed
	{
		public Vector2 uv;

		/// <summary>-1 = 全サブメッシュから検索(サブメッシュ情報の無い旧データ)</summary>
		public int subMesh;

		public IslandSeed(Vector2 uv, int subMesh)
		{
			this.uv = uv;
			this.subMesh = subMesh;
		}
	}

	/// <summary>
	/// 選択した UV 島の変形を打ち消すマスク。
	/// スタック内でこのマスクより前にあるデフォーマの変形結果を、
	/// 島に属する頂点についてスタック適用前のスナップショットへ戻す。
	/// invert で「選択した島だけに変形を残す」動作になる。
	/// </summary>
	[DeformerMeta(Name = "UV Island Mask", Category = DeformerCategory.Mask,
	              Description = "選択した UV 島の変形を打ち消す(反転で島のみに変形を残す)")]
	[AddComponentMenu("NDMF Deform/Deformers/UV Island Mask")]
	public class UVIslandMaskDeformer : DeformerBase
	{
		[SerializeField, Range(0f, 1f)] private float factor = 1f;
		[SerializeField, Min(0f)] private float falloff = 0f;
		[SerializeField] private bool invert;
		[SerializeField, HideInInspector] private List<IslandSeed> selectedIslands = new List<IslandSeed>();

		// 旧形式(M4 初版): サブメッシュ情報なしの代表 UV。OnValidate で selectedIslands へ移行する
		[SerializeField, HideInInspector] private List<Vector2> islandSeeds = new List<Vector2>();

		public float Factor { get => factor; set => factor = Mathf.Clamp01(value); }
		public float Falloff { get => falloff; set => falloff = Mathf.Max(0f, value); }
		public bool Invert { get => invert; set => invert = value; }
		public List<IslandSeed> SelectedIslands => selectedIslands;

		public override DeformDataFlags DataFlags =>
			DeformDataFlags.Vertices | DeformDataFlags.OriginalVertices;

		private void OnValidate()
		{
			if (islandSeeds != null && islandSeeds.Count > 0)
			{
				foreach (var seed in islandSeeds)
					selectedIslands.Add(new IslandSeed(seed, -1));
				islandSeeds.Clear();
			}
		}

		// ---- 解析・重みキャッシュ(シリアライズ対象外) ----
		[System.NonSerialized] private Mesh _analyzedMesh;
		[System.NonSerialized] private int _analyzedVertexCount;
		[System.NonSerialized] private UVIslandAnalysis _analysis;

		// 選択島からの UV 距離(0 = 島内)。選択が変わった時のみ再計算するため、
		// falloff のドラッグ中は頂点数分の軽いループしか走らない
		[System.NonSerialized] private float[] _distances;
		[System.NonSerialized] private int _distancesHash;
		[System.NonSerialized] private bool _distancesExact;
		[System.NonSerialized] private float[] _weights;

		/// <summary>
		/// メッシュの UV 島解析を返す(メッシュが変わらない限りキャッシュされる)。
		/// エディタ UI もこの結果を共有する。
		/// </summary>
		public UVIslandAnalysis GetOrCreateAnalysis(Mesh source)
		{
			if (source == null)
			{
				InvalidateAnalysis();
				return null;
			}

			if (_analysis == null || _analyzedMesh != source || _analyzedVertexCount != source.vertexCount)
			{
				_analysis = UVIslandAnalysis.Analyze(source);
				_analyzedMesh = source;
				_analyzedVertexCount = source.vertexCount;
				_distances = null;
				_weights = null;
			}
			return _analysis;
		}

		/// <summary>解析キャッシュを無効化する(エディタの再解析ボタン用)</summary>
		public void InvalidateAnalysis()
		{
			_analyzedMesh = null;
			_analysis = null;
			_distances = null;
			_weights = null;
		}

		/// <summary>
		/// 親の DeformStack が付いたレンダラーからソースメッシュを解決する。
		/// renderer は MeshFilter のみの構成では null のことがある。
		/// </summary>
		public bool TryGetSourceMesh(out Mesh mesh, out Transform meshTransform, out Renderer renderer)
		{
			mesh = null;
			meshTransform = null;
			renderer = null;

			var stack = GetComponentInParent<DeformStack>();
			if (stack == null)
				return false;

			meshTransform = stack.transform;
			if (stack.TryGetComponent<SkinnedMeshRenderer>(out var smr))
			{
				renderer = smr;
				mesh = smr.sharedMesh;
			}
			else if (stack.TryGetComponent<MeshFilter>(out var mf))
			{
				stack.TryGetComponent<Renderer>(out renderer);
				mesh = mf.sharedMesh;
			}
			return mesh != null;
		}

		/// <summary>保存されたシードを現在の解析結果の島へ解決する(重複なし)</summary>
		public List<UVIslandAnalysis.Island> ResolveSelectedIslands(UVIslandAnalysis analysis)
		{
			var list = new List<UVIslandAnalysis.Island>();
			if (analysis == null)
				return list;
			foreach (var seed in selectedIslands)
			{
				var island = analysis.FindIslandAt(seed.uv, seed.subMesh);
				if (island != null && !list.Contains(island))
					list.Add(island);
			}
			return list;
		}

		/// <summary>島選択のみのハッシュ(falloff / invert は含まない)</summary>
		public int SelectionHash()
		{
			unchecked
			{
				var h = 17;
				h = h * 31 + selectedIslands.Count;
				foreach (var seed in selectedIslands)
				{
					h = h * 31 + seed.uv.x.GetHashCode();
					h = h * 31 + seed.uv.y.GetHashCode();
					h = h * 31 + seed.subMesh;
				}
				return h;
			}
		}

		public override void PrepareBake(Mesh source)
		{
			var analysis = GetOrCreateAnalysis(source);
			if (analysis == null || analysis.VertexCount == 0)
			{
				_weights = null;
				return;
			}

			var n = analysis.VertexCount;
			var selectionHash = SelectionHash();
			var needExact = falloff > 0f;
			if (_distances == null || _distances.Length != n ||
			    _distancesHash != selectionHash || (needExact && !_distancesExact))
			{
				RebuildDistances(analysis, selectionHash, needExact);
			}

			// 距離 → 重みは頂点数分の軽いループのみ(falloff / invert 変更時はここだけ走る)
			if (_weights == null || _weights.Length != n)
				_weights = new float[n];
			for (var i = 0; i < n; i++)
			{
				var d = _distances[i];
				var w = d <= 0f ? 1f : (falloff > 0f ? 1f - Mathf.Clamp01(d / falloff) : 0f);
				_weights[i] = invert ? 1f - w : w;
			}
		}

		public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			if (factor <= 0f)
				return dependency;
			if (_weights == null || _weights.Length != buffers.Length)
				return dependency;
			if (!buffers.OriginalVertices.IsCreated)
				return dependency;

			var mask = new NativeArray<float>(_weights, Allocator.TempJob);
			return new MaskBlendJob
			{
				factor = factor,
				mask = mask,
				original = buffers.OriginalVertices,
				vertices = buffers.Vertices,
			}.Schedule(buffers.Length, 128, dependency);
		}

		/// <summary>
		/// 選択島からの UV 距離を再構築する。島内の頂点は 0、
		/// needExact の場合のみ外側の頂点へ境界エッジ距離を Burst ジョブで計算する
		/// (falloff = 0 のあいだは内外フラグだけで済ませる)。
		/// </summary>
		private void RebuildDistances(UVIslandAnalysis analysis, int selectionHash, bool needExact)
		{
			var n = analysis.VertexCount;
			if (_distances == null || _distances.Length != n)
				_distances = new float[n];
			for (var i = 0; i < n; i++)
				_distances[i] = float.MaxValue;

			var selected = ResolveSelectedIslands(analysis);
			foreach (var island in selected)
			{
				foreach (var v in island.Vertices)
					_distances[v] = 0f;
			}

			_distancesExact = false;
			if (needExact)
			{
				var borderList = new List<Vector4>();
				if (analysis.Uvs.Length == n)
				{
					foreach (var island in selected)
						borderList.AddRange(island.BorderEdges);
				}

				if (borderList.Count > 0)
				{
					var uvs = new NativeArray<float2>(n, Allocator.TempJob,
						NativeArrayOptions.UninitializedMemory);
					for (var i = 0; i < n; i++)
						uvs[i] = new float2(analysis.Uvs[i].x, analysis.Uvs[i].y);

					var borders = new NativeArray<float4>(borderList.Count, Allocator.TempJob,
						NativeArrayOptions.UninitializedMemory);
					for (var i = 0; i < borderList.Count; i++)
					{
						var b = borderList[i];
						borders[i] = new float4(b.x, b.y, b.z, b.w);
					}

					var distances = new NativeArray<float>(_distances, Allocator.TempJob);
					try
					{
						new BorderDistanceJob
						{
							uvs = uvs,
							borders = borders,
							distances = distances,
						}.Schedule(n, 64, default).Complete();
						distances.CopyTo(_distances);
					}
					finally
					{
						uvs.Dispose();
						borders.Dispose();
						distances.Dispose();
					}
				}
				// 境界が無い(選択なし等)場合も内外フラグのままで正確なので再構築不要
				_distancesExact = true;
			}

			_distancesHash = selectionHash;
		}

		[BurstCompile]
		public struct MaskBlendJob : IJobParallelFor
		{
			public float factor;
			[ReadOnly, DeallocateOnJobCompletion] public NativeArray<float> mask;
			[ReadOnly] public NativeArray<float3> original;
			public NativeArray<float3> vertices;

			public void Execute(int index)
			{
				vertices[index] = lerp(vertices[index], original[index], saturate(mask[index] * factor));
			}
		}

		/// <summary>島外の頂点について、選択島の境界エッジまでの最短 UV 距離を求める</summary>
		[BurstCompile]
		public struct BorderDistanceJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float2> uvs;
			[ReadOnly] public NativeArray<float4> borders;
			public NativeArray<float> distances;

			public void Execute(int index)
			{
				if (distances[index] <= 0f)
					return;

				var uv = uvs[index];
				var best = float.MaxValue;
				for (var e = 0; e < borders.Length; e++)
				{
					var b = borders[e];
					best = min(best, SegmentDistance(uv, b.xy, b.zw));
				}
				distances[index] = best;
			}

			private static float SegmentDistance(float2 p, float2 a, float2 b)
			{
				var line = b - a;
				var len2 = dot(line, line);
				if (len2 <= 0f)
					return distance(p, a);
				var t = saturate(dot(p - a, line) / len2);
				return distance(p, a + t * line);
			}
		}
	}
}
