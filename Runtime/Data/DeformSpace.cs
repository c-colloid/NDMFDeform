using Unity.Mathematics;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// メッシュ空間とデフォーマ軸空間の相互変換。
	/// ベイクコアがレンダラーと軸 Transform から一元的に計算して各デフォーマへ渡す
	/// (旧 DeformerUtils.GetMeshToAxisSpace 相当の責務をコアへ集約)。
	/// </summary>
	public readonly struct DeformSpace
	{
		public readonly float4x4 MeshToAxis;
		public readonly float4x4 AxisToMesh;

		public DeformSpace(float4x4 meshToAxis)
		{
			MeshToAxis = meshToAxis;
			AxisToMesh = math.inverse(meshToAxis);
		}
	}
}
