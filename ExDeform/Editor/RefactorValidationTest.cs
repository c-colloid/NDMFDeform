using System;
using UnityEditor;
using UnityEngine;
using ExDeform.Runtime.Core.Interfaces;

namespace ExDeform.Editor
{
    /// <summary>
    /// Validation test for refactored cache components
    /// リファクタリング済みキャッシュコンポーネントの検証テスト
    /// </summary>
    public static class RefactorValidationTest
    {
        [MenuItem("Tools/UV Island Cache/Validate Refactoring")]
        public static void ValidateRefactoring()
        {
            Debug.Log("=== Refactoring Validation Test ===");
            
            var implementations = new ICacheStorage[]
            {
                new EditorPrefsCache(),
                new JsonFileCache(), 
                new BinaryFileCache()
            };
            
            var testTexture = CreateTestTexture();
            var testKey = "validation_test_key";
            var allTestsPassed = true;
            
            foreach (var cache in implementations)
            {
                try
                {
                    Debug.Log($"Testing {cache.CacheTypeName} implementation...");
                    
                    // Test save
                    cache.SaveTexture(testKey, testTexture);
                    if (!cache.HasCache(testKey))
                    {
                        Debug.LogError($"{cache.CacheTypeName}: Save/HasCache test failed");
                        allTestsPassed = false;
                        continue;
                    }
                    
                    // Test load
                    var loadedTexture = cache.LoadTexture(testKey);
                    if (loadedTexture == null)
                    {
                        Debug.LogError($"{cache.CacheTypeName}: Load test failed");
                        allTestsPassed = false;
                        cache.ClearCache(testKey);
                        continue;
                    }
                    
                    // Verify dimensions
                    if (loadedTexture.width != testTexture.width || loadedTexture.height != testTexture.height)
                    {
                        Debug.LogError($"{cache.CacheTypeName}: Dimension mismatch - Expected {testTexture.width}x{testTexture.height}, Got {loadedTexture.width}x{loadedTexture.height}");
                        allTestsPassed = false;
                    }
                    
                    // Cleanup
                    UnityEngine.Object.DestroyImmediate(loadedTexture);
                    cache.ClearCache(testKey);
                    
                    if (cache.HasCache(testKey))
                    {
                        Debug.LogError($"{cache.CacheTypeName}: Clear test failed");
                        allTestsPassed = false;
                    }
                    else
                    {
                        Debug.Log($"{cache.CacheTypeName}: All tests passed ✓");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"{cache.CacheTypeName}: Exception occurred - {e.Message}");
                    allTestsPassed = false;
                }
            }
            
            UnityEngine.Object.DestroyImmediate(testTexture);
            
            if (allTestsPassed)
            {
                Debug.Log("✓ Refactoring validation PASSED - All implementations work correctly");
            }
            else
            {
                Debug.LogError("✗ Refactoring validation FAILED - Some implementations have issues");
            }
        }
        
        private static Texture2D CreateTestTexture()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = new Color[64 * 64];
            
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(
                    UnityEngine.Random.value,
                    UnityEngine.Random.value, 
                    UnityEngine.Random.value,
                    1.0f);
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}