using System;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// デフォーマが変更するメッシュデータチャンネル。
	/// ベイクコアはこのフラグを見て、必要なバッファの構築と書き戻しを最小化する。
	/// </summary>
	[Flags]
	public enum DeformDataFlags
	{
		None     = 0,
		Vertices = 1 << 0,
		Normals  = 1 << 1,
		Tangents = 1 << 2,

		// ---- 読み取り専用チャンネル(要求すると構築されるが書き戻しはされない) ----

		/// <summary>UV0(読み取り専用)</summary>
		UVs = 1 << 3,

		/// <summary>スタック適用前の頂点スナップショット(読み取り専用)。マスク系が使用する</summary>
		OriginalVertices = 1 << 4,

		/// <summary>頂点カラー(読み取り専用)</summary>
		Colors = 1 << 5,

		/// <summary>書き戻し対象の全チャンネル(読み取り専用チャンネルは含まない)</summary>
		All = Vertices | Normals | Tangents,
	}
}
