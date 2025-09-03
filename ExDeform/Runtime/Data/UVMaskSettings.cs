using UnityEngine;
using Unity.Collections;

namespace ExDeform.Runtime.Data
{
    /// <summary>
    /// UVIslandMask設定データ
    /// Deformエコシステムとの統合を考慮した設計
    /// </summary>
    [System.Serializable]
    public class UVMaskSettings
    {
        #region Deform統合設定
        [Header("Deform Integration")]
        [Tooltip("このマスクが適用されるDeformableコンポーネント")]
        public Deformable targetDeformable;
        
        [Tooltip("他のDeformerとの処理優先順位")]
        [Range(-100, 100)]
        public int executionOrder = 0;
        
        [Tooltip("Deformerチェーン内での有効/無効状態")]
        public bool enabled = true;
        #endregion
        
        #region マスク基本設定
        [Header("Mask Configuration")]
        [Tooltip("選択されたUVアイランドのID配列")]
        public int[] selectedIslandIDs = new int[0];
        
        [Tooltip("マスクを反転（選択範囲を除外）")]
        public bool invertMask = false;
        
        [Tooltip("マスクの強度（0=変形無効、1=完全マスク）")]
        [Range(0f, 1f)]
        public float maskStrength = 1f;
        
        [Tooltip("マスクの境界をぼかす")]
        [Range(0f, 0.1f)]
        public float featherRadius = 0.01f;
        #endregion
        
        #region パフォーマンス設定
        [Header("Performance")]
        [Tooltip("UV解析結果をキャッシュ（メモリ vs 計算時間）")]
        public bool enableCaching = true;
        
        [Tooltip("並列処理を使用（大量頂点メッシュでの高速化）")]
        public bool useJobSystem = true;
        
        [Tooltip("Burstコンパイラを使用（さらなる高速化）")]
        public bool useBurstCompilation = true;
        
        [Tooltip("メッシュ変更時の自動更新")]
        public CacheUpdateMode updateMode = CacheUpdateMode.Auto;
        #endregion
        
        #region エディタ専用設定
        [Header("Editor Only")]
        [Tooltip("エディタでのUVマップ表示解像度")]
        [Range(128, 1024)]
        public int editorTextureResolution = 512;
        
        [Tooltip("選択状態の可視化")]
        public bool showSelectionInSceneView = true;
        
        [Tooltip("アイランド境界線の表示")]
        public bool showIslandBorders = true;
        
        [Tooltip("UV座標のデバッグ表示")]
        public bool debugUVCoordinates = false;
        #endregion
        
        #region 内部状態（シリアライズ対象外）
        [System.NonSerialized]
        private UVIslandData[] cachedIslands;
        
        [System.NonSerialized] 
        private int cachedMeshHash = -1;
        
        [System.NonSerialized]
        private bool isDirty = true;
        #endregion
        
        #region プロパティ
        /// <summary>
        /// キャッシュされたアイランドデータ
        /// </summary>
        public UVIslandData[] CachedIslands 
        { 
            get => cachedIslands; 
            internal set => cachedIslands = value; 
        }
        
        /// <summary>
        /// 設定変更フラグ
        /// </summary>
        public bool IsDirty 
        { 
            get => isDirty; 
            internal set => isDirty = value; 
        }
        
        /// <summary>
        /// 有効なアイランドが選択されているか
        /// </summary>
        public bool HasValidSelection => selectedIslandIDs != null && selectedIslandIDs.Length > 0;
        
        /// <summary>
        /// Deformer機能として有効か
        /// </summary>
        public bool IsActive => enabled && HasValidSelection && targetDeformable != null;
        #endregion
        
        #region メソッド
        /// <summary>
        /// 設定の検証とDeformerとの整合性確認
        /// </summary>
        public bool ValidateSettings()
        {
            // Deformable参照の確認
            if (targetDeformable == null)
            {
                Debug.LogWarning("[UVMaskSettings] Target Deformable is not assigned");
                return false;
            }
            
            // メッシュの存在確認
            var mesh = targetDeformable.GetMesh();
            if (mesh == null)
            {
                Debug.LogWarning("[UVMaskSettings] Target mesh is null");
                return false;
            }
            
            // UV座標の存在確認
            if (mesh.uv == null || mesh.uv.Length == 0)
            {
                Debug.LogWarning("[UVMaskSettings] Target mesh has no UV coordinates");
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// アイランド選択状態の更新
        /// Deformerの変更通知と連動
        /// </summary>
        /// <param name="islandIDs">新しい選択アイランドID</param>
        public void UpdateSelection(int[] islandIDs)
        {
            selectedIslandIDs = islandIDs ?? new int[0];
            isDirty = true;
            
            // Deformable側に変更を通知
            if (targetDeformable != null)
            {
                targetDeformable.SetDirty();
            }
        }
        
        /// <summary>
        /// キャッシュの強制無効化
        /// メッシュ変更時の確実な更新
        /// </summary>
        public void InvalidateCache()
        {
            cachedIslands = null;
            cachedMeshHash = -1;
            isDirty = true;
        }
        
        /// <summary>
        /// Deformer設定の複製
        /// プレハブやインスタンス間での設定共有
        /// </summary>
        public UVMaskSettings Clone()
        {
            var clone = new UVMaskSettings
            {
                targetDeformable = targetDeformable,
                executionOrder = executionOrder,
                enabled = enabled,
                selectedIslandIDs = (int[])selectedIslandIDs.Clone(),
                invertMask = invertMask,
                maskStrength = maskStrength,
                featherRadius = featherRadius,
                enableCaching = enableCaching,
                useJobSystem = useJobSystem,
                useBurstCompilation = useBurstCompilation,
                updateMode = updateMode,
                editorTextureResolution = editorTextureResolution,
                showSelectionInSceneView = showSelectionInSceneView,
                showIslandBorders = showIslandBorders,
                debugUVCoordinates = debugUVCoordinates
            };
            
            return clone;
        }
        #endregion
    }
    
    /// <summary>
    /// キャッシュ更新モード
    /// Deformワークフローに適した更新戦略
    /// </summary>
    public enum CacheUpdateMode
    {
        /// <summary>自動更新（推奨）</summary>
        Auto = 0,
        
        /// <summary>手動更新のみ</summary>
        Manual = 1,
        
        /// <summary>再生時のみ更新</summary>
        PlayModeOnly = 2,
        
        /// <summary>更新無効</summary>
        Disabled = 3
    }
}