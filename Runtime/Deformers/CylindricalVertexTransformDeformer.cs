// 移植元: dev ブランチ ExDeform/CylindricalVertexTransformDefomer.cs(自作コード)
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 円柱コントローラによる放射状頂点移動。
	/// 軸空間で半径 scope・区間 [bottom, top] 内の頂点を、
	/// XY 放射方向へ (radius - scope) × factor だけ押し出す。
	/// </summary>
	[DeformerMeta(Name = "Cylindrical Vertex Transform", Category = DeformerCategory.Shape,
	              Description = "円柱コントローラで頂点を放射状に移動する")]
	[AddComponentMenu("NDMF Deform/Deformers/Cylindrical Vertex Transform")]
	public class CylindricalVertexTransformDeformer : DeformerBase
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

#if UNITY_EDITOR
		public override void DescribeHandles(IHandleBuilder h)
		{
			// キャップは top リングの縁に載せる(中空に浮かせない)
			h.RadiusSlider(nameof(radius), HandleAxis.Y, HandleLineStyle.Solid, 1f,
				nameof(top), HandleAxis.Z);
			h.RadiusSlider(nameof(scope), HandleAxis.Y, HandleLineStyle.Dotted, 1f,
				nameof(top), HandleAxis.Z);
			h.AxisSlider(nameof(top), HandleAxis.Z);
			h.AxisSlider(nameof(bottom), HandleAxis.Z);
			h.Circle(HandleAxis.Z, nameof(top), nameof(radius));
			h.Circle(HandleAxis.Z, nameof(bottom), nameof(radius));
			h.Circle(HandleAxis.Z, nameof(top), nameof(scope), HandleLineStyle.Dotted);
			h.Circle(HandleAxis.Z, nameof(bottom), nameof(scope), HandleLineStyle.Dotted);
		}
#endif

		public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			if (Mathf.Approximately(factor, 0f))
				return dependency;

			return new CylindricalVertexTransformJob
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
		public struct CylindricalVertexTransformJob : IJobParallelFor
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

				if (d < scope && point.z <= top && point.z >= bottom)
				{
					point.xy += lerp(new float2(0f), normalize(point.xy) * (radius - scope), factor);
				}

				vertices[index] = mul(axisToMesh, point).xyz;
			}
		}
	}
}
