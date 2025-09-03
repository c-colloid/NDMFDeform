using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;

namespace ExDeform.Editor
{
    /// <summary>
    /// Texture generation and management for UV Island editor
    /// UVアイランドエディタのテクスチャ生成と管理
    /// </summary>
    public class UVIslandTextureManager
    {
        #region Private Fields
        
        // Texture generation control
        private bool textureInitialized = false;
        private float lastUpdateTime = 0f;
        private const float TEXTURE_UPDATE_THROTTLE = 0.016f; // ~60fps limit
        
        // Robust caching system integration
        private Texture2D currentLowResTexture;
        private bool isLoadingFromCache = false;
        private bool shouldShowLowResUntilInteraction = false; // Flag to show low-res until user interaction
        
        // Magnifying glass texture
        private Texture2D magnifyingGlassTexture;
        
        // EditorApplication callback management
        private EditorApplication.CallbackFunction pendingTextureUpdate;
        
        // Reference to the UV map image element
        private VisualElement uvMapImage;
        
        // Reference to the selector
        private UVIslandSelector selector;
        
        // Cache key for texture operations
        private string currentCacheKey;
        
        #endregion
        
        #region Public Properties
        
        public bool TextureInitialized => textureInitialized;
        public bool IsLoadingFromCache => isLoadingFromCache;
        public bool ShouldShowLowResUntilInteraction => shouldShowLowResUntilInteraction;
        public Texture2D CurrentLowResTexture => currentLowResTexture;
        public Texture2D MagnifyingGlassTexture => magnifyingGlassTexture;
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Initialize the texture manager with required references
        /// 必要な参照でテクスチャマネージャを初期化
        /// </summary>
        public void Initialize(VisualElement uvMapImage, UVIslandSelector selector, string cacheKey)
        {
            this.uvMapImage = uvMapImage;
            this.selector = selector;
            this.currentCacheKey = cacheKey;
            
            textureInitialized = false;
            isLoadingFromCache = false;
            shouldShowLowResUntilInteraction = false;
        }
        
        /// <summary>
        /// Load low-resolution cached texture for immediate display
        /// 即座の表示のために低解像度キャッシュテクスチャを読み込み
        /// </summary>
        public void LoadLowResTextureFromCache()
        {
            currentLowResTexture = UVIslandCacheManager.LoadLowResTextureFromCache(currentCacheKey);
            isLoadingFromCache = (currentLowResTexture != null && selector?.UvMapTexture == null);
            
            if (currentLowResTexture != null)
            {
                // Mark that we have valid cached data to display immediately
                shouldShowLowResUntilInteraction = true;
            }
        }
        
        /// <summary>
        /// Save current low-res texture to cache
        /// 現在の低解像度テクスチャをキャッシュに保存
        /// </summary>
        public void SaveLowResTextureToCache()
        {
            UVIslandCacheManager.SaveLowResTextureToCache(currentCacheKey, selector);
        }
        
        /// <summary>
        /// Force immediate data refresh with texture generation
        /// テクスチャ生成による強制的な即座のデータ更新
        /// </summary>
        public void RefreshDataWithImmediateTexture()
        {
            if (selector == null) return;
            
            // Force mesh data update
            selector.UpdateMeshData();
            
            // Always generate texture immediately
            selector.GenerateUVMapTexture();
            textureInitialized = true;
            isLoadingFromCache = false; // Clear cache loading flag since full texture is ready
            
            RefreshUVMapImage();
        }
        
        /// <summary>
        /// Refresh UV map image with current texture state
        /// 現在のテクスチャ状態でUVマップイメージを更新
        /// </summary>
        public void RefreshUVMapImage()
        {
            if (uvMapImage == null) return;
            
            if (selector?.UvMapTexture != null && !shouldShowLowResUntilInteraction)
            {
                // Show full resolution texture
                uvMapImage.style.backgroundImage = new StyleBackground(selector.UvMapTexture);
                ClearLowResDisplayState();
            }
            else if (currentLowResTexture != null && (isLoadingFromCache || shouldShowLowResUntilInteraction))
            {
                // Show low-resolution cached texture until user interaction
                uvMapImage.style.backgroundImage = new StyleBackground(currentLowResTexture);
            }
            else if (selector?.UvMapTexture != null)
            {
                // Fallback to full texture if low-res is not available
                uvMapImage.style.backgroundImage = new StyleBackground(selector.UvMapTexture);
                ClearLowResDisplayState();
            }
            else
            {
                // Clear image if no texture is available
                uvMapImage.style.backgroundImage = StyleKeyword.None;
            }
        }
        
        /// <summary>
        /// Throttled immediate texture update for interactive operations
        /// インタラクティブ操作のためのスロットル付き即座テクスチャ更新
        /// </summary>
        public void UpdateTextureWithThrottle()
        {
            if (selector == null) return;
            
            float currentTime = Time.realtimeSinceStartup;
            if (currentTime - lastUpdateTime >= TEXTURE_UPDATE_THROTTLE)
            {
                // Immediate update if enough time has passed
                selector.GenerateUVMapTexture();
                RefreshUVMapImage();
                lastUpdateTime = currentTime;
            }
            else if (pendingTextureUpdate == null)
            {
                // Schedule single deferred update if throttled and none pending
                pendingTextureUpdate = () =>
                {
                    if (selector != null)
                    {
                        selector.GenerateUVMapTexture();
                        RefreshUVMapImage();
                        lastUpdateTime = Time.realtimeSinceStartup;
                    }
                    pendingTextureUpdate = null;
                };
                EditorApplication.delayCall += pendingTextureUpdate;
            }
            // If there's already a pending update, do nothing to avoid duplicates
        }
        
        /// <summary>
        /// Handle user interaction that should trigger full-resolution mode
        /// フル解像度モードをトリガーするユーザーインタラクションを処理
        /// </summary>
        public void OnUserInteraction()
        {
            if (shouldShowLowResUntilInteraction)
            {
                shouldShowLowResUntilInteraction = false;
                
                // Generate full texture when user interacts
                if (selector != null && selector.UvMapTexture == null)
                {
                    selector.GenerateUVMapTexture();
                    textureInitialized = true;
                }
                
                RefreshUVMapImage();
            }
        }
        
        /// <summary>
        /// Generate and update magnifying glass texture
        /// 拡大鏡テクスチャの生成と更新
        /// </summary>
        public Texture2D UpdateMagnifyingGlassTexture(Vector2 uvCoord, float size)
        {
            if (selector == null) return null;
            
            if (magnifyingGlassTexture != null)
            {
                Object.DestroyImmediate(magnifyingGlassTexture);
            }
            
            var sizeInt = Mathf.RoundToInt(size);
            magnifyingGlassTexture = selector.GenerateMagnifyingGlassTexture(uvCoord, sizeInt);
            
            return magnifyingGlassTexture;
        }
        
        /// <summary>
        /// Clean up magnifying glass texture
        /// 拡大鏡テクスチャのクリーンアップ
        /// </summary>
        public void CleanupMagnifyingGlass()
        {
            if (magnifyingGlassTexture != null)
            {
                Object.DestroyImmediate(magnifyingGlassTexture);
                magnifyingGlassTexture = null;
            }
        }
        
        /// <summary>
        /// Clean up all textures and resources
        /// すべてのテクスチャとリソースのクリーンアップ
        /// </summary>
        public void Cleanup()
        {
            CleanupMagnifyingGlass();
            
            // Clean up current low-res texture
            if (currentLowResTexture != null)
            {
                Object.DestroyImmediate(currentLowResTexture);
                currentLowResTexture = null;
            }
            
            // Clear any pending texture updates
            if (pendingTextureUpdate != null)
            {
                EditorApplication.delayCall -= pendingTextureUpdate;
                pendingTextureUpdate = null;
            }
        }
        
        /// <summary>
        /// Reset texture state for new selector
        /// 新しいセレクタ用にテクスチャ状態をリセット
        /// </summary>
        public void ResetForNewSelector(UVIslandSelector newSelector, string newCacheKey)
        {
            selector = newSelector;
            currentCacheKey = newCacheKey;
            textureInitialized = false;
            ClearLowResDisplayState();
        }
        
        #endregion
        
        #region Private Methods
        
        /// <summary>
        /// Centralized method to clear low-res display state
        /// 低解像度表示状態をクリアする集中メソッド
        /// </summary>
        private void ClearLowResDisplayState()
        {
            isLoadingFromCache = false;
            shouldShowLowResUntilInteraction = false;
        }
        
        #endregion
    }
}