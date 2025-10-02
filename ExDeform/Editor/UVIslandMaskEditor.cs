using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using System.Linq;
using System.Collections.Generic;

namespace Deform.Masking.Editor
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
        private VisualElement submeshSelector;
        private Label currentSubmeshLabel;
        private Button prevSubmeshButton;
        private Button nextSubmeshButton;
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

        // UI element references for post-initialization updates
        private VisualElement submeshSelectorContainer;
        private VisualElement noSubmeshLabel;
        private VisualElement highlightSettingsContainer;
        private VisualElement noSelectorLabel;
        private Slider highlightOpacitySlider;
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
            
            // Initialize with low-res cache if available, full-res only on user interaction
            if (selector != null)
            {
                // Selector exists (cached): try to load low-res cache for immediate display
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
                    // No cache available for existing selector: generate texture asynchronously
                    ShowPlaceholderMessage("Initializing UV Map...");

                    EditorApplication.delayCall += () =>
                    {
                        if (selector != null && targetMask != null)
                        {
                            RefreshDataWithImmediteTexture();
                        }
                    };
                }
            }
            else if (originalMesh != null)
            {
                // Selector doesn't exist yet: create in background to avoid blocking UI
                ShowPlaceholderMessage("Initializing UV Map...");

                EditorApplication.delayCall += () =>
                {
                    // Null check in case Inspector was closed
                    if (targetMask == null) return;

                    // Get mesh again to ensure it's still valid
                    var mesh = GetOriginalMesh();
                    if (mesh == null) return;

                    // Create new selector with full mesh analysis (300-1000ms, now in background)
                    selector = new UVIslandSelector(mesh);
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

                    selector.TargetTransform = GetRendererTransform();

                    // Cache the selector for future use (using mesh-based key, not submesh-based)
                    string cacheKey = GenerateCacheKey(mesh, 0); // Use submesh 0 as base key
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
                    lastCachedMesh = mesh;
                    lastMeshInstanceID = mesh?.GetInstanceID() ?? -1;

                    // Now refresh with full data
                    RefreshDataWithImmediteTexture();
                };
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
        
        private void CreateLanguageSelector()
        {
            var languageContainer = new VisualElement
            {
                style = { 
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 10,
                    paddingTop = 5,
                    paddingBottom = 5,
                    backgroundColor = new Color(0.3f, 0.3f, 0.4f, 0.2f)
                }
            };
            
            var languageLabel = new Label("Language / 言語:");
            languageLabel.style.marginRight = 10;
            languageLabel.style.fontSize = 11;
            
            languageField = new EnumField(UVIslandLocalization.CurrentLanguage);
            languageField.style.width = 100;
            languageField.RegisterValueChangedCallback(evt =>
            {
                UVIslandLocalization.CurrentLanguage = (UVIslandLocalization.Language)evt.newValue;
                RefreshUIText();
            });
            
            languageContainer.Add(languageLabel);
            languageContainer.Add(languageField);
            root.Add(languageContainer);
        }
        
        private void CreateHeader()
        {
            var headerLabel = new Label(UVIslandLocalization.Get("header_selection"))
            {
                style = {
                    fontSize = 16,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 10
                }
            };
            root.Add(headerLabel);
            
            var description = new Label("UV Island based mask for mesh deformation")
            {
                style = {
                    fontSize = 11,
                    color = Color.gray,
                    marginBottom = 15,
                    whiteSpace = WhiteSpace.Normal
                }
            };
            root.Add(description);
        }
        
        private void CreateMaskSettings()
        {
            var maskContainer = CreateSection("Mask Settings / マスク設定");

            // Invert mask toggle
            var invertMaskToggle = new Toggle("Invert Mask / マスク反転")
            {
                value = targetMask.InvertMask
            };
            invertMaskToggle.tooltip = "反転したマスクを適用します";
            invertMaskToggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(targetMask, "Toggle Invert Mask");
                targetMask.InvertMask = evt.newValue;
                EditorUtility.SetDirty(targetMask);
            });

            // Mask strength slider
            var maskStrengthSlider = new Slider("Mask Strength / マスク強度", 0f, 1f)
            {
                value = targetMask.MaskStrength
            };
            maskStrengthSlider.tooltip = "マスクの強度を調整します (0 = 効果なし, 1 = 完全なマスク)";
            maskStrengthSlider.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(targetMask, "Change Mask Strength");
                targetMask.MaskStrength = evt.newValue;
                EditorUtility.SetDirty(targetMask);
            });

            maskContainer.Add(invertMaskToggle);
            maskContainer.Add(maskStrengthSlider);
            root.Add(maskContainer);
        }

        private void CreateSubmeshSelector()
        {
            submeshSelectorContainer = CreateSection("Submesh Selection / サブメッシュ選択");

            if (selector == null || selector.SubmeshCount == 0)
            {
                noSubmeshLabel = new Label("Initializing submesh data... / サブメッシュデータを初期化中...");
                noSubmeshLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                submeshSelectorContainer.Add(noSubmeshLabel);
                root.Add(submeshSelectorContainer);
                return;
            }

            // Create submesh choice list
            var choices = new List<string>();
            for (int i = 0; i < selector.SubmeshCount; i++)
            {
                choices.Add($"Submesh {i}");
            }

            // Calculate initial mask value from selected submeshes
            int initialMask = 0;
            foreach (int submeshIndex in targetMask.SelectedSubmeshes)
            {
                if (submeshIndex < selector.SubmeshCount)
                    initialMask |= (1 << submeshIndex);
            }

            // Create MaskField for multi-selection
            var maskField = new MaskField("Selected Submeshes", choices, initialMask)
            {
                tooltip = "複数のサブメッシュを選択できます (少なくとも1つ必須)"
            };

            maskField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(targetMask, "Change Submesh Selection");
                var submeshes = new List<int>();

                // Convert mask to list of indices
                for (int i = 0; i < selector.SubmeshCount; i++)
                {
                    if ((evt.newValue & (1 << i)) != 0)
                    {
                        submeshes.Add(i);
                    }
                }

                // Ensure at least one submesh is selected
                if (submeshes.Count == 0)
                {
                    submeshes.Add(0);
                    // Update mask field value
                    maskField.SetValueWithoutNotify(1);
                }

                targetMask.SetSelectedSubmeshes(submeshes);
                selector.SetSelectedSubmeshes(submeshes);
                EditorUtility.SetDirty(targetMask);
                UpdateSubmeshNavigationVisibility();
                RefreshUI(false);
            });

            submeshSelectorContainer.Add(maskField);
            root.Add(submeshSelectorContainer);
        }

        private void CreateHighlightSettings()
        {
            highlightSettingsContainer = CreateSection("Highlight Settings / ハイライト設定");

            if (selector == null)
            {
                noSelectorLabel = new Label("Initializing highlight settings... / ハイライト設定を初期化中...");
                noSelectorLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                highlightSettingsContainer.Add(noSelectorLabel);
                root.Add(highlightSettingsContainer);
                return;
            }

            // Highlight opacity slider
            highlightOpacitySlider = new Slider("Highlight Opacity / ハイライト不透明度", 0f, 1f)
            {
                value = selector.HighlightOpacity,
                showInputField = true
            };
            highlightOpacitySlider.tooltip = "ハイライトの不透明度を調整します (0 = 完全に透明, 1 = 不透明)\nAdjust highlight opacity (0 = fully transparent, 1 = opaque)";
            highlightOpacitySlider.RegisterValueChangedCallback(evt =>
            {
                if (selector != null)
                {
                    selector.HighlightOpacity = evt.newValue;
                    // Repaint scene view immediately to show opacity change
                    if (selector.HasSelectedIslands)
                    {
                        SceneView.RepaintAll();
                    }
                }
            });

            highlightSettingsContainer.Add(highlightOpacitySlider);
            root.Add(highlightSettingsContainer);
        }

        private void CreateDisplaySettings()
        {
            var settingsContainer = CreateSection(UVIslandLocalization.Get("header_display"));
            
            // Adaptive vertex size
            adaptiveVertexSizeToggle = new Toggle()
            {
                value = selector?.UseAdaptiveVertexSize ?? true
            };
            SetLocalizedContent(adaptiveVertexSizeToggle, "adaptive_vertex_size", "tooltip_adaptive_size");
            adaptiveVertexSizeToggle.RegisterValueChangedCallback(evt =>
            {
                if (selector != null)
                {
                    selector.UseAdaptiveVertexSize = evt.newValue;
                    vertexSizeSlider.SetEnabled(!evt.newValue);
                    adaptiveMultiplierSlider.SetEnabled(evt.newValue);
                    // Only repaint if there are selected islands to display
                    if (selector.HasSelectedIslands)
                    {
                        SceneView.RepaintAll();
                    }
                }
            });
            
            // Manual vertex size
            vertexSizeSlider = new Slider("", 0.001f, 0.1f)
            {
                value = selector?.ManualVertexSphereSize ?? 0.01f
            };
            SetLocalizedContent(vertexSizeSlider, "manual_vertex_size", "tooltip_manual_size");
            vertexSizeSlider.RegisterValueChangedCallback(evt =>
            {
                if (selector != null)
                {
                    selector.ManualVertexSphereSize = evt.newValue;
                    // Only repaint if there are selected islands to display
                    if (selector.HasSelectedIslands)
                    {
                        SceneView.RepaintAll();
                    }
                }
            });
            
            // Adaptive multiplier
            adaptiveMultiplierSlider = new Slider("", 0.001f, 0.02f)
            {
                value = selector?.AdaptiveSizeMultiplier ?? 0.007f
            };
            SetLocalizedContent(adaptiveMultiplierSlider, "size_multiplier", "tooltip_size_multiplier");
            adaptiveMultiplierSlider.RegisterValueChangedCallback(evt =>
            {
                if (selector != null)
                {
                    selector.AdaptiveSizeMultiplier = evt.newValue;
                    // Only repaint if there are selected islands to display
                    if (selector.HasSelectedIslands)
                    {
                        SceneView.RepaintAll();
                    }
                }
            });
            
            settingsContainer.Add(adaptiveVertexSizeToggle);
            settingsContainer.Add(vertexSizeSlider);
            settingsContainer.Add(adaptiveMultiplierSlider);
            root.Add(settingsContainer);
        }
        
        private void CreateUVMapArea()
        {
            // Header with submesh navigation
            var headerContainer = new VisualElement
            {
                name = "uvMapHeader",
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 5
                }
            };

            var uvMapLabel = new Label();
            SetLocalizedContent(uvMapLabel, "header_preview");
            uvMapLabel.style.fontSize = 14;
            uvMapLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            uvMapLabel.style.flexGrow = 1;
            headerContainer.Add(uvMapLabel);

            // Submesh preview navigation - always create, control visibility
            submeshSelector = new VisualElement
            {
                name = "submeshNavigationContainer",
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center
                }
            };

            prevSubmeshButton = new Button(() =>
            {
                // Save current submesh's cache before switching
                SaveLowResTextureToCache();

                // Switch to previous submesh
                selector.PreviousPreviewSubmesh();

                // Update cache key for new submesh
                var originalMesh = GetOriginalMesh();
                currentCacheKey = GenerateCacheKey(originalMesh, selector.CurrentPreviewSubmesh);

                // Load new submesh's cache
                LoadLowResTextureFromCache();

                // Update UI
                UpdateSubmeshLabel();
                RebuildIslandList();

                // Show cached low-res texture if available, otherwise generate full
                if (currentLowResTexture != null)
                {
                    shouldShowLowResUntilInteraction = true;
                    RefreshUIFast();
                }
                else
                {
                    RefreshUVMapImage();
                }
            })
            {
                text = "◀",
                style = { width = 30, marginRight = 5 }
            };
            prevSubmeshButton.tooltip = "Previous submesh / 前のサブメッシュ";

            currentSubmeshLabel = new Label($"Submesh {selector?.CurrentPreviewSubmesh ?? 0}")
            {
                style = {
                    fontSize = 11,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    minWidth = 80
                }
            };

            nextSubmeshButton = new Button(() =>
            {
                // Save current submesh's cache before switching
                SaveLowResTextureToCache();

                // Switch to next submesh
                selector.NextPreviewSubmesh();

                // Update cache key for new submesh
                var originalMesh = GetOriginalMesh();
                currentCacheKey = GenerateCacheKey(originalMesh, selector.CurrentPreviewSubmesh);

                // Load new submesh's cache
                LoadLowResTextureFromCache();

                // Update UI
                UpdateSubmeshLabel();
                RebuildIslandList();

                // Show cached low-res texture if available, otherwise generate full
                if (currentLowResTexture != null)
                {
                    shouldShowLowResUntilInteraction = true;
                    RefreshUIFast();
                }
                else
                {
                    RefreshUVMapImage();
                }
            })
            {
                text = "▶",
                style = { width = 30, marginLeft = 5 }
            };
            nextSubmeshButton.tooltip = "Next submesh / 次のサブメッシュ";

            submeshSelector.Add(prevSubmeshButton);
            submeshSelector.Add(currentSubmeshLabel);
            submeshSelector.Add(nextSubmeshButton);

            headerContainer.Add(submeshSelector);

            // Update visibility based on selection
            UpdateSubmeshNavigationVisibility();

            root.Add(headerContainer);
            
            // UV map preview settings
            var previewSettings = new VisualElement
            {
	            name = "previewSettings",
                style = { 
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 5
                }
            };
            
            autoUpdateToggle = new Toggle()
            {
                value = selector?.AutoUpdatePreview ?? true
            };
            SetLocalizedContent(autoUpdateToggle, "auto_update", "tooltip_auto_update");
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
            
            var zoomContainer = new VisualElement 
            { 
                style = { 
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginLeft = 15
                } 
            };
            
            zoomSlider = new Slider("", 1f, 8f)
            {
                value = selector?.UvMapZoom ?? 1f,
                style = { flexGrow = 1, marginRight = 10, width = 100 }
            };
            SetLocalizedContent(zoomSlider, "zoom_level", "tooltip_zoom");
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
            
            resetZoomButton = new Button(() => 
            {
                if (selector != null)
                {
                    selector.ResetViewTransform();
                    zoomSlider.value = 1f;
                    if (selector.AutoUpdatePreview)
                    {
                        selector.GenerateUVMapTexture(); // Immediate update for reset button
                        RefreshUVMapImage();
                    }
                }
            })
            {
                style = { width = 50 }
            };
            SetLocalizedContent(resetZoomButton, "reset");
            
            zoomContainer.Add(zoomSlider);
            zoomContainer.Add(resetZoomButton);
            
            previewSettings.Add(autoUpdateToggle);
            previewSettings.Add(zoomContainer);
            root.Add(previewSettings);
            
            // Magnifying glass settings
            var magnifyingSettings = new VisualElement
            {
                style = { 
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 5
                }
            };
            
            magnifyingToggle = new Toggle()
            {
                value = selector?.EnableMagnifyingGlass ?? true
            };
            SetLocalizedContent(magnifyingToggle, "magnifying_glass", "tooltip_magnifying");
            magnifyingToggle.RegisterValueChangedCallback(evt =>
            {
                if (selector != null)
                {
                    selector.EnableMagnifyingGlass = evt.newValue;
                    magnifyingSizeSlider.SetEnabled(evt.newValue);
                }
            });
            
            var magnifyingContainer = new VisualElement 
            { 
                style = { 
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginLeft = 15
                } 
            };
            
            // Replace slider with buttons for x2, x4, x8, x16 zoom
            var zoomButtonContainer = new VisualElement 
            { 
                style = { 
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center
                } 
            };
            
            var zoom2xButton = new Button(() => SetMagnifyingZoom(2f)) { text = "x2" };
            var zoom4xButton = new Button(() => SetMagnifyingZoom(4f)) { text = "x4" };
            var zoom8xButton = new Button(() => SetMagnifyingZoom(8f)) { text = "x8" };
            var zoom16xButton = new Button(() => SetMagnifyingZoom(16f)) { text = "x16" };
            
            zoom2xButton.style.marginRight = 2;
            zoom4xButton.style.marginRight = 2;
            zoom8xButton.style.marginRight = 2;
            zoom16xButton.style.marginRight = 2;
            
            zoomButtonContainer.Add(zoom2xButton);
            zoomButtonContainer.Add(zoom4xButton);
            zoomButtonContainer.Add(zoom8xButton);
            zoomButtonContainer.Add(zoom16xButton);
            
            magnifyingSizeSlider = new Slider("", 80f, 150f)
            {
                value = selector?.MagnifyingGlassSize ?? 100f,
                style = { flexGrow = 1, marginRight = 10, width = 80 }
            };
            SetLocalizedContent(magnifyingSizeSlider, "magnifying_size", "tooltip_magnifying_size");
            magnifyingSizeSlider.RegisterValueChangedCallback(evt =>
            {
                if (selector != null)
                {
                    selector.MagnifyingGlassSize = evt.newValue;
                }
            });
            
            magnifyingContainer.Add(zoomButtonContainer);
            magnifyingContainer.Add(magnifyingSizeSlider);
            magnifyingSettings.Add(magnifyingToggle);
            magnifyingSettings.Add(magnifyingContainer);
            root.Add(magnifyingSettings);
            
            // UV map container
            uvMapContainer = new VisualElement
            {
                style = {
                    width = UV_MAP_SIZE,
                    height = UV_MAP_SIZE,
                    backgroundColor = Color.white,
                    borderBottomColor = Color.gray,
                    borderBottomWidth = 1,
                    borderTopColor = Color.gray,
                    borderTopWidth = 1,
                    borderLeftColor = Color.gray,
                    borderLeftWidth = 1,
                    borderRightColor = Color.gray,
                    borderRightWidth = 1,
                    marginBottom = 15,
                    alignSelf = Align.Center
                }
            };
            
            uvMapImage = new VisualElement
            {
                style = {
                    width = UV_MAP_SIZE,
                    height = UV_MAP_SIZE,
                    backgroundImage = null
                }
            };
            
            // Register mouse events
            uvMapImage.RegisterCallback<MouseDownEvent>(OnUVMapMouseDown, TrickleDown.TrickleDown);
            uvMapImage.RegisterCallback<MouseMoveEvent>(OnUVMapMouseMove, TrickleDown.TrickleDown);
            uvMapImage.RegisterCallback<MouseUpEvent>(OnUVMapMouseUp, TrickleDown.TrickleDown);
            uvMapImage.RegisterCallback<WheelEvent>(OnUVMapWheel, TrickleDown.TrickleDown);
            
            uvMapContainer.RegisterCallback<MouseMoveEvent>(OnUVMapContainerMouseMove, TrickleDown.TrickleDown);
            uvMapContainer.RegisterCallback<MouseUpEvent>(OnUVMapContainerMouseUp, TrickleDown.TrickleDown);
            
            SetLocalizedTooltip(uvMapImage, "controls_uv_map");
            
            uvMapContainer.Add(uvMapImage);
            
            // Create overlays
            CreateRangeSelectionOverlay();
            CreateMagnifyingGlassOverlay();
            
            uvMapContainer.Add(rangeSelectionOverlay);
            uvMapContainer.Add(magnifyingGlassOverlay);
            
            root.Add(uvMapContainer);
        }
        
        private void CreateRangeSelectionOverlay()
        {
            rangeSelectionOverlay = new VisualElement
            {
                style = {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    right = 0,
                    bottom = 0,
                    backgroundColor = new Color(0.3f, 0.5f, 0.8f, 0.3f),
                    borderLeftColor = new Color(0.3f, 0.5f, 0.8f, 0.8f),
                    borderRightColor = new Color(0.3f, 0.5f, 0.8f, 0.8f),
                    borderTopColor = new Color(0.3f, 0.5f, 0.8f, 0.8f),
                    borderBottomColor = new Color(0.3f, 0.5f, 0.8f, 0.8f),
                    borderLeftWidth = 2,
                    borderRightWidth = 2,
                    borderTopWidth = 2,
                    borderBottomWidth = 2,
                    display = DisplayStyle.None
                }
            };
        }
        
        private void CreateMagnifyingGlassOverlay()
        {
            magnifyingGlassOverlay = new VisualElement
            {
                style = {
                    position = Position.Absolute,
                    width = 120,
                    height = 140,
                    backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.95f),
                    borderLeftColor = Color.white,
                    borderRightColor = Color.white,
                    borderTopColor = Color.white,
                    borderBottomColor = Color.white,
                    borderLeftWidth = 2,
                    borderRightWidth = 2,
                    borderTopWidth = 2,
                    borderBottomWidth = 2,
                    borderTopLeftRadius = 8,
                    borderTopRightRadius = 8,
                    borderBottomLeftRadius = 8,
                    borderBottomRightRadius = 8,
                    display = DisplayStyle.None,
                    paddingTop = 5,
                    paddingBottom = 5,
                    paddingLeft = 5,
                    paddingRight = 5
                }
            };
            
            magnifyingGlassLabel = new Label
            {
                style = {
                    fontSize = 10,
                    color = Color.white,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    height = 15,
                    marginBottom = 3
                }
            };
            
            magnifyingGlassImage = new VisualElement
            {
                style = {
                    flexGrow = 1,
                    backgroundColor = Color.black,
                    position = Position.Relative
                }
            };
            
            CreateMagnifyingGlassReticle();
            
            magnifyingGlassOverlay.Add(magnifyingGlassLabel);
            magnifyingGlassOverlay.Add(magnifyingGlassImage);
        }
        
        private void CreateMagnifyingGlassReticle()
        {
            // Vertical line
            var verticalLine = new VisualElement
            {
                style = {
                    position = Position.Absolute,
                    left = new StyleLength(new Length(50, LengthUnit.Percent)),
                    top = new StyleLength(new Length(20, LengthUnit.Percent)),
                    bottom = new StyleLength(new Length(20, LengthUnit.Percent)),
                    width = 2,
                    backgroundColor = Color.red,
                    marginLeft = -1
                }
            };
            
            // Horizontal line
            var horizontalLine = new VisualElement
            {
                style = {
                    position = Position.Absolute,
                    top = new StyleLength(new Length(50, LengthUnit.Percent)),
                    left = new StyleLength(new Length(20, LengthUnit.Percent)),
                    right = new StyleLength(new Length(20, LengthUnit.Percent)),
                    height = 2,
                    backgroundColor = Color.red,
                    marginTop = -1
                }
            };
            
            magnifyingGlassImage.Add(verticalLine);
            magnifyingGlassImage.Add(horizontalLine);
        }
        
        private void CreateIslandList()
        {
            var listLabel = new Label("UV Islands");
            listLabel.style.fontSize = 14;
            listLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            listLabel.style.marginBottom = 5;
            root.Add(listLabel);
            
            islandListView = new ListView
            {
                style = {
                    height = 120,
                    marginBottom = 10
                },
                selectionType = SelectionType.Multiple,
                reorderable = false
            };
            
            islandListView.makeItem = () => CreateIslandListItem();
            islandListView.bindItem = (element, index) => BindIslandListItem(element, index);
            islandListView.onSelectionChange += OnIslandListSelectionChanged;
            
            root.Add(islandListView);
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
                label.text = $"Island {island.islandID} (SM{island.submeshIndex})";
                vertexCountLabel.text = UVIslandLocalization.Get("vertex_count", island.vertexIndices.Count);
                faceCountLabel.text = UVIslandLocalization.Get("face_count", island.faceCount);

                // Selection state
                var isSelected = selector.SelectedIslandIDs.Contains(island.islandID);
                container.style.backgroundColor = isSelected ?
                    new Color(0.3f, 0.5f, 0.8f, 0.3f) : Color.clear;
            }
        }
        
        private void CreateControlButtons()
        {
            var buttonContainer = new VisualElement
            {
                style = {
                    flexDirection = FlexDirection.Row,
                    marginBottom = 10
                }
            };
            
            refreshButton = new Button(() => RefreshData())
            {
                style = {
                    flexGrow = 1,
                    marginRight = 5
                }
            };
            SetLocalizedContent(refreshButton, "refresh");
            
            clearSelectionButton = new Button(() => ClearSelection())
            {
                style = {
                    flexGrow = 1,
                    marginLeft = 5
                }
            };
            SetLocalizedContent(clearSelectionButton, "clear_selection");
            
            buttonContainer.Add(refreshButton);
            buttonContainer.Add(clearSelectionButton);
            root.Add(buttonContainer);
            
        }
        
        private void CreateStatusArea()
        {
            statusLabel = new Label(UVIslandLocalization.Get("status_ready"))
            {
                style = {
                    fontSize = 11,
                    color = Color.gray,
                    marginTop = 10
                }
            };
            root.Add(statusLabel);
        }
        
        private VisualElement CreateSection(string title)
        {
            var section = new VisualElement
            {
                style = {
                    backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.3f),
                    paddingTop = 10,
                    paddingBottom = 10,
                    paddingLeft = 10,
                    paddingRight = 10,
                    marginBottom = 15
                }
            };
            
            var titleLabel = new Label(title)
            {
                style = {
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 10
                }
            };
            section.Add(titleLabel);
            
            return section;
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

                // Force repaint of UV map image to ensure it displays
                if (uvMapImage != null)
                {
                    uvMapImage.MarkDirtyRepaint();
                }

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
                UpdateSubmeshNavigationVisibility();

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
        
        private void ClearSelection()
        {
            if (selector == null) return;

            Undo.RecordObject(targetMask, "Clear UV Island Selection");
            selector.ClearSelection();
            UpdateMaskComponent();
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
        
        private void OnEnable()
        {
            targetMask = target as UVIslandMask;
            
            // Track active editor instances to prevent duplicates
            int targetID = targetMask != null ? targetMask.GetInstanceID() : 0;
            if (targetID != 0)
            {
                if (activeEditors.ContainsKey(targetID))
                {
                    // Another editor instance exists for this target, dispose the old one
                    var oldEditor = activeEditors[targetID];
                    if (oldEditor != this && oldEditor != null)
                    {
                        //Debug.Log($"[UVIslandMaskEditor] Replacing duplicate editor instance for target {targetID}");
                        oldEditor.CleanupEditor();
                    }
                }
                activeEditors[targetID] = this;
            }
            
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.duringSceneGui += OnSceneGUI;
        }
        
        private void OnDisable()
        {
            CleanupEditor();
        }
        
        private void CleanupEditor()
        {
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

        private void UpdateSubmeshNavigationVisibility()
        {
            if (submeshSelector == null) return;

            bool shouldShow = selector != null && selector.SelectedSubmeshIndices.Count > 1;
            submeshSelector.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;

            if (shouldShow)
            {
                UpdateSubmeshLabel();
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
            if (submeshSelectorContainer == null || selector == null) return;

            // Remove placeholder message
            if (noSubmeshLabel != null)
            {
                submeshSelectorContainer.Remove(noSubmeshLabel);
                noSubmeshLabel = null;
            }

            // Create submesh choice list
            var choices = new List<string>();
            for (int i = 0; i < selector.SubmeshCount; i++)
            {
                choices.Add($"Submesh {i}");
            }

            // Calculate initial mask value from selected submeshes
            int initialMask = 0;
            foreach (int submeshIndex in targetMask.SelectedSubmeshes)
            {
                if (submeshIndex < selector.SubmeshCount)
                    initialMask |= (1 << submeshIndex);
            }

            // Create MaskField for multi-selection
            var maskField = new MaskField("Selected Submeshes", choices, initialMask)
            {
                tooltip = "複数のサブメッシュを選択できます (少なくとも1つ必須)"
            };

            maskField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(targetMask, "Change Submesh Selection");
                var submeshes = new List<int>();

                // Convert mask to list of indices
                for (int i = 0; i < selector.SubmeshCount; i++)
                {
                    if ((evt.newValue & (1 << i)) != 0)
                    {
                        submeshes.Add(i);
                    }
                }

                // Ensure at least one submesh is selected
                if (submeshes.Count == 0)
                {
                    submeshes.Add(0);
                    // Update mask field value
                    maskField.SetValueWithoutNotify(1);
                }

                targetMask.SetSelectedSubmeshes(submeshes);
                selector.SetSelectedSubmeshes(submeshes);
                EditorUtility.SetDirty(targetMask);
                UpdateSubmeshNavigationVisibility();
                RefreshUI(false);
            });

            submeshSelectorContainer.Add(maskField);
        }

        /// <summary>
        /// Update Highlight Settings UI after background initialization
        /// バックグラウンド初期化後にハイライト設定UIを更新
        /// </summary>
        private void UpdateHighlightSettingsUI()
        {
            if (highlightSettingsContainer == null || selector == null) return;

            // Remove placeholder message
            if (noSelectorLabel != null)
            {
                highlightSettingsContainer.Remove(noSelectorLabel);
                noSelectorLabel = null;
            }

            // Update or create highlight opacity slider
            if (highlightOpacitySlider == null)
            {
                highlightOpacitySlider = new Slider("Highlight Opacity / ハイライト不透明度", 0f, 1f)
                {
                    value = selector.HighlightOpacity,
                    showInputField = true
                };
                highlightOpacitySlider.tooltip = "ハイライトの不透明度を調整します (0 = 完全に透明, 1 = 不透明)\nAdjust highlight opacity (0 = fully transparent, 1 = opaque)";
                highlightOpacitySlider.RegisterValueChangedCallback(evt =>
                {
                    if (selector != null)
                    {
                        selector.HighlightOpacity = evt.newValue;
                        // Repaint scene view immediately to show opacity change
                        if (selector.HasSelectedIslands)
                        {
                            SceneView.RepaintAll();
                        }
                    }
                });

                highlightSettingsContainer.Add(highlightOpacitySlider);
            }
            else
            {
                // Update existing slider value
                highlightOpacitySlider.value = selector.HighlightOpacity;
            }
        }

        /// <summary>
        /// Update Display Settings UI after background initialization
        /// バックグラウンド初期化後に表示設定UIを更新
        /// </summary>
        private void UpdateDisplaySettingsUI()
        {
            if (selector == null) return;

            // Update toggle and sliders with actual selector values
            if (adaptiveVertexSizeToggle != null)
            {
                adaptiveVertexSizeToggle.value = selector.UseAdaptiveVertexSize;
            }
            if (vertexSizeSlider != null)
            {
                vertexSizeSlider.value = selector.ManualVertexSphereSize;
            }
            if (adaptiveMultiplierSlider != null)
            {
                adaptiveMultiplierSlider.value = selector.AdaptiveSizeMultiplier;
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