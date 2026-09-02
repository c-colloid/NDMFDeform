using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// BodyFitDeformer 専用インスペクタ。
	/// 共通 UI に加えて、体の自動検出・二重球のフィットボタンと、
	/// 参照状態(未設定 / 自己参照 / 重ね着)のメッセージを持つ。
	/// 二重球の編集はシーンビュー(共通ハンドル)で行う。
	/// </summary>
	[CustomEditor(typeof(BodyFitDeformer))]
	[CanEditMultipleObjects]
	public class BodyFitDeformerEditor : DeformerBaseEditor
	{
		// Unity の OnSceneGUI 探索が宣言型のみを走査する場合に備えた明示オーバーライド
		protected override void OnSceneGUI() => base.OnSceneGUI();

		private HelpBox _status;

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
						Undo.RecordObjects(new Object[] { fit, fit.transform }, "Fit Body Fit Sphere");
						fit.FitSphereToParentStack();
						EditorUtility.SetDirty(fit);
					}
					serializedObject.Update();
					SceneView.RepaintAll();
				};

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
	}
}
