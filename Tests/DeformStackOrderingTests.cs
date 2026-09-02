using System.Collections.Generic;
using System.Text.RegularExpressions;
using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// DeformStackOrdering: 参照先(Body Fit の体など)にスタックがある場合、参照先を先に並べる。
	/// 依存の無いスタックは入力順を保ち、循環は警告して入力順のまま。
	/// </summary>
	public class DeformStackOrderingTests
	{
		private GameObject _root;

		[TearDown]
		public void TearDown()
		{
			if (_root != null) Object.DestroyImmediate(_root);
			_root = null;
		}

		private DeformStack CreateStack(string name)
		{
			// ??= は Unity の破棄済み判定(== null)を通らないため明示的に比較する
			if (_root == null)
				_root = new GameObject("OrderingRoot");
			var go = new GameObject(name);
			go.transform.SetParent(_root.transform, false);
			go.AddComponent<MeshFilter>();
			go.AddComponent<MeshRenderer>();
			return go.AddComponent<DeformStack>();
		}

		private static void AddBodyFit(DeformStack stack, Renderer body, bool enabled = true)
		{
			var go = new GameObject("BodyFit");
			go.transform.SetParent(stack.transform, false);
			var fit = go.AddComponent<BodyFitDeformer>();
			fit.Body = body;
			stack.AddDeformer(fit, enabled);
		}

		[Test]
		public void Sort_PutsReferencedStackFirst()
		{
			var jacket = CreateStack("Jacket");
			var shirt = CreateStack("Shirt");
			var other = CreateStack("Other");
			AddBodyFit(jacket, shirt.GetComponent<Renderer>());

			var sorted = DeformStackOrdering.Sort(new List<DeformStack> { jacket, shirt, other });

			Assert.That(sorted, Is.EqualTo(new List<DeformStack> { shirt, jacket, other }));
		}

		[Test]
		public void Sort_KeepsInputOrderWithoutDependencies()
		{
			var a = CreateStack("A");
			var b = CreateStack("B");
			var c = CreateStack("C");
			// 参照先にスタックが無い(体そのもの)場合は依存にならない
			var bodyGo = new GameObject("Body");
			bodyGo.transform.SetParent(_root.transform, false);
			bodyGo.AddComponent<MeshFilter>();
			var body = bodyGo.AddComponent<MeshRenderer>();
			AddBodyFit(b, body);

			var sorted = DeformStackOrdering.Sort(new List<DeformStack> { a, b, c });

			Assert.That(sorted, Is.EqualTo(new List<DeformStack> { a, b, c }));
		}

		[Test]
		public void Sort_IgnoresDisabledDeformers()
		{
			var jacket = CreateStack("Jacket");
			var shirt = CreateStack("Shirt");
			AddBodyFit(jacket, shirt.GetComponent<Renderer>(), enabled: false);

			var sorted = DeformStackOrdering.Sort(new List<DeformStack> { jacket, shirt });

			Assert.That(sorted, Is.EqualTo(new List<DeformStack> { jacket, shirt }));
		}

		[Test]
		public void Sort_ChainOfThreeLayers()
		{
			var coat = CreateStack("Coat");
			var dress = CreateStack("Dress");
			var underwear = CreateStack("Underwear");
			AddBodyFit(coat, dress.GetComponent<Renderer>());
			AddBodyFit(dress, underwear.GetComponent<Renderer>());

			var sorted = DeformStackOrdering.Sort(new List<DeformStack> { coat, dress, underwear });

			Assert.That(sorted, Is.EqualTo(new List<DeformStack> { underwear, dress, coat }));
		}

		[Test]
		public void Sort_CycleWarnsAndKeepsInputOrder()
		{
			var a = CreateStack("A");
			var b = CreateStack("B");
			AddBodyFit(a, b.GetComponent<Renderer>());
			AddBodyFit(b, a.GetComponent<Renderer>());

			LogAssert.Expect(LogType.Warning, new Regex("循環"));
			var sorted = DeformStackOrdering.Sort(new List<DeformStack> { a, b });

			Assert.That(sorted, Has.Count.EqualTo(2));
			Assert.That(sorted[0], Is.SameAs(a));
			Assert.That(sorted[1], Is.SameAs(b));
		}
	}
}
