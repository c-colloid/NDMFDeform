using System;
using UnityEngine;
using ExDeform.Runtime.Cache.Interfaces;

namespace ExDeform.Runtime.Cache.Implementations
{
    /// <summary>
    /// OptimalUVCache用のランタイムアダプター
    /// エディタ機能をランタイムで使用可能にするラッパー
    /// </summary>
    public class OptimalUVCacheAdapter : IUVCache
    {
        #region Private Fields
        private readonly MemoryUVCache fallbackCache;
        #endregion

        #region Constructor
        public OptimalUVCacheAdapter()
        {
            // エディタコードはランタイムで使用できないため、フォールバックとしてMemoryUVCacheを使用
            fallbackCache = new MemoryUVCache();
        }
        #endregion

        #region IUVCache Implementation
        public bool CacheUVData(string meshKey, Texture2D uvTexture, UVIslandData[] islandData, int[] selectedIslands)
        {
            try
            {
#if UNITY_EDITOR
                // エディタ環境でのみOptimalUVCacheを使用
                return ExDeform.Editor.OptimalUVCache.SaveTexture(meshKey, uvTexture);
#else
                // ランタイムではフォールバック使用
                return fallbackCache.CacheUVData(meshKey, uvTexture, islandData, selectedIslands);
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCacheAdapter] Falling back to memory cache due to error: {e.Message}");
                return fallbackCache.CacheUVData(meshKey, uvTexture, islandData, selectedIslands);
            }
        }

        public UVCacheData LoadUVData(string meshKey)
        {
            try
            {
#if UNITY_EDITOR
                // エディタ環境でのみOptimalUVCacheを使用
                var texture = ExDeform.Editor.OptimalUVCache.LoadTexture(meshKey);
                if (texture != null)
                {
                    return new UVCacheData
                    {
                        uvTexture = texture,
                        previewTexture = CreatePreviewTexture(texture, 128),
                        islands = new UVIslandData[0], // OptimalUVCacheはテクスチャのみ
                        selectedIslandIDs = new int[0],
                        meshHash = meshKey.GetHashCode(),
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        zoomLevel = 1.0f,
                        panOffset = Vector2.zero
                    };
                }
                return default;
#else
                // ランタイムではフォールバック使用
                return fallbackCache.LoadUVData(meshKey);
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCacheAdapter] Falling back to memory cache due to error: {e.Message}");
                return fallbackCache.LoadUVData(meshKey);
            }
        }

        public Texture2D GetPreviewTexture(string meshKey, int resolution = 128)
        {
            try
            {
#if UNITY_EDITOR
                var texture = ExDeform.Editor.OptimalUVCache.LoadTexture(meshKey);
                if (texture != null)
                {
                    return CreatePreviewTexture(texture, resolution);
                }
                return null;
#else
                return fallbackCache.GetPreviewTexture(meshKey, resolution);
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCacheAdapter] Falling back to memory cache due to error: {e.Message}");
                return fallbackCache.GetPreviewTexture(meshKey, resolution);
            }
        }

        public bool IsValidCache(string meshKey, int meshHash)
        {
            try
            {
#if UNITY_EDITOR
                return ExDeform.Editor.OptimalUVCache.HasValidCache(meshKey);
#else
                return fallbackCache.IsValidCache(meshKey, meshHash);
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCacheAdapter] Falling back to memory cache due to error: {e.Message}");
                return fallbackCache.IsValidCache(meshKey, meshHash);
            }
        }

        public void InvalidateCache(string meshKey)
        {
            try
            {
#if UNITY_EDITOR
                ExDeform.Editor.OptimalUVCache.ClearCache(meshKey);
#endif
                fallbackCache.InvalidateCache(meshKey);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCacheAdapter] Error invalidating cache: {e.Message}");
                fallbackCache.InvalidateCache(meshKey);
            }
        }

        public void OptimizeMemoryUsage()
        {
            try
            {
#if UNITY_EDITOR
                ExDeform.Editor.OptimalUVCache.OptimizeStorage();
#endif
                fallbackCache.OptimizeMemoryUsage();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCacheAdapter] Error optimizing memory: {e.Message}");
                fallbackCache.OptimizeMemoryUsage();
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
                Debug.LogError($"[OptimalUVCacheAdapter] Error creating preview texture: {e.Message}");
                return null;
            }
        }
        #endregion
    }
}