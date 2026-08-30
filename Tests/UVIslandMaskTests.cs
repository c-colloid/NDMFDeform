using System.Collections.Generic;
using System.Reflection;
using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	public class UVIslandMaskTests
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

		/// <summary>
		/// UV 島が 2 つある 8 頂点メッシュ。
		/// 島 A = 頂点 0-3(UV [0.1,0.3]²)、島 B = 頂点 4-7(UV [0.6,0.8]²)。
		/// </summary>
		private static Mesh CreateTwoIslandMesh()
		{
			return new Mesh
			{
				vertices = new[]
				{
					new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
					new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
					new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f),
					new Vector3(2f, 1f, 0f), new Vector3(3f, 1f, 0f),
				},
				uv = new[]
				{
					new Vector2(0.1f, 0.1f), new Vector2(0.3f, 0.1f),
					new Vector2(0.1f, 0.3f), new Vector2(0.3f, 0.3f),
					new Vector2(0.6f, 0.1f), new Vector2(0.8f, 0.1f),
					new Vector2(0.6f, 0.3f), new Vector2(0.8f, 0.3f),
				},
				triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 },
			};
		}

		/// <summary>
		/// 2 サブメッシュが同じ UV 範囲([0.1,0.3]²)に重なるメッシュ。
		/// サブメッシュ 0 = 頂点 0-3、サブメッシュ 1 = 頂点 4-7。
		/// </summary>
		private static Mesh CreateOverlappingSubMeshMesh()
		{
			var mesh = new Mesh
			{
				vertices = new[]
				{
					new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
					new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
					new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f),
					new Vector3(2f, 1f, 0f), new Vector3(3f, 1f, 0f),
				},
				uv = new[]
				{
					new Vector2(0.1f, 0.1f), new Vector2(0.3f, 0.1f),
					new Vector2(0.1f, 0.3f), new Vector2(0.3f, 0.3f),
					new Vector2(0.1f, 0.1f), new Vector2(0.3f, 0.1f),
					new Vector2(0.1f, 0.3f), new Vector2(0.3f, 0.3f),
				},
			};
			mesh.subMeshCount = 2;
			mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
			mesh.SetTriangles(new[] { 4, 6, 5, 5, 6, 7 }, 1);
			return mesh;
		}

		private (DeformStack stack, TestTranslateDeformer translate, UVIslandMaskDeformer mask) CreateSetup(
			Mesh source)
		{
			_root = new GameObject("UVIslandMaskTestRoot");
			var stack = _root.AddComponent<DeformStack>();

			var translateGo = new GameObject("Translate");
			translateGo.transform.SetParent(_root.transform, false);
			var translate = translateGo.AddComponent<TestTranslateDeformer>();

			var maskGo = new GameObject("Mask");
			maskGo.transform.SetParent(_root.transform, false);
			var mask = maskGo.AddComponent<UVIslandMaskDeformer>();

			_source = source;
			return (stack, translate, mask);
		}

		// ---- 解析 ----

		[Test]
		public void Analyze_FindsTwoIslands()
		{
			_source = CreateTwoIslandMesh();
			var analysis = UVIslandAnalysis.Analyze(_source);

			Assert.That(analysis.Islands.Count, Is.EqualTo(2));
			Assert.That(analysis.VertexCount, Is.EqualTo(8));

			var islandA = analysis.FindIslandAt(new Vector2(0.2f, 0.2f));
			Assert.That(islandA, Is.Not.Null);
			Assert.That(islandA.Vertices, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));

			var islandB = analysis.FindIslandAt(new Vector2(0.7f, 0.2f));
			Assert.That(islandB, Is.Not.Null);
			Assert.That(islandB.Vertices, Is.EquivalentTo(new[] { 4, 5, 6, 7 }));
			Assert.That(islandB, Is.Not.SameAs(islandA));
		}

		[Test]
		public void FindIslandAt_ReturnsNullFarFromIslands()
		{
			_source = CreateTwoIslandMesh();
			var analysis = UVIslandAnalysis.Analyze(_source);

			Assert.That(analysis.FindIslandAt(new Vector2(0.45f, 0.9f)), Is.Null);
		}

		[Test]
		public void Analyze_SeedResolvesToOwnIsland()
		{
			_source = CreateTwoIslandMesh();
			var analysis = UVIslandAnalysis.Analyze(_source);

			foreach (var island in analysis.Islands)
				Assert.That(analysis.FindIslandAt(island.Seed, island.SubMesh), Is.SameAs(island));
		}

		[Test]
		public void Analyze_BorderEdgesFormQuadPerimeter()
		{
			_source = CreateTwoIslandMesh();
			var analysis = UVIslandAnalysis.Analyze(_source);

			// 各クワッドは外周 4 辺のみが境界(対角線は 2 三角形が共有)
			foreach (var island in analysis.Islands)
			{
				Assert.That(island.BorderEdges.Count, Is.EqualTo(4));
				Assert.That(island.BorderEdgeVerts.Count, Is.EqualTo(8));
			}
		}

		[Test]
		public void Analyze_MapsTrianglesToIslands()
		{
			_source = CreateTwoIslandMesh();
			var analysis = UVIslandAnalysis.Analyze(_source);

			Assert.That(analysis.IslandOfTriangle, Is.Not.Null);
			Assert.That(analysis.IslandOfTriangle.Length, Is.EqualTo(4));

			var islandA = analysis.FindIslandAt(new Vector2(0.2f, 0.2f));
			var islandB = analysis.FindIslandAt(new Vector2(0.7f, 0.2f));
			Assert.That(analysis.IslandOfTriangle[0], Is.SameAs(islandA));
			Assert.That(analysis.IslandOfTriangle[1], Is.SameAs(islandA));
			Assert.That(analysis.IslandOfTriangle[2], Is.SameAs(islandB));
			Assert.That(analysis.IslandOfTriangle[3], Is.SameAs(islandB));
		}

		[Test]
		public void Analyze_SeparatesOverlappingSubMeshIslands()
		{
			_source = CreateOverlappingSubMeshMesh();
			var analysis = UVIslandAnalysis.Analyze(_source);

			// UV が完全に重なっていてもサブメッシュ単位で別の島になる
			Assert.That(analysis.Islands.Count, Is.EqualTo(2));
			Assert.That(analysis.SubMeshCount, Is.EqualTo(2));

			var island0 = analysis.FindIslandAt(new Vector2(0.2f, 0.2f), 0);
			var island1 = analysis.FindIslandAt(new Vector2(0.2f, 0.2f), 1);
			Assert.That(island0, Is.Not.Null);
			Assert.That(island1, Is.Not.Null);
			Assert.That(island0.SubMesh, Is.EqualTo(0));
			Assert.That(island1.SubMesh, Is.EqualTo(1));
			Assert.That(island0.Vertices, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
			Assert.That(island1.Vertices, Is.EquivalentTo(new[] { 4, 5, 6, 7 }));
		}

		// ---- UV 重なりの選択 ----

		/// <summary>
		/// 同一サブメッシュ内で UV が入れ子に重なるメッシュ。
		/// 島 A = 頂点 0-3(UV [0.1,0.5]²)、島 B = 頂点 4-7(UV [0.2,0.3]²。A の内側)。
		/// 両島とも最初の三角形の重心(= シード)が (0.2333, 0.2333) で一致するため、
		/// シードの index による重なり解決の検証になる。
		/// </summary>
		private static Mesh CreateNestedOverlapMesh()
		{
			return new Mesh
			{
				vertices = new[]
				{
					new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
					new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
					new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f),
					new Vector3(2f, 1f, 0f), new Vector3(3f, 1f, 0f),
				},
				uv = new[]
				{
					new Vector2(0.1f, 0.1f), new Vector2(0.5f, 0.1f),
					new Vector2(0.1f, 0.5f), new Vector2(0.5f, 0.5f),
					new Vector2(0.2f, 0.2f), new Vector2(0.3f, 0.2f),
					new Vector2(0.2f, 0.3f), new Vector2(0.3f, 0.3f),
				},
				triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 },
			};
		}

		[Test]
		public void FindIslandsAt_ReturnsAllOverlappingIslands()
		{
			_source = CreateNestedOverlapMesh();
			var analysis = UVIslandAnalysis.Analyze(_source);
			Assert.That(analysis.Islands.Count, Is.EqualTo(2));

			// 重なった領域では両方の島が候補になる
			var hits = new List<UVIslandAnalysis.Island>();
			analysis.FindIslandsAt(new Vector2(0.25f, 0.25f), -1, hits);
			Assert.That(hits.Count, Is.EqualTo(2));

			// 外側の島だけの領域では 1 つ
			var outer = new List<UVIslandAnalysis.Island>();
			analysis.FindIslandsAt(new Vector2(0.45f, 0.45f), -1, outer);
			Assert.That(outer.Count, Is.EqualTo(1));
			Assert.That(outer[0].Vertices, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
		}

		[Test]
		public void SeedRoundtrip_DisambiguatesOverlappingIslands()
		{
			_source = CreateNestedOverlapMesh();
			var analysis = UVIslandAnalysis.Analyze(_source);

			// シード UV が完全に一致していても index で自分の島へ戻る
			foreach (var island in analysis.Islands)
			{
				var seed = analysis.MakeSeed(island);
				Assert.That(analysis.ResolveSeed(seed), Is.SameAs(island), $"island {island.Id}");
			}
		}

		[Test]
		public void CollectIslandsInRect_SelectsIslandsWithVerticesInRect()
		{
			_source = CreateNestedOverlapMesh();
			var analysis = UVIslandAnalysis.Analyze(_source);

			// 内側の島の頂点だけを囲む矩形 → 内側のみ
			var inner = new List<UVIslandAnalysis.Island>();
			analysis.CollectIslandsInRect(
				new Vector2(0.15f, 0.15f), new Vector2(0.35f, 0.35f), -1, inner);
			Assert.That(inner.Count, Is.EqualTo(1));
			Assert.That(inner[0].Vertices, Is.EquivalentTo(new[] { 4, 5, 6, 7 }));

			// 全体を囲む矩形 → 両方
			var all = new List<UVIslandAnalysis.Island>();
			analysis.CollectIslandsInRect(
				new Vector2(0.05f, 0.05f), new Vector2(0.55f, 0.55f), -1, all);
			Assert.That(all.Count, Is.EqualTo(2));
		}

		[Test]
		public void Bake_OverlappedInnerIslandMasksOnlyItself()
		{
			// UV が重なった内側の島だけを選択できる(従来の UV 保存形式では
			// 常に外側の島へ解決されてしまい不可能だったケース)
			var (stack, translate, mask) = CreateSetup(CreateNestedOverlapMesh());
			stack.AddDeformer(translate);
			stack.AddDeformer(mask);

			var analysis = mask.GetOrCreateAnalysis(_source);
			var innerIsland = analysis.Islands[1];
			Assert.That(innerIsland.Vertices, Is.EquivalentTo(new[] { 4, 5, 6, 7 }));

			mask.SelectedIslands.Add(analysis.MakeSeed(innerIsland));
			mask.Factor = 1f;
			mask.Falloff = 0f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			var original = _source.vertices;
			for (var i = 0; i < 4; i++)
				Assert.That(Vector3.Distance(v[i], original[i] + Vector3.up), Is.LessThan(1e-4f), $"vertex {i}");
			for (var i = 4; i < 8; i++)
				Assert.That(Vector3.Distance(v[i], original[i]), Is.LessThan(1e-4f), $"vertex {i}");
		}

		// ---- ベイク統合 ----

		[Test]
		public void Bake_SelectedIslandIsRestored()
		{
			var (stack, translate, mask) = CreateSetup(CreateTwoIslandMesh());
			stack.AddDeformer(translate);
			stack.AddDeformer(mask);

			// 島 A を選択(頂点 0-3 の変形が打ち消される)
			mask.SelectedIslands.Add(new IslandSeed(new Vector2(0.2f, 0.2f), 0));
			mask.Factor = 1f;
			mask.Falloff = 0f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked, Is.Not.Null);
			var v = _baked.vertices;
			var original = _source.vertices;
			for (var i = 0; i < 4; i++)
				Assert.That(Vector3.Distance(v[i], original[i]), Is.LessThan(1e-4f), $"vertex {i}");
			for (var i = 4; i < 8; i++)
				Assert.That(Vector3.Distance(v[i], original[i] + Vector3.up), Is.LessThan(1e-4f), $"vertex {i}");
		}

		[Test]
		public void Bake_InvertKeepsDeformOnlyOnSelectedIsland()
		{
			var (stack, translate, mask) = CreateSetup(CreateTwoIslandMesh());
			stack.AddDeformer(translate);
			stack.AddDeformer(mask);

			mask.SelectedIslands.Add(new IslandSeed(new Vector2(0.2f, 0.2f), 0));
			mask.Factor = 1f;
			mask.Falloff = 0f;
			mask.Invert = true;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			var original = _source.vertices;
			for (var i = 0; i < 4; i++)
				Assert.That(Vector3.Distance(v[i], original[i] + Vector3.up), Is.LessThan(1e-4f), $"vertex {i}");
			for (var i = 4; i < 8; i++)
				Assert.That(Vector3.Distance(v[i], original[i]), Is.LessThan(1e-4f), $"vertex {i}");
		}

		[Test]
		public void Bake_FalloffBlendsOutsideVertices()
		{
			var (stack, translate, mask) = CreateSetup(CreateTwoIslandMesh());
			stack.AddDeformer(translate);
			stack.AddDeformer(mask);

			mask.SelectedIslands.Add(new IslandSeed(new Vector2(0.2f, 0.2f), 0));
			mask.Factor = 1f;
			mask.Falloff = 0.4f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			var original = _source.vertices;

			// 頂点 4(UV 0.6,0.1)から島 A の右辺(x=0.3)までの UV 距離は 0.3
			// → マスク強度 = 1 - 0.3/0.4 = 0.25 → 残る変形 = 0.75
			Assert.That(v[4].y - original[4].y, Is.EqualTo(0.75f).Within(1e-4f));

			// 頂点 5(UV 0.8,0.1)は距離 0.5 > falloff → 変形はそのまま残る
			Assert.That(v[5].y - original[5].y, Is.EqualTo(1f).Within(1e-4f));

			// 島 A 内は完全に打ち消される
			Assert.That(Vector3.Distance(v[0], original[0]), Is.LessThan(1e-4f));
		}

		[Test]
		public void Bake_FalloffAfterZeroFalloffBakeStillBlends()
		{
			// 距離キャッシュが falloff=0 の内外フラグのみで止まったまま
			// falloff>0 のベイクに再利用されないことを確認する
			var (stack, translate, mask) = CreateSetup(CreateTwoIslandMesh());
			stack.AddDeformer(translate);
			stack.AddDeformer(mask);

			mask.SelectedIslands.Add(new IslandSeed(new Vector2(0.2f, 0.2f), 0));
			mask.Factor = 1f;
			mask.Falloff = 0f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);
			Object.DestroyImmediate(_baked);

			mask.Falloff = 0.4f;
			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			var original = _source.vertices;
			Assert.That(v[4].y - original[4].y, Is.EqualTo(0.75f).Within(1e-4f));
		}

		[Test]
		public void Bake_SubMeshSeedMasksOnlyItsIsland()
		{
			var (stack, translate, mask) = CreateSetup(CreateOverlappingSubMeshMesh());
			stack.AddDeformer(translate);
			stack.AddDeformer(mask);

			// UV が重なっていてもサブメッシュ 1 の島だけが打ち消される
			mask.SelectedIslands.Add(new IslandSeed(new Vector2(0.2f, 0.2f), 1));
			mask.Factor = 1f;
			mask.Falloff = 0f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			var original = _source.vertices;
			for (var i = 0; i < 4; i++)
				Assert.That(Vector3.Distance(v[i], original[i] + Vector3.up), Is.LessThan(1e-4f), $"vertex {i}");
			for (var i = 4; i < 8; i++)
				Assert.That(Vector3.Distance(v[i], original[i]), Is.LessThan(1e-4f), $"vertex {i}");
		}

		[Test]
		public void Bake_NoSelectionLeavesDeformUntouched()
		{
			var (stack, translate, mask) = CreateSetup(CreateTwoIslandMesh());
			stack.AddDeformer(translate);
			stack.AddDeformer(mask);
			mask.Factor = 1f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			var original = _source.vertices;
			for (var i = 0; i < v.Length; i++)
				Assert.That(Vector3.Distance(v[i], original[i] + Vector3.up), Is.LessThan(1e-4f), $"vertex {i}");
		}

		[Test]
		public void Bake_MaskBeforeDeformerHasNoEffect()
		{
			var (stack, translate, mask) = CreateSetup(CreateTwoIslandMesh());
			// マスクが先 → スナップショットと同一の頂点をブレンドするだけで変形は残る
			stack.AddDeformer(mask);
			stack.AddDeformer(translate);

			mask.SelectedIslands.Add(new IslandSeed(new Vector2(0.2f, 0.2f), 0));
			mask.Factor = 1f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			var original = _source.vertices;
			for (var i = 0; i < v.Length; i++)
				Assert.That(Vector3.Distance(v[i], original[i] + Vector3.up), Is.LessThan(1e-4f), $"vertex {i}");
		}

		[Test]
		public void LegacySeeds_MigrateOnValidateAndStillMask()
		{
			var (stack, translate, mask) = CreateSetup(CreateTwoIslandMesh());
			stack.AddDeformer(translate);
			stack.AddDeformer(mask);
			mask.Factor = 1f;

			// 旧形式(Vector2 リスト)をリフレクションで注入し、OnValidate で移行させる
			var legacyField = typeof(UVIslandMaskDeformer).GetField(
				"islandSeeds", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(legacyField, Is.Not.Null);
			legacyField.SetValue(mask,
				new System.Collections.Generic.List<Vector2> { new Vector2(0.2f, 0.2f) });
			typeof(UVIslandMaskDeformer)
				.GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)
				.Invoke(mask, null);

			Assert.That(mask.SelectedIslands.Count, Is.EqualTo(1));
			Assert.That(mask.SelectedIslands[0].subMesh, Is.EqualTo(-1));

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			var original = _source.vertices;
			for (var i = 0; i < 4; i++)
				Assert.That(Vector3.Distance(v[i], original[i]), Is.LessThan(1e-4f), $"vertex {i}");
			for (var i = 4; i < 8; i++)
				Assert.That(Vector3.Distance(v[i], original[i] + Vector3.up), Is.LessThan(1e-4f), $"vertex {i}");
		}

		/// <summary>全頂点を +Y に 1 動かすテスト用デフォーマ</summary>
		private class TestTranslateDeformer : DeformerBase
		{
			public override DeformDataFlags DataFlags => DeformDataFlags.Vertices;

			public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
			{
				return new TranslateJob { vertices = buffers.Vertices }
					.Schedule(buffers.Length, 64, dependency);
			}

			private struct TranslateJob : IJobParallelFor
			{
				public NativeArray<float3> vertices;

				public void Execute(int index)
				{
					vertices[index] += new float3(0f, 1f, 0f);
				}
			}
		}
	}
}
