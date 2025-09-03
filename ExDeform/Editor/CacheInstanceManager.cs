using System;
using UnityEngine;
using ExDeform.Core.Interfaces;

namespace ExDeform.Editor
{
    /// <summary>
    /// Cache implementation instance manager
    /// キャッシュ実装インスタンス管理
    /// </summary>
    public static class CacheInstanceManager
    {
        private static readonly ICacheStorage[] testImplementations = {
            new EditorPrefsCache(),
            new JsonFileCache(),
            new BinaryFileCache()
        };

        /// <summary>
        /// Get all available cache implementations for testing
        /// テスト用の全利用可能キャッシュ実装を取得
        /// </summary>
        /// <returns>Array of cache implementations</returns>
        public static ICacheStorage[] GetAllTestImplementations()
        {
            return testImplementations;
        }

        /// <summary>
        /// Get specific cache implementation by index
        /// インデックスによる特定キャッシュ実装取得
        /// </summary>
        /// <param name="index">Implementation index</param>
        /// <returns>Cache implementation or null if index is invalid</returns>
        public static ICacheStorage GetImplementation(int index)
        {
            if (index >= 0 && index < testImplementations.Length)
            {
                return testImplementations[index];
            }
            return null;
        }

        /// <summary>
        /// Get implementation count
        /// 実装数を取得
        /// </summary>
        /// <returns>Number of available implementations</returns>
        public static int GetImplementationCount()
        {
            return testImplementations.Length;
        }

        /// <summary>
        /// Get implementation names for UI display
        /// UI表示用の実装名を取得
        /// </summary>
        /// <returns>Array of implementation names</returns>
        public static string[] GetImplementationNames()
        {
            var names = new string[testImplementations.Length];
            for (int i = 0; i < testImplementations.Length; i++)
            {
                names[i] = testImplementations[i].CacheTypeName;
            }
            return names;
        }

        /// <summary>
        /// Cleanup test data across all implementations
        /// 全実装のテストデータをクリーンアップ
        /// </summary>
        /// <param name="baseKey">Base key used for test data</param>
        public static void CleanupTestData(string baseKey)
        {
            foreach (var implementation in testImplementations)
            {
                try
                {
                    for (int i = 0; i < CacheConstants.TEST_ITERATIONS; i++)
                    {
                        implementation.ClearCache($"{baseKey}_{i}");
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"Cleanup warning for {implementation.CacheTypeName}: {e.Message}");
                }
            }
        }
    }
}