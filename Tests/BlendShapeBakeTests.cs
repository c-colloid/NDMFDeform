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

			// 非線形補正で中間フレームが挿入されうるため、最終フレームを検証する
			var last = _baked.GetBlendShapeFrameCount(0) - 1;
			Assert.That(_baked.GetBlendShapeFrameWeight(0, last), Is.EqualTo(100f));

			var dv = new Vector3[4];
			_baked.GetBlendShapeFrameVertices(0, last, dv, null, null);

			// 頂点 2: 外→内へ入って 2 倍にスケールされるため、デルタが作り直される
			Assert.That(Vector3.Distance(dv[2], new Vector3(-4f, 0f, 0f)), Is.LessThan(1e-4f));
			// 動かない頂点のデルタは 0 のまま
			Assert.That(dv[0].magnitude, Is.LessThan(1e-4f));
			Assert.That(dv[3].magnitude, Is.LessThan(1e-4f));
		}

		/// <summary>
		/// 円柱スケールの変形は scope 境界で区分的(非線形)なので、
		/// 外→内へ横切るシェイプには 25/50/75% の中間フレームが挿入される。
		/// 50% フレームのデルタは Deform(base + 0.5δ) − Deform(base) に一致する。
		/// </summary>
		[Test]
		public void Bake_NonlinearShapeGetsIntermediateFrames()
		{
			var stack = CreateStack();
			var deformer = AddDeformer<CylindricalScaleDeformer>(stack);
			deformer.Factor = 1f;
			deformer.Radius = 2f;
			deformer.Scope = 1f;

			_source = new Mesh
			{
				vertices = new[] { new Vector3(5f, 0f, 0f) },
			};
			_source.AddBlendShapeFrame("move", 100f, new[] { new Vector3(-4.5f, 0f, 0f) }, null, null);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked.GetBlendShapeFrameCount(0), Is.EqualTo(4));
			Assert.That(_baked.GetBlendShapeFrameWeight(0, 0), Is.EqualTo(25f).Within(1e-3f));
			Assert.That(_baked.GetBlendShapeFrameWeight(0, 1), Is.EqualTo(50f).Within(1e-3f));
			Assert.That(_baked.GetBlendShapeFrameWeight(0, 2), Is.EqualTo(75f).Within(1e-3f));
			Assert.That(_baked.GetBlendShapeFrameWeight(0, 3), Is.EqualTo(100f));

			// 50%: (5,0,0) + 0.5*(-4.5,0,0) = (2.75,0,0) は scope 外 → 変形されず
			// delta = (2.75,0,0) − Deform(base)=(5,0,0) = (−2.25,0,0)
			var dv = new Vector3[1];
			_baked.GetBlendShapeFrameVertices(0, 1, dv, null, null);
			Assert.That(Vector3.Distance(dv[0], new Vector3(-2.25f, 0f, 0f)), Is.LessThan(1e-4f));
		}

		[Test]
		public void Bake_CorrectionDisabledKeepsSingleFrame()
		{
			var stack = CreateStack();
			var deformer = AddDeformer<CylindricalScaleDeformer>(stack);
			deformer.Factor = 1f;
			deformer.Radius = 2f;
			deformer.Scope = 1f;
			stack.NonlinearShapeCorrection = false;

			_source = new Mesh { vertices = new[] { new Vector3(5f, 0f, 0f) } };
			_source.AddBlendShapeFrame("move", 100f, new[] { new Vector3(-4.5f, 0f, 0f) }, null, null);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked.GetBlendShapeFrameCount(0), Is.EqualTo(1));
			var dv = new Vector3[1];
			_baked.GetBlendShapeFrameVertices(0, 0, dv, null, null);
			Assert.That(Vector3.Distance(dv[0], new Vector3(-4f, 0f, 0f)), Is.LessThan(1e-4f));
		}

		/// <summary>線形な変形(平行移動)ではフレームは増えない</summary>
		[Test]
		public void Bake_LinearShapeKeepsSingleFrame()
		{
			var stack = CreateStack();
			AddDeformer<TestTranslateDeformer>(stack);

			_source = new Mesh { vertices = new[] { new Vector3(0f, 0f, 0f) } };
			_source.AddBlendShapeFrame("move", 100f, new[] { new Vector3(0f, 0f, 2f) }, null, null);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked.GetBlendShapeFrameCount(0), Is.EqualTo(1));
		}

		/// <summary>
		/// KeepAuthoredShape 指定のシェイプは 100% で作者の作った形状そのものになる
		/// (デルタを持つ頂点のみ変形を打ち消す。デルタ 0 の頂点は変形されたまま)。
		/// </summary>
		[Test]
		public void Bake_KeepAuthoredShapeReachesAuthoredTarget()
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
				},
			};
			// 頂点 0 を (0.1,0,0) へ細くするシェイプ(頂点 1 はデルタなし)
			_source.AddBlendShapeFrame("thin", 100f,
				new[] { new Vector3(-0.4f, 0f, 0f), Vector3.zero }, null, null);
			stack.BlendShapeOverrides.Add(new DeformStack.BlendShapeOverride
			{
				shapeName = "thin",
				mode = DeformStack.BlendShapeDeltaMode.KeepAuthoredShape,
			});

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked.GetBlendShapeFrameCount(0), Is.EqualTo(1));
			var dv = new Vector3[2];
			_baked.GetBlendShapeFrameVertices(0, 0, dv, null, null);

			// 頂点 0: Deform(base)=(1,0,0) → 100% で作者のターゲット (0.1,0,0) に届く
			Assert.That(Vector3.Distance(dv[0], new Vector3(-0.9f, 0f, 0f)), Is.LessThan(1e-4f));
			// デルタ 0 の頂点は変形されたまま(デルタ 0 のまま)
			Assert.That(dv[1].magnitude, Is.LessThan(1e-4f));
		}

		/// <summary>ShapesToRebake に含まれないシェイプは元のデルタを維持する(プレビュー高速化)</summary>
		[Test]
		public void Bake_ShapeFilterKeepsOriginalDeltas()
		{
			var stack = CreateStack();
			var deformer = AddDeformer<CylindricalScaleDeformer>(stack);
			deformer.Factor = 1f;
			deformer.Radius = 2f;
			deformer.Scope = 1f;

			_source = new Mesh { vertices = new[] { new Vector3(5f, 0f, 0f) } };
			_source.AddBlendShapeFrame("move", 100f, new[] { new Vector3(-4.5f, 0f, 0f) }, null, null);

			var options = new DeformBakeOptions
			{
				RebakeBlendShapes = true,
				ShapesToRebake = new System.Collections.Generic.HashSet<string>(),
			};
			_baked = DeformBakeCore.Bake(stack, _source, _root.transform, options);

			Assert.That(_baked.GetBlendShapeFrameCount(0), Is.EqualTo(1));
			var dv = new Vector3[1];
			_baked.GetBlendShapeFrameVertices(0, 0, dv, null, null);
			Assert.That(Vector3.Distance(dv[0], new Vector3(-4.5f, 0f, 0f)), Is.LessThan(1e-4f));
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
