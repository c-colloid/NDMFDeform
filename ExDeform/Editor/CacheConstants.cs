using ExDeform.Core.Constants;

namespace ExDeform.Editor
{
    /// <summary>
    /// Editor-specific constants for cache system testing and UI
    /// エディター固有のキャッシュシステムテストとUI用定数
    /// </summary>
    public static class CacheConstants
    {
        #region Performance Test Settings
        /// <summary>テスト用テクスチャサイズ</summary>
        public const int TEST_TEXTURE_SIZE = 128;
        
        /// <summary>パフォーマンステストで使用するテクスチャフォーマット</summary>
        public const UnityEngine.TextureFormat TEST_TEXTURE_FORMAT = UnityEngine.TextureFormat.RGBA32;
        
        /// <summary>パフォーマンステスト反復回数</summary>
        public const int PERFORMANCE_TEST_ITERATIONS = 100;
        #endregion
        
        #region Editor-specific Cache Settings
        /// <summary>EditorPrefsキャッシュのプレフィックス</summary>
        public const string EDITOR_PREFS_PREFIX = "UVCache_";
        
        /// <summary>EditorPrefsメタデータのサフィックス</summary>
        public const string EDITOR_PREFS_META_SUFFIX = "_meta";
        
        /// <summary>キャッシュファイル拡張子</summary>
        public const string CACHE_JSON_EXTENSION = ".json";
        public const string CACHE_PNG_EXTENSION = ".png";
        public const string CACHE_META_EXTENSION = ".meta";
        
        /// <summary>キャッシュルートディレクトリ</summary>
        public const string CACHE_ROOT_DIRECTORY = "Library/ExDeformCache";
        
        /// <summary>バイナリキャッシュディレクトリ</summary>
        public const string BINARY_CACHE_DIRECTORY = "Library/ExDeformCache/Binary";
        #endregion
        
        #region Size and Performance Limits
        /// <summary>Base64エンコーディングのオーバーヘッド係数</summary>
        public const float BASE64_OVERHEAD_FACTOR = 1.37f;
        
        /// <summary>メタデータ分離文字</summary>
        public const char METADATA_SEPARATOR = '|';
        
        /// <summary>デフォルト画像品質（PNG圧縮なし）</summary>
        public const int DEFAULT_IMAGE_QUALITY = 100;
        #endregion
        
        #region Test Result Format
        /// <summary>パフォーマンス結果の表示精度（小数点以下桁数）</summary>
        public const int PERFORMANCE_DECIMAL_PLACES = 3;
        
        /// <summary>メモリ使用量表示精度</summary>
        public const int MEMORY_DECIMAL_PLACES = 1;
        
        /// <summary>KBへの変換係数</summary>
        public const float BYTES_TO_KB = 1024f;
        #endregion
    }
}