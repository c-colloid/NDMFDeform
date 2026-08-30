using MeshModifier.NDMFDeform.Core;
using NUnit.Framework;
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
	}
}
