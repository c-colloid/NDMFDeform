using System;

namespace Deform.Masking.Core.Constants
{
    /// <summary>
    /// キャッシュシステム関連の定数定義
    /// パフォーマンス・容量・時間制限を一元管理
    /// </summary>
    public static class CacheConstants
    {
        #region ディレクトリパス
        /// <summary>メインキャッシュディレクトリ</summary>
        public const string CACHE_ROOT_DIRECTORY = "Library/UVIslandCache";
        
        /// <summary>バイナリキャッシュディレクトリ</summary>
        public const string BINARY_CACHE_DIRECTORY = "Library/UVIslandCacheBinary";
        
        /// <summary>最適化キャッシュディレクトリ</summary>
        public const string OPTIMAL_CACHE_DIRECTORY = "Library/UVIslandCacheOptimal";
        
        /// <summary>キャッシュインデックスファイル</summary>
        public const string CACHE_INDEX_FILE = "Library/UVIslandCache/.cache_index";
        #endregion
        
        #region サイズ・容量制限
        /// <summary>メモリキャッシュの最大エントリ数</summary>
        public const int MAX_MEMORY_CACHE_SIZE = 100;
        
        /// <summary>単一ファイルの最大サイズ（バイト）</summary>
        public const int MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024; // 10MB
        
        /// <summary>自動クリーンアップのトリガーサイズ（バイト）</summary>
        public const long AUTO_CLEANUP_TRIGGER_SIZE = 100 * 1024 * 1024; // 100MB
        
        /// <summary>キャッシュキーの最大長（ファイルシステム制限回避）</summary>
        public const int MAX_CACHE_KEY_LENGTH = 200;
        #endregion
        
        #region 時間設定
        /// <summary>キャッシュファイルの有効期限（日）</summary>
        public const int CACHE_EXPIRY_DAYS = 7;
        
        /// <summary>ヘルスチェック間隔（時間）</summary>
        public const double CACHE_HEALTH_CHECK_INTERVAL_HOURS = 1.0;
        
        /// <summary>定期メンテナンス間隔（時間）</summary>
        public const double PERIODIC_MAINTENANCE_INTERVAL_HOURS = 24.0;
        #endregion
        
        #region リトライ・エラー処理
        /// <summary>最大リトライ回数</summary>
        public const int MAX_RETRY_ATTEMPTS = 3;
        
        /// <summary>リトライ間隔（ミリ秒）</summary>
        public const int RETRY_DELAY_MS = 100;
        
        /// <summary>操作タイムアウト（ミリ秒）</summary>
        public const int OPERATION_TIMEOUT_MS = 5000;
        
        /// <summary>最大並行操作数</summary>
        public const int MAX_CONCURRENT_OPERATIONS = 4;
        #endregion
        
        #region パフォーマンス監視
        /// <summary>パフォーマンステストのイテレーション数</summary>
        public const int PERFORMANCE_TEST_ITERATIONS = 1000;
        
        /// <summary>低ヒット率の警告閾値</summary>
        public const float LOW_HIT_RATE_THRESHOLD = 0.5f;
        
        /// <summary>読み込み時間の警告閾値（ミリ秒）</summary>
        public const float SLOW_READ_TIME_THRESHOLD = 5.0f;
        
        /// <summary>最小統計サンプル数（信頼性確保）</summary>
        public const int MIN_STATISTICS_SAMPLE_COUNT = 10;
        #endregion
        
        #region バージョン管理
        /// <summary>現在のキャッシュフォーマットバージョン</summary>
        public const int CURRENT_CACHE_VERSION = 1;
        #endregion
        
        #region ログ設定
        /// <summary>キャッシュログのプレフィックス</summary>
        public const string LOG_PREFIX = "[UVIslandCache]";
        
        /// <summary>デバッグモード時のみログ出力</summary>
        public static bool EnableDebugLogging => UnityEngine.Debug.isDebugBuild;
        #endregion
        
        #region ヘルパーメソッド
        /// <summary>
        /// ファイルサイズを人間が読みやすい形式で取得
        /// </summary>
        /// <param name="bytes">バイト数</param>
        /// <returns>フォーマット済み文字列</returns>
        public static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:F1} {sizes[order]}";
        }
        
        /// <summary>
        /// 有効期限切れ判定
        /// </summary>
        /// <param name="timestamp">タイムスタンプ</param>
        /// <returns>期限切れの場合true</returns>
        public static bool IsExpired(DateTime timestamp)
        {
            return DateTime.UtcNow - timestamp > TimeSpan.FromDays(CACHE_EXPIRY_DAYS);
        }
        
        /// <summary>
        /// 安全なキャッシュキー生成（ファイルシステム制約対応）
        /// </summary>
        /// <param name="rawKey">生のキー</param>
        /// <returns>安全なキー</returns>
        public static string SanitizeKey(string rawKey)
        {
            if (string.IsNullOrEmpty(rawKey)) return "empty_key";
            
            // 危険な文字を置換
            var sanitized = rawKey
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(":", "_")
                .Replace("*", "_")
                .Replace("?", "_")
                .Replace("\"", "_")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace("|", "_");
            
            // 長さ制限
            if (sanitized.Length > MAX_CACHE_KEY_LENGTH)
            {
                sanitized = sanitized.Substring(0, MAX_CACHE_KEY_LENGTH);
            }
            
            return sanitized;
        }
        #endregion
    }
}