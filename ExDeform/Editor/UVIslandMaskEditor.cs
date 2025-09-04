using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using System.Linq;
using System.Collections.Generic;
using ExDeform.Runtime.Deformers;

#if EXDEFORM_DEFORM_AVAILABLE
using Deform;
#endif

namespace ExDeform.Editor
{
    /// <summary>
    /// Custom editor for UV Island Mask with improved UI and localization
    /// 改良されたUI・多言語化対応のUVアイランドマスクカスタムエディタ
    /// </summary>
    [CustomEditor(typeof(UVIslandMask))]
    public class UVIslandMaskEditor : UnityEditor.Editor
	{
    	#region variables
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
        private Toggle adaptiveVertexSizeToggle;
        private Slider vertexSizeSlider;
        private Slider adaptiveMultiplierSlider;
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
        
        // Instance-based caching to avoid serialization issues
        private UVIslandSelector cachedSelector;
        private UVIslandMask lastTargetMask;
        private bool isInitialized = false;
        private Mesh lastCachedMesh;
        private int lastMeshInstanceID = -1;
        
        // Service integration
        private readonly IEditorCacheService cacheService = EditorCacheService.Instance;
        private readonly IUIBuilderService uiBuilderService = new UIBuilderService();
        private string currentCacheKey;
        
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
        #endregion
        
        // EditorApplication callback management
        private EditorApplication.CallbackFunction pendingTextureUpdate;
        
        public override VisualElement CreateInspectorGUI()
        {
            targetMask = target as UVIslandMask;
            int targetID = targetMask != null ? targetMask.GetInstanceID() : 0;
            
            // Log CreateInspectorGUI calls for debugging
            LogCacheOperation($"CreateInspectorGUI called for target {targetID}, existing root: {(root != null)}, same target: {lastTargetMask == targetMask}");
            
            // Prevent multiple UI creation for the same target
            if (root != null && lastTargetMask == targetMask)
            {
                LogCacheOperation($"Reusing existing UI for target {targetID}");
                return root;
            }
            
            // Get original mesh for UV mapping and cache key generation
            var originalMesh = GetOriginalMesh();
            currentCacheKey = UVIslandCacheManager.GenerateCacheKey(originalMesh);
            
            // Try to get selector from cache service
            if (currentCacheKey != null)
            {
                var cachedSelector = UVIslandCacheManager.GetCachedSelector(currentCacheKey);
                if (cachedSelector != null)
                {
                    // Reuse cached selector with UV data
                    selector = cachedSelector;
                    selector.SetSelectedIslands(targetMask.SelectedIslandIDs);
                    
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
                    // Create new selector based on original mesh
                    selector = new UVIslandSelector(originalMesh);
                    selector.SetSelectedIslands(targetMask.SelectedIslandIDs);
                    selector.TargetTransform = GetRendererTransform();
                    
                    // Cache the selector for future use
                    UVIslandCacheManager.CacheSelector(currentCacheKey, selector);
                    
                    // Try to load cached texture even for new selectors
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
            
            CreateUIWithService();
            
            // Register global mouse events
            root.RegisterCallback<MouseMoveEvent>(OnRootMouseMove, TrickleDown.TrickleDown);
            root.RegisterCallback<MouseUpEvent>(OnRootMouseUp, TrickleDown.TrickleDown);
            
            // Initialize with low-res cache if available, full-res only on user interaction
            if (selector != null)
            {
                // Load low-res texture from cache for immediate display
                LoadLowResTextureFromCache();
                
                if (currentLowResTexture != null)
                {
                    // Show cached low-res texture until user interaction
                    shouldShowLowResUntilInteraction = true;
                    isLoadingFromCache = true;
                    RefreshUIFast(); // Quick UI update with low-res
                }
                else
                {
                    // No cache available, generate full texture immediately
                    RefreshDataWithImmediteTexture();
                }
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
        
        private void CreateUIWithService()
        {
            // Create language selector using service
            var languageSelector = uiBuilderService.CreateLanguageSelector(OnLanguageChanged);
            root.Add(languageSelector);
            
            // Create header using service
            var header = uiBuilderService.CreateHeader();
            root.Add(header);
            
            // Create mask settings using service
            var maskSettings = uiBuilderService.CreateMaskSettings(targetMask);
            root.Add(maskSettings);
            
            // Create display settings using service
            var displaySettings = uiBuilderService.CreateDisplaySettings(selector);
            root.Add(displaySettings);
            
            // Create UV map area using service
            var uvMapConfig = new UVMapConfig
            {
                MapSize = UV_MAP_SIZE,
                Selector = selector,
                MouseHandlers = CreateMouseHandlers()
            };
            var uvMapComponents = uiBuilderService.CreateUVMapArea(uvMapConfig);
            
            // Store references to important UI components
            uvMapContainer = uvMapComponents.Container;
            uvMapImage = uvMapComponents.ImageElement;
            autoUpdateToggle = uvMapComponents.AutoUpdateToggle;
            zoomSlider = uvMapComponents.ZoomSlider;
            resetZoomButton = uvMapComponents.ResetZoomButton;
            magnifyingToggle = uvMapComponents.MagnifyingToggle;
            magnifyingSizeSlider = uvMapComponents.MagnifyingSizeSlider;
            
            root.Add(uvMapComponents.Container);
            
            // Create overlays using service
            rangeSelectionOverlay = uiBuilderService.CreateRangeSelectionOverlay();
            var magnifyingComponents = uiBuilderService.CreateMagnifyingGlassOverlay();
            magnifyingGlassOverlay = magnifyingComponents.Overlay;
            magnifyingGlassImage = magnifyingComponents.ImageElement;
            magnifyingGlassLabel = magnifyingComponents.InfoLabel;
            
            // Add overlays to UV map container (find the actual container with the image)
            var actualUVContainer = FindUVMapImageContainer(uvMapComponents.Container);
            if (actualUVContainer != null)
            {
                actualUVContainer.Add(rangeSelectionOverlay);
                actualUVContainer.Add(magnifyingGlassOverlay);
            }
            
            // Create island list using service
            var listConfig = new IslandListConfig
            {
                MakeItem = CreateIslandListItem,
                BindItem = BindIslandListItem,
                OnSelectionChanged = OnIslandListSelectionChanged,
                Height = 120
            };
            islandListView = uiBuilderService.CreateIslandList(listConfig);
            
            var listLabel = new Label("UV Islands")
            {
                style = {
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 5
                }
            };
            root.Add(listLabel);
            root.Add(islandListView);
            
            // Create control buttons using service
            var controlButtons = uiBuilderService.CreateControlButtons(RefreshData, ClearSelection);
            root.Add(controlButtons);
            
            // Create status area using service
            var statusComponents = uiBuilderService.CreateStatusArea();
            statusLabel = statusComponents.statusLabel;
            root.Add(statusComponents.container);
            
            // Setup additional event handlers
            SetupAdditionalEventHandlers();
        }
        
        private void OnLanguageChanged(UVIslandLocalization.Language newLanguage)
        {
            RefreshUIText();
        }
        
        private UVMapMouseHandlers CreateMouseHandlers()
        {
            return new UVMapMouseHandlers
            {
                OnMouseDown = OnUVMapMouseDown,
                OnMouseMove = OnUVMapMouseMove,
                OnMouseUp = OnUVMapMouseUp,
                OnWheel = OnUVMapWheel,
                OnContainerMouseMove = OnUVMapContainerMouseMove,
                OnContainerMouseUp = OnUVMapContainerMouseUp
            };
        }
        
        private VisualElement FindUVMapImageContainer(VisualElement uvMapArea)
        {
            // Find the actual UV map container that contains the image element
            // This is a simplified approach - in a real implementation, you might want to
            // structure this differently or use userData/names to identify elements
            return uvMapArea.Q<VisualElement>(null, "uv-map-container") ?? 
                   uvMapArea.Children().FirstOrDefault(child => 
                       child.style.backgroundColor.value.Equals(Color.white) &&
                       child.style.borderBottomWidth.value > 0);
        }
        
        private void SetupAdditionalEventHandlers()
        {
            // Register global mouse events
            root.RegisterCallback<MouseMoveEvent>(OnRootMouseMove, TrickleDown.TrickleDown);
            root.RegisterCallback<MouseUpEvent>(OnRootMouseUp, TrickleDown.TrickleDown);
            
            // Setup zoom slider callback
            if (zoomSlider != null)
            {
                zoomSlider.RegisterValueChangedCallback(evt =>
                {
                    if (selector != null)
                    {
                        selector.SetZoomLevel(evt.newValue);
                        if (selector.AutoUpdatePreview)
                        {
                            UpdateTextureWithThrottle(); // Immediate feedback with throttling
                        }
                    }
                });
            }
            
            // Setup reset zoom button callback
            if (resetZoomButton != null)
            {
                resetZoomButton.clicked += () => 
                {
                    if (selector != null)
                    {
                        selector.ResetViewTransform();
                        if (zoomSlider != null) zoomSlider.value = 1f;
                        if (selector.AutoUpdatePreview)
                        {
                            selector.GenerateUVMapTexture(); // Immediate update for reset button
                            RefreshUVMapImage();
                        }
                    }
                };
            }
            
            // Setup auto update toggle callback
            if (autoUpdateToggle != null)
            {
                autoUpdateToggle.RegisterValueChangedCallback(evt =>
                {
                    if (selector != null)
                    {
                        selector.AutoUpdatePreview = evt.newValue;
                        if (evt.newValue)
                        {
                            RefreshUVMapImage();
                        }
                    }
                });
            }
        }
        
        
        
        
        
        
        
        
        
        private VisualElement CreateIslandListItem()
        {
            var container = new VisualElement
            {
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 5,
                    paddingRight = 5,
                    paddingTop = 3,
                    paddingBottom = 3
                }
            };
            
            var colorBox = new VisualElement
            {
                style = {
                    width = 16,
                    height = 16,
                    marginRight = 8,
                    backgroundColor = Color.gray
                }
            };
            
            var label = new Label
            {
                style = {
                    flexGrow = 1,
                    fontSize = 12
                }
            };
            
            var detailsContainer = new VisualElement
            {
                style = {
                    alignItems = Align.FlexEnd
                }
            };
            
            var vertexCountLabel = new Label
            {
                style = {
                    color = Color.gray,
                    fontSize = 10
                }
            };
            
            var faceCountLabel = new Label
            {
                style = {
                    color = Color.gray,
                    fontSize = 10
                }
            };
            
            detailsContainer.Add(vertexCountLabel);
            detailsContainer.Add(faceCountLabel);
            
            container.Add(colorBox);
            container.Add(label);
            container.Add(detailsContainer);
            
            return container;
        }
        
        private void BindIslandListItem(VisualElement element, int index)
        {
            if (selector?.UVIslands != null && index < selector.UVIslands.Count)
            {
                var island = selector.UVIslands[index];
                var container = element;
                var colorBox = container[0];
                var label = container[1] as Label;
                var detailsContainer = container[2];
                var vertexCountLabel = detailsContainer[0] as Label;
                var faceCountLabel = detailsContainer[1] as Label;
                
                colorBox.style.backgroundColor = island.maskColor;
                label.text = UVIslandLocalization.Get("island_info", island.islandID);
                vertexCountLabel.text = UVIslandLocalization.Get("vertex_count", island.vertexIndices.Count);
                faceCountLabel.text = UVIslandLocalization.Get("face_count", island.faceCount);
                
                // Selection state
                var isSelected = selector.SelectedIslandIDs.Contains(island.islandID);
                container.style.backgroundColor = isSelected ? 
                    new Color(0.3f, 0.5f, 0.8f, 0.3f) : Color.clear;
            }
        }
        
        
        
        
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
        
        // Mouse event handlers (simplified versions)
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
                    StartRangeSelection(localPosition);
                }
                else
                {
                    HandleIslandSelection(localPosition);
                }
                evt.StopPropagation();
            }
            else if (evt.button == 2 && selector.EnableMagnifyingGlass) // Middle click
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
                targetMask.SetSelectedIslands(selector.SelectedIslandIDs);
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
            else
            {
                isDraggingUVMap = true;
                lastMousePos = localPosition;
            }
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
                var panSensitivity = 1f / (UV_MAP_SIZE * Mathf.Max(selector.UvMapZoom, 1f));
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
                    FinishRangeSelection(addToSelection, removeFromSelection);
                }
                
                isDraggingUVMap = false;
                evt.StopPropagation();
            }
            else if (evt.button == 2) // Middle button
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
                var panSensitivity = 1f / (UV_MAP_SIZE * Mathf.Max(selector.UvMapZoom, 1f));
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
                if (isMagnifyingGlassActive)
                {
                    StopMagnifyingGlass();
                    evt.StopPropagation();
                }
            }
        }
        
        // Helper methods
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
        
        private Mesh GetDynamicMesh()
        {
            // Get the current dynamic mesh for highlighting
            if (targetMask?.CachedMesh != null)
            {
                return targetMask.CachedMesh;
            }
            
            // Fallback to original mesh if dynamic mesh is not available
            return GetOriginalMesh();
        }
        
        
        // UI update methods
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
                
                // Immediate UI refresh
                RefreshUVMapImage();
                
                if (selector?.UVIslands != null)
                {
                    islandListView.itemsSource = selector.UVIslands;
                    islandListView.RefreshItems();
                }
                
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
            
            // Force scene repaint for selection highlighting
            SceneView.RepaintAll();
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
            
            var selectedCount = selector.SelectedIslandIDs?.Count ?? 0;
            var maskedVertexCount = selector.VertexMask?.Length ?? 0;
            var maskedFaceCount = (selector.TriangleMask?.Length ?? 0) / 3;
            
            if (selectedCount > 0)
            {
                statusLabel.text = UVIslandLocalization.Get("status_islands_selected", 
                    selectedCount, maskedVertexCount, maskedFaceCount);
            }
            else
            {
                int islandCount = selector.UVIslands?.Count ?? 0;
                statusLabel.text = UVIslandLocalization.Get("status_islands_found", islandCount);
            }
        }
        
        private void ClearSelection()
        {
            if (selector == null) return;
            
            Undo.RecordObject(targetMask, "Clear UV Island Selection");
            selector.ClearSelection();
            targetMask.SetSelectedIslands(selector.SelectedIslandIDs);
            EditorUtility.SetDirty(targetMask);
            RefreshUI(false);
        }
        
        
        // Range selection methods (simplified)
        private void StartRangeSelection(Vector2 localPos)
        {
            // Use proper coordinate transformation that accounts for zoom and pan
            var uvCoordinate = LocalPosToUV(localPos);
            selector.StartRangeSelection(uvCoordinate);
            isRangeSelecting = true;
            UpdateRangeSelectionVisual();
        }
        
        private void UpdateRangeSelection(Vector2 localPos)
        {
            // Use proper coordinate transformation that accounts for zoom and pan
            var uvCoordinate = LocalPosToUV(localPos);
            selector.UpdateRangeSelection(uvCoordinate);
            UpdateRangeSelectionVisual();
        }
        
        private void FinishRangeSelection(bool addToSelection, bool removeFromSelection = false)
        {
            selector.FinishRangeSelection(addToSelection, removeFromSelection);
            rangeSelectionOverlay.style.display = DisplayStyle.None;
            targetMask.SetSelectedIslands(selector.SelectedIslandIDs);
            EditorUtility.SetDirty(targetMask);
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
            
            rangeSelectionOverlay.style.left = left;
            rangeSelectionOverlay.style.top = top;
            rangeSelectionOverlay.style.width = width;
            rangeSelectionOverlay.style.height = height;
            rangeSelectionOverlay.style.display = DisplayStyle.Flex;
        }
        
        // Magnifying glass methods (simplified)
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
                Undo.RecordObject(targetMask, "Select UV Island from Magnifying Glass");
                selector.ToggleIslandSelection(islandID);
                targetMask.SetSelectedIslands(selector.SelectedIslandIDs);
                EditorUtility.SetDirty(targetMask);
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
            
            targetMask.SetSelectedIslands(selector.SelectedIslandIDs);
            EditorUtility.SetDirty(targetMask);
            RefreshUIFast();
        }
        
        private void OnEnable()
        {
            targetMask = target as UVIslandMask;
            
            // Track active editor instances to prevent duplicates using cache manager
            int targetID = targetMask != null ? targetMask.GetInstanceID() : 0;
            UVIslandCacheManager.RegisterActiveEditor(targetID, this);
            
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.duringSceneGui += OnSceneGUI;
        }
        
        private void OnDisable()
        {
            CleanupEditor();
        }
        
        public void CleanupEditor()
        {
            // Remove from active editors tracking using cache manager
            int targetID = targetMask != null ? targetMask.GetInstanceID() : 0;
            UVIslandCacheManager.UnregisterActiveEditor(targetID, this);
            
            // Clean up resources
            if (magnifyingGlassTexture != null)
            {
                DestroyImmediate(magnifyingGlassTexture);
                magnifyingGlassTexture = null;
            }
            
            // Keep cached selector for reuse - only dispose when editor is destroyed
            // cachedSelector will be reused for better performance
            
            Undo.undoRedoPerformed -= OnUndoRedo;
            SceneView.duringSceneGui -= OnSceneGUI;
        }
        
        private void OnDestroy()
        {
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
        
        private void OnSceneGUI(SceneView sceneView)
        {
            if (selector == null || !selector.HasSelectedIslands) return;
            
            // Use proper renderer transform for scene highlighting
            var rendererTransform = GetRendererTransform();
            if (rendererTransform != null)
            {
                selector.TargetTransform = rendererTransform;
                
                // Set dynamic mesh for highlighting if available
                var dynamicMesh = GetDynamicMesh();
                selector.DynamicMesh = dynamicMesh;
                
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
        
        /// <summary>
        /// Centralized handler for user interactions that should trigger full-resolution mode
        /// </summary>
        private void OnUserInteraction()
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
        /// Load low-resolution UV texture from cache manager
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
                currentLowResTexture = UVIslandCacheManager.LoadLowResTextureFromCache(currentCacheKey);
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
        /// Save low-resolution UV texture to cache manager
        /// </summary>
        private void SaveLowResTextureToCache()
        {
            if (string.IsNullOrEmpty(currentCacheKey) || selector == null)
            {
                return;
            }
            
            UVIslandCacheManager.SaveLowResTextureToCache(currentCacheKey, selector);
        }
        
        #region Cache Management and Health Monitoring
        
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
                    Debug.Log(logMessage);
                }
            }
        }
        
        #endregion
        
    }
}