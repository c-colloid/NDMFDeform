// 移植元: keenanwoodall/Deform (MIT) VertexColorMask。
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 頂点カラーの 1 チャンネルで変形を打ち消すマスク。
	/// 既定(invert = false)では、チャンネル値が大きい(塗られている)頂点ほど
	/// 変形が打ち消される。
	/// 注意: 元実装は invert の分岐が逆に接続されており、その実効挙動を互換のため踏襲する。
	/// </summary>
	[DeformerMeta(Name = "Vertex Color Mask", Category = DeformerCategory.Mask,
	              Description = "頂点カラーのチャンネルで変形を打ち消す")]
	[AddComponentMenu("NDMF Deform/Deformers/Vertex Color Mask")]
	public class VertexColorMaskDeformer : DeformerBase
	{
		public enum ColorChannel { R = 0, G = 1, B = 2, A = 3 }

		[SerializeField, Range(0f, 1f)] private float factor = 1f;
		[SerializeField] private float falloff = 1f;
		[SerializeField] private bool invert;
		[SerializeField] private ColorChannel channel = ColorChannel.R;

		public float Factor { get => factor; set => factor = Mathf.Clamp01(value); }
		public float Falloff { get => falloff; set => falloff = value; }
		public bool Invert { get => invert; set => invert = value; }
		public ColorChannel Channel { get => channel; set => channel = value; }

		public override DeformDataFlags DataFlags =>
			DeformDataFlags.Vertices | DeformDataFlags.OriginalVertices | DeformDataFlags.Colors;

		public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			if (factor <= 0f)
				return dependency;
			if (!buffers.OriginalVertices.IsCreated || !buffers.Colors.IsCreated)
				return dependency;

			return new VertexColorMaskJob
			{
				factor = factor,
				falloff = falloff,
				channel = (int)channel,
				// 元実装の実効挙動: invert=false → 1 − exp(−falloff·c)·factor
				invert = invert ? 0 : 1,
				vertices = buffers.Vertices,
				original = buffers.OriginalVertices,
				colors = buffers.Colors,
			}.Schedule(buffers.Length, 128, dependency);
		}

		[BurstCompile]
		public struct VertexColorMaskJob : IJobParallelFor
		{
			public float factor;
			public float falloff;
			public int channel;
			public int invert;
			public NativeArray<float3> vertices;
			[ReadOnly] public NativeArray<float3> original;
			[ReadOnly] public NativeArray<float4> colors;

			public void Execute(int index)
			{
				var t = exp(-falloff * colors[index][channel]) * factor;
				if (invert == 1)
					t = 1f - t;

				vertices[index] = lerp(vertices[index], original[index], saturate(t));
			}
		}
	}
}
