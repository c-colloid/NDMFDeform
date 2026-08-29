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

		private (DeformStack stack, TestTranslateDeformer translate, UVIslandMaskDeformer mask) CreateSetup()
		{
			_root = new GameObject("UVIslandMaskTestRoot");
			var stack = _root.AddComponent<DeformStack>();

			var translateGo = new GameObject("Translate");
			translateGo.transform.SetParent(_root.transform, false);
			var translate = translateGo.AddComponent<TestTranslateDeformer>();

			var maskGo = new GameObject("Mask");
			maskGo.transform.SetParent(_root.transform, false);
			var mask = maskGo.AddComponent<UVIslandMaskDeformer>();

			_source = CreateTwoIslandMesh();
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
				Assert.That(analysis.FindIslandAt(island.Seed), Is.SameAs(island));
		}

		[Test]
		public void Analyze_BorderEdgesFormQuadPerimeter()
		{
			_source = CreateTwoIslandMesh();
			var analysis = UVIslandAnalysis.Analyze(_source);

			// 各クワッドは外周 4 辺のみが境界(対角線は 2 三角形が共有)
			foreach (var island in analysis.Islands)
				Assert.That(island.BorderEdges.Count, Is.EqualTo(4));
		}

		// ---- ベイク統合 ----

		[Test]
		public void Bake_SelectedIslandIsRestored()
		{
			var (stack, translate, mask) = CreateSetup();
			stack.AddDeformer(translate);
			stack.AddDeformer(mask);

			// 島 A を選択(頂点 0-3 の変形が打ち消される)
			mask.IslandSeeds.Add(new Vector2(0.2f, 0.2f));
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
			var (stack, translate, mask) = CreateSetup();
			stack.AddDeformer(translate);
			stack.AddDeformer(mask);

			mask.IslandSeeds.Add(new Vector2(0.2f, 0.2f));
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
			var (stack, translate, mask) = CreateSetup();
			stack.AddDeformer(translate);
			stack.AddDeformer(mask);

			mask.IslandSeeds.Add(new Vector2(0.2f, 0.2f));
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
		public void Bake_NoSelectionLeavesDeformUntouched()
		{
			var (stack, translate, mask) = CreateSetup();
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
			var (stack, translate, mask) = CreateSetup();
			// マスクが先 → スナップショットと同一の頂点をブレンドするだけで変形は残る
			stack.AddDeformer(mask);
			stack.AddDeformer(translate);

			mask.IslandSeeds.Add(new Vector2(0.2f, 0.2f));
			mask.Factor = 1f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			var original = _source.vertices;
			for (var i = 0; i < v.Length; i++)
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
