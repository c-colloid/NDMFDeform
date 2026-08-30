using System;
using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// DeformStack.BlendShapeOverride の行 UI。
	/// シェイプ名は内部的には文字列で保持しつつ(モデル更新でシェイプ数・順序が
	/// 変わっても対象がズレない)、UI ではレンダラーのブレンドシェイプ一覧から
	/// 検索付きドロップダウンで選択する。
	/// メッシュに存在しない名前(リネーム・削除後)は警告色で表示する。
	/// </summary>
	[CustomPropertyDrawer(typeof(DeformStack.BlendShapeOverride))]
	public class BlendShapeOverrideDrawer : PropertyDrawer
	{
		public override VisualElement CreatePropertyGUI(SerializedProperty property)
		{
			// 行の構成は BlendShapeOverrideRow.uxml / 状態表現は USS クラス
			// (--empty / --missing)。ここではバインドと文言の更新だけを行う
			var row = new VisualElement();
			NdmfDeformUI.CloneTree(NdmfDeformUI.BlendShapeOverrideRowGuid, row);

			var nameProp = property.FindPropertyRelative("shapeName");
			var modeProp = property.FindPropertyRelative("mode");
			var serializedObject = property.serializedObject;
			var namePath = nameProp.propertyPath;

			var picker = row.Q<Button>("shape-picker");
			if (picker == null)
				return row;

			void Refresh()
			{
				var p = serializedObject.FindProperty(namePath);
				if (p == null)
					return;
				var shapeName = p.stringValue;
				picker.EnableInClassList("ndmf-shape-picker--empty", string.IsNullOrEmpty(shapeName));
				if (string.IsNullOrEmpty(shapeName))
				{
					picker.text = "(シェイプを選択...)";
					picker.EnableInClassList("ndmf-shape-picker--missing", false);
					return;
				}
				if (GetShapeNames(serializedObject.targetObject as DeformStack).Contains(shapeName))
				{
					picker.text = shapeName;
					picker.EnableInClassList("ndmf-shape-picker--missing", false);
					picker.tooltip = shapeName;
				}
				else
				{
					picker.text = $"{shapeName} (メッシュに無し)";
					picker.EnableInClassList("ndmf-shape-picker--missing", true);
					picker.tooltip = $"'{shapeName}' は現在のメッシュに存在しません(リネームまたは削除された可能性)";
				}
			}

			picker.clicked += () =>
			{
				var names = GetShapeNames(serializedObject.targetObject as DeformStack);
				var dropdown = new ShapeDropdown(new AdvancedDropdownState(), names, selected =>
				{
					var p = serializedObject.FindProperty(namePath);
					if (p == null)
						return;
					p.stringValue = selected;
					serializedObject.ApplyModifiedProperties();
				});
				dropdown.Show(picker.worldBound);
			};

			row.TrackPropertyValue(nameProp, _ => Refresh());
			Refresh();

			var mode = row.Q<PropertyField>("shape-mode");
			if (mode != null)
			{
				mode.label = string.Empty;
				mode.BindProperty(modeProp);
			}

			return row;
		}

		/// <summary>スタックのレンダラーが持つブレンドシェイプ名の一覧</summary>
		public static List<string> GetShapeNames(DeformStack stack)
		{
			var result = new List<string>();
			if (stack == null)
				return result;
			if (!stack.TryGetComponent<SkinnedMeshRenderer>(out var smr) || smr.sharedMesh == null)
				return result;

			var mesh = smr.sharedMesh;
			for (var i = 0; i < mesh.blendShapeCount; i++)
				result.Add(mesh.GetBlendShapeName(i));
			return result;
		}

		/// <summary>検索ボックス付きのシェイプ選択ドロップダウン(数百シェイプでも扱える)</summary>
		private class ShapeDropdown : AdvancedDropdown
		{
			private readonly List<string> _names;
			private readonly Action<string> _onSelect;

			public ShapeDropdown(AdvancedDropdownState state, List<string> names, Action<string> onSelect)
				: base(state)
			{
				_names = names;
				_onSelect = onSelect;
				minimumSize = new Vector2(260, 320);
			}

			protected override AdvancedDropdownItem BuildRoot()
			{
				var root = new AdvancedDropdownItem("ブレンドシェイプ");
				if (_names.Count == 0)
				{
					root.AddChild(new AdvancedDropdownItem("(このレンダラーにシェイプがありません)") { enabled = false });
					return root;
				}
				foreach (var name in _names)
					root.AddChild(new AdvancedDropdownItem(name));
				return root;
			}

			protected override void ItemSelected(AdvancedDropdownItem item)
			{
				if (item.enabled)
					_onSelect(item.name);
			}
		}
	}
}
