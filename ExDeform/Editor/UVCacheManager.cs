using System;
using System.Collections.Generic;
using UnityEngine;
using ExDeform.Runtime;
using ExDeform.Runtime.Cache.Interfaces;
using ExDeform.Runtime.Data;

namespace ExDeform.Editor
{
    /// <summary>
    /// UV Cache management system for editor operations
    /// エディター操作用UVキャッシュ管理システム
    /// </summary>
    public class UVCacheManager : IDisposable
    {
        private readonly Dictionary<string, IUVCache> cacheInstances;
        private bool isDisposed = false;

        public UVCacheManager()
        {
            cacheInstances = new Dictionary<string, IUVCache>();
        }

        /// <summary>
        /// Get or create a cache instance for the given mesh
        /// 指定されたメッシュのキャッシュインスタンスを取得または作成
        /// </summary>
        public IUVCache GetCache(Mesh mesh)
        {
            if (mesh == null)
                return null;

            var meshId = mesh.GetInstanceID().ToString();
            
            if (!cacheInstances.ContainsKey(meshId))
            {
                // Create a simple in-memory cache for editor operations
                cacheInstances[meshId] = new EditorUVCache(mesh);
            }

            return cacheInstances[meshId];
        }

        /// <summary>
        /// Clear cache for specific mesh
        /// 特定のメッシュのキャッシュをクリア
        /// </summary>
        public void ClearCache(Mesh mesh)
        {
            if (mesh == null)
                return;

            var meshId = mesh.GetInstanceID().ToString();
            if (cacheInstances.TryGetValue(meshId, out var cache))
            {
                cache?.Dispose();
                cacheInstances.Remove(meshId);
            }
        }

        /// <summary>
        /// Get cached UV islands for the given mesh
        /// 指定されたメッシュのキャッシュされたUVアイランドを取得
        /// </summary>
        public List<UVIslandAnalyzer.UVIsland> GetCachedIslands(Mesh mesh)
        {
            // For now, return null to force fresh analysis
            // In a full implementation, this would cache analyzed islands
            return null;
        }

        /// <summary>
        /// Clear all caches
        /// 全てのキャッシュをクリア
        /// </summary>
        public void ClearAllCaches()
        {
            foreach (var cache in cacheInstances.Values)
            {
                cache?.Dispose();
            }
            cacheInstances.Clear();
        }

        public void Dispose()
        {
            if (!isDisposed)
            {
                ClearAllCaches();
                isDisposed = true;
            }
        }
    }

    /// <summary>
    /// Simple UV cache implementation for editor use
    /// エディター用シンプルUVキャッシュ実装
    /// </summary>
    internal class EditorUVCache : IUVCache, IDisposable
    {
        private readonly Mesh targetMesh;
        private Vector2[] cachedUVs;
        private bool isValid = false;

        public EditorUVCache(Mesh mesh)
        {
            targetMesh = mesh;
            RefreshCache();
        }

        public Vector2[] GetUVs()
        {
            if (!isValid)
                RefreshCache();
            
            return cachedUVs;
        }

        public bool IsValid => isValid && targetMesh != null;

        public void InvalidateCache()
        {
            isValid = false;
            cachedUVs = null;
        }

        private void RefreshCache()
        {
            if (targetMesh == null)
            {
                isValid = false;
                return;
            }

            try
            {
                cachedUVs = targetMesh.uv;
                isValid = cachedUVs != null && cachedUVs.Length > 0;
            }
            catch (Exception)
            {
                isValid = false;
                cachedUVs = null;
            }
        }

        // IUVCache implementation
        public bool CacheUVData(string meshKey, Texture2D uvTexture, UVIslandData[] islandData, int[] selectedIslands)
        {
            // Simple implementation - just refresh the cache
            RefreshCache();
            return isValid;
        }

        public UVCacheData LoadUVData(string meshKey)
        {
            return new UVCacheData
            {
                uvTexture = null, // Not stored in this simple implementation
                islands = new UVIslandData[0],
                selectedIslandIDs = new int[0],
                meshHash = targetMesh?.GetInstanceID() ?? 0,
                timestamp = DateTime.Now.Ticks
            };
        }

        public Texture2D GetPreviewTexture(string meshKey, int resolution = 128)
        {
            return null; // Not implemented in this simple cache
        }

        public bool IsValidCache(string meshKey, int meshHash)
        {
            return isValid && targetMesh != null && targetMesh.GetInstanceID() == meshHash;
        }

        public void InvalidateCache(string meshKey)
        {
            InvalidateCache();
        }

        public void OptimizeMemoryUsage()
        {
            if (!isValid)
            {
                cachedUVs = null;
            }
        }

        public void Dispose()
        {
            InvalidateCache();
        }
    }
}