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
	/// 「表示」「スライス」「選択」の 3 セクションで構成する。
	/// </summary>
	[Overlay(typeof(SceneView), "NDMF Deform Lattice", true)]
	public class PointGridOverlay : Overlay
	{
		private static readonly Color StripOnColor = new Color(0.2f, 0.42f, 0.68f);

		public override VisualElement CreatePanelContent()
		{
			var root = new VisualElement();
			root.style.minWidth = 210;

			// --- 表示 ---
			var displaySection = AddSection(root, "表示");

			var occlusion = new EnumField("奥点の表示", PointGridViewState.OcclusionMode);
			occlusion.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.OcclusionMode = (PointGridOcclusionMode)e.newValue;
				SceneView.RepaintAll();
			});
			displaySection.Add(occlusion);

			// --- スライス ---
			var sliceSection = AddSection(root, "スライス");

			var sliceToggle = new Toggle("スライス表示") { value = PointGridViewState.SliceEnabled };
			var sliceAxis = new EnumField("スライス軸", PointGridViewState.SliceAxis);
			var strip = new VisualElement();
			strip.style.flexDirection = FlexDirection.Row;
			strip.style.flexWrap = Wrap.Wrap;
			strip.style.marginTop = 2;

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

			sliceToggle.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.SliceEnabled = e.newValue;
				strip.SetEnabled(e.newValue);
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
			strip.SetEnabled(PointGridViewState.SliceEnabled);
			sliceSection.Add(sliceToggle);
			sliceSection.Add(sliceAxis);
			sliceSection.Add(strip);

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

			// --- 選択 ---
			var selectionSection = AddSection(root, "選択");

			var hint = new Label(
				"Ctrl+ドラッグ: スワイプ方向の軸で行選択\n" +
				"Ctrl+Shift+ドラッグ: スワイプ方向の軸のリング選択\n" +
				"クリックのみ: 再クリックで軸循環 / ダブルクリック: シート全体");
			hint.style.opacity = 0.6f;
			hint.style.fontSize = 10;
			hint.style.whiteSpace = WhiteSpace.Normal;
			hint.style.marginBottom = 2;
			selectionSection.Add(hint);

			var buttons = new VisualElement();
			buttons.style.flexDirection = FlexDirection.Row;
			buttons.Add(MakeCommandButton("全選択", PointGridCommand.SelectAll));
			buttons.Add(MakeCommandButton("解除", PointGridCommand.ClearSelection));
			buttons.Add(MakeCommandButton("反転", PointGridCommand.InvertSelection));
			selectionSection.Add(buttons);

			return root;
		}

		/// <summary>見出し付きセクションを追加し、内容用コンテナを返す</summary>
		private static VisualElement AddSection(VisualElement root, string title)
		{
			var header = new Label(title);
			header.style.unityFontStyleAndWeight = FontStyle.Bold;
			header.style.fontSize = 11;
			header.style.opacity = 0.75f;
			header.style.marginTop = root.childCount == 0 ? 0 : 8;
			root.Add(header);

			var content = new VisualElement();
			content.style.marginLeft = 6;
			root.Add(content);
			return content;
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
