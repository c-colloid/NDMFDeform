using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace ExDeform.Editor
{
    /// <summary>
    /// Optimal UV texture cache implementation using binary files for maximum performance
    /// バイナリファイルを使用した最高パフォーマンスのUVテクスチャキャッシュ
    /// </summary>
    public static class OptimalUVCache
    {
        private const string CACHE_FOLDER = "Library/UVIslandCacheOptimal";
        private const string CACHE_INDEX_FILE = "Library/UVIslandCacheOptimal/cache_index.dat";
        private const int CURRENT_VERSION = 1;
        
        // In-memory cache for ultra-fast access
        private static Dictionary<string, CacheEntry> memoryCache = new Dictionary<string, CacheEntry>();
        private static Dictionary<string, DateTime> cacheAccessTimes = new Dictionary<string, DateTime>();
        private const int MAX_MEMORY_CACHE_SIZE = 50; // Limit memory usage
        
        [System.Serializable]
        private struct CacheEntry
        {
            public string filePath;
            public int width;
            public int height;
            public long timestamp;
            public int version;
        }
        
        static OptimalUVCache()
        {
            InitializeCache();
        }
        
        private static void InitializeCache()
        {
            if (!Directory.Exists(CACHE_FOLDER))
            {
                Directory.CreateDirectory(CACHE_FOLDER);
            }
            
            LoadCacheIndex();
            
            // Clean up old cache files on startup
            CleanupOldCacheFiles();
        }
        
        /// <summary>
        /// Save texture to cache with maximum performance
        /// 最高パフォーマンスでテクスチャをキャッシュに保存
        /// </summary>
        public static void SaveTexture(string key, Texture2D texture)
        {
            if (string.IsNullOrEmpty(key) || texture == null) return;
            
            try
            {
                var filePath = GetCacheFilePath(key);
                var pngData = texture.EncodeToPNG();
                
                // Ultra-fast binary write
                File.WriteAllBytes(filePath, pngData);
                
                // Update cache entry
                var entry = new CacheEntry
                {
                    filePath = filePath,
                    width = texture.width,
                    height = texture.height,
                    timestamp = DateTime.Now.Ticks,
                    version = CURRENT_VERSION
                };
                
                // Update memory cache
                memoryCache[key] = entry;
                cacheAccessTimes[key] = DateTime.Now;
                
                // Maintain memory cache size limit
                TrimMemoryCache();
                
                // Update persistent index
                SaveCacheIndex();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCache] Failed to save texture cache: {e.Message}");
            }
        }
        
        /// <summary>
        /// Load texture from cache with maximum performance
        /// 最高パフォーマンスでキャッシュからテクスチャを読み込み
        /// </summary>
        public static Texture2D LoadTexture(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            
            try
            {
                // Check memory cache first (fastest)
                if (memoryCache.TryGetValue(key, out var entry))
                {
                    cacheAccessTimes[key] = DateTime.Now; // Update access time
                    return LoadTextureFromFile(entry.filePath, entry.width, entry.height);
                }
                
                // Check if file exists
                var filePath = GetCacheFilePath(key);
                if (!File.Exists(filePath)) return null;
                
                // Load texture and update memory cache
                var texture = LoadTextureFromFile(filePath);
                if (texture != null)
                {
                    // Add to memory cache for future fast access
                    var newEntry = new CacheEntry
                    {
                        filePath = filePath,
                        width = texture.width,
                        height = texture.height,
                        timestamp = File.GetLastWriteTime(filePath).Ticks,
                        version = CURRENT_VERSION
                    };
                    
                    memoryCache[key] = newEntry;
                    cacheAccessTimes[key] = DateTime.Now;
                    TrimMemoryCache();
                }
                
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCache] Failed to load texture cache: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Ultra-fast cache existence check
        /// 超高速キャッシュ存在確認
        /// </summary>
        public static bool HasCache(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            
            // Memory cache check (fastest possible)
            if (memoryCache.ContainsKey(key))
            {
                return true;
            }
            
            // File system check
            return File.Exists(GetCacheFilePath(key));
        }
        
        /// <summary>
        /// Remove specific cache entry
        /// 特定のキャッシュエントリを削除
        /// </summary>
        public static void ClearCache(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            
            try
            {
                // Remove from memory cache
                memoryCache.Remove(key);
                cacheAccessTimes.Remove(key);
                
                // Remove file
                var filePath = GetCacheFilePath(key);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                
                SaveCacheIndex();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCache] Failed to clear cache: {e.Message}");
            }
        }
        
        /// <summary>
        /// Clear all cached data
        /// すべてのキャッシュデータをクリア
        /// </summary>
        public static void ClearAllCache()
        {
            try
            {
                memoryCache.Clear();
                cacheAccessTimes.Clear();
                
                if (Directory.Exists(CACHE_FOLDER))
                {
                    Directory.Delete(CACHE_FOLDER, true);
                    Directory.CreateDirectory(CACHE_FOLDER);
                }
                
                SaveCacheIndex();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCache] Failed to clear all cache: {e.Message}");
            }
        }
        
        /// <summary>
        /// Get cache statistics for debugging
        /// デバッグ用キャッシュ統計情報取得
        /// </summary>
        public static (int memoryCacheCount, int fileCacheCount, long totalSizeBytes) GetCacheStats()
        {
            var memoryCacheCount = memoryCache.Count;
            var fileCacheCount = 0;
            long totalSize = 0;
            
            if (Directory.Exists(CACHE_FOLDER))
            {
                var files = Directory.GetFiles(CACHE_FOLDER, "*.png");
                fileCacheCount = files.Length;
                
                foreach (var file in files)
                {
                    totalSize += new FileInfo(file).Length;
                }
            }
            
            return (memoryCacheCount, fileCacheCount, totalSize);
        }
        
        #region Private Helper Methods
        
        private static string GetCacheFilePath(string key)
        {
            // Use hash to avoid filesystem limitations
            var hash = key.GetHashCode().ToString("X8");
            return Path.Combine(CACHE_FOLDER, $"uv_{hash}.png");
        }
        
        private static Texture2D LoadTextureFromFile(string filePath, int width = 0, int height = 0)
        {
            var data = File.ReadAllBytes(filePath);
            
            // Pre-allocate texture with known dimensions for better performance
            var texture = width > 0 && height > 0 ? 
                new Texture2D(width, height, TextureFormat.RGBA32, false) :
                new Texture2D(2, 2, TextureFormat.RGBA32, false);
                
            texture.LoadImage(data);
            return texture;
        }
        
        private static void TrimMemoryCache()
        {
            if (memoryCache.Count <= MAX_MEMORY_CACHE_SIZE) return;
            
            // Remove least recently used entries
            var sortedEntries = new List<KeyValuePair<string, DateTime>>();
            foreach (var kvp in cacheAccessTimes)
            {
                sortedEntries.Add(kvp);
            }
            
            sortedEntries.Sort((a, b) => a.Value.CompareTo(b.Value));
            
            // Remove oldest entries
            var toRemove = sortedEntries.Count - MAX_MEMORY_CACHE_SIZE;
            for (int i = 0; i < toRemove; i++)
            {
                var key = sortedEntries[i].Key;
                memoryCache.Remove(key);
                cacheAccessTimes.Remove(key);
            }
        }
        
        private static void LoadCacheIndex()
        {
            try
            {
                if (!File.Exists(CACHE_INDEX_FILE)) return;
                
                var json = File.ReadAllText(CACHE_INDEX_FILE);
                var entries = JsonUtility.FromJson<CacheIndexData>(json);
                
                if (entries != null && entries.version == CURRENT_VERSION)
                {
                    foreach (var entry in entries.entries)
                    {
                        if (File.Exists(entry.filePath))
                        {
                            memoryCache[entry.filePath] = entry;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCache] Failed to load cache index: {e.Message}");
            }
        }
        
        private static void SaveCacheIndex()
        {
            try
            {
                var indexData = new CacheIndexData
                {
                    version = CURRENT_VERSION,
                    entries = new List<CacheEntry>()
                };
                
                foreach (var entry in memoryCache.Values)
                {
                    indexData.entries.Add(entry);
                }
                
                var json = JsonUtility.ToJson(indexData, false);
                File.WriteAllText(CACHE_INDEX_FILE, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCache] Failed to save cache index: {e.Message}");
            }
        }
        
        private static void CleanupOldCacheFiles()
        {
            try
            {
                if (!Directory.Exists(CACHE_FOLDER)) return;
                
                var files = Directory.GetFiles(CACHE_FOLDER, "*.png");
                var cutoffTime = DateTime.Now.AddDays(-7); // Remove files older than 7 days
                
                foreach (var file in files)
                {
                    if (File.GetLastAccessTime(file) < cutoffTime)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OptimalUVCache] Failed to cleanup old cache files: {e.Message}");
            }
        }
        
        [System.Serializable]
        private class CacheIndexData
        {
            public int version;
            public List<CacheEntry> entries = new List<CacheEntry>();
        }
        
        #endregion
        
        #region Editor Integration
        
        [MenuItem("Tools/UV Island Cache/Clear All Cache")]
        private static void MenuClearAllCache()
        {
            ClearAllCache();
            Debug.Log("[OptimalUVCache] All cache cleared.");
        }
        
        [MenuItem("Tools/UV Island Cache/Show Cache Stats")]
        private static void MenuShowCacheStats()
        {
            var stats = GetCacheStats();
            Debug.Log($"[OptimalUVCache] Memory Cache: {stats.memoryCacheCount}, File Cache: {stats.fileCacheCount}, Total Size: {stats.totalSizeBytes / 1024f:F1}KB");
        }
        
        #endregion
    }
}