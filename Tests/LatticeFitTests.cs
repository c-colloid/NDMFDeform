using MeshModifier.NDMFDeform.Core;
using NUnit.Framework;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// LatticeDeformer.FitToParentStack のワールド空間一致の検証。
	/// フィット結果(位置・回転・ワールドサイズ)は、デフォーマの親の Transform が
	/// スタックとズレていても、レンダラーのバウンズと一致しなければならない。
	/// </summary>
	public class LatticeFitTests
	{
		private GameObject _root;
		private Mesh _mesh;

		[TearDown]
		public void TearDown()
		{
			if (_root != null) Object.DestroyImmediate(_root);
			if (_mesh != null) Object.DestroyImmediate(_mesh);
		}

		/// <summary>bounds center (1,2,3) / size (1,2,3) のメッシュを持つスタックを作る</summary>
		private DeformStack CreateStack(out Bounds meshBounds)
		{
			_root = new GameObject("FitRoot");
			_root.transform.position = new Vector3(1f, 0f, 0f);
			_root.transform.localScale = Vector3.one * 0.8f;

			var stackGo = new GameObject("Stack");
			stackGo.transform.SetParent(_root.transform, false);
			stackGo.transform.localPosition = new Vector3(0f, 1f, 0f);
			stackGo.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);

			_mesh = new Mesh
			{
				vertices = new[]
				{
					new Vector3(0.5f, 1f, 1.5f),
					new Vector3(1.5f, 3f, 4.5f),
				},
			};
			_mesh.RecalculateBounds();
			meshBounds = _mesh.bounds;

			stackGo.AddComponent<MeshFilter>().sharedMesh = _mesh;
			stackGo.AddComponent<MeshRenderer>();
			return stackGo.AddComponent<DeformStack>();
		}

		private static void AssertFitsRendererBounds(LatticeDeformer lattice, DeformStack stack, Bounds meshBounds)
		{
			var stackTransform = stack.transform;
			var expectedCenter = stackTransform.TransformPoint(meshBounds.center);
			var expectedSize = Vector3.Scale(stackTransform.lossyScale, meshBounds.size);

			Assert.That(Vector3.Distance(lattice.transform.position, expectedCenter), Is.LessThan(1e-4f),
				"フィット位置がレンダラーバウンズの中心と一致する");
			Assert.That(Quaternion.Angle(lattice.transform.rotation, stackTransform.rotation), Is.LessThan(0.01f),
				"フィット回転がスタックの回転と一致する");
			var lossy = lattice.transform.lossyScale;
			Assert.That(Vector3.Distance(lossy, expectedSize), Is.LessThan(1e-4f),
				$"ワールドサイズがレンダラーバウンズと一致する (actual {lossy}, expected {expectedSize})");
		}

		[Test]
		public void Fit_DirectChildOfStack_MatchesRendererBounds()
		{
			var stack = CreateStack(out var meshBounds);
			var latticeGo = new GameObject("Lattice");
			latticeGo.transform.SetParent(stack.transform, false);

			var lattice = latticeGo.AddComponent<LatticeDeformer>();
			lattice.FitToParentStack();

			AssertFitsRendererBounds(lattice, stack, meshBounds);
		}

		[Test]
		public void Fit_UnderOffsetRotatedScaledParent_MatchesRendererBounds()
		{
			// 親のTransformがズレている(オフセット + 回転 + スケール)場合の回帰テスト:
			// 旧実装は localScale にメッシュ空間サイズを直接入れていたため、
			// 親のスケールが掛かって箱がズレていた
			var stack = CreateStack(out var meshBounds);

			var mid = new GameObject("OffsetParent");
			mid.transform.SetParent(stack.transform, false);
			mid.transform.localPosition = new Vector3(0.3f, -0.2f, 0.1f);
			mid.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
			mid.transform.localScale = Vector3.one * 2f;

			var latticeGo = new GameObject("Lattice");
			latticeGo.transform.SetParent(mid.transform, false);

			var lattice = latticeGo.AddComponent<LatticeDeformer>();
			lattice.FitToParentStack();

			AssertFitsRendererBounds(lattice, stack, meshBounds);
		}
	}
}
