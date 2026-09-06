using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// メッシュの頂点隣接情報(CSR 形式)。
	/// 三角形の辺で結ばれた頂点同士を隣接とみなす。
	/// UV シームや硬い法線で分割された重複頂点は位置で溶接し、
	/// 重複頂点は代表頂点の隣接リストを共有する
	/// (隣接は代表頂点のインデックスで表す)ため、シームをまたいでも
	/// 隣接ベースの平滑化が途切れない。
	/// PrepareBake で構築し、Schedule でジョブへ渡す用途を想定する。
	/// </summary>
	public sealed class MeshAdjacency
	{
		/// <summary>位置溶接の量子化単位(この距離未満の頂点は同一位置とみなす)</summary>
		public const float WeldEpsilon = 1e-5f;

		/// <summary>拡散重みの計算で辺長をこの値以上に丸める(退化した辺で重みが発散しないように)</summary>
		public const float MinEdgeLength = 1e-4f;

		public int VertexCount { get; private set; }

		/// <summary>頂点 i の隣接は Neighbors[Offsets[i] .. Offsets[i + 1])。長さ VertexCount + 1</summary>
		public int[] Offsets { get; private set; }

		/// <summary>隣接頂点(代表頂点)のインデックス列</summary>
		public int[] Neighbors { get; private set; }

		/// <summary>
		/// Neighbors と同じ並びの拡散重み。辺長の逆数を頂点ごとに正規化(和 = 1)したもので、
		/// 短い辺ほど強く結合するため、頂点密度が不均一なメッシュでも拡散が幾何学的な距離に沿う
		/// (一様重みだと細長い三角形の短辺の両端に大きな値差が残る)。
		/// </summary>
		public float[] NeighborWeights { get; private set; }

		/// <summary>頂点 i と同じ位置の頂点群を代表するインデックス(自身のこともある)</summary>
		public int[] Representative { get; private set; }

		public int NeighborCount(int vertex) => Offsets[vertex + 1] - Offsets[vertex];

		public static MeshAdjacency Build(Mesh mesh)
		{
			var result = new MeshAdjacency();
			if (mesh == null)
			{
				result.SetEmpty(0);
				return result;
			}

			var vertices = mesh.vertices;
			var n = vertices.Length;
			if (n == 0)
			{
				result.SetEmpty(0);
				return result;
			}

			// 位置で溶接: 量子化した位置 → 最初に現れた頂点(代表)
			var representative = new int[n];
			var byPosition = new Dictionary<int3, int>(n);
			var inv = 1f / WeldEpsilon;
			for (var i = 0; i < n; i++)
			{
				var v = vertices[i];
				var key = (int3)math.round(new float3(v.x, v.y, v.z) * inv);
				if (byPosition.TryGetValue(key, out var rep))
				{
					representative[i] = rep;
				}
				else
				{
					byPosition.Add(key, i);
					representative[i] = i;
				}
			}

			// 代表頂点間の辺を収集(重複辺は除外)
			var lists = new List<int>[n];
			var seenEdges = new HashSet<long>();
			for (var s = 0; s < mesh.subMeshCount; s++)
			{
				if (mesh.GetTopology(s) != MeshTopology.Triangles)
					continue;
				var triangles = mesh.GetTriangles(s);
				for (var t = 0; t + 2 < triangles.Length; t += 3)
				{
					var a = representative[triangles[t]];
					var b = representative[triangles[t + 1]];
					var c = representative[triangles[t + 2]];
					AddEdge(lists, seenEdges, a, b);
					AddEdge(lists, seenEdges, b, c);
					AddEdge(lists, seenEdges, c, a);
				}
			}

			// CSR 化。重複頂点は代表頂点のリストをそのまま共有する
			var offsets = new int[n + 1];
			var total = 0;
			for (var i = 0; i < n; i++)
			{
				offsets[i] = total;
				var list = lists[representative[i]];
				total += list?.Count ?? 0;
			}
			offsets[n] = total;

			var neighbors = new int[total];
			var weights = new float[total];
			for (var i = 0; i < n; i++)
			{
				var list = lists[representative[i]];
				if (list == null)
					continue;
				list.CopyTo(neighbors, offsets[i]);

				var begin = offsets[i];
				var end = offsets[i + 1];
				var sum = 0f;
				var vi = vertices[i];
				for (var k = begin; k < end; k++)
				{
					var w = 1f / math.max(Vector3.Distance(vi, vertices[neighbors[k]]), MinEdgeLength);
					weights[k] = w;
					sum += w;
				}
				for (var k = begin; k < end; k++)
					weights[k] /= sum;
			}

			result.VertexCount = n;
			result.Offsets = offsets;
			result.Neighbors = neighbors;
			result.NeighborWeights = weights;
			result.Representative = representative;
			return result;
		}

		private static void AddEdge(List<int>[] lists, HashSet<long> seen, int a, int b)
		{
			if (a == b)
				return;
			var lo = math.min(a, b);
			var hi = math.max(a, b);
			if (!seen.Add(((long)lo << 32) | (uint)hi))
				return;
			(lists[a] ??= new List<int>()).Add(b);
			(lists[b] ??= new List<int>()).Add(a);
		}

		private void SetEmpty(int n)
		{
			VertexCount = n;
			Offsets = new int[n + 1];
			Neighbors = System.Array.Empty<int>();
			NeighborWeights = System.Array.Empty<float>();
			Representative = new int[n];
			for (var i = 0; i < n; i++)
				Representative[i] = i;
		}
	}
}
