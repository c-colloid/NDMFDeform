using System;
using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// BodyFitDeformer 専用インスペクタ。
	/// 共通 UI に加えて、体の自動検出・二重球のフィットボタン、
	/// 参照状態(未設定 / 自己参照 / 重ね着)のメッセージ、
	/// パーツ所属の一覧(グループごとの投票結果・要確認・手動上書き)を持つ。
	/// 二重球の編集はシーンビュー(共通ハンドル)で行う。
	/// </summary>
	[CustomEditor(typeof(BodyFitDeformer))]
	[CanEditMultipleObjects]
	public class BodyFitDeformerEditor : DeformerBaseEditor
	{
		// Unity の OnSceneGUI 探索が宣言型のみを走査する場合に備えた明示オーバーライド
		protected override void OnSceneGUI() => base.OnSceneGUI();

		private HelpBox _status;
		private VisualElement _partsList;
		private Label _partsSummary;
		private Toggle _showAllParts;
		private bool _partsAnalyzed;

		private static readonly List<BodyPart> PartChoices = BuildPartChoices();

		private static List<BodyPart> BuildPartChoices()
		{
			var list = new List<BodyPart>();
			foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
				list.Add(part);
			return list;
		}

		public override VisualElement CreateInspectorGUI()
		{
			var root = base.CreateInspectorGUI();

			// ボタンと操作ガイドの構成は BodyFitInspector.uxml
			NdmfDeformUI.CloneTree(NdmfDeformUI.BodyFitInspectorGuid, root);

			var statusSlot = root.Q<VisualElement>("status-slot");
			if (statusSlot != null)
			{
				_status = new HelpBox(string.Empty, HelpBoxMessageType.Info);
				_status.style.display = DisplayStyle.None;
				statusSlot.Add(_status);
				RefreshStatus();
				// Body の差し替え等で状態メッセージを更新する
				root.TrackSerializedObjectValue(serializedObject, _ => RefreshStatus());
			}

			var detect = root.Q<Button>("detect-body");
			if (detect != null)
				detect.clicked += () =>
				{
					foreach (var t in targets)
					{
						if (t is not BodyFitDeformer fit) continue;
						Undo.RecordObject(fit, "Detect Body");
						if (!fit.AutoDetectBody())
							Debug.LogWarning("[NDMF Deform] 同じアバター内に \"Body\" という名前の SkinnedMeshRenderer が見つかりません。Body を手動で指定してください", fit);
						EditorUtility.SetDirty(fit);
					}
					serializedObject.Update();
					RefreshStatus();
				};

			var fitSphere = root.Q<Button>("fit-sphere");
			if (fitSphere != null)
				fitSphere.clicked += () =>
				{
					foreach (var t in targets)
					{
						if (t is not BodyFitDeformer fit) continue;
						Undo.RecordObjects(new UnityEngine.Object[] { fit, fit.transform }, "Fit Body Fit Sphere");
						fit.FitSphereToParentStack();
						EditorUtility.SetDirty(fit);
					}
					serializedObject.Update();
					SceneView.RepaintAll();
				};

			_partsList = root.Q<VisualElement>("parts-list");
			_partsSummary = root.Q<Label>("parts-summary");
			_showAllParts = root.Q<Toggle>("parts-show-all");
			_showAllParts?.RegisterValueChangedCallback(_ => RebuildPartRows());
			var analyze = root.Q<Button>("analyze-parts");
			if (analyze != null)
				analyze.clicked += RefreshParts;
			var partsFoldout = root.Q<Foldout>("bodyfit-parts");
			if (partsFoldout != null && targets.Length > 1)
				partsFoldout.style.display = DisplayStyle.None;

			return root;
		}

		private void RefreshStatus()
		{
			if (_status == null || target is not BodyFitDeformer fit)
				return;

			string message = null;
			var type = HelpBoxMessageType.Info;
			var body = fit.Body;
			if (body == null)
			{
				message = "Body が未設定です。沿わせる体のレンダラーを指定するか「体を自動検出」を押してください。";
				type = HelpBoxMessageType.Warning;
			}
			else if (body == fit.GetOwnRenderer())
			{
				message = "Body に衣装自身のレンダラーが指定されています。体のレンダラーを指定してください(この状態では何もしません)。";
				type = HelpBoxMessageType.Error;
			}
			else if (body is not SkinnedMeshRenderer && !body.TryGetComponent<MeshFilter>(out _))
			{
				message = "Body のレンダラーにメッシュがありません(SkinnedMeshRenderer または MeshFilter 付きの MeshRenderer を指定してください)。";
				type = HelpBoxMessageType.Error;
			}
			else if (body.TryGetComponent<DeformStack>(out var bodyStack) &&
			         DeformBakeCore.CollectEnabledDeformers(bodyStack).Count > 0)
			{
				message = "Body 側にも Deform Stack があります。体側を先にベイクし、その変形後の形状へフィットします(重ね着)。";
			}
			else if (fit.Mode == BodyFitDeformer.FitMode.PartCylinder && fit.FindHumanoidAnimator() == null)
			{
				// 実行時と同じ探索(体の親 → 衣装の親)で判定する
				message = "Part Cylinder にはヒューマノイドの Animator(体または衣装の親)が必要です。見つからない場合は Nearest Surface で動作します。";
				type = HelpBoxMessageType.Warning;
			}

			_status.text = message ?? string.Empty;
			_status.messageType = type;
			_status.style.display = message == null ? DisplayStyle.None : DisplayStyle.Flex;
		}

		// ---- パーツ所属 ----

		/// <summary>衣装メッシュのパーツ所属を計算し直して一覧を更新する</summary>
		private void RefreshParts()
		{
			if (_partsList == null || target is not BodyFitDeformer fit)
				return;
			_partsAnalyzed = false;
			try
			{
				_partsAnalyzed = fit.AnalyzeParts();
			}
			catch (Exception e)
			{
				Debug.LogException(e, fit);
			}
			RebuildPartRows();
		}

		private void RebuildPartRows()
		{
			if (_partsList == null || target is not BodyFitDeformer fit)
				return;
			_partsList.Clear();

			if (!_partsAnalyzed)
			{
				if (_partsSummary != null)
					_partsSummary.text = fit.Body == null
						? "Body を指定してから「解析を更新」を押してください。"
						: "パーツ所属を計算できません(ヒューマノイドの骨格、または体のメッシュが使えません)。";
				return;
			}

			var reports = fit.PartReports;
			if (fit.Grouping == BodyFitDeformer.PartGrouping.None || reports.Count == 0)
			{
				if (_partsSummary != null)
					_partsSummary.text = "グループ化なし(頂点ごとの所属)。Part Grouping を UV Islands / Connected Components にすると一覧と上書きが使えます。";
				return;
			}

			var review = 0;
			var overridden = 0;
			foreach (var r in reports)
			{
				if (r.NeedsReview) review++;
				if (r.Decision == PartDecision.Override) overridden++;
			}
			if (_partsSummary != null)
				_partsSummary.text = $"{reports.Count} グループ / 要確認 {review} 件 / 上書き {overridden} 件。" +
				                     "⚠ はウェイトと体の形状の判定が食い違う、または複数パーツにまたがるグループです。";

			var showAll = _showAllParts != null && _showAllParts.value;
			var sorted = new List<PartGroupReport>(reports);
			sorted.Sort((a, b) =>
			{
				var ra = a.NeedsReview ? 0 : 1;
				var rb = b.NeedsReview ? 0 : 1;
				if (ra != rb) return ra.CompareTo(rb);
				return b.VertexCount.CompareTo(a.VertexCount);
			});
			foreach (var r in sorted)
			{
				if (!showAll && !r.NeedsReview && r.Decision != PartDecision.Override)
					continue;
				_partsList.Add(CreatePartRow(fit, r));
			}
		}

		private VisualElement CreatePartRow(BodyFitDeformer fit, PartGroupReport r)
		{
			var row = new VisualElement();
			row.AddToClassList("ndmf-row");
			row.style.alignItems = Align.Center;

			var label = new Label(DescribeReport(r))
			{
				tooltip = DescribeEvidence(r),
			};
			label.style.flexGrow = 1f;
			label.style.whiteSpace = WhiteSpace.Normal;
			row.Add(label);

			var current = fit.GetPartOverride(r);
			var popup = new PopupField<BodyPart>(PartChoices, current, FormatPart, FormatPart)
			{
				tooltip = "このグループの所属パーツを指定します(自動 = 投票結果)",
			};
			popup.style.minWidth = 130f;
			popup.RegisterValueChangedCallback(evt =>
			{
				Undo.RecordObject(fit, "Body Fit Part Override");
				fit.SetPartOverride(r, evt.newValue);
				EditorUtility.SetDirty(fit);
				serializedObject.Update();
				RefreshParts();
			});
			row.Add(popup);
			return row;
		}

		private static string FormatPart(BodyPart part)
		{
			return part == BodyPart.None ? "自動" : part.ToString();
		}

		private static string DescribeReport(PartGroupReport r)
		{
			var unit = r.IsIsland ? "島" : "成分";
			var head = (r.NeedsReview ? "⚠ " : string.Empty) + $"{unit} #{r.Group}({r.VertexCount} 頂点)";
			switch (r.Decision)
			{
				case PartDecision.Override:
					return $"{head} 上書き: {r.Part}";
				case PartDecision.Unified:
					return $"{head} → {r.Part}({r.Confidence:P0})";
				case PartDecision.PerVertex:
					return $"{head} 頂点ごと(上位 {r.Part} {r.Confidence:P0})";
				default:
					return $"{head} 所属なし";
			}
		}

		private static string DescribeEvidence(PartGroupReport r)
		{
			var bone = r.BonePart == BodyPart.None
				? "ウェイト: なし"
				: $"ウェイト: {r.BonePart} {r.BoneConfidence:P0}(対応付けの信頼度 {r.BoneMapConfidence:P0})";
			var geo = r.GeometryPart == BodyPart.None
				? "形状: なし"
				: $"形状: {r.GeometryPart} {r.GeometryConfidence:P0}";
			return $"{bone}\n{geo}\n大きさ {r.Size:F3} m";
		}
	}
}
