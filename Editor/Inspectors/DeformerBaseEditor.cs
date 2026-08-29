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

		public override VisualElement CreateInspectorGUI()
		{
			var root = new VisualElement();

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

		private void OnSceneGUI()
		{
			var deformer = target as DeformerBase;
			if (deformer == null) return;

			var axis = deformer.Axis;
			if (axis == null) return;

			// 未適用の変更があるあいだは Update で巻き戻さない
			if (!_hasPendingChanges)
				serializedObject.UpdateIfRequiredOrScript();

			var builder = new SceneHandleBuilder(serializedObject);
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
					serializedObject.ApplyModifiedProperties();
					_lastApplyTime = now;
					_hasPendingChanges = false;
				}
			}
		}
	}
}
