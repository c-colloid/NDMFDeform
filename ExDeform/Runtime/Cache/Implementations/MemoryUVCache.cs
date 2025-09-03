using System;
using System.Collections.Generic;
using UnityEngine;
using ExDeform.Runtime.Cache.Interfaces;

namespace ExDeform.Runtime.Cache.Implementations
{
    /// <summary>
    /// 高速メモリキャッシュ実装
    /// 小〜中サイズのUVデータの高速アクセス用
    /// </summary>
    public class MemoryUVCache : IUVCache, IDisposable
    {
        #region Private Fields
        private readonly Dictionary<string, UVCacheData> memoryCache;
        private readonly object lockObject = new object();
        private bool isDisposed = false;
        #endregion

        #region Constructor
        public MemoryUVCache()
        {
            memoryCache = new Dictionary<string, UVCacheData>();
        }
        #endregion

        #region IUVCache Implementation
        public bool CacheUVData(string meshKey, Texture2D uvTexture, UVIslandData[] islandData, int[] selectedIslands)
        {
            if (isDisposed || string.IsNullOrEmpty(meshKey) || uvTexture == null || islandData == null)
                return false;

            lock (lockObject)
            {
                try
                {
                    var cacheData = new UVCacheData
                    {
                        uvTexture = uvTexture,
                        previewTexture = CreatePreviewTexture(uvTexture, 128),
                        islands = islandData,
                        selectedIslandIDs = selectedIslands ?? new int[0],
                        meshHash = meshKey.GetHashCode(),
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        zoomLevel = 1.0f,
                        panOffset = Vector2.zero
                    };

                    memoryCache[meshKey] = cacheData;
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MemoryUVCache] Error caching data for {meshKey}: {e.Message}");
                    return false;
                }
            }
        }

        public UVCacheData LoadUVData(string meshKey)
        {
            if (isDisposed || string.IsNullOrEmpty(meshKey))
                return default;

            lock (lockObject)
            {
                if (memoryCache.TryGetValue(meshKey, out var cacheData))
                {
                    return cacheData;
                }
                return default;
            }
        }

        public Texture2D GetPreviewTexture(string meshKey, int resolution = 128)
        {
            if (isDisposed || string.IsNullOrEmpty(meshKey))
                return null;

            lock (lockObject)
            {
                if (memoryCache.TryGetValue(meshKey, out var cacheData))
                {
                    if (cacheData.previewTexture != null && cacheData.previewTexture.width == resolution)
                    {
                        return cacheData.previewTexture;
                    }
                    else if (cacheData.uvTexture != null)
                    {
                        return CreatePreviewTexture(cacheData.uvTexture, resolution);
                    }
                }
                return null;
            }
        }

        public bool IsValidCache(string meshKey, int meshHash)
        {
            if (isDisposed || string.IsNullOrEmpty(meshKey))
                return false;

            lock (lockObject)
            {
                if (memoryCache.TryGetValue(meshKey, out var cacheData))
                {
                    return cacheData.IsValid && cacheData.meshHash == meshHash;
                }
                return false;
            }
        }

        public void InvalidateCache(string meshKey)
        {
            if (isDisposed || string.IsNullOrEmpty(meshKey))
                return;

            lock (lockObject)
            {
                if (memoryCache.TryGetValue(meshKey, out var cacheData))
                {
                    // Texture cleanup
                    if (cacheData.previewTexture != null)
                    {
                        UnityEngine.Object.DestroyImmediate(cacheData.previewTexture);
                    }
                    memoryCache.Remove(meshKey);
                }
            }
        }

        public void OptimizeMemoryUsage()
        {
            if (isDisposed) return;

            lock (lockObject)
            {
                var keysToRemove = new List<string>();
                var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                const long maxAge = 3600; // 1 hour

                foreach (var kvp in memoryCache)
                {
                    if (currentTime - kvp.Value.timestamp > maxAge)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    InvalidateCache(key);
                }

                if (keysToRemove.Count > 0)
                {
                    Debug.Log($"[MemoryUVCache] Cleaned up {keysToRemove.Count} expired cache entries");
                }
            }
        }
        #endregion

        #region Helper Methods
        private Texture2D CreatePreviewTexture(Texture2D source, int targetSize)
        {
            if (source == null) return null;

            try
            {
                var preview = new Texture2D(targetSize, targetSize, TextureFormat.RGBA32, false);
                var sourcePixels = source.GetPixels();
                var targetPixels = new Color[targetSize * targetSize];

                float scaleX = (float)source.width / targetSize;
                float scaleY = (float)source.height / targetSize;

                for (int y = 0; y < targetSize; y++)
                {
                    for (int x = 0; x < targetSize; x++)
                    {
                        int sourceX = Mathf.FloorToInt(x * scaleX);
                        int sourceY = Mathf.FloorToInt(y * scaleY);
                        
                        sourceX = Mathf.Clamp(sourceX, 0, source.width - 1);
                        sourceY = Mathf.Clamp(sourceY, 0, source.height - 1);
                        
                        targetPixels[y * targetSize + x] = sourcePixels[sourceY * source.width + sourceX];
                    }
                }

                preview.SetPixels(targetPixels);
                preview.Apply();
                return preview;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MemoryUVCache] Error creating preview texture: {e.Message}");
                return null;
            }
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            if (isDisposed) return;

            lock (lockObject)
            {
                foreach (var kvp in memoryCache)
                {
                    if (kvp.Value.previewTexture != null)
                    {
                        UnityEngine.Object.DestroyImmediate(kvp.Value.previewTexture);
                    }
                }
                memoryCache.Clear();
            }

            isDisposed = true;
        }
        #endregion
    }
}