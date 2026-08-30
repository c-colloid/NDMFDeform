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
			// 構成は DeformStackInspector.uxml / スタイルは NdmfDeform.uss。
			// ここでは要素の取得とバインド・コールバックの接続だけを行う
			var root = new VisualElement();
			NdmfDeformUI.CloneTree(NdmfDeformUI.StackInspectorGuid, root);

			_listView = root.Q<ListView>("deformer-list");
			if (_listView != null)
			{
				// binding-path は UXML にも書いてあるが、Unity バージョンによって
				// ListView の UXML 属性として解釈されないことがあるため C# 側でも保証する
				_listView.bindingPath = DeformersPath;
				_listView.makeItem = MakeRow;
				_listView.bindItem = BindRow;
				_listView.unbindItem = (element, _) => element.Unbind();
				_listView.selectedIndicesChanged += OnRowSelectionChanged;
				_listView.itemIndexChanged += (_, _) => SyncInlineFromSelection();
			}

			var remove = root.Q<Button>("remove-button");
			if (remove != null)
				remove.clicked += RemoveSelected;
			var add = root.Q<Button>("add-button");
			if (add != null)
				add.clicked += ShowAddMenu;

			_inlineContainer = root.Q<VisualElement>("inline-container");

			RegisterDragAndDrop(root);
			return root;
		}

		// ---- 一覧の行 ----

		private VisualElement MakeRow()
		{
			// 行の構成は DeformStackRow.uxml
			var row = new VisualElement();
			NdmfDeformUI.CloneTree(NdmfDeformUI.StackRowGuid, row);

			var toggle = row.Q<Toggle>("row-enabled");
			var field = row.Q<ObjectField>("row-deformer");
			var badge = row.Q<Label>("row-badge");
			if (toggle == null || field == null)
				return row;

			field.objectType = typeof(DeformerBase);

			toggle.RegisterValueChangedCallback(evt => ApplyEnabledVisual(row, evt.newValue));
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
			var enabledProp = entry.FindPropertyRelative("enabled");
			row.Q<Toggle>("row-enabled")?.BindProperty(enabledProp);
			row.Q<ObjectField>("row-deformer")?.BindProperty(entry.FindPropertyRelative("deformer"));
			ApplyEnabledVisual(row, enabledProp.boolValue);
		}

		/// <summary>
		/// 行の有効状態の見た目を反映する。淡色化は USS
		/// (.ndmf-deformer-row--disabled)、目のアイコン画像だけここで差し替える
		/// (エディタ内蔵アイコンのため USS からは参照できない)。
		/// </summary>
		private static void ApplyEnabledVisual(VisualElement row, bool enabled)
		{
			row.Q(className: "ndmf-deformer-row")
				?.EnableInClassList("ndmf-deformer-row--disabled", !enabled);

			var eye = row.Q<Toggle>("row-enabled")?.Q<VisualElement>("unity-checkmark");
			if (eye != null)
			{
				var icon = enabled ? EyeOnIcon : EyeOffIcon;
				if (icon != null)
					eye.style.backgroundImage = new StyleBackground(icon);
				eye.style.opacity = enabled ? 0.9f : 0.45f;
			}
		}

		private static Texture2D _eyeOnIcon;
		private static Texture2D _eyeOffIcon;

		private static Texture2D EyeOnIcon
		{
			get
			{
				if (_eyeOnIcon == null)
					_eyeOnIcon = LoadIcon("scenevis_visible_hover", "animationvisibilitytoggleon");
				return _eyeOnIcon;
			}
		}

		private static Texture2D EyeOffIcon
		{
			get
			{
				if (_eyeOffIcon == null)
					_eyeOffIcon = LoadIcon("scenevis_hidden_hover", "animationvisibilitytoggleoff");
				return _eyeOffIcon;
			}
		}

		private static Texture2D LoadIcon(params string[] names)
		{
			foreach (var name in names)
			{
				var content = EditorGUIUtility.IconContent(name);
				if (content?.image is Texture2D texture)
					return texture;
			}
			return null;
		}

		/// <summary>
		/// カテゴリバッジの文言と色を反映する。色は USS のモディファイアクラス
		/// (.ndmf-row-badge--*)で定義され、クラスが無い状態は非表示になる。
		/// </summary>
		private static void UpdateBadge(Label badge, DeformerBase deformer)
		{
			if (badge == null)
				return;
			foreach (var className in BadgeClasses.Values)
				badge.RemoveFromClassList(className);
			if (deformer == null)
				return;

			var category = MetaOf(deformer.GetType())?.Category ?? DeformerCategory.Shape;
			badge.text = CategoryLabel(category);
			badge.AddToClassList(BadgeClasses.TryGetValue(category, out var badgeClass)
				? badgeClass
				: BadgeClasses[DeformerCategory.Shape]);
		}

		private static readonly Dictionary<DeformerCategory, string> BadgeClasses =
			new Dictionary<DeformerCategory, string>
			{
				{ DeformerCategory.Shape, "ndmf-row-badge--shape" },
				{ DeformerCategory.Mask, "ndmf-row-badge--mask" },
				{ DeformerCategory.Utility, "ndmf-row-badge--utility" },
				{ DeformerCategory.Experimental, "ndmf-row-badge--experimental" },
			};

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

		private void ShowAddMenu()
		{
			var stack = (DeformStack)target;
			var menu = new GenericMenu();
			foreach (var (type, meta) in DeformerTypes())
			{
				// GameObject メニュー(Deformers/Mask/...)と同じ階層表記に合わせる
				var path = meta.Category == DeformerCategory.Mask ? $"Mask/{meta.Name}" : meta.Name;
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
			// 枠の構成は DeformStackInline.uxml
			var box = new VisualElement();
			NdmfDeformUI.CloneTree(NdmfDeformUI.StackInlineGuid, box);

			var title = box.Q<Label>("inline-title");
			if (title != null)
				title.text = deformer.gameObject.name;

			var select = box.Q<Button>("inline-select");
			if (select != null)
				select.clicked += () =>
				{
					Selection.activeGameObject = deformer.gameObject;
					EditorGUIUtility.PingObject(deformer.gameObject);
				};

			_inlineEditor = CreateEditor(deformer);
			(box.Q<VisualElement>("inline-editor-slot") ?? box).Add(new InspectorElement(_inlineEditor));
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
