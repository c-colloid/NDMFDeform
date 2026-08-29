using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// PointGrid(格子ハンドル)の表示・選択操作のコンパクトな SceneView オーバーレイ。
	/// 操作方法の説明はインスペクタ側の「操作ガイド」にある。
	/// </summary>
	[Overlay(typeof(SceneView), "NDMF Deform Lattice", true)]
	public class PointGridOverlay : Overlay
	{
		private static readonly Color StripOnColor = new Color(0.2f, 0.42f, 0.68f);
		private const float LabelWidth = 58;

		public override VisualElement CreatePanelContent()
		{
			var root = new VisualElement();
			root.style.minWidth = 200;

			// 奥点の表示モード
			var occlusion = new EnumField("奥点", PointGridViewState.OcclusionMode);
			occlusion.labelElement.style.minWidth = LabelWidth;
			occlusion.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.OcclusionMode = (PointGridOcclusionMode)e.newValue;
				SceneView.RepaintAll();
			});
			root.Add(occlusion);

			// スライス: トグル + 軸を 1 行に、番号ボタンは有効時のみ表示
			var sliceRow = new VisualElement();
			sliceRow.style.flexDirection = FlexDirection.Row;
			sliceRow.style.alignItems = Align.Center;

			var sliceToggle = new Toggle("スライス") { value = PointGridViewState.SliceEnabled };
			sliceToggle.labelElement.style.minWidth = LabelWidth;
			var sliceAxis = new EnumField(PointGridViewState.SliceAxis);
			sliceAxis.style.width = 46;
			sliceAxis.style.flexShrink = 0;
			sliceRow.Add(sliceToggle);
			sliceRow.Add(sliceAxis);
			root.Add(sliceRow);

			var strip = new VisualElement();
			strip.style.flexDirection = FlexDirection.Row;
			strip.style.flexWrap = Wrap.Wrap;
			strip.style.marginLeft = LabelWidth + 4;
			strip.style.marginBottom = 2;
			root.Add(strip);

			void RebuildStrip()
			{
				strip.Clear();
				var max = SliceMaxIndex();
				for (var i = 0; i <= max; i++)
				{
					var index = i;
					var button = new Button { text = index.ToString() };
					button.style.width = 26;
					button.style.marginLeft = 0;
					button.style.marginRight = 1;
					button.style.flexShrink = 0;
					StyleStripButton(button, PointGridViewState.SliceIndices.Contains(index));
					button.clicked += () =>
					{
						if (!PointGridViewState.SliceIndices.Add(index))
							PointGridViewState.SliceIndices.Remove(index);
						PointGridViewState.SliceVersion++;
						StyleStripButton(button, PointGridViewState.SliceIndices.Contains(index));
						SceneView.RepaintAll();
					};
					strip.Add(button);
				}
			}

			void UpdateStripVisibility()
			{
				strip.style.display = PointGridViewState.SliceEnabled ? DisplayStyle.Flex : DisplayStyle.None;
			}

			sliceToggle.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.SliceEnabled = e.newValue;
				UpdateStripVisibility();
				SceneView.RepaintAll();
			});
			sliceAxis.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.SliceAxis = (HandleAxis)e.newValue;
				PointGridViewState.SliceVersion++;
				RebuildStrip();
				SceneView.RepaintAll();
			});

			RebuildStrip();
			UpdateStripVisibility();

			// 選択や解像度の変化に番号ボタンを追従させる
			var lastMax = SliceMaxIndex();
			root.schedule.Execute(() =>
			{
				var max = SliceMaxIndex();
				if (max != lastMax)
				{
					lastMax = max;
					RebuildStrip();
				}
			}).Every(500);

			// 選択コマンド
			var selectionRow = new VisualElement();
			selectionRow.style.flexDirection = FlexDirection.Row;
			selectionRow.style.alignItems = Align.Center;
			var selectionLabel = new Label("選択");
			selectionLabel.style.minWidth = LabelWidth;
			selectionRow.Add(selectionLabel);
			selectionRow.Add(MakeCommandButton("全", PointGridCommand.SelectAll));
			selectionRow.Add(MakeCommandButton("解除", PointGridCommand.ClearSelection));
			selectionRow.Add(MakeCommandButton("反転", PointGridCommand.InvertSelection));
			root.Add(selectionRow);

			return root;
		}

		private static void StyleStripButton(Button button, bool on)
		{
			button.style.backgroundColor = on ? (StyleColor)StripOnColor : StyleKeyword.Null;
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
