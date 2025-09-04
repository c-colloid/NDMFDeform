using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using ExDeform.Core.Interfaces;
using ExDeform.Core.Extensions;
using ExDeform.Runtime.Cache;
using ExDeform.Runtime.Data;
// using ExDeform.Editor; // Runtime cannot reference Editor
using ExDeform.Runtime;

namespace ExDeform.Runtime.Deformers
{
    /// <summary>
    /// UV Island based mask for deformation - ExDeform拡張版
    /// 外部Deform拡張との完全互換性を保ちつつモジュール化
    /// </summary>
    [System.Serializable]
    public class UVIslandMask : MonoBehaviour, IExDeformer
    {
        #region IExDeformer プロパティ
        public string DeformerName => "UV Island Mask";
        public DeformerCategory Category => DeformerCategory.Mask;
        public string Description => "Masks deformation based on UV island selection";
        public System.Version CompatibleDeformVersion => new System.Version(1, 0, 0);
        public bool IsVisibleInEditor => true;
        public bool IsEnabledInRuntime => enabled && HasValidConfiguration();
        #endregion
        
        #region シリアライズフィールド（後方互換性維持）
        [Header("Mask Configuration")]
        [SerializeField] private List<int> selectedIslandIDs = new List<int>();
        [SerializeField] private bool invertMask = false;
        [SerializeField, Range(0f, 1f)] private float maskStrength = 1f;
        [SerializeField, Range(0f, 0.1f)] private float featherRadius = 0.01f;
        
        [Header("Performance Settings")]
        [SerializeField] private bool enableCaching = true;
        [SerializeField] private bool useJobSystem = true;
        [SerializeField] private bool useBurstCompilation = true;
        
        [Header("Editor Settings")]
        [SerializeField, Range(128, 1024)] private int editorTextureResolution = 512;
        [SerializeField] private bool showSelectionInSceneView = true;
        [SerializeField] private bool debugMode = false;
        #endregion
        
        #region ランタイムデータ
        [System.NonSerialized] private NativeArray<float> maskValues;
        [System.NonSerialized] private bool maskDataReady = false;
        [System.NonSerialized] private bool isDisposing = false;
        [System.NonSerialized] private Mesh cachedMesh;
        [System.NonSerialized] private Mesh originalMesh;
        
        // モジュール化されたコンポーネント (Editor-only classes disabled in Runtime)
        // [System.NonSerialized] private UVCacheManager cacheManager;
        // [System.NonSerialized] private UVMaskProcessor maskProcessor;
        // UVIslandAnalyzer is a static utility class
        
        // 外部Deform統合
        [System.NonSerialized] private object externalDeformable;
        [System.NonSerialized] private object currentMeshData;
        [System.NonSerialized] private Transform cachedRendererTransform;
        
        #if EXDEFORM_DEFORM_AVAILABLE
        [SerializeField] private Deform.Deformable targetDeformable;
        #endif
        #endregion
        
        #region 公開プロパティ（後方互換性）
        public List<int> SelectedIslandIDs => selectedIslandIDs;
        public bool InvertMask { get => invertMask; set => invertMask = value; }
        public float MaskStrength { get => maskStrength; set => maskStrength = Mathf.Clamp01(value); }
        public float FeatherRadius { get => featherRadius; set => featherRadius = Mathf.Clamp(value, 0f, 0.1f); }
        public Mesh CachedMesh => cachedMesh;
        public Mesh OriginalMesh => originalMesh;
        
        // ExDeform固有プロパティ
        public bool EnableCaching { get => enableCaching; set => enableCaching = value; }
        public bool UseJobSystem { get => useJobSystem; set => useJobSystem = value; }
        public bool UseBurstCompilation { get => useBurstCompilation; set => useBurstCompilation = value; }
        public int EditorTextureResolution => editorTextureResolution;
        
        // エディター用プロパティ
        public Transform CachedRendererTransform 
        { 
            get => cachedRendererTransform; 
            set => cachedRendererTransform = value; 
        }
        
        #if EXDEFORM_DEFORM_AVAILABLE
        public Deform.Deformable TargetDeformable => targetDeformable;
        #else
        public object TargetDeformable => null;
        #endif
        #endregion
        
        #region Unity ライフサイクル
        private void Awake()
        {
            InitializeModules();
        }
        
        private void OnEnable()
        {
            TryIntegrateWithExternalDeform();
        }
        
        private void OnDisable()
        {
            if (!isDisposing)
            {
                DisposeMaskValues();
            }
        }
        
        private void OnDestroy()
        {
            Cleanup();
        }
        #endregion
        
        #region IExDeformer 実装
        public bool Initialize(object deformable)
        {
            externalDeformable = deformable;
            InitializeModules();
            return HasValidConfiguration();
        }
        
        public JobHandle ProcessMesh(object meshData, JobHandle dependency)
        {
            currentMeshData = meshData;
            
            // 外部MeshDataから情報を抽出
            var vertexCount = meshData.GetVertexCount();
            var originalMesh = meshData.GetOriginalMesh();
            
            if (vertexCount == 0 || originalMesh == null)
            {
                return dependency; // パススルー
            }
            
            // マスクデータの更新確認
            if (!maskDataReady || maskValues.Length != vertexCount || originalMesh != this.originalMesh)
            {
                UpdateMaskData(originalMesh, vertexCount);
            }
            
            if (!maskValues.IsCreated || maskValues.Length == 0)
            {
                return dependency; // マスクデータなし、パススルー
            }
            
            // Job実行（外部MeshDataと統合）
            // Note: maskProcessor is Editor-only, using simple pass-through in Runtime
            // TODO: Implement runtime-compatible mask processing
            return dependency; // Pass-through for now
        }
        
        public void Cleanup()
        {
            if (isDisposing) return;
            
            isDisposing = true;
            
            DisposeMaskValues();
            // Editor-only classes disabled in Runtime
            // cacheManager?.Cleanup();
            // maskProcessor?.Cleanup();
        }
        #endregion
        
        #region 内部実装
        private void InitializeModules()
        {
            // Editor-only modules disabled in Runtime
            // if (cacheManager == null && enableCaching)
            // {
            //     cacheManager = new UVCacheManager();
            // }
            // 
            // if (maskProcessor == null)
            // {
            //     maskProcessor = new UVMaskProcessor(useJobSystem, useBurstCompilation);
            // }
            
            // UVIslandAnalyzer is a static utility class - no instantiation needed
        }
        
        private void TryIntegrateWithExternalDeform()
        {
            if (!DeformExtensions.IsDeformAvailable())
            {
                if (debugMode)
                {
                    Debug.Log("[UVIslandMask] 外部Deform拡張が見つかりません。スタンドアローンモードで動作します。");
                }
                return;
            }
            
            // 外部Deformableを検索・統合
            var deformable = GetComponent("Deformable") as Component; // 動的型解決
            if (deformable != null)
            {
                externalDeformable = deformable;
                deformable.AddExDeformer(this); // 拡張メソッドで追加
                
                if (debugMode)
                {
                    Debug.Log($"[UVIslandMask] 外部Deform拡張と統合しました。バージョン: {DeformExtensions.GetDeformVersion()}");
                }
            }
        }
        
        private void UpdateMaskData(Mesh mesh, int vertexCount)
        {
            // 既存マスクデータの安全な破棄
            DisposeMaskValues();
            
            if (isDisposing) return;
            
            // 新しいマスクデータ作成
            maskValues = new NativeArray<float>(vertexCount, Allocator.Persistent);
            
            // 全頂点を初期状態に設定（1.0 = 変形無効）
            for (int i = 0; i < maskValues.Length; i++)
            {
                maskValues[i] = 1f;
            }
            
            // 選択されたアイランドの処理
            if (mesh != null && selectedIslandIDs.Count > 0 && mesh.uv != null && mesh.uv.Length > 0)
            {
                ProcessSelectedIslands(mesh);
            }
            
            originalMesh = mesh;
            maskDataReady = true;
        }
        
        private void ProcessSelectedIslands(Mesh mesh)
        {
            try
            {
                // キャッシュから島データを取得または解析 (Editor-only cache disabled)
                // var islands = cacheManager?.GetCachedIslands(mesh) ?? 
                //              UVIslandAnalyzer.AnalyzeUVIslands(mesh);
                var islands = UVIslandAnalyzer.AnalyzeUVIslands(mesh);
                
                // 選択された島の頂点を変形許可に設定
                foreach (var islandID in selectedIslandIDs)
                {
                    var island = islands.Find(i => i.islandID == islandID);
                    if (island != null && island.vertexIndices != null)
                    {
                        foreach (var vertexIndex in island.vertexIndices)
                        {
                            if (vertexIndex < maskValues.Length)
                            {
                                maskValues[vertexIndex] = 0f; // 変形許可
                            }
                        }
                    }
                }
                
                // キャッシュに保存（有効な場合） - Editor-only cache disabled
                // cacheManager?.CacheIslands(mesh, islands);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UVIslandMask] アイランド処理エラー: {e.Message}");
            }
        }
        
        private void DisposeMaskValues()
        {
            if (maskValues.IsCreated && !isDisposing)
            {
                try
                {
                    maskValues.Dispose();
                }
                catch (System.ObjectDisposedException)
                {
                    // 既に破棄済み、無視
                }
                catch (System.InvalidOperationException)
                {
                    // 無効な状態、無視
                }
            }
            
            maskDataReady = false;
        }
        
        private bool HasValidConfiguration()
        {
            return selectedIslandIDs != null && 
                   selectedIslandIDs.Count > 0 && 
                   maskStrength > 0f;
        }
        #endregion
        
        #region 公開メソッド（外部統合用）
        /// <summary>
        /// アイランド選択の更新（エディタ用）
        /// </summary>
        public void SetSelectedIslands(List<int> islandIDs)
        {
            selectedIslandIDs = islandIDs ?? new List<int>();
            maskDataReady = false; // 再計算をトリガー
        }
        
        /// <summary>
        /// キャッシュクリア（メッシュ変更時用）
        /// </summary>
        public void InvalidateCache()
        {
            maskDataReady = false;
            // Editor-only cache disabled
            // cacheManager?.ClearCache();
        }
        
        /// <summary>
        /// デバッグ情報の取得
        /// </summary>
        public string GetDebugInfo()
        {
            return $"UVIslandMask: Islands={selectedIslandIDs?.Count ?? 0}, " +
                   $"MaskReady={maskDataReady}, " +
                   $"ExternalDeform={DeformExtensions.IsDeformAvailable()}, " +
                   $"CacheEnabled={enableCaching}";
        }
        
        /// <summary>
        /// レンダラーキャッシュの更新（エディター用）
        /// </summary>
        public void UpdateRendererCache()
        {
            if (cachedRendererTransform == null)
            {
                var renderer = GetComponent<Renderer>();
                cachedRendererTransform = renderer?.transform;
            }
        }
        #endregion
    }
}