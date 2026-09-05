using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// Cylindrical Vertex Transform の断ち切りライン(scope 円筒・top / bottom 平面)の扱い:
	/// falloff による滑らかな減衰、押し出しの折り返し防止、軸の横切り防止、隣接拡散。
	/// </summary>
	public class CylindricalVertexTransformTests
	{
		private GameObject _root;
		private Mesh _source;
		private Mesh _baked;

		[TearDown]
		public void TearDown()
		{
			if (_root != null) Object.DestroyImmediate(_root);
			if (_source != null) Object.DestroyImmediate(_source);
			if (_baked != null) Object.DestroyImmediate(_baked);
		}

		/// <summary>ルート直下に軸(= ルート空間)を持つデフォーマと頂点のみのメッシュを作る</summary>
		private (DeformStack stack, CylindricalVertexTransformDeformer deformer) CreateSetup(Vector3[] vertices)
		{
			_root = new GameObject("CylindricalVertexTransformTestRoot");
			var stack = _root.AddComponent<DeformStack>();

			var child = new GameObject("Deformer");
			child.transform.SetParent(_root.transform, false);
			var deformer = child.AddComponent<CylindricalVertexTransformDeformer>();
			stack.AddDeformer(deformer);

			_source = new Mesh { vertices = vertices };
			return (stack, deformer);
		}

		private static float Radial(Vector3 v) => new Vector2(v.x, v.y).magnitude;

		// ---- 純粋関数 ----

		[Test]
		public void Fade_IsOneInsideAndZeroBeyondBand()
		{
			Assert.That(CylindricalCutLine.Fade(-0.3f, 1f), Is.EqualTo(1f));
			Assert.That(CylindricalCutLine.Fade(0f, 1f), Is.EqualTo(1f));
			Assert.That(CylindricalCutLine.Fade(0.5f, 1f), Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(CylindricalCutLine.Fade(1f, 1f), Is.EqualTo(0f));
			// band = 0 は境界で打ち切る(NaN にならない)
			Assert.That(CylindricalCutLine.Fade(1e-6f, 0f), Is.EqualTo(0f));
		}

		[Test]
		public void Weight_WithZeroBands_MatchesLegacyBoundary()
		{
			// 旧実装: d < scope(strict)、bottom <= z <= top(inclusive)
			Assert.That(CylindricalCutLine.Weight(new float3(0.999f, 0f, 0f), 1f, 0.5f, -0.5f, 0f, 0f), Is.EqualTo(1f));
			Assert.That(CylindricalCutLine.Weight(new float3(1f, 0f, 0f), 1f, 0.5f, -0.5f, 0f, 0f), Is.EqualTo(0f));
			Assert.That(CylindricalCutLine.Weight(new float3(0.5f, 0f, 0.5f), 1f, 0.5f, -0.5f, 0f, 0f), Is.EqualTo(1f));
			Assert.That(CylindricalCutLine.Weight(new float3(0.5f, 0f, 0.51f), 1f, 0.5f, -0.5f, 0f, 0f), Is.EqualTo(0f));
			Assert.That(CylindricalCutLine.Weight(new float3(0.5f, 0f, -0.51f), 1f, 0.5f, -0.5f, 0f, 0f), Is.EqualTo(0f));
		}

		[Test]
		public void Apply_NeverCrossesAxisAndHasNoNaNOnAxis()
		{
			var pulled = CylindricalCutLine.Apply(new float3(0.2f, 0f, 0f), 1f, -1f);
			Assert.That(pulled.x, Is.GreaterThan(0f));
			Assert.That(pulled.x, Is.EqualTo(0.2f * CylindricalCutLine.MinRadialSlope).Within(1e-5f));

			var onAxis = CylindricalCutLine.Apply(new float3(0f, 0f, 0f), 1f, -1f);
			Assert.That(float.IsNaN(onAxis.x) || float.IsNaN(onAxis.y), Is.False);
			Assert.That(math.length(onAxis), Is.EqualTo(0f));
		}

		// ---- ベイク: falloff ----

		[Test]
		public void PullIn_Falloff_BlendsAcrossScopeCutLine()
		{
			// radius 0.5 / scope 1 → 引き込み 0.5。falloff 1 で d ∈ [1, 2] に帯を取る
			var (stack, deformer) = CreateSetup(new[]
			{
				new Vector3(0.9f, 0f, 0f),   // 内側: 全量
				new Vector3(1.5f, 0f, 0f),   // 帯の中央: 半量
				new Vector3(2f, 0f, 0f),     // 帯の端: 変化なし
				new Vector3(3f, 0f, 0f),     // 外側: 変化なし
			});
			deformer.Factor = 1f;
			deformer.Radius = 0.5f;
			deformer.Scope = 1f;
			deformer.Falloff = 1f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);
			var v = _baked.vertices;

			Assert.That(v[0].x, Is.EqualTo(0.4f).Within(1e-4f));
			Assert.That(v[1].x, Is.EqualTo(1.25f).Within(1e-4f));
			Assert.That(v[2].x, Is.EqualTo(2f).Within(1e-4f));
			Assert.That(v[3].x, Is.EqualTo(3f).Within(1e-4f));
		}

		[Test]
		public void Falloff_BlendsAcrossTopAndBottomCutLines()
		{
			var (stack, deformer) = CreateSetup(new[]
			{
				new Vector3(0.5f, 0f, 0.5f),    // top 上: 全量
				new Vector3(0.5f, 0f, 0.6f),    // top の帯中央: 半量
				new Vector3(0.5f, 0f, 0.7f),    // top の帯の端: 変化なし
				new Vector3(0.5f, 0f, -0.6f),   // bottom の帯中央: 半量
				new Vector3(0.5f, 0f, -0.8f),   // bottom より外: 変化なし
			});
			deformer.Factor = 1f;
			deformer.Radius = 0.5f;
			deformer.Scope = 1f;
			deformer.Top = 0.5f;
			deformer.Bottom = -0.5f;
			deformer.Falloff = 0.2f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);
			var v = _baked.vertices;

			Assert.That(v[0].x, Is.EqualTo(0f).Within(1e-4f));
			Assert.That(v[1].x, Is.EqualTo(0.25f).Within(1e-4f));
			Assert.That(v[2].x, Is.EqualTo(0.5f).Within(1e-4f));
			Assert.That(v[3].x, Is.EqualTo(0.25f).Within(1e-4f));
			Assert.That(v[4].x, Is.EqualTo(0.5f).Within(1e-4f));
			// z は変わらない
			for (var i = 0; i < v.Length; i++)
				Assert.That(v[i].z, Is.EqualTo(_source.vertices[i].z).Within(1e-5f));
		}

		[Test]
		public void ZeroFalloff_KeepsLegacyHardCutForPullIn()
		{
			var (stack, deformer) = CreateSetup(new[]
			{
				new Vector3(0.9f, 0f, 0f),
				new Vector3(1.1f, 0f, 0f),
			});
			deformer.Factor = 1f;
			deformer.Radius = 0.5f;
			deformer.Scope = 1f;
			deformer.Falloff = 0f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);
			var v = _baked.vertices;

			Assert.That(v[0].x, Is.EqualTo(0.4f).Within(1e-4f));
			Assert.That(v[1].x, Is.EqualTo(1.1f).Within(1e-4f));
		}

		// ---- ベイク: 折り返し防止 ----

		[Test]
		public void PushOut_DoesNotOvertakeVerticesOutsideCutLine()
		{
			// radius 2 / scope 1 → 押し出し 1。falloff 0 でも、外側 [1, 3] の頂点は
			// 内側の頂点に追い越されず、放射方向の順序が保たれる
			var (stack, deformer) = CreateSetup(new[]
			{
				new Vector3(0.5f, 0f, 0f),
				new Vector3(0.99f, 0f, 0f),
				new Vector3(1.01f, 0f, 0f),
				new Vector3(1.3f, 0f, 0f),
				new Vector3(1.7f, 0f, 0f),
				new Vector3(2f, 0f, 0f),
				new Vector3(2.5f, 0f, 0f),
				new Vector3(3f, 0f, 0f),
				new Vector3(4f, 0f, 0f),
			});
			deformer.Factor = 1f;
			deformer.Radius = 2f;
			deformer.Scope = 1f;
			deformer.Falloff = 0f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);
			var v = _baked.vertices;

			// 内側は従来どおり全量押し出し
			Assert.That(v[0].x, Is.EqualTo(1.5f).Within(1e-4f));
			Assert.That(v[1].x, Is.EqualTo(1.99f).Within(1e-4f));
			// 帯の外は不変
			Assert.That(v[7].x, Is.EqualTo(3f).Within(1e-4f));
			Assert.That(v[8].x, Is.EqualTo(4f).Within(1e-4f));
			// 単調(追い越しなし)かつ勾配は MinRadialSlope 以上
			var src = _source.vertices;
			for (var i = 1; i < v.Length; i++)
			{
				var slope = (v[i].x - v[i - 1].x) / (src[i].x - src[i - 1].x);
				Assert.That(slope, Is.GreaterThanOrEqualTo(CylindricalCutLine.MinRadialSlope - 1e-3f),
					$"vertex {i - 1} → {i}");
			}
		}

		[Test]
		public void PushOut_FoldGuardBandScalesWithFactor()
		{
			// factor 0.5 → 押し出し 0.5 → 帯幅 1。d = 2.5 は帯の外で不変
			var (stack, deformer) = CreateSetup(new[]
			{
				new Vector3(0.5f, 0f, 0f),
				new Vector3(2.5f, 0f, 0f),
			});
			deformer.Factor = 0.5f;
			deformer.Radius = 2f;
			deformer.Scope = 1f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);
			var v = _baked.vertices;

			Assert.That(v[0].x, Is.EqualTo(1f).Within(1e-4f));
			Assert.That(v[1].x, Is.EqualTo(2.5f).Within(1e-4f));
		}

		[Test]
		public void PullIn_StopsBeforeAxis()
		{
			var (stack, deformer) = CreateSetup(new[]
			{
				new Vector3(0.2f, 0f, 0f),
				new Vector3(0f, 0.2f, 0f),
				new Vector3(0f, 0f, 0f),
			});
			deformer.Factor = 1f;
			deformer.Radius = 0f;
			deformer.Scope = 1f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);
			var v = _baked.vertices;

			Assert.That(v[0].x, Is.GreaterThan(0f));
			Assert.That(v[1].y, Is.GreaterThan(0f));
			Assert.That(Radial(v[0]), Is.EqualTo(Radial(v[1])).Within(1e-5f));
			Assert.That(float.IsNaN(v[2].x) || float.IsNaN(v[2].y) || float.IsNaN(v[2].z), Is.False);
			Assert.That(v[2].magnitude, Is.LessThan(1e-5f));
		}

		// ---- ベイク: 隣接拡散 ----

		/// <summary>
		/// x 方向に並ぶ帯状メッシュ(2 行 × columns 列、x = column × spacing)。
		/// seamColumn の列は UV シーム相当として頂点を複製し(左右のクアッドで別の頂点を使う)、
		/// 位置溶接をまたいで拡散が伝わることも確かめられるようにする。
		/// rowStart[c] は列 c の行 0 の頂点インデックス(seamColumn は左側の複製)、
		/// seamAlt は seamColumn の右側の複製の行 0 インデックス。
		/// </summary>
		private static Mesh CreateStrip(int columns, float spacing, int seamColumn,
			out int[] rowStart, out int seamAlt)
		{
			var vertices = new System.Collections.Generic.List<Vector3>();
			var triangles = new System.Collections.Generic.List<int>();
			rowStart = new int[columns];
			seamAlt = -1;
			for (var c = 0; c < columns; c++)
			{
				rowStart[c] = vertices.Count;
				vertices.Add(new Vector3(c * spacing, 0f, 0f));
				vertices.Add(new Vector3(c * spacing, 0f, 0.1f));
				if (c == seamColumn)
				{
					seamAlt = vertices.Count;
					vertices.Add(new Vector3(c * spacing, 0f, 0f));
					vertices.Add(new Vector3(c * spacing, 0f, 0.1f));
				}
			}
			for (var c = 0; c + 1 < columns; c++)
			{
				// 列 c の右側クアッドは、c がシーム列なら右側の複製を使う
				var a = c == seamColumn ? seamAlt : rowStart[c];
				var b = rowStart[c + 1];
				triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
				triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
			}
			var mesh = new Mesh();
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			return mesh;
		}

		[Test]
		public void Smoothing_SpreadsHardCutAlongMeshConnectivity()
		{
			// 列 x = 0, 0.2, ..., 2.0。scope 1.05 → 列 0..5 が内側、6.. が外側。
			// falloff 0 で段差を作り、拡散で列 5/6 の境界がならされることを見る。
			// 列 6 はシーム(重複頂点)にし、溶接なしでは右へ伝わらない状況を作る
			const int columns = 11;
			const int seam = 6;
			_source = CreateStrip(columns, 0.2f, seam, out var rowStart, out var seamAlt);

			_root = new GameObject("CylindricalVertexTransformTestRoot");
			var stack = _root.AddComponent<DeformStack>();
			var child = new GameObject("Deformer");
			child.transform.SetParent(_root.transform, false);
			var deformer = child.AddComponent<CylindricalVertexTransformDeformer>();
			stack.AddDeformer(deformer);
			deformer.Factor = 1f;
			deformer.Radius = 0.55f;
			deformer.Scope = 1.05f;
			deformer.Falloff = 0f;
			deformer.Top = 1f;
			deformer.Bottom = -1f;
			deformer.SmoothIterations = 4;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);
			var v = _baked.vertices;
			var src = _source.vertices;

			// 行ごとの変位量(引き込みなので正の値で扱う)
			var pull0 = new float[columns];
			var pull1 = new float[columns];
			for (var c = 0; c < columns; c++)
			{
				pull0[c] = src[rowStart[c]].x - v[rowStart[c]].x;
				pull1[c] = src[rowStart[c] + 1].x - v[rowStart[c] + 1].x;
			}

			foreach (var pull in new[] { pull0, pull1 })
			{
				// 境界の両側が部分変位になっている(段差がならされている)
				Assert.That(pull[5], Is.GreaterThan(0.01f).And.LessThan(0.49f), "inside edge");
				Assert.That(pull[6], Is.GreaterThan(0.01f).And.LessThan(0.49f), "outside edge (seam)");
				// シームの先(列 7)へも溶接を通じて伝わる
				Assert.That(pull[7], Is.GreaterThan(0.01f), "beyond seam");
				// 遠方は全量 / 不変のまま
				Assert.That(pull[0], Is.EqualTo(0.5f).Within(1e-4f));
				Assert.That(pull[columns - 1], Is.EqualTo(0f).Within(1e-4f));
				// 変位は内→外へ単調減少
				for (var c = 1; c < columns; c++)
					Assert.That(pull[c], Is.LessThanOrEqualTo(pull[c - 1] + 1e-5f), $"column {c}");
			}

			// シームの複製頂点は同じ位置・同じ隣接を共有するので結果も一致する
			Assert.That(v[seamAlt].x, Is.EqualTo(v[rowStart[seam]].x).Within(1e-6f));
			Assert.That(v[seamAlt + 1].x, Is.EqualTo(v[rowStart[seam] + 1].x).Within(1e-6f));
		}

		[Test]
		public void Smoothing_WithoutTriangles_FallsBackToSinglePass()
		{
			var (stack, deformer) = CreateSetup(new[]
			{
				new Vector3(0.9f, 0f, 0f),
				new Vector3(1.1f, 0f, 0f),
			});
			deformer.Factor = 1f;
			deformer.Radius = 0.5f;
			deformer.Scope = 1f;
			deformer.SmoothIterations = 8;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);
			var v = _baked.vertices;

			Assert.That(v[0].x, Is.EqualTo(0.4f).Within(1e-4f));
			Assert.That(v[1].x, Is.EqualTo(1.1f).Within(1e-4f));
		}

		// ---- MeshAdjacency ----

		[Test]
		public void MeshAdjacency_WeldsDuplicatePositionsAcrossSeam()
		{
			// 2 枚の三角形が辺 (1,0,0)-(0,1,0) を共有するが、頂点は分割されている
			var mesh = new Mesh
			{
				vertices = new[]
				{
					new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
					new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
				},
				triangles = new[] { 0, 1, 2, 3, 5, 4 },
			};
			try
			{
				var adjacency = MeshAdjacency.Build(mesh);

				Assert.That(adjacency.VertexCount, Is.EqualTo(6));
				Assert.That(adjacency.Representative[3], Is.EqualTo(1));
				Assert.That(adjacency.Representative[4], Is.EqualTo(2));

				// 頂点 0 の隣接: 1, 2。頂点 5 の隣接は溶接後の代表 1, 2
				Assert.That(Neighbors(adjacency, 0), Is.EquivalentTo(new[] { 1, 2 }));
				Assert.That(Neighbors(adjacency, 5), Is.EquivalentTo(new[] { 1, 2 }));
				// 溶接された頂点 1(= 3)はシームの両側 0, 2, 5 と繋がる
				Assert.That(Neighbors(adjacency, 1), Is.EquivalentTo(new[] { 0, 2, 5 }));
				Assert.That(Neighbors(adjacency, 3), Is.EquivalentTo(new[] { 0, 2, 5 }));
			}
			finally
			{
				Object.DestroyImmediate(mesh);
			}
		}

		[Test]
		public void MeshAdjacency_EmptyMeshHasNoNeighbors()
		{
			var adjacency = MeshAdjacency.Build(null);
			Assert.That(adjacency.VertexCount, Is.EqualTo(0));
			Assert.That(adjacency.Offsets.Length, Is.EqualTo(1));
			Assert.That(adjacency.Neighbors.Length, Is.EqualTo(0));
		}

		private static int[] Neighbors(MeshAdjacency adjacency, int vertex)
		{
			var count = adjacency.NeighborCount(vertex);
			var result = new int[count];
			System.Array.Copy(adjacency.Neighbors, adjacency.Offsets[vertex], result, 0, count);
			return result;
		}
	}
}
