// 移植元: keenanwoodall/Deform (MIT) ScaleDeformer。
// パラメータは軸 Transform の localScale そのもの(Transform ギズモで編集する)。
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 軸 Transform のスケール値でメッシュを軸空間で拡縮する。
	/// 軸の回転を使うと任意方向のスケールになる。
	/// </summary>
	[DeformerMeta(Name = "Scale", Category = DeformerCategory.Shape,
	              Description = "軸 Transform のスケールでメッシュを拡縮する")]
	[AddComponentMenu("NDMF Deform/Deformers/Scale")]
	public class ScaleDeformer : DeformerBase
	{
		[SerializeField] private Transform axisOverride;

		public Transform AxisOverride
		{
			get => axisOverride;
			set => axisOverride = value;
		}

		public override Transform Axis => axisOverride != null ? axisOverride : transform;

		public override DeformDataFlags DataFlags => DeformDataFlags.Vertices;

		public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			return new ScaleJob
			{
				scale = Axis.localScale,
				meshToAxis = space.MeshToAxis,
				axisToMesh = space.AxisToMesh,
				vertices = buffers.Vertices,
			}.Schedule(buffers.Length, 128, dependency);
		}

		[BurstCompile]
		public struct ScaleJob : IJobParallelFor
		{
			public float3 scale;
			public float4x4 meshToAxis;
			public float4x4 axisToMesh;
			public NativeArray<float3> vertices;

			public void Execute(int index)
			{
				var point = mul(meshToAxis, float4(vertices[index], 1f));
				point *= float4(scale, 1f);
				vertices[index] = mul(axisToMesh, point).xyz;
			}
		}
	}
}
