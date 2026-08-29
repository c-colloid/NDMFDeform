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

		[SerializeField] private List<DeformerEntry> deformers = new List<DeformerEntry>();

		public IReadOnlyList<DeformerEntry> Deformers => deformers;
	}
}
