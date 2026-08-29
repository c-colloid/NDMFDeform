using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	public class BlendShapeBakeTests
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

		private DeformStack CreateStack()
		{
			_root = new GameObject("BlendShapeBakeTestRoot");
			return _root.AddComponent<DeformStack>();
		}

		private T AddDeformer<T>(DeformStack stack) where T : DeformerBase
		{
			var go = new GameObject(typeof(T).Name);
			go.transform.SetParent(_root.transform, false);
			var deformer = go.AddComponent<T>();
			stack.AddDeformer(deformer);
			return deformer;
		}

		// ---- ブレンドシェイプ再ベイク ----

		/// <summary>
		/// 円柱スケール(scope=1 の内側 XY を 2 倍)に対して、
		/// 頂点 2 を円柱の外(5,0,0)から内(0.5,0,0)へ動かすシェイプを再ベイクすると
		/// deformedDelta = Deform(base+delta) − Deform(base) = (1,0,0) − (5,0,0) = (−4,0,0) になる。
		/// </summary>
		[Test]
		public void Bake_RebakesBlendShapeDeltas()
		{
			var stack = CreateStack();
			var deformer = AddDeformer<CylindricalScaleDeformer>(stack);
			deformer.Factor = 1f;
			deformer.Radius = 2f;
			deformer.Scope = 1f;

			_source = new Mesh
			{
				vertices = new[]
				{
					new Vector3(0.5f, 0f, 0f),
					new Vector3(0f, 0.5f, 0f),
					new Vector3(5f, 0f, 0f),
					new Vector3(0f, 5f, 0f),
				},
			};
			var deltas = new[]
			{
				Vector3.zero, Vector3.zero, new Vector3(-4.5f, 0f, 0f), Vector3.zero,
			};
			_source.AddBlendShapeFrame("move", 100f, deltas, null, null);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked.blendShapeCount, Is.EqualTo(1));
			Assert.That(_baked.GetBlendShapeName(0), Is.EqualTo("move"));
			Assert.That(_baked.GetBlendShapeFrameWeight(0, 0), Is.EqualTo(100f));

			var dv = new Vector3[4];
			_baked.GetBlendShapeFrameVertices(0, 0, dv, null, null);

			// 頂点 2: 外→内へ入って 2 倍にスケールされるため、デルタが作り直される
			Assert.That(Vector3.Distance(dv[2], new Vector3(-4f, 0f, 0f)), Is.LessThan(1e-4f));
			// 動かない頂点のデルタは 0 のまま
			Assert.That(dv[0].magnitude, Is.LessThan(1e-4f));
			Assert.That(dv[3].magnitude, Is.LessThan(1e-4f));
		}

		[Test]
		public void Bake_PreservesMultiFrameShapes()
		{
			var stack = CreateStack();
			var deformer = AddDeformer<CylindricalScaleDeformer>(stack);
			deformer.Factor = 1f;
			deformer.Radius = 2f;

			_source = new Mesh { vertices = new[] { new Vector3(5f, 0f, 0f) } };
			_source.AddBlendShapeFrame("multi", 50f, new[] { new Vector3(0f, 1f, 0f) }, null, null);
			_source.AddBlendShapeFrame("multi", 100f, new[] { new Vector3(0f, 2f, 0f) }, null, null);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked.blendShapeCount, Is.EqualTo(1));
			Assert.That(_baked.GetBlendShapeFrameCount(0), Is.EqualTo(2));
			Assert.That(_baked.GetBlendShapeFrameWeight(0, 0), Is.EqualTo(50f));
			Assert.That(_baked.GetBlendShapeFrameWeight(0, 1), Is.EqualTo(100f));
		}

		/// <summary>マスクされた島の頂点は、変形の打ち消しとともにシェイプデルタも元のまま残る</summary>
		[Test]
		public void Bake_MaskKeepsBlendShapeDeltaOnMaskedVertices()
		{
			var stack = CreateStack();
			AddDeformer<TestTranslateDeformer>(stack);
			var mask = AddDeformer<UVIslandMaskDeformer>(stack);

			_source = new Mesh
			{
				vertices = new[]
				{
					new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
					new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
				},
				uv = new[]
				{
					new Vector2(0.1f, 0.1f), new Vector2(0.3f, 0.1f),
					new Vector2(0.1f, 0.3f), new Vector2(0.3f, 0.3f),
				},
				triangles = new[] { 0, 2, 1, 1, 2, 3 },
			};
			var deltas = new[]
			{
				new Vector3(0f, 0f, 2f), Vector3.zero, Vector3.zero, Vector3.zero,
			};
			_source.AddBlendShapeFrame("shape", 100f, deltas, null, null);

			mask.SelectedIslands.Add(new IslandSeed(new Vector2(0.2f, 0.2f), 0));
			mask.Factor = 1f;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			// マスクで基本形状は元に戻る
			Assert.That(Vector3.Distance(_baked.vertices[0], _source.vertices[0]), Is.LessThan(1e-4f));

			// マスクされた頂点のシェイプデルタは作者の意図(0,0,2)のまま
			var dv = new Vector3[4];
			_baked.GetBlendShapeFrameVertices(0, 0, dv, null, null);
			Assert.That(Vector3.Distance(dv[0], new Vector3(0f, 0f, 2f)), Is.LessThan(1e-4f));
		}

		// ---- 法線・タンジェント ----

		/// <summary>XY 平面の三角形を X に応じて Z 方向へシアーするメッシュを作る</summary>
		private (DeformStack stack, Mesh mesh) CreateShearSetup()
		{
			var stack = CreateStack();
			AddDeformer<TestShearDeformer>(stack);

			_source = new Mesh
			{
				vertices = new[]
				{
					new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
				},
				normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
				triangles = new[] { 0, 1, 2 },
			};
			return (stack, _source);
		}

		[Test]
		public void Bake_PreserveAuthoredNormalsByDefault()
		{
			var (stack, mesh) = CreateShearSetup();
			Assert.That(stack.Normals, Is.EqualTo(DeformStack.NormalsMode.PreserveAuthored));

			_baked = DeformBakeCore.Bake(stack, mesh, _root.transform);

			// 頂点はシアーされるが、作り込み法線はそのまま
			Assert.That(Vector3.Distance(_baked.vertices[1], new Vector3(1f, 0f, 1f)), Is.LessThan(1e-4f));
			foreach (var n in _baked.normals)
				Assert.That(Vector3.Distance(n, Vector3.forward), Is.LessThan(1e-4f));
		}

		[Test]
		public void Bake_RecalculateNormalsFollowsDeformedShape()
		{
			var (stack, mesh) = CreateShearSetup();
			stack.Normals = DeformStack.NormalsMode.Recalculate;

			_baked = DeformBakeCore.Bake(stack, mesh, _root.transform);

			// シアー後の面 ((0,0,0),(1,0,1),(0,1,0)) の法線は (-1,0,1)/√2
			var expected = new Vector3(-1f, 0f, 1f).normalized;
			foreach (var n in _baked.normals)
				Assert.That(Vector3.Distance(n, expected), Is.LessThan(1e-4f));
		}

		[Test]
		public void Bake_RecalculateModeRebuildsTangents()
		{
			var stack = CreateStack();
			AddDeformer<TestShearDeformer>(stack);
			stack.Normals = DeformStack.NormalsMode.Recalculate;

			_source = new Mesh
			{
				vertices = new[]
				{
					new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
					new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
				},
				normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward },
				uv = new[]
				{
					new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
				},
				triangles = new[] { 0, 2, 1, 1, 2, 3 },
			};

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var tangents = _baked.tangents;
			Assert.That(tangents.Length, Is.EqualTo(4));
			foreach (var t in tangents)
				Assert.That(Mathf.Abs(t.w), Is.EqualTo(1f).Within(1e-4f));
		}

		// ---- テスト用デフォーマ ----

		/// <summary>全頂点を +Y に 1 動かす</summary>
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

		/// <summary>z += x のシアー(法線再計算の検証用に面の向きを変える)</summary>
		private class TestShearDeformer : DeformerBase
		{
			public override DeformDataFlags DataFlags => DeformDataFlags.Vertices;

			public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
			{
				return new ShearJob { vertices = buffers.Vertices }
					.Schedule(buffers.Length, 64, dependency);
			}

			private struct ShearJob : IJobParallelFor
			{
				public NativeArray<float3> vertices;

				public void Execute(int index)
				{
					var v = vertices[index];
					v.z += v.x;
					vertices[index] = v;
				}
			}
		}
	}
}
