//using System;
//using System.Collections;
//using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using System.Linq;
using UnityEngine;
//using UnityEditor;
using Deform;
using static Unity.Mathematics.math;
using float3 = Unity.Mathematics.float3;
using float4x4 = Unity.Mathematics.float4x4;

namespace MeshModifier.NDMFDeform.ExDeform
{
	[Deformer(Name = "Cylindrical Scale", Description = "Scale deform a mesh using cylinder controler", XRotation = 90f, Type = typeof(CylindricalScaleDeformer))]
	public class CylindricalScaleDeformer : Deformer, IFactor
	{
		public float Factor
		{
			get => factor;
			set => factor = Mathf.Clamp01 (value);
		}
		public float Radius
		{
			get => radius;
			set => radius = value;
		}
		public float Scope
		{
			get => scope;
			set => scope = value;
		}
		public float Top
		{
			get => top;
			set => top = value;
		}
		public float Bottom
		{
			get => bottom;
			set => bottom = value;
		}
		public Transform Axis
		{
			get
			{
				if (axis == null)
					axis = transform;
				return axis;
			}
			set => axis = value;
		}
		
		[SerializeField, HideInInspector] private float factor = 0f;
		[SerializeField, HideInInspector] private float radius = 1f;
		[SerializeField, HideInInspector] private float scope = 1f;
		[SerializeField, HideInInspector] private float top = 0.5f;
		[SerializeField, HideInInspector] private float bottom = -0.5f;
		[SerializeField, HideInInspector] private Transform axis;
		
		public override DataFlags DataFlags => Deform.DataFlags.Vertices;
		
		public override JobHandle Process(MeshData data, JobHandle dependency = default) {
			if (Mathf.Approximately(Factor, 0f))
				return dependency;
				
			var meshToAxis = DeformerUtils.GetMeshToAxisSpace(Axis, data.Target.GetTransform());
			
			return new CylindricalScaleJob
			{
				factor = Factor,
				radius = Radius,
				scope = Scope,
				top = Top,
				bottom = Bottom,
				meshToAxis = meshToAxis,
				axisToMesh = meshToAxis.inverse,
				vertices = data.DynamicNative.VertexBuffer
			}.Schedule(data.Length, DEFAULT_BATCH_COUNT, dependency);
		}
		
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
				var range = scope;
				var point = mul (meshToAxis, float4 (vertices[index],1f));
				var d = length(point.xy);
				
				if (d < range && point.z < top && point.z > bottom)
				{
					point.xy *= lerp(1f, radius/scope, factor);
				}
				
				vertices[index] = mul(axisToMesh, point).xyz;
			}
		}
	}
}