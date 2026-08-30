using System.Reflection;
using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// Lattice オーバーレイ(PointGridOverlay)の表示条件の検証。
	/// DeformStack 選択中はリストにラティスが含まれるだけでは表示せず、
	/// ラティス行をインライン選択している時のみ表示する。
	/// </summary>
	public class PointGridOverlayTests
	{
		private Object[] _previousSelection;
		private GameObject _root;

		[SetUp]
		public void SetUp()
		{
			_previousSelection = Selection.objects;
		}

		[TearDown]
		public void TearDown()
		{
			SetActiveInlineDeformer(null);
			Selection.objects = _previousSelection;
			if (_root != null) Object.DestroyImmediate(_root);
		}

		// 内部メンバー(表示判定とインライン選択状態)へはリフレクションでアクセスする

		private static bool ComputeVisible()
		{
			return (bool)typeof(PointGridOverlay)
				.GetMethod("ComputeVisible", BindingFlags.Static | BindingFlags.NonPublic)
				.Invoke(null, null);
		}

		private static void SetActiveInlineDeformer(DeformerBase deformer)
		{
			typeof(DeformStackEditor)
				.GetProperty("ActiveInlineDeformer", BindingFlags.Static | BindingFlags.NonPublic)
				.SetValue(null, deformer);
		}

		private (DeformStack stack, LatticeDeformer lattice) CreateStackWithLattice()
		{
			_root = new GameObject("OverlayTestStack");
			var stack = _root.AddComponent<DeformStack>();
			var child = new GameObject("Lattice");
			child.transform.SetParent(_root.transform, false);
			var lattice = child.AddComponent<LatticeDeformer>();
			stack.AddDeformer(lattice);
			return (stack, lattice);
		}

		[Test]
		public void StackSelected_WithoutInlineLatticeSelection_HidesOverlay()
		{
			var (stack, _) = CreateStackWithLattice();
			Selection.objects = new Object[] { stack.gameObject };
			SetActiveInlineDeformer(null);

			Assert.That(ComputeVisible(), Is.False,
				"リストにラティスがあるだけ(行を選択していない)ではオーバーレイを出さない");
		}

		[Test]
		public void StackSelected_WithInlineLatticeSelection_ShowsOverlay()
		{
			var (stack, lattice) = CreateStackWithLattice();
			Selection.objects = new Object[] { stack.gameObject };
			SetActiveInlineDeformer(lattice);

			Assert.That(ComputeVisible(), Is.True,
				"ラティス行をインライン選択中はオーバーレイを出す");
		}

		[Test]
		public void LatticeGameObjectSelected_ShowsOverlay()
		{
			var (_, lattice) = CreateStackWithLattice();
			Selection.objects = new Object[] { lattice.gameObject };
			SetActiveInlineDeformer(null);

			Assert.That(ComputeVisible(), Is.True,
				"ラティスの GameObject を直接選択中はオーバーレイを出す");
		}

		[Test]
		public void UnrelatedSelection_HidesOverlay()
		{
			_root = new GameObject("PlainObject");
			Selection.objects = new Object[] { _root };
			SetActiveInlineDeformer(null);

			Assert.That(ComputeVisible(), Is.False);
		}
	}
}
