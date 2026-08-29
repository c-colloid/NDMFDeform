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
		// UV・カラーは MeshBuffers に対応バッファを実装した時点で追加する
		All      = Vertices | Normals | Tangents,
	}
}
