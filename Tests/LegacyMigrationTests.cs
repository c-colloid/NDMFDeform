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

		private class FakeLegacyScale : MonoBehaviour
		{
			public Transform axis;
		}

		private class FakeLegacyTransform : MonoBehaviour
		{
			public Transform target;
			public float factor = 1f;
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
		public void Migrate_ConvertsScaleAndTransformDeformers()
		{
			_root = new GameObject("LegacyRoot");
			var deformable = _root.AddComponent<FakeLegacyDeformable>();

			var scaleGo = new GameObject("LegacyScale");
			scaleGo.transform.SetParent(_root.transform, false);
			var legacyScale = scaleGo.AddComponent<FakeLegacyScale>();

			var targetGo = new GameObject("Target");
			targetGo.transform.SetParent(_root.transform, false);
			var transformGo = new GameObject("LegacyTransform");
			transformGo.transform.SetParent(_root.transform, false);
			var legacyTransform = transformGo.AddComponent<FakeLegacyTransform>();
			legacyTransform.target = targetGo.transform;
			legacyTransform.factor = 0.5f;

			deformable.deformerElements.Add(new FakeLegacyDeformable.Element { component = legacyScale });
			deformable.deformerElements.Add(new FakeLegacyDeformable.Element { component = legacyTransform });

			var report = LegacyDeformMigration.Migrate(
				new Component[] { deformable }, removeLegacy: true,
				isScale: c => c is FakeLegacyScale,
				isTransform: c => c is FakeLegacyTransform);

			Assert.That(report.SimpleDeformersMigrated, Is.EqualTo(2));
			Assert.That(report.UnsupportedDeformers, Is.Empty);

			Assert.That(_root.TryGetComponent<DeformStack>(out var stack), Is.True);
			Assert.That(stack.Deformers.Count, Is.EqualTo(2));

			var scale = stack.Deformers[0].deformer as ScaleDeformer;
			Assert.That(scale, Is.Not.Null);
			Assert.That(scale.gameObject, Is.SameAs(scaleGo));
			Assert.That(scale.AxisOverride == null, Is.True);
			Assert.That(scale.Axis, Is.SameAs(scaleGo.transform));

			var transform = stack.Deformers[1].deformer as TransformDeformer;
			Assert.That(transform, Is.Not.Null);
			Assert.That(transform.Target, Is.SameAs(targetGo.transform));
			Assert.That(transform.Factor, Is.EqualTo(0.5f).Within(1e-5f));

			// 旧コンポーネントは削除される
			Assert.That(legacyScale == null, Is.True);
			Assert.That(legacyTransform == null, Is.True);
			Assert.That(deformable == null, Is.True);
		}

		private class FakeLegacySphereMask : MonoBehaviour
		{
			public float factor = 0.8f;
			public float innerRadius = 1.5f;
			public float outerRadius = 3f;
			public bool invert = true;
			public Transform axis;
		}

		private class FakeLegacyBoxMask : MonoBehaviour
		{
			public float factor = 1f;
			public Bounds innerBounds;
			public Bounds outerBounds;
			public bool invert;
			public Transform axis;
		}

		[Test]
		public void MigrateSphereMask_CopiesFields()
		{
			_root = new GameObject("LegacyRoot");
			var axisGo = new GameObject("Axis");
			axisGo.transform.SetParent(_root.transform, false);
			var legacy = _root.AddComponent<FakeLegacySphereMask>();
			legacy.axis = axisGo.transform;

			var mask = LegacyDeformMigration.MigrateSphereMask(legacy);

			Assert.That(mask.Factor, Is.EqualTo(0.8f).Within(1e-5f));
			Assert.That(mask.InnerRadius, Is.EqualTo(1.5f).Within(1e-5f));
			Assert.That(mask.OuterRadius, Is.EqualTo(3f).Within(1e-5f));
			Assert.That(mask.Invert, Is.True);
			Assert.That(mask.Axis, Is.SameAs(axisGo.transform));
		}

		[Test]
		public void MigrateBoxMask_CopiesBounds()
		{
			_root = new GameObject("LegacyRoot");
			var legacy = _root.AddComponent<FakeLegacyBoxMask>();
			legacy.innerBounds = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 2f, 2f));
			legacy.outerBounds = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 4f, 4f));

			var mask = LegacyDeformMigration.MigrateBoxMask(legacy);

			Assert.That(mask.InnerBounds.center, Is.EqualTo(new Vector3(1f, 2f, 3f)));
			Assert.That(mask.InnerBounds.size, Is.EqualTo(new Vector3(2f, 2f, 2f)));
			Assert.That(mask.OuterBounds.size, Is.EqualTo(new Vector3(4f, 4f, 4f)));
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
