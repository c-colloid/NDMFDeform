using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Beans.Unity.Mathematics;
using System.Collections.Generic;

namespace Deform.Masking
{
    [Deformer(Name = "UV Island Mask", Description = "Masks deformation based on UV island selection", Type = typeof(UVIslandMask), Category = Category.Mask)]
    public class UVIslandMask : Deformer, IFactor
    {
        public float Factor
        {
            get => factor;
            set => factor = Mathf.Clamp(value, -1f, 1f);
        }
        
        public List<Vector2> SelectionPoints
        {
            get => selectionPoints;
            set => selectionPoints = value;
        }
        
        public float Falloff
        {
            get => falloff;
            set => falloff = Mathf.Max(0f, value);
        }
        
        public bool Invert
        {
            get => invert;
            set => invert = value;
        }

        [SerializeField, HideInInspector] private float factor = 1f;
        [SerializeField, HideInInspector] private List<Vector2> selectionPoints = new List<Vector2>();
        [SerializeField, HideInInspector] private float falloff = 0.1f;
        [SerializeField, HideInInspector] private bool invert;

        public override DataFlags DataFlags => DataFlags.Vertices | DataFlags.UVs;

        public override JobHandle Process(MeshData data, JobHandle dependency = default)
        {
            // 選択ポイントをNativeArrayに変換
            var nativePoints = new NativeArray<float2>(selectionPoints.Count, Allocator.TempJob);
            for (int i = 0; i < selectionPoints.Count; i++)
            {
                nativePoints[i] = new float2(selectionPoints[i].x, selectionPoints[i].y);
            }

            var jobHandle = !invert
                ? new UVIslandMaskJob
                {
                    factor = Factor,
                    selectionPoints = nativePoints,
                    selectionPointCount = selectionPoints.Count,
                    falloff = Falloff,
                    uvs = data.DynamicNative.UVBuffer,
                    currentVertices = data.DynamicNative.VertexBuffer,
                    maskVertices = data.DynamicNative.MaskVertexBuffer
                }.Schedule(data.Length, DEFAULT_BATCH_COUNT, dependency)
                : new InvertedUVIslandMaskJob
                {
                    factor = Factor,
                    selectionPoints = nativePoints,
                    selectionPointCount = selectionPoints.Count,
                    falloff = Falloff,
                    uvs = data.DynamicNative.UVBuffer,
                    currentVertices = data.DynamicNative.VertexBuffer,
                    maskVertices = data.DynamicNative.MaskVertexBuffer
                }.Schedule(data.Length, DEFAULT_BATCH_COUNT, dependency);

            // Disposeを別のジョブとしてスケジュール
            var disposeJob = new DisposeNativeArrayJob { array = nativePoints };
            return disposeJob.Schedule(jobHandle);
        }

        [BurstCompile(CompileSynchronously = COMPILE_SYNCHRONOUSLY)]
        public struct UVIslandMaskJob : IJobParallelFor
        {
            public float factor;
            [ReadOnly] public NativeArray<float2> selectionPoints;
            public int selectionPointCount;
            public float falloff;

            [ReadOnly] public NativeArray<float2> uvs;
            public NativeArray<float3> currentVertices;
            [ReadOnly] public NativeArray<float3> maskVertices;

            public void Execute(int index)
            {
                var uv = uvs[index];
                var t = CalculateMaskStrength(uv);
                t *= factor;

                currentVertices[index] = lerp(currentVertices[index], maskVertices[index], saturate(t));
            }

            private float CalculateMaskStrength(float2 uv)
            {
                if (selectionPointCount < 3) return 0f;

                // ポイントインポリゴンテスト
                bool inside = IsPointInPolygon(uv);
                if (inside) return 1f;

                // フォールオフの計算
                if (falloff <= 0f) return 0f;

                float minDistance = float.MaxValue;
                for (int i = 0; i < selectionPointCount; i++)
                {
                    int nextIndex = (i + 1) % selectionPointCount;
                    float2 start = selectionPoints[i];
                    float2 end = selectionPoints[nextIndex];
                    
                    float dist = DistanceToLineSegment(uv, start, end);
                    minDistance = min(minDistance, dist);
                }

                return 1f - saturate(minDistance / falloff);
            }

            private bool IsPointInPolygon(float2 point)
            {
                bool inside = false;
                for (int i = 0, j = selectionPointCount - 1; i < selectionPointCount; j = i++)
                {
                    float2 pi = selectionPoints[i];
                    float2 pj = selectionPoints[j];

                    if (((pi.y <= point.y && point.y < pj.y) || (pj.y <= point.y && point.y < pi.y)) &&
                        (point.x < (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y) + pi.x))
                    {
                        inside = !inside;
                    }
                }
                return inside;
            }

            private float DistanceToLineSegment(float2 point, float2 start, float2 end)
            {
                float2 line = end - start;
                float len2 = dot(line, line);
                
                if (len2 == 0f) return length(point - start);
                
                float t = saturate(dot(point - start, line) / len2);
                float2 projection = start + t * line;
                
                return length(point - projection);
            }
        }

        [BurstCompile(CompileSynchronously = COMPILE_SYNCHRONOUSLY)]
        public struct InvertedUVIslandMaskJob : IJobParallelFor
        {
            public float factor;
            [ReadOnly] public NativeArray<float2> selectionPoints;
            public int selectionPointCount;
            public float falloff;

            [ReadOnly] public NativeArray<float2> uvs;
            public NativeArray<float3> currentVertices;
            [ReadOnly] public NativeArray<float3> maskVertices;

            public void Execute(int index)
            {
                var uv = uvs[index];
                var t = 1f - CalculateMaskStrength(uv);
                t *= factor;

                currentVertices[index] = lerp(currentVertices[index], maskVertices[index], saturate(t));
            }

            private float CalculateMaskStrength(float2 uv)
            {
                if (selectionPointCount < 3) return 0f;

                bool inside = IsPointInPolygon(uv);
                if (inside) return 1f;

                if (falloff <= 0f) return 0f;

                float minDistance = float.MaxValue;
                for (int i = 0; i < selectionPointCount; i++)
                {
                    int nextIndex = (i + 1) % selectionPointCount;
                    float2 start = selectionPoints[i];
                    float2 end = selectionPoints[nextIndex];
                    
                    float dist = DistanceToLineSegment(uv, start, end);
                    minDistance = min(minDistance, dist);
                }

                return 1f - saturate(minDistance / falloff);
            }

            private bool IsPointInPolygon(float2 point)
            {
                bool inside = false;
                for (int i = 0, j = selectionPointCount - 1; i < selectionPointCount; j = i++)
                {
                    float2 pi = selectionPoints[i];
                    float2 pj = selectionPoints[j];

                    if (((pi.y <= point.y && point.y < pj.y) || (pj.y <= point.y && point.y < pi.y)) &&
                        (point.x < (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y) + pi.x))
                    {
                        inside = !inside;
                    }
                }
                return inside;
            }

            private float DistanceToLineSegment(float2 point, float2 start, float2 end)
            {
                float2 line = end - start;
                float len2 = dot(line, line);
                
                if (len2 == 0f) return length(point - start);
                
                float t = saturate(dot(point - start, line) / len2);
                float2 projection = start + t * line;
                
                return length(point - projection);
            }
        }

        [BurstCompile(CompileSynchronously = COMPILE_SYNCHRONOUSLY)]
        private struct DisposeNativeArrayJob : IJob
        {
            [DeallocateOnJobCompletion]
            public NativeArray<float2> array;

            public void Execute() { }
        }
    }
}