// 移植元: keenanwoodall/Deform (MIT) TransformDeformer。
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// メッシュ全体を、レンダラーの姿勢からターゲット Transform の
	/// 位置・回転・スケールへ factor で補間して配置し直す。
	/// </summary>
	[DeformerMeta(Name = "Transform", Category = DeformerCategory.Shape,
	              Description = "ターゲット Transform の位置・回転・スケールへメッシュを補間する")]
	[AddComponentMenu("NDMF Deform/Deformers/Transform")]
	public class TransformDeformer : DeformerBase
	{
		[SerializeField] private Transform target;
		[SerializeField, Range(0f, 1f)] private float factor = 1f;

		public Transform Target
		{
			get => target != null ? target : transform;
			set => target = value;
		}

		public float Factor
		{
			get => factor;
			set => factor = Mathf.Clamp01(value);
		}

		// ターゲットの移動でプレビューが無効化されるよう、軸はターゲットにする
		public override Transform Axis => Target;

		public override DeformDataFlags DataFlags => DeformDataFlags.Vertices;

		public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			var renderer = space.RendererTransform;
			if (renderer == null || factor <= 0f)
				return dependency;

			var t = Target;
			// 元実装と同じく位置・回転・ローカルスケールを成分ごとに補間する。
			// バッファはワールド空間(スキン済み)なので、レンダラー姿勢を
			// 打ち消してから補間姿勢を適用する(ワールド入出力)
			var matrix = Matrix4x4.TRS(
				Vector3.Lerp(renderer.position, t.position, factor),
				Quaternion.Lerp(renderer.rotation, t.rotation, factor),
				Vector3.Lerp(renderer.localScale, t.localScale, factor));
			matrix = matrix * renderer.worldToLocalMatrix;

			return new TransformJob
			{
				matrix = new float4x4(matrix.GetColumn(0), matrix.GetColumn(1),
					matrix.GetColumn(2), matrix.GetColumn(3)),
				vertices = buffers.Vertices,
			}.Schedule(buffers.Length, 128, dependency);
		}

		[BurstCompile]
		public struct TransformJob : IJobParallelFor
		{
			public float4x4 matrix;
			public NativeArray<float3> vertices;

			public void Execute(int index)
			{
				vertices[index] = mul(matrix, float4(vertices[index], 1f)).xyz;
			}
		}
	}
}
