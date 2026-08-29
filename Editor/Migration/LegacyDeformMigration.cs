using System;
using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// 旧 NDMFDeform(Deform フォーク)コンポーネントから v2 への移行。
	/// フォークのアセンブリを参照せず、型名の一致と SerializedProperty で読み取るため、
	/// フォークが削除済みのプロジェクトでもこのパッケージはコンパイルできる
	/// (移行はフォークがまだ存在するプロジェクトで実行する)。
	/// </summary>
	public static class LegacyDeformMigration
	{
		public const string DeformableTypeName = "Deform.Deformable";
		public const string LatticeTypeName = "Deform.LatticeDeformer";
		public const string ScaleTypeName = "Deform.ScaleDeformer";
		public const string TransformTypeName = "Deform.TransformDeformer";

		public sealed class Report
		{
			public int StacksCreated;
			public int LatticesMigrated;
			public int SimpleDeformersMigrated;

			/// <summary>移行できなかった旧デフォーマ("オブジェクト名: 型名")</summary>
			public readonly List<string> UnsupportedDeformers = new List<string>();

			/// <summary>移行はしたが挙動が変わる点などの注意</summary>
			public readonly List<string> Notes = new List<string>();
		}

		public static bool IsLegacyDeformable(Component component) =>
			TypeNameMatches(component, DeformableTypeName);

		public static bool IsLegacyLattice(Component component) =>
			TypeNameMatches(component, LatticeTypeName);

		private static bool TypeNameMatches(Component component, string fullName)
		{
			if (component == null)
				return false;
			for (var t = component.GetType(); t != null; t = t.BaseType)
			{
				if (t.FullName == fullName)
					return true;
			}
			return false;
		}

		/// <summary>ロード済みシーンから旧 Deformable を集める(非アクティブ含む)</summary>
		public static List<Component> FindLegacyDeformables()
		{
			var result = new List<Component>();
			foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
			{
				if (IsLegacyDeformable(behaviour))
					result.Add(behaviour);
			}
			return result;
		}

		/// <summary>
		/// 旧 Deformable のリストを v2(DeformStack + LatticeDeformer)へ移行する。
		/// removeLegacy が真なら移行済みの旧コンポーネントを削除する
		/// (未対応デフォーマが残る Deformable は手掛かりとして残す)。
		/// isLattice はテスト用の差し替えポイント。
		/// </summary>
		public static Report Migrate(IEnumerable<Component> legacyDeformables, bool removeLegacy,
			Func<Component, bool> isLattice = null,
			Func<Component, bool> isScale = null,
			Func<Component, bool> isTransform = null)
		{
			isLattice ??= IsLegacyLattice;
			isScale ??= c => TypeNameMatches(c, ScaleTypeName);
			isTransform ??= c => TypeNameMatches(c, TransformTypeName);
			var report = new Report();

			foreach (var legacy in legacyDeformables)
			{
				if (legacy == null)
					continue;
				var go = legacy.gameObject;

				// ElasticDeformable など派生型のスタックも移行するが、
				// 実行時挙動(揺れ等)はベイク対象外である旨を残す
				if (TypeNameMatches(legacy, DeformableTypeName) &&
				    legacy.GetType().FullName != DeformableTypeName)
				{
					report.Notes.Add(
						$"{go.name}: {legacy.GetType().Name} の実行時挙動(揺れ等)は移行されません(ベイクは静的です)");
				}

				if (!go.TryGetComponent<DeformStack>(out var stack))
				{
					stack = Undo.AddComponent<DeformStack>(go);
					report.StacksCreated++;
				}
				Undo.RecordObject(stack, "Migrate Deformable");

				var legacySo = new SerializedObject(legacy);

				// 旧法線設定: Auto(0) = 再計算 / None(1) = 再計算しない
				var normals = legacySo.FindProperty("normalsRecalculation");
				if (normals != null)
				{
					stack.Normals = normals.intValue == 0
						? DeformStack.NormalsMode.Recalculate
						: DeformStack.NormalsMode.PreserveAuthored;
				}

				var fullyMigrated = true;
				var migratedComponents = new List<Component>();
				var elements = legacySo.FindProperty("deformerElements");
				if (elements != null && elements.isArray)
				{
					for (var i = 0; i < elements.arraySize; i++)
					{
						var element = elements.GetArrayElementAtIndex(i);
						var component = element.FindPropertyRelative("component")?.objectReferenceValue as Component;
						var active = element.FindPropertyRelative("active")?.boolValue ?? true;
						if (component == null)
							continue;

						if (isLattice(component))
						{
							var lattice = MigrateLattice(component);
							stack.AddDeformer(lattice, active);
							migratedComponents.Add(component);
							report.LatticesMigrated++;
						}
						else if (isScale(component))
						{
							stack.AddDeformer(MigrateScale(component), active);
							migratedComponents.Add(component);
							report.SimpleDeformersMigrated++;
						}
						else if (isTransform(component))
						{
							stack.AddDeformer(MigrateTransform(component), active);
							migratedComponents.Add(component);
							report.SimpleDeformersMigrated++;
						}
						else
						{
							// Missing Script は型が素の MonoBehaviour として現れる
							var label = component.GetType() == typeof(MonoBehaviour)
								? "Missing Script(参照切れ)"
								: component.GetType().Name;
							report.UnsupportedDeformers.Add($"{go.name}: {label}");
							fullyMigrated = false;
						}
					}
				}

				PrefabUtility.RecordPrefabInstancePropertyModifications(stack);
				EditorUtility.SetDirty(stack);

				if (removeLegacy)
				{
					foreach (var component in migratedComponents)
						Undo.DestroyObjectImmediate(component);
					if (fullyMigrated)
						Undo.DestroyObjectImmediate(legacy);
				}
			}

			return report;
		}

		/// <summary>旧 ScaleDeformer → v2 ScaleDeformer(パラメータは軸 Transform の localScale のまま)</summary>
		public static ScaleDeformer MigrateScale(Component legacyScale)
		{
			var scale = Undo.AddComponent<ScaleDeformer>(legacyScale.gameObject);
			var legacySo = new SerializedObject(legacyScale);
			var so = new SerializedObject(scale);

			// 旧 axis(null = 自身の Transform)はそのまま axisOverride へ
			var axis = legacySo.FindProperty("axis")?.objectReferenceValue;
			so.FindProperty("axisOverride").objectReferenceValue = axis;
			so.ApplyModifiedPropertiesWithoutUndo();
			return scale;
		}

		/// <summary>旧 TransformDeformer → v2 TransformDeformer(target / factor を引き継ぐ)</summary>
		public static TransformDeformer MigrateTransform(Component legacyTransform)
		{
			var deformer = Undo.AddComponent<TransformDeformer>(legacyTransform.gameObject);
			var legacySo = new SerializedObject(legacyTransform);
			var so = new SerializedObject(deformer);

			var target = legacySo.FindProperty("target")?.objectReferenceValue;
			so.FindProperty("target").objectReferenceValue = target;
			var factor = legacySo.FindProperty("factor");
			if (factor != null)
				so.FindProperty("factor").floatValue = Mathf.Clamp01(factor.floatValue);
			so.ApplyModifiedPropertiesWithoutUndo();
			return deformer;
		}

		/// <summary>
		/// 旧 LatticeDeformer と同じ GameObject に v2 の LatticeDeformer を作る。
		/// 制御点レイアウト([-0.5,0.5]³、x + y*resX + z*resX*resY)は互換のため直接コピーする。
		/// </summary>
		public static LatticeDeformer MigrateLattice(Component legacyLattice)
		{
			var go = legacyLattice.gameObject;

			// AddComponent 時の Reset が FitToParentStack で Transform を書き換えるため退避・復元する
			var t = go.transform;
			Undo.RecordObject(t, "Migrate Lattice");
			var localPosition = t.localPosition;
			var localRotation = t.localRotation;
			var localScale = t.localScale;

			var lattice = Undo.AddComponent<LatticeDeformer>(go);

			t.localPosition = localPosition;
			t.localRotation = localRotation;
			t.localScale = localScale;

			var legacySo = new SerializedObject(legacyLattice);
			var so = new SerializedObject(lattice);

			var resolutionProperty = legacySo.FindProperty("resolution");
			var resolution = resolutionProperty != null
				? resolutionProperty.vector3IntValue
				: new Vector3Int(2, 2, 2);
			so.FindProperty("resolution").vector3IntValue = resolution;
			so.FindProperty("appliedResolution").vector3IntValue = resolution;

			var legacyPoints = legacySo.FindProperty("controlPoints");
			if (legacyPoints != null && legacyPoints.isArray)
			{
				var points = so.FindProperty("controlPoints");
				points.arraySize = legacyPoints.arraySize;
				for (var i = 0; i < legacyPoints.arraySize; i++)
				{
					var src = legacyPoints.GetArrayElementAtIndex(i);
					var dst = points.GetArrayElementAtIndex(i);
					dst.FindPropertyRelative("x").floatValue = src.FindPropertyRelative("x").floatValue;
					dst.FindPropertyRelative("y").floatValue = src.FindPropertyRelative("y").floatValue;
					dst.FindPropertyRelative("z").floatValue = src.FindPropertyRelative("z").floatValue;
				}
			}

			// 旧ミラーはビットフラグ(X=1,Y=2,Z=4)、v2 は単一軸。複数指定時は X → Y → Z の優先で採用
			var legacyMirror = legacySo.FindProperty("mirrorAxis");
			if (legacyMirror != null)
			{
				var flags = legacyMirror.intValue;
				var mapped = (flags & 1) != 0 ? MirrorAxis.X
					: (flags & 2) != 0 ? MirrorAxis.Y
					: (flags & 4) != 0 ? MirrorAxis.Z
					: MirrorAxis.None;
				so.FindProperty("mirrorAxis").intValue = (int)mapped;
			}

			// AddComponent 自体が Undo 対象のため、プロパティ書き込みに追加の Undo は不要
			so.ApplyModifiedPropertiesWithoutUndo();
			return lattice;
		}
	}
}
