using System.Text.RegularExpressions;
using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>GameObject メニューの作成ロジック(NdmfDeformObjectFactory)の検証</summary>
	public class GameObjectFactoryTests
	{
		private GameObject _root;

		[TearDown]
		public void TearDown()
		{
			if (_root != null) Object.DestroyImmediate(_root);
		}

		private GameObject CreateRendererObject()
		{
			_root = new GameObject("FactoryTestRoot");
			_root.AddComponent<MeshFilter>();
			_root.AddComponent<MeshRenderer>();
			return _root;
		}

		[Test]
		public void AddStack_AddsOnceAndReturnsExisting()
		{
			var go = CreateRendererObject();
			var first = NdmfDeformObjectFactory.AddStack(go);
			var second = NdmfDeformObjectFactory.AddStack(go);

			Assert.That(first, Is.Not.Null);
			Assert.That(second, Is.SameAs(first));
			Assert.That(go.GetComponents<DeformStack>(), Has.Length.EqualTo(1));
		}

		[Test]
		public void AddStack_WithoutRenderer_WarnsAndReturnsNull()
		{
			_root = new GameObject("NoRenderer");
			LogAssert.Expect(LogType.Warning, new Regex("Deform Stack"));
			Assert.That(NdmfDeformObjectFactory.AddStack(_root), Is.Null);
		}

		[Test]
		public void CreateDeformer_AutoCreatesStackAndRegisters()
		{
			var go = CreateRendererObject();
			var deformer = NdmfDeformObjectFactory.CreateDeformer(
				go, typeof(SphereMaskDeformer), "Sphere Mask");

			Assert.That(deformer, Is.Not.Null);
			Assert.That(deformer, Is.TypeOf<SphereMaskDeformer>());
			Assert.That(deformer.transform.parent, Is.SameAs(go.transform));

			var stack = go.GetComponent<DeformStack>();
			Assert.That(stack, Is.Not.Null, "スタックが自動追加される");
			Assert.That(stack.Deformers, Has.Count.EqualTo(1));
			Assert.That(stack.Deformers[0].deformer, Is.SameAs(deformer));
			Assert.That(stack.Deformers[0].enabled, Is.True);
		}

		[Test]
		public void CreateDeformer_RegistersToAncestorStack()
		{
			var go = CreateRendererObject();
			var stack = go.AddComponent<DeformStack>();
			var child = new GameObject("Child");
			child.transform.SetParent(go.transform, false);

			var deformer = NdmfDeformObjectFactory.CreateDeformer(
				child, typeof(BoxMaskDeformer), "Box Mask");

			Assert.That(deformer, Is.Not.Null);
			Assert.That(deformer.transform.parent, Is.SameAs(child.transform));
			Assert.That(child.GetComponent<DeformStack>(), Is.Null, "子には新しいスタックを作らない");
			Assert.That(stack.Deformers, Has.Count.EqualTo(1));
			Assert.That(stack.Deformers[0].deformer, Is.SameAs(deformer));
		}

		[Test]
		public void CreateDeformer_WithoutStackOrRenderer_WarnsAndReturnsNull()
		{
			_root = new GameObject("NoRenderer");
			LogAssert.Expect(LogType.Warning, new Regex("Deform Stack"));
			Assert.That(
				NdmfDeformObjectFactory.CreateDeformer(_root, typeof(SphereMaskDeformer), "Sphere Mask"),
				Is.Null);
		}

		[Test]
		public void CreateDeformer_LatticeResetRunsSafely()
		{
			// AddComponent 時の Reset(FitToParentStack)がメッシュ未設定でも安全に通ること
			var go = CreateRendererObject();
			var deformer = NdmfDeformObjectFactory.CreateDeformer(go, typeof(LatticeDeformer), "Lattice");
			Assert.That(deformer, Is.Not.Null);
		}
	}
}
