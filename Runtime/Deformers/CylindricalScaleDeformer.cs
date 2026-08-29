// 移植元: dev ブランチ ExDeform/CylindricalScaleDeformer.cs(自作コード)
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 円柱コントローラによる範囲スケール。
	/// 軸空間で半径 scope・区間 [bottom, top] 内の頂点の XY を radius/scope 倍へ補間する。
	/// </summary>
	[DeformerMeta(Name = "Cylindrical Scale", Category = DeformerCategory.Shape,
	              Description = "円柱コントローラで範囲をスケールする")]
	[AddComponentMenu("NDMF Deform/Deformers/Cylindrical Scale")]
	public class CylindricalScaleDeformer : DeformerBase
	{
		[SerializeField, Range(0f, 1f)] private float factor = 0f;
		[SerializeField] private float radius = 1f;
		[SerializeField] private float scope = 1f;
		[SerializeField] private float top = 0.5f;
		[SerializeField] private float bottom = -0.5f;
		[SerializeField] private Transform axisOverride;

		public float Factor { get => factor; set => factor = Mathf.Clamp01(value); }
		public float Radius { get => radius; set => radius = value; }
		public float Scope { get => scope; set => scope = value; }
		public float Top { get => top; set => top = value; }
		public float Bottom { get => bottom; set => bottom = value; }

		public override Transform Axis => axisOverride != null ? axisOverride : transform;

		public override DeformDataFlags DataFlags => DeformDataFlags.Vertices;

		public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			if (Mathf.Approximately(factor, 0f))
				return dependency;

			return new CylindricalScaleJob
			{
				factor = factor,
				radius = radius,
				scope = scope,
				top = top,
				bottom = bottom,
				meshToAxis = space.MeshToAxis,
				axisToMesh = space.AxisToMesh,
				vertices = buffers.Vertices,
			}.Schedule(buffers.Length, 64, dependency);
		}

		[BurstCompile]
		public struct CylindricalScaleJob : IJobParallelFor
		{
			public float factor;
			public float radius;
			public float scope;
			public float top;
			public float bottom;
			public float4x4 meshToAxis;
			public float4x4 axisToMesh;
			public NativeArray<float3> vertices;

			public void Execute(int index)
			{
				var point = mul(meshToAxis, float4(vertices[index], 1f));
				var d = length(point.xy);

				if (d < scope && point.z < top && point.z > bottom)
				{
					point.xy *= lerp(1f, radius / scope, factor);
				}

				vertices[index] = mul(axisToMesh, point).xyz;
			}
		}
	}
}
