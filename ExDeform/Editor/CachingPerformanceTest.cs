using UnityEditor;
using UnityEngine;

namespace ExDeform.Editor
{
    /// <summary>
    /// Separated responsibility caching performance test class
    /// 責任分離されたキャッシュパフォーマンステストクラス
    /// </summary>
    public static class CachingPerformanceTest
    {
        [MenuItem("Tools/UV Island Cache/Run All Performance Tests")]
        public static void RunPerformanceTests()
        {
            if (!CacheTestUI.ShowRunAllTestsDialog())
            {
                return;
            }

            Debug.Log("=== UV Island Cache Performance Tests ===");
            
            var testTexture = TestResourceManager.CreateTestTexture();
            var testKey = TestResourceManager.GenerateTestKey("performance_test");
            var implementations = CacheInstanceManager.GetAllTestImplementations();
            var results = new PerformanceResult[implementations.Length];
            
            // Test all implementations
            for (int i = 0; i < implementations.Length; i++)
            {
                results[i] = PerformanceMeasurement.MeasureCachePerformance(
                    implementations[i], testKey, testTexture);
            }
            
            // Display comprehensive results
            PerformanceMeasurement.DisplayResults(results, testTexture);
            
            // Cleanup
            CacheInstanceManager.CleanupTestData(testKey);
            TestResourceManager.DisposeTestTexture(testTexture);
        }
        
        [MenuItem("Tools/UV Island Cache/Run Single Implementation Test")]
        public static void RunSingleImplementationTest()
        {
            var choice = CacheTestUI.ShowImplementationSelectionDialog();
            var implementation = CacheInstanceManager.GetImplementation(choice);
            
            if (implementation == null)
            {
                return;
            }

            var testTexture = TestResourceManager.CreateTestTexture();
            var testKey = TestResourceManager.GenerateTestKey("single_test");
            
            Debug.Log($"=== Testing {implementation.CacheTypeName} Only ===");
            var result = PerformanceMeasurement.MeasureCachePerformance(
                implementation, testKey, testTexture);
            PerformanceMeasurement.DisplaySingleResult(result);
            
            implementation.ClearCache(testKey);
            TestResourceManager.DisposeTestTexture(testTexture);
        }

        [MenuItem("Tools/UV Island Cache/Run Memory Usage Test")]
        public static void RunMemoryUsageTest()
        {
            Debug.Log("=== Memory Usage Comparison Test ===");
            
            var testTextures = TestResourceManager.CreateMultipleTestTextures(5);
            var implementations = CacheInstanceManager.GetAllTestImplementations();
            
            foreach (var implementation in implementations)
            {
                Debug.Log($"\n--- {implementation.CacheTypeName} Memory Test ---");
                
                var baseKey = TestResourceManager.GenerateTestKey($"memory_{implementation.CacheTypeName}");
                
                // Save multiple textures
                for (int i = 0; i < testTextures.Length; i++)
                {
                    implementation.SaveTexture($"{baseKey}_{i}", testTextures[i]);
                }
                
                // Check statistics if available
                var stats = implementation.GetStatistics();
                Debug.Log($"Statistics: {stats}");
                
                // Cleanup
                for (int i = 0; i < testTextures.Length; i++)
                {
                    implementation.ClearCache($"{baseKey}_{i}");
                }
            }
            
            TestResourceManager.DisposeMultipleTestTextures(testTextures);
        }

        [MenuItem("Tools/UV Island Cache/Clear All Test Caches")]
        public static void ClearAllTestCaches()
        {
            if (EditorUtility.DisplayDialog("Clear All Test Caches", 
                "This will clear all test cache data from all implementations. Continue?",
                "Clear", "Cancel"))
            {
                var implementations = CacheInstanceManager.GetAllTestImplementations();
                
                foreach (var implementation in implementations)
                {
                    try
                    {
                        implementation.ClearAllCache();
                        Debug.Log($"Cleared all cache for {implementation.CacheTypeName}");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Failed to clear cache for {implementation.CacheTypeName}: {e.Message}");
                    }
                }
                
                Debug.Log("Test cache cleanup completed.");
            }
        }
    }
}