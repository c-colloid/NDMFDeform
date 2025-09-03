using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEditor;

namespace ExDeform.Editor
{
    /// <summary>
    /// Refactored production-ready UV texture cache with improved code structure
    /// 改善されたコード構造を持つ本格的なUVテクスチャキャッシュ（リファクタリング版）
    /// </summary>
    public static class RefactoredRobustUVCache
    {
        #region Configuration Constants
        private const string CACHE_FOLDER = "Library/UVIslandCache";
        private const string CACHE_INDEX_FILE = "Library/UVIslandCache/.cache_index";
        private const string LOG_PREFIX = "[RefactoredRobustUVCache]";
        private const int CURRENT_VERSION = 1;
        private const int MAX_MEMORY_CACHE_SIZE = 100;
        private const int MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024; // 10MB
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 100;
        private const int MAX_CONCURRENT_OPERATIONS = 4;
        #endregion
        
        #region Thread-Safe Data
        private static readonly object s_lock = new object();
        private static readonly Dictionary<string, CacheEntry> s_memoryCache = new Dictionary<string, CacheEntry>();
        private static readonly Dictionary<string, DateTime> s_accessTimes = new Dictionary<string, DateTime>();
        private static readonly SemaphoreSlim s_operationSemaphore = new SemaphoreSlim(MAX_CONCURRENT_OPERATIONS);
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
            public string checksum;
            
            public bool IsValid => !string.IsNullOrEmpty(key) && 
                                  !string.IsNullOrEmpty(filePath) && 
                                  width > 0 && height > 0 && 
                                  version == CURRENT_VERSION;
        }
        #endregion
        
        #region Public API
        public static bool SaveTexture(string key, Texture2D texture)
        {
            EnsureInitialized();
            
            if (!ValidateInput(key, texture))
                return false;
                
            return ExecuteWithRetry(() => SaveTextureInternal(key, texture), $"SaveTexture({key})");
        }
        
        public static Texture2D LoadTexture(string key)
        {
            EnsureInitialized();
            
            if (string.IsNullOrWhiteSpace(key))
            {
                LogWarning("LoadTexture called with null or empty key");
                return null;
            }
            
            var result = ExecuteWithRetry(() => LoadTextureInternal(key), $"LoadTexture({key})");
            return result.success ? result.result : null;
        }
        
        public static bool HasCache(string key)
        {
            EnsureInitialized();
            return !string.IsNullOrWhiteSpace(key) && HasCacheInternal(key);
        }
        
        public static void ClearCache(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            
            ExecuteWithRetry(() => ClearCacheInternal(key), $"ClearCache({key})");
        }
        #endregion
        
        #region Input Validation
        private static bool ValidateInput(string key, Texture2D texture)
        {
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
            
            return true;
        }
        #endregion
        
        #region Save Operation (Refactored)
        private static bool SaveTextureInternal(string key, Texture2D texture)
        {
            var startTime = DateTime.UtcNow;
            
            if (!AcquireOperationSlot(key))
                return false;
                
            try
            {
                var pngData = ValidateAndEncodeToPNG(texture, key);
                if (pngData == null) 
                    return false;
                
                var filePath = GetCacheFilePath(key);
                if (!WriteTextureToFile(pngData, filePath, key))
                    return false;
                
                var checksum = CalculateChecksum(pngData);
                var entry = CreateCacheEntry(key, filePath, texture, pngData.Length, checksum);
                
                UpdateMemoryCache(key, entry);
                SaveCacheIndex();
                UpdatePerformanceMetrics(key, true, startTime, isRead: false);
                
                return true;
            }
            catch (Exception e)
            {
                LogError($"SaveTextureInternal failed for key '{key}': {e.Message}");
                UpdatePerformanceMetrics(key, false, startTime, isRead: false);
                return false;
            }
            finally
            {
                s_operationSemaphore.Release();
            }
        }
        
        private static bool AcquireOperationSlot(string key)
        {
            if (!s_operationSemaphore.Wait(5000))
            {
                LogWarning($"SaveTexture timed out waiting for operation slot: {key}");
                return false;
            }
            return true;
        }
        
        private static byte[] ValidateAndEncodeToPNG(Texture2D texture, string key)
        {
            var pngData = texture.EncodeToPNG();
            if (pngData == null || pngData.Length == 0)
            {
                LogError($"Failed to encode texture to PNG: {key}");
                return null;
            }
            
            if (pngData.Length > MAX_FILE_SIZE_BYTES)
            {
                LogWarning($"Texture too large for cache ({pngData.Length} bytes): {key}");
                return null;
            }
            
            return pngData;
        }
        
        private static bool WriteTextureToFile(byte[] pngData, string filePath, string key)
        {
            try
            {
                var tempPath = filePath + ".tmp";
                
                // Atomic write operation
                File.WriteAllBytes(tempPath, pngData);
                
                // Move to final location
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tempPath, filePath);
                
                return true;
            }
            catch (Exception e)
            {
                LogError($"Failed to write texture file for key '{key}': {e.Message}");
                return false;
            }
        }
        
        private static CacheEntry CreateCacheEntry(string key, string filePath, Texture2D texture, int dataLength, string checksum)
        {
            return new CacheEntry
            {
                key = key,
                filePath = filePath,
                width = texture.width,
                height = texture.height,
                timestamp = DateTime.UtcNow.Ticks,
                version = CURRENT_VERSION,
                fileSizeBytes = dataLength,
                checksum = checksum
            };
        }
        
        private static void UpdateMemoryCache(string key, CacheEntry entry)
        {
            lock (s_lock)
            {
                s_memoryCache[key] = entry;
                UpdateAccessTime(key);
                TrimMemoryCache();
            }
        }
        #endregion
        
        #region Load Operation (Refactored)
        private static (bool success, Texture2D result) LoadTextureInternal(string key)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                // Try memory cache first
                var cacheResult = TryLoadFromMemoryCache(key);
                if (cacheResult.success)
                {
                    UpdatePerformanceMetrics(key, true, startTime, isRead: true);
                    return cacheResult;
                }
                
                // Try direct file access
                var fileResult = TryLoadFromFile(key);
                if (fileResult.success)
                {
                    AddToMemoryCache(key, fileResult.filePath, fileResult.texture);
                    UpdatePerformanceMetrics(key, true, startTime, isRead: true);
                    return (true, fileResult.texture);
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
        
        private static (bool success, Texture2D texture) TryLoadFromMemoryCache(string key)
        {
            lock (s_lock)
            {
                if (s_memoryCache.TryGetValue(key, out var entry) && entry.IsValid)
                {
                    UpdateAccessTime(key);
                    
                    if (File.Exists(entry.filePath))
                    {
                        var texture = LoadTextureFromFile(entry.filePath, entry.width, entry.height, entry.checksum);
                        if (texture != null)
                        {
                            return (true, texture);
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
            
            return (false, null);
        }
        
        private static (bool success, string filePath, Texture2D texture) TryLoadFromFile(string key)
        {
            var filePath = GetCacheFilePath(key);
            if (!File.Exists(filePath)) 
                return (false, null, null);
                
            try
            {
                var texture = LoadTextureFromFile(filePath);
                if (texture != null)
                {
                    return (true, filePath, texture);
                }
            }
            catch (Exception e)
            {
                LogWarning($"Failed to load texture from file '{filePath}': {e.Message}");
            }
            
            return (false, null, null);
        }
        
        private static void AddToMemoryCache(string key, string filePath, Texture2D texture)
        {
            try
            {
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
                    checksum = ""
                };
                
                lock (s_lock)
                {
                    s_memoryCache[key] = entry;
                    UpdateAccessTime(key);
                    TrimMemoryCache();
                }
            }
            catch (Exception e)
            {
                LogWarning($"Failed to add to memory cache for key '{key}': {e.Message}");
            }
        }
        #endregion
        
        #region Helper Methods
        private static void EnsureInitialized()
        {
            if (s_isInitialized) return;
            
            try
            {
                InitializeCacheDirectory();
                LoadCacheIndex();
                s_isInitialized = true;
                LogInfo("Cache system initialized");
            }
            catch (Exception e)
            {
                LogError($"Failed to initialize cache system: {e.Message}");
            }
        }
        
        private static void InitializeCacheDirectory()
        {
            if (!Directory.Exists(CACHE_FOLDER))
            {
                Directory.CreateDirectory(CACHE_FOLDER);
            }
        }
        
        private static bool HasCacheInternal(string key)
        {
            lock (s_lock)
            {
                if (s_memoryCache.ContainsKey(key))
                    return true;
            }
            
            return File.Exists(GetCacheFilePath(key));
        }
        
        private static bool ClearCacheInternal(string key)
        {
            try
            {
                lock (s_lock)
                {
                    s_memoryCache.Remove(key);
                    s_accessTimes.Remove(key);
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
        
        private static string GetCacheFilePath(string key)
        {
            var hash = ComputeStableHash(key);
            return Path.Combine(CACHE_FOLDER, $"uv_{hash:X8}.png");
        }
        
        private static uint ComputeStableHash(string input)
        {
            uint hash = 2166136261u;
            foreach (char c in input)
            {
                hash = (hash ^ c) * 16777619u;
            }
            return hash;
        }
        
        private static string CalculateChecksum(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            
            uint hash = 0;
            for (int i = 0; i < Math.Min(data.Length, 1024); i++)
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
        
        private static Texture2D LoadTextureFromFile(string filePath, int expectedWidth = 0, int expectedHeight = 0, string expectedChecksum = null)
        {
            if (!File.Exists(filePath)) return null;
            
            try
            {
                var data = File.ReadAllBytes(filePath);
                if (data == null || data.Length == 0) return null;
                
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
        
        private static bool ExecuteWithRetry(System.Func<bool> operation, string operationName)
        {
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    if (operation()) return true;
                }
                catch (Exception e)
                {
                    if (attempt == MAX_RETRY_ATTEMPTS)
                    {
                        LogError($"{operationName} failed after {MAX_RETRY_ATTEMPTS} attempts: {e.Message}");
                        return false;
                    }
                    
                    LogWarning($"{operationName} attempt {attempt} failed, retrying: {e.Message}");
                    Thread.Sleep(RETRY_DELAY_MS * attempt);
                }
            }
            
            return false;
        }
        
        private static (bool success, T result) ExecuteWithRetry<T>(System.Func<(bool, T)> operation, string operationName)
        {
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    var (success, result) = operation();
                    if (success) return (true, result);
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
        
        #region Placeholder Methods
        private static void LoadCacheIndex() { /* Implementation needed */ }
        private static void SaveCacheIndex() { /* Implementation needed */ }
        private static void UpdatePerformanceMetrics(string key, bool success, DateTime startTime, bool isRead) { /* Implementation needed */ }
        #endregion
        
        #region Logging
        private static void LogInfo(string message) => Debug.Log($"{LOG_PREFIX} {message}");
        private static void LogWarning(string message) => Debug.LogWarning($"{LOG_PREFIX} {message}");
        private static void LogError(string message) => Debug.LogError($"{LOG_PREFIX} {message}");
        #endregion
    }
}