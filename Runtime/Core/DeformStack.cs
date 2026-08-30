using System.Collections.Generic;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// レンダラーに適用するデフォーマの順序付きリスト(旧 Deformable 相当)。
	/// 自身では何もしないパッシブな設定コンポーネント。
	/// SkinnedMeshRenderer または MeshFilter+MeshRenderer を持つ GameObject に付ける。
	/// ベイクは NDMF Transforming フェーズ、可視化は NDMF プレビューが行い、
	/// シーンのレンダラー・sharedMesh はオーサリング中一切書き換えない。
	/// </summary>
	[AddComponentMenu("NDMF Deform/Deform Stack (旧 Deformable)")]
	[DisallowMultipleComponent]
	public class DeformStack : MonoBehaviour
#if NDMFDEFORM_VRCSDK
		, VRC.SDKBase.IEditorOnly
#endif
	{
		[System.Serializable]
		public struct DeformerEntry
		{
			public DeformerBase deformer;
			public bool enabled;
		}

		/// <summary>ベイク時の法線の扱い</summary>
		public enum NormalsMode
		{
			/// <summary>作り込まれた法線を保持する(既定。シームやトゥーン調整を壊さない)</summary>
			PreserveAuthored = 0,

			/// <summary>変形後の形状から再計算する</summary>
			Recalculate = 1,
		}

		/// <summary>ブレンドシェイプのデルタをベイク時にどう扱うか(シェイプ別に上書き可能)</summary>
		public enum BlendShapeDeltaMode
		{
			/// <summary>変形に追従: deformedDelta = Deform(base+delta) − Deform(base)(既定)</summary>
			FollowDeform = 0,

			/// <summary>
			/// 作った形を維持: シェイプ 100% で作者の作った形状(base+delta)そのものになる
			/// (デルタを持つ頂点についてのみ変形を打ち消す)。
			/// 「太さ 0」のような絶対的なターゲットを持つシェイプ向け。
			/// デフォーマの影響範囲がシェイプの影響範囲からはみ出していると境界に段差が出うる。
			/// </summary>
			KeepAuthoredShape = 1,
		}

		[System.Serializable]
		public struct BlendShapeOverride
		{
			public string shapeName;
			public BlendShapeDeltaMode mode;
		}

		[SerializeField] private List<DeformerEntry> deformers = new List<DeformerEntry>();
		[SerializeField] private NormalsMode normalsMode = NormalsMode.PreserveAuthored;

		// 変形が非線形な区間を通るシェイプに中間フレームを自動挿入し、
		// 途中重みでの直線補間による食い込み・行き過ぎを抑える
		[SerializeField] private bool nonlinearShapeCorrection = true;
		[SerializeField] private List<BlendShapeOverride> blendShapeOverrides = new List<BlendShapeOverride>();

		public IReadOnlyList<DeformerEntry> Deformers => deformers;

		public NormalsMode Normals
		{
			get => normalsMode;
			set => normalsMode = value;
		}

		public bool NonlinearShapeCorrection
		{
			get => nonlinearShapeCorrection;
			set => nonlinearShapeCorrection = value;
		}

		public List<BlendShapeOverride> BlendShapeOverrides => blendShapeOverrides;

		public BlendShapeDeltaMode GetBlendShapeMode(string shapeName)
		{
			foreach (var entry in blendShapeOverrides)
			{
				if (entry.shapeName == shapeName)
					return entry.mode;
			}
			return BlendShapeDeltaMode.FollowDeform;
		}

		/// <summary>スタック末尾にデフォーマを追加する(テスト・移行ツール用 API)</summary>
		public void AddDeformer(DeformerBase deformer, bool enabled = true)
		{
			deformers.Add(new DeformerEntry { deformer = deformer, enabled = enabled });
		}
	}
}
