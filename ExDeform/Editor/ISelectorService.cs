using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExDeform.Editor
{
    /// <summary>
    /// Service interface for managing UVIslandSelector lifecycle, initialization, and disposal
    /// UVIslandSelectorのライフサイクル、初期化、破棄を管理するサービスインターフェース
    /// </summary>
    public interface ISelectorService
    {
        #region Selector Lifecycle Management
        
        /// <summary>
        /// Create or retrieve a cached UVIslandSelector for the specified mesh
        /// 指定されたメッシュのUVIslandSelectorを作成または取得
        /// </summary>
        /// <param name="mesh">Target mesh for UV island analysis</param>
        /// <param name="cacheKey">Optional cache key for persistent storage</param>
        /// <returns>Configured UVIslandSelector instance</returns>
        UVIslandSelector GetOrCreateSelector(Mesh mesh, string cacheKey = null);
        
        /// <summary>
        /// Create or retrieve a UVIslandSelector with specific configuration
        /// 特定の設定でUVIslandSelectorを作成または取得
        /// </summary>
        /// <param name="config">Selector configuration options</param>
        /// <returns>Configured UVIslandSelector instance</returns>
        UVIslandSelector GetOrCreateSelector(SelectorConfig config);
        
        /// <summary>
        /// Initialize a selector with mesh data and configuration
        /// メッシュデータと設定でセレクターを初期化
        /// </summary>
        /// <param name="selector">Selector to initialize</param>
        /// <param name="mesh">Target mesh</param>
        /// <param name="config">Optional configuration</param>
        void InitializeSelector(UVIslandSelector selector, Mesh mesh, SelectorConfig config = null);
        
        /// <summary>
        /// Release a selector and clean up its resources
        /// セレクターをリリースしてリソースを解放
        /// </summary>
        /// <param name="selector">Selector to dispose</param>
        void DisposeSelector(UVIslandSelector selector);
        
        /// <summary>
        /// Release a selector by cache key
        /// キャッシュキーでセレクターをリリース
        /// </summary>
        /// <param name="cacheKey">Cache key of selector to dispose</param>
        void DisposeSelector(string cacheKey);
        
        #endregion
        
        #region Cache Management
        
        /// <summary>
        /// Check if a selector exists in cache
        /// セレクターがキャッシュに存在するかチェック
        /// </summary>
        /// <param name="cacheKey">Cache key to check</param>
        /// <returns>True if selector exists in cache</returns>
        bool HasCachedSelector(string cacheKey);
        
        /// <summary>
        /// Generate a cache key for a mesh
        /// メッシュのキャッシュキーを生成
        /// </summary>
        /// <param name="mesh">Target mesh</param>
        /// <returns>Generated cache key</returns>
        string GenerateCacheKey(Mesh mesh);
        
        /// <summary>
        /// Generate a cache key with custom suffix
        /// カスタムサフィックス付きのキャッシュキーを生成
        /// </summary>
        /// <param name="mesh">Target mesh</param>
        /// <param name="suffix">Custom suffix for cache key</param>
        /// <returns>Generated cache key</returns>
        string GenerateCacheKey(Mesh mesh, string suffix);
        
        /// <summary>
        /// Clear all cached selectors
        /// すべてのキャッシュされたセレクターをクリア
        /// </summary>
        void ClearCache();
        
        /// <summary>
        /// Clear cached selectors matching pattern
        /// パターンに一致するキャッシュされたセレクターをクリア
        /// </summary>
        /// <param name="keyPattern">Pattern to match cache keys</param>
        void ClearCache(string keyPattern);
        
        #endregion
        
        #region Statistics and Health
        
        /// <summary>
        /// Get service statistics and cache information
        /// サービス統計とキャッシュ情報を取得
        /// </summary>
        /// <returns>Service statistics</returns>
        SelectorServiceStatistics GetStatistics();
        
        /// <summary>
        /// Perform health check and cleanup of stale selectors
        /// 古いセレクターの健康チェックとクリーンアップを実行
        /// </summary>
        void PerformHealthCheck();
        
        #endregion
    }
    
    /// <summary>
    /// Configuration options for selector creation and initialization
    /// セレクターの作成と初期化の設定オプション
    /// </summary>
    public class SelectorConfig
    {
        /// <summary>
        /// Target mesh for UV island analysis
        /// </summary>
        public Mesh TargetMesh { get; set; }
        
        /// <summary>
        /// Transform for scene rendering
        /// </summary>
        public Transform TargetTransform { get; set; }
        
        /// <summary>
        /// Optional cache key for persistent storage
        /// </summary>
        public string CacheKey { get; set; }
        
        /// <summary>
        /// Pre-selected island IDs
        /// </summary>
        public List<int> SelectedIslandIDs { get; set; } = new List<int>();
        
        /// <summary>
        /// Dynamic mesh for highlighting (optional)
        /// </summary>
        public Mesh DynamicMesh { get; set; }
        
        /// <summary>
        /// Whether to use adaptive vertex sizing
        /// </summary>
        public bool UseAdaptiveVertexSize { get; set; } = true;
        
        /// <summary>
        /// Manual vertex sphere size
        /// </summary>
        public float ManualVertexSphereSize { get; set; } = 0.01f;
        
        /// <summary>
        /// Adaptive size multiplier
        /// </summary>
        public float AdaptiveSizeMultiplier { get; set; } = 0.007f;
        
        /// <summary>
        /// Enable auto-update of preview
        /// </summary>
        public bool AutoUpdatePreview { get; set; } = true;
        
        /// <summary>
        /// Enable range selection
        /// </summary>
        public bool EnableRangeSelection { get; set; } = true;
        
        /// <summary>
        /// Enable magnifying glass
        /// </summary>
        public bool EnableMagnifyingGlass { get; set; } = true;
        
        /// <summary>
        /// Maximum vertices to display for performance
        /// </summary>
        public int MaxDisplayVertices { get; set; } = 1000;
        
        /// <summary>
        /// Enable performance optimization
        /// </summary>
        public bool EnablePerformanceOptimization { get; set; } = true;
    }
    
    /// <summary>
    /// Statistics for selector service performance and cache status
    /// セレクターサービスのパフォーマンスとキャッシュステータスの統計
    /// </summary>
    public struct SelectorServiceStatistics
    {
        /// <summary>
        /// Total number of cached selectors
        /// </summary>
        public int totalCachedSelectors;
        
        /// <summary>
        /// Number of cache hits
        /// </summary>
        public int cacheHits;
        
        /// <summary>
        /// Number of cache misses
        /// </summary>
        public int cacheMisses;
        
        /// <summary>
        /// Cache hit rate
        /// </summary>
        public float cacheHitRate;
        
        /// <summary>
        /// Total number of selectors created
        /// </summary>
        public int totalSelectorsCreated;
        
        /// <summary>
        /// Total number of selectors disposed
        /// </summary>
        public int totalSelectorsDisposed;
        
        /// <summary>
        /// Active selectors currently in use
        /// </summary>
        public int activeSelectors;
        
        /// <summary>
        /// Total memory usage estimate in bytes
        /// </summary>
        public long estimatedMemoryUsage;
        
        /// <summary>
        /// Last health check time
        /// </summary>
        public DateTime lastHealthCheck;
        
        public override string ToString()
        {
            return $"Cached: {totalCachedSelectors}, Active: {activeSelectors}, " +
                   $"Hit Rate: {cacheHitRate:P1}, Created: {totalSelectorsCreated}, " +
                   $"Disposed: {totalSelectorsDisposed}, Memory: {estimatedMemoryUsage / 1024}KB";
        }
    }
}