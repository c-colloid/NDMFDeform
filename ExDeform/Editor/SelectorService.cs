using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace ExDeform.Editor
{
    /// <summary>
    /// Service implementation for managing UVIslandSelector lifecycle, initialization, and disposal
    /// UVIslandSelectorのライフサイクル、初期化、破棄を管理するサービス実装
    /// </summary>
    public class SelectorService : ISelectorService
    {
        #region Singleton Pattern
        
        private static SelectorService _instance;
        private static readonly object _lock = new object();
        
        /// <summary>
        /// Singleton instance of the selector service
        /// </summary>
        public static SelectorService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new SelectorService();
                        }
                    }
                }
                return _instance;
            }
        }
        
        #endregion
        
        #region Fields and Constants
        
        private readonly Dictionary<string, UVIslandSelector> _cachedSelectors;
        private readonly Dictionary<string, DateTime> _lastAccessTimes;
        private readonly Dictionary<string, SelectorConfig> _selectorConfigs;
        private readonly IEditorCacheService _cacheService;
        
        // Statistics tracking
        private int _cacheHits = 0;
        private int _cacheMisses = 0;
        private int _totalSelectorsCreated = 0;
        private int _totalSelectorsDisposed = 0;
        private DateTime _lastHealthCheck = DateTime.MinValue;
        
        // Configuration
        private const int MAX_CACHED_SELECTORS = 50;
        private const double SELECTOR_EXPIRY_HOURS = 2.0;
        private const double HEALTH_CHECK_INTERVAL_MINUTES = 30.0;
        private const string CACHE_KEY_PREFIX = "SelectorService_";
        
        #endregion
        
        #region Constructor
        
        private SelectorService()
        {
            _cachedSelectors = new Dictionary<string, UVIslandSelector>();
            _lastAccessTimes = new Dictionary<string, DateTime>();
            _selectorConfigs = new Dictionary<string, SelectorConfig>();
            _cacheService = EditorCacheService.Instance;
            
            // Register for editor application events
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }
        
        #endregion
        
        #region Public API - Selector Lifecycle
        
        public UVIslandSelector GetOrCreateSelector(Mesh mesh, string cacheKey = null)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh), "Mesh cannot be null");
            }
            
            var config = new SelectorConfig
            {
                TargetMesh = mesh,
                CacheKey = cacheKey
            };
            
            return GetOrCreateSelector(config);
        }
        
        public UVIslandSelector GetOrCreateSelector(SelectorConfig config)
        {
            if (config?.TargetMesh == null)
            {
                throw new ArgumentNullException(nameof(config), "Config and target mesh cannot be null");
            }
            
            string cacheKey = config.CacheKey ?? GenerateCacheKey(config.TargetMesh);
            
            // Check if selector exists in cache
            if (_cachedSelectors.TryGetValue(cacheKey, out var existingSelector))
            {
                _cacheHits++;
                _lastAccessTimes[cacheKey] = DateTime.Now;
                
                // Update configuration if needed
                UpdateSelectorConfiguration(existingSelector, config);
                return existingSelector;
            }
            
            // Create new selector
            _cacheMisses++;
            var selector = CreateNewSelector(config);
            
            // Cache the selector
            CacheSelector(cacheKey, selector, config);
            
            return selector;
        }
        
        public void InitializeSelector(UVIslandSelector selector, Mesh mesh, SelectorConfig config = null)
        {
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));
            
            // Set mesh and update data
            selector.SetMesh(mesh);
            
            // Apply configuration if provided
            if (config != null)
            {
                ApplyConfiguration(selector, config);
            }
        }
        
        public void DisposeSelector(UVIslandSelector selector)
        {
            if (selector == null) return;
            
            // Find and remove from cache
            var cacheKeyToRemove = _cachedSelectors.FirstOrDefault(kvp => kvp.Value == selector).Key;
            if (!string.IsNullOrEmpty(cacheKeyToRemove))
            {
                DisposeSelector(cacheKeyToRemove);
            }
            else
            {
                // Not in cache, dispose directly
                selector.Dispose();
                _totalSelectorsDisposed++;
            }
        }
        
        public void DisposeSelector(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey)) return;
            
            if (_cachedSelectors.TryGetValue(cacheKey, out var selector))
            {
                selector.Dispose();
                _cachedSelectors.Remove(cacheKey);
                _lastAccessTimes.Remove(cacheKey);
                _selectorConfigs.Remove(cacheKey);
                _totalSelectorsDisposed++;
            }
        }
        
        #endregion
        
        #region Public API - Cache Management
        
        public bool HasCachedSelector(string cacheKey)
        {
            return !string.IsNullOrEmpty(cacheKey) && _cachedSelectors.ContainsKey(cacheKey);
        }
        
        public string GenerateCacheKey(Mesh mesh)
        {
            if (mesh == null) return null;
            return $"{CACHE_KEY_PREFIX}{mesh.GetInstanceID()}_{mesh.vertexCount}_{mesh.triangles?.Length ?? 0}";
        }
        
        public string GenerateCacheKey(Mesh mesh, string suffix)
        {
            var baseKey = GenerateCacheKey(mesh);
            return !string.IsNullOrEmpty(suffix) ? $"{baseKey}_{suffix}" : baseKey;
        }
        
        public void ClearCache()
        {
            foreach (var selector in _cachedSelectors.Values)
            {
                selector.Dispose();
                _totalSelectorsDisposed++;
            }
            
            _cachedSelectors.Clear();
            _lastAccessTimes.Clear();
            _selectorConfigs.Clear();
        }
        
        public void ClearCache(string keyPattern)
        {
            if (string.IsNullOrEmpty(keyPattern)) return;
            
            var keysToRemove = _cachedSelectors.Keys
                .Where(key => MatchesPattern(key, keyPattern))
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                DisposeSelector(key);
            }
        }
        
        #endregion
        
        #region Public API - Statistics and Health
        
        public SelectorServiceStatistics GetStatistics()
        {
            return new SelectorServiceStatistics
            {
                totalCachedSelectors = _cachedSelectors.Count,
                cacheHits = _cacheHits,
                cacheMisses = _cacheMisses,
                cacheHitRate = _cacheHits + _cacheMisses > 0 ? (float)_cacheHits / (_cacheHits + _cacheMisses) : 0f,
                totalSelectorsCreated = _totalSelectorsCreated,
                totalSelectorsDisposed = _totalSelectorsDisposed,
                activeSelectors = _totalSelectorsCreated - _totalSelectorsDisposed,
                estimatedMemoryUsage = EstimateMemoryUsage(),
                lastHealthCheck = _lastHealthCheck
            };
        }
        
        public void PerformHealthCheck()
        {
            _lastHealthCheck = DateTime.Now;
            
            var expiredKeys = new List<string>();
            var currentTime = DateTime.Now;
            
            // Find expired selectors
            foreach (var kvp in _lastAccessTimes)
            {
                var timeSinceLastAccess = currentTime - kvp.Value;
                if (timeSinceLastAccess.TotalHours > SELECTOR_EXPIRY_HOURS)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }
            
            // Remove expired selectors
            foreach (var key in expiredKeys)
            {
                DisposeSelector(key);
            }
            
            // Enforce cache size limit
            if (_cachedSelectors.Count > MAX_CACHED_SELECTORS)
            {
                var oldestKeys = _lastAccessTimes
                    .OrderBy(kvp => kvp.Value)
                    .Take(_cachedSelectors.Count - MAX_CACHED_SELECTORS)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in oldestKeys)
                {
                    DisposeSelector(key);
                }
            }
        }
        
        #endregion
        
        #region Private Helper Methods
        
        private UVIslandSelector CreateNewSelector(SelectorConfig config)
        {
            var selector = new UVIslandSelector(config.TargetMesh);
            _totalSelectorsCreated++;
            
            // Apply configuration
            ApplyConfiguration(selector, config);
            
            return selector;
        }
        
        private void ApplyConfiguration(UVIslandSelector selector, SelectorConfig config)
        {
            if (config == null) return;
            
            // Apply basic properties
            selector.UseAdaptiveVertexSize = config.UseAdaptiveVertexSize;
            selector.ManualVertexSphereSize = config.ManualVertexSphereSize;
            selector.AdaptiveSizeMultiplier = config.AdaptiveSizeMultiplier;
            selector.AutoUpdatePreview = config.AutoUpdatePreview;
            selector.EnableRangeSelection = config.EnableRangeSelection;
            selector.EnableMagnifyingGlass = config.EnableMagnifyingGlass;
            
            // Set transform and dynamic mesh
            if (config.TargetTransform != null)
                selector.TargetTransform = config.TargetTransform;
            
            if (config.DynamicMesh != null)
                selector.DynamicMesh = config.DynamicMesh;
            
            // Set selected islands
            if (config.SelectedIslandIDs != null && config.SelectedIslandIDs.Count > 0)
                selector.SetSelectedIslands(config.SelectedIslandIDs);
        }
        
        private void UpdateSelectorConfiguration(UVIslandSelector selector, SelectorConfig config)
        {
            if (config == null) return;
            
            // Only update if mesh has changed
            if (selector.TargetMesh != config.TargetMesh)
            {
                selector.SetMesh(config.TargetMesh);
            }
            
            // Apply updated configuration
            ApplyConfiguration(selector, config);
        }
        
        private void CacheSelector(string cacheKey, UVIslandSelector selector, SelectorConfig config)
        {
            _cachedSelectors[cacheKey] = selector;
            _lastAccessTimes[cacheKey] = DateTime.Now;
            _selectorConfigs[cacheKey] = config;
            
            // Trigger health check if needed
            var timeSinceLastHealthCheck = DateTime.Now - _lastHealthCheck;
            if (timeSinceLastHealthCheck.TotalMinutes > HEALTH_CHECK_INTERVAL_MINUTES)
            {
                PerformHealthCheck();
            }
        }
        
        private bool MatchesPattern(string text, string pattern)
        {
            // Simple wildcard matching for now
            if (pattern == "*") return true;
            if (pattern.EndsWith("*"))
                return text.StartsWith(pattern.Substring(0, pattern.Length - 1));
            if (pattern.StartsWith("*"))
                return text.EndsWith(pattern.Substring(1));
            return text.Contains(pattern);
        }
        
        private long EstimateMemoryUsage()
        {
            // Rough estimation based on selector count and typical data size
            return _cachedSelectors.Count * 10000L; // 10KB per selector estimate
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnEditorQuitting()
        {
            ClearCache();
        }
        
        private void OnBeforeAssemblyReload()
        {
            ClearCache();
        }
        
        #endregion
        
        #region IDisposable Support (for potential future use)
        
        /// <summary>
        /// Clean up all resources when service is destroyed
        /// </summary>
        ~SelectorService()
        {
            ClearCache();
        }
        
        #endregion
    }
}