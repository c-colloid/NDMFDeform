using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ExDeform.Core.Interfaces;

namespace ExDeform.Editor
{
    /// <summary>
    /// Interface for editor cache service that provides async operations for caching,
    /// cache invalidation, and statistics reporting
    /// </summary>
    public interface IEditorCacheService
    {
        /// <summary>
        /// Get or create cached data asynchronously
        /// </summary>
        /// <typeparam name="T">Type of data to cache</typeparam>
        /// <param name="key">Cache key</param>
        /// <param name="factory">Factory function to create data if not cached</param>
        /// <returns>Cached or newly created data</returns>
        Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory) where T : class;

        /// <summary>
        /// Get or create cached data asynchronously with synchronous factory
        /// </summary>
        /// <typeparam name="T">Type of data to cache</typeparam>
        /// <param name="key">Cache key</param>
        /// <param name="factory">Synchronous factory function to create data if not cached</param>
        /// <returns>Cached or newly created data</returns>
        Task<T> GetOrCreateAsync<T>(string key, Func<T> factory) where T : class;

        /// <summary>
        /// Invalidate cache entry by key
        /// </summary>
        /// <param name="key">Cache key to invalidate</param>
        void InvalidateCache(string key);

        /// <summary>
        /// Invalidate all cache entries matching a pattern
        /// </summary>
        /// <param name="keyPattern">Pattern to match cache keys (supports wildcards)</param>
        void InvalidateCachePattern(string keyPattern);

        /// <summary>
        /// Clear all cached data
        /// </summary>
        void InvalidateAllCache();

        /// <summary>
        /// Get cache statistics
        /// </summary>
        /// <returns>Cache performance statistics</returns>
        CacheStatistics GetStatistics();
    }
}