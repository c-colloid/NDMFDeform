using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

namespace ExDeform.Editor
{
    /// <summary>
    /// UV mask processing system with Job System and Burst support
    /// Job SystemとBurst対応UVマスク処理システム
    /// </summary>
    public class UVMaskProcessor : IDisposable
    {
        private readonly bool useJobSystem;
        private readonly bool useBurstCompilation;
        private bool isDisposed = false;

        public UVMaskProcessor(bool useJobSystem = true, bool useBurstCompilation = true)
        {
            this.useJobSystem = useJobSystem;
            this.useBurstCompilation = useBurstCompilation;
        }

        /// <summary>
        /// Process UV mask for selected islands
        /// 選択されたアイランドのUVマスクを処理
        /// </summary>
        public NativeArray<float> ProcessMask(
            Mesh mesh,
            List<int> selectedIslandIDs,
            bool invertMask = false,
            float maskStrength = 1f,
            float featherRadius = 0.01f)
        {
            if (mesh == null || mesh.vertexCount == 0)
                return new NativeArray<float>();

            var vertexCount = mesh.vertexCount;
            var maskValues = new NativeArray<float>(vertexCount, Allocator.Persistent);

            try
            {
                if (useJobSystem && useBurstCompilation)
                {
                    ProcessMaskWithJobs(mesh, selectedIslandIDs, invertMask, maskStrength, featherRadius, maskValues);
                }
                else
                {
                    ProcessMaskDirect(mesh, selectedIslandIDs, invertMask, maskStrength, featherRadius, maskValues);
                }

                return maskValues;
            }
            catch (Exception)
            {
                if (maskValues.IsCreated)
                    maskValues.Dispose();
                throw;
            }
        }

        private void ProcessMaskWithJobs(
            Mesh mesh,
            List<int> selectedIslandIDs,
            bool invertMask,
            float maskStrength,
            float featherRadius,
            NativeArray<float> maskValues)
        {
            var uvs = mesh.uv;
            var triangles = mesh.triangles;
            
            var nativeUVs = new NativeArray<float2>(uvs.Length, Allocator.TempJob);
            var nativeTriangles = new NativeArray<int>(triangles.Length, Allocator.TempJob);
            var nativeSelectedIslands = new NativeArray<int>(selectedIslandIDs.ToArray(), Allocator.TempJob);

            try
            {
                // Copy data to native arrays
                for (int i = 0; i < uvs.Length; i++)
                {
                    nativeUVs[i] = new float2(uvs[i].x, uvs[i].y);
                }
                nativeTriangles.CopyFrom(triangles);

                // Create and schedule job
                var job = new UVMaskJob
                {
                    uvs = nativeUVs,
                    triangles = nativeTriangles,
                    selectedIslandIDs = nativeSelectedIslands,
                    invertMask = invertMask,
                    maskStrength = maskStrength,
                    featherRadius = featherRadius,
                    maskValues = maskValues
                };

                job.Schedule().Complete();
            }
            finally
            {
                if (nativeUVs.IsCreated) nativeUVs.Dispose();
                if (nativeTriangles.IsCreated) nativeTriangles.Dispose();
                if (nativeSelectedIslands.IsCreated) nativeSelectedIslands.Dispose();
            }
        }

        private void ProcessMaskDirect(
            Mesh mesh,
            List<int> selectedIslandIDs,
            bool invertMask,
            float maskStrength,
            float featherRadius,
            NativeArray<float> maskValues)
        {
            var uvs = mesh.uv;
            var triangles = mesh.triangles;

            // Simple direct processing (fallback)
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                // Default mask value
                float maskValue = invertMask ? maskStrength : 0f;
                
                // Apply basic masking logic here
                // This is a simplified version - in practice you'd analyze UV islands
                if (selectedIslandIDs.Count > 0)
                {
                    maskValue = invertMask ? 0f : maskStrength;
                }

                maskValues[i] = maskValue;
            }
        }

        public void Dispose()
        {
            if (!isDisposed)
            {
                isDisposed = true;
            }
        }
    }

    /// <summary>
    /// Burst-compiled job for UV mask processing
    /// UVマスク処理用Burstコンパイル済みジョブ
    /// </summary>
    [BurstCompile]
    public struct UVMaskJob : IJob
    {
        [ReadOnly] public NativeArray<float2> uvs;
        [ReadOnly] public NativeArray<int> triangles;
        [ReadOnly] public NativeArray<int> selectedIslandIDs;
        [ReadOnly] public bool invertMask;
        [ReadOnly] public float maskStrength;
        [ReadOnly] public float featherRadius;
        
        [WriteOnly] public NativeArray<float> maskValues;

        public void Execute()
        {
            // Initialize all mask values
            for (int i = 0; i < maskValues.Length; i++)
            {
                maskValues[i] = invertMask ? maskStrength : 0f;
            }

            // Simple implementation - in practice, you'd implement proper UV island analysis
            if (selectedIslandIDs.Length > 0)
            {
                for (int i = 0; i < maskValues.Length; i++)
                {
                    maskValues[i] = invertMask ? 0f : maskStrength;
                }
            }
        }
    }
}