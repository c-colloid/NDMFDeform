using System;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ExDeform.Core.Interfaces;
using ExDeform.Core.Constants;

namespace ExDeform.Editor
{
    /// <summary>
    /// Refactored performance benchmark tests for caching mechanisms
    /// リファクタリング済みキャッシュ機構のパフォーマンステスト（重複削除版）
    /// </summary>
    public static class RefactoredCachingPerformanceTest
    {
        private static readonly ICacheStorage[] testImplementations = {
            new EditorPrefsCache(),
            new JsonFileCache(),
            new BinaryFileCache()
        };
        
        [MenuItem("Tools/UV Island Cache/Run Refactored Performance Tests")]
        public static void RunPerformanceTests()
        {
            Debug.Log("=== Refactored UV Island Cache Performance Tests ===");
            
            var testTexture = CreateTestTexture();
            var testKey = "refactored_performance_test_key";
            var results = new PerformanceResult[testImplementations.Length];
            
            // Test all implementations using unified interface
            for (int i = 0; i < testImplementations.Length; i++)
            {
                var implementation = testImplementations[i];
                results[i] = TestCacheImplementation(implementation, testKey, testTexture);
            }
            
            // Display comprehensive results
            DisplayResults(results, testTexture);
            
            // Cleanup
            CleanupTestData(testKey);
            UnityEngine.Object.DestroyImmediate(testTexture);
        }
        
        [MenuItem("Tools/UV Island Cache/Run Single Implementation Test")]
        public static void RunSingleImplementationTest()
        {
            var choice = EditorUtility.DisplayDialogComplex(
                "Choose Cache Implementation",
                "Select cache implementation to test:",
                "EditorPrefs", "JsonFile", "BinaryFile");
                
            if (choice >= 0 && choice < testImplementations.Length)
            {
                var implementation = testImplementations[choice];
                var testTexture = CreateTestTexture();
                var testKey = "single_test_key";
                
                Debug.Log($"=== Testing {implementation.CacheTypeName} Only ===");
                var result = TestCacheImplementation(implementation, testKey, testTexture);
                DisplaySingleResult(result);
                
                implementation.ClearCache(testKey);
                UnityEngine.Object.DestroyImmediate(testTexture);
            }
        }
        
        private static PerformanceResult TestCacheImplementation(ICacheStorage cache, string baseKey, Texture2D testTexture)
        {
            Debug.Log($"--- Testing {cache.CacheTypeName} Implementation ---");
            
            var stopwatch = new Stopwatch();
            var result = new PerformanceResult { implementationName = cache.CacheTypeName };
            
            // Save performance test
            var saveTimes = new double[CacheConstants.PERFORMANCE_TEST_ITERATIONS];
            for (int i = 0; i < CacheConstants.PERFORMANCE_TEST_ITERATIONS; i++)
            {
                var key = $"{baseKey}_{i}";
                stopwatch.Restart();
                cache.SaveTexture(key, testTexture);
                stopwatch.Stop();
                saveTimes[i] = stopwatch.Elapsed.TotalMilliseconds;
            }
            result.avgSaveTime = saveTimes.Average();
            
            // Load performance test  
            var loadTimes = new double[CacheConstants.PERFORMANCE_TEST_ITERATIONS];
            for (int i = 0; i < CacheConstants.PERFORMANCE_TEST_ITERATIONS; i++)
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
            var checkTimes = new double[CacheConstants.PERFORMANCE_TEST_ITERATIONS];
            for (int i = 0; i < CacheConstants.PERFORMANCE_TEST_ITERATIONS; i++)
            {
                var key = $"{baseKey}_{i}";
                stopwatch.Restart();
                cache.HasCache(key);
                stopwatch.Stop();
                checkTimes[i] = stopwatch.Elapsed.TotalMilliseconds;
            }
            result.avgCheckTime = checkTimes.Average();
            
            // Cleanup test data for this implementation
            for (int i = 0; i < CacheConstants.PERFORMANCE_TEST_ITERATIONS; i++)
            {
                cache.ClearCache($"{baseKey}_{i}");
            }
            
            return result;
        }
        
        private static Texture2D CreateTestTexture()
        {
            var texture = new Texture2D(CacheConstants.TEST_TEXTURE_SIZE, CacheConstants.TEST_TEXTURE_SIZE, 
                CacheConstants.TEST_TEXTURE_FORMAT, false);
            var pixels = new Color[CacheConstants.TEST_TEXTURE_SIZE * CacheConstants.TEST_TEXTURE_SIZE];
            
            // Create test pattern (gradient + noise for realistic data)
            for (int y = 0; y < CacheConstants.TEST_TEXTURE_SIZE; y++)
            {
                for (int x = 0; x < CacheConstants.TEST_TEXTURE_SIZE; x++)
                {
                    var index = y * CacheConstants.TEST_TEXTURE_SIZE + x;
                    var noise = Mathf.PerlinNoise(x * 0.1f, y * 0.1f) * 0.2f;
                    pixels[index] = new Color(
                        (float)x / CacheConstants.TEST_TEXTURE_SIZE + noise,
                        (float)y / CacheConstants.TEST_TEXTURE_SIZE + noise,
                        0.5f + noise,
                        1.0f
                    );
                }
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
        
        private static void DisplayResults(PerformanceResult[] results, Texture2D testTexture)
        {
            Debug.Log("\n=== Performance Comparison ===");
            
            foreach (var result in results)
            {
                Debug.Log($"{result.implementationName.PadRight(15)} - " +
                         $"Save: {result.avgSaveTime.ToString($"F{CacheConstants.PERFORMANCE_DECIMAL_PLACES}")}ms, " +
                         $"Load: {result.avgLoadTime.ToString($"F{CacheConstants.PERFORMANCE_DECIMAL_PLACES}")}ms, " +
                         $"Check: {(result.avgCheckTime * 1000).ToString($"F{CacheConstants.PERFORMANCE_DECIMAL_PLACES + 3}")}μs");
            }
            
            // Memory usage estimation
            DisplayMemoryUsageComparison(testTexture);
            
            // Performance ranking
            DisplayPerformanceRanking(results);
        }
        
        private static void DisplaySingleResult(PerformanceResult result)
        {
            Debug.Log($"\n=== {result.implementationName} Results ===");
            Debug.Log($"Average Save Time: {result.avgSaveTime:F3}ms");
            Debug.Log($"Average Load Time: {result.avgLoadTime:F3}ms"); 
            Debug.Log($"Average Check Time: {result.avgCheckTime * 1000:F6}μs");
        }
        
        private static void DisplayMemoryUsageComparison(Texture2D testTexture)
        {
            Debug.Log("\n=== Memory Usage Estimation ===");
            
            var pngSize = testTexture.EncodeToPNG().Length;
            var editorPrefsMemory = (long)(pngSize * CacheConstants.BASE64_OVERHEAD_FACTOR);
            
            Debug.Log($"EditorPrefs     : ~{(editorPrefsMemory / CacheConstants.BYTES_TO_KB).ToString($"F{CacheConstants.MEMORY_DECIMAL_PLACES}")}KB (Base64 overhead included)");
            Debug.Log($"JsonFile        : ~{(pngSize / CacheConstants.BYTES_TO_KB).ToString($"F{CacheConstants.MEMORY_DECIMAL_PLACES}")}KB (JSON metadata overhead ~5%)");
            Debug.Log($"BinaryFile      : ~{(pngSize / CacheConstants.BYTES_TO_KB).ToString($"F{CacheConstants.MEMORY_DECIMAL_PLACES}")}KB (pure binary data)");
        }
        
        private static void DisplayPerformanceRanking(PerformanceResult[] results)
        {
            Debug.Log("\n=== Performance Ranking ===");
            
            var saveRanking = results.OrderBy(r => r.avgSaveTime).ToArray();
            var loadRanking = results.OrderBy(r => r.avgLoadTime).ToArray();
            var checkRanking = results.OrderBy(r => r.avgCheckTime).ToArray();
            
            Debug.Log("Save Speed  : " + string.Join(" > ", saveRanking.Select(r => r.implementationName)));
            Debug.Log("Load Speed  : " + string.Join(" > ", loadRanking.Select(r => r.implementationName)));  
            Debug.Log("Check Speed : " + string.Join(" > ", checkRanking.Select(r => r.implementationName)));
        }
        
        private static void CleanupTestData(string baseKey)
        {
            foreach (var implementation in testImplementations)
            {
                try
                {
                    for (int i = 0; i < CacheConstants.PERFORMANCE_TEST_ITERATIONS; i++)
                    {
                        implementation.ClearCache($"{baseKey}_{i}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Cleanup warning for {implementation.CacheTypeName}: {e.Message}");
                }
            }
        }
        
        private struct PerformanceResult
        {
            public string implementationName;
            public double avgSaveTime;
            public double avgLoadTime;
            public double avgCheckTime;
        }
    }
}