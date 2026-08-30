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
		private static readonly Color MissingColor = new Color(1f, 0.62f, 0.25f);

		public override VisualElement CreatePropertyGUI(SerializedProperty property)
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.Center;

			var nameProp = property.FindPropertyRelative("shapeName");
			var modeProp = property.FindPropertyRelative("mode");
			var serializedObject = property.serializedObject;
			var namePath = nameProp.propertyPath;

			var picker = new Button
			{
				tooltip = "レンダラーのブレンドシェイプから選択",
			};
			picker.style.flexGrow = 1;
			picker.style.flexShrink = 1;
			picker.style.flexBasis = 0;
			picker.style.unityTextAlign = TextAnchor.MiddleLeft;
			picker.style.marginLeft = 0;
			picker.style.overflow = Overflow.Hidden;

			void Refresh()
			{
				var p = serializedObject.FindProperty(namePath);
				if (p == null)
					return;
				var shapeName = p.stringValue;
				if (string.IsNullOrEmpty(shapeName))
				{
					picker.text = "(シェイプを選択...)";
					picker.style.color = StyleKeyword.Null;
					picker.style.opacity = 0.6f;
					return;
				}
				picker.style.opacity = 1f;
				if (GetShapeNames(serializedObject.targetObject as DeformStack).Contains(shapeName))
				{
					picker.text = shapeName;
					picker.style.color = StyleKeyword.Null;
					picker.tooltip = shapeName;
				}
				else
				{
					picker.text = $"{shapeName} (メッシュに無し)";
					picker.style.color = MissingColor;
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
			row.Add(picker);

			var mode = new PropertyField(modeProp, "");
			mode.style.width = 150;
			mode.style.flexShrink = 0;
			mode.style.marginLeft = 4;
			row.Add(mode);

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
