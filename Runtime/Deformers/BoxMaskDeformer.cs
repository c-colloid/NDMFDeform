// 移植元: keenanwoodall/Deform (MIT) BoxMask。
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 直方体領域のマスク。内側バウンズの中で変形を完全に打ち消し、
	/// 外側バウンズまで減衰する(invert で外側を打ち消す)。
	/// </summary>
	[DeformerMeta(Name = "Box Mask", Category = DeformerCategory.Mask,
	              Description = "直方体領域の変形を打ち消す(反転で外側を打ち消す)")]
	[AddComponentMenu("NDMF Deform/Deformers/Box Mask")]
	public class BoxMaskDeformer : DeformerBase
	{
		[SerializeField, Range(0f, 1f)] private float factor = 1f;
		[SerializeField] private Bounds innerBounds = new Bounds(Vector3.zero, Vector3.one * 0.5f);
		[SerializeField] private Bounds outerBounds = new Bounds(Vector3.zero, Vector3.one);
		[SerializeField] private bool invert;
		[SerializeField] private Transform axisOverride;

		public float Factor { get => factor; set => factor = Mathf.Clamp01(value); }
		public Bounds InnerBounds { get => innerBounds; set => innerBounds = value; }
		public Bounds OuterBounds { get => outerBounds; set => outerBounds = value; }
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

			return new BoxMaskJob
			{
				factor = factor,
				innerCenter = innerBounds.center,
				innerExtents = innerBounds.extents,
				outerCenter = outerBounds.center,
				outerExtents = outerBounds.extents,
				invert = invert ? 1 : 0,
				meshToAxis = space.MeshToAxis,
				vertices = buffers.Vertices,
				original = buffers.OriginalVertices,
			}.Schedule(buffers.Length, 128, dependency);
		}

		[BurstCompile]
		public struct BoxMaskJob : IJobParallelFor
		{
			public float factor;
			public float3 innerCenter;
			public float3 innerExtents;
			public float3 outerCenter;
			public float3 outerExtents;
			public int invert;
			public float4x4 meshToAxis;
			public NativeArray<float3> vertices;
			[ReadOnly] public NativeArray<float3> original;

			public void Execute(int index)
			{
				var meshPoint = vertices[index];
				var point = mul(meshToAxis, float4(meshPoint, 1f)).xyz;

				float t;
				if (all(abs(point - innerCenter) <= innerExtents))
				{
					t = 1f;
				}
				else
				{
					var innerPoint = ClosestSurfacePoint(innerCenter, innerExtents, point);
					var outerPoint = ClosestSurfacePoint(outerCenter, outerExtents, point);
					var span = max(distance(innerPoint, outerPoint), 1e-6f);
					t = 1f - distance(innerPoint, point) / span;
				}

				if (invert == 1)
					t = 1f - t;

				vertices[index] = lerp(meshPoint, original[index], saturate(t * factor));
			}

			/// <summary>バウンズ表面上の最近接点(内側の点は最も近い面へ押し出す)</summary>
			private static float3 ClosestSurfacePoint(float3 center, float3 extents, float3 point)
			{
				var local = point - center;
				var clamped = clamp(local, -extents, extents);
				if (any(abs(local) > extents))
					return center + clamped;

				var faceDistance = extents - abs(local);
				if (faceDistance.x <= faceDistance.y && faceDistance.x <= faceDistance.z)
					clamped.x = local.x >= 0f ? extents.x : -extents.x;
				else if (faceDistance.y <= faceDistance.z)
					clamped.y = local.y >= 0f ? extents.y : -extents.y;
				else
					clamped.z = local.z >= 0f ? extents.z : -extents.z;
				return center + clamped;
			}
		}
	}
}
