using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
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
			var sliceIndex = new SliderInt("スライス位置", 0, 16)
			{
				value = PointGridViewState.SliceIndex,
				showInputField = true,
			};
			slice.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.SliceEnabled = e.newValue;
				SceneView.RepaintAll();
			});
			sliceAxis.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.SliceAxis = (HandleAxis)e.newValue;
				SceneView.RepaintAll();
			});
			sliceIndex.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.SliceIndex = e.newValue;
				SceneView.RepaintAll();
			});
			root.Add(slice);
			root.Add(sliceAxis);
			root.Add(sliceIndex);

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
	}
}
