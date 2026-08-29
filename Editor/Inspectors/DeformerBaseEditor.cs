using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// 全 DeformerBase 派生の共通エディタ。
	/// インスペクタ: [DeformerMeta] ヘッダ + 自動生成プロパティ(UITK)。
	/// シーン: DescribeHandles 宣言を SceneHandleBuilder で描画・編集する。
	/// ドラッグ中の ApplyModifiedProperties は一定間隔に間引き、
	/// NDMF プレビューの再ベイク頻度を抑える(簡易ホットパス)。
	/// </summary>
	[CustomEditor(typeof(DeformerBase), true)]
	[CanEditMultipleObjects]
	public class DeformerBaseEditor : UnityEditor.Editor
	{
		/// <summary>ドラッグ中に変更を適用する最小間隔(秒)。約 20Hz</summary>
		private const double DragApplyInterval = 0.05;

		private double _lastApplyTime;
		private bool _hasPendingChanges;

		// OnSceneGUI 内で Editor.serializedObject を使うと Unity が警告するため、
		// シーン用にターゲット毎の SerializedObject を自前で保持する
		private readonly System.Collections.Generic.Dictionary<int, SerializedObject> _sceneSerializedObjects =
			new System.Collections.Generic.Dictionary<int, SerializedObject>();
		private readonly System.Collections.Generic.Dictionary<int,
				System.Collections.Generic.Dictionary<string, PointGridController>> _pointGrids =
			new System.Collections.Generic.Dictionary<int,
				System.Collections.Generic.Dictionary<string, PointGridController>>();

		protected virtual void OnDisable()
		{
			foreach (var so in _sceneSerializedObjects.Values)
				so?.Dispose();
			_sceneSerializedObjects.Clear();

			// 点選択中に隠した標準 Transform ギズモを戻す
			foreach (var grids in _pointGrids.Values)
				foreach (var controller in grids.Values)
					controller.ReleaseToolsHidden();
			_pointGrids.Clear();
		}

		private SerializedObject GetSceneSerializedObject(DeformerBase deformer)
		{
			var id = deformer.GetInstanceID();
			if (!_sceneSerializedObjects.TryGetValue(id, out var so) || so == null)
			{
				so = new SerializedObject(deformer);
				_sceneSerializedObjects[id] = so;
			}
			return so;
		}

		private System.Collections.Generic.Dictionary<string, PointGridController> GetPointGrids(DeformerBase deformer)
		{
			var id = deformer.GetInstanceID();
			if (!_pointGrids.TryGetValue(id, out var grids))
			{
				grids = new System.Collections.Generic.Dictionary<string, PointGridController>();
				_pointGrids[id] = grids;
			}
			return grids;
		}

		public override VisualElement CreateInspectorGUI()
		{
			var root = new VisualElement();
			NdmfDeformFonts.ApplyEditorUiFont(root);

			var meta = (DeformerMetaAttribute)System.Attribute.GetCustomAttribute(
				target.GetType(), typeof(DeformerMetaAttribute));
			if (meta != null)
			{
				if (!string.IsNullOrEmpty(meta.Name))
				{
					var title = new Label(meta.Name);
					title.style.unityFontStyleAndWeight = FontStyle.Bold;
					title.style.marginTop = 2;
					root.Add(title);
				}
				if (!string.IsNullOrEmpty(meta.Description))
				{
					var description = new Label(meta.Description);
					description.style.opacity = 0.7f;
					description.style.whiteSpace = WhiteSpace.Normal;
					description.style.marginBottom = 4;
					root.Add(description);
				}
			}

			InspectorElement.FillDefaultInspector(root, serializedObject, this);
			return root;
		}

		// 注意: private にすると派生エディタ(LatticeDeformerEditor 等)経由で
		// Unity のコールバック探索から見えなくなり、シーンハンドルが描画されない
		protected virtual void OnSceneGUI()
		{
			var deformer = target as DeformerBase;
			if (deformer == null) return;

			var axis = deformer.Axis;
			if (axis == null) return;

			var sceneSerializedObject = GetSceneSerializedObject(deformer);

			// 未適用の変更があるあいだは Update で巻き戻さない
			if (!_hasPendingChanges)
				sceneSerializedObject.UpdateIfRequiredOrScript();

			var builder = new SceneHandleBuilder(sceneSerializedObject, GetPointGrids(deformer));
			using (new Handles.DrawingScope(Matrix4x4.TRS(axis.position, axis.rotation, axis.lossyScale)))
			{
				deformer.DescribeHandles(builder);
			}

			if (builder.Changed)
				_hasPendingChanges = true;

			if (_hasPendingChanges)
			{
				var dragging = GUIUtility.hotControl != 0;
				var now = EditorApplication.timeSinceStartup;
				if (!dragging || now - _lastApplyTime >= DragApplyInterval)
				{
					sceneSerializedObject.ApplyModifiedProperties();
					_lastApplyTime = now;
					_hasPendingChanges = false;
				}
			}
		}
	}
}
