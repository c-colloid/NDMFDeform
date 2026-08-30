using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEditor.UIElements;
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

			// 解像度は確定時(Enter / フォーカスアウト)にのみ反映する。
			// 即時反映だと入力途中の "1" が OnValidate の最小値クランプで "2" に
			// 上書きされ、"11" が "21" になってしまう
			root.schedule.Execute(() =>
			{
				root.Query<PropertyField>().ForEach(pf =>
				{
					if (pf.bindingPath == "resolution")
						pf.Query<IntegerField>().ForEach(f => f.isDelayed = true);
				});
			});

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

			var guide = new Foldout { text = "操作ガイド", value = false };
			guide.style.marginTop = 4;
			var hint = new Label(
				"制御点はシーンビューで編集します。\n" +
				"クリック選択 / Shift 追加 / ドラッグで矩形選択\n" +
				"Ctrl+ドラッグ: 点から軸ガイドが出て、スワイプ方向の軸で行選択\n" +
				"Ctrl+Shift+ドラッグ: スワイプ方向の軸のリング(ループ)選択\n" +
				"クリックのみの場合は再クリックで軸循環、\n" +
				"Ctrl+Shift+ダブルクリックでシート全体に拡張します。\n" +
				"選択中は W/E/R(移動 / 回転 / スケール)ツールが\n" +
				"選択点に適用されます。Ctrl+A で全選択、\n" +
				"Esc または空クリックで選択解除。\n" +
				"表示設定(奥点フェード・スライス)は SceneView の\n" +
				"「NDMF Deform Lattice」オーバーレイから切替えられます。");
			hint.style.opacity = 0.7f;
			hint.style.whiteSpace = WhiteSpace.Normal;
			guide.Add(hint);
			root.Add(guide);

			return root;
		}
	}
}
