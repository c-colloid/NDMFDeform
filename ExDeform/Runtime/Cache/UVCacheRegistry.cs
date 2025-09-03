using System;
using System.Collections.Generic;
using UnityEngine;
//using ExDeform.Runtime.Cache.Implementations;
using ExDeform.Runtime.Cache.Interfaces;
using ExDeform.Runtime.Data;
using ExDeform.Editor;

namespace ExDeform.Runtime.Cache
{
    /// <summary>
    /// ExDeform統合キャッシュレジストリ
    /// 複数のキャッシュ実装を統合管理し、最適なパフォーマンスを提供
    /// </summary>
    public class UVCacheRegistry : IDisposable
    {
        #region シングルトン
        private static UVCacheRegistry instance;
        private static readonly object lockObject = new object();
        
        public static UVCacheRegistry Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (lockObject)
                    {
                        if (instance == null)
                        {
                            instance = new UVCacheRegistry();
                        }
                    }
                }
                return instance;
            }
        }
        #endregion
        
        #region フィールド
        private readonly Dictionary<CacheType, IUVCache> cacheProviders;
        private readonly Dictionary<string, CacheType> meshToProviderMap;
        private readonly CachePerformanceMonitor performanceMonitor;
        
        private CacheType primaryCacheType = CacheType.RobustUVCache;
        private bool isDisposed = false;
        #endregion
        
        #region コンストラクタ
        private UVCacheRegistry()
        {
            cacheProviders = new Dictionary<CacheType, IUVCache>();
            meshToProviderMap = new Dictionary<string, CacheType>();
            performanceMonitor = new CachePerformanceMonitor();
            
            InitializeCacheProviders();
        }
        #endregion
        
        #region 初期化
        private void InitializeCacheProviders()
        {
            try
            {
                // 各キャッシュ実装を初期化
                cacheProviders[CacheType.MemoryUVCache] = new MemoryUVCache();
                cacheProviders[CacheType.OptimalUVCache] = new OptimalUVCacheAdapter();
                cacheProviders[CacheType.RobustUVCache] = new RobustUVCacheAdapter();
                
                Debug.Log($"[UVCacheRegistry] {cacheProviders.Count}個のキャッシュプロバイダーを初期化しました");
            }
            catch (Exception e)
            {
                Debug.LogError($"[UVCacheRegistry] キャッシュプロバイダー初期化エラー: {e.Message}");
            }
        }
        #endregion
        
        #region 公開メソッド
        /// <summary>
        /// 最適なキャッシュプロバイダーを使用してUVデータをキャッシュ
        /// </summary>
        /// <param name="meshKey">メッシュキー</param>
        /// <param name="uvTexture">UVテクスチャ</param>
        /// <param name="islandData">アイランドデータ</param>
        /// <param name="selectedIslands">選択アイランド</param>
        /// <returns>キャッシュ成功時true</returns>
        public bool CacheUVData(string meshKey, Texture2D uvTexture, UVIslandData[] islandData, int[] selectedIslands)
        {
            if (isDisposed || string.IsNullOrEmpty(meshKey) || uvTexture == null)
                return false;
            
            var provider = GetOptimalProvider(meshKey, uvTexture);
            var startTime = DateTime.UtcNow;
            
            try
            {
                var result = provider.CacheUVData(meshKey, uvTexture, islandData, selectedIslands);
                
                // パフォーマンス記録
                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                performanceMonitor.RecordCacheOperation(GetCacheType(provider), true, duration, false);
                
                // 成功したプロバイダーをマッピング
                if (result)
                {
                    meshToProviderMap[meshKey] = GetCacheType(provider);
                }
                
                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UVCacheRegistry] キャッシュ保存エラー ({meshKey}): {e.Message}");
                
                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                performanceMonitor.RecordCacheOperation(GetCacheType(provider), false, duration, false);
                
                return false;
            }
        }
        
        /// <summary>
        /// 最適なキャッシュプロバイダーからUVデータを読み込み
        /// </summary>
        /// <param name="meshKey">メッシュキー</param>
        /// <returns>キャッシュデータ、存在しない場合default</returns>
        public UVCacheData LoadUVData(string meshKey)
        {
            if (isDisposed || string.IsNullOrEmpty(meshKey))
                return default;
            
            // 前回使用したプロバイダーを優先
            if (meshToProviderMap.TryGetValue(meshKey, out var preferredType) && 
                cacheProviders.TryGetValue(preferredType, out var preferredProvider))
            {
                var result = TryLoadFromProvider(preferredProvider, meshKey);
                if (result.IsValid)
                    return result;
            }
            
            // 全プロバイダーから検索
            foreach (var kvp in cacheProviders)
            {
                if (kvp.Key == preferredType) continue; // 既に試行済み
                
                var result = TryLoadFromProvider(kvp.Value, meshKey);
                if (result.IsValid)
                {
                    // 見つかったプロバイダーを記録
                    meshToProviderMap[meshKey] = kvp.Key;
                    return result;
                }
            }
            
            return default; // 見つからない
        }
        
        /// <summary>
        /// プレビューテクスチャの高速取得
        /// </summary>
        /// <param name="meshKey">メッシュキー</param>
        /// <param name="resolution">解像度</param>
        /// <returns>プレビューテクスチャ</returns>
        public Texture2D GetPreviewTexture(string meshKey, int resolution = 128)
        {
            if (isDisposed || string.IsNullOrEmpty(meshKey))
                return null;
            
            // メモリキャッシュを優先（高速）
            if (cacheProviders.TryGetValue(CacheType.MemoryUVCache, out var memoryCache))
            {
                var preview = memoryCache.GetPreviewTexture(meshKey, resolution);
                if (preview != null) return preview;
            }
            
            // 他のプロバイダーから検索
            foreach (var provider in cacheProviders.Values)
            {
                var preview = provider.GetPreviewTexture(meshKey, resolution);
                if (preview != null) return preview;
            }
            
            return null;
        }
        
        /// <summary>
        /// 有効なキャッシュの存在確認
        /// </summary>
        /// <param name="meshKey">メッシュキー</param>
        /// <param name="meshHash">メッシュハッシュ</param>
        /// <returns>有効なキャッシュが存在する場合true</returns>
        public bool IsValidCache(string meshKey, int meshHash)
        {
            if (isDisposed || string.IsNullOrEmpty(meshKey))
                return false;
            
            // 優先プロバイダーから確認
            if (meshToProviderMap.TryGetValue(meshKey, out var preferredType) &&
                cacheProviders.TryGetValue(preferredType, out var preferredProvider))
            {
                if (preferredProvider.IsValidCache(meshKey, meshHash))
                    return true;
            }
            
            // 全プロバイダーで確認
            foreach (var provider in cacheProviders.Values)
            {
                if (provider.IsValidCache(meshKey, meshHash))
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// 特定メッシュのキャッシュ無効化
        /// </summary>
        /// <param name="meshKey">無効化するメッシュキー</param>
        public void InvalidateCache(string meshKey)
        {
            if (isDisposed || string.IsNullOrEmpty(meshKey))
                return;
            
            foreach (var provider in cacheProviders.Values)
            {
                try
                {
                    provider.InvalidateCache(meshKey);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UVCacheRegistry] キャッシュ無効化エラー ({meshKey}): {e.Message}");
                }
            }
            
            meshToProviderMap.Remove(meshKey);
        }
        
        /// <summary>
        /// 全プロバイダーのメモリ最適化
        /// </summary>
        public void OptimizeMemoryUsage()
        {
            if (isDisposed) return;
            
            foreach (var provider in cacheProviders.Values)
            {
                try
                {
                    provider.OptimizeMemoryUsage();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UVCacheRegistry] メモリ最適化エラー: {e.Message}");
                }
            }
            
            performanceMonitor.TrimOldRecords();
        }
        
        /// <summary>
        /// 統合パフォーマンス統計の取得
        /// </summary>
        /// <returns>パフォーマンス統計</returns>
        public CacheRegistryStatistics GetStatistics()
        {
            return new CacheRegistryStatistics
            {
                totalProviders = cacheProviders.Count,
                activeMeshMappings = meshToProviderMap.Count,
                overallPerformance = performanceMonitor.GetOverallStatistics(),
                providerPerformance = performanceMonitor.GetProviderStatistics()
            };
        }
        #endregion
        
        #region 内部メソッド
        private IUVCache GetOptimalProvider(string meshKey, Texture2D texture)
        {
            // メッシュサイズに基づく最適プロバイダー選択
            var textureSize = texture.width * texture.height * 4; // RGBA
            
            if (textureSize < 256 * 256 * 4) // 256KB未満
            {
                return cacheProviders[CacheType.MemoryUVCache];
            }
            else if (textureSize < 1024 * 1024 * 4) // 4MB未満
            {
                return cacheProviders[CacheType.OptimalUVCache];
            }
            else
            {
                return cacheProviders[CacheType.RobustUVCache];
            }
        }
        
        private UVCacheData TryLoadFromProvider(IUVCache provider, string meshKey)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                var result = provider.LoadUVData(meshKey);
                
                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                performanceMonitor.RecordCacheOperation(GetCacheType(provider), result.IsValid, duration, true);
                
                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UVCacheRegistry] プロバイダー読み込みエラー ({meshKey}): {e.Message}");
                
                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                performanceMonitor.RecordCacheOperation(GetCacheType(provider), false, duration, true);
                
                return default;
            }
        }
        
        private CacheType GetCacheType(IUVCache provider)
        {
            foreach (var kvp in cacheProviders)
            {
                if (kvp.Value == provider)
                    return kvp.Key;
            }
            return CacheType.MemoryUVCache; // フォールバック
        }
        #endregion
        
        #region IDisposable
        public void Dispose()
        {
            if (isDisposed) return;
            
            isDisposed = true;
            
            foreach (var provider in cacheProviders.Values)
            {
                if (provider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            
            cacheProviders.Clear();
            meshToProviderMap.Clear();
            performanceMonitor?.Dispose();
        }
        #endregion
    }
    
    /// <summary>
    /// キャッシュプロバイダータイプ
    /// </summary>
    public enum CacheType
    {
        MemoryUVCache = 0,     // 高速メモリキャッシュ
        OptimalUVCache = 1,    // 最適化ファイルキャッシュ
        RobustUVCache = 2      // 堅牢ファイルキャッシュ
    }
    
    /// <summary>
    /// キャッシュレジストリ統計情報
    /// </summary>
    [System.Serializable]
    public struct CacheRegistryStatistics
    {
        public int totalProviders;
        public int activeMeshMappings;
        public PerformanceStatistics overallPerformance;
        public Dictionary<CacheType, PerformanceStatistics> providerPerformance;
        
        public override string ToString()
        {
            return $"CacheRegistry: Providers={totalProviders}, " +
                   $"Mappings={activeMeshMappings}, " +
                   $"HitRate={overallPerformance.hitRate:P1}";
        }
    }
}