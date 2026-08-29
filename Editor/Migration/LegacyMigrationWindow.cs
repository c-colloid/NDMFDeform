using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// 旧 NDMFDeform(Deform フォーク)コンポーネントを v2 へ移行するウィンドウ。
	/// シーン内の旧 Deformable を列挙し、DeformStack + LatticeDeformer へ変換する。
	/// </summary>
	public class LegacyMigrationWindow : EditorWindow
	{
		private List<Component> _found = new List<Component>();
		private Toggle _removeToggle;
		private ScrollView _list;
		private Label _status;

		[MenuItem("Tools/NDMF Deform/旧 Deformable から移行...")]
		public static void Open()
		{
			var window = GetWindow<LegacyMigrationWindow>("NDMF Deform 移行");
			window.minSize = new Vector2(340, 260);
		}

		private void CreateGUI()
		{
			var root = rootVisualElement;
			NdmfDeformFonts.ApplyEditorUiFont(root);
			root.style.paddingTop = 8;
			root.style.paddingBottom = 8;
			root.style.paddingLeft = 8;
			root.style.paddingRight = 8;

			var description = new Label(
				"旧 NDMFDeform(Deform フォーク)の Deformable を\n" +
				"DeformStack + LatticeDeformer(v2)へ変換します。\n" +
				"Lattice 以外の旧デフォーマは移行されず、一覧に報告されます。");
			description.style.whiteSpace = WhiteSpace.Normal;
			description.style.opacity = 0.8f;
			description.style.marginBottom = 6;
			root.Add(description);

			_removeToggle = new Toggle("移行後に旧コンポーネントを削除") { value = true };
			root.Add(_removeToggle);

			var buttons = new VisualElement();
			buttons.style.flexDirection = FlexDirection.Row;
			buttons.style.marginTop = 4;
			buttons.style.marginBottom = 4;
			buttons.Add(new Button(Refresh) { text = "再走査" });
			buttons.Add(new Button(RunMigration) { text = "移行実行" });
			root.Add(buttons);

			_list = new ScrollView();
			_list.style.flexGrow = 1;
			_list.style.minHeight = 80;
			root.Add(_list);

			_status = new Label();
			_status.style.whiteSpace = WhiteSpace.Normal;
			_status.style.marginTop = 4;
			root.Add(_status);

			Refresh();
		}

		private void Refresh()
		{
			_found = LegacyDeformMigration.FindLegacyDeformables();
			_list.Clear();
			foreach (var component in _found)
			{
				if (component == null)
					continue;
				var row = new Label($"• {component.gameObject.name}");
				_list.Add(row);
			}
			_status.text = _found.Count > 0
				? $"旧 Deformable: {_found.Count} 件"
				: "旧 Deformable は見つかりませんでした。";
		}

		private void RunMigration()
		{
			if (_found.Count == 0)
			{
				Refresh();
				if (_found.Count == 0)
					return;
			}

			Undo.SetCurrentGroupName("NDMF Deform 移行");
			var report = LegacyDeformMigration.Migrate(_found, _removeToggle.value);

			var lines = new List<string>
			{
				$"移行完了: DeformStack {report.StacksCreated} 件 / Lattice {report.LatticesMigrated} 件",
			};
			if (report.UnsupportedDeformers.Count > 0)
			{
				lines.Add("未対応(手動で対応してください):");
				foreach (var entry in report.UnsupportedDeformers)
					lines.Add($"  {entry}");
			}
			Refresh();
			// 再走査で件数表示に上書きされるため、移行結果をあらためて表示する
			_status.text = string.Join("\n", lines);
			SceneView.RepaintAll();
		}
	}
}
