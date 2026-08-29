using Unity.Mathematics;
using UnityEngine;

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

		/// <summary>
		/// レンダラーの Transform(メインスレッド専用。ジョブへ渡さず、
		/// Schedule 内で行列などへ変換して使うこと)。
		/// TransformDeformer のように軸空間以外の情報が要るデフォーマが参照する。
		/// 直接構築された場合など null のことがあるため、使用側は null を許容すること。
		/// </summary>
		public readonly Transform RendererTransform;

		public DeformSpace(float4x4 meshToAxis) : this(meshToAxis, null) { }

		public DeformSpace(float4x4 meshToAxis, Transform rendererTransform)
		{
			MeshToAxis = meshToAxis;
			AxisToMesh = math.inverse(meshToAxis);
			RendererTransform = rendererTransform;
		}
	}
}
