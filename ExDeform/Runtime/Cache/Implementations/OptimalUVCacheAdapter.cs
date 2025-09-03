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
                // ランタイムではフォールバック使用（エディタキャッシュは利用不可）
                return fallbackCache.CacheUVData(meshKey, uvTexture, islandData, selectedIslands);
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
                // ランタイムではフォールバック使用（エディタキャッシュは利用不可）
                return fallbackCache.LoadUVData(meshKey);
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
                // ランタイムではフォールバック使用（エディタキャッシュは利用不可）
                return fallbackCache.GetPreviewTexture(meshKey, resolution);
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
                // ランタイムではフォールバック使用（エディタキャッシュは利用不可）
                return fallbackCache.IsValidCache(meshKey, meshHash);
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
                // ランタイムではフォールバック使用（エディタキャッシュは利用不可）
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
                // ランタイムではフォールバック使用（エディタキャッシュは利用不可）
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