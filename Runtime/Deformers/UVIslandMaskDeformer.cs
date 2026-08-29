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

		/// <summary>
		/// 選択した島の代表 UV(UVIslandAnalysis.Island.Seed)。
		/// 島 ID はメッシュ変更で不安定なため、UV 座標で保存し解析時に再解決する。
		/// </summary>
		[SerializeField, HideInInspector] private List<Vector2> islandSeeds = new List<Vector2>();

		public float Factor { get => factor; set => factor = Mathf.Clamp01(value); }
		public float Falloff { get => falloff; set => falloff = Mathf.Max(0f, value); }
		public bool Invert { get => invert; set => invert = value; }
		public List<Vector2> IslandSeeds => islandSeeds;

		public override DeformDataFlags DataFlags =>
			DeformDataFlags.Vertices | DeformDataFlags.OriginalVertices;

		// ---- PrepareBake キャッシュ(シリアライズ対象外) ----
		[System.NonSerialized] private Mesh _analyzedMesh;
		[System.NonSerialized] private int _analyzedVertexCount;
		[System.NonSerialized] private UVIslandAnalysis _analysis;
		[System.NonSerialized] private float[] _weights;
		[System.NonSerialized] private int _weightsHash;

		/// <summary>
		/// メッシュの UV 島解析を返す(メッシュが変わらない限りキャッシュされる)。
		/// エディタ UI もこの結果を共有する。
		/// </summary>
		public UVIslandAnalysis GetOrCreateAnalysis(Mesh source)
		{
			if (source == null)
			{
				_analyzedMesh = null;
				_analysis = null;
				_weights = null;
				return null;
			}

			if (_analysis == null || _analyzedMesh != source || _analyzedVertexCount != source.vertexCount)
			{
				_analysis = UVIslandAnalysis.Analyze(source);
				_analyzedMesh = source;
				_analyzedVertexCount = source.vertexCount;
				_weights = null;
			}
			return _analysis;
		}

		/// <summary>解析キャッシュを無効化する(エディタの再解析ボタン用)</summary>
		public void InvalidateAnalysis()
		{
			_analyzedMesh = null;
			_analysis = null;
			_weights = null;
		}

		public override void PrepareBake(Mesh source)
		{
			var analysis = GetOrCreateAnalysis(source);
			if (analysis == null)
				return;

			var hash = ComputeSelectionHash();
			if (_weights != null && _weights.Length == analysis.VertexCount && _weightsHash == hash)
				return;

			_weights = BuildWeights(analysis);
			_weightsHash = hash;
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
		/// 頂点ごとのマスク強度(invert 適用済み)を構築する。
		/// 選択島の頂点 = 1、falloff > 0 なら選択島の境界エッジからの
		/// UV 距離に応じて外側の頂点にも減衰した強度を与える。
		/// </summary>
		private float[] BuildWeights(UVIslandAnalysis analysis)
		{
			var weights = new float[analysis.VertexCount];

			var selected = new List<UVIslandAnalysis.Island>();
			foreach (var seed in islandSeeds)
			{
				var island = analysis.FindIslandAt(seed);
				if (island != null && !selected.Contains(island))
					selected.Add(island);
			}

			foreach (var island in selected)
			{
				foreach (var v in island.Vertices)
					weights[v] = 1f;
			}

			if (falloff > 0f && selected.Count > 0)
			{
				var borders = new List<Vector4>();
				foreach (var island in selected)
					borders.AddRange(island.BorderEdges);

				var uvs = analysis.Uvs;
				if (borders.Count > 0 && uvs.Length == weights.Length)
				{
					for (var i = 0; i < weights.Length; i++)
					{
						if (weights[i] >= 1f)
							continue;

						var minDistance = float.MaxValue;
						foreach (var edge in borders)
						{
							var d = UVIslandAnalysis.DistancePointSegment(uvs[i],
								new Vector2(edge.x, edge.y), new Vector2(edge.z, edge.w));
							if (d < minDistance)
								minDistance = d;
						}
						weights[i] = Mathf.Max(weights[i], 1f - Mathf.Clamp01(minDistance / falloff));
					}
				}
			}

			if (invert)
			{
				for (var i = 0; i < weights.Length; i++)
					weights[i] = 1f - weights[i];
			}

			return weights;
		}

		private int ComputeSelectionHash()
		{
			unchecked
			{
				var h = 17;
				h = h * 31 + islandSeeds.Count;
				foreach (var seed in islandSeeds)
				{
					h = h * 31 + seed.x.GetHashCode();
					h = h * 31 + seed.y.GetHashCode();
				}
				h = h * 31 + falloff.GetHashCode();
				h = h * 31 + invert.GetHashCode();
				return h;
			}
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
	}
}
