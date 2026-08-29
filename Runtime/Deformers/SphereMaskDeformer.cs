// 移植元: keenanwoodall/Deform (MIT) SphereMask。
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 球形領域のマスク。内半径の内側で変形を完全に打ち消し、
	/// 外半径まで減衰する(invert で外側を打ち消す)。
	/// 元実装と同じく、半径は直径的な値(ジョブ内で 0.5 倍)として扱い、
	/// 領域判定は変形後の頂点位置に対して行う。
	/// </summary>
	[DeformerMeta(Name = "Sphere Mask", Category = DeformerCategory.Mask,
	              Description = "球形領域の変形を打ち消す(反転で外側を打ち消す)")]
	[AddComponentMenu("NDMF Deform/Deformers/Sphere Mask")]
	public class SphereMaskDeformer : DeformerBase
	{
		[SerializeField, Range(0f, 1f)] private float factor = 1f;
		[SerializeField, Min(0f)] private float innerRadius = 0.5f;
		[SerializeField, Min(0f)] private float outerRadius = 1f;
		[SerializeField] private bool invert;
		[SerializeField] private Transform axisOverride;

		public float Factor { get => factor; set => factor = Mathf.Clamp01(value); }
		public float InnerRadius { get => innerRadius; set => innerRadius = Mathf.Max(0f, value); }
		public float OuterRadius { get => outerRadius; set => outerRadius = Mathf.Max(0f, value); }
		public bool Invert { get => invert; set => invert = value; }

		public override Transform Axis => axisOverride != null ? axisOverride : transform;

		public override DeformDataFlags DataFlags =>
			DeformDataFlags.Vertices | DeformDataFlags.OriginalVertices;

#if UNITY_EDITOR
		public override void DescribeHandles(IHandleBuilder h)
		{
			// 実効半径はシリアライズ値の 0.5 倍(ジョブ内換算)なので、表示も scale=0.5 で合わせる
			const float RadiusScale = 0.5f;
			h.RadiusSlider(nameof(innerRadius), HandleAxis.Y, HandleLineStyle.Solid, RadiusScale);
			h.RadiusSlider(nameof(outerRadius), HandleAxis.Y, HandleLineStyle.Dotted, RadiusScale);
			h.Circle(HandleAxis.X, 0f, nameof(innerRadius), HandleLineStyle.Solid, RadiusScale);
			h.Circle(HandleAxis.Y, 0f, nameof(innerRadius), HandleLineStyle.Solid, RadiusScale);
			h.Circle(HandleAxis.Z, 0f, nameof(innerRadius), HandleLineStyle.Solid, RadiusScale);
			h.Circle(HandleAxis.X, 0f, nameof(outerRadius), HandleLineStyle.Dotted, RadiusScale);
			h.Circle(HandleAxis.Y, 0f, nameof(outerRadius), HandleLineStyle.Dotted, RadiusScale);
			h.Circle(HandleAxis.Z, 0f, nameof(outerRadius), HandleLineStyle.Dotted, RadiusScale);
		}
#endif

		public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			if (factor <= 0f || Mathf.Approximately(outerRadius, 0f))
				return dependency;
			if (!buffers.OriginalVertices.IsCreated)
				return dependency;

			return new SphereMaskJob
			{
				factor = factor,
				innerRadius = innerRadius * 0.5f,
				outerRadius = outerRadius * 0.5f,
				invert = invert ? 1 : 0,
				meshToAxis = space.MeshToAxis,
				vertices = buffers.Vertices,
				original = buffers.OriginalVertices,
			}.Schedule(buffers.Length, 128, dependency);
		}

		[BurstCompile]
		public struct SphereMaskJob : IJobParallelFor
		{
			public float factor;
			public float innerRadius;
			public float outerRadius;
			public int invert;
			public float4x4 meshToAxis;
			public NativeArray<float3> vertices;
			[ReadOnly] public NativeArray<float3> original;

			public void Execute(int index)
			{
				var meshPoint = vertices[index];
				var dist = length(mul(meshToAxis, float4(meshPoint, 1f)).xyz);

				float t;
				if (dist > outerRadius)
					t = 0f;
				else if (dist < innerRadius)
					t = 1f;
				else
					t = unlerp(outerRadius, innerRadius, dist);

				if (invert == 1)
					t = 1f - t;

				vertices[index] = lerp(meshPoint, original[index], saturate(t * factor));
			}
		}
	}
}
