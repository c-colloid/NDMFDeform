using System.Collections.Generic;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 頂点の隣接関係(CSR 形式)。位置で溶接して構築するため、
	/// UV シームで分割された頂点も同じ位置の頂点の隣接をすべて共有する
	/// (変位場の平滑化でシームに裂け目が出ないようにするため)。
	/// 同一位置の頂点同士は隣接に含めない(自分自身の複製なので寄与しない)。
	///
	/// 隣接リストには「隣接グループの代表頂点」を入れる。同じ位置の頂点は
	/// 平滑化の入力(変位)も隣接集合も同一なので、代表 1 つを参照すれば足りる。
	/// </summary>
	public sealed class MeshAdjacency
	{
		/// <summary>頂点 i の隣接は Neighbors[Start[i] .. Start[i + 1])</summary>
		public int[] Start { get; private set; }

		public int[] Neighbors { get; private set; }

		/// <summary>頂点 → 溶接グループ番号</summary>
		public int[] GroupOf { get; private set; }

		/// <summary>溶接グループ → 代表頂点番号(そのグループで最小の頂点番号)</summary>
		public int[] Representative { get; private set; }

		public int VertexCount => Start.Length - 1;

		public bool HasEdges => Neighbors.Length > 0;

		public static MeshAdjacency Build(Vector3[] vertices, int[] triangles)
		{
			var vertexCount = vertices?.Length ?? 0;
			var result = new MeshAdjacency();
			if (vertexCount == 0)
			{
				result.Start = new int[1];
				result.Neighbors = System.Array.Empty<int>();
				result.GroupOf = System.Array.Empty<int>();
				result.Representative = System.Array.Empty<int>();
				return result;
			}

			var weld = MeshSurface.WeldVertices(vertices);
			var groupCount = weld.GroupCount;
			var representative = new int[groupCount];
			for (var g = 0; g < groupCount; g++)
				representative[g] = -1;
			for (var i = 0; i < vertexCount; i++)
			{
				var g = weld.GroupOf[i];
				if (representative[g] < 0)
					representative[g] = i;
			}

			// グループ間のエッジ集合(重複なし・自己ループなし)
			var groupNeighbors = new HashSet<int>[groupCount];
			if (triangles != null)
			{
				var triCount = triangles.Length / 3;
				for (var t = 0; t < triCount; t++)
				{
					for (var e = 0; e < 3; e++)
					{
						var a = triangles[t * 3 + e];
						var b = triangles[t * 3 + (e + 1) % 3];
						if (a < 0 || b < 0 || a >= vertexCount || b >= vertexCount)
							continue;
						var ga = weld.GroupOf[a];
						var gb = weld.GroupOf[b];
						if (ga == gb)
							continue;
						(groupNeighbors[ga] ??= new HashSet<int>()).Add(gb);
						(groupNeighbors[gb] ??= new HashSet<int>()).Add(ga);
					}
				}
			}

			var start = new int[vertexCount + 1];
			for (var i = 0; i < vertexCount; i++)
			{
				var set = groupNeighbors[weld.GroupOf[i]];
				start[i + 1] = start[i] + (set?.Count ?? 0);
			}

			var neighbors = new int[start[vertexCount]];
			for (var i = 0; i < vertexCount; i++)
			{
				var set = groupNeighbors[weld.GroupOf[i]];
				if (set == null)
					continue;
				var cursor = start[i];
				foreach (var g in set)
					neighbors[cursor++] = representative[g];
			}

			result.Start = start;
			result.Neighbors = neighbors;
			result.GroupOf = weld.GroupOf;
			result.Representative = representative;
			return result;
		}
	}
}
