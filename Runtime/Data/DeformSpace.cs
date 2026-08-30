using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 変形空間とデフォーマ軸空間の相互変換。
	/// ベイクコアは頂点をスキン行列で「見た目のワールド空間」へ持ち上げてから
	/// デフォーマを適用するため、MeshToAxis は実質「ワールド → 軸空間」
	/// (軸 Transform の worldToLocal)になる。名前は歴史的経緯によるもの。
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
