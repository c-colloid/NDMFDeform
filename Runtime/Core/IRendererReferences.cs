using System.Collections.Generic;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 自身のレンダラー以外のレンダラー(参照する体のメッシュなど)を入力に使うデフォーマが実装する。
	/// ベイクコア外の連携で使う:
	/// - NDMF プレビュー: 参照先のレンダラー・Transform・(あれば)その DeformStack を監視対象に加える
	/// - ビルド: 参照先に DeformStack がある場合、そちらを先にベイクする(重ね着の順序保証)
	/// </summary>
	public interface IRendererReferences
	{
		/// <summary>参照しているレンダラーを results に追加する(null は追加しない)</summary>
		void CollectReferencedRenderers(List<Renderer> results);
	}
}
