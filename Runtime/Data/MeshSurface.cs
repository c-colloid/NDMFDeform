using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// BVH ノード。ノード配列は深さ優先順に並び、内部ノードは「子は自身の直後」、
	/// Skip は「自身の部分木を飛ばした次のノード」を指す。
	/// スタック無し(スキップポインタ)で走査できるため、Burst ジョブから
	/// 追加のコンテナ無しに検索できる。
	/// </summary>
	public struct MeshSurfaceNode
	{
		public float3 Min;
		public float3 Max;

		/// <summary>葉: 三角形範囲の先頭(MeshSurfaceData.Triangles の三角形番号)</summary>
		public int Start;

		/// <summary>葉: 三角形数。内部ノードは 0</summary>
		public int Count;

		/// <summary>この部分木の次のノード番号(葉では自身 + 1)</summary>
		public int Skip;
	}

	/// <summary>最近接点クエリの結果</summary>
	public struct MeshSurfaceHit
	{
		/// <summary>表面上の最近接点</summary>
		public float3 Point;

		/// <summary>最近接点の特徴(面 / 辺 / 頂点)に応じた擬似法線(外向き・正規化済み)</summary>
		public float3 Normal;

		/// <summary>クエリ点から最近接点までの距離(符号なし)</summary>
		public float Distance;

		/// <summary>+1 = 表面の外側(法線側)、-1 = 内側</summary>
		public float Sign;

		/// <summary>最近接三角形(MeshSurfaceData 内の並び順)</summary>
		public int Triangle;

		/// <summary>符号付き距離(外側が正)</summary>
		public float SignedDistance => Distance * Sign;
	}

	/// <summary>
	/// 三角形メッシュへの最近接点クエリ(Burst ジョブから使用可能な読み取り専用ビュー)。
	/// 所有者は MeshSurface。全配列は三角形を BVH の葉順に並べ替えて保持する。
	///
	/// 内外判定は角度重み付き擬似法線(Baerentzen &amp; Aanaes, 2005)による:
	/// 最近接点が面 / 辺 / 頂点のどれに載っているかを分類し、それぞれ
	/// 面法線 / 隣接 2 面の法線和 / 角度重み付き法線和 を使う。
	/// 頂点は位置で溶接して集計するため、UV シームで分割された頂点でも法線が途切れない。
	/// </summary>
	public struct MeshSurfaceData
	{
		/// <summary>面の特徴コード: 0 = 面内、1..3 = 頂点 a/b/c、4..6 = 辺 ab/bc/ca</summary>
		public const int FeatureFace = 0;
		public const int FeatureVertexA = 1;
		public const int FeatureEdgeAB = 4;

		[ReadOnly] public NativeArray<float3> Vertices;

		/// <summary>三角形の頂点番号(3 つずつ、BVH 葉順)</summary>
		[ReadOnly] public NativeArray<int> Triangles;

		/// <summary>三角形ごとの面法線(正規化済み)</summary>
		[ReadOnly] public NativeArray<float3> FaceNormals;

		/// <summary>三角形ごと 3 本の辺(ab, bc, ca)の擬似法線</summary>
		[ReadOnly] public NativeArray<float3> EdgeNormals;

		/// <summary>三角形ごと 3 つの頂点(a, b, c)の擬似法線</summary>
		[ReadOnly] public NativeArray<float3> CornerNormals;

		[ReadOnly] public NativeArray<MeshSurfaceNode> Nodes;

		public bool IsCreated => Nodes.IsCreated && Nodes.Length > 0;

		public int TriangleCount => Triangles.Length / 3;

		/// <summary>
		/// 点 p の最近接点を maxDistance 以内で探す。見つからなければ false。
		/// </summary>
		public bool FindClosest(float3 p, float maxDistance, out MeshSurfaceHit hit)
		{
			hit = default;
			var bestSq = maxDistance * maxDistance;
			var bestTri = -1;
			var bestFeature = 0;
			var bestPoint = float3.zero;

			var nodeCount = Nodes.Length;
			var i = 0;
			while (i < nodeCount)
			{
				var node = Nodes[i];
				if (DistanceSqToBox(p, node.Min, node.Max) >= bestSq)
				{
					i = node.Skip;
					continue;
				}

				var end = node.Start + node.Count;
				for (var t = node.Start; t < end; t++)
				{
					var a = Vertices[Triangles[t * 3]];
					var b = Vertices[Triangles[t * 3 + 1]];
					var c = Vertices[Triangles[t * 3 + 2]];
					var q = ClosestPointOnTriangle(p, a, b, c, out var feature);
					var dSq = lengthsq(q - p);
					if (dSq < bestSq)
					{
						bestSq = dSq;
						bestTri = t;
						bestFeature = feature;
						bestPoint = q;
					}
				}
				i++;
			}

			if (bestTri < 0)
				return false;

			float3 normal;
			if (bestFeature == FeatureFace)
				normal = FaceNormals[bestTri];
			else if (bestFeature < FeatureEdgeAB)
				normal = CornerNormals[bestTri * 3 + (bestFeature - FeatureVertexA)];
			else
				normal = EdgeNormals[bestTri * 3 + (bestFeature - FeatureEdgeAB)];

			var offset = p - bestPoint;
			var distance = sqrt(bestSq);
			hit.Point = bestPoint;
			hit.Normal = normal;
			hit.Distance = distance;
			hit.Triangle = bestTri;
			// 表面上(距離ほぼ 0)は外側扱いにする(押し出し方向が法線になる)
			hit.Sign = distance <= 1e-7f || dot(offset, normal) >= 0f ? 1f : -1f;
			return true;
		}

		public static float DistanceSqToBox(float3 p, float3 boxMin, float3 boxMax)
		{
			var d = max(max(boxMin - p, p - boxMax), float3.zero);
			return dot(d, d);
		}

		/// <summary>
		/// 三角形 abc 上の p への最近接点(Ericson, Real-Time Collision Detection 5.1.5)。
		/// feature に最近接点の載る特徴(FeatureFace / FeatureVertexA+k / FeatureEdgeAB+k)を返す。
		/// </summary>
		public static float3 ClosestPointOnTriangle(float3 p, float3 a, float3 b, float3 c, out int feature)
		{
			var ab = b - a;
			var ac = c - a;
			var ap = p - a;
			var d1 = dot(ab, ap);
			var d2 = dot(ac, ap);
			if (d1 <= 0f && d2 <= 0f)
			{
				feature = FeatureVertexA;
				return a;
			}

			var bp = p - b;
			var d3 = dot(ab, bp);
			var d4 = dot(ac, bp);
			if (d3 >= 0f && d4 <= d3)
			{
				feature = FeatureVertexA + 1;
				return b;
			}

			var vc = d1 * d4 - d3 * d2;
			if (vc <= 0f && d1 >= 0f && d3 <= 0f)
			{
				var denom = d1 - d3;
				var v = denom > 0f ? d1 / denom : 0f;
				feature = FeatureEdgeAB;
				return a + v * ab;
			}

			var cp = p - c;
			var d5 = dot(ab, cp);
			var d6 = dot(ac, cp);
			if (d6 >= 0f && d5 <= d6)
			{
				feature = FeatureVertexA + 2;
				return c;
			}

			var vb = d5 * d2 - d1 * d6;
			if (vb <= 0f && d2 >= 0f && d6 <= 0f)
			{
				var denom = d2 - d6;
				var w = denom > 0f ? d2 / denom : 0f;
				feature = FeatureEdgeAB + 2;
				return a + w * ac;
			}

			var va = d3 * d6 - d5 * d4;
			if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
			{
				var denom = (d4 - d3) + (d5 - d6);
				var w = denom > 0f ? (d4 - d3) / denom : 0f;
				feature = FeatureEdgeAB + 1;
				return b + w * (c - b);
			}

			{
				var denom = va + vb + vc;
				if (denom <= 0f)
				{
					// 退化三角形: 頂点 a を返す
					feature = FeatureVertexA;
					return a;
				}
				var inv = 1f / denom;
				var v = vb * inv;
				var w = vc * inv;
				feature = FeatureFace;
				return a + ab * v + ac * w;
			}
		}
	}

	/// <summary>
	/// 三角形メッシュの最近接点クエリ用データ(BVH + 擬似法線)を構築・所有する。
	/// 入力頂点は呼び出し側で目的の空間(通常はスキン済みワールド空間)へ変換しておく。
	/// 構築はメインスレッドの managed コードで行い、結果を NativeArray に書き出す。
	/// </summary>
	public sealed class MeshSurface : IDisposable
	{
		/// <summary>頂点溶接の量子化幅(m)。これ未満の差は同一位置とみなす</summary>
		public const float WeldEpsilon = 1e-5f;

		/// <summary>葉あたりの最大三角形数</summary>
		private const int LeafSize = 4;

		public MeshSurfaceData Data;
		public bool IsCreated => Data.IsCreated;

		private MeshSurface() { }

		/// <summary>
		/// 頂点と三角形インデックス(全サブメッシュ連結)から構築する。
		/// 退化三角形は除外する。三角形が 1 つも無い場合は IsCreated = false の空データを返す。
		/// </summary>
		public static MeshSurface Build(Vector3[] vertices, int[] triangles, Allocator allocator)
		{
			var surface = new MeshSurface();
			if (vertices == null || triangles == null)
				return surface;

			var vertexCount = vertices.Length;
			var weld = WeldVertices(vertices);

			// 有効(非退化)三角形の収集と面法線
			var triCount = triangles.Length / 3;
			var validTris = new List<int>(triCount);
			var faceNormals = new List<Vector3>(triCount);
			for (var t = 0; t < triCount; t++)
			{
				var i0 = triangles[t * 3];
				var i1 = triangles[t * 3 + 1];
				var i2 = triangles[t * 3 + 2];
				if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount)
					continue;
				var n = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
				var len = n.magnitude;
				if (len <= 1e-12f)
					continue;
				validTris.Add(t);
				faceNormals.Add(n / len);
			}

			var count = validTris.Count;
			if (count == 0)
				return surface;

			// 角度重み付き頂点擬似法線(溶接 ID 単位)
			var vertexNormals = new Vector3[weld.GroupCount];
			for (var k = 0; k < count; k++)
			{
				var t = validTris[k];
				var n = faceNormals[k];
				for (var corner = 0; corner < 3; corner++)
				{
					var i0 = triangles[t * 3 + corner];
					var i1 = triangles[t * 3 + (corner + 1) % 3];
					var i2 = triangles[t * 3 + (corner + 2) % 3];
					var e1 = (vertices[i1] - vertices[i0]).normalized;
					var e2 = (vertices[i2] - vertices[i0]).normalized;
					var angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(e1, e2), -1f, 1f));
					vertexNormals[weld.GroupOf[i0]] += n * angle;
				}
			}
			for (var g = 0; g < vertexNormals.Length; g++)
			{
				var len = vertexNormals[g].magnitude;
				if (len > 1e-12f)
					vertexNormals[g] /= len;
			}

			// 辺擬似法線: 隣接面の法線和(境界辺は片面のみ)
			var edgeNormalSum = new Dictionary<long, Vector3>(count * 3);
			for (var k = 0; k < count; k++)
			{
				var t = validTris[k];
				var n = faceNormals[k];
				for (var e = 0; e < 3; e++)
				{
					var key = EdgeKey(weld.GroupOf[triangles[t * 3 + e]], weld.GroupOf[triangles[t * 3 + (e + 1) % 3]]);
					edgeNormalSum.TryGetValue(key, out var sum);
					edgeNormalSum[key] = sum + n;
				}
			}

			// BVH 構築(三角形の並べ替え順を得る)
			var order = new int[count];
			for (var k = 0; k < count; k++)
				order[k] = k;
			var centroids = new Vector3[count];
			var boxMin = new Vector3[count];
			var boxMax = new Vector3[count];
			for (var k = 0; k < count; k++)
			{
				var t = validTris[k];
				var a = vertices[triangles[t * 3]];
				var b = vertices[triangles[t * 3 + 1]];
				var c = vertices[triangles[t * 3 + 2]];
				boxMin[k] = Vector3.Min(a, Vector3.Min(b, c));
				boxMax[k] = Vector3.Max(a, Vector3.Max(b, c));
				centroids[k] = (a + b + c) / 3f;
			}
			var nodes = BuildNodes(order, centroids, boxMin, boxMax);

			// 出力配列(BVH 葉順)
			surface.Data.Vertices = new NativeArray<float3>(vertexCount, allocator, NativeArrayOptions.UninitializedMemory);
			for (var i = 0; i < vertexCount; i++)
				surface.Data.Vertices[i] = vertices[i];

			surface.Data.Triangles = new NativeArray<int>(count * 3, allocator, NativeArrayOptions.UninitializedMemory);
			surface.Data.FaceNormals = new NativeArray<float3>(count, allocator, NativeArrayOptions.UninitializedMemory);
			surface.Data.EdgeNormals = new NativeArray<float3>(count * 3, allocator, NativeArrayOptions.UninitializedMemory);
			surface.Data.CornerNormals = new NativeArray<float3>(count * 3, allocator, NativeArrayOptions.UninitializedMemory);
			for (var slot = 0; slot < count; slot++)
			{
				var k = order[slot];
				var t = validTris[k];
				surface.Data.FaceNormals[slot] = faceNormals[k];
				for (var e = 0; e < 3; e++)
				{
					var v0 = triangles[t * 3 + e];
					var v1 = triangles[t * 3 + (e + 1) % 3];
					surface.Data.Triangles[slot * 3 + e] = v0;
					surface.Data.CornerNormals[slot * 3 + e] = vertexNormals[weld.GroupOf[v0]];

					var sum = edgeNormalSum[EdgeKey(weld.GroupOf[v0], weld.GroupOf[v1])];
					var len = sum.magnitude;
					surface.Data.EdgeNormals[slot * 3 + e] = len > 1e-12f ? sum / len : faceNormals[k];
				}
			}

			surface.Data.Nodes = new NativeArray<MeshSurfaceNode>(nodes.Count, allocator, NativeArrayOptions.UninitializedMemory);
			for (var i = 0; i < nodes.Count; i++)
				surface.Data.Nodes[i] = nodes[i];

			return surface;
		}

		public void Dispose()
		{
			if (Data.Vertices.IsCreated) Data.Vertices.Dispose();
			if (Data.Triangles.IsCreated) Data.Triangles.Dispose();
			if (Data.FaceNormals.IsCreated) Data.FaceNormals.Dispose();
			if (Data.EdgeNormals.IsCreated) Data.EdgeNormals.Dispose();
			if (Data.CornerNormals.IsCreated) Data.CornerNormals.Dispose();
			if (Data.Nodes.IsCreated) Data.Nodes.Dispose();
			Data = default;
		}

		// ---- 溶接 ----

		public struct WeldResult
		{
			/// <summary>頂点番号 → 溶接グループ番号</summary>
			public int[] GroupOf;

			public int GroupCount;
		}

		/// <summary>位置を量子化して同一位置の頂点を同じグループにまとめる</summary>
		public static WeldResult WeldVertices(Vector3[] vertices)
		{
			var groupOf = new int[vertices.Length];
			var groups = new Dictionary<(int, int, int), int>(vertices.Length);
			for (var i = 0; i < vertices.Length; i++)
			{
				var v = vertices[i];
				var key = (Mathf.RoundToInt(v.x / WeldEpsilon), Mathf.RoundToInt(v.y / WeldEpsilon),
					Mathf.RoundToInt(v.z / WeldEpsilon));
				if (!groups.TryGetValue(key, out var g))
				{
					g = groups.Count;
					groups[key] = g;
				}
				groupOf[i] = g;
			}
			return new WeldResult { GroupOf = groupOf, GroupCount = groups.Count };
		}

		private static long EdgeKey(int a, int b)
		{
			if (a > b)
			{
				var tmp = a;
				a = b;
				b = tmp;
			}
			return ((long)a << 32) | (uint)b;
		}

		// ---- BVH ----

		/// <summary>
		/// 重心の最長軸を中央値で分割する単純な BVH を深さ優先順で構築する。
		/// order は三角形番号の並び(入力は恒等順、出力は葉順)。
		/// 再帰深さは log2(三角形数 / LeafSize) 程度(20 万三角形で約 16)。
		/// </summary>
		private static List<MeshSurfaceNode> BuildNodes(int[] order, Vector3[] centroids, Vector3[] boxMin,
			Vector3[] boxMax)
		{
			var nodes = new List<MeshSurfaceNode>(order.Length / LeafSize * 2 + 1);
			var comparer = new CentroidComparer { Centroids = centroids };
			nodes.Add(default);
			BuildSubtree(nodes, order, centroids, boxMin, boxMax, comparer, 0, order.Length, 0);
			return nodes;
		}

		/// <summary>
		/// [begin, end) の部分木を nodeIndex に構築する(nodeIndex は確保済み)。
		/// 部分木のノードは nodeIndex の直後に連続して追加される(左の子 = nodeIndex + 1)。
		/// </summary>
		private static void BuildSubtree(List<MeshSurfaceNode> nodes, int[] order, Vector3[] centroids,
			Vector3[] boxMin, Vector3[] boxMax, CentroidComparer comparer, int begin, int end, int nodeIndex)
		{
			var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			var cMin = min;
			var cMax = max;
			for (var i = begin; i < end; i++)
			{
				var k = order[i];
				min = Vector3.Min(min, boxMin[k]);
				max = Vector3.Max(max, boxMax[k]);
				cMin = Vector3.Min(cMin, centroids[k]);
				cMax = Vector3.Max(cMax, centroids[k]);
			}

			var node = new MeshSurfaceNode { Min = min, Max = max };
			var n = end - begin;
			var extent = cMax - cMin;
			var axis = extent.x >= extent.y ? (extent.x >= extent.z ? 0 : 2) : (extent.y >= extent.z ? 1 : 2);
			if (n <= LeafSize || extent[axis] <= 1e-9f)
			{
				node.Start = begin;
				node.Count = n;
				node.Skip = nodeIndex + 1;
				nodes[nodeIndex] = node;
				return;
			}

			comparer.Axis = axis;
			Array.Sort(order, begin, n, comparer);
			var mid = begin + n / 2;

			var leftIndex = nodes.Count;
			nodes.Add(default);
			BuildSubtree(nodes, order, centroids, boxMin, boxMax, comparer, begin, mid, leftIndex);
			var rightIndex = nodes.Count;
			nodes.Add(default);
			BuildSubtree(nodes, order, centroids, boxMin, boxMax, comparer, mid, end, rightIndex);

			node.Count = 0;
			node.Start = 0;
			node.Skip = nodes.Count;
			nodes[nodeIndex] = node;
		}

		private sealed class CentroidComparer : IComparer<int>
		{
			public Vector3[] Centroids;
			public int Axis;

			public int Compare(int a, int b)
			{
				return Centroids[a][Axis].CompareTo(Centroids[b][Axis]);
			}
		}
	}
}
