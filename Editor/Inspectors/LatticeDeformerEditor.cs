using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// LatticeDeformer 専用インスペクタ。
	/// 共通 UI に加えて、解像度の適用(制御点リセット)とバウンズフィットのボタンを持つ。
	/// 制御点の編集はシーンビュー(PointGrid ハンドル)で行う。
	/// </summary>
	[CustomEditor(typeof(LatticeDeformer))]
	[CanEditMultipleObjects]
	public class LatticeDeformerEditor : DeformerBaseEditor
	{
		// Unity の OnSceneGUI 探索が宣言型のみを走査する場合に備えた明示オーバーライド
		protected override void OnSceneGUI() => base.OnSceneGUI();

		public override VisualElement CreateInspectorGUI()
		{
			var root = base.CreateInspectorGUI();

			var buttons = new VisualElement();
			buttons.style.flexDirection = FlexDirection.Row;
			buttons.style.marginTop = 4;

			// 解像度の変更は OnValidate で自動適用(既存変形はリサンプリングで引継ぎ)されるため、
			// ここは恒等格子への明示リセットのみ
			var resetPoints = new Button(() =>
			{
				foreach (var t in targets)
				{
					if (t is not LatticeDeformer lattice) continue;
					Undo.RecordObject(lattice, "Reset Lattice Control Points");
					lattice.GenerateControlPoints(lattice.Resolution);
					EditorUtility.SetDirty(lattice);
				}
				SceneView.RepaintAll();
			}) { text = "制御点をリセット" };

			var fitBounds = new Button(() =>
			{
				foreach (var t in targets)
				{
					if (t is not LatticeDeformer lattice) continue;
					Undo.RecordObject(lattice.transform, "Fit Lattice To Bounds");
					lattice.FitToParentStack();
				}
				SceneView.RepaintAll();
			}) { text = "バウンズへフィット" };

			buttons.Add(resetPoints);
			buttons.Add(fitBounds);
			root.Add(buttons);

			var hint = new Label(
				"制御点はシーンビューで編集します:\n" +
				"クリック選択 / Shift 追加 / ドラッグで矩形選択\n" +
				"Ctrl+クリック: ループ選択 / Ctrl+Shift+クリック: シート選択\n" +
				"表示設定(奥点フェード・スライス・ループ軸)は SceneView の\n" +
				"「NDMF Deform Lattice」オーバーレイから切替えられます。");
			hint.style.opacity = 0.6f;
			hint.style.whiteSpace = WhiteSpace.Normal;
			hint.style.marginTop = 4;
			root.Add(hint);

			return root;
		}
	}
}
