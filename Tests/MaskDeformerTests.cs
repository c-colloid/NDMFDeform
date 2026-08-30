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
			// 領域判定は変形後(+Y 1 移動後)の位置に対して行われる。
			// ジョブ内半径は指定値の 0.5 倍: inner 4→2, outer 8→4
			var (stack, maskGo) = CreateSetup(new[]
			{
				new Vector3(0f, 0f, 0f),             // 移動後 dist=1 < 2 → 打ち消し
				new Vector3(10f, 0f, 0f),            // 移動後 dist>4 → 変形のまま
				new Vector3(Mathf.Sqrt(8f), 0f, 0f), // 移動後 dist=3 → 半分
			});
			var mask = maskGo.AddComponent<SphereMaskDeformer>();
			mask.InnerRadius = 4f;
			mask.OuterRadius = 8f;
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
			// 領域判定は変形後(+Y 1 移動後)の位置に対して行われるため、
			// Y 方向に余裕のあるバウンズにする(inner extents (0.5,2,0.5) / outer (1,3,1))
			var (stack, maskGo) = CreateSetup(new[]
			{
				new Vector3(0.2f, 0f, 0f),   // 移動後も inner 内 → 打ち消し
				new Vector3(5f, 0f, 0f),     // outer の外 → 変形のまま
				new Vector3(0.75f, 0f, 0f),  // 中間 → 半分
			});
			var mask = maskGo.AddComponent<BoxMaskDeformer>();
			mask.InnerBounds = new Bounds(Vector3.zero, new Vector3(1f, 4f, 1f));
			mask.OuterBounds = new Bounds(Vector3.zero, new Vector3(2f, 6f, 2f));
			stack.AddDeformer(mask);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			AssertRestored(_baked, _source, 0);
			AssertTranslated(_baked, _source, 1);
			Assert.That(_baked.vertices[2].y, Is.EqualTo(0.5f).Within(1e-4f));
		}

		[Test]
		public void BoxMask_FalloffIsContinuousAcrossSeams()
		{
			// 段差バグの回帰テスト: 内側バウンズが外側バウンズ内で大きく偏心した構成
			// (実プロジェクトで段差が出た値)で、減衰帯を横切る直線上の重みが
			// 隣接サンプル間でジャンプしないこと。
			// 旧実装(表面投影点間の距離比)は x≈-0.57 付近で約 0.23 ジャンプしていた。
			const int count = 81;
			const float startX = -0.75f;
			const float stepX = 0.005f;
			var vertices = new NativeArray<float3>(count, Allocator.TempJob);
			var original = new NativeArray<float3>(count, Allocator.TempJob);
			try
			{
				for (var i = 0; i < count; i++)
				{
					var p = new float3(startX + stepX * i, -0.22f, 0f);
					vertices[i] = p;
					// 復元先を +X に 1 ずらしておくと、出力の x 増分がそのまま重みになる
					original[i] = p + new float3(1f, 0f, 0f);
				}

				new BoxMaskDeformer.BoxMaskJob
				{
					factor = 1f,
					innerCenter = new float3(0.0552f, -0.0523f, 0f),
					innerExtents = new float3(0.1948f, 0.1978f, 0.25f),
					outerCenter = new float3(-0.1732f, 0f, 0f),
					outerExtents = new float3(0.6732f, 0.5f, 0.5f),
					invert = 0,
					meshToAxis = float4x4.identity,
					vertices = vertices,
					original = original,
				}.Run(count);

				var previous = vertices[0].x - startX;
				for (var i = 1; i < count; i++)
				{
					var weight = vertices[i].x - (startX + stepX * i);
					Assert.That(Mathf.Abs(weight - previous), Is.LessThan(0.05f),
						$"sample {i} (x={startX + stepX * i:F3}) で重みがジャンプしています");
					previous = weight;
				}
			}
			finally
			{
				vertices.Dispose();
				original.Dispose();
			}
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
