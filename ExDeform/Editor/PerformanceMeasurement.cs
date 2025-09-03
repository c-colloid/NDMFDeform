using System;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using ExDeform.Core.Interfaces;

namespace ExDeform.Editor
{
    /// <summary>
    /// Performance measurement data
    /// パフォーマンス測定データ
    /// </summary>
    [Serializable]
    public struct PerformanceResult
    {
        public string implementationName;
        public double avgSaveTime;
        public double avgLoadTime;
        public double avgCheckTime;
    }

    /// <summary>
    /// Performance measurement utilities for cache testing
    /// キャッシュテスト用パフォーマンス測定ユーティリティ
    /// </summary>
    public static class PerformanceMeasurement
    {
        /// <summary>
        /// Measure cache implementation performance
        /// キャッシュ実装のパフォーマンスを測定
        /// </summary>
        /// <param name="cache">Cache implementation to test</param>
        /// <param name="baseKey">Base key for test data</param>
        /// <param name="testTexture">Texture to use for testing</param>
        /// <returns>Performance measurement results</returns>
        public static PerformanceResult MeasureCachePerformance(ICacheStorage cache, string baseKey, Texture2D testTexture)
        {
            UnityEngine.Debug.Log($"--- Testing {cache.CacheTypeName} Implementation ---");
            
            var stopwatch = new Stopwatch();
            var result = new PerformanceResult { implementationName = cache.CacheTypeName };
            
            // Save performance test
            var saveTimes = new double[CacheConstants.TEST_ITERATIONS];
            for (int i = 0; i < CacheConstants.TEST_ITERATIONS; i++)
            {
                var key = $"{baseKey}_{i}";
                stopwatch.Restart();
                cache.SaveTexture(key, testTexture);
                stopwatch.Stop();
                saveTimes[i] = stopwatch.Elapsed.TotalMilliseconds;
            }
            result.avgSaveTime = saveTimes.Average();
            
            // Load performance test  
            var loadTimes = new double[CacheConstants.TEST_ITERATIONS];
            for (int i = 0; i < CacheConstants.TEST_ITERATIONS; i++)
            {
                var key = $"{baseKey}_{i}";
                stopwatch.Restart();
                var loaded = cache.LoadTexture(key);
                stopwatch.Stop();
                loadTimes[i] = stopwatch.Elapsed.TotalMilliseconds;
                if (loaded != null) UnityEngine.Object.DestroyImmediate(loaded);
            }
            result.avgLoadTime = loadTimes.Average();
            
            // Existence check performance test
            var checkTimes = new double[CacheConstants.TEST_ITERATIONS];
            for (int i = 0; i < CacheConstants.TEST_ITERATIONS; i++)
            {
                var key = $"{baseKey}_{i}";
                stopwatch.Restart();
                cache.HasCache(key);
                stopwatch.Stop();
                checkTimes[i] = stopwatch.Elapsed.TotalMilliseconds;
            }
            result.avgCheckTime = checkTimes.Average();
            
            // Cleanup test data for this implementation
            for (int i = 0; i < CacheConstants.TEST_ITERATIONS; i++)
            {
                cache.ClearCache($"{baseKey}_{i}");
            }
            
            return result;
        }

        /// <summary>
        /// Display comprehensive performance results
        /// 包括的なパフォーマンス結果を表示
        /// </summary>
        /// <param name="results">Performance results to display</param>
        /// <param name="testTexture">Texture used for testing (for memory estimation)</param>
        public static void DisplayResults(PerformanceResult[] results, Texture2D testTexture)
        {
            UnityEngine.Debug.Log("\n=== Performance Comparison ===");
            
            foreach (var result in results)
            {
                UnityEngine.Debug.Log($"{result.implementationName.PadRight(15)} - " +
                         $"Save: {result.avgSaveTime.ToString($"F{CacheConstants.PERFORMANCE_DECIMAL_PLACES}")}ms, " +
                         $"Load: {result.avgLoadTime.ToString($"F{CacheConstants.PERFORMANCE_DECIMAL_PLACES}")}ms, " +
                         $"Check: {(result.avgCheckTime * 1000).ToString($"F{CacheConstants.PERFORMANCE_DECIMAL_PLACES + 3}")}μs");
            }
            
            // Memory usage estimation
            DisplayMemoryUsageComparison(testTexture);
            
            // Performance ranking
            DisplayPerformanceRanking(results);
        }

        /// <summary>
        /// Display single implementation results
        /// 単一実装の結果を表示
        /// </summary>
        /// <param name="result">Performance result to display</param>
        public static void DisplaySingleResult(PerformanceResult result)
        {
            UnityEngine.Debug.Log($"\n=== {result.implementationName} Results ===");
            UnityEngine.Debug.Log($"Average Save Time: {result.avgSaveTime:F3}ms");
            UnityEngine.Debug.Log($"Average Load Time: {result.avgLoadTime:F3}ms"); 
            UnityEngine.Debug.Log($"Average Check Time: {result.avgCheckTime * 1000:F6}μs");
        }

        private static void DisplayMemoryUsageComparison(Texture2D testTexture)
        {
            UnityEngine.Debug.Log("\n=== Memory Usage Estimation ===");
            
            var pngSize = testTexture.EncodeToPNG().Length;
            var editorPrefsMemory = (long)(pngSize * CacheConstants.BASE64_OVERHEAD_FACTOR);
            
            UnityEngine.Debug.Log($"EditorPrefs     : ~{(editorPrefsMemory / CacheConstants.BYTES_TO_KB).ToString($"F{CacheConstants.MEMORY_DECIMAL_PLACES}")}KB (Base64 overhead included)");
            UnityEngine.Debug.Log($"JsonFile        : ~{(pngSize / CacheConstants.BYTES_TO_KB).ToString($"F{CacheConstants.MEMORY_DECIMAL_PLACES}")}KB (JSON metadata overhead ~5%)");
            UnityEngine.Debug.Log($"BinaryFile      : ~{(pngSize / CacheConstants.BYTES_TO_KB).ToString($"F{CacheConstants.MEMORY_DECIMAL_PLACES}")}KB (pure binary data)");
        }

        private static void DisplayPerformanceRanking(PerformanceResult[] results)
        {
            UnityEngine.Debug.Log("\n=== Performance Ranking ===");
            
            var saveRanking = results.OrderBy(r => r.avgSaveTime).ToArray();
            var loadRanking = results.OrderBy(r => r.avgLoadTime).ToArray();
            var checkRanking = results.OrderBy(r => r.avgCheckTime).ToArray();
            
            UnityEngine.Debug.Log("Save Speed  : " + string.Join(" > ", saveRanking.Select(r => r.implementationName)));
            UnityEngine.Debug.Log("Load Speed  : " + string.Join(" > ", loadRanking.Select(r => r.implementationName)));  
            UnityEngine.Debug.Log("Check Speed : " + string.Join(" > ", checkRanking.Select(r => r.implementationName)));
        }
    }
}