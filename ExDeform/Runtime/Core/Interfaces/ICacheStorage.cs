using UnityEngine;

namespace ExDeform.Runtime.Core.Interfaces
{
    /// <summary>
    /// キャッシュストレージの統一インターフェース
    /// 異なる実装（EditorPrefs, File, ScriptableObject）を統一
    /// </summary>
    public interface ICacheStorage
    {
        /// <summary>
        /// テクスチャをキャッシュに保存
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <param name="texture">保存するテクスチャ</param>
        /// <returns>成功時true</returns>
        bool SaveTexture(string key, Texture2D texture);
        
        /// <summary>
        /// キャッシュからテクスチャを読み込み
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <returns>テクスチャ、存在しない場合null</returns>
        Texture2D LoadTexture(string key);
        
        /// <summary>
        /// キャッシュの存在確認（高速）
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <returns>存在する場合true</returns>
        bool HasCache(string key);
        
        /// <summary>
        /// 特定のキャッシュエントリを削除
        /// </summary>
        /// <param name="key">削除するキー</param>
        void ClearCache(string key);
        
        /// <summary>
        /// 全キャッシュをクリア
        /// </summary>
        void ClearAllCache();
        
        /// <summary>
        /// キャッシュ統計情報取得（パフォーマンス監視用）
        /// </summary>
        /// <returns>統計情報</returns>
        CacheStatistics GetStatistics();
    }
    
    /// <summary>
    /// キャッシュ統計情報
    /// </summary>
    [System.Serializable]
    public struct CacheStatistics
    {
        public int entryCount;
        public long totalSizeBytes;
        public float hitRate;
        public float averageAccessTime;
        
        public override string ToString()
        {
            return $"Entries: {entryCount}, Size: {totalSizeBytes / 1024f:F1}KB, " +
                   $"Hit Rate: {hitRate:P1}, Avg Access: {averageAccessTime:F2}ms";
        }
    }
}