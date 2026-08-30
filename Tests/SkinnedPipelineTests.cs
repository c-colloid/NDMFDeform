using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// スキン済みメッシュの変形パイプライン検証。
	/// 変形は頂点ごとのスキン行列で「見た目のワールド空間」へ持ち上げて行われるため、
	/// ボーンがバインド後に個別調整されていても(単一のアフィン行列では表せなくても)
	/// ワールドのギズモ位置と変形対象が一致する。
	/// </summary>
	public class SkinnedPipelineTests
	{
		private GameObject _root;
		private Mesh _mesh;
		private Mesh _baked;

		[TearDown]
		public void TearDown()
		{
			if (_root != null) Object.DestroyImmediate(_root);
			if (_mesh != null) Object.DestroyImmediate(_mesh);
			if (_baked != null) Object.DestroyImmediate(_baked);
		}

		[Test]
		public void Bake_PerBoneAdjustedSkinnedMesh_MasksInVisibleSpace()
		{
			_root = new GameObject("SkinnedRoot");

			var bone0 = new GameObject("Bone0").transform;
			bone0.SetParent(_root.transform, false);
			var bone1 = new GameObject("Bone1").transform;
			bone1.SetParent(_root.transform, false);

			var smrGo = new GameObject("Renderer");
			smrGo.transform.SetParent(_root.transform, false);
			var smr = smrGo.AddComponent<SkinnedMeshRenderer>();

			_mesh = new Mesh
			{
				vertices = new[] { new Vector3(-3f, 0f, 0f), new Vector3(2f, 0f, 0f) },
			};
			_mesh.boneWeights = new[]
			{
				new BoneWeight { boneIndex0 = 0, weight0 = 1f },
				new BoneWeight { boneIndex0 = 1, weight0 = 1f },
			};
			// バインド時は両ボーンとも原点(bindpose = identity)
			_mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
			smr.bones = new[] { bone0, bone1 };
			smr.rootBone = bone0;
			smr.sharedMesh = _mesh;

			// バインド後にボーンを個別調整(Modular Avatar のアーマチュア合わせ相当)。
			// 見た目: v0 = (-3,1,0)、v1 = (2,5,0) — 単一アフィンでは表せない
			bone0.position = new Vector3(0f, 1f, 0f);
			bone1.position = new Vector3(0f, 5f, 0f);

			var stack = smrGo.AddComponent<DeformStack>();

			var translateGo = new GameObject("Translate");
			translateGo.transform.SetParent(smrGo.transform, false);
			stack.AddDeformer(translateGo.AddComponent<TestTranslateDeformer>());

			// マスクの軸(ワールドのギズモ位置)は v1 の見た目位置に置く
			var maskGo = new GameObject("Mask");
			maskGo.transform.SetParent(smrGo.transform, false);
			maskGo.transform.position = new Vector3(2f, 5f, 0f);
			var mask = maskGo.AddComponent<SphereMaskDeformer>();
			mask.InnerRadius = 4f; // 実効 2
			mask.OuterRadius = 6f; // 実効 3
			stack.AddDeformer(mask);

			_baked = DeformBakeCore.Bake(stack, _mesh, smrGo.transform);
			Assert.That(_baked, Is.Not.Null);

			// v1: 変形後の見た目 (2,6,0) は軸から距離 1 < 実効内半径 2 → 完全に打ち消され、
			// メッシュ空間では元の頂点のまま
			Assert.That(Vector3.Distance(_baked.vertices[1], new Vector3(2f, 0f, 0f)),
				Is.LessThan(1e-3f), "ギズモ内の頂点は見た目基準で打ち消される");

			// v0: 変形後の見た目 (-3,2,0) は軸から距離 5.83 > 実効外半径 3 → 変形のまま。
			// メッシュ空間では +Y1 された位置になる
			Assert.That(Vector3.Distance(_baked.vertices[0], new Vector3(-3f, 1f, 0f)),
				Is.LessThan(1e-3f), "ギズモ外の頂点は変形のまま");
		}

		/// <summary>全頂点を +Y に 1 動かすテスト用デフォーマ(ワールド空間バッファに対して)</summary>
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
