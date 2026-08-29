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

		[SerializeField] private List<DeformerEntry> deformers = new List<DeformerEntry>();
		[SerializeField] private NormalsMode normalsMode = NormalsMode.PreserveAuthored;

		public IReadOnlyList<DeformerEntry> Deformers => deformers;

		public NormalsMode Normals
		{
			get => normalsMode;
			set => normalsMode = value;
		}

		/// <summary>スタック末尾にデフォーマを追加する(テスト・移行ツール用 API)</summary>
		public void AddDeformer(DeformerBase deformer, bool enabled = true)
		{
			deformers.Add(new DeformerEntry { deformer = deformer, enabled = enabled });
		}
	}
}
