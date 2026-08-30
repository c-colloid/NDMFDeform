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
	/// 元実装と同じく、領域判定は変形後の頂点位置に対して行う。
	///
	/// 減衰は元実装(表面投影点間の距離比)から変更している:
	/// 元実装は「内側の点から外側バウンズ表面への最近接投影」が面の切替わりで
	/// 不連続になり、重みがジャンプしてメッシュに段差が出るため、
	/// 連続な距離場 t = d_out / (d_in + d_out) を使う
	/// (d_in = 内側バウンズまでの距離、d_out = 外側バウンズへのめり込み深さ)。
	/// 面中央の軸上では元実装と同じ減衰になる。
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

#if UNITY_EDITOR
		public override void DescribeHandles(IHandleBuilder h)
		{
			h.Box(nameof(innerBounds));
			h.Box(nameof(outerBounds), HandleLineStyle.Dotted);
		}
#endif

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

				// d_in: 内側バウンズまでの距離(内側なら 0)
				var dIn = length(max(abs(point - innerCenter) - innerExtents, float3(0f)));
				// d_out: 外側バウンズへのめり込み深さ = 最も近い面までの距離(外側なら 0)
				var dOut = max(cmin(outerExtents - abs(point - outerCenter)), 0f);

				float t;
				if (dIn <= 0f)
					t = 1f;
				else if (dOut <= 0f)
					t = 0f;
				else
					t = dOut / (dIn + dOut);

				if (invert == 1)
					t = 1f - t;

				vertices[index] = lerp(meshPoint, original[index], saturate(t * factor));
			}
		}
	}
}
