// LatticeJob は keenanwoodall/Deform (MIT License) の
// Code/Runtime/Mesh/Deformers/LatticeDeformer.cs から移植。
// Copyright (c) 2019 Keenan Woodall — 全文は THIRD-PARTY-NOTICES.md 参照。
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;
using float3 = Unity.Mathematics.float3;
using float4x4 = Unity.Mathematics.float4x4;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 格子(ラティス)による自由変形。
	/// 制御点は軸空間 [-0.5, 0.5]^3 内の位置として保持し、
	/// 頂点はトライリニア補間で変形される(格子外は境界オフセットで並進)。
	/// 軸 Transform のスケールが格子の実サイズを決める。
	/// </summary>
	[DeformerMeta(Name = "Lattice", Category = DeformerCategory.Shape,
	              Description = "格子制御点による自由変形")]
	[AddComponentMenu("NDMF Deform/Deformers/Lattice")]
	public class LatticeDeformer : DeformerBase
	{
		[SerializeField] private Vector3Int resolution = new Vector3Int(2, 2, 2);
		[SerializeField, HideInInspector] private float3[] controlPoints;
		[SerializeField] private MirrorAxis mirrorAxis = MirrorAxis.None;

		public Vector3Int Resolution => resolution;
		public float3[] ControlPoints => controlPoints;
		public MirrorAxis EditMirrorAxis { get => mirrorAxis; set => mirrorAxis = value; }

		public override DeformDataFlags DataFlags => DeformDataFlags.Vertices;

		protected virtual void Reset()
		{
			GenerateControlPoints(resolution);
			FitToParentStack();
		}

		private void OnValidate()
		{
			resolution = new Vector3Int(
				Mathf.Max(2, resolution.x), Mathf.Max(2, resolution.y), Mathf.Max(2, resolution.z));
			if (controlPoints == null || controlPoints.Length == 0)
				GenerateControlPoints(resolution);
		}

		public int GetIndex(int x, int y, int z)
		{
			return x + y * resolution.x + z * (resolution.x * resolution.y);
		}

		/// <summary>制御点を指定分割数の恒等格子(変形なし)で作り直す</summary>
		public void GenerateControlPoints(Vector3Int newResolution)
		{
			resolution = new Vector3Int(
				Mathf.Max(2, newResolution.x), Mathf.Max(2, newResolution.y), Mathf.Max(2, newResolution.z));
			controlPoints = new float3[resolution.x * resolution.y * resolution.z];
			for (var z = 0; z < resolution.z; z++)
			for (var y = 0; y < resolution.y; y++)
			for (var x = 0; x < resolution.x; x++)
			{
				controlPoints[GetIndex(x, y, z)] = new float3(
					x / (float)(resolution.x - 1) - 0.5f,
					y / (float)(resolution.y - 1) - 0.5f,
					z / (float)(resolution.z - 1) - 0.5f);
			}
		}

		/// <summary>親の DeformStack のレンダラーのバウンズへ格子をフィットさせる</summary>
		public void FitToParentStack()
		{
			var stack = GetComponentInParent<DeformStack>();
			if (stack == null) return;

			Mesh mesh = null;
			if (stack.GetComponent<SkinnedMeshRenderer>() is SkinnedMeshRenderer smr)
				mesh = smr.sharedMesh;
			else if (stack.GetComponent<MeshFilter>() is MeshFilter mf)
				mesh = mf.sharedMesh;
			if (mesh == null) return;

			var bounds = mesh.bounds;
			var size = bounds.size;
			size.x = Mathf.Max(Mathf.Abs(size.x), 0.0001f);
			size.y = Mathf.Max(Mathf.Abs(size.y), 0.0001f);
			size.z = Mathf.Max(Mathf.Abs(size.z), 0.0001f);

			transform.position = stack.transform.TransformPoint(bounds.center);
			transform.rotation = stack.transform.rotation;
			transform.localScale = size;
		}

#if UNITY_EDITOR
		public override void DescribeHandles(IHandleBuilder h)
		{
			h.PointGrid(nameof(controlPoints), resolution, nameof(mirrorAxis));
		}
#endif

		public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			if (controlPoints == null || controlPoints.Length != resolution.x * resolution.y * resolution.z)
				return dependency;

			return new LatticeJob
			{
				controlPoints = new NativeArray<float3>(controlPoints, Allocator.TempJob),
				resolution = new int3(resolution.x, resolution.y, resolution.z),
				meshToTarget = space.MeshToAxis,
				targetToMesh = space.AxisToMesh,
				vertices = buffers.Vertices,
			}.Schedule(buffers.Length, 64, dependency);
		}

		[BurstCompile]
		public struct LatticeJob : IJobParallelFor
		{
			[DeallocateOnJobCompletion, ReadOnly] public NativeArray<float3> controlPoints;
			[ReadOnly] public int3 resolution;
			[ReadOnly] public float4x4 meshToTarget;
			[ReadOnly] public float4x4 targetToMesh;
			public NativeArray<float3> vertices;

			public void Execute(int index)
			{
				// [-0.5,0.5] 空間から [0,1] 空間へ
				var sourcePosition = transform(meshToTarget, vertices[index]) + float3(0.5f, 0.5f, 0.5f);

				// 頂点を含むセルの負側コーナー
				var negativeCorner = new int3((int)(sourcePosition.x * (resolution.x - 1)),
					(int)(sourcePosition.y * (resolution.y - 1)), (int)(sourcePosition.z * (resolution.z - 1)));

				negativeCorner = max(negativeCorner, new int3(0, 0, 0));
				negativeCorner = min(negativeCorner, resolution - new int3(2, 2, 2));

				int index0 = (negativeCorner.x + 0) + (negativeCorner.y + 0) * resolution.x +
				             (negativeCorner.z + 0) * (resolution.x * resolution.y);
				int index1 = (negativeCorner.x + 1) + (negativeCorner.y + 0) * resolution.x +
				             (negativeCorner.z + 0) * (resolution.x * resolution.y);
				int index2 = (negativeCorner.x + 0) + (negativeCorner.y + 1) * resolution.x +
				             (negativeCorner.z + 0) * (resolution.x * resolution.y);
				int index3 = (negativeCorner.x + 1) + (negativeCorner.y + 1) * resolution.x +
				             (negativeCorner.z + 0) * (resolution.x * resolution.y);
				int index4 = (negativeCorner.x + 0) + (negativeCorner.y + 0) * resolution.x +
				             (negativeCorner.z + 1) * (resolution.x * resolution.y);
				int index5 = (negativeCorner.x + 1) + (negativeCorner.y + 0) * resolution.x +
				             (negativeCorner.z + 1) * (resolution.x * resolution.y);
				int index6 = (negativeCorner.x + 0) + (negativeCorner.y + 1) * resolution.x +
				             (negativeCorner.z + 1) * (resolution.x * resolution.y);
				int index7 = (negativeCorner.x + 1) + (negativeCorner.y + 1) * resolution.x +
				             (negativeCorner.z + 1) * (resolution.x * resolution.y);

				var localizedSourcePosition = sourcePosition * (resolution - new int3(1, 1, 1)) - negativeCorner;
				localizedSourcePosition = clamp(localizedSourcePosition, float3.zero, new float3(1, 1, 1));

				var newPosition = float3.zero;

				// X 軸
				if (sourcePosition.x < 0)
				{
					var min1 = lerp(controlPoints[index0].x, controlPoints[index2].x, localizedSourcePosition.y);
					var min2 = lerp(controlPoints[index4].x, controlPoints[index6].x, localizedSourcePosition.y);
					var min = lerp(min1, min2, localizedSourcePosition.z);
					newPosition.x = sourcePosition.x + min;
				}
				else if (sourcePosition.x > 1)
				{
					var max1 = lerp(controlPoints[index1].x, controlPoints[index3].x, localizedSourcePosition.y);
					var max2 = lerp(controlPoints[index5].x, controlPoints[index7].x, localizedSourcePosition.y);
					var max = lerp(max1, max2, localizedSourcePosition.z);
					newPosition.x = sourcePosition.x + max - 1;
				}
				else
				{
					var min1 = lerp(controlPoints[index0].x, controlPoints[index2].x, localizedSourcePosition.y);
					var max1 = lerp(controlPoints[index1].x, controlPoints[index3].x, localizedSourcePosition.y);
					var min2 = lerp(controlPoints[index4].x, controlPoints[index6].x, localizedSourcePosition.y);
					var max2 = lerp(controlPoints[index5].x, controlPoints[index7].x, localizedSourcePosition.y);
					var min = lerp(min1, min2, localizedSourcePosition.z);
					var max = lerp(max1, max2, localizedSourcePosition.z);
					newPosition.x = lerp(min, max, localizedSourcePosition.x);
				}

				// Y 軸
				if (sourcePosition.y < 0)
				{
					var min1 = lerp(controlPoints[index0].y, controlPoints[index1].y, localizedSourcePosition.x);
					var min2 = lerp(controlPoints[index4].y, controlPoints[index5].y, localizedSourcePosition.x);
					var min = lerp(min1, min2, localizedSourcePosition.z);
					newPosition.y = sourcePosition.y + min;
				}
				else if (sourcePosition.y > 1)
				{
					var max1 = lerp(controlPoints[index2].y, controlPoints[index3].y, localizedSourcePosition.x);
					var max2 = lerp(controlPoints[index6].y, controlPoints[index7].y, localizedSourcePosition.x);
					var max = lerp(max1, max2, localizedSourcePosition.z);
					newPosition.y = sourcePosition.y + max - 1;
				}
				else
				{
					var min1 = lerp(controlPoints[index0].y, controlPoints[index1].y, localizedSourcePosition.x);
					var max1 = lerp(controlPoints[index2].y, controlPoints[index3].y, localizedSourcePosition.x);
					var min2 = lerp(controlPoints[index4].y, controlPoints[index5].y, localizedSourcePosition.x);
					var max2 = lerp(controlPoints[index6].y, controlPoints[index7].y, localizedSourcePosition.x);
					var min = lerp(min1, min2, localizedSourcePosition.z);
					var max = lerp(max1, max2, localizedSourcePosition.z);
					newPosition.y = lerp(min, max, localizedSourcePosition.y);
				}

				// Z 軸
				if (sourcePosition.z < 0)
				{
					var min1 = lerp(controlPoints[index0].z, controlPoints[index1].z, localizedSourcePosition.x);
					var min2 = lerp(controlPoints[index2].z, controlPoints[index3].z, localizedSourcePosition.x);
					var min = lerp(min1, min2, localizedSourcePosition.y);
					newPosition.z = sourcePosition.z + min;
				}
				else if (sourcePosition.z > 1)
				{
					var max1 = lerp(controlPoints[index4].z, controlPoints[index5].z, localizedSourcePosition.x);
					var max2 = lerp(controlPoints[index6].z, controlPoints[index7].z, localizedSourcePosition.x);
					var max = lerp(max1, max2, localizedSourcePosition.y);
					newPosition.z = sourcePosition.z + max - 1;
				}
				else
				{
					var min1 = lerp(controlPoints[index0].z, controlPoints[index1].z, localizedSourcePosition.x);
					var max1 = lerp(controlPoints[index4].z, controlPoints[index5].z, localizedSourcePosition.x);
					var min2 = lerp(controlPoints[index2].z, controlPoints[index3].z, localizedSourcePosition.x);
					var max2 = lerp(controlPoints[index6].z, controlPoints[index7].z, localizedSourcePosition.x);
					var min = lerp(min1, min2, localizedSourcePosition.y);
					var max = lerp(max1, max2, localizedSourcePosition.y);
					newPosition.z = lerp(min, max, localizedSourcePosition.z);
				}

				vertices[index] = transform(targetToMesh, newPosition);
			}
		}
	}
}
