using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Deform.Masking.Editor
{
    /// <summary>
    /// Performance benchmark tests for caching mechanisms
    /// キャッシュ機構のパフォーマンステスト
    /// </summary>
    public static class CachingPerformanceTest
    {
        private const int TEST_ITERATIONS = 1000;
        private const int TEXTURE_SIZE = 128;
        
        [Serializable]
        public class CachedTextureData
        {
            public byte[] textureData;
            public int width;
            public int height;
            public string meshHash;
            public long timestamp;
        }
        
        /// <summary>
        /// EditorPrefs approach - stores texture as base64 string
        /// </summary>
        public static class EditorPrefsCache
        {
            public static void SaveTexture(string key, Texture2D texture)
            {
                var data = texture.EncodeToPNG();
                var base64 = Convert.ToBase64String(data);
                EditorPrefs.SetString($"UVCache_{key}", base64);
                EditorPrefs.SetString($"UVCache_{key}_meta", $"{texture.width}|{texture.height}|{DateTime.Now.Ticks}");
            }
            
            public static Texture2D LoadTexture(string key)
            {
                var base64 = EditorPrefs.GetString($"UVCache_{key}", "");
                if (string.IsNullOrEmpty(base64)) return null;
                
                var meta = EditorPrefs.GetString($"UVCache_{key}_meta", "");
                var parts = meta.Split('|');
                if (parts.Length < 2) return null;
                
                var data = Convert.FromBase64String(base64);
                var texture = new Texture2D(int.Parse(parts[0]), int.Parse(parts[1]));
                texture.LoadImage(data);
                return texture;
            }
            
            public static bool HasCache(string key)
            {
                return EditorPrefs.HasKey($"UVCache_{key}");
            }
            
            public static void ClearCache(string key)
            {
                EditorPrefs.DeleteKey($"UVCache_{key}");
                EditorPrefs.DeleteKey($"UVCache_{key}_meta");
            }
        }
        
        /// <summary>
        /// ScriptableObject approach - stores texture data in asset files
        /// </summary>
        public static class ScriptableObjectCache
        {
            private const string CACHE_FOLDER = "Library/UVIslandCache";
            
            static ScriptableObjectCache()
            {
                if (!Directory.Exists(CACHE_FOLDER))
                {
                    Directory.CreateDirectory(CACHE_FOLDER);
                }
            }
            
            public static void SaveTexture(string key, Texture2D texture)
            {
                var data = new CachedTextureData
                {
                    textureData = texture.EncodeToPNG(),
                    width = texture.width,
                    height = texture.height,
                    meshHash = key,
                    timestamp = DateTime.Now.Ticks
                };
                
                var json = JsonUtility.ToJson(data, false);
                var filePath = Path.Combine(CACHE_FOLDER, $"{key}.json");
                File.WriteAllText(filePath, json);
            }
            
            public static Texture2D LoadTexture(string key)
            {
                var filePath = Path.Combine(CACHE_FOLDER, $"{key}.json");
                if (!File.Exists(filePath)) return null;
                
                var json = File.ReadAllText(filePath);
                var data = JsonUtility.FromJson<CachedTextureData>(json);
                
                var texture = new Texture2D(data.width, data.height);
                texture.LoadImage(data.textureData);
                return texture;
            }
            
            public static bool HasCache(string key)
            {
                return File.Exists(Path.Combine(CACHE_FOLDER, $"{key}.json"));
            }
            
            public static void ClearCache(string key)
            {
                var filePath = Path.Combine(CACHE_FOLDER, $"{key}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }
        
        /// <summary>
        /// Binary file approach - direct binary storage
        /// </summary>
        public static class BinaryFileCache
        {
            private const string CACHE_FOLDER = "Library/UVIslandCacheBinary";
            
            static BinaryFileCache()
            {
                if (!Directory.Exists(CACHE_FOLDER))
                {
                    Directory.CreateDirectory(CACHE_FOLDER);
                }
            }
            
            public static void SaveTexture(string key, Texture2D texture)
            {
                var data = texture.EncodeToPNG();
                var filePath = Path.Combine(CACHE_FOLDER, $"{key}.png");
                File.WriteAllBytes(filePath, data);
                
                // Save metadata separately
                var metaPath = Path.Combine(CACHE_FOLDER, $"{key}.meta");
                var metaData = $"{texture.width}|{texture.height}|{DateTime.Now.Ticks}";
                File.WriteAllText(metaPath, metaData);
            }
            
            public static Texture2D LoadTexture(string key)
            {
                var filePath = Path.Combine(CACHE_FOLDER, $"{key}.png");
                if (!File.Exists(filePath)) return null;
                
                var data = File.ReadAllBytes(filePath);
                var texture = new Texture2D(2, 2);
                texture.LoadImage(data);
                return texture;
            }
            
            public static bool HasCache(string key)
            {
                return File.Exists(Path.Combine(CACHE_FOLDER, $"{key}.png"));
            }
            
            public static void ClearCache(string key)
            {
                var filePath = Path.Combine(CACHE_FOLDER, $"{key}.png");
                var metaPath = Path.Combine(CACHE_FOLDER, $"{key}.meta");
                if (File.Exists(filePath)) File.Delete(filePath);
                if (File.Exists(metaPath)) File.Delete(metaPath);
            }
        }
        
        [MenuItem("Tools/UV Island Cache/Run Performance Tests")]
        public static void RunPerformanceTests()
        {
            UnityEngine.Debug.Log("=== UV Island Cache Performance Tests ===");
            
            // Create test texture
            var testTexture = CreateTestTexture();
            var testKey = "performance_test_key";
            
            // Test EditorPrefs
            var editorPrefsResults = TestCachingMethod("EditorPrefs", testKey, testTexture,
                EditorPrefsCache.SaveTexture, EditorPrefsCache.LoadTexture, 
                EditorPrefsCache.HasCache, EditorPrefsCache.ClearCache);
                
            // Test ScriptableObject (JSON)
            var scriptableObjectResults = TestCachingMethod("ScriptableObject", testKey, testTexture,
                ScriptableObjectCache.SaveTexture, ScriptableObjectCache.LoadTexture,
                ScriptableObjectCache.HasCache, ScriptableObjectCache.ClearCache);
                
            // Test Binary File
            var binaryFileResults = TestCachingMethod("BinaryFile", testKey, testTexture,
                BinaryFileCache.SaveTexture, BinaryFileCache.LoadTexture,
                BinaryFileCache.HasCache, BinaryFileCache.ClearCache);
            
            // Results comparison
            UnityEngine.Debug.Log("\n=== Performance Comparison ===");
            UnityEngine.Debug.Log($"EditorPrefs    - Save: {editorPrefsResults.avgSaveTime:F3}ms, Load: {editorPrefsResults.avgLoadTime:F3}ms, Check: {editorPrefsResults.avgCheckTime:F6}ms");
            UnityEngine.Debug.Log($"ScriptableObj  - Save: {scriptableObjectResults.avgSaveTime:F3}ms, Load: {scriptableObjectResults.avgLoadTime:F3}ms, Check: {scriptableObjectResults.avgCheckTime:F6}ms");
            UnityEngine.Debug.Log($"BinaryFile     - Save: {binaryFileResults.avgSaveTime:F3}ms, Load: {binaryFileResults.avgLoadTime:F3}ms, Check: {binaryFileResults.avgCheckTime:F6}ms");
            
            // Memory usage estimation
            var editorPrefsMemory = EstimateEditorPrefsMemory(testTexture);
            var fileMemory = EstimateFileMemory(testTexture);
            
            UnityEngine.Debug.Log($"\n=== Memory Usage Estimation ===");
            UnityEngine.Debug.Log($"EditorPrefs: ~{editorPrefsMemory / 1024f:F1}KB (base64 overhead)");
            UnityEngine.Debug.Log($"File-based: ~{fileMemory / 1024f:F1}KB (binary data)");
            
            UnityEngine.Object.DestroyImmediate(testTexture);
        }
        
        private static (double avgSaveTime, double avgLoadTime, double avgCheckTime) TestCachingMethod(
            string methodName, string testKey, Texture2D testTexture,
            System.Action<string, Texture2D> saveMethod,
            System.Func<string, Texture2D> loadMethod,
            System.Func<string, bool> hasMethod,
            System.Action<string> clearMethod)
        {
            UnityEngine.Debug.Log($"\n--- Testing {methodName} ---");
            
            var stopwatch = new Stopwatch();
            
            // Test Save Performance
            var saveTimes = new double[TEST_ITERATIONS];
            for (int i = 0; i < TEST_ITERATIONS; i++)
            {
                var key = $"{testKey}_{i}";
                stopwatch.Restart();
                saveMethod(key, testTexture);
                stopwatch.Stop();
                saveTimes[i] = stopwatch.Elapsed.TotalMilliseconds;
            }
            
            // Test Load Performance
            var loadTimes = new double[TEST_ITERATIONS];
            for (int i = 0; i < TEST_ITERATIONS; i++)
            {
                var key = $"{testKey}_{i}";
                stopwatch.Restart();
                var loaded = loadMethod(key);
                stopwatch.Stop();
                loadTimes[i] = stopwatch.Elapsed.TotalMilliseconds;
                if (loaded != null) UnityEngine.Object.DestroyImmediate(loaded);
            }
            
            // Test Check Performance
            var checkTimes = new double[TEST_ITERATIONS];
            for (int i = 0; i < TEST_ITERATIONS; i++)
            {
                var key = $"{testKey}_{i}";
                stopwatch.Restart();
                hasMethod(key);
                stopwatch.Stop();
                checkTimes[i] = stopwatch.Elapsed.TotalMilliseconds;
            }
            
            // Cleanup
            for (int i = 0; i < TEST_ITERATIONS; i++)
            {
                clearMethod($"{testKey}_{i}");
            }
            
            var avgSave = Array.ConvertAll(saveTimes, x => x).Average();
            var avgLoad = Array.ConvertAll(loadTimes, x => x).Average();
            var avgCheck = Array.ConvertAll(checkTimes, x => x).Average();
            
            return (avgSave, avgLoad, avgCheck);
        }
        
        private static Texture2D CreateTestTexture()
        {
            var texture = new Texture2D(TEXTURE_SIZE, TEXTURE_SIZE, TextureFormat.RGBA32, false);
            var pixels = new Color[TEXTURE_SIZE * TEXTURE_SIZE];
            
            // Create test pattern
            for (int y = 0; y < TEXTURE_SIZE; y++)
            {
                for (int x = 0; x < TEXTURE_SIZE; x++)
                {
                    var index = y * TEXTURE_SIZE + x;
                    pixels[index] = new Color(
                        (float)x / TEXTURE_SIZE,
                        (float)y / TEXTURE_SIZE,
                        0.5f,
                        1.0f
                    );
                }
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
        
        private static long EstimateEditorPrefsMemory(Texture2D texture)
        {
            var pngData = texture.EncodeToPNG();
            var base64Data = Convert.ToBase64String(pngData);
            return base64Data.Length * sizeof(char); // Base64 string overhead
        }
        
        private static long EstimateFileMemory(Texture2D texture)
        {
            var pngData = texture.EncodeToPNG();
            return pngData.Length; // Direct binary size
        }
    }
}