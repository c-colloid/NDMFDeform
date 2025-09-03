using UnityEngine;
using UnityEditor;

namespace ExDeform.Editor
{
    /// <summary>
    /// Example demonstrating how to integrate ISelectorService into existing editor code
    /// 既存エディタコードへのISelectorServiceの統合例
    /// </summary>
    public static class SelectorServiceIntegrationExample
    {
        /// <summary>
        /// Example: Refactoring existing editor initialization pattern
        /// 例：既存のエディタ初期化パターンのリファクタリング
        /// </summary>
        public static void ExampleEditorInitialization(UVIslandMask targetMask, Mesh originalMesh, Transform rendererTransform)
        {
            // OLD WAY (what editors currently do):
            // var selector = new UVIslandSelector(originalMesh);
            // selector.SetSelectedIslands(targetMask.SelectedIslandIDs);
            // selector.TargetTransform = rendererTransform;

            // NEW WAY (using ISelectorService):
            var selectorService = SelectorService.Instance;
            
            var config = new SelectorConfig
            {
                TargetMesh = originalMesh,
                TargetTransform = rendererTransform,
                SelectedIslandIDs = targetMask.SelectedIslandIDs,
                CacheKey = $"editor_{targetMask.GetInstanceID()}",
                AutoUpdatePreview = true,
                EnableRangeSelection = true,
                EnableMagnifyingGlass = true
            };
            
            var selector = selectorService.GetOrCreateSelector(config);
            
            // The selector is now properly cached and managed by the service
            // セレクターは適切にキャッシュされ、サービスによって管理される
        }
        
        /// <summary>
        /// Example: Proper cleanup in editor OnDestroy/OnDisable
        /// 例：エディタのOnDestroy/OnDisableでの適切なクリーンアップ
        /// </summary>
        public static void ExampleEditorCleanup(string cacheKey)
        {
            // OLD WAY (what editors currently do):
            // if (cachedSelector != null)
            // {
            //     cachedSelector.Dispose();
            //     cachedSelector = null;
            // }

            // NEW WAY (using ISelectorService):
            var selectorService = SelectorService.Instance;
            
            // The service handles disposal automatically and manages cache lifecycle
            // サービスが自動的に破棄を処理し、キャッシュライフサイクルを管理する
            if (!string.IsNullOrEmpty(cacheKey))
            {
                // Optional: explicitly dispose if you want immediate cleanup
                // オプション：即座にクリーンアップしたい場合は明示的に破棄
                selectorService.DisposeSelector(cacheKey);
            }
            
            // Otherwise, the service will automatically clean up expired selectors
            // そうでなければ、サービスが期限切れのセレクターを自動的にクリーンアップする
        }
        
        /// <summary>
        /// Example: Getting service statistics for debugging/monitoring
        /// 例：デバッグ/監視のためのサービス統計取得
        /// </summary>
        [MenuItem("ExDeform/Debug/Show Selector Service Statistics")]
        public static void ShowServiceStatistics()
        {
            var selectorService = SelectorService.Instance;
            var stats = selectorService.GetStatistics();
            
            Debug.Log($"Selector Service Statistics:\n{stats}");
        }
        
        /// <summary>
        /// Example: Force cleanup of all cached selectors
        /// 例：すべてのキャッシュされたセレクターの強制クリーンアップ
        /// </summary>
        [MenuItem("ExDeform/Debug/Clear Selector Service Cache")]
        public static void ClearServiceCache()
        {
            var selectorService = SelectorService.Instance;
            selectorService.ClearCache();
            Debug.Log("Selector service cache cleared.");
        }
        
        /// <summary>
        /// Example: Create selector with different configurations for different use cases
        /// 例：異なるユースケース用の異なる設定でセレクターを作成
        /// </summary>
        public static void ExampleSpecializedSelectors(Mesh mesh)
        {
            var selectorService = SelectorService.Instance;
            
            // High-performance selector for large meshes
            // 大きなメッシュ用高性能セレクター
            var performanceConfig = new SelectorConfig
            {
                TargetMesh = mesh,
                CacheKey = $"performance_{mesh.GetInstanceID()}",
                EnablePerformanceOptimization = true,
                MaxDisplayVertices = 500, // Lower limit for better performance
                AutoUpdatePreview = false, // Manual updates only
                EnableMagnifyingGlass = false // Disable expensive features
            };
            var performanceSelector = selectorService.GetOrCreateSelector(performanceConfig);
            
            // Feature-rich selector for detailed editing
            // 詳細編集用機能豊富セレクター  
            var detailedConfig = new SelectorConfig
            {
                TargetMesh = mesh,
                CacheKey = $"detailed_{mesh.GetInstanceID()}",
                EnablePerformanceOptimization = false,
                MaxDisplayVertices = 2000, // Higher limit for more detail
                AutoUpdatePreview = true,
                EnableMagnifyingGlass = true,
                EnableRangeSelection = true,
                UseAdaptiveVertexSize = true
            };
            var detailedSelector = selectorService.GetOrCreateSelector(detailedConfig);
        }
        
        /// <summary>
        /// Example: Health check integration in editor update loops
        /// 例：エディタ更新ループでのヘルスチェック統合
        /// </summary>
        public static void ExampleHealthCheckIntegration()
        {
            var selectorService = SelectorService.Instance;
            var stats = selectorService.GetStatistics();
            
            // Perform health check if it's been a while
            // しばらく経っている場合はヘルスチェックを実行
            var timeSinceLastCheck = System.DateTime.Now - stats.lastHealthCheck;
            if (timeSinceLastCheck.TotalMinutes > 30)
            {
                selectorService.PerformHealthCheck();
            }
        }
    }
}