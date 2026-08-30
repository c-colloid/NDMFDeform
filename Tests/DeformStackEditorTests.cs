using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>DeformStack インスペクタ UI の構築スモークテスト</summary>
	public class DeformStackEditorTests
	{
		[Test]
		public void CreateInspectorGUI_BuildsReorderableListUi()
		{
			var go = new GameObject("StackUiTest", typeof(MeshFilter), typeof(MeshRenderer));
			try
			{
				var stack = go.AddComponent<DeformStack>();
				var child = new GameObject("Mask");
				child.transform.SetParent(go.transform, false);
				stack.AddDeformer(child.AddComponent<SphereMaskDeformer>());

				var editor = UnityEditor.Editor.CreateEditor(stack);
				try
				{
					var root = editor.CreateInspectorGUI();
					Assert.That(root, Is.Not.Null);

					var list = root.Q<ListView>();
					Assert.That(list, Is.Not.Null, "デフォーマ一覧の ListView がある");
					Assert.That(list.reorderable, Is.True, "一覧はリオーダブル");
					Assert.That(list.showFoldoutHeader, Is.False, "一覧は常時展開");

					var row = list.makeItem();
					Assert.That(row.Q<Toggle>("row-enabled"), Is.Not.Null, "行に有効トグルがある");
					Assert.That(row.Q<UnityEditor.UIElements.ObjectField>("row-deformer"), Is.Not.Null,
						"行にデフォーマ参照フィールドがある");
				}
				finally
				{
					Object.DestroyImmediate(editor);
				}
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void BlendShapeOverrideDrawer_ListsShapesFromRendererAndShowsStoredName()
		{
			var go = new GameObject("ShapeStack");
			Mesh mesh = null;
			try
			{
				var smr = go.AddComponent<SkinnedMeshRenderer>();
				mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.one } };
				var deltas = new Vector3[2];
				mesh.AddBlendShapeFrame("ShapeA", 100f, deltas, null, null);
				mesh.AddBlendShapeFrame("ShapeB", 100f, deltas, null, null);
				smr.sharedMesh = mesh;

				var stack = go.AddComponent<DeformStack>();
				stack.BlendShapeOverrides.Add(new DeformStack.BlendShapeOverride
				{
					shapeName = "ShapeB",
					mode = DeformStack.BlendShapeDeltaMode.KeepAuthoredShape,
				});

				Assert.That(BlendShapeOverrideDrawer.GetShapeNames(stack),
					Is.EqualTo(new[] { "ShapeA", "ShapeB" }), "レンダラーのシェイプ一覧を取得できる");

				using (var serializedObject = new SerializedObject(stack))
				{
					var element = serializedObject.FindProperty("blendShapeOverrides").GetArrayElementAtIndex(0);
					var row = new BlendShapeOverrideDrawer().CreatePropertyGUI(element);
					Assert.That(row, Is.Not.Null);

					var picker = row.Q<Button>();
					Assert.That(picker, Is.Not.Null, "シェイプ選択ボタンがある");
					Assert.That(picker.text, Is.EqualTo("ShapeB"), "保持中のシェイプ名が表示される");
				}
			}
			finally
			{
				Object.DestroyImmediate(go);
				if (mesh != null) Object.DestroyImmediate(mesh);
			}
		}
	}
}
