using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// PointGrid(格子ハンドル)の表示・選択操作を集約した SceneView オーバーレイ。
	/// 奥点マスク・スライス表示・ループ選択軸・選択コマンドを提供する。
	/// </summary>
	[Overlay(typeof(SceneView), "NDMF Deform Lattice", true)]
	public class PointGridOverlay : Overlay
	{
		public override VisualElement CreatePanelContent()
		{
			var root = new VisualElement();
			root.style.minWidth = 200;

			var occlusion = new EnumField("奥点の表示", PointGridViewState.OcclusionMode);
			occlusion.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.OcclusionMode = (PointGridOcclusionMode)e.newValue;
				SceneView.RepaintAll();
			});
			root.Add(occlusion);

			var loopAxis = new EnumField("ループ選択軸", PointGridViewState.LoopAxis);
			loopAxis.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.LoopAxis = (HandleAxis)e.newValue;
				SceneView.RepaintAll();
			});
			root.Add(loopAxis);

			var slice = new Toggle("スライス表示") { value = PointGridViewState.SliceEnabled };
			var sliceAxis = new EnumField("スライス軸", PointGridViewState.SliceAxis);
			slice.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.SliceEnabled = e.newValue;
				SceneView.RepaintAll();
			});
			root.Add(slice);
			root.Add(sliceAxis);

			// スライス位置: −/+ ボタンで加減算。上限は選択中ラティスの解像度でクランプ
			var sliceRow = new VisualElement();
			sliceRow.style.flexDirection = FlexDirection.Row;
			sliceRow.style.alignItems = Align.Center;

			var sliceLabel = new Label("スライス位置");
			sliceLabel.style.minWidth = 80;
			sliceLabel.style.flexShrink = 0;
			var minus = new Button { text = "−" };
			minus.style.width = 22;
			minus.style.flexShrink = 0;
			var indexField = new IntegerField { value = PointGridViewState.SliceIndex };
			// IntegerField は既定で flex-grow するため、幅を固定しないと
			// 行の残り幅を占有して + ボタンと上限表示が押し出される
			indexField.style.flexGrow = 0;
			indexField.style.flexShrink = 0;
			indexField.style.width = 44;
			var plus = new Button { text = "+" };
			plus.style.width = 22;
			plus.style.flexShrink = 0;
			var rangeLabel = new Label($"/ {SliceMaxIndex()}");
			rangeLabel.style.opacity = 0.7f;
			rangeLabel.style.marginLeft = 4;
			rangeLabel.style.flexShrink = 0;

			void SetSliceIndex(int value)
			{
				var max = SliceMaxIndex();
				value = Mathf.Clamp(value, 0, max);
				PointGridViewState.SliceIndex = value;
				indexField.SetValueWithoutNotify(value);
				rangeLabel.text = $"/ {max}";
				SceneView.RepaintAll();
			}

			minus.clicked += () => SetSliceIndex(PointGridViewState.SliceIndex - 1);
			plus.clicked += () => SetSliceIndex(PointGridViewState.SliceIndex + 1);
			indexField.RegisterValueChangedCallback(e => SetSliceIndex(e.newValue));
			sliceAxis.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.SliceAxis = (HandleAxis)e.newValue;
				SetSliceIndex(PointGridViewState.SliceIndex);
			});

			sliceRow.Add(sliceLabel);
			sliceRow.Add(minus);
			sliceRow.Add(indexField);
			sliceRow.Add(plus);
			sliceRow.Add(rangeLabel);
			root.Add(sliceRow);

			// 選択や解像度の変化に上限表示を追従させる
			root.schedule.Execute(() =>
			{
				var max = SliceMaxIndex();
				rangeLabel.text = $"/ {max}";
				if (PointGridViewState.SliceIndex > max)
					SetSliceIndex(max);
			}).Every(500);

			var buttons = new VisualElement();
			buttons.style.flexDirection = FlexDirection.Row;
			buttons.Add(MakeCommandButton("全選択", PointGridCommand.SelectAll));
			buttons.Add(MakeCommandButton("解除", PointGridCommand.ClearSelection));
			buttons.Add(MakeCommandButton("反転", PointGridCommand.InvertSelection));
			root.Add(buttons);

			return root;
		}

		private static Button MakeCommandButton(string label, PointGridCommand command)
		{
			return new Button(() =>
			{
				PointGridCommands.Pending = command;
				SceneView.RepaintAll();
			}) { text = label };
		}

		/// <summary>選択中のラティスの解像度から現在のスライス軸の最大インデックスを得る</summary>
		private static int SliceMaxIndex()
		{
			var go = Selection.activeGameObject;
			var lattice = go != null ? go.GetComponent<LatticeDeformer>() : null;
			if (lattice == null)
				return 15;

			var res = lattice.Resolution;
			switch (PointGridViewState.SliceAxis)
			{
				case HandleAxis.X: return res.x - 1;
				case HandleAxis.Y: return res.y - 1;
				default: return res.z - 1;
			}
		}
	}
}
