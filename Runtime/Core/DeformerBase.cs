using Unity.Jobs;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 全デフォーマの基底クラス。
	/// パッシブな設定コンポーネントであり、更新ループを持たない。
	/// 軸として使う Transform の GameObject に付ける。
	/// 変形の実行はベイクコア(Editor 側)が Schedule を呼ぶことでのみ行われる。
	/// </summary>
	public abstract class DeformerBase : MonoBehaviour
#if NDMFDEFORM_VRCSDK
		, VRC.SDKBase.IEditorOnly
#endif
	{
		/// <summary>このデフォーマが変更するデータチャンネル</summary>
		public abstract DeformDataFlags DataFlags { get; }

		/// <summary>
		/// 変形の基準となる軸 Transform。既定は自身の Transform
		/// (デフォーマは軸として使う GameObject に付ける)。
		/// ベイクコアはこの Transform から DeformSpace を計算する。
		/// </summary>
		public virtual Transform Axis => transform;

		/// <summary>
		/// 変形ジョブをスケジュールする。
		/// バッファへのインプレース変形のみ許可(頂点数・順序は変更不可)。
		/// </summary>
		public abstract JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency);
	}
}
