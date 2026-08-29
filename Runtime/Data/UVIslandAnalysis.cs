// 移植元: dev ブランチ ExDeform/UVIslandSelector.cs(自作コード)の島解析部を再設計
using System.Collections.Generic;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// UV0 空間での UV 島(アイランド)解析。
	/// UV 座標を量子化したエッジを共有する三角形同士を同一の島とみなす
	/// (頂点インデックスが分割されていても UV が一致するシームは接続される)。
	/// 島はサブメッシュ単位で検出する(UV が重なる別マテリアルのパーツを区別するため)。
	/// 結果はメッシュが変わらない限り再利用できる。
	/// </summary>
	public sealed class UVIslandAnalysis
	{
		/// <summary>UV 量子化の刻み。これ未満の差は同一座標とみなす</summary>
		private const float UvEpsilon = 0.001f;

		public sealed class Island
		{
			public int Id;

			/// <summary>この島が属するサブメッシュ</summary>
			public int SubMesh;

			/// <summary>島に属する頂点インデックス(重複なし)</summary>
			public List<int> Vertices = new List<int>();

			/// <summary>島に属する三角形(頂点インデックス 3 つずつ)</summary>
			public List<int> Triangles = new List<int>();

			/// <summary>UV 空間の境界エッジ(x,y = 始点 / z,w = 終点)。フォールオフ距離に使う</summary>
			public List<Vector4> BorderEdges = new List<Vector4>();

			/// <summary>境界エッジの頂点インデックスペア(シーンビューの輪郭描画に使う)</summary>
			public List<int> BorderEdgeVerts = new List<int>();

			public Vector2 UvMin;
			public Vector2 UvMax;

			/// <summary>
			/// 島を再特定するための代表 UV(最初の三角形の重心。必ず島の内部にある)。
			/// シリアライズにはこの値を保存し、解析し直した際に FindIslandAt で解決する。
			/// </summary>
			public Vector2 Seed;
		}

		public readonly List<Island> Islands = new List<Island>();
		public int VertexCount { get; private set; }
		public int SubMeshCount { get; private set; }

		/// <summary>解析時の UV0(頂点インデックス順)。UV が無いメッシュでは空</summary>
		public Vector2[] Uvs { get; private set; } = System.Array.Empty<Vector2>();

		/// <summary>
		/// 全サブメッシュ連結順の三角形インデックス → 島の対応
		/// (RaycastHit.triangleIndex と同じ並び)。
		/// 三角形トポロジ以外のサブメッシュを含むメッシュでは null。
		/// </summary>
		public Island[] IslandOfTriangle { get; private set; }

		/// <summary>全島の UV 範囲(ビューの初期表示ウィンドウに使う)</summary>
		public Vector2 UvBoundsMin { get; private set; } = Vector2.zero;
		public Vector2 UvBoundsMax { get; private set; } = Vector2.one;

		public static UVIslandAnalysis Analyze(Mesh mesh)
		{
			var result = new UVIslandAnalysis();
			if (mesh == null)
				return result;

			result.VertexCount = mesh.vertexCount;
			var uvs = mesh.uv;
			if (uvs == null || uvs.Length != mesh.vertexCount)
				return result;

			result.Uvs = uvs;
			result.SubMeshCount = mesh.subMeshCount;

			var subTriangles = new int[result.SubMeshCount][];
			var totalTriangles = 0;
			var allTriangleTopology = true;
			for (var s = 0; s < result.SubMeshCount; s++)
			{
				if (mesh.GetTopology(s) == MeshTopology.Triangles)
				{
					subTriangles[s] = mesh.GetTriangles(s);
					totalTriangles += subTriangles[s].Length / 3;
				}
				else
				{
					subTriangles[s] = System.Array.Empty<int>();
					allTriangleTopology = false;
				}
			}
			if (totalTriangles == 0)
				return result;

			if (allTriangleTopology)
				result.IslandOfTriangle = new Island[totalTriangles];

			var globalTriangleOffset = 0;
			for (var s = 0; s < result.SubMeshCount; s++)
			{
				AnalyzeSubMesh(result, s, subTriangles[s], uvs, globalTriangleOffset);
				globalTriangleOffset += subTriangles[s].Length / 3;
			}

			if (result.Islands.Count > 0)
			{
				var min = result.Islands[0].UvMin;
				var max = result.Islands[0].UvMax;
				foreach (var island in result.Islands)
				{
					min = Vector2.Min(min, island.UvMin);
					max = Vector2.Max(max, island.UvMax);
				}
				result.UvBoundsMin = min;
				result.UvBoundsMax = max;
			}

			return result;
		}

		private static void AnalyzeSubMesh(
			UVIslandAnalysis result, int subMesh, int[] triangles, Vector2[] uvs, int globalTriangleOffset)
		{
			var triangleCount = triangles.Length / 3;
			if (triangleCount == 0)
				return;

			// Union-Find: 量子化 UV エッジを共有する三角形を統合する
			var parent = new int[triangleCount];
			for (var i = 0; i < triangleCount; i++)
				parent[i] = i;

			int Find(int x)
			{
				while (parent[x] != x)
				{
					parent[x] = parent[parent[x]];
					x = parent[x];
				}
				return x;
			}

			void Union(int a, int b)
			{
				var ra = Find(a);
				var rb = Find(b);
				if (ra != rb)
					parent[ra] = rb;
			}

			var edges = new Dictionary<(long, long), EdgeInfo>(triangleCount * 2);
			for (var t = 0; t < triangleCount; t++)
			{
				var baseIndex = t * 3;
				for (var e = 0; e < 3; e++)
				{
					var v1 = triangles[baseIndex + e];
					var v2 = triangles[baseIndex + (e + 1) % 3];
					var key = EdgeKey(uvs[v1], uvs[v2]);

					if (edges.TryGetValue(key, out var info))
					{
						Union(t, info.Triangle);
						info.Count++;
						edges[key] = info;
					}
					else
					{
						edges[key] = new EdgeInfo
						{
							Triangle = t, Count = 1,
							A = uvs[v1], B = uvs[v2], V1 = v1, V2 = v2,
						};
					}
				}
			}

			// ルートごとに島を構築(三角形の出現順で安定した ID を振る)
			var islandOfRoot = new Dictionary<int, Island>();
			var vertexSets = new Dictionary<Island, HashSet<int>>();
			var islandOfTriangle = new Island[triangleCount];

			for (var t = 0; t < triangleCount; t++)
			{
				var root = Find(t);
				if (!islandOfRoot.TryGetValue(root, out var island))
				{
					island = new Island { Id = result.Islands.Count, SubMesh = subMesh };
					islandOfRoot[root] = island;
					vertexSets[island] = new HashSet<int>();
					result.Islands.Add(island);

					var c = (uvs[triangles[t * 3]] + uvs[triangles[t * 3 + 1]] + uvs[triangles[t * 3 + 2]]) / 3f;
					island.Seed = c;
					island.UvMin = c;
					island.UvMax = c;
				}

				islandOfTriangle[t] = island;
				if (result.IslandOfTriangle != null)
					result.IslandOfTriangle[globalTriangleOffset + t] = island;

				var set = vertexSets[island];
				for (var e = 0; e < 3; e++)
				{
					var v = triangles[t * 3 + e];
					island.Triangles.Add(v);
					if (set.Add(v))
						island.Vertices.Add(v);

					var uv = uvs[v];
					island.UvMin = Vector2.Min(island.UvMin, uv);
					island.UvMax = Vector2.Max(island.UvMax, uv);
				}
			}

			// 1 つの三角形しか使わないエッジ = UV 空間の境界エッジ
			foreach (var info in edges.Values)
			{
				if (info.Count != 1)
					continue;
				var island = islandOfTriangle[info.Triangle];
				island.BorderEdges.Add(new Vector4(info.A.x, info.A.y, info.B.x, info.B.y));
				island.BorderEdgeVerts.Add(info.V1);
				island.BorderEdgeVerts.Add(info.V2);
			}
		}

		/// <summary>
		/// UV 座標が属する島を返す(見つからなければ null)。
		/// subMesh が 0 以上の場合はそのサブメッシュの島のみを対象にする。
		/// 三角形の内包判定を優先し、外れた場合は maxDistance 以内で
		/// 境界エッジが最も近い島へフォールバックする。
		/// </summary>
		public Island FindIslandAt(Vector2 uv, int subMesh = -1, float maxDistance = 0.02f)
		{
			foreach (var island in Islands)
			{
				if (subMesh >= 0 && island.SubMesh != subMesh)
					continue;
				if (uv.x < island.UvMin.x - UvEpsilon || uv.x > island.UvMax.x + UvEpsilon ||
				    uv.y < island.UvMin.y - UvEpsilon || uv.y > island.UvMax.y + UvEpsilon)
					continue;
				if (ContainsPoint(island, uv))
					return island;
			}

			Island best = null;
			var bestDistance = maxDistance;
			foreach (var island in Islands)
			{
				if (subMesh >= 0 && island.SubMesh != subMesh)
					continue;
				foreach (var edge in island.BorderEdges)
				{
					var d = DistancePointSegment(uv,
						new Vector2(edge.x, edge.y), new Vector2(edge.z, edge.w));
					if (d < bestDistance)
					{
						bestDistance = d;
						best = island;
					}
				}
			}
			return best;
		}

		public bool ContainsPoint(Island island, Vector2 uv)
		{
			var triangles = island.Triangles;
			for (var i = 0; i + 2 < triangles.Count; i += 3)
			{
				if (IsPointInTriangle(uv, Uvs[triangles[i]], Uvs[triangles[i + 1]], Uvs[triangles[i + 2]]))
					return true;
			}
			return false;
		}

		public static float DistancePointSegment(Vector2 point, Vector2 start, Vector2 end)
		{
			var line = end - start;
			var len2 = Vector2.Dot(line, line);
			if (len2 <= 0f)
				return Vector2.Distance(point, start);

			var t = Mathf.Clamp01(Vector2.Dot(point - start, line) / len2);
			return Vector2.Distance(point, start + t * line);
		}

		/// <summary>重心座標による三角形内包判定</summary>
		public static bool IsPointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
		{
			var v0 = c - a;
			var v1 = b - a;
			var v2 = point - a;

			var dot00 = Vector2.Dot(v0, v0);
			var dot01 = Vector2.Dot(v0, v1);
			var dot02 = Vector2.Dot(v0, v2);
			var dot11 = Vector2.Dot(v1, v1);
			var dot12 = Vector2.Dot(v1, v2);

			var denom = dot00 * dot11 - dot01 * dot01;
			if (Mathf.Approximately(denom, 0f))
				return false;

			var invDenom = 1f / denom;
			var u = (dot11 * dot02 - dot01 * dot12) * invDenom;
			var v = (dot00 * dot12 - dot01 * dot02) * invDenom;
			return u >= 0f && v >= 0f && u + v <= 1f;
		}

		private struct EdgeInfo
		{
			public int Triangle;
			public int Count;
			public Vector2 A;
			public Vector2 B;
			public int V1;
			public int V2;
		}

		private static (long, long) EdgeKey(Vector2 a, Vector2 b)
		{
			var ka = PointKey(a);
			var kb = PointKey(b);
			return ka <= kb ? (ka, kb) : (kb, ka);
		}

		private static long PointKey(Vector2 uv)
		{
			var x = Mathf.RoundToInt(uv.x / UvEpsilon);
			var y = Mathf.RoundToInt(uv.y / UvEpsilon);
			return ((long)x << 32) ^ (uint)y;
		}
	}
}
