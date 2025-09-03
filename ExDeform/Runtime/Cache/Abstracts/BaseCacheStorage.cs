using System;
using UnityEngine;
using ExDeform.Runtime.Core.Interfaces;
using ExDeform.Runtime.Core.Constants;

namespace ExDeform.Runtime.Cache.Abstracts
{
    /// <summary>
    /// キャッシュストレージの基底クラス
    /// 共通機能とテンプレートメソッドパターンを提供
    /// </summary>
    public abstract class BaseCacheStorage : ICacheStorage
    {
        #region Protected Fields
        protected readonly ILogger logger;
        private CacheStatistics statistics;
        private readonly object statisticsLock = new object();
        #endregion
        
        #region Constructor
        protected BaseCacheStorage(ILogger logger = null)
        {
            this.logger = logger ?? new DefaultLogger();
            statistics = new CacheStatistics();
            InitializeStorage();
        }
        #endregion
        
        #region ICacheStorage Implementation
        public bool SaveTexture(string key, Texture2D texture)
        {
            if (string.IsNullOrEmpty(key))
            {
                LogError("SaveTexture called with null or empty key");
                return false;
            }
            
            if (texture == null)
            {
                LogError($"SaveTexture called with null texture for key: {key}");
                return false;
            }
            
            if (!ValidateTexture(texture))
            {
                LogError($"Invalid texture for key: {key}");
                return false;
            }
            
            var sanitizedKey = CacheConstants.SanitizeKey(key);
            var startTime = DateTime.UtcNow;
            
            try
            {
                var success = SaveTextureInternal(sanitizedKey, texture);
                UpdateStatistics(success, startTime, isRead: false);
                
                if (success)
                {
                    LogInfo($"Successfully saved texture: {sanitizedKey}");
                }
                else
                {
                    LogWarning($"Failed to save texture: {sanitizedKey}");
                }
                
                return success;
            }
            catch (Exception e)
            {
                LogError($"Exception in SaveTexture({sanitizedKey}): {e.Message}");
                UpdateStatistics(false, startTime, isRead: false);
                return false;
            }
        }
        
        public Texture2D LoadTexture(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                LogError("LoadTexture called with null or empty key");
                return null;
            }
            
            var sanitizedKey = CacheConstants.SanitizeKey(key);
            var startTime = DateTime.UtcNow;
            
            try
            {
                var texture = LoadTextureInternal(sanitizedKey);
                var success = texture != null;
                UpdateStatistics(success, startTime, isRead: true);
                
                if (success)
                {
                    LogInfo($"Successfully loaded texture: {sanitizedKey}");
                }
                else
                {
                    LogInfo($"Texture not found: {sanitizedKey}");
                }
                
                return texture;
            }
            catch (Exception e)
            {
                LogError($"Exception in LoadTexture({sanitizedKey}): {e.Message}");
                UpdateStatistics(false, startTime, isRead: true);
                return null;
            }
        }
        
        public bool HasCache(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            
            var sanitizedKey = CacheConstants.SanitizeKey(key);
            
            try
            {
                return HasCacheInternal(sanitizedKey);
            }
            catch (Exception e)
            {
                LogError($"Exception in HasCache({sanitizedKey}): {e.Message}");
                return false;
            }
        }
        
        public void ClearCache(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            
            var sanitizedKey = CacheConstants.SanitizeKey(key);
            
            try
            {
                ClearCacheInternal(sanitizedKey);
                LogInfo($"Cache cleared: {sanitizedKey}");
            }
            catch (Exception e)
            {
                LogError($"Exception in ClearCache({sanitizedKey}): {e.Message}");
            }
        }
        
        public void ClearAllCache()
        {
            try
            {
                ClearAllCacheInternal();
                ResetStatistics();
                LogInfo("All cache cleared");
            }
            catch (Exception e)
            {
                LogError($"Exception in ClearAllCache: {e.Message}");
            }
        }
        
        public CacheStatistics GetStatistics()
        {
            lock (statisticsLock)
            {
                return statistics;
            }
        }
        #endregion
        
        #region Abstract Methods (Template Method Pattern)
        /// <summary>
        /// ストレージ初期化処理（各実装で定義）
        /// </summary>
        protected abstract void InitializeStorage();
        
        /// <summary>
        /// テクスチャ保存の内部実装
        /// </summary>
        protected abstract bool SaveTextureInternal(string key, Texture2D texture);
        
        /// <summary>
        /// テクスチャ読み込みの内部実装
        /// </summary>
        protected abstract Texture2D LoadTextureInternal(string key);
        
        /// <summary>
        /// キャッシュ存在確認の内部実装
        /// </summary>
        protected abstract bool HasCacheInternal(string key);
        
        /// <summary>
        /// 特定キャッシュ削除の内部実装
        /// </summary>
        protected abstract void ClearCacheInternal(string key);
        
        /// <summary>
        /// 全キャッシュ削除の内部実装
        /// </summary>
        protected abstract void ClearAllCacheInternal();
        #endregion
        
        #region Virtual Methods (Customizable)
        /// <summary>
        /// テクスチャ検証（サブクラスでオーバーライド可能）
        /// </summary>
        protected virtual bool ValidateTexture(Texture2D texture)
        {
            return texture != null && 
                   texture.width > 0 && 
                   texture.height > 0;
        }
        
        /// <summary>
        /// 追加検証ロジック（サブクラスで拡張可能）
        /// </summary>
        protected virtual bool ValidateKey(string key)
        {
            return !string.IsNullOrEmpty(key) && 
                   key.Length <= CacheConstants.MAX_CACHE_KEY_LENGTH;
        }
        #endregion
        
        #region Statistics Management
        private void UpdateStatistics(bool success, DateTime startTime, bool isRead)
        {
            var duration = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;
            
            lock (statisticsLock)
            {
                if (success)
                {
                    statistics.hitCount++;
                }
                else
                {
                    statistics.missCount++;
                }
                
                var totalOperations = statistics.hitCount + statistics.missCount;
                if (totalOperations > 0)
                {
                    statistics.hitRate = (float)statistics.hitCount / totalOperations;
                }
                
                // 移動平均で平均アクセス時間を更新
                if (statistics.averageAccessTime == 0)
                {
                    statistics.averageAccessTime = duration;
                }
                else
                {
                    statistics.averageAccessTime = (statistics.averageAccessTime * 0.9f) + (duration * 0.1f);
                }
            }
        }
        
        private void ResetStatistics()
        {
            lock (statisticsLock)
            {
                statistics = new CacheStatistics();
            }
        }
        #endregion
        
        #region Logging Helpers
        protected void LogInfo(string message)
        {
            if (CacheConstants.EnableDebugLogging)
            {
                logger?.LogInfo($"{CacheConstants.LOG_PREFIX} {GetType().Name}: {message}");
            }
        }
        
        protected void LogWarning(string message)
        {
            logger?.LogWarning($"{CacheConstants.LOG_PREFIX} {GetType().Name}: {message}");
        }
        
        protected void LogError(string message)
        {
            logger?.LogError($"{CacheConstants.LOG_PREFIX} {GetType().Name}: {message}");
        }
        #endregion
    }
    
    /// <summary>
    /// デフォルトログ実装（Unity.Debug使用）
    /// </summary>
    internal class DefaultLogger : ILogger
    {
        public void LogInfo(string message) => Debug.Log(message);
        public void LogWarning(string message) => Debug.LogWarning(message);
        public void LogError(string message) => Debug.LogError(message);
    }
    
    /// <summary>
    /// ログインターフェース
    /// </summary>
    public interface ILogger
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
    }
}