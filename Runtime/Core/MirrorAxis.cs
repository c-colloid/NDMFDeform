namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 編集時ミラーの対称軸。
	/// PointGrid(格子ハンドル)での選択・移動を対称側の制御点へ反映するために使う。
	/// ベイク結果は制御点の位置のみで決まる(ミラーは編集支援であり、ジョブには関与しない)。
	/// </summary>
	public enum MirrorAxis
	{
		None = 0,
		X = 1,
		Y = 2,
		Z = 3,
	}
}
