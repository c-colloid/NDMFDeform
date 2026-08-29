// 移植元: keenanwoodall/Deform (MIT) VerticalGradientMask。
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 軸 Z 方向の距離に応じた指数減衰グラデーションで変形を打ち消すマスク。
	/// 軸原点付近ほど強く打ち消され、Z+ 方向へ falloff に従って減衰する。
	/// </summary>
	[DeformerMeta(Name = "Vertical Gradient Mask", Category = DeformerCategory.Mask,
	              Description = "軸方向のグラデーションで変形を打ち消す")]
	[AddComponentMenu("NDMF Deform/Deformers/Vertical Gradient Mask")]
	public class VerticalGradientMaskDeformer : DeformerBase
	{
		[SerializeField, Range(0f, 1f)] private float factor = 1f;
		[SerializeField] private float falloff = 10f;
		[SerializeField] private bool invert;
		[SerializeField] private Transform axisOverride;

		public float Factor { get => factor; set => factor = Mathf.Clamp01(value); }
		public float Falloff { get => falloff; set => falloff = value; }
		public bool Invert { get => invert; set => invert = value; }

		public override Transform Axis => axisOverride != null ? axisOverride : transform;

		public override DeformDataFlags DataFlags =>
			DeformDataFlags.Vertices | DeformDataFlags.OriginalVertices;

		public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			if (factor <= 0f)
				return dependency;
			if (!buffers.OriginalVertices.IsCreated)
				return dependency;

			return new VerticalGradientMaskJob
			{
				factor = factor,
				falloff = falloff,
				invert = invert ? 1 : 0,
				meshToAxis = space.MeshToAxis,
				vertices = buffers.Vertices,
				original = buffers.OriginalVertices,
			}.Schedule(buffers.Length, 128, dependency);
		}

		[BurstCompile]
		public struct VerticalGradientMaskJob : IJobParallelFor
		{
			public float factor;
			public float falloff;
			public int invert;
			public float4x4 meshToAxis;
			public NativeArray<float3> vertices;
			[ReadOnly] public NativeArray<float3> original;

			public void Execute(int index)
			{
				var meshPoint = vertices[index];
				var point = mul(meshToAxis, float4(meshPoint, 1f)).xyz;

				var t = exp(-falloff * point.z) * factor;
				if (invert == 1)
					t = 1f - t;

				vertices[index] = lerp(meshPoint, original[index], saturate(t));
			}
		}
	}
}
