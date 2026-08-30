using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// DeformStack の UITK インスペクタ(旧 Deformable 相当の UX)。
	/// - 常時展開・リオーダブルなデフォーマ一覧(行 = 有効トグル + 参照 + カテゴリバッジ)
	/// - 行選択でそのデフォーマのインスペクタをインライン表示し、
	///   シーンギズモもそのデフォーマの Editor.OnSceneGUI をリフレクション呼び出しで駆動する
	///   (スタックを選択したままデフォーマを編集できる)
	/// - デフォーマ / GameObject の D&amp;D 追加、＋ メニューからの新規作成(子 GameObject 化 + 登録)
	/// </summary>
	[CustomEditor(typeof(DeformStack))]
	public class DeformStackEditor : UnityEditor.Editor
	{
		private const string DeformersPath = "deformers";
		private const int RowHeight = 24;

		/// <summary>
		/// スタック経由でインライン編集中のデフォーマ。
		/// PointGridOverlay の表示判定・解像度参照に使う(直接選択時は null)。
		/// </summary>
		internal static DeformerBase ActiveInlineDeformer { get; private set; }

		private static readonly Dictionary<Type, MethodInfo> SceneGuiMethods = new Dictionary<Type, MethodInfo>();

		private ListView _listView;
		private VisualElement _inlineContainer;
		private DeformerBase _inlineTarget;
		private UnityEditor.Editor _inlineEditor;

		protected virtual void OnDisable()
		{
			SetInlineTarget(null);
		}

		public override VisualElement CreateInspectorGUI()
		{
			var root = new VisualElement();
			NdmfDeformFonts.ApplyEditorUiFont(root);

			var header = new Label("デフォーマ");
			header.style.unityFontStyleAndWeight = FontStyle.Bold;
			header.style.marginTop = 2;
			header.style.marginBottom = 2;
			root.Add(header);

			_listView = new ListView
			{
				bindingPath = DeformersPath,
				reorderable = true,
				reorderMode = ListViewReorderMode.Animated,
				showFoldoutHeader = false,
				showBoundCollectionSize = false,
				showAddRemoveFooter = false,
				showBorder = true,
				selectionType = SelectionType.Single,
				virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
				fixedItemHeight = RowHeight,
			};
			// 空リストでもドロップ先として見えるように最低高さを確保する
			_listView.style.minHeight = RowHeight + 4;
			_listView.makeItem = MakeRow;
			_listView.bindItem = BindRow;
			_listView.unbindItem = (element, _) => element.Unbind();
			_listView.selectedIndicesChanged += OnRowSelectionChanged;
			_listView.itemIndexChanged += (_, _) => SyncInlineFromSelection();
			root.Add(_listView);

			root.Add(MakeFooter());

			_inlineContainer = new VisualElement();
			_inlineContainer.style.marginTop = 4;
			root.Add(_inlineContainer);

			var settings = new VisualElement();
			settings.style.marginTop = 6;
			settings.Add(new PropertyField(serializedObject.FindProperty("normalsMode"), "法線"));
			settings.Add(new PropertyField(serializedObject.FindProperty("nonlinearShapeCorrection"), "シェイプ非線形補正"));
			settings.Add(new PropertyField(serializedObject.FindProperty("blendShapeOverrides"), "シェイプ個別設定"));
			root.Add(settings);

			RegisterDragAndDrop(root);
			return root;
		}

		// ---- 一覧の行 ----

		private VisualElement MakeRow()
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.Center;
			row.style.height = RowHeight;

			var toggle = new Toggle { name = "row-enabled", tooltip = "このデフォーマを適用するか" };
			toggle.style.marginLeft = 2;
			toggle.style.marginRight = 2;
			toggle.style.flexShrink = 0;

			var field = new ObjectField
			{
				name = "row-deformer",
				objectType = typeof(DeformerBase),
				allowSceneObjects = true,
			};
			field.style.flexGrow = 1;
			field.style.flexShrink = 1;
			field.style.flexBasis = 0;
			field.style.marginLeft = 0;
			field.style.marginRight = 0;

			var badge = new Label { name = "row-badge" };
			badge.style.display = DisplayStyle.None;
			badge.style.fontSize = 9;
			badge.style.paddingLeft = 4;
			badge.style.paddingRight = 4;
			badge.style.paddingTop = 1;
			badge.style.paddingBottom = 1;
			badge.style.marginLeft = 3;
			badge.style.marginRight = 3;
			badge.style.borderTopLeftRadius = 6;
			badge.style.borderTopRightRadius = 6;
			badge.style.borderBottomLeftRadius = 6;
			badge.style.borderBottomRightRadius = 6;
			badge.style.flexShrink = 0;
			badge.style.unityTextAlign = TextAnchor.MiddleCenter;

			row.Add(toggle);
			row.Add(field);
			row.Add(badge);

			// 無効な行は参照フィールドを淡色化して状態を見えるようにする
			toggle.RegisterValueChangedCallback(evt =>
			{
				field.style.opacity = evt.newValue ? 1f : 0.45f;
			});
			field.RegisterValueChangedCallback(evt =>
			{
				UpdateBadge(badge, evt.newValue as DeformerBase);
				// 選択中の行の参照が差し替わったらインライン表示も追従させる
				if (row.userData is int index && _listView != null && index == _listView.selectedIndex)
					SetInlineTarget(evt.newValue as DeformerBase);
			});
			return row;
		}

		private void BindRow(VisualElement row, int index)
		{
			row.userData = index;
			var entry = serializedObject.FindProperty($"{DeformersPath}.Array.data[{index}]");
			if (entry == null)
				return;
			row.Q<Toggle>("row-enabled").BindProperty(entry.FindPropertyRelative("enabled"));
			row.Q<ObjectField>("row-deformer").BindProperty(entry.FindPropertyRelative("deformer"));
		}

		private static void UpdateBadge(Label badge, DeformerBase deformer)
		{
			if (deformer == null)
			{
				badge.style.display = DisplayStyle.None;
				return;
			}
			var category = MetaOf(deformer.GetType())?.Category ?? DeformerCategory.Shape;
			badge.text = CategoryLabel(category);
			badge.style.backgroundColor = CategoryColor(category);
			badge.style.display = DisplayStyle.Flex;
		}

		private static string CategoryLabel(DeformerCategory category)
		{
			switch (category)
			{
				case DeformerCategory.Mask: return "マスク";
				case DeformerCategory.Utility: return "補助";
				case DeformerCategory.Experimental: return "実験的";
				default: return "形状";
			}
		}

		private static Color CategoryColor(DeformerCategory category)
		{
			switch (category)
			{
				case DeformerCategory.Mask: return new Color(0.85f, 0.55f, 0.20f, 0.45f);
				case DeformerCategory.Utility: return new Color(0.55f, 0.55f, 0.55f, 0.45f);
				case DeformerCategory.Experimental: return new Color(0.65f, 0.40f, 0.85f, 0.45f);
				default: return new Color(0.30f, 0.50f, 0.85f, 0.45f);
			}
		}

		// ---- フッター(＋ / − とヒント) ----

		private VisualElement MakeFooter()
		{
			var footer = new VisualElement();
			footer.style.flexDirection = FlexDirection.Row;
			footer.style.alignItems = Align.Center;
			footer.style.marginTop = 2;

			var hint = new Label("デフォーマや GameObject をリストへドラッグ、または ＋ で新規作成");
			hint.style.opacity = 0.5f;
			hint.style.fontSize = 10;
			hint.style.flexGrow = 1;
			hint.style.flexShrink = 1;
			footer.Add(hint);

			var remove = new Button(RemoveSelected)
			{
				text = "−",
				tooltip = "選択行をリストから外す(コンポーネント自体は削除しません)",
			};
			remove.style.width = 26;
			footer.Add(remove);

			var add = new Button(ShowAddMenu)
			{
				text = "＋",
				tooltip = "デフォーマを新規作成して追加(子 GameObject として作成されます)",
			};
			add.style.width = 26;
			footer.Add(add);

			return footer;
		}

		private void ShowAddMenu()
		{
			var stack = (DeformStack)target;
			var menu = new GenericMenu();
			foreach (var (type, meta) in DeformerTypes())
			{
				var path = meta.Category == DeformerCategory.Mask ? $"マスク/{meta.Name}" : meta.Name;
				menu.AddItem(new GUIContent(path, meta.Description), false, () =>
				{
					var created = NdmfDeformObjectFactory.CreateDeformer(stack.gameObject, type, meta.Name);
					// バインドの反映を待ってから新しい行を選択する
					if (created != null && _listView != null)
						_listView.schedule.Execute(() =>
						{
							var last = ((DeformStack)target).Deformers.Count - 1;
							if (last >= 0)
								_listView.selectedIndex = last;
						}).ExecuteLater(100);
				});
			}
			menu.ShowAsContext();
		}

		private void RemoveSelected()
		{
			var deformers = serializedObject.FindProperty(DeformersPath);
			var index = _listView != null ? _listView.selectedIndex : -1;
			if (index < 0)
				index = deformers.arraySize - 1;
			if (index < 0 || index >= deformers.arraySize)
				return;

			deformers.DeleteArrayElementAtIndex(index);
			serializedObject.ApplyModifiedProperties();
			_listView?.ClearSelection();
			SetInlineTarget(null);
		}

		private static IEnumerable<(Type type, DeformerMetaAttribute meta)> DeformerTypes()
		{
			return typeof(DeformerBase).Assembly.GetTypes()
				.Where(t => typeof(DeformerBase).IsAssignableFrom(t) && !t.IsAbstract)
				.Select(t => (type: t, meta: MetaOf(t)))
				.Where(p => p.meta != null)
				.OrderBy(p => (int)p.meta.Category)
				.ThenBy(p => p.meta.Name, StringComparer.Ordinal);
		}

		private static DeformerMetaAttribute MetaOf(Type type)
		{
			return (DeformerMetaAttribute)Attribute.GetCustomAttribute(type, typeof(DeformerMetaAttribute));
		}

		// ---- インラインインスペクタ ----

		private void OnRowSelectionChanged(IEnumerable<int> indices)
		{
			var stack = (DeformStack)target;
			DeformerBase deformer = null;
			foreach (var index in indices)
			{
				if (index >= 0 && index < stack.Deformers.Count)
					deformer = stack.Deformers[index].deformer;
				break;
			}
			SetInlineTarget(deformer);
		}

		private void SyncInlineFromSelection()
		{
			var stack = (DeformStack)target;
			var index = _listView != null ? _listView.selectedIndex : -1;
			SetInlineTarget(index >= 0 && index < stack.Deformers.Count
				? stack.Deformers[index].deformer
				: null);
		}

		private void SetInlineTarget(DeformerBase deformer)
		{
			if (deformer == _inlineTarget && (deformer == null || _inlineEditor != null))
				return;

			if (_inlineEditor != null)
			{
				DestroyImmediate(_inlineEditor);
				_inlineEditor = null;
			}
			_inlineTarget = deformer;
			if (ActiveInlineDeformer != deformer)
			{
				ActiveInlineDeformer = deformer;
				PointGridOverlay.InvalidateVisibility();
			}

			if (_inlineContainer != null)
			{
				_inlineContainer.Clear();
				if (deformer != null)
					_inlineContainer.Add(BuildInlineInspector(deformer));
			}
			SceneView.RepaintAll();
		}

		private VisualElement BuildInlineInspector(DeformerBase deformer)
		{
			var box = new VisualElement();
			box.style.borderTopWidth = 1;
			box.style.borderBottomWidth = 1;
			box.style.borderLeftWidth = 1;
			box.style.borderRightWidth = 1;
			var borderColor = new Color(0f, 0f, 0f, 0.3f);
			box.style.borderTopColor = borderColor;
			box.style.borderBottomColor = borderColor;
			box.style.borderLeftColor = borderColor;
			box.style.borderRightColor = borderColor;
			box.style.borderTopLeftRadius = 3;
			box.style.borderTopRightRadius = 3;
			box.style.borderBottomLeftRadius = 3;
			box.style.borderBottomRightRadius = 3;
			box.style.paddingLeft = 4;
			box.style.paddingRight = 4;
			box.style.paddingTop = 2;
			box.style.paddingBottom = 4;

			var headerRow = new VisualElement();
			headerRow.style.flexDirection = FlexDirection.Row;
			headerRow.style.alignItems = Align.Center;

			var title = new Label(deformer.gameObject.name);
			title.style.opacity = 0.7f;
			title.style.fontSize = 10;
			title.style.flexGrow = 1;
			headerRow.Add(title);

			var select = new Button(() =>
			{
				Selection.activeGameObject = deformer.gameObject;
				EditorGUIUtility.PingObject(deformer.gameObject);
			})
			{
				text = "オブジェクトを選択",
				tooltip = "デフォーマの GameObject を選択する",
			};
			select.style.fontSize = 10;
			headerRow.Add(select);
			box.Add(headerRow);

			_inlineEditor = CreateEditor(deformer);
			box.Add(new InspectorElement(_inlineEditor));
			return box;
		}

		// シーンギズモ: 選択中デフォーマの Editor.OnSceneGUI を呼び出して、
		// スタックを選択したままギズモ編集できるようにする
		// (旧 Deformable の ReorderableComponentElementList と同じ方式)
		private void OnSceneGUI()
		{
			if (_inlineEditor == null || _inlineEditor.target == null)
				return;

			var type = _inlineEditor.GetType();
			if (!SceneGuiMethods.TryGetValue(type, out var method))
			{
				method = type.GetMethod("OnSceneGUI",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				SceneGuiMethods[type] = method;
			}
			method?.Invoke(_inlineEditor, null);
		}

		// ---- D&D 追加 ----

		private void RegisterDragAndDrop(VisualElement root)
		{
			root.RegisterCallback<DragUpdatedEvent>(_ =>
			{
				if (GetDraggedDeformers().Count > 0)
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
			});
			root.RegisterCallback<DragPerformEvent>(_ =>
			{
				var dragged = GetDraggedDeformers();
				if (dragged.Count == 0)
					return;
				DragAndDrop.AcceptDrag();

				var deformers = serializedObject.FindProperty(DeformersPath);
				var added = false;
				foreach (var deformer in dragged)
				{
					if (ContainsDeformer(deformers, deformer))
						continue;
					var index = deformers.arraySize;
					deformers.arraySize++;
					var entry = deformers.GetArrayElementAtIndex(index);
					entry.FindPropertyRelative("deformer").objectReferenceValue = deformer;
					entry.FindPropertyRelative("enabled").boolValue = true;
					added = true;
				}
				if (added)
					serializedObject.ApplyModifiedProperties();
			});
		}

		private static List<DeformerBase> GetDraggedDeformers()
		{
			var result = new List<DeformerBase>();
			foreach (var obj in DragAndDrop.objectReferences)
			{
				if (obj is DeformerBase deformer)
					result.Add(deformer);
				else if (obj is GameObject go && go.TryGetComponent<DeformerBase>(out var fromGo))
					result.Add(fromGo);
			}
			return result;
		}

		private static bool ContainsDeformer(SerializedProperty deformers, DeformerBase deformer)
		{
			for (var i = 0; i < deformers.arraySize; i++)
			{
				var entry = deformers.GetArrayElementAtIndex(i);
				if (entry.FindPropertyRelative("deformer").objectReferenceValue == deformer)
					return true;
			}
			return false;
		}
	}
}
