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
		UVs      = 1 << 3,
		Colors   = 1 << 4,
		All      = Vertices | Normals | Tangents | UVs | Colors,
	}
}
