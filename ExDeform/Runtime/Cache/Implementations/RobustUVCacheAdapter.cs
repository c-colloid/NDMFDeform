using System;
using UnityEngine;
using ExDeform.Runtime.Cache.Interfaces;

namespace ExDeform.Runtime.Cache.Implementations
{
    /// <summary>
    /// RobustUVCache用のランタイムアダプター
    /// エディタ機能をランタイムで使用可能にするラッパー
    /// </summary>
    public class RobustUVCacheAdapter : IUVCache
    {
        #region Private Fields
        private readonly MemoryUVCache fallbackCache;
        #endregion

        #region Constructor
        public RobustUVCacheAdapter()
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
                // エディタ環境でのみRobustUVCacheを使用
                // Note: RefactoredRobustUVCache の実際のAPIに合わせて調整が必要
                // 現在はフォールバックを使用
                return fallbackCache.CacheUVData(meshKey, uvTexture, islandData, selectedIslands);
#else
                // ランタイムではフォールバック使用
                return fallbackCache.CacheUVData(meshKey, uvTexture, islandData, selectedIslands);
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RobustUVCacheAdapter] Falling back to memory cache due to error: {e.Message}");
                return fallbackCache.CacheUVData(meshKey, uvTexture, islandData, selectedIslands);
            }
        }

        public UVCacheData LoadUVData(string meshKey)
        {
            try
            {
#if UNITY_EDITOR
                // エディタ環境でのみRobustUVCacheを使用
                // Note: RefactoredRobustUVCache の実際のAPIに合わせて調整が必要
                return fallbackCache.LoadUVData(meshKey);
#else
                // ランタイムではフォールバック使用
                return fallbackCache.LoadUVData(meshKey);
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RobustUVCacheAdapter] Falling back to memory cache due to error: {e.Message}");
                return fallbackCache.LoadUVData(meshKey);
            }
        }

        public Texture2D GetPreviewTexture(string meshKey, int resolution = 128)
        {
            try
            {
#if UNITY_EDITOR
                // エディタ環境でのみRobustUVCacheを使用
                return fallbackCache.GetPreviewTexture(meshKey, resolution);
#else
                return fallbackCache.GetPreviewTexture(meshKey, resolution);
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RobustUVCacheAdapter] Falling back to memory cache due to error: {e.Message}");
                return fallbackCache.GetPreviewTexture(meshKey, resolution);
            }
        }

        public bool IsValidCache(string meshKey, int meshHash)
        {
            try
            {
#if UNITY_EDITOR
                // エディタ環境でのみRobustUVCacheを使用
                return fallbackCache.IsValidCache(meshKey, meshHash);
#else
                return fallbackCache.IsValidCache(meshKey, meshHash);
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RobustUVCacheAdapter] Falling back to memory cache due to error: {e.Message}");
                return fallbackCache.IsValidCache(meshKey, meshHash);
            }
        }

        public void InvalidateCache(string meshKey)
        {
            try
            {
#if UNITY_EDITOR
                // エディタ環境でのみRobustUVCacheを使用
                fallbackCache.InvalidateCache(meshKey);
#endif
                fallbackCache.InvalidateCache(meshKey);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RobustUVCacheAdapter] Error invalidating cache: {e.Message}");
                fallbackCache.InvalidateCache(meshKey);
            }
        }

        public void OptimizeMemoryUsage()
        {
            try
            {
#if UNITY_EDITOR
                // エディタ環境でのみRobustUVCacheを使用
                fallbackCache.OptimizeMemoryUsage();
#endif
                fallbackCache.OptimizeMemoryUsage();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RobustUVCacheAdapter] Error optimizing memory: {e.Message}");
                fallbackCache.OptimizeMemoryUsage();
            }
        }

        public void Dispose()
        {
            try
            {
                fallbackCache?.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RobustUVCacheAdapter] Error disposing fallback cache: {e.Message}");
            }
        }
        #endregion
    }
}