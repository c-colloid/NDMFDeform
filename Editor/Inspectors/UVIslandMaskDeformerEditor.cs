using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// UVIslandMaskDeformer 専用インスペクタ。
	/// 共通 UI(factor / falloff / invert)に加えて UV 島の選択ビューを持つ。
	/// </summary>
	[CustomEditor(typeof(UVIslandMaskDeformer))]
	[CanEditMultipleObjects]
	public class UVIslandMaskDeformerEditor : DeformerBaseEditor
	{
		// Unity の OnSceneGUI 探索が宣言型のみを走査する場合に備えた明示オーバーライド
		protected override void OnSceneGUI() => base.OnSceneGUI();

		public override VisualElement CreateInspectorGUI()
		{
			var root = base.CreateInspectorGUI();

			if (targets.Length == 1)
			{
				root.Add(new UVIslandSelectorView((UVIslandMaskDeformer)target));
			}
			else
			{
				var note = new Label("UV 島の選択は 1 つずつ編集してください(複数選択中は非表示)。");
				note.style.opacity = 0.7f;
				note.style.whiteSpace = WhiteSpace.Normal;
				root.Add(note);
			}

			var guide = new Foldout { text = "操作ガイド", value = false };
			guide.style.marginTop = 4;
			var hint = new Label(
				"UV マップ上の島をクリックすると選択 / 解除できます。\n" +
				"スタック内でこのマスクより前にあるデフォーマの変形が、\n" +
				"選択した島の頂点で打ち消されます(元の形状に戻ります)。\n" +
				"Invert を有効にすると「選択した島だけに変形を残す」動作になります。\n" +
				"Falloff は島の外側へ変形の打ち消しを UV 距離でぼかします。");
			hint.style.opacity = 0.7f;
			hint.style.whiteSpace = WhiteSpace.Normal;
			guide.Add(hint);
			root.Add(guide);

			return root;
		}
	}
}
