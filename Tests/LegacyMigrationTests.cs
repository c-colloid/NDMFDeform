using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	public class LegacyMigrationTests
	{
		private GameObject _root;

		[TearDown]
		public void TearDown()
		{
			if (_root != null) Object.DestroyImmediate(_root);
		}

		// ---- 旧フォークと同じシリアライズレイアウトを持つテスト用ダミー ----

		private enum FakeMirrorAxis
		{
			None = 0,
			X = 1 << 0,
			Y = 1 << 1,
			Z = 1 << 2,
		}

		private class FakeLegacyLattice : MonoBehaviour
		{
			public float3[] controlPoints;
			public Vector3Int resolution = new Vector3Int(2, 2, 2);
			public FakeMirrorAxis mirrorAxis = FakeMirrorAxis.None;
		}

		private class FakeLegacyDeformable : MonoBehaviour
		{
			[System.Serializable]
			public class Element
			{
				public Component component;
				public bool active = true;
			}

			public List<Element> deformerElements = new List<Element>();
			public int normalsRecalculation; // 旧 enum: Auto = 0 / None = 1
		}

		private (FakeLegacyDeformable deformable, FakeLegacyLattice lattice) CreateLegacySetup()
		{
			_root = new GameObject("LegacyRoot");
			var deformable = _root.AddComponent<FakeLegacyDeformable>();

			var latticeGo = new GameObject("LegacyLattice");
			latticeGo.transform.SetParent(_root.transform, false);
			latticeGo.transform.localPosition = new Vector3(1f, 2f, 3f);
			latticeGo.transform.localScale = new Vector3(2f, 3f, 4f);
			var lattice = latticeGo.AddComponent<FakeLegacyLattice>();

			deformable.deformerElements.Add(new FakeLegacyDeformable.Element
			{
				component = lattice,
				active = true,
			});
			return (deformable, lattice);
		}

		[Test]
		public void Migrate_CreatesStackAndCopiesLattice()
		{
			var (deformable, legacyLattice) = CreateLegacySetup();
			legacyLattice.resolution = new Vector3Int(3, 2, 2);
			legacyLattice.controlPoints = new float3[3 * 2 * 2];
			for (var i = 0; i < legacyLattice.controlPoints.Length; i++)
				legacyLattice.controlPoints[i] = new float3(i * 0.01f, -0.5f, 0.5f);
			legacyLattice.mirrorAxis = FakeMirrorAxis.Y | FakeMirrorAxis.Z;

			var latticeGo = legacyLattice.gameObject;
			var report = LegacyDeformMigration.Migrate(
				new Component[] { deformable }, removeLegacy: true,
				isLattice: c => c is FakeLegacyLattice);

			Assert.That(report.StacksCreated, Is.EqualTo(1));
			Assert.That(report.LatticesMigrated, Is.EqualTo(1));
			Assert.That(report.UnsupportedDeformers, Is.Empty);

			Assert.That(_root.TryGetComponent<DeformStack>(out var stack), Is.True);
			Assert.That(stack.Deformers.Count, Is.EqualTo(1));
			Assert.That(stack.Deformers[0].enabled, Is.True);

			var lattice = stack.Deformers[0].deformer as LatticeDeformer;
			Assert.That(lattice, Is.Not.Null);
			Assert.That(lattice.gameObject, Is.SameAs(latticeGo));
			Assert.That(lattice.Resolution, Is.EqualTo(new Vector3Int(3, 2, 2)));
			Assert.That(lattice.ControlPoints.Length, Is.EqualTo(12));
			Assert.That(lattice.ControlPoints[5].x, Is.EqualTo(0.05f).Within(1e-5f));
			// 旧フラグ Y|Z は優先順で Y に写像される
			Assert.That(lattice.EditMirrorAxis, Is.EqualTo(MirrorAxis.Y));

			// Reset の FitToParentStack で軸 Transform が動かされていない
			Assert.That(Vector3.Distance(latticeGo.transform.localPosition, new Vector3(1f, 2f, 3f)),
				Is.LessThan(1e-5f));
			Assert.That(Vector3.Distance(latticeGo.transform.localScale, new Vector3(2f, 3f, 4f)),
				Is.LessThan(1e-5f));

			// 旧コンポーネントは削除される
			Assert.That(legacyLattice == null, Is.True);
			Assert.That(deformable == null, Is.True);
		}

		[Test]
		public void Migrate_ReportsUnsupportedAndKeepsLegacy()
		{
			_root = new GameObject("LegacyRoot");
			var deformable = _root.AddComponent<FakeLegacyDeformable>();
			var unsupported = _root.AddComponent<BoxCollider>();
			deformable.deformerElements.Add(new FakeLegacyDeformable.Element
			{
				component = unsupported,
				active = true,
			});

			var report = LegacyDeformMigration.Migrate(
				new Component[] { deformable }, removeLegacy: true,
				isLattice: c => c is FakeLegacyLattice);

			Assert.That(report.UnsupportedDeformers.Count, Is.EqualTo(1));
			// 未対応が残る場合は旧 Deformable を手掛かりとして残す
			Assert.That(deformable != null, Is.True);
			Assert.That(_root.TryGetComponent<DeformStack>(out _), Is.True);
		}

		[Test]
		public void Migrate_MapsNormalsRecalculation()
		{
			var (deformable, _) = CreateLegacySetup();
			deformable.normalsRecalculation = 0; // Auto = 再計算

			LegacyDeformMigration.Migrate(new Component[] { deformable }, removeLegacy: false,
				isLattice: c => c is FakeLegacyLattice);

			Assert.That(_root.TryGetComponent<DeformStack>(out var stack), Is.True);
			Assert.That(stack.Normals, Is.EqualTo(DeformStack.NormalsMode.Recalculate));

			// None(1) は作り込み保持
			var second = new GameObject("LegacyRoot2");
			try
			{
				var deformable2 = second.AddComponent<FakeLegacyDeformable>();
				deformable2.normalsRecalculation = 1;
				LegacyDeformMigration.Migrate(new Component[] { deformable2 }, removeLegacy: false,
					isLattice: c => c is FakeLegacyLattice);
				Assert.That(second.TryGetComponent<DeformStack>(out var stack2), Is.True);
				Assert.That(stack2.Normals, Is.EqualTo(DeformStack.NormalsMode.PreserveAuthored));
			}
			finally
			{
				Object.DestroyImmediate(second);
			}
		}
	}
}
