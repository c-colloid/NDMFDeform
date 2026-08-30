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

			// ボタンと操作ガイドの構成は LatticeInspector.uxml
			NdmfDeformUI.CloneTree(NdmfDeformUI.LatticeInspectorGuid, root);

			// 解像度の変更は OnValidate で自動適用(既存変形はリサンプリングで引継ぎ)されるため、
			// ここは恒等格子への明示リセットのみ
			var resetPoints = root.Q<Button>("reset-points");
			if (resetPoints != null)
				resetPoints.clicked += () =>
				{
					foreach (var t in targets)
					{
						if (t is not LatticeDeformer lattice) continue;
						Undo.RecordObject(lattice, "Reset Lattice Control Points");
						lattice.GenerateControlPoints(lattice.Resolution);
						EditorUtility.SetDirty(lattice);
					}
					SceneView.RepaintAll();
				};

			var fitBounds = root.Q<Button>("fit-bounds");
			if (fitBounds != null)
				fitBounds.clicked += () =>
				{
					foreach (var t in targets)
					{
						if (t is not LatticeDeformer lattice) continue;
						Undo.RecordObject(lattice.transform, "Fit Lattice To Bounds");
						lattice.FitToParentStack();
					}
					SceneView.RepaintAll();
				};

			return root;
		}
	}
}
