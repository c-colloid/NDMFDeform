using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using System.Linq;
using System.Collections.Generic;
using Deform.Masking.Editor.Views;
using Deform.Masking;
using Deform.Masking.Editor;


namespace DeformEditor.Masking
{
    /// <summary>
    /// Custom editor for UV Island Mask with improved UI and localization
    /// 改良されたUI・多言語化対応のUVアイランドマスクカスタムエディタ
    /// </summary>
    [CustomEditor(typeof(UVIslandMask))]
	public partial class UVIslandMaskEditor : DeformerEditor
	{
        #region Fields and Constants
        private UVIslandMask targetMask;
        private UVIslandSelector selector;
        private VisualElement root;
        private VisualElement uvMapContainer;
        private VisualElement uvMapImage;
        private Label statusLabel;
        private ListView islandListView;
        private Button refreshButton;
        private Button clearSelectionButton;
        
        // UI controls
        private EnumField languageField;
        private VisualElement submeshSelector;
        private Label currentSubmeshLabel;
        private Button prevSubmeshButton;
        private Button nextSubmeshButton;
        private Toggle autoUpdateToggle;
        private Slider zoomSlider;
        private Button resetZoomButton;
        private Toggle rangeSelectionToggle;
        private Toggle magnifyingToggle;
        private Slider magnifyingZoomSlider;
        private Slider magnifyingSizeSlider;
        
        // Range selection overlay
        private VisualElement rangeSelectionOverlay;
        
        // Magnifying glass overlay
        private VisualElement magnifyingGlassOverlay;
        private VisualElement magnifyingGlassImage;
        private Label magnifyingGlassLabel;
        
        private const int UV_MAP_SIZE = 300;
        private bool isDraggingUVMap = false;
        private Vector2 lastMousePos;
        private bool isMagnifyingGlassActive = false;
        private Vector2 currentMagnifyingMousePos;
        private Texture2D magnifyingGlassTexture;
        private bool isRangeSelecting = false;
        private bool isRangeDeselecting = false;
        
        // Instance-based caching to avoid serialization issues
        private UVIslandSelector cachedSelector;
        private UVIslandMask lastTargetMask;
        private bool isInitialized = false;
        private Mesh lastCachedMesh;
        private int lastMeshInstanceID = -1;
        
        // Persistent cache system based on original mesh - survives Unity restart
        private static Dictionary<string, UVIslandSelector> persistentCache = new Dictionary<string, UVIslandSelector>();
        private string currentCacheKey;
        
        // Static initialization flag to ensure proper cache restoration across Unity restarts
        private static bool isCacheSystemInitialized = false;
        
        // Static tracking to prevent multiple editor instances for same target
        private static Dictionary<int, UVIslandMaskEditor> activeEditors = new Dictionary<int, UVIslandMaskEditor>();
        
        // Texture generation control
        private bool textureInitialized = false;
        private float lastUpdateTime = 0f;
		private const float TEXTURE_UPDATE_THROTTLE = 0.016f; // ~60fps limit
        
        // Robust caching system integration
        private Texture2D currentLowResTexture;
        private bool isLoadingFromCache = false;
        private bool shouldShowLowResUntilInteraction = false; // Flag to show low-res until user interaction
        private const int LOW_RES_TEXTURE_SIZE = 128; // Small size for quick display
        
        // Cache health monitoring
        private static DateTime lastCacheHealthCheck = DateTime.MinValue;
        private const double CACHE_HEALTH_CHECK_INTERVAL_HOURS = 1.0;

        // Async initialization
        private AsyncInitializationManager asyncInitManager;
        private InitializationProgressView progressView;
        private bool asyncInitializationInProgress = false;

        // Custom Views
        private HighlightSettingsView highlightSettingsView;
        private SubmeshSelectorView submeshSelectorView;
        #endregion

        // EditorApplication callback management
        private EditorApplication.CallbackFunction pendingTextureUpdate;

        #region Editor Lifecycle
        // エディタのライフサイクル管理
        // Editor lifecycle management

        /// <summary>
        /// Override to enable constant repaint for UI Toolkit inspectors
        /// UI Toolkit inspectors require constant repaint to update dynamic UI elements
        /// UITKインスペクターは動的UI要素の更新に継続的な再描画が必要
        /// </summary>
        public override bool RequiresConstantRepaint()
        {
            // CRITICAL: Always return true for UI Toolkit inspectors
            // UI Toolkit does not automatically repaint when UI elements change
            // Only Repaint() or RequiresConstantRepaint()=true will update the display
            return true;
        }

        public override VisualElement CreateInspectorGUI()
        {
            targetMask = target as UVIslandMask;
            int targetID = targetMask != null ? targetMask.GetInstanceID() : 0;

            Debug.Log($"[UVIslandMaskEditor] CreateInspectorGUI called for target {targetID}, this instance: {this.GetInstanceID()}");

            // CRITICAL: Register this instance IMMEDIATELY to prevent OnEnable from cleaning up wrong instance
            // OnEnable may be called after CreateInspectorGUI, and we need to ensure it sees the correct instance
            if (targetID != 0)
            {
                if (activeEditors.ContainsKey(targetID))
                {
                    var oldEditor = activeEditors[targetID];
                    if (oldEditor != this && oldEditor != null)
                    {
                        Debug.Log($"[UVIslandMaskEditor] Replacing old editor instance {oldEditor.GetInstanceID()} with new {this.GetInstanceID()}");
                        oldEditor.CleanupEditor();
                    }
                }
                activeEditors[targetID] = this;
                Debug.Log($"[UVIslandMaskEditor] Registered editor instance {this.GetInstanceID()} in CreateInspectorGUI");
            }

            // Log CreateInspectorGUI calls for debugging
            LogCacheOperation($"CreateInspectorGUI called for target {targetID}, existing root: {(root != null)}, same target: {lastTargetMask == targetMask}");

            // CRITICAL: Reuse UI for same instance and same target
            // This ensures progressBar field references match the actual displayed UI elements
            // Without this, progressBar field would point to orphaned UI elements
            if (root != null && lastTargetMask == targetMask)
            {
                LogCacheOperation($"Reusing existing UI for target {targetID}");

                // UI elements (including progressContainer) are reused as-is
                // Async initialization continues with correct callback references

                return root;
            }

            // If creating new UI, cancel any ongoing async initialization from previous UI
            if (asyncInitManager != null && asyncInitManager.IsRunning)
            {
                Debug.Log($"[UVIslandMaskEditor] Cancelling async initialization before creating new UI");
                asyncInitManager.Cancel();
                asyncInitManager = null;
            }
            
            // Lightweight cache system initialization - only when UI is created
            InitializeCacheSystem();
            
            // Get original mesh for UV mapping and cache key generation
            var originalMesh = GetOriginalMesh();

            // Generate mesh-only cache key for selector (not submesh-specific)
            string meshCacheKey = GenerateCacheKey(originalMesh, 0); // Use submesh 0 as base key
            if (meshCacheKey != null && meshCacheKey.EndsWith("_sm0"))
            {
                meshCacheKey = meshCacheKey.Substring(0, meshCacheKey.Length - 4); // Remove "_sm0" suffix
            }

            // Generate submesh-specific cache key for low-res texture
            int currentSubmesh = targetMask?.CurrentPreviewSubmesh ?? 0;
            currentCacheKey = GenerateCacheKey(originalMesh, currentSubmesh);

            // Try to get selector from persistent cache first (mesh-based, not submesh-based)
            if (meshCacheKey != null && persistentCache.TryGetValue(meshCacheKey, out var cachedSelector))
            {
                // Reuse cached selector with UV data
                selector = cachedSelector;
                selector.SetSelectedSubmeshes(targetMask.SelectedSubmeshes);

                // Restore current preview submesh from saved state (without triggering texture generation)
                bool wasAutoUpdate = selector.AutoUpdatePreview;
                selector.AutoUpdatePreview = false;
                selector.SetPreviewSubmesh(targetMask.CurrentPreviewSubmesh);
                selector.AutoUpdatePreview = wasAutoUpdate;

                // Load per-submesh selections (new format)
                if (targetMask.PerSubmeshSelections.Count > 0)
                {
                    selector.SetAllSelectedIslands(targetMask.GetPerSubmeshSelections());
                }
                // Fallback to legacy flat list if new format is empty (backward compatibility)
                else if (targetMask.SelectedIslandIDs.Count > 0)
                {
                    selector.SetSelectedIslands(targetMask.SelectedIslandIDs);
                }

                // Update target transform for dynamic mesh highlighting
                selector.TargetTransform = GetRendererTransform();

                // Load low-resolution cached texture for immediate display
                LoadLowResTextureFromCache();

                // Always regenerate texture for cached selector to ensure freshness
                isInitialized = false;
                textureInitialized = false;
            }
            else if (originalMesh != null)
            {
                // Don't create selector synchronously to avoid blocking Inspector UI (300-1000ms)
                // Selector will be created in background via delayCall
                selector = null;

                // Try to load cached texture even without selector
                // This allows immediate display if low-res cache is available
                LoadLowResTextureFromCache();

                isInitialized = false;
                textureInitialized = false;
            }
            else
            {
                selector = null;
                isInitialized = false;
                textureInitialized = false;
            }
            
            // Update references for instance-based caching (legacy)
            cachedSelector = selector;
            lastTargetMask = targetMask;
            lastCachedMesh = originalMesh;
            lastMeshInstanceID = originalMesh?.GetInstanceID() ?? -1;
            
            root = new VisualElement();
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;

            // Create progress view for async initialization
            progressView = new InitializationProgressView
            {
                Progress = 0f,
                StatusMessage = "Initializing..."
            };
            progressView.style.display = DisplayStyle.None; // Hidden by default
            root.Add(progressView);

            CreateLanguageSelector();
            CreateHeader();
            CreateMaskSettings();
            CreateSubmeshSelector();
            CreateHighlightSettings();
            CreateDisplaySettings();
            CreateUVMapArea();
            CreateIslandList();
            CreateControlButtons();
            CreateStatusArea();
            
            // Register global mouse events
            root.RegisterCallback<MouseMoveEvent>(OnRootMouseMove, TrickleDown.TrickleDown);
            root.RegisterCallback<MouseUpEvent>(OnRootMouseUp, TrickleDown.TrickleDown);
            
            // CRITICAL: Determine initialization strategy based on MESH CACHE, not instance fields
            // Check if we have a cached selector for this mesh (NOT whether selector field is set)
            bool hasMeshCache = (meshCacheKey != null && persistentCache.ContainsKey(meshCacheKey));
            bool hasLowResCache = (currentLowResTexture != null);

            if (hasMeshCache && hasLowResCache)
            {
                // Case 1: Both mesh and texture cache exist
                // Show cached low-res texture immediately, no async initialization needed
                Debug.Log($"[UVIslandMaskEditor] Using mesh cache + texture cache for {meshCacheKey}");
                shouldShowLowResUntilInteraction = true;
                isLoadingFromCache = true;
                RefreshUIFast(); // Quick UI update with low-res
            }
            else if (hasMeshCache && !hasLowResCache)
            {
                // Case 2: Mesh cache exists but no texture cache
                // Selector has UV data, but need to generate texture asynchronously
                Debug.Log($"[UVIslandMaskEditor] Mesh cache exists but no texture cache, starting async texture generation");

                // Show progress view
                if (progressView != null)
                {
                    progressView.Progress = 0f;
                    progressView.StatusMessage = "Initializing UV Map...";
                    progressView.style.display = DisplayStyle.Flex;
                }

                // Use async initialization for texture generation only (selector already has UV data)
                asyncInitializationInProgress = true;
                asyncInitManager = new AsyncInitializationManager();
                asyncInitManager.StartInitialization(
                    originalMesh,
                    selector,
                    OnAsyncInitializationCompleted
                );

                // Start monitoring async initialization progress
                EditorApplication.update += MonitorAsyncInitialization;
            }
            else if (!hasMeshCache && originalMesh != null)
            {
                // Case 3: No mesh cache - need full async initialization from scratch
                Debug.Log($"[UVIslandMaskEditor] No mesh cache, starting full async initialization");

                // Show progress view
                if (progressView != null)
                {
                    progressView.Progress = 0f;
                    progressView.StatusMessage = "Initializing UV Map...";
                    progressView.style.display = DisplayStyle.Flex;
                }

                // Create selector WITHOUT mesh initialization (empty constructor)
                selector = new UVIslandSelector();
                selector.AutoUpdatePreview = false; // Prevent automatic texture generation

                // Set mesh without analysis - analysis will be done asynchronously
                selector.SetMeshWithoutAnalysis(originalMesh);

                // Set submesh configuration (safe before UV analysis)
                selector.SetSelectedSubmeshes(targetMask.SelectedSubmeshes);
                selector.SetPreviewSubmesh(targetMask.CurrentPreviewSubmesh);
                selector.TargetTransform = GetRendererTransform();

                // NOTE: Island selections will be restored in OnAsyncInitializationCompleted()
                // after UV analysis completes, because we need the islands to exist first

                // Cache the selector for future use (using mesh-based key, not submesh-based)
                string cacheKey = GenerateCacheKey(originalMesh, 0); // Use submesh 0 as base key
                if (cacheKey != null && cacheKey.EndsWith("_sm0"))
                {
                    cacheKey = cacheKey.Substring(0, cacheKey.Length - 4); // Remove "_sm0" suffix
                }
                if (cacheKey != null)
                {
                    persistentCache[cacheKey] = selector;
                }

                cachedSelector = selector;
                lastTargetMask = targetMask;
                lastCachedMesh = originalMesh;
                lastMeshInstanceID = originalMesh?.GetInstanceID() ?? -1;

                // Start async initialization with UV analysis and texture generation
                asyncInitializationInProgress = true;
                asyncInitManager = new AsyncInitializationManager();
                asyncInitManager.StartInitialization(
                    originalMesh,
                    selector,
                    OnAsyncInitializationCompleted
                );

                // Start monitoring async initialization progress
                EditorApplication.update += MonitorAsyncInitialization;
            }
            else
            {
                // Show status when no mesh data is available
                if (statusLabel != null)
                {
                    statusLabel.text = "No mesh data available - please assign a mesh to the GameObject";
                }
            }
            
            return root;
        }

        #endregion

        #endregion

        #region Localization and Utilities
        // ローカリゼーションとユーティリティ
        // Localization and utility helper methods

        private void SetLocalizedContent(VisualElement element, string textKey, string tooltipKey = null)
        {
            if (element is TextElement textElement)
            {
                textElement.text = UVIslandLocalization.Get(textKey);
            }
            
            if (!string.IsNullOrEmpty(tooltipKey))
            {
                element.tooltip = UVIslandLocalization.Get(tooltipKey);
            }
        }
        
        private void SetLocalizedTooltip(VisualElement element, string tooltipKey)
        {
            element.tooltip = UVIslandLocalization.Get(tooltipKey);
        }

        #endregion

        #region UI Updates and Refresh
        // UI更新とリフレッシュ
        // UI update and refresh methods

        private void RefreshUIText()
        {
            // This would refresh all UI text when language changes
            // For now, we'll recreate the entire UI
            var parent = root.parent;
            if (parent != null)
            {
                parent.Remove(root);
                var newRoot = CreateInspectorGUI();
                parent.Add(newRoot);
            }
        }

        #endregion

        #region Mouse Event Handlers
        // マウスイベントハンドラ
        // Mouse event handling for UV map interaction

        private void OnUVMapMouseDown(MouseDownEvent evt)
        {
            if (selector == null) return;
            
            // Any mouse interaction should trigger full resolution mode
            OnUserInteraction();
            
            var localPosition = evt.localMousePosition;
            
            if (evt.button == 0) // Left click
            {
                if (isMagnifyingGlassActive)
                {
                    evt.StopPropagation();
                    return;
                }
                
                if (selector.EnableRangeSelection && evt.shiftKey)
                {
                    // Check for deselection mode (Ctrl+Shift) at the start of range selection
                    isRangeDeselecting = evt.ctrlKey && evt.shiftKey;
                    StartRangeSelection(localPosition);
                }
                else
                {
                    HandleIslandSelection(localPosition);
                }
                evt.StopPropagation();
            }
            else if (evt.button == 2 && !isMagnifyingGlassActive) // Middle click for pan
            {
                isDraggingUVMap = true;
                lastMousePos = localPosition;
                evt.StopPropagation();
            }
            else if (evt.button == 1 && selector.EnableMagnifyingGlass) // Right click for magnifying glass
            {
                StartMagnifyingGlass(localPosition);
                evt.StopPropagation();
                return;
            }
        }
        
        private void HandleIslandSelection(Vector2 localPosition)
        {
            // Use proper coordinate transformation that accounts for zoom and pan
            var uvCoordinate = LocalPosToUV(localPosition);
            
            int islandID = selector.GetIslandAtUVCoordinate(uvCoordinate);
            
            if (islandID >= 0)
            {
                // User interaction detected - switch to full resolution mode
                OnUserInteraction();

                Undo.RecordObject(targetMask, "Toggle UV Island Selection");
                selector.ToggleIslandSelection(islandID);
                UpdateMaskComponent();
                EditorUtility.SetDirty(targetMask);

                // Generate full texture and update display
                if (selector.AutoUpdatePreview)
                {
                    selector.GenerateUVMapTexture();
                    RefreshUVMapImage();
                }

                // Save low-res texture to cache after selection changes
                SaveLowResTextureToCache();
                
                RefreshUIFast();
            }
            // Removed automatic pan start on left click - pan is now middle button only
        }
        
        private void OnUVMapMouseMove(MouseMoveEvent evt) => HandleMouseMove(evt);
        private void OnUVMapContainerMouseMove(MouseMoveEvent evt) => HandleMouseMove(evt);
        
        private void HandleMouseMove(MouseMoveEvent evt)
        {
            if (selector == null) return;
            
            var localPosition = evt.localMousePosition;
            
            if (isMagnifyingGlassActive)
            {
                UpdateMagnifyingGlass(localPosition);
                evt.StopPropagation();
            }
            else if (selector.IsRangeSelecting)
            {
                UpdateRangeSelection(localPosition);
                evt.StopPropagation();
            }
            else if (isDraggingUVMap && !isMagnifyingGlassActive)
            {
                var deltaPos = localPosition - lastMousePos;
                // Fixed pan sensitivity to maintain consistent movement regardless of zoom
                var panSensitivity = 1f / UV_MAP_SIZE;
                var uvDelta = new Vector2(
                    deltaPos.x * panSensitivity,
                    -deltaPos.y * panSensitivity
                );
                
                var currentOffset = selector.UvMapPanOffset;
                selector.SetPanOffset(currentOffset + uvDelta);
                
                lastMousePos = localPosition;
                
                if (selector.AutoUpdatePreview)
                {
                    UpdateTextureWithThrottle(); // Immediate feedback for pan with throttling
                }
                
                evt.StopPropagation();
            }
        }
        
        private void OnUVMapMouseUp(MouseUpEvent evt) => HandleMouseUp(evt);
        private void OnUVMapContainerMouseUp(MouseUpEvent evt) => HandleMouseUp(evt);
        
        private void HandleMouseUp(MouseUpEvent evt)
        {
            if (evt.button == 0) // Left button
            {
                if (isMagnifyingGlassActive)
                {
                    HandleMagnifyingGlassClick(evt);
                }
                else if (selector?.IsRangeSelecting == true)
                {
                    bool addToSelection = evt.shiftKey && !evt.ctrlKey;
                    bool removeFromSelection = evt.ctrlKey && evt.shiftKey;
                    isRangeDeselecting = removeFromSelection;
                    FinishRangeSelection(addToSelection, removeFromSelection);
                }
                
                evt.StopPropagation();
            }
            else if (evt.button == 2) // Middle button - stop panning
            {
                isDraggingUVMap = false;
                
                // Update texture after mouse interaction ends
                if (selector?.AutoUpdatePreview ?? false)
                {
                    selector?.UpdateTextureIfNeeded();
                    RefreshUVMapImage();
                }
                
                evt.StopPropagation();
            }
            else if (evt.button == 1) // Right button
            {
                StopMagnifyingGlass();
                evt.StopPropagation();
            }
        }
        
        private void OnUVMapWheel(WheelEvent evt)
        {
            if (selector == null) return;
            
            // Zoom interaction should trigger full resolution mode
            OnUserInteraction();
            
            var localPosition = evt.localMousePosition;
            var zoomPoint = LocalPosToUV(localPosition);
            var zoomDelta = -evt.delta.y * 0.1f;
            
            selector.ZoomAtPoint(zoomPoint, zoomDelta);
            zoomSlider.value = selector.UvMapZoom;
            
            if (selector.AutoUpdatePreview)
            {
                UpdateTextureWithThrottle(); // Immediate feedback for wheel zoom
            }
            
            evt.StopPropagation();
        }
        
        private void OnRootMouseMove(MouseMoveEvent evt)
        {
            if (selector == null) return;
            
            var containerWorldBound = uvMapContainer.worldBound;
            var relativeX = evt.mousePosition.x - containerWorldBound.x;
            var relativeY = evt.mousePosition.y - containerWorldBound.y;
            var localPos = new Vector2(relativeX, relativeY);
            
            if (selector.IsRangeSelecting)
            {
                var clampedPos = new Vector2(
                    Mathf.Clamp(localPos.x, 0, UV_MAP_SIZE),
                    Mathf.Clamp(localPos.y, 0, UV_MAP_SIZE)
                );
                
                var uvCoord = LocalPosToUV(clampedPos);
                selector.UpdateRangeSelection(uvCoord);
                
	            bool removeFromSelection = evt.ctrlKey && evt.shiftKey;
                // Update deselection mode state based on current key state during dragging
                // Use Input class for cross-platform key detection
	            isRangeDeselecting = removeFromSelection;
                
                UpdateRangeSelectionVisual();
                evt.StopPropagation();
            }
            else if (isMagnifyingGlassActive)
            {
                var clampedPos = new Vector2(
                    Mathf.Clamp(localPos.x, 0, UV_MAP_SIZE),
                    Mathf.Clamp(localPos.y, 0, UV_MAP_SIZE)
                );
                
                UpdateMagnifyingGlass(clampedPos);
                evt.StopPropagation();
            }
            else if (isDraggingUVMap)
            {
                var clampedPos = new Vector2(
                    Mathf.Clamp(localPos.x, 0, UV_MAP_SIZE),
                    Mathf.Clamp(localPos.y, 0, UV_MAP_SIZE)
                );
                
                var deltaPos = clampedPos - lastMousePos;
                // Fixed pan sensitivity to maintain consistent movement regardless of zoom
                var panSensitivity = 1f / UV_MAP_SIZE;
                var uvDelta = new Vector2(
                    deltaPos.x * panSensitivity,
                    -deltaPos.y * panSensitivity
                );
                
                var currentOffset = selector.UvMapPanOffset;
                selector.SetPanOffset(currentOffset + uvDelta);
                
                lastMousePos = clampedPos;
                
                if (selector.AutoUpdatePreview)
                {
                    UpdateTextureWithThrottle(); // Immediate feedback for pan with throttling
                }
                
                evt.StopPropagation();
            }
        }
        
        private void OnRootMouseUp(MouseUpEvent evt)
        {
            if (selector == null) return;
            
            if (evt.button == 0)
            {
                if (selector.IsRangeSelecting)
                {
                    bool addToSelection = evt.shiftKey && !evt.ctrlKey;
                    bool removeFromSelection = evt.ctrlKey && evt.shiftKey;
                    isRangeDeselecting = removeFromSelection;
                    FinishRangeSelection(addToSelection, removeFromSelection);
                    evt.StopPropagation();
                }
                else if (isDraggingUVMap)
                {
                    isDraggingUVMap = false;
                    
                    // Update texture after mouse interaction ends
                    if (selector?.AutoUpdatePreview ?? false)
                    {
                        selector?.UpdateTextureIfNeeded();
                        RefreshUVMapImage();
                    }
                    
                    evt.StopPropagation();
                }
            }
            else if (evt.button == 2)
            {
                if (isDraggingUVMap)
                {
                    isDraggingUVMap = false;
                    
                    // Update texture after mouse interaction ends
                    if (selector?.AutoUpdatePreview ?? false)
                    {
                        selector?.UpdateTextureIfNeeded();
                        RefreshUVMapImage();
                    }
                    
                    evt.StopPropagation();
                }
            }
            else if (evt.button == 1)
            {
                if (isMagnifyingGlassActive)
                {
                    StopMagnifyingGlass();
                    evt.StopPropagation();
                }
            }
        }

        #endregion

        #region Mesh and Data Access
        // メッシュとデータアクセス
        // Mesh and data access helper methods

        private Vector2 LocalPosToUV(Vector2 localPos)
        {
            if (selector == null) return Vector2.zero;
            
            var normalizedPos = new Vector2(
                localPos.x / UV_MAP_SIZE,
                1f - (localPos.y / UV_MAP_SIZE)
            );
            
            var transform = selector.CalculateUVTransformMatrix();
            var inverseTransform = transform.inverse;
            var actualUV = inverseTransform.MultiplyPoint3x4(new Vector3(normalizedPos.x, normalizedPos.y, 0f));
            
            return new Vector2(actualUV.x, actualUV.y);
        }
        
        private Mesh GetMeshData()
        {
            // Try to get mesh from Deformable component first
	        if (targetMask?.CachedMesh != null)
            {
	            var mesh = targetMask.CachedMesh;
                if (mesh != null)
                {
                    return mesh;
                }
            }
            
            // Fallback: try to get original mesh from mesh sources
            var meshFilter = targetMask.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return meshFilter.sharedMesh;
            }
            
            var skinnedMeshRenderer = targetMask.GetComponent<SkinnedMeshRenderer>();
            if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
            {
                return skinnedMeshRenderer.sharedMesh;
            }
            
            return null;
        }
        
        private Mesh GetOriginalMesh()
	    {
		    // Try to get mesh from Deformable component first
		    if (targetMask?.OriginalMesh != null)
		    {
			    var mesh = targetMask.OriginalMesh;
			    if (mesh != null)
			    {
				    return mesh;
			    }
		    }
        	
            // Always get the original mesh for UV mapping and caching
            var meshFilter = targetMask.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return meshFilter.sharedMesh;
            }
            
            var skinnedMeshRenderer = targetMask.GetComponent<SkinnedMeshRenderer>();
            if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
            {
                return skinnedMeshRenderer.sharedMesh;
            }
            
            return null;
        }

        #endregion

        #region Cache Key Generation
        // キャッシュキー生成
        // Cache key generation methods

        /// <summary>
        /// Generate stable cache key with comprehensive error handling and validation
        /// 包括的エラー処理と検証機能付きの安定キャッシュキー生成
        /// </summary>
        private string GenerateCacheKey(Mesh originalMesh, int submeshIndex)
        {
            if (originalMesh == null)
            {
                LogCacheOperation("GenerateCacheKey called with null mesh", isError: true);
                return null;
            }

            try
            {
                // Use stable mesh identifiers instead of instance ID for Unity restart persistence
                string meshName = !string.IsNullOrEmpty(originalMesh.name) ?
                    originalMesh.name.Replace("/", "_").Replace("\\", "_") : // Sanitize for file system
                    "unnamed_mesh";

                int uvHash = CalculateUVHash(originalMesh.uv);
                int vertexCount = originalMesh.vertexCount;

                var key = $"{meshName}_{vertexCount}_{uvHash}_sm{submeshIndex}";

                // Validate cache key integrity
                if (string.IsNullOrEmpty(key) || key.Length < 3)
                {
                    LogCacheOperation($"Invalid cache key generated: '{key}'", isError: true);
                    return $"fallback_{originalMesh.GetHashCode()}_sm{submeshIndex}"; // Fallback key
                }

                if (key.Length > 200) // Prevent filesystem issues
                {
                    LogCacheOperation($"Cache key too long ({key.Length} chars), truncating", isError: false);
                    key = key.Substring(0, 195) + $"_sm{submeshIndex}"; // Keep submesh info
                }

                return key;
            }
            catch (System.Exception e)
            {
                LogCacheOperation($"Failed to generate cache key: {e.Message}", isError: true);
                // Fallback to basic hash-based key
                return $"fallback_{originalMesh.GetHashCode()}_{originalMesh.vertexCount}_sm{submeshIndex}";
            }
        }
        
        /// <summary>
        /// Safe UV hash calculation with comprehensive error handling and performance optimization
        /// 包括的エラー処理とパフォーマンス最適化を備えた安全なUVハッシュ計算
        /// </summary>
        private int CalculateUVHash(Vector2[] uvs)
        {
            if (uvs == null) 
            {
                LogCacheOperation("CalculateUVHash called with null UV array");
                return 0;
            }
            
            if (uvs.Length == 0)
            {
                LogCacheOperation("CalculateUVHash called with empty UV array");
                return 1; // Return distinct value for empty array to differentiate from null
            }
            
            try
            {
                unchecked
                {
                    int hash = 17;
                    // Sample UV coordinates with performance limit to balance accuracy and speed
                    int step = Mathf.Max(1, uvs.Length / 100);
                    int sampleCount = 0;
                    const int MAX_SAMPLES = 100; // Prevent excessive computation
                    
                    for (int i = 0; i < uvs.Length && sampleCount < MAX_SAMPLES; i += step)
                    {
                        // Additional safety check for corrupted UV data
                        var uv = uvs[i];
                        if (!float.IsNaN(uv.x) && !float.IsNaN(uv.y) && 
                            !float.IsInfinity(uv.x) && !float.IsInfinity(uv.y))
                        {
                            hash = hash * 31 + uv.GetHashCode();
                            sampleCount++;
                        }
                    }
                    
                    // Include array length in hash to distinguish different sized meshes
                    hash = hash * 31 + uvs.Length;
                    
                    return hash;
                }
            }
            catch (System.Exception e)
            {
                LogCacheOperation($"Exception in CalculateUVHash: {e.Message}", isError: true);
                // Fallback: use array length as hash if UV data is corrupted
                return uvs.Length.GetHashCode() + 42; // Add constant to avoid collision with length-only hashes
            }
        }
        
        #endregion

        #region Data Management
        // データ管理
        // Data refresh and component update methods

        private void RefreshData()
        {
            if (selector == null) 
            {
                statusLabel.text = "No mesh data available";
                return;
            }
            
            statusLabel.text = UVIslandLocalization.Get("status_refreshing");
            
            try 
            {
                selector.UpdateMeshData();
                
                // Force generate UV map texture if needed
                if (selector.UvMapTexture == null)
                {
                    selector.GenerateUVMapTexture();
                }
                
                RefreshUI(false);
                
                int islandCount = selector.UVIslands?.Count ?? 0;
                statusLabel.text = UVIslandLocalization.Get("status_islands_found", islandCount);
            }
            catch (System.Exception ex)
            {
                statusLabel.text = $"Error: {ex.Message}";
                Debug.LogError($"[UVIslandMaskEditor] Error refreshing data: {ex}");
            }
        }

        #endregion

        #region Async Initialization
        // 非同期初期化
        // Async initialization and progress monitoring

        /// <summary>
        /// Show placeholder message while UV map is being initialized
        /// UV マップ初期化中のプレースホルダーメッセージを表示
        /// </summary>
        private void ShowPlaceholderMessage(string message)
        {
            // Update status label
            if (statusLabel != null)
            {
                statusLabel.text = message;
            }

            // Show placeholder in UV map area
            if (uvMapImage != null)
            {
                // Set a neutral gray background to indicate loading state
                uvMapImage.style.backgroundColor = new StyleColor(new Color(0.22f, 0.22f, 0.22f, 1f));
                // Image will be replaced when texture loads, no need to explicitly clear
            }

            // Clear island list during initialization
            if (islandListView != null)
            {
                islandListView.itemsSource = null;
                islandListView.Rebuild();
            }
        }

        // ShowProgressUI and HideProgressUI methods removed - replaced with InitializationProgressView

        /// <summary>
        /// Monitor async initialization progress and update UI elements
        /// 非同期初期化の進捗を監視し、UI要素を更新
        /// </summary>
        private void MonitorAsyncInitialization()
        {
            if (asyncInitManager == null || !asyncInitManager.IsRunning)
            {
                // Stop monitoring when initialization is not running
                EditorApplication.update -= MonitorAsyncInitialization;

                // Hide progress view when monitoring stops
                if (progressView != null)
                {
                    progressView.style.display = DisplayStyle.None;
                }
                return;
            }

            // Update progress view with latest status
            if (progressView != null)
            {
                progressView.Progress = asyncInitManager.Progress;
                progressView.StatusMessage = asyncInitManager.StatusMessage;
            }

            // Update status label
            if (statusLabel != null)
            {
                statusLabel.text = asyncInitManager.StatusMessage;
            }

            // Update UV image with incremental textures
            if (selector != null && selector.UvMapTexture != null && uvMapImage != null)
            {
                uvMapImage.style.backgroundImage = new StyleBackground(selector.UvMapTexture);
            }
        }

        /// <summary>
        /// Callback when async initialization is completed
        /// 非同期初期化完了時のコールバック
        /// </summary>
        private void OnAsyncInitializationCompleted(UVIslandSelector completedSelector)
        {
            Debug.Log("[UVIslandMaskEditor] OnAsyncInitializationCompleted called");

            // Stop monitoring async initialization
            EditorApplication.update -= MonitorAsyncInitialization;

            // Clear async initialization flag
            asyncInitializationInProgress = false;

            if (completedSelector == null)
            {
                Debug.LogError("[UVIslandMaskEditor] Completion callback received null selector");
                if (progressView != null)
                {
                    progressView.style.display = DisplayStyle.None;
                }
                if (statusLabel != null)
                {
                    statusLabel.text = "Initialization failed";
                }
                return;
            }

            Debug.Log($"[UVIslandMaskEditor] Selector initialized with {completedSelector.UVIslands?.Count ?? 0} islands");

            // Step 1: Restore island selections now that UV analysis is complete
            if (targetMask != null)
            {
                // Load per-submesh selections (new format)
                if (targetMask.PerSubmeshSelections.Count > 0)
                {
                    Debug.Log($"[UVIslandMaskEditor] Restoring {targetMask.PerSubmeshSelections.Count} per-submesh selections");
                    selector.SetAllSelectedIslands(targetMask.GetPerSubmeshSelections());
                }
                // Fallback to legacy flat list if new format is empty (backward compatibility)
                else if (targetMask.SelectedIslandIDs.Count > 0)
                {
                    Debug.Log($"[UVIslandMaskEditor] Restoring {targetMask.SelectedIslandIDs.Count} legacy island selections");
                    selector.SetSelectedIslands(targetMask.SelectedIslandIDs);
                }
            }

            // Step 2: Update flags BEFORE UI refresh
            textureInitialized = true;
            isInitialized = true;
            isLoadingFromCache = false;
            shouldShowLowResUntilInteraction = false;

            // Step 3: Re-enable auto preview
            selector.AutoUpdatePreview = true;

            // Step 4: Clear placeholder background color
            if (uvMapImage != null)
            {
                uvMapImage.style.backgroundColor = StyleKeyword.Null;
                uvMapImage.MarkDirtyRepaint();
            }

            // Step 5: Save low-res texture to cache for next reload
            SaveLowResTextureToCache();

            // Step 6: Full UI refresh to update all elements
            Debug.Log("[UVIslandMaskEditor] Refreshing UI after initialization");
            RefreshUI(false);

            // Step 7: Update status message
            if (statusLabel != null)
            {
                int islandCount = selector.UVIslands?.Count ?? 0;
                statusLabel.text = UVIslandLocalization.Get("status_islands_found", islandCount);
            }

            // Step 8: Hide progress view LAST to ensure all UI is updated first
            if (progressView != null)
            {
                progressView.style.display = DisplayStyle.None;
            }

            Debug.Log("[UVIslandMaskEditor] Async initialization completed successfully");
        }

        /// <summary>
        /// Force immediate data refresh with texture generation - used for initial load
        /// </summary>
        private void RefreshDataWithImmediteTexture()
        {
            if (selector == null) 
            {
                if (statusLabel != null)
                {
                    statusLabel.text = "No mesh data available";
                }
                return;
            }
            
            if (statusLabel != null)
            {
                statusLabel.text = UVIslandLocalization.Get("status_refreshing");
            }
            
            try 
            {
                // Force mesh data update
                selector.UpdateMeshData();
                
                // Always generate texture immediately on first load
                selector.GenerateUVMapTexture();
                textureInitialized = true;
                isInitialized = true;
                isLoadingFromCache = false; // Clear cache loading flag since full texture is ready
                shouldShowLowResUntilInteraction = false; // Ensure we show full-res texture, not low-res

                // Save low-res texture to cache for next reload
                SaveLowResTextureToCache();

                // Clear placeholder background color
                if (uvMapImage != null)
                {
                    uvMapImage.style.backgroundColor = StyleKeyword.Null;
                }

                // Immediate UI refresh
                RefreshUVMapImage();

                if (selector?.UVIslands != null)
                {
                    islandListView.itemsSource = selector.UVIslands;
                    islandListView.Rebuild(); // Use Rebuild instead of RefreshItems to ensure full update
                }

                // Update UI elements that were created with null selector
                UpdateSubmeshSelectorUI();
                UpdateHighlightSettingsUI();
                UpdateDisplaySettingsUI();
                UpdateSubmeshLabel();

                UpdateStatus();

                int islandCount = selector.UVIslands?.Count ?? 0;
                if (statusLabel != null)
                {
                    statusLabel.text = UVIslandLocalization.Get("status_islands_found", islandCount);
                }
            }
            catch (System.Exception ex)
            {
                if (statusLabel != null)
                {
                    statusLabel.text = $"Error: {ex.Message}";
                }
                Debug.LogError($"[UVIslandMaskEditor] Error refreshing data: {ex}");
            }
        }
        
        private void RefreshUI(bool forceSceneRepaint = false)
        {
            // Always refresh the texture when UI updates
            if (selector?.AutoUpdatePreview ?? false)
            {
                selector.UpdateTextureIfNeeded(); // Use deferred update instead of direct generation
            }
            
            RefreshUVMapImage();
            
            if (selector?.UVIslands != null)
            {
                islandListView.itemsSource = selector.UVIslands;
                islandListView.RefreshItems();
            }
            
            UpdateStatus();
            
            if (forceSceneRepaint)
            {
                SceneView.RepaintAll();
            }
        }
        
        // Fast UI refresh for frequent operations like selection changes
        private void RefreshUIFast()
        {
            // Update essential UI elements immediately
            UpdateStatus();
            
            // Update list view selection state without full refresh
            if (islandListView != null && selector?.UVIslands != null)
            {
                // Only refresh if needed
                if (islandListView.itemsSource != selector.UVIslands)
                {
                    islandListView.itemsSource = selector.UVIslands;
                }
                islandListView.RefreshItems();
            }
            
            // Only generate texture if needed and not showing low-res until interaction
            if (selector != null && !shouldShowLowResUntilInteraction)
            {
                if (selector.UvMapTexture == null)
                {
                    selector.GenerateUVMapTexture();
                    textureInitialized = true;
                }
            }
            
            // Always refresh image (may show low-res or full-res based on state)
            RefreshUVMapImage();

            // Only repaint scene if there are selected islands to display
            if (selector?.HasSelectedIslands ?? false)
            {
                SceneView.RepaintAll();
            }
        }
        
        private void RefreshUVMapImage()
        {
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
        /// Centralized method to clear low-res display state
        /// </summary>
        private void ClearLowResDisplayState()
        {
            isLoadingFromCache = false;
            shouldShowLowResUntilInteraction = false;
        }
        
        // Throttled immediate texture update for interactive operations
        private void UpdateTextureWithThrottle()
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
        
        private void UpdateStatus()
        {
            if (selector == null) return;
            
            // Show total selected islands across all submeshes
            var totalSelectedCount = selector.AllSelectedIslandIDs?.Count ?? 0;
            var currentSubmeshSelectedCount = selector.SelectedIslandIDs?.Count ?? 0;
            var maskedVertexCount = selector.VertexMask?.Length ?? 0;
            var maskedFaceCount = (selector.TriangleMask?.Length ?? 0) / 3;

            if (totalSelectedCount > 0)
            {
                // Show both current submesh selection and total across all submeshes
                if (selector.SelectedSubmeshIndices.Count > 1)
                {
                    statusLabel.text = $"Selected: {currentSubmeshSelectedCount} islands (Submesh {selector.CurrentPreviewSubmesh}) | Total: {totalSelectedCount} islands across all submeshes | {maskedVertexCount} vertices, {maskedFaceCount} faces";
                }
                else
                {
                    statusLabel.text = UVIslandLocalization.Get("status_islands_selected",
                        currentSubmeshSelectedCount, maskedVertexCount, maskedFaceCount);
                }
            }
            else
            {
                int islandCount = selector.UVIslands?.Count ?? 0;
                statusLabel.text = UVIslandLocalization.Get("status_islands_found", islandCount);
            }
        }

        #endregion

        #region Island Selection and Interaction
        // アイランド選択とインタラクション
        // Island selection and user interaction methods

        private void ClearSelection()
        {
            if (selector == null) return;

            Undo.RecordObject(targetMask, "Clear UV Island Selection");
            selector.ClearSelection();
            UpdateMaskComponent();
            EditorUtility.SetDirty(targetMask);
            RefreshUI(false);
        }
        


        #endregion

        #region Range Selection
        // 範囲選択
        // Range selection methods

        private void StartRangeSelection(Vector2 localPos)
        {
            // Use proper coordinate transformation that accounts for zoom and pan
            var uvCoordinate = LocalPosToUV(localPos);
            selector.StartRangeSelection(uvCoordinate);
            isRangeSelecting = true;
	        isRangeDeselecting = false; // Default to selection mode
            UpdateRangeSelectionVisual();
        }
        
        private void UpdateRangeSelection(Vector2 localPos)
        {
            // Use proper coordinate transformation that accounts for zoom and pan
            var uvCoordinate = LocalPosToUV(localPos);
            selector.UpdateRangeSelection(uvCoordinate);
            
            // Update deselection mode state based on current key state during dragging
            // Use Input class for cross-platform key detection
            //isRangeDeselecting = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && 
            //                    (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            
            UpdateRangeSelectionVisual();
        }
        
        private void FinishRangeSelection(bool addToSelection, bool removeFromSelection = false)
        {
            selector.FinishRangeSelection(addToSelection, removeFromSelection);
            rangeSelectionOverlay.style.display = DisplayStyle.None;
            isRangeSelecting = false;
            isRangeDeselecting = false;
            UpdateMaskComponent();
            EditorUtility.SetDirty(targetMask);

            // Generate full texture and save to cache
            if (selector.AutoUpdatePreview)
            {
                selector.GenerateUVMapTexture();
            }
            SaveLowResTextureToCache();

            RefreshUI(false);
        }
        
        private void UpdateRangeSelectionVisual()
        {
            if (selector == null || !selector.IsRangeSelecting)
            {
                rangeSelectionOverlay.style.display = DisplayStyle.None;
                return;
            }
            
            var selectionRect = selector.GetCurrentSelectionRect();
            
            // Convert UV coordinates back to screen coordinates
            var transform = selector.CalculateUVTransformMatrix();
            var topLeft = transform.MultiplyPoint3x4(new Vector3(selectionRect.xMin, selectionRect.yMax, 0f));
            var bottomRight = transform.MultiplyPoint3x4(new Vector3(selectionRect.xMax, selectionRect.yMin, 0f));
            
            var left = topLeft.x * UV_MAP_SIZE;
            var top = (1f - topLeft.y) * UV_MAP_SIZE;
            var width = (bottomRight.x - topLeft.x) * UV_MAP_SIZE;
            var height = (topLeft.y - bottomRight.y) * UV_MAP_SIZE;
            
            // Update visual style based on selection mode
            if (isRangeDeselecting)
            {
                // Red/orange style for deselection
                rangeSelectionOverlay.style.backgroundColor = new Color(0.8f, 0.3f, 0.2f, 0.3f);
                rangeSelectionOverlay.style.borderLeftColor = new Color(0.8f, 0.3f, 0.2f, 0.8f);
                rangeSelectionOverlay.style.borderRightColor = new Color(0.8f, 0.3f, 0.2f, 0.8f);
                rangeSelectionOverlay.style.borderTopColor = new Color(0.8f, 0.3f, 0.2f, 0.8f);
                rangeSelectionOverlay.style.borderBottomColor = new Color(0.8f, 0.3f, 0.2f, 0.8f);
            }
            else
            {
                // Blue style for selection (default)
                rangeSelectionOverlay.style.backgroundColor = new Color(0.3f, 0.5f, 0.8f, 0.3f);
                rangeSelectionOverlay.style.borderLeftColor = new Color(0.3f, 0.5f, 0.8f, 0.8f);
                rangeSelectionOverlay.style.borderRightColor = new Color(0.3f, 0.5f, 0.8f, 0.8f);
                rangeSelectionOverlay.style.borderTopColor = new Color(0.3f, 0.5f, 0.8f, 0.8f);
                rangeSelectionOverlay.style.borderBottomColor = new Color(0.3f, 0.5f, 0.8f, 0.8f);
            }
            
            rangeSelectionOverlay.style.left = left;
            rangeSelectionOverlay.style.top = top;
            rangeSelectionOverlay.style.width = width;
            rangeSelectionOverlay.style.height = height;
            rangeSelectionOverlay.style.display = DisplayStyle.Flex;
        }

        #endregion

        #region Magnifying Glass
        // 拡大鏡
        // Magnifying glass feature methods

        private void StartMagnifyingGlass(Vector2 localPos)
        {
            if (!selector.EnableMagnifyingGlass) return;
            
            isMagnifyingGlassActive = true;
            currentMagnifyingMousePos = localPos;
            UpdateMagnifyingGlass(localPos);
        }
        
        private void UpdateMagnifyingGlass(Vector2 localPos)
        {
            if (!isMagnifyingGlassActive || !selector.EnableMagnifyingGlass) return;
            
            currentMagnifyingMousePos = localPos;
            var uvCoord = LocalPosToUV(localPos);
            var size = Mathf.RoundToInt(selector.MagnifyingGlassSize);
            
            if (magnifyingGlassTexture != null)
            {
                DestroyImmediate(magnifyingGlassTexture);
            }
            
            magnifyingGlassTexture = selector.GenerateMagnifyingGlassTexture(uvCoord, size);
            
            if (magnifyingGlassTexture != null)
            {
                var overlaySize = 120f;
                var posX = Mathf.Clamp(localPos.x + 10, 0, UV_MAP_SIZE - overlaySize);
                var posY = Mathf.Clamp(localPos.y - overlaySize - 10, 0, UV_MAP_SIZE - 140);
                
                magnifyingGlassOverlay.style.left = posX;
                magnifyingGlassOverlay.style.top = posY;
                magnifyingGlassLabel.text = $"UV: ({uvCoord.x:F3}, {uvCoord.y:F3})";
                magnifyingGlassImage.style.backgroundImage = new StyleBackground(magnifyingGlassTexture);
                magnifyingGlassImage.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
                magnifyingGlassOverlay.style.display = DisplayStyle.Flex;
            }
        }
        
        private void StopMagnifyingGlass()
        {
            isMagnifyingGlassActive = false;
            
            if (magnifyingGlassOverlay != null)
            {
                magnifyingGlassOverlay.style.display = DisplayStyle.None;
            }
            
            if (magnifyingGlassTexture != null)
            {
                DestroyImmediate(magnifyingGlassTexture);
                magnifyingGlassTexture = null;
            }
        }
        
        private void HandleMagnifyingGlassClick(MouseUpEvent evt)
        {
            // Use proper coordinate transformation that accounts for zoom and pan
            var uvCoordinate = LocalPosToUV(currentMagnifyingMousePos);

            int islandID = selector.GetIslandAtUVCoordinate(uvCoordinate);

            if (islandID >= 0)
            {
                // User interaction detected - switch to full resolution mode
                OnUserInteraction();

                Undo.RecordObject(targetMask, "Select UV Island from Magnifying Glass");
                selector.ToggleIslandSelection(islandID);
                UpdateMaskComponent();
                EditorUtility.SetDirty(targetMask);

                // Generate full texture and save to cache
                if (selector.AutoUpdatePreview)
                {
                    selector.GenerateUVMapTexture();
                    RefreshUVMapImage();
                }
                SaveLowResTextureToCache();

                RefreshUIFast();
            }
        }
        
        private void OnIslandListSelectionChanged(System.Collections.Generic.IEnumerable<object> selectedItems)
        {
            if (selector == null) return;
            
            var selectedIndices = islandListView.selectedIndices.ToArray();
            
            Undo.RecordObject(targetMask, "Select UV Islands");
            selector.ClearSelection();
            
            foreach (int index in selectedIndices)
            {
                if (index < selector.UVIslands.Count)
                {
                    int islandID = selector.UVIslands[index].islandID;
                    selector.ToggleIslandSelection(islandID);
                }
            }

            UpdateMaskComponent();
            EditorUtility.SetDirty(targetMask);
            RefreshUIFast();
        }

        #endregion

        #region Editor Lifecycle (continued)
        // エディタライフサイクル（続き）
        // Editor lifecycle callbacks

        private void OnEnable()
        {
            targetMask = target as UVIslandMask;

            // Track active editor instances to prevent duplicates
            int targetID = targetMask != null ? targetMask.GetInstanceID() : 0;
            Debug.Log($"[UVIslandMaskEditor] OnEnable called for target {targetID}, this instance: {this.GetInstanceID()}");

            if (targetID != 0)
            {
                if (activeEditors.ContainsKey(targetID))
                {
                    // Another editor instance exists for this target
                    var oldEditor = activeEditors[targetID];
                    Debug.Log($"[UVIslandMaskEditor] Found existing editor instance: {oldEditor.GetInstanceID()}, current: {this.GetInstanceID()}, same: {oldEditor == this}");

                    if (oldEditor != this && oldEditor != null)
                    {
                        Debug.Log($"[UVIslandMaskEditor] Cleaning up old editor instance {oldEditor.GetInstanceID()}");
                        oldEditor.CleanupEditor();
                    }
                    else if (oldEditor == this)
                    {
                        Debug.Log($"[UVIslandMaskEditor] Old editor is the same as current editor, skipping cleanup");
                    }
                }
                activeEditors[targetID] = this;
                Debug.Log($"[UVIslandMaskEditor] Registered this editor instance {this.GetInstanceID()} for target {targetID}");
            }

            // DO NOT call CleanupEditor() here!
            // OnDisable already handles cleanup, and calling it here would cancel
            // async initialization that was just started in CreateInspectorGUI()

            Undo.undoRedoPerformed += OnUndoRedo;
        }
        
        private void OnDisable()
        {
            CleanupEditor();
        }
        
        private void CleanupEditor()
        {
            Debug.Log("[UVIslandMaskEditor] CleanupEditor called");

            // Stop monitoring async initialization
            EditorApplication.update -= MonitorAsyncInitialization;

            // Cancel any ongoing async initialization
            if (asyncInitManager != null && asyncInitManager.IsRunning)
            {
                Debug.Log("[UVIslandMaskEditor] Cancelling ongoing async initialization");
                // Just cancel - callbacks will be cleared in the manager itself
                asyncInitManager.Cancel();
                asyncInitManager = null;
            }

            // Clear async initialization flag
            asyncInitializationInProgress = false;

            // DO NOT touch UI elements here - they may still be in use
            // UI cleanup is only done in OnDestroy()

            // Remove from active editors tracking
            int targetID = targetMask != null ? targetMask.GetInstanceID() : 0;
            if (targetID != 0 && activeEditors.ContainsKey(targetID) && activeEditors[targetID] == this)
            {
                activeEditors.Remove(targetID);
            }

            // Clean up resources
            if (magnifyingGlassTexture != null)
            {
                DestroyImmediate(magnifyingGlassTexture);
                magnifyingGlassTexture = null;
            }

            // Keep cached selector for reuse - only dispose when editor is destroyed
            // cachedSelector will be reused for better performance

            Undo.undoRedoPerformed -= OnUndoRedo;
        }
        
        private void OnDestroy()
        {
            Debug.Log("[UVIslandMaskEditor] OnDestroy called - cleaning up all resources");

            // Clean up cached selector only when editor is destroyed
            if (cachedSelector != null)
            {
                cachedSelector.Dispose();
                cachedSelector = null;
            }

            // Clean up current low-res texture
            if (currentLowResTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(currentLowResTexture);
                currentLowResTexture = null;
            }

            // Note: progressView is part of root VisualElement hierarchy and will be cleaned up automatically
        }
        
		/*
        // Clean up persistent cache when domain reloads
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            // Clear persistent cache on domain reload to prevent stale data
            if (persistentCache != null)
            {
                foreach (var kvp in persistentCache)
                {
                    kvp.Value?.Dispose();
                }
                persistentCache.Clear();
            }
            
            // Clear low-res texture cache
            if (lowResUVCache != null)
            {
                foreach (var kvp in lowResUVCache)
                {
                    if (kvp.Value != null)
                    {
                        UnityEngine.Object.DestroyImmediate(kvp.Value);
                    }
                }
                lowResUVCache.Clear();
            }
		}
		*/

        #endregion

        #region Cache Initialization
        // キャッシュ初期化
        // Cache system initialization

        /// <summary>
        /// Lightweight cache system initialization - called only when needed
        /// 軽量キャッシュシステム初期化 - 必要時のみ呼び出し
        /// </summary>
        private static void InitializeCacheSystem()
        {
            if (isCacheSystemInitialized) return;
            
            try
            {
                // Just set the flag - actual cache initialization happens lazily in RobustUVCache
                isCacheSystemInitialized = true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UVIslandMaskEditor] Failed to initialize cache system: {e.Message}");
            }
        }
        
        // Clean up on editor shutdown
        static UVIslandMaskEditor()
        {
            EditorApplication.quitting += () =>
            {
                // Clean up all active editors
                if (activeEditors != null)
                {
                    foreach (var kvp in activeEditors)
                    {
                        kvp.Value?.CleanupEditor();
                    }
                    activeEditors.Clear();
                }
                
                // Clean up persistent cache
                if (persistentCache != null)
                {
                    foreach (var kvp in persistentCache)
                    {
                        kvp.Value?.Dispose();
                    }
                    persistentCache.Clear();
                }
            };
        }
        
        /// <summary>
        /// Lightweight cache system readiness check - no heavy operations
        /// 軽量キャッシュシステム準備確認 - 重い操作なし
        /// </summary>
        private static void EnsureCacheSystemInitialized()
        {
            // The RobustUVCache has its own lazy initialization
            // We don't need to do anything heavy here
            if (!isCacheSystemInitialized)
            {
                isCacheSystemInitialized = true;
            }
        }

        #endregion

        #region UI Updates and Refresh (continued)
        // UI更新とリフレッシュ（続き）
        // Additional UI update methods

        private void OnUndoRedo()
        {
            RefreshUI(false);
        }

        private void SetMagnifyingZoom(float zoomLevel)
        {
            if (selector != null)
            {
                selector.MagnifyingGlassZoom = zoomLevel;
            }
        }

        private void UpdateSubmeshLabel()
        {
            if (currentSubmeshLabel != null && selector != null)
            {
                currentSubmeshLabel.text = $"Submesh {selector.CurrentPreviewSubmesh}";
            }
        }

        private void RebuildIslandList()
        {
            if (islandListView != null && selector != null)
            {
                islandListView.itemsSource = selector.UVIslands;
                islandListView.Rebuild();
            }
        }

        /// <summary>
        /// Update Submesh Selector UI after background initialization
        /// バックグラウンド初期化後にサブメッシュセレクターUIを更新
        /// </summary>
        private void UpdateSubmeshSelectorUI()
        {
            if (selector == null) return;

            // Update SubmeshSelectorView with current selector values
            if (submeshSelectorView != null)
            {
                submeshSelectorView.TotalSubmeshes = selector.SubmeshCount;
                submeshSelectorView.CurrentSubmesh = selector.CurrentPreviewSubmesh;

                // Calculate current mask from selected submeshes
                int currentMask = SubmeshSelectorView.ListToMask(targetMask.SelectedSubmeshes);
                submeshSelectorView.SelectedMask = currentMask;
            }
        }

        /// <summary>
        /// Update Highlight Settings UI after background initialization
        /// バックグラウンド初期化後にハイライト設定UIを更新
        /// </summary>
        private void UpdateHighlightSettingsUI()
        {
            if (selector == null) return;

            // Update HighlightSettingsView with current selector value
            if (highlightSettingsView != null)
            {
                highlightSettingsView.HighlightOpacity = selector.HighlightOpacity;
            }
        }

        /// <summary>
        /// Update Display Settings UI after background initialization
        /// バックグラウンド初期化後に表示設定UIを更新
        /// </summary>
        private void UpdateDisplaySettingsUI()
        {
            if (selector == null) return;

            // Display settings UI update (adaptive vertex size removed)
            // Will be populated with HighlightSettingsView in Phase 3
        }

        #endregion

        #region Scene View Integration
        // シーンビュー統合
        // Scene view integration for 3D mesh highlighting

        private void OnSceneGUI()
		{
			if (target == null) return;
			base.OnSceneGUI();
			
            if (selector == null || !selector.HasSelectedIslands) return;

            // Use proper renderer transform for scene highlighting
            var rendererTransform = GetRendererTransform();
            if (rendererTransform != null)
            {
                selector.TargetTransform = rendererTransform;
                selector.DrawSelectedFacesInScene();
            }
        }
        
        private Transform GetRendererTransform()
        {
            // Try to get cached renderer transform from the UVIslandMask component
            if (targetMask?.CachedRendererTransform != null)
            {
                return targetMask.CachedRendererTransform;
            }
            
            // Update renderer cache if not available
            targetMask?.UpdateRendererCache();
            
            // Return the updated cached transform
            return targetMask?.CachedRendererTransform;
        }

        #endregion

        #region Data Management (continued)
        // データ管理（続き）
        // User interaction and component update methods

        /// <summary>
        /// Centralized handler for user interactions that should trigger full-resolution mode
        /// </summary>
        private void OnUserInteraction()
        {
            // Force async initialization to complete with full resolution
            if (asyncInitManager != null && asyncInitManager.IsRunning)
            {
                asyncInitManager.ForceFullResolution();
                return;
            }

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
        /// Update mask component with current selection data including vertex list
        /// マスクコンポーネントを現在の選択データ（頂点リスト含む）で更新
        /// </summary>
        private void UpdateMaskComponent()
        {
            if (selector == null || targetMask == null) return;

            // Update current preview submesh (for cache key persistence)
            targetMask.CurrentPreviewSubmesh = selector.CurrentPreviewSubmesh;

            // Update per-submesh selections (new format)
            targetMask.SetPerSubmeshSelections(selector.SelectedIslandsPerSubmesh);

            // Legacy: also update flat list for backward compatibility
            targetMask.SetSelectedIslands(selector.AllSelectedIslandIDs);

            // Update vertex list (most efficient for mask processing)
            var vertexList = new List<int>(selector.VertexMask);
            targetMask.SetSelectedVertexIndices(vertexList);
        }

        #endregion

        #region Texture and Cache Operations
        // テクスチャとキャッシュ操作
        // Texture caching and loading operations

        /// <summary>
        /// Load low-resolution UV texture from robust cache system
        /// 堅牢なキャッシュシステムから低解像度UVテクスチャを読み込み
        /// </summary>
        private void LoadLowResTextureFromCache()
        {
            if (string.IsNullOrEmpty(currentCacheKey))
            {
                LogCacheOperation("LoadLowResTextureFromCache called with null cache key", isError: true);
                return;
            }
            
            try
            {
                // Lightweight cache system check
                EnsureCacheSystemInitialized();
                
                currentLowResTexture = RobustUVCache.LoadTexture(currentCacheKey);
                isLoadingFromCache = (currentLowResTexture != null && selector?.UvMapTexture == null);
                
                if (currentLowResTexture != null)
                {
                    LogCacheOperation($"Successfully loaded low-res texture for key: {currentCacheKey}");
                    // Mark that we have valid cached data to display immediately
                    shouldShowLowResUntilInteraction = true;
                }
                else
                {
                    LogCacheOperation($"No cached texture found for key: {currentCacheKey}");
                    shouldShowLowResUntilInteraction = false;
                }
                
                // Periodic cache health check
                CheckCacheHealth();
            }
            catch (System.Exception e)
            {
                LogCacheOperation($"Failed to load cached texture: {e.Message}", isError: true);
                currentLowResTexture = null;
                isLoadingFromCache = false;
                shouldShowLowResUntilInteraction = false;
            }
        }
        
        /// <summary>
        /// Save low-resolution UV texture to robust cache system
        /// 堅牢なキャッシュシステムに低解像度UVテクスチャを保存
        /// </summary>
        private void SaveLowResTextureToCache()
        {
            if (string.IsNullOrEmpty(currentCacheKey))
            {
                LogCacheOperation("SaveLowResTextureToCache called with null cache key", isError: true);
                return;
            }
            
            if (selector == null)
            {
                LogCacheOperation("SaveLowResTextureToCache called with null selector", isError: true);
                return;
            }

            // Always save low-res cache regardless of selection state
            // This ensures fast reload for both selected and unselected states
            try
            {
                // Use the new method that ignores zoom/pan state for cache generation
                var lowResTexture = selector.GenerateLowResUVMapTexture(LOW_RES_TEXTURE_SIZE, LOW_RES_TEXTURE_SIZE);
                if (lowResTexture != null)
                {
                    bool saveSuccess = RobustUVCache.SaveTexture(currentCacheKey, lowResTexture);

                    if (saveSuccess)
                    {
                        LogCacheOperation($"Successfully cached low-res texture for key: {currentCacheKey}");
                    }
                    else
                    {
                        LogCacheOperation($"Failed to cache texture for key: {currentCacheKey}", isError: true);
                    }

                    // Clean up temporary texture
                    UnityEngine.Object.DestroyImmediate(lowResTexture);
                }
                else
                {
                    LogCacheOperation($"Failed to generate low-res texture for key: {currentCacheKey}", isError: true);
                }
            }
            catch (System.Exception e)
            {
                LogCacheOperation($"Exception in SaveLowResTextureToCache: {e.Message}", isError: true);
            }
        }

        #endregion

        #region Cache Management and Health Monitoring
        // キャッシュ管理とヘルスモニタリング
        // Cache health monitoring and logging

        /// <summary>
        /// Log cache operations for debugging and monitoring
        /// デバッグと監視のためのキャッシュ操作ログ
        /// </summary>
        private void LogCacheOperation(string message, bool isError = false)
        {
            var logMessage = $"[UVIslandCache] {message}";
            
            if (isError)
            {
                Debug.LogError(logMessage);
            }
            else
            {
                // Only log in debug mode to avoid spam
                if (Debug.isDebugBuild)
                {
                    //Debug.Log(logMessage);
                }
            }
        }
        
        /// <summary>
        /// Periodic cache health check to ensure optimal performance
        /// 最適なパフォーマンスを確保するための定期的キャッシュヘルスチェック
        /// </summary>
        private void CheckCacheHealth()
        {
            var now = DateTime.UtcNow;
            if ((now - lastCacheHealthCheck).TotalHours >= CACHE_HEALTH_CHECK_INTERVAL_HOURS)
            {
                lastCacheHealthCheck = now;
                
                try
                {
                    var stats = RobustUVCache.GetCacheStatistics();
                    
                    // Log performance metrics
                    LogCacheOperation($"Cache Health Check: {stats}");
                    
                    // Check for performance issues
                    if (stats.overallHitRate < 0.5f && stats.totalHitCount + stats.totalMissCount > 10)
                    {
                        LogCacheOperation($"Low cache hit rate detected: {stats.overallHitRate:P1} - Consider investigating cache key generation", isError: true);
                    }
                    
                    if (stats.averageReadTime > 5.0f)
                    {
                        LogCacheOperation($"Slow cache read performance: {stats.averageReadTime:F2}ms average - Consider cache cleanup", isError: true);
                    }
                    
                    // Automatic cleanup for large caches
                    if (stats.totalSizeBytes > 100 * 1024 * 1024) // 100MB
                    {
                        LogCacheOperation("Cache size exceeding 100MB, scheduling cleanup...");
                        EditorApplication.delayCall += () => RobustUVCache.CleanupCache();
                    }
                }
                catch (System.Exception e)
                {
                    LogCacheOperation($"Cache health check failed: {e.Message}", isError: true);
                }
            }
        }
        
        
        
        #endregion
        
    }
}