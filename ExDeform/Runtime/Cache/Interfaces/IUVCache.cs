using UnityEngine;
using Unity.Collections;

namespace ExDeform.Runtime.Cache.Interfaces
{
    /// <summary>
    /// UV専用キャッシュインターフェース
    /// Deformerとしての特性を活かした最適化されたキャッシュ機能
    /// </summary>
    public interface IUVCache
    {
        /// <summary>
        /// UVテクスチャとメタデータを一括保存
        /// Deformerの状態変更と連動した効率的なキャッシング
        /// </summary>
        /// <param name="meshKey">メッシュ識別キー（Deformer由来）</param>
        /// <param name="uvTexture">UVマップテクスチャ</param>
        /// <param name="islandData">アイランド情報</param>
        /// <param name="selectedIslands">選択済みアイランドID</param>
        /// <returns>保存成功時true</returns>
        bool CacheUVData(string meshKey, Texture2D uvTexture, UVIslandData[] islandData, int[] selectedIslands);
        
        /// <summary>
        /// UV関連データの高速読み込み
        /// Deformable初期化時の最適化された復元
        /// </summary>
        /// <param name="meshKey">メッシュ識別キー</param>
        /// <returns>キャッシュデータ、存在しない場合null</returns>
        UVCacheData LoadUVData(string meshKey);
        
        /// <summary>
        /// 低解像度プレビュー用テクスチャの取得
        /// エディタUI表示用の軽量版データ
        /// </summary>
        /// <param name="meshKey">メッシュ識別キー</param>
        /// <param name="resolution">要求解像度</param>
        /// <returns>プレビューテクスチャ</returns>
        Texture2D GetPreviewTexture(string meshKey, int resolution = 128);
        
        /// <summary>
        /// Deformer固有の高速存在確認
        /// MeshDataの変更検出との連携
        /// </summary>
        /// <param name="meshKey">メッシュ識別キー</param>
        /// <param name="meshHash">現在のメッシュハッシュ</param>
        /// <returns>有効なキャッシュが存在する場合true</returns>
        bool IsValidCache(string meshKey, int meshHash);
        
        /// <summary>
        /// Deformer無効化時のクリーンアップ
        /// </summary>
        /// <param name="meshKey">削除対象キー</param>
        void InvalidateCache(string meshKey);
        
        /// <summary>
        /// メモリ使用量の最適化
        /// Deformエディタ終了時の自動クリーンアップ
        /// </summary>
        void OptimizeMemoryUsage();
    }
    
    /// <summary>
    /// UVキャッシュデータ構造
    /// Deformerでの効率的な復元を考慮した設計
    /// </summary>
    [System.Serializable]
    public struct UVCacheData
    {
        public Texture2D uvTexture;           // フル解像度UVマップ
        public Texture2D previewTexture;      // プレビュー用低解像度版
        public UVIslandData[] islands;        // アイランド情報配列
        public int[] selectedIslandIDs;       // 選択済みアイランド
        public int meshHash;                  // メッシュハッシュ（整合性確認用）
        public long timestamp;                // キャッシュ作成時刻
        public float zoomLevel;               // 保存時のズームレベル
        public Vector2 panOffset;             // 保存時のパン位置
        
        public bool IsValid => uvTexture != null && islands != null && islands.Length > 0;
    }
    
    /// <summary>
    /// UVアイランド情報
    /// メッシュ変形処理との統合を考慮
    /// </summary>
    [System.Serializable] 
    public struct UVIslandData
    {
        public int islandID;                  // アイランド識別子
        public Vector2[] uvCoordinates;       // UV座標配列
        public int[] vertexIndices;           // 頂点インデックス
        public int[] triangleIndices;         // 三角形インデックス
        public Color maskColor;               // 表示用カラー
        public Bounds uvBounds;               // UV空間での境界
        public int faceCount;                 // 面数
        public bool isSelected;               // 選択状態
        
        public bool IsValid => vertexIndices != null && vertexIndices.Length > 0;
    }
}