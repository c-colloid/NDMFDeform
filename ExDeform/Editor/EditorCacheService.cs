using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using ExDeform.Core.Interfaces;

namespace ExDeform.Editor
{
    /// <summary>
    /// Editor cache service implementation that integrates UV Island cache management
    /// with async operations, cache invalidation, and statistics reporting
    /// </summary>
    public class EditorCacheService : IEditorCacheService
    {
        #region Private Fields

        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new ConcurrentDictionary<string, CacheEntry>();
        private readonly object _statisticsLock = new object();
        
        // Statistics tracking
        private int _hitCount = 0;
        private int _missCount = 0;
        private double _totalReadTime = 0.0;
        private int _readOperations = 0;

        // Cache health monitoring
        private DateTime _lastHealthCheck = DateTime.MinValue;
        private const double CACHE_HEALTH_CHECK_INTERVAL_HOURS = 1.0;

        #endregion

        #region Cache Entry Class

        private class CacheEntry
        {
            public object Data { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastAccessed { get; set; }
            public Type DataType { get; set; }
            
            public CacheEntry(object data, Type dataType)
            {
                Data = data;
                DataType = dataType;
                CreatedAt = DateTime.UtcNow;
                LastAccessed = DateTime.UtcNow;
            }
        }

        #endregion

        #region IEditorCacheService Implementation

        /// <summary>
        /// Get or create cached data asynchronously
        /// </summary>
        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory) where T : class
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Cache key cannot be null or empty", nameof(key));
            
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            var startTime = DateTime.UtcNow;
            
            try
            {
                // Try to get from cache first
                if (_cache.TryGetValue(key, out var entry))
                {
                    entry.LastAccessed = DateTime.UtcNow;
                    
                    if (entry.Data is T cachedData)
                    {
                        RecordCacheHit(startTime);
                        return cachedData;
                    }
                    else
                    {
                        // Type mismatch, remove invalid entry
                        LogCacheOperation($"Type mismatch for key '{key}'. Expected: {typeof(T).Name}, Found: {entry.DataType.Name}", isError: true);
                        _cache.TryRemove(key, out _);
                    }
                }

                // Cache miss, create new data
                RecordCacheMiss(startTime);
                
                var newData = await factory();
                if (newData != null)
                {
                    var newEntry = new CacheEntry(newData, typeof(T));
                    _cache.TryAdd(key, newEntry);
                }

                return newData;
            }
            catch (Exception ex)
            {
                LogCacheOperation($"Exception in GetOrCreateAsync for key '{key}': {ex.Message}", isError: true);
                throw;
            }
        }

        /// <summary>
        /// Get or create cached data asynchronously with synchronous factory
        /// </summary>
        public async Task<T> GetOrCreateAsync<T>(string key, Func<T> factory) where T : class
        {
            return await GetOrCreateAsync(key, () => Task.FromResult(factory()));
        }

        /// <summary>
        /// Invalidate cache entry by key
        /// </summary>
        public void InvalidateCache(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                LogCacheOperation("InvalidateCache called with null or empty key", isError: true);
                return;
            }

            if (_cache.TryRemove(key, out var entry))
            {
                LogCacheOperation($"Invalidated cache entry for key: {key}");
                
                // Clean up disposable resources
                if (entry.Data is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        LogCacheOperation($"Error disposing cached data for key '{key}': {ex.Message}", isError: true);
                    }
                }
            }
            else
            {
                LogCacheOperation($"Attempted to invalidate non-existent cache key: {key}");
            }
        }

        /// <summary>
        /// Invalidate all cache entries matching a pattern
        /// </summary>
        public void InvalidateCachePattern(string keyPattern)
        {
            if (string.IsNullOrEmpty(keyPattern))
            {
                LogCacheOperation("InvalidateCache called with null or empty pattern", isError: true);
                return;
            }

            var keysToRemove = new List<string>();
            
            // Simple wildcard support (* at the end)
            bool isWildcard = keyPattern.EndsWith("*");
            string pattern = isWildcard ? keyPattern.TrimEnd('*') : keyPattern;

            foreach (var key in _cache.Keys)
            {
                bool matches = isWildcard ? key.StartsWith(pattern) : key.Equals(keyPattern);
                
                if (matches)
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                InvalidateCache(key);
            }

            LogCacheOperation($"Invalidated {keysToRemove.Count} cache entries matching pattern: {keyPattern}");
        }

        /// <summary>
        /// Clear all cached data
        /// </summary>
        public void InvalidateAllCache()
        {
            var totalEntries = _cache.Count;
            
            // Dispose all disposable entries
            foreach (var entry in _cache.Values)
            {
                if (entry.Data is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        LogCacheOperation($"Error disposing cached data: {ex.Message}", isError: true);
                    }
                }
            }

            _cache.Clear();
            LogCacheOperation($"Cleared all {totalEntries} cache entries");
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            lock (_statisticsLock)
            {
                long totalSize = 0;
                
                // Estimate size (this is approximate)
                foreach (var entry in _cache.Values)
                {
                    totalSize += EstimateObjectSize(entry.Data);
                }

                var totalOperations = _hitCount + _missCount;
                var hitRate = totalOperations > 0 ? (float)_hitCount / totalOperations : 0f;
                var avgReadTime = _readOperations > 0 ? _totalReadTime / _readOperations : 0.0;

                return new CacheStatistics
                {
                    entryCount = _cache.Count,
                    totalSizeBytes = totalSize,
                    hitRate = hitRate,
                    averageAccessTime = (float)avgReadTime
                };
            }
        }

        #endregion

        #region Private Methods

        private void RecordCacheHit(DateTime startTime)
        {
            lock (_statisticsLock)
            {
                _hitCount++;
                _readOperations++;
                _totalReadTime += (DateTime.UtcNow - startTime).TotalMilliseconds;
            }
            
            CheckCacheHealth();
        }

        private void RecordCacheMiss(DateTime startTime)
        {
            lock (_statisticsLock)
            {
                _missCount++;
                _readOperations++;
                _totalReadTime += (DateTime.UtcNow - startTime).TotalMilliseconds;
            }
        }

        private void CheckCacheHealth()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastHealthCheck).TotalHours >= CACHE_HEALTH_CHECK_INTERVAL_HOURS)
            {
                _lastHealthCheck = now;
                
                try
                {
                    var stats = GetStatistics();
                    
                    // Log performance metrics
                    LogCacheOperation($"Cache Health Check: {stats}");
                    
                    // Check for performance issues  
                    var totalOps = _hitCount + _missCount;
                    if (stats.hitRate < 0.5f && totalOps > 10)
                    {
                        LogCacheOperation($"Low cache hit rate detected: {stats.hitRate:P1} - Consider investigating cache key generation", isError: true);
                    }
                    
                    if (stats.averageAccessTime > 5.0f)
                    {
                        LogCacheOperation($"Slow cache read performance: {stats.averageAccessTime:F2}ms average - Consider cache cleanup", isError: true);
                    }
                    
                    // Automatic cleanup for large caches
                    if (stats.totalSizeBytes > 100 * 1024 * 1024) // 100MB
                    {
                        LogCacheOperation("Cache size exceeding 100MB, scheduling cleanup...");
                        EditorApplication.delayCall += () => CleanupOldEntries();
                    }
                }
                catch (Exception e)
                {
                    LogCacheOperation($"Cache health check failed: {e.Message}", isError: true);
                }
            }
        }

        private void CleanupOldEntries()
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-24); // Remove entries older than 24 hours
            var keysToRemove = new List<string>();

            foreach (var kvp in _cache)
            {
                if (kvp.Value.LastAccessed < cutoffTime)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                InvalidateCache(key);
            }

            if (keysToRemove.Count > 0)
            {
                LogCacheOperation($"Cleaned up {keysToRemove.Count} old cache entries");
            }
        }

        private long EstimateObjectSize(object obj)
        {
            if (obj == null) return 0;

            // Rough estimation based on common types
            switch (obj)
            {
                case string str:
                    return str.Length * sizeof(char) + 24; // String overhead
                case Texture2D texture:
                    return texture.width * texture.height * 4 + 100; // RGBA + overhead
                case UVIslandSelector selector:
                    return 10000; // Rough estimate for selector data
                default:
                    return 100; // Default estimate for unknown objects
            }
        }

        private void LogCacheOperation(string message, bool isError = false)
        {
            var logMessage = $"[EditorCacheService] {message}";
            
            if (isError)
            {
                Debug.LogError(logMessage);
            }
            else
            {
                // Only log in debug mode to avoid spam
                if (Debug.isDebugBuild)
                {
                    Debug.Log(logMessage);
                }
            }
        }

        #endregion

        #region Static Instance

        private static EditorCacheService _instance;
        
        /// <summary>
        /// Get the singleton instance of the cache service
        /// </summary>
        public static EditorCacheService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new EditorCacheService();
                    
                    // Clean up on editor shutdown
                    EditorApplication.quitting += () =>
                    {
                        _instance?.InvalidateAllCache();
                        _instance = null;
                    };
                }
                return _instance;
            }
        }

        #endregion
    }
}