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
	/// ITransientOverlay により LatticeDeformer 選択中のみ表示される。
	/// 操作方法の説明はインスペクタ側の「操作ガイド」にある。
	/// </summary>
	[Overlay(typeof(SceneView), "NDMF Deform Lattice", true)]
	public class PointGridOverlay : Overlay, ITransientOverlay
	{
		// visible は毎フレーム参照されるため、選択変更時のみ再判定する
		private static bool _visibleCache;
		private static bool _visibleCacheValid;

		static PointGridOverlay()
		{
			Selection.selectionChanged += () => _visibleCacheValid = false;
		}

		/// <summary>PointGrid ハンドルを持つデフォーマの選択中のみ表示する</summary>
		public bool visible
		{
			get
			{
				if (!_visibleCacheValid)
				{
					_visibleCache = ComputeVisible();
					_visibleCacheValid = true;
				}
				return _visibleCache;
			}
		}

		/// <summary>選択以外の要因(スタック内のインライン選択)が変わった時の再判定要求</summary>
		internal static void InvalidateVisibility()
		{
			_visibleCacheValid = false;
		}

		internal static bool ComputeVisible()
		{
			// DeformStack 選択中は、リストにラティスが含まれるだけでは表示しない。
			// 実際にラティス行をインライン選択して編集している時のみ表示する
			// (リスト内の別オブジェクトのラティス参照でも、選択されて初めて対象になる)
			if (DeformStackEditor.ActiveInlineDeformer is LatticeDeformer)
				return true;

			foreach (var go in Selection.gameObjects)
			{
				if (go == null) continue;
				if (go.TryGetComponent<LatticeDeformer>(out _))
					return true;
			}
			return false;
		}

		public override VisualElement CreatePanelContent()
		{
			// 構成は PointGridOverlay.uxml / スタイルは NdmfDeform.uss。
			// ここでは EnumField の型初期化・状態反映・コールバック接続と、
			// 解像度依存のスライス番号ボタンの生成だけを行う
			var root = new VisualElement();
			NdmfDeformUI.CloneTree(NdmfDeformUI.PointGridOverlayGuid, root);

			var occlusion = root.Q<EnumField>("occlusion-mode");
			var sliceToggle = root.Q<Toggle>("slice-toggle");
			var sliceAxis = root.Q<EnumField>("slice-axis");
			var strip = root.Q<VisualElement>("slice-strip");
			if (occlusion == null || sliceToggle == null || sliceAxis == null || strip == null)
				return root;

			// 奥点の表示モード
			occlusion.Init(PointGridViewState.OcclusionMode);
			occlusion.RegisterValueChangedCallback(e =>
			{
				PointGridViewState.OcclusionMode = (PointGridOcclusionMode)e.newValue;
				SceneView.RepaintAll();
			});

			// スライス: トグル + 軸、番号ボタンは有効時のみ表示
			sliceToggle.SetValueWithoutNotify(PointGridViewState.SliceEnabled);
			sliceAxis.Init(PointGridViewState.SliceAxis);

			void RebuildStrip()
			{
				strip.Clear();
				var max = SliceMaxIndex();
				for (var i = 0; i <= max; i++)
				{
					var index = i;
					var button = new Button { text = index.ToString() };
					button.AddToClassList("ndmf-overlay-strip-button");
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
			WireCommandButton(root, "select-all", PointGridCommand.SelectAll);
			WireCommandButton(root, "clear-selection", PointGridCommand.ClearSelection);
			WireCommandButton(root, "invert-selection", PointGridCommand.InvertSelection);

			return root;
		}

		private static void StyleStripButton(Button button, bool on)
		{
			button.EnableInClassList("ndmf-overlay-strip-button--on", on);
		}

		private static void WireCommandButton(VisualElement root, string name, PointGridCommand command)
		{
			var button = root.Q<Button>(name);
			if (button == null)
				return;
			button.clicked += () =>
			{
				PointGridCommands.Pending = command;
				SceneView.RepaintAll();
			};
		}

		/// <summary>選択中のラティスの解像度から現在のスライス軸の最大インデックスを得る</summary>
		private static int SliceMaxIndex()
		{
			// スタック経由のインライン編集中はそのラティスを優先する
			var lattice = DeformStackEditor.ActiveInlineDeformer as LatticeDeformer;
			if (lattice == null)
			{
				var go = Selection.activeGameObject;
				lattice = go != null ? go.GetComponent<LatticeDeformer>() : null;
			}
			// 対象ラティスが特定できない場合はボタンを出さない
			// (表示条件により通常ここへは来ない。以前の既定値 15 は
			// 無関係な 16 個の番号ボタンが出て別のラティスの UI に見えてしまっていた)
			if (lattice == null)
				return -1;

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
