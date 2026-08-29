using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	public class PreviewBakeCacheTests
	{
		private GameObject _root;
		private Mesh _source;

		[TearDown]
		public void TearDown()
		{
			if (_root != null) Object.DestroyImmediate(_root);
			if (_source != null) Object.DestroyImmediate(_source);
			// スタック破棄後の掃除パスでキャッシュエントリ(常駐メッシュ・NativeArray)を回収する
			DeformPreviewBakeCache.RefreshStaleEntries(0);
		}

		private (DeformStack stack, CylindricalScaleDeformer deformer) CreateSetup()
		{
			_root = new GameObject("PreviewCacheTestRoot");
			var stack = _root.AddComponent<DeformStack>();

			var child = new GameObject("Deformer");
			child.transform.SetParent(_root.transform, false);
			var deformer = child.AddComponent<CylindricalScaleDeformer>();
			deformer.Factor = 1f;
			deformer.Radius = 2f;
			deformer.Scope = 1f;
			stack.AddDeformer(deformer);
			return (stack, deformer);
		}

		private static Vector3 LastFrameDelta(Mesh baked, int vertex)
		{
			var last = baked.GetBlendShapeFrameCount(0) - 1;
			var dv = new Vector3[baked.vertexCount];
			baked.GetBlendShapeFrameVertices(0, last, dv, null, null);
			return dv[vertex];
		}

		[Test]
		public void FastPath_ReusesMeshAndUpdatesVertices()
		{
			var (stack, deformer) = CreateSetup();
			_source = new Mesh { vertices = new[] { new Vector3(0.5f, 0f, 0f) } };

			var first = DeformPreviewBakeCache.Bake(stack, _source, _root.transform, new HashSet<string>());
			Assert.That(first, Is.Not.Null);
			var firstMesh = first.Baked;
			Assert.That(Vector3.Distance(firstMesh.vertices[0], new Vector3(1f, 0f, 0f)), Is.LessThan(1e-4f));

			// パラメータ変更のみ → 同じメッシュインスタンスのまま頂点だけ更新される
			deformer.Radius = 3f;
			var second = DeformPreviewBakeCache.Bake(stack, _source, _root.transform, new HashSet<string>());
			Assert.That(second, Is.SameAs(first));
			Assert.That(second.Baked, Is.SameAs(firstMesh));
			Assert.That(Vector3.Distance(second.Baked.vertices[0], new Vector3(1.5f, 0f, 0f)), Is.LessThan(1e-4f));
		}

		[Test]
		public void ActiveShapeSetChange_TriggersFullBake()
		{
			var (stack, _) = CreateSetup();
			_source = new Mesh { vertices = new[] { new Vector3(5f, 0f, 0f) } };
			_source.AddBlendShapeFrame("move", 100f, new[] { new Vector3(-4.5f, 0f, 0f) }, null, null);

			// 非アクティブ → シェイプは元のデルタのまま
			var inactive = DeformPreviewBakeCache.Bake(stack, _source, _root.transform, new HashSet<string>());
			var inactiveMesh = inactive.Baked;
			Assert.That(Vector3.Distance(LastFrameDelta(inactiveMesh, 0), new Vector3(-4.5f, 0f, 0f)),
				Is.LessThan(1e-4f));

			// アクティブ化 → フルベイクで再ベイクされたデルタになる
			var active = DeformPreviewBakeCache.Bake(stack, _source, _root.transform,
				new HashSet<string> { "move" });
			Assert.That(active.Baked, Is.Not.SameAs(inactiveMesh));
			Assert.That(Vector3.Distance(LastFrameDelta(active.Baked, 0), new Vector3(-4f, 0f, 0f)),
				Is.LessThan(1e-4f));
		}

		[Test]
		public void StaleActiveShapes_RefreshAfterQuietPeriod()
		{
			var (stack, deformer) = CreateSetup();
			_source = new Mesh { vertices = new[] { new Vector3(5f, 0f, 0f) } };
			_source.AddBlendShapeFrame("move", 100f, new[] { new Vector3(-4.5f, 0f, 0f) }, null, null);
			var activeShapes = new HashSet<string> { "move" };

			var entry = DeformPreviewBakeCache.Bake(stack, _source, _root.transform, activeShapes);
			Assert.That(Vector3.Distance(LastFrameDelta(entry.Baked, 0), new Vector3(-4f, 0f, 0f)),
				Is.LessThan(1e-4f));

			// ドラッグ相当: パラメータ変更 → ホットパス。シェイプデルタは一時的に古いまま
			deformer.Radius = 3f;
			entry = DeformPreviewBakeCache.Bake(stack, _source, _root.transform, activeShapes);
			Assert.That(entry.ShapesStale, Is.True);
			Assert.That(Vector3.Distance(LastFrameDelta(entry.Baked, 0), new Vector3(-4f, 0f, 0f)),
				Is.LessThan(1e-4f));

			// 静穏後の追いかけフルベイクで最新のデルタ(radius 3 → −3.5)になる
			DeformPreviewBakeCache.RefreshStaleEntries(entry.LastBakeTime + 10.0);
			entry = DeformPreviewBakeCache.Bake(stack, _source, _root.transform, activeShapes);
			Assert.That(Vector3.Distance(LastFrameDelta(entry.Baked, 0), new Vector3(-3.5f, 0f, 0f)),
				Is.LessThan(1e-4f));
		}
	}
}
