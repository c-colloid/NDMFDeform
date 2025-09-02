using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEditor;

namespace Deform.Masking.Editor
{
    /// <summary>
    /// Production-ready UV texture cache with robust error handling and performance optimization
    /// 本格的なエラー処理とパフォーマンス最適化を備えたUVテクスチャキャッシュ
    /// </summary>
    public static class RobustUVCache
    {
        #region Configuration
        
        private const string CACHE_FOLDER = "Library/UVIslandCache";
        private const string CACHE_INDEX_FILE = "Library/UVIslandCache/.cache_index";
        private const string LOG_PREFIX = "[RobustUVCache]";
        private const int CURRENT_VERSION = 1;
        
        // Performance tuning parameters
        private const int MAX_MEMORY_CACHE_SIZE = 100;
        private const int MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024; // 10MB
        private const int CACHE_CLEANUP_DAYS = 7;
        private const int MAX_CONCURRENT_OPERATIONS = 4;
        
        // Retry configuration
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 100;
        
        #endregion
        
        #region Thread-Safe Data Structures
        
        private static readonly object s_lock = new object();
        private static readonly Dictionary<string, CacheEntry> s_memoryCache = new Dictionary<string, CacheEntry>();
        private static readonly Dictionary<string, DateTime> s_accessTimes = new Dictionary<string, DateTime>();
        private static readonly SemaphoreSlim s_operationSemaphore = new SemaphoreSlim(MAX_CONCURRENT_OPERATIONS);
        
        // Performance monitoring
        private static readonly Dictionary<string, PerformanceMetrics> s_performanceData = new Dictionary<string, PerformanceMetrics>();
        private static volatile bool s_isInitialized = false;
        
        #endregion
        
        #region Data Structures
        
        [System.Serializable]
        private struct CacheEntry
        {
            public string key;
            public string filePath;
            public int width;
            public int height;
            public long timestamp;
            public int version;
            public long fileSizeBytes;
            public string checksum; // Data integrity check
            
            public bool IsValid => !string.IsNullOrEmpty(key) && 
                                  !string.IsNullOrEmpty(filePath) && 
                                  width > 0 && 
                                  height > 0 && 
                                  version == CURRENT_VERSION;
        }
        
        [System.Serializable]
        private struct PerformanceMetrics
        {
            public int hitCount;
            public int missCount;
            public float totalReadTime;
            public float totalWriteTime;
            public DateTime lastAccess;
            
            public float HitRate => hitCount + missCount > 0 ? (float)hitCount / (hitCount + missCount) : 0f;
        }
        
        [System.Serializable]
        private class CacheIndex
        {
            public int version = CURRENT_VERSION;
            public List<CacheEntry> entries = new List<CacheEntry>();
            public long totalSizeBytes = 0;
            public DateTime lastCleanup = DateTime.MinValue;
        }
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Lazy initialization - called only when cache operations are needed
        /// 遅延初期化 - キャッシュ操作が必要な時のみ呼び出し
        /// </summary>
        private static void EnsureInitialized()
        {
            if (s_isInitialized) return;
            
            try
            {
                InitializeCacheDirectory();
                LoadCacheIndex();
                SchedulePeriodicCleanup();
                s_isInitialized = true;
                
                LogInfo("Cache system initialized on demand");
            }
            catch (Exception e)
            {
                LogError($"Failed to initialize cache system: {e.Message}");
            }
        }
        
        private static void InitializeCacheDirectory()
        {
            try
            {
                if (!Directory.Exists(CACHE_FOLDER))
                {
                    Directory.CreateDirectory(CACHE_FOLDER);
                    LogInfo($"Created cache directory: {CACHE_FOLDER}");
                }
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Cannot create cache directory: {e.Message}", e);
            }
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Save texture to cache with comprehensive error handling
        /// 包括的なエラー処理でテクスチャをキャッシュに保存
        /// </summary>
        public static bool SaveTexture(string key, Texture2D texture)
        {
            // Ensure cache system is initialized
            EnsureInitialized();
            
            // Null safety checks
            if (string.IsNullOrWhiteSpace(key))
            {
                LogWarning("SaveTexture called with null or empty key");
                return false;
            }
            
            if (texture == null)
            {
                LogWarning($"SaveTexture called with null texture for key: {key}");
                return false;
            }
            
            if (texture.width <= 0 || texture.height <= 0)
            {
                LogWarning($"SaveTexture called with invalid texture dimensions: {texture.width}x{texture.height}");
                return false;
            }
            
            return ExecuteWithRetry(() => SaveTextureInternal(key, texture), $"SaveTexture({key})");
        }
        
        /// <summary>
        /// Load texture from cache with null safety
        /// Null安全性を備えたキャッシュからのテクスチャ読み込み
        /// </summary>
        public static Texture2D LoadTexture(string key)
        {
            // Ensure cache system is initialized
            EnsureInitialized();
            
            if (string.IsNullOrWhiteSpace(key))
            {
                LogWarning("LoadTexture called with null or empty key");
                return null;
            }
            
            var result = ExecuteWithRetry(() => LoadTextureInternal(key), $"LoadTexture({key})");
            return result.success ? result.result : null;
        }
        
        /// <summary>
        /// Fast cache existence check with performance tracking
        /// パフォーマンス追跡付きの高速キャッシュ存在確認
        /// </summary>
        public static bool HasCache(string key)
        {
            // Ensure cache system is initialized
            EnsureInitialized();
            
            if (string.IsNullOrWhiteSpace(key)) return false;
            
            var startTime = DateTime.UtcNow;
            bool result = false;
            
            try
            {
                lock (s_lock)
                {
                    // Memory cache check (fastest)
                    if (s_memoryCache.TryGetValue(key, out var entry) && entry.IsValid)
                    {
                        UpdateAccessTime(key);
                        result = File.Exists(entry.filePath);
                        UpdatePerformanceMetrics(key, result, startTime, isRead: true);
                        return result;
                    }
                }
                
                // File system check
                var filePath = GetCacheFilePath(key);
                result = File.Exists(filePath);
                UpdatePerformanceMetrics(key, result, startTime, isRead: true);
                
                return result;
            }
            catch (Exception e)
            {
                LogError($"HasCache failed for key '{key}': {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Remove cache entry with cleanup
        /// クリーンアップ付きキャッシュエントリ削除
        /// </summary>
        public static bool ClearCache(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            
            return ExecuteWithRetry(() => ClearCacheInternal(key), $"ClearCache({key})");
        }
        
        /// <summary>
        /// Get comprehensive cache statistics
        /// 包括的なキャッシュ統計情報取得
        /// </summary>
        public static CacheStatistics GetCacheStatistics()
        {
            // Ensure cache system is initialized
            EnsureInitialized();
            
            lock (s_lock)
            {
                var stats = new CacheStatistics();
                
                try
                {
                    stats.memoryCacheCount = s_memoryCache.Count;
                    stats.totalHitCount = 0;
                    stats.totalMissCount = 0;
                    stats.totalReadTime = 0f;
                    stats.totalWriteTime = 0f;
                    
                    foreach (var metrics in s_performanceData.Values)
                    {
                        stats.totalHitCount += metrics.hitCount;
                        stats.totalMissCount += metrics.missCount;
                        stats.totalReadTime += metrics.totalReadTime;
                        stats.totalWriteTime += metrics.totalWriteTime;
                    }
                    
                    if (Directory.Exists(CACHE_FOLDER))
                    {
                        var files = Directory.GetFiles(CACHE_FOLDER, "*.png", SearchOption.TopDirectoryOnly);
                        stats.fileCacheCount = files.Length;
                        
                        foreach (var file in files)
                        {
                            try
                            {
                                stats.totalSizeBytes += new FileInfo(file).Length;
                            }
                            catch
                            {
                                // Skip inaccessible files
                            }
                        }
                    }
                    
                    stats.overallHitRate = stats.totalHitCount + stats.totalMissCount > 0 ? 
                        (float)stats.totalHitCount / (stats.totalHitCount + stats.totalMissCount) : 0f;
                    
                    stats.averageReadTime = stats.totalHitCount > 0 ? stats.totalReadTime / stats.totalHitCount : 0f;
                    stats.averageWriteTime = stats.totalHitCount > 0 ? stats.totalWriteTime / stats.totalHitCount : 0f;
                }
                catch (Exception e)
                {
                    LogError($"Failed to gather cache statistics: {e.Message}");
                }
                
                return stats;
            }
        }
        
        /// <summary>
        /// Manual cache cleanup with progress reporting
        /// 進捗報告付きの手動キャッシュクリーンアップ
        /// </summary>
        public static void CleanupCache(bool force = false)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                int removedFiles = 0;
                long reclaimedBytes = 0;
                
                LogInfo("Starting cache cleanup...");
                
                if (Directory.Exists(CACHE_FOLDER))
                {
                    var files = Directory.GetFiles(CACHE_FOLDER, "*.png", SearchOption.TopDirectoryOnly);
                    var cutoffTime = DateTime.UtcNow.AddDays(-CACHE_CLEANUP_DAYS);
                    
                    foreach (var file in files)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            if (force || fileInfo.LastAccessTime < cutoffTime)
                            {
                                reclaimedBytes += fileInfo.Length;
                                File.Delete(file);
                                removedFiles++;
                            }
                        }
                        catch (Exception e)
                        {
                            LogWarning($"Failed to delete cache file {file}: {e.Message}");
                        }
                    }
                }
                
                // Clean memory cache
                lock (s_lock)
                {
                    var keysToRemove = new List<string>();
                    foreach (var kvp in s_memoryCache)
                    {
                        if (force || !File.Exists(kvp.Value.filePath))
                        {
                            keysToRemove.Add(kvp.Key);
                        }
                    }
                    
                    foreach (var key in keysToRemove)
                    {
                        s_memoryCache.Remove(key);
                        s_accessTimes.Remove(key);
                        s_performanceData.Remove(key);
                    }
                }
                
                SaveCacheIndex();
                
                var duration = DateTime.UtcNow - startTime;
                LogInfo($"Cache cleanup completed: {removedFiles} files removed, " +
                       $"{reclaimedBytes / 1024f:F1}KB reclaimed in {duration.TotalMilliseconds:F0}ms");
            }
            catch (Exception e)
            {
                LogError($"Cache cleanup failed: {e.Message}");
            }
        }
        
        #endregion
        
        #region Internal Implementation
        
        private static bool SaveTextureInternal(string key, Texture2D texture)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                // Wait for operation slot
                if (!s_operationSemaphore.Wait(5000))
                {
                    LogWarning($"SaveTexture timed out waiting for operation slot: {key}");
                    return false;
                }
                
                try
                {
                    var pngData = texture.EncodeToPNG();
                    if (pngData == null || pngData.Length == 0)
                    {
                        LogError($"Failed to encode texture to PNG: {key}");
                        return false;
                    }
                    
                    if (pngData.Length > MAX_FILE_SIZE_BYTES)
                    {
                        LogWarning($"Texture too large for cache ({pngData.Length} bytes): {key}");
                        return false;
                    }
                    
                    var filePath = GetCacheFilePath(key);
                    var tempPath = filePath + ".tmp";
                    
                    // Write to temporary file first (atomic operation)
                    File.WriteAllBytes(tempPath, pngData);
                    
                    // Move to final location
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                    File.Move(tempPath, filePath);
                    
                    // Calculate checksum for integrity
                    var checksum = CalculateChecksum(pngData);
                    
                    // Update cache entry
                    var entry = new CacheEntry
                    {
                        key = key,
                        filePath = filePath,
                        width = texture.width,
                        height = texture.height,
                        timestamp = DateTime.UtcNow.Ticks,
                        version = CURRENT_VERSION,
                        fileSizeBytes = pngData.Length,
                        checksum = checksum
                    };
                    
                    lock (s_lock)
                    {
                        s_memoryCache[key] = entry;
                        UpdateAccessTime(key);
                        TrimMemoryCache();
                    }
                    
                    SaveCacheIndex();
                    UpdatePerformanceMetrics(key, true, startTime, isRead: false);
                    
                    return true;
                }
                finally
                {
                    s_operationSemaphore.Release();
                }
            }
            catch (Exception e)
            {
                LogError($"SaveTextureInternal failed for key '{key}': {e.Message}");
                UpdatePerformanceMetrics(key, false, startTime, isRead: false);
                return false;
            }
        }
        
        private static (bool success, Texture2D result) LoadTextureInternal(string key)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                // Check memory cache first
                lock (s_lock)
                {
                    if (s_memoryCache.TryGetValue(key, out var entry) && entry.IsValid)
                    {
                        UpdateAccessTime(key);
                        
                        if (File.Exists(entry.filePath))
                        {
                            try
                            {
                                var texture = LoadTextureFromFile(entry.filePath, entry.width, entry.height, entry.checksum);
                                if (texture != null)
                                {
                                    UpdatePerformanceMetrics(key, true, startTime, isRead: true);
                                    return (true, texture);
                                }
                            }
                            catch (Exception e)
                            {
                                LogWarning($"Failed to load texture from cached path '{entry.filePath}': {e.Message}");
                                // Remove invalid entry
                                s_memoryCache.Remove(key);
                                s_accessTimes.Remove(key);
                            }
                        }
                        else
                        {
                            // File doesn't exist, remove from memory cache
                            s_memoryCache.Remove(key);
                            s_accessTimes.Remove(key);
                        }
                    }
                }
                
                // Try direct file access
                var filePath = GetCacheFilePath(key);
                if (File.Exists(filePath))
                {
                    try
                    {
                        var texture = LoadTextureFromFile(filePath);
                        if (texture != null)
                        {
                            // Add to memory cache for future access
                            var fileInfo = new FileInfo(filePath);
                            var entry = new CacheEntry
                            {
                                key = key,
                                filePath = filePath,
                                width = texture.width,
                                height = texture.height,
                                timestamp = fileInfo.LastWriteTime.Ticks,
                                version = CURRENT_VERSION,
                                fileSizeBytes = fileInfo.Length,
                                checksum = "" // Will be calculated on next save
                            };
                            
                            lock (s_lock)
                            {
                                s_memoryCache[key] = entry;
                                UpdateAccessTime(key);
                                TrimMemoryCache();
                            }
                            
                            UpdatePerformanceMetrics(key, true, startTime, isRead: true);
                            return (true, texture);
                        }
                    }
                    catch (Exception e)
                    {
                        LogWarning($"Failed to load texture from file '{filePath}': {e.Message}");
                    }
                }
                
                UpdatePerformanceMetrics(key, false, startTime, isRead: true);
                return (false, null);
            }
            catch (Exception e)
            {
                LogError($"LoadTextureInternal failed for key '{key}': {e.Message}");
                UpdatePerformanceMetrics(key, false, startTime, isRead: true);
                return (false, null);
            }
        }
        
        private static bool ClearCacheInternal(string key)
        {
            try
            {
                lock (s_lock)
                {
                    s_memoryCache.Remove(key);
                    s_accessTimes.Remove(key);
                    s_performanceData.Remove(key);
                }
                
                var filePath = GetCacheFilePath(key);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                
                SaveCacheIndex();
                return true;
            }
            catch (Exception e)
            {
                LogError($"ClearCacheInternal failed for key '{key}': {e.Message}");
                return false;
            }
        }
        
        #endregion
        
        #region Helper Methods
        
        private static string GetCacheFilePath(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Cache key cannot be null or empty");
                
            // Use stable hash for filename
            var hash = ComputeStableHash(key);
            return Path.Combine(CACHE_FOLDER, $"uv_{hash:X8}.png");
        }
        
        private static uint ComputeStableHash(string input)
        {
            // FNV-1a hash for stable results across sessions
            uint hash = 2166136261u;
            foreach (char c in input)
            {
                hash = (hash ^ c) * 16777619u;
            }
            return hash;
        }
        
        private static Texture2D LoadTextureFromFile(string filePath, int expectedWidth = 0, int expectedHeight = 0, string expectedChecksum = null)
        {
            if (!File.Exists(filePath))
                return null;
                
            try
            {
                var data = File.ReadAllBytes(filePath);
                if (data == null || data.Length == 0)
                    return null;
                
                // Verify checksum if provided
                if (!string.IsNullOrEmpty(expectedChecksum))
                {
                    var actualChecksum = CalculateChecksum(data);
                    if (actualChecksum != expectedChecksum)
                    {
                        LogWarning($"Checksum mismatch for file '{filePath}', cache may be corrupted");
                        return null;
                    }
                }
                
                // Pre-allocate texture with expected dimensions for better performance
                var texture = expectedWidth > 0 && expectedHeight > 0 ?
                    new Texture2D(expectedWidth, expectedHeight, TextureFormat.RGBA32, false) :
                    new Texture2D(2, 2, TextureFormat.RGBA32, false);
                
                if (!texture.LoadImage(data))
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                    return null;
                }
                
                return texture;
            }
            catch (Exception e)
            {
                LogError($"LoadTextureFromFile failed for '{filePath}': {e.Message}");
                return null;
            }
        }
        
        private static string CalculateChecksum(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "";
                
            // Simple but fast checksum - can be replaced with MD5/SHA1 for stronger integrity
            uint hash = 0;
            for (int i = 0; i < Math.Min(data.Length, 1024); i++) // Sample first 1KB
            {
                hash = (hash * 31) + data[i];
            }
            return hash.ToString("X8");
        }
        
        private static void UpdateAccessTime(string key)
        {
            s_accessTimes[key] = DateTime.UtcNow;
        }
        
        private static void TrimMemoryCache()
        {
            if (s_memoryCache.Count <= MAX_MEMORY_CACHE_SIZE) return;
            
            try
            {
                // Remove least recently used entries
                var sortedEntries = new List<KeyValuePair<string, DateTime>>(s_accessTimes);
                sortedEntries.Sort((a, b) => a.Value.CompareTo(b.Value));
                
                int toRemove = s_memoryCache.Count - MAX_MEMORY_CACHE_SIZE;
                for (int i = 0; i < toRemove && i < sortedEntries.Count; i++)
                {
                    var key = sortedEntries[i].Key;
                    s_memoryCache.Remove(key);
                    s_accessTimes.Remove(key);
                }
            }
            catch (Exception e)
            {
                LogError($"TrimMemoryCache failed: {e.Message}");
            }
        }
        
        private static void UpdatePerformanceMetrics(string key, bool hit, DateTime startTime, bool isRead)
        {
            try
            {
                lock (s_lock)
                {
                    if (!s_performanceData.TryGetValue(key, out var metrics))
                    {
                        metrics = new PerformanceMetrics();
                    }
                    
                    var duration = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;
                    
                    if (hit)
                        metrics.hitCount++;
                    else
                        metrics.missCount++;
                    
                    if (isRead)
                        metrics.totalReadTime += duration;
                    else
                        metrics.totalWriteTime += duration;
                    
                    metrics.lastAccess = DateTime.UtcNow;
                    
                    s_performanceData[key] = metrics;
                }
            }
            catch (Exception e)
            {
                LogError($"UpdatePerformanceMetrics failed: {e.Message}");
            }
        }
        
        private static bool ExecuteWithRetry(Func<bool> operation, string operationName)
        {
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    if (operation())
                        return true;
                }
                catch (Exception e)
                {
                    if (attempt == MAX_RETRY_ATTEMPTS)
                    {
                        LogError($"{operationName} failed after {MAX_RETRY_ATTEMPTS} attempts: {e.Message}");
                        return false;
                    }
                    
                    LogWarning($"{operationName} attempt {attempt} failed, retrying: {e.Message}");
                    Thread.Sleep(RETRY_DELAY_MS * attempt); // Exponential backoff
                }
            }
            
            return false;
        }
        
        private static (bool success, T result) ExecuteWithRetry<T>(Func<(bool, T)> operation, string operationName)
        {
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    var (success, result) = operation();
                    if (success)
                        return (true, result);
                }
                catch (Exception e)
                {
                    if (attempt == MAX_RETRY_ATTEMPTS)
                    {
                        LogError($"{operationName} failed after {MAX_RETRY_ATTEMPTS} attempts: {e.Message}");
                        return (false, default(T));
                    }
                    
                    LogWarning($"{operationName} attempt {attempt} failed, retrying: {e.Message}");
                    Thread.Sleep(RETRY_DELAY_MS * attempt);
                }
            }
            
            return (false, default(T));
        }
        
        #endregion
        
        #region Persistence
        
        private static void LoadCacheIndex()
        {
            try
            {
                if (!File.Exists(CACHE_INDEX_FILE))
                    return;
                
                var json = File.ReadAllText(CACHE_INDEX_FILE);
                var index = JsonUtility.FromJson<CacheIndex>(json);
                
                if (index?.version == CURRENT_VERSION && index.entries != null)
                {
                    lock (s_lock)
                    {
                        foreach (var entry in index.entries)
                        {
                            if (entry.IsValid && File.Exists(entry.filePath))
                            {
                                s_memoryCache[entry.key] = entry;
                                s_accessTimes[entry.key] = new DateTime(entry.timestamp);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogWarning($"Failed to load cache index: {e.Message}");
            }
        }
        
        private static void SaveCacheIndex()
        {
            try
            {
                var index = new CacheIndex();
                
                lock (s_lock)
                {
                    foreach (var entry in s_memoryCache.Values)
                    {
                        if (entry.IsValid)
                        {
                            index.entries.Add(entry);
                            index.totalSizeBytes += entry.fileSizeBytes;
                        }
                    }
                }
                
                index.lastCleanup = DateTime.UtcNow;
                
                var json = JsonUtility.ToJson(index, false);
                var tempPath = CACHE_INDEX_FILE + ".tmp";
                
                File.WriteAllText(tempPath, json);
                
                if (File.Exists(CACHE_INDEX_FILE))
                {
                    File.Delete(CACHE_INDEX_FILE);
                }
                File.Move(tempPath, CACHE_INDEX_FILE);
            }
            catch (Exception e)
            {
                LogError($"Failed to save cache index: {e.Message}");
            }
        }
        
        #endregion
        
        #region Periodic Maintenance
        
        private static void SchedulePeriodicCleanup()
        {
            EditorApplication.update += PeriodicMaintenanceCheck;
        }
        
        private static DateTime s_lastMaintenanceCheck = DateTime.MinValue;
        
        private static void PeriodicMaintenanceCheck()
        {
            var now = DateTime.UtcNow;
            if ((now - s_lastMaintenanceCheck).TotalHours >= 24) // Daily maintenance
            {
                s_lastMaintenanceCheck = now;
                
                try
                {
                    CleanupCache();
                }
                catch (Exception e)
                {
                    LogError($"Periodic maintenance failed: {e.Message}");
                }
            }
        }
        
        #endregion
        
        #region Logging
        
        private static void LogInfo(string message)
        {
            Debug.Log($"{LOG_PREFIX} {message}");
        }
        
        private static void LogWarning(string message)
        {
            Debug.LogWarning($"{LOG_PREFIX} {message}");
        }
        
        private static void LogError(string message)
        {
            Debug.LogError($"{LOG_PREFIX} {message}");
        }
        
        #endregion
        
        #region Public Data Structures
        
        [System.Serializable]
        public struct CacheStatistics
        {
            public int memoryCacheCount;
            public int fileCacheCount;
            public long totalSizeBytes;
            public int totalHitCount;
            public int totalMissCount;
            public float overallHitRate;
            public float totalReadTime;
            public float totalWriteTime;
            public float averageReadTime;
            public float averageWriteTime;
            
            public override string ToString()
            {
                return $"Cache Stats: Memory({memoryCacheCount}) Files({fileCacheCount}) " +
                       $"Size({totalSizeBytes / 1024f:F1}KB) HitRate({overallHitRate:P1}) " +
                       $"AvgRead({averageReadTime:F2}ms) AvgWrite({averageWriteTime:F2}ms)";
            }
        }
        
        #endregion
        
        #region Editor Integration
        
        [MenuItem("Tools/UV Cache/Show Statistics")]
        private static void ShowStatistics()
        {
            var stats = GetCacheStatistics();
            LogInfo($"Cache Statistics: {stats}");
            
            EditorUtility.DisplayDialog("UV Cache Statistics", 
                $"Memory Cache: {stats.memoryCacheCount} entries\n" +
                $"File Cache: {stats.fileCacheCount} files\n" +
                $"Total Size: {stats.totalSizeBytes / 1024f:F1} KB\n" +
                $"Hit Rate: {stats.overallHitRate:P1}\n" +
                $"Avg Read Time: {stats.averageReadTime:F2} ms\n" +
                $"Avg Write Time: {stats.averageWriteTime:F2} ms", "OK");
        }
        
        [MenuItem("Tools/UV Cache/Clear All Cache")]
        private static void ClearAllCache()
        {
            if (EditorUtility.DisplayDialog("Clear UV Cache", 
                "Are you sure you want to clear all cached UV textures?", "Yes", "Cancel"))
            {
                CleanupCache(force: true);
                LogInfo("All cache cleared by user");
            }
        }
        
        [MenuItem("Tools/UV Cache/Force Cleanup")]
        private static void ForceCleanup()
        {
            CleanupCache(force: false);
        }
        
        #endregion
    }
}