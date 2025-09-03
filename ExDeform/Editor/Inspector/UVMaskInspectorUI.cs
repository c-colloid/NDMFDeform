using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using Deform.Masking.Data;
using Deform.Masking.Cache.Interfaces;

namespace Deform.Masking.Editor.Inspector
{
    /// <summary>
    /// UVIslandMask専用インスペクターUI
    /// Deformエディタワークフローとの統合を重視した設計
    /// </summary>
    public class UVMaskInspectorUI
    {
        #region Private Fields
        private readonly UVIslandMask targetMask;
        private readonly SerializedObject serializedObject;
        private readonly IUVCache uvCache;
        
        private VisualElement rootElement;
        private UVMapRenderer uvMapRenderer;
        private IslandSelector islandSelector;
        
        // Deform統合用
        private PropertyField deformableField;
        private PropertyField executionOrderField;
        #endregion
        
        #region Constructor
        public UVMaskInspectorUI(UVIslandMask target, SerializedObject serializedObject, IUVCache uvCache = null)
        {
            this.targetMask = target;
            this.serializedObject = serializedObject;
            this.uvCache = uvCache ?? new DefaultUVCache();
            
            InitializeComponents();
        }
        #endregion
        
        #region Public Methods
        /// <summary>
        /// Deformエディタ統合用のUIを作成
        /// 既存のDeformerインスペクターパターンに準拠
        /// </summary>
        public VisualElement CreateInspectorUI()
        {
            rootElement = new VisualElement();
            rootElement.style.paddingTop = 10;
            rootElement.style.paddingBottom = 10;
            rootElement.style.paddingLeft = 10;
            rootElement.style.paddingRight = 10;
            
            CreateDeformIntegrationSection();
            CreateMaskConfigurationSection();
            CreateUVVisualizationSection();
            CreatePerformanceSection();
            
            RefreshUI();
            
            return rootElement;
        }
        
        /// <summary>
        /// Deformerチェーン変更時の更新
        /// </summary>
        public void OnDeformerChainChanged()
        {
            RefreshUI();
            uvMapRenderer?.RefreshTexture();
        }
        
        /// <summary>
        /// メッシュ変更時の更新（Deformable経由）
        /// </summary>
        public void OnMeshChanged()
        {
            uvCache?.InvalidateCache(GetMeshKey());
            islandSelector?.RefreshIslandData();
            uvMapRenderer?.RefreshTexture();
        }
        #endregion
        
        #region Private Methods - UI Creation
        private void CreateDeformIntegrationSection()
        {
            var deformSection = CreateSection("Deform Integration");
            
            // Deformable参照（必須）
            deformableField = new PropertyField(serializedObject.FindProperty("targetDeformable"));
            deformableField.label = "Target Deformable";
            deformableField.tooltip = "このマスクが適用されるDeformableコンポーネント";
            deformableField.RegisterValueChangeCallback(OnDeformableChanged);
            
            // 実行順序
            executionOrderField = new PropertyField(serializedObject.FindProperty("executionOrder"));
            executionOrderField.label = "Execution Order";
            executionOrderField.tooltip = "Deformerチェーン内での処理順序";
            
            // 有効/無効切り替え
            var enabledToggle = new PropertyField(serializedObject.FindProperty("enabled"));
            enabledToggle.label = "Enabled";
            enabledToggle.tooltip = "Deformerチェーン内での有効状態";
            
            deformSection.Add(deformableField);
            deformSection.Add(executionOrderField);
            deformSection.Add(enabledToggle);
            rootElement.Add(deformSection);
        }
        
        private void CreateMaskConfigurationSection()
        {
            var maskSection = CreateSection("Mask Configuration");
            
            // マスク強度
            var strengthSlider = new PropertyField(serializedObject.FindProperty("maskStrength"));
            strengthSlider.label = "Mask Strength";
            
            // マスク反転
            var invertToggle = new PropertyField(serializedObject.FindProperty("invertMask"));
            invertToggle.label = "Invert Mask";
            
            // フェザー半径（新機能）
            var featherSlider = new PropertyField(serializedObject.FindProperty("featherRadius"));
            featherSlider.label = "Feather Radius";
            
            maskSection.Add(strengthSlider);
            maskSection.Add(invertToggle);
            maskSection.Add(featherSlider);
            rootElement.Add(maskSection);
        }
        
        private void CreateUVVisualizationSection()
        {
            var uvSection = CreateSection("UV Island Selection");
            
            // UVマップレンダラー
            uvMapRenderer = new UVMapRenderer(targetMask, uvCache);
            var uvMapElement = uvMapRenderer.CreateMapElement();
            
            // アイランドセレクター
            islandSelector = new IslandSelector(targetMask, uvCache);
            var selectorElement = islandSelector.CreateSelectorElement();
            
            uvSection.Add(uvMapElement);
            uvSection.Add(selectorElement);
            rootElement.Add(uvSection);
        }
        
        private void CreatePerformanceSection()
        {
            var perfSection = CreateSection("Performance");
            
            // キャッシュ有効/無効
            var cacheToggle = new PropertyField(serializedObject.FindProperty("enableCaching"));
            cacheToggle.label = "Enable Caching";
            
            // Job System使用
            var jobToggle = new PropertyField(serializedObject.FindProperty("useJobSystem"));
            jobToggle.label = "Use Job System";
            
            // Burst使用
            var burstToggle = new PropertyField(serializedObject.FindProperty("useBurstCompilation"));
            burstToggle.label = "Use Burst Compilation";
            
            perfSection.Add(cacheToggle);
            perfSection.Add(jobToggle);
            perfSection.Add(burstToggle);
            rootElement.Add(perfSection);
        }
        
        private VisualElement CreateSection(string title)
        {
            var section = new VisualElement();
            section.style.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.3f);
            section.style.paddingTop = 10;
            section.style.paddingBottom = 10;
            section.style.paddingLeft = 10;
            section.style.paddingRight = 10;
            section.style.marginBottom = 15;
            
            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 14;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.marginBottom = 10;
            section.Add(titleLabel);
            
            return section;
        }
        #endregion
        
        #region Private Methods - Event Handlers
        private void OnDeformableChanged(SerializedPropertyChangeEvent evt)
        {
            var newDeformable = evt.changedProperty.objectReferenceValue as Deformable;
            
            if (newDeformable != null)
            {
                // 新しいDeformableのメッシュに基づいてキャッシュを無効化
                OnMeshChanged();
                
                // Deformerチェーンに追加（必要に応じて）
                EnsureDeformerInChain(newDeformable);
            }
            
            RefreshUI();
        }
        
        private void EnsureDeformerInChain(Deformable deformable)
        {
            // DeformableのDeformerチェーンにUVIslandMaskが含まれていない場合は追加
            var deformers = deformable.GetDeformers();
            bool containsThisMask = System.Array.Exists(deformers, d => d == targetMask);
            
            if (!containsThisMask)
            {
                // Deformableにこのマスクを自動追加（ユーザー確認付き）
                if (EditorUtility.DisplayDialog(
                    "Add to Deformer Chain",
                    $"Add this UV Island Mask to the Deformer chain of '{deformable.name}'?",
                    "Add", "Cancel"))
                {
                    deformable.AddDeformer(targetMask);
                }
            }
        }
        #endregion
        
        #region Private Methods - Utilities
        private void InitializeComponents()
        {
            // コンポーネント初期化
        }
        
        private void RefreshUI()
        {
            // UI状態更新
            serializedObject.ApplyModifiedProperties();
        }
        
        private string GetMeshKey()
        {
            var deformable = targetMask.targetDeformable;
            if (deformable?.GetMesh() != null)
            {
                var mesh = deformable.GetMesh();
                return $"{mesh.name}_{mesh.GetInstanceID()}_{mesh.vertexCount}";
            }
            return "invalid_mesh";
        }
        #endregion
    }
    
    /// <summary>
    /// デフォルトUVキャッシュ実装（フォールバック用）
    /// </summary>
    internal class DefaultUVCache : IUVCache
    {
        public bool CacheUVData(string meshKey, Texture2D uvTexture, UVIslandData[] islandData, int[] selectedIslands) => false;
        public UVCacheData LoadUVData(string meshKey) => default;
        public Texture2D GetPreviewTexture(string meshKey, int resolution = 128) => null;
        public bool IsValidCache(string meshKey, int meshHash) => false;
        public void InvalidateCache(string meshKey) { }
        public void OptimizeMemoryUsage() { }
    }
}