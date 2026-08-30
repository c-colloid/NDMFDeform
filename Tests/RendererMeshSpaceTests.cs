using MeshModifier.NDMFDeform.Core;
using NUnit.Framework;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// RendererMeshSpace: SMR のメッシュ空間→ワールドはボーン×バインドポーズ基準で、
	/// レンダラー GameObject の Transform 変更(見た目に影響しない)に左右されないこと。
	/// </summary>
	public class RendererMeshSpaceTests
	{
		private GameObject _root;
		private Mesh _mesh;

		[TearDown]
		public void TearDown()
		{
			if (_root != null) Object.DestroyImmediate(_root);
			if (_mesh != null) Object.DestroyImmediate(_mesh);
		}

		private static void AssertMatrixApprox(Matrix4x4 actual, Matrix4x4 expected, string message)
		{
			for (var i = 0; i < 16; i++)
				Assert.That(actual[i], Is.EqualTo(expected[i]).Within(1e-4f), $"{message} (element {i})");
		}

		/// <summary>
		/// ボーン付き SMR を作る。バインド時のレンダラー行列 = identity として
		/// bindpose を作るため、メッシュ空間→ワールドは identity になるのが正。
		/// </summary>
		private SkinnedMeshRenderer CreateSkinnedSetup(out GameObject smrGo)
		{
			_root = new GameObject("SmrRoot");

			var bone = new GameObject("Bone");
			bone.transform.SetParent(_root.transform, false);
			bone.transform.localPosition = new Vector3(0f, 1f, 0f);
			bone.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);

			smrGo = new GameObject("Renderer");
			smrGo.transform.SetParent(_root.transform, false);
			var smr = smrGo.AddComponent<SkinnedMeshRenderer>();

			_mesh = new Mesh
			{
				vertices = new[]
				{
					new Vector3(0.5f, 1f, 1.5f),
					new Vector3(1.5f, 3f, 4.5f),
				},
			};
			_mesh.RecalculateBounds();
			// BakeMesh がスキンを反映できるよう、ウェイトまで揃えた完全なスキン設定にする
			_mesh.boneWeights = new[]
			{
				new BoneWeight { boneIndex0 = 0, weight0 = 1f },
				new BoneWeight { boneIndex0 = 0, weight0 = 1f },
			};
			_mesh.bindposes = new[] { bone.transform.worldToLocalMatrix };
			smr.bones = new[] { bone.transform };
			smr.rootBone = bone.transform;
			smr.sharedMesh = _mesh;
			return smr;
		}

		[Test]
		public void MeshToWorld_SkinnedMesh_IgnoresRendererTransformChanges()
		{
			var smr = CreateSkinnedSetup(out var smrGo);

			AssertMatrixApprox(RendererMeshSpace.GetMeshToWorld(smr.transform), Matrix4x4.identity,
				"バインド時の写像(identity)と一致する");

			// レンダラー GameObject の Transform をバインド後に大きくズラしても
			// スキン結果は動かないため、写像も変わらないのが正しい
			smrGo.transform.localPosition = new Vector3(5f, -2f, 3f);
			smrGo.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
			smrGo.transform.localScale = Vector3.one * 0.5f;

			AssertMatrixApprox(RendererMeshSpace.GetMeshToWorld(smr.transform), Matrix4x4.identity,
				"レンダラー Transform 変更後も写像が不変");
		}

		[Test]
		public void MeshToWorld_SkinnedMesh_FollowsBoneMovement()
		{
			var smr = CreateSkinnedSetup(out _);
			var bone = smr.rootBone;

			// ボーンを平行移動するとスキン結果も動くため、写像も追従する
			bone.position += new Vector3(0f, 0f, 2f);

			var m = RendererMeshSpace.GetMeshToWorld(smr.transform);
			var expected = Matrix4x4.Translate(new Vector3(0f, 0f, 2f));
			AssertMatrixApprox(m, expected, "ボーンの移動に写像が追従する");
		}

		[Test]
		public void Fit_SkinnedMesh_UsesBindSpaceNotRendererTransform()
		{
			var smr = CreateSkinnedSetup(out var smrGo);
			var stack = smrGo.AddComponent<DeformStack>();
			var meshBounds = _mesh.bounds;

			// レンダラー(=スタック)の Transform をバインド後にズラす
			smrGo.transform.position = new Vector3(3f, 4f, 5f);
			smrGo.transform.localScale = Vector3.one * 0.5f;

			var latticeGo = new GameObject("Lattice");
			latticeGo.transform.SetParent(smrGo.transform, false);
			var lattice = latticeGo.AddComponent<LatticeDeformer>();
			lattice.FitToParentStack();

			// 見た目のメッシュ(バインド空間 = identity 写像)に一致し、
			// ズラしたレンダラー Transform の影響を受けない
			Assert.That(Vector3.Distance(lattice.transform.position, meshBounds.center), Is.LessThan(1e-4f),
				"フィット位置がスキン結果のバウンズ中心と一致する");
			Assert.That(Quaternion.Angle(lattice.transform.rotation, Quaternion.identity), Is.LessThan(0.01f),
				"フィット回転がバインド空間と一致する");
			Assert.That(Vector3.Distance(lattice.transform.lossyScale, meshBounds.size), Is.LessThan(1e-4f),
				"ワールドサイズがスキン結果のバウンズと一致する");

			Assert.That(stack, Is.Not.Null); // スタック経由でフィットしたことの明示
		}
	}
}
