namespace ExDeform.Core.Constants
{
    /// <summary>
    /// UV関連の定数定義
    /// 全体で一元管理し、変更時の影響範囲を最小化
    /// </summary>
    public static class UVConstants
    {
        #region テクスチャサイズ
        /// <summary>エディタUI用UVマップサイズ（ピクセル）</summary>
        public const int UV_MAP_DISPLAY_SIZE = 300;
        
        /// <summary>標準解像度テクスチャサイズ</summary>
        public const int STANDARD_TEXTURE_SIZE = 512;
        
        /// <summary>低解像度キャッシュ用テクスチャサイズ</summary>
        public const int LOW_RES_TEXTURE_SIZE = 128;
        
        /// <summary>拡大鏡表示用テクスチャサイズ</summary>
        public const int MAGNIFYING_TEXTURE_SIZE = 256;
        #endregion
        
        #region パフォーマンス設定
        /// <summary>テクスチャ更新スロットル（秒）- 60FPS制限</summary>
        public const float TEXTURE_UPDATE_THROTTLE = 0.016f;
        
        /// <summary>UVハッシュ計算時の最大サンプル数</summary>
        public const int MAX_UV_HASH_SAMPLES = 100;
        
        /// <summary>並列処理バッチサイズ</summary>
        public const int JOB_BATCH_SIZE = 64;
        #endregion
        
        #region UI設定  
        /// <summary>拡大鏡のデフォルトサイズ（ピクセル）</summary>
        public const float DEFAULT_MAGNIFYING_SIZE = 100f;
        
        /// <summary>拡大鏡の最小サイズ</summary>
        public const float MIN_MAGNIFYING_SIZE = 80f;
        
        /// <summary>拡大鏡の最大サイズ</summary>
        public const float MAX_MAGNIFYING_SIZE = 150f;
        
        /// <summary>ズームの最小値</summary>
        public const float MIN_ZOOM_LEVEL = 1f;
        
        /// <summary>ズームの最大値</summary>
        public const float MAX_ZOOM_LEVEL = 8f;
        
        /// <summary>適応的頂点サイズの倍率範囲</summary>
        public const float MIN_ADAPTIVE_MULTIPLIER = 0.001f;
        public const float MAX_ADAPTIVE_MULTIPLIER = 0.02f;
        public const float DEFAULT_ADAPTIVE_MULTIPLIER = 0.007f;
        #endregion
        
        #region カラーテーマ
        /// <summary>選択されていないアイランドのデフォルト色</summary>
        public static readonly UnityEngine.Color DEFAULT_ISLAND_COLOR = UnityEngine.Color.gray;
        
        /// <summary>選択範囲オーバーレイの色</summary>
        public static readonly UnityEngine.Color SELECTION_OVERLAY_COLOR = new UnityEngine.Color(0.3f, 0.5f, 0.8f, 0.3f);
        
        /// <summary>選択範囲境界線の色</summary>
        public static readonly UnityEngine.Color SELECTION_BORDER_COLOR = new UnityEngine.Color(0.3f, 0.5f, 0.8f, 0.8f);
        
        /// <summary>拡大鏡の十字線色</summary>
        public static readonly UnityEngine.Color MAGNIFYING_RETICLE_COLOR = UnityEngine.Color.red;
        #endregion
    }
}