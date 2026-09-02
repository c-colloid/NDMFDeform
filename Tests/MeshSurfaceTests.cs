using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// MeshSurface(BVH + 最近接点 + 擬似法線による内外判定)の検証。
	/// 立方体に対する面 / 辺 / 頂点の各特徴と符号、探索距離上限、
	/// シーム分割された頂点の溶接、ランダム点の総当たりとの一致を確認する。
	/// </summary>
	public class MeshSurfaceTests
	{
		private readonly List<MeshSurface> _surfaces = new List<MeshSurface>();

		[TearDown]
		public void TearDown()
		{
			foreach (var s in _surfaces)
				s.Dispose();
			_surfaces.Clear();
		}

		private MeshSurface Build(Vector3[] vertices, int[] triangles)
		{
			var surface = MeshSurface.Build(vertices, triangles, Allocator.Persistent);
			_surfaces.Add(surface);
			return surface;
		}

		/// <summary>
		/// 原点中心・一辺 1 の立方体。sharedVertices = true なら 8 頂点を共有、
		/// false なら面ごとに 4 頂点を持つ(UV シームで分割されたメッシュ相当)。
		/// 巻き順は外向き法線になるよう自動調整する。
		/// </summary>
		private static (Vector3[] vertices, int[] triangles) MakeCube(bool sharedVertices)
		{
			var vertices = new List<Vector3>();
			var triangles = new List<int>();
			var shared = new Dictionary<Vector3, int>();

			int Add(Vector3 v)
			{
				if (sharedVertices)
				{
					if (!shared.TryGetValue(v, out var idx))
					{
						idx = vertices.Count;
						vertices.Add(v);
						shared[v] = idx;
					}
					return idx;
				}
				vertices.Add(v);
				return vertices.Count - 1;
			}

			void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
			{
				var n = Vector3.Cross(b - a, c - a);
				if (Vector3.Dot(n, outward) < 0f)
				{
					var tmp = b;
					b = c;
					c = tmp;
				}
				triangles.Add(Add(a));
				triangles.Add(Add(b));
				triangles.Add(Add(c));
			}

			for (var axis = 0; axis < 3; axis++)
			{
				for (var sign = -1; sign <= 1; sign += 2)
				{
					var normal = Vector3.zero;
					normal[axis] = sign;
					var u = Vector3.zero;
					u[(axis + 1) % 3] = 1f;
					var v = Vector3.zero;
					v[(axis + 2) % 3] = 1f;
					var center = normal * 0.5f;
					var p00 = center - u * 0.5f - v * 0.5f;
					var p10 = center + u * 0.5f - v * 0.5f;
					var p01 = center - u * 0.5f + v * 0.5f;
					var p11 = center + u * 0.5f + v * 0.5f;
					AddTriangle(p00, p10, p11, normal);
					AddTriangle(p00, p11, p01, normal);
				}
			}
			return (vertices.ToArray(), triangles.ToArray());
		}

		[Test]
		public void Build_CubeHasTwelveTrianglesAndNodes()
		{
			var (v, t) = MakeCube(true);
			var surface = Build(v, t);
			Assert.That(surface.IsCreated, Is.True);
			Assert.That(surface.Data.TriangleCount, Is.EqualTo(12));
			Assert.That(surface.Data.Nodes.Length, Is.GreaterThan(1));
			Assert.That(surface.Data.Nodes[0].Skip, Is.EqualTo(surface.Data.Nodes.Length),
				"ルートの Skip は配列末尾を指す");
		}

		[Test]
		public void Build_EmptyInputIsNotCreated()
		{
			var surface = Build(new Vector3[0], new int[0]);
			Assert.That(surface.IsCreated, Is.False);
		}

		[Test]
		public void FindClosest_OutsidePointOnFace()
		{
			var (v, t) = MakeCube(true);
			var surface = Build(v, t);

			var found = surface.Data.FindClosest(new float3(1f, 0.1f, 0.2f), 10f, out var hit);
			Assert.That(found, Is.True);
			Assert.That(math.distance(hit.Point, new float3(0.5f, 0.1f, 0.2f)), Is.LessThan(1e-5f));
			Assert.That(hit.Distance, Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(hit.Sign, Is.EqualTo(1f));
			Assert.That(math.distance(hit.Normal, new float3(1f, 0f, 0f)), Is.LessThan(1e-5f));
			Assert.That(hit.SignedDistance, Is.EqualTo(0.5f).Within(1e-5f));
		}

		[Test]
		public void FindClosest_InsidePointIsNegative()
		{
			var (v, t) = MakeCube(true);
			var surface = Build(v, t);

			var found = surface.Data.FindClosest(new float3(0.3f, 0f, 0f), 10f, out var hit);
			Assert.That(found, Is.True);
			Assert.That(math.distance(hit.Point, new float3(0.5f, 0f, 0f)), Is.LessThan(1e-5f));
			Assert.That(hit.Distance, Is.EqualTo(0.2f).Within(1e-5f));
			Assert.That(hit.Sign, Is.EqualTo(-1f));
			Assert.That(hit.SignedDistance, Is.EqualTo(-0.2f).Within(1e-5f));
		}

		[Test]
		public void FindClosest_VertexFeatureUsesWeldedPseudoNormal()
		{
			// 面ごとに頂点を分割した立方体でも、角の擬似法線は 3 面の合成(1,1,1)/√3 になる
			var (v, t) = MakeCube(false);
			Assert.That(v.Length, Is.EqualTo(24));
			var surface = Build(v, t);

			var found = surface.Data.FindClosest(new float3(1f, 1f, 1f), 10f, out var hit);
			Assert.That(found, Is.True);
			Assert.That(math.distance(hit.Point, new float3(0.5f, 0.5f, 0.5f)), Is.LessThan(1e-5f));
			Assert.That(hit.Sign, Is.EqualTo(1f));
			var expected = math.normalize(new float3(1f, 1f, 1f));
			Assert.That(math.distance(hit.Normal, expected), Is.LessThan(1e-4f),
				$"角の擬似法線が溶接されていない: {hit.Normal}");

			// 角のすぐ内側は内側判定になる(面法線だけでは判定できない位置)
			found = surface.Data.FindClosest(new float3(0.45f, 0.45f, 0.45f), 10f, out hit);
			Assert.That(found, Is.True);
			Assert.That(hit.Sign, Is.EqualTo(-1f));
		}

		[Test]
		public void FindClosest_EdgeFeatureUsesEdgePseudoNormal()
		{
			var (v, t) = MakeCube(false);
			var surface = Build(v, t);

			var found = surface.Data.FindClosest(new float3(1f, 1f, 0f), 10f, out var hit);
			Assert.That(found, Is.True);
			Assert.That(math.distance(hit.Point, new float3(0.5f, 0.5f, 0f)), Is.LessThan(1e-5f));
			Assert.That(hit.Sign, Is.EqualTo(1f));
			var expected = math.normalize(new float3(1f, 1f, 0f));
			Assert.That(math.distance(hit.Normal, expected), Is.LessThan(1e-4f));
		}

		[Test]
		public void FindClosest_RespectsMaxDistance()
		{
			var (v, t) = MakeCube(true);
			var surface = Build(v, t);

			Assert.That(surface.Data.FindClosest(new float3(3f, 0f, 0f), 1f, out _), Is.False);
			Assert.That(surface.Data.FindClosest(new float3(3f, 0f, 0f), 3f, out var hit), Is.True);
			Assert.That(hit.Distance, Is.EqualTo(2.5f).Within(1e-5f));
		}

		[Test]
		public void FindClosest_MatchesBruteForceOnBumpyGrid()
		{
			// 高低差のある格子面(800 三角形)に対して、ランダム点の最近接距離が総当たりと一致する
			const int n = 21;
			var vertices = new Vector3[n * n];
			for (var y = 0; y < n; y++)
			for (var x = 0; x < n; x++)
			{
				var fx = x / (float)(n - 1) * 4f - 2f;
				var fy = y / (float)(n - 1) * 4f - 2f;
				vertices[y * n + x] = new Vector3(fx, Mathf.Sin(fx * 2f) * Mathf.Cos(fy * 1.5f) * 0.4f, fy);
			}
			var triangles = new List<int>();
			for (var y = 0; y < n - 1; y++)
			for (var x = 0; x < n - 1; x++)
			{
				var i0 = y * n + x;
				triangles.AddRange(new[] { i0, i0 + n, i0 + 1, i0 + 1, i0 + n, i0 + n + 1 });
			}
			var surface = Build(vertices, triangles.ToArray());

			var random = new System.Random(12345);
			for (var s = 0; s < 200; s++)
			{
				var p = new float3(
					(float)(random.NextDouble() * 6.0 - 3.0),
					(float)(random.NextDouble() * 4.0 - 2.0),
					(float)(random.NextDouble() * 6.0 - 3.0));

				var best = float.MaxValue;
				for (var t = 0; t < triangles.Count; t += 3)
				{
					var q = MeshSurfaceData.ClosestPointOnTriangle(p,
						vertices[triangles[t]], vertices[triangles[t + 1]], vertices[triangles[t + 2]], out _);
					best = Mathf.Min(best, math.distance(p, q));
				}

				var found = surface.Data.FindClosest(p, float.MaxValue, out var hit);
				Assert.That(found, Is.True);
				Assert.That(hit.Distance, Is.EqualTo(best).Within(1e-4f), $"sample {s} at {p}");
			}
		}

		[Test]
		public void ClosestPointOnTriangle_ClassifiesFeatures()
		{
			var a = new float3(0f, 0f, 0f);
			var b = new float3(1f, 0f, 0f);
			var c = new float3(0f, 1f, 0f);

			MeshSurfaceData.ClosestPointOnTriangle(new float3(0.2f, 0.2f, 1f), a, b, c, out var feature);
			Assert.That(feature, Is.EqualTo(MeshSurfaceData.FeatureFace));

			MeshSurfaceData.ClosestPointOnTriangle(new float3(-1f, -1f, 0f), a, b, c, out feature);
			Assert.That(feature, Is.EqualTo(MeshSurfaceData.FeatureVertexA));

			MeshSurfaceData.ClosestPointOnTriangle(new float3(2f, -0.5f, 0f), a, b, c, out feature);
			Assert.That(feature, Is.EqualTo(MeshSurfaceData.FeatureVertexA + 1));

			MeshSurfaceData.ClosestPointOnTriangle(new float3(-0.5f, 2f, 0f), a, b, c, out feature);
			Assert.That(feature, Is.EqualTo(MeshSurfaceData.FeatureVertexA + 2));

			var q = MeshSurfaceData.ClosestPointOnTriangle(new float3(0.5f, -1f, 0f), a, b, c, out feature);
			Assert.That(feature, Is.EqualTo(MeshSurfaceData.FeatureEdgeAB));
			Assert.That(math.distance(q, new float3(0.5f, 0f, 0f)), Is.LessThan(1e-6f));

			q = MeshSurfaceData.ClosestPointOnTriangle(new float3(1f, 1f, 0f), a, b, c, out feature);
			Assert.That(feature, Is.EqualTo(MeshSurfaceData.FeatureEdgeAB + 1));
			Assert.That(math.distance(q, new float3(0.5f, 0.5f, 0f)), Is.LessThan(1e-6f));

			q = MeshSurfaceData.ClosestPointOnTriangle(new float3(-1f, 0.5f, 0f), a, b, c, out feature);
			Assert.That(feature, Is.EqualTo(MeshSurfaceData.FeatureEdgeAB + 2));
			Assert.That(math.distance(q, new float3(0f, 0.5f, 0f)), Is.LessThan(1e-6f));
		}

		[Test]
		public void Build_SkipsDegenerateTriangles()
		{
			var vertices = new[]
			{
				new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
				new Vector3(2f, 2f, 2f),
			};
			// 2 つ目は同一頂点の退化三角形
			var triangles = new[] { 0, 2, 1, 3, 3, 3 };
			var surface = Build(vertices, triangles);
			Assert.That(surface.IsCreated, Is.True);
			Assert.That(surface.Data.TriangleCount, Is.EqualTo(1));
		}
	}
}
