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
		/// ベイク直前にソースメッシュ全体を参照する解析を行う任意フック
		/// (UV 島解析など、NativeArray 化されない情報が必要な場合に使う)。
		/// ベイクコアが Schedule より前にメインスレッドで呼ぶ。
		/// 結果はインスタンス内にキャッシュし、Schedule で参照すること。
		/// </summary>
		public virtual void PrepareBake(Mesh source) { }

		/// <summary>
		/// 変形ジョブをスケジュールする。
		/// バッファへのインプレース変形のみ許可(頂点数・順序は変更不可)。
		/// buffers.Vertices 以外のチャンネルは元メッシュに存在しない場合
		/// 未生成のことがあるため、使用前に IsCreated を確認すること。
		/// </summary>
		public abstract JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency);

#if UNITY_EDITOR
		/// <summary>
		/// 編集ハンドルを宣言する(任意)。宣言は軸空間で解釈される。
		/// 描画・バインド・Undo はフレームワーク(DeformerBaseEditor)が処理する。
		/// </summary>
		public virtual void DescribeHandles(IHandleBuilder h) { }
#endif
	}
}
