using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	public class MaskDeformerTests
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

		private (DeformStack stack, GameObject maskGo) CreateSetup(Vector3[] vertices)
		{
			_root = new GameObject("MaskTestRoot");
			var stack = _root.AddComponent<DeformStack>();

			var translateGo = new GameObject("Translate");
			translateGo.transform.SetParent(_root.transform, false);
			stack.AddDeformer(translateGo.AddComponent<TestTranslateDeformer>());

			var maskGo = new GameObject("Mask");
			maskGo.transform.SetParent(_root.transform, false);

			_source = new Mesh { vertices = vertices };
			return (stack, maskGo);
		}

		private static void AssertRestored(Mesh baked, Mesh source, int index)
		{
			Assert.That(Vector3.Distance(baked.vertices[index], source.vertices[index]),
				Is.LessThan(1e-4f), $"vertex {index} should be restored");
		}

		private static void AssertTranslated(Mesh baked, Mesh source, int index)
		{
			Assert.That(Vector3.Distance(baked.vertices[index], source.vertices[index] + Vector3.up),
				Is.LessThan(1e-4f), $"vertex {index} should stay deformed");
		}

		[Test]
		public void SphereMask_RestoresInsideAndFadesOut()
		{
			// ジョブ内半径は指定値の 0.5 倍: inner 2→1, outer 4→2
			var (stack, maskGo) = CreateSetup(new[]
			{
				new Vector3(0.5f, 0f, 0f),   // 内側 → 打ち消し
				new Vector3(3f, 0f, 0f),     // 外側 → 変形のまま
				new Vector3(1.5f, 0f, 0f),   // 中間 → 半分
			});
			var mask = maskGo.AddComponent<SphereMaskDeformer>();
			mask.InnerRadius = 2f;
			mask.OuterRadius = 4f;
			stack.AddDeformer(mask);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			AssertRestored(_baked, _source, 0);
			AssertTranslated(_baked, _source, 1);
			Assert.That(_baked.vertices[2].y, Is.EqualTo(0.5f).Within(1e-4f));

			// invert で内外が反転する
			Object.DestroyImmediate(_baked);
			mask.Invert = true;
			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);
			AssertTranslated(_baked, _source, 0);
			AssertRestored(_baked, _source, 1);
		}

		[Test]
		public void BoxMask_RestoresInsideAndFadesOut()
		{
			var (stack, maskGo) = CreateSetup(new[]
			{
				new Vector3(0.2f, 0f, 0f),   // inner(extents 0.5) の内側 → 打ち消し
				new Vector3(5f, 0f, 0f),     // outer(extents 1) の外側 → 変形のまま
				new Vector3(0.75f, 0f, 0f),  // 中間 → 半分
			});
			var mask = maskGo.AddComponent<BoxMaskDeformer>();
			mask.InnerBounds = new Bounds(Vector3.zero, Vector3.one);      // extents 0.5
			mask.OuterBounds = new Bounds(Vector3.zero, Vector3.one * 2f); // extents 1
			stack.AddDeformer(mask);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			AssertRestored(_baked, _source, 0);
			AssertTranslated(_baked, _source, 1);
			Assert.That(_baked.vertices[2].y, Is.EqualTo(0.5f).Within(1e-4f));
		}

		[Test]
		public void VerticalGradientMask_FadesAlongAxisZ()
		{
			var (stack, maskGo) = CreateSetup(new[]
			{
				new Vector3(0f, 0f, 0f),    // z=0 → exp(0)=1 → 打ち消し
				new Vector3(0f, 0f, 10f),   // z=10, falloff=1 → ほぼ 0 → 変形のまま
			});
			var mask = maskGo.AddComponent<VerticalGradientMaskDeformer>();
			mask.Falloff = 1f;
			stack.AddDeformer(mask);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			AssertRestored(_baked, _source, 0);
			AssertTranslated(_baked, _source, 1);

			Object.DestroyImmediate(_baked);
			mask.Invert = true;
			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);
			AssertTranslated(_baked, _source, 0);
			AssertRestored(_baked, _source, 1);
		}

		[Test]
		public void VertexColorMask_PaintedVerticesAreProtected()
		{
			var (stack, maskGo) = CreateSetup(new[]
			{
				new Vector3(0f, 0f, 0f),
				new Vector3(1f, 0f, 0f),
			});
			// R=1(塗り) → 打ち消し / R=0 → 変形のまま(元実装の実効挙動)
			_source.colors = new[] { new Color(1f, 0f, 0f, 1f), new Color(0f, 0f, 0f, 1f) };
			var mask = maskGo.AddComponent<VertexColorMaskDeformer>();
			mask.Falloff = 10f;
			stack.AddDeformer(mask);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			AssertRestored(_baked, _source, 0);
			AssertTranslated(_baked, _source, 1);
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
