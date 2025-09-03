using UnityEngine;

namespace ExDeform.Editor
{
    /// <summary>
    /// Common interface for all caching implementations
    /// 全キャッシュ実装の共通インターフェース
    /// </summary>
    public interface ICacheStorage
    {
        /// <summary>
        /// Save texture to cache
        /// テクスチャをキャッシュに保存
        /// </summary>
        void SaveTexture(string key, Texture2D texture);
        
        /// <summary>
        /// Load texture from cache
        /// キャッシュからテクスチャを読み込み
        /// </summary>
        Texture2D LoadTexture(string key);
        
        /// <summary>
        /// Check if cache exists
        /// キャッシュの存在確認
        /// </summary>
        bool HasCache(string key);
        
        /// <summary>
        /// Clear specific cache entry
        /// 特定のキャッシュエントリをクリア
        /// </summary>
        void ClearCache(string key);
        
        /// <summary>
        /// Get cache type name for logging/debugging
        /// ログ・デバッグ用のキャッシュ種別名取得
        /// </summary>
        string CacheTypeName { get; }
    }
}