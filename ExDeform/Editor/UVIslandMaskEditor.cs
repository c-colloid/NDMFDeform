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
        
        // Persistent cache system based on original mesh
        private static Dictionary<string, UVIslandSelector> persistentCache = new Dictionary<string, UVIslandSelector>();
        private string currentCacheKey;
        
        // Texture generation control
        private bool textureInitialized = false;
        private float lastUpdateTime = 0f;
        private const float TEXTURE_UPDATE_THROTTLE = 0.016f; // ~60fps limit
        
        // EditorApplication callback management
        private EditorApplication.CallbackFunction pendingTextureUpdate;
        
        public override VisualElement CreateInspectorGUI()
        {
            targetMask = target as UVIslandMask;
            
            // Get original mesh for UV mapping and cache key generation
            var originalMesh = GetOriginalMesh();
            currentCacheKey = GenerateCacheKey(originalMesh);
            
            // Try to get selector from persistent cache first
            if (currentCacheKey != null && persistentCache.TryGetValue(currentCacheKey, out var cachedSelector))
            {
                // Reuse cached selector with UV data
                selector = cachedSelector;
                selector.SetSelectedIslands(targetMask.SelectedIslandIDs);
                
                // Update target transform for dynamic mesh highlighting
                selector.TargetTransform = GetRendererTransform();
                
                // Ensure texture is generated if not already available
                if (selector.UvMapTexture == null)
                {
                    isInitialized = false; // Force refresh to generate texture
                    textureInitialized = false;
                }
                else
                {
                    isInitialized = true; // Use existing texture
                    textureInitialized = true;
                }
            }
            else if (originalMesh != null)
            {
                // Create new selector based on original mesh
                selector = new UVIslandSelector(originalMesh);
                selector.SetSelectedIslands(targetMask.SelectedIslandIDs);
                selector.TargetTransform = GetRendererTransform();
                
                // Cache the selector for future use
                if (currentCacheKey != null)
                {
                    persistentCache[currentCacheKey] = selector;
                }
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
            CreateDisplaySettings();
            CreateUVMapArea();
            CreateIslandList();
            CreateControlButtons();
            CreateStatusArea();
            
            // Register global mouse events
            root.RegisterCallback<MouseMoveEvent>(OnRootMouseMove, TrickleDown.TrickleDown);
            root.RegisterCallback<MouseUpEvent>(OnRootMouseUp, TrickleDown.TrickleDown);
            
            // Optimized initialization - avoid heavy operations when possible
            if (!isInitialized)
            {
                // Only perform full refresh if selector is newly created
                if (selector != null)
                {
                    RefreshData();
                }
            }
            else
            {
                // Quick refresh for cached data - no heavy computation
                RefreshUIFast();
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
                    SceneView.RepaintAll();
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
                    SceneView.RepaintAll();
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
                    SceneView.RepaintAll();
                }
            });
            
            settingsContainer.Add(adaptiveVertexSizeToggle);
            settingsContainer.Add(vertexSizeSlider);
            settingsContainer.Add(adaptiveMultiplierSlider);
            root.Add(settingsContainer);
        }
        
        private void CreateUVMapArea()
        {
            var uvMapLabel = new Label();
            SetLocalizedContent(uvMapLabel, "header_preview");
            uvMapLabel.style.fontSize = 14;
            uvMapLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            uvMapLabel.style.marginBottom = 5;
            root.Add(uvMapLabel);
            
            // UV map preview settings
            var previewSettings = new VisualElement
            {
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
                label.text = UVIslandLocalization.Get("island_info", island.islandID);
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
            // Use the same coordinate conversion as the old working code
            var normalizedPos = new Vector2(
                localPosition.x / UV_MAP_SIZE,
                1f - (localPosition.y / UV_MAP_SIZE)
            );
            
            int islandID = selector.GetIslandAtUVCoordinate(normalizedPos);
            
            if (islandID >= 0)
            {
                Undo.RecordObject(targetMask, "Toggle UV Island Selection");
                selector.ToggleIslandSelection(islandID);
                targetMask.SetSelectedIslands(selector.SelectedIslandIDs);
                EditorUtility.SetDirty(targetMask);
                
                // Mark texture for update when selection changes
                textureInitialized = false;
                
                // Use optimized immediate refresh for better responsiveness
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
        
        private string GenerateCacheKey(Mesh originalMesh)
        {
            if (originalMesh == null) return null;
            
            // Use original mesh instance ID and UV hash for cache key
            int meshID = originalMesh.GetInstanceID();
            int uvHash = CalculateUVHash(originalMesh.uv);
            return $"{meshID}_{uvHash}";
        }
        
        private int CalculateUVHash(Vector2[] uvs)
        {
            if (uvs == null || uvs.Length == 0) return 0;
            
            unchecked
            {
                int hash = 17;
                // Sample every 10th UV coordinate to balance performance and accuracy
                int step = Mathf.Max(1, uvs.Length / 100);
                for (int i = 0; i < uvs.Length; i += step)
                {
                    hash = hash * 31 + uvs[i].GetHashCode();
                }
                return hash;
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
            
            // Only handle texture generation if not already initialized
            if (selector != null && !textureInitialized)
            {
                if (selector.UvMapTexture == null)
                {
                    // Generate texture only once during initialization
                    selector.GenerateUVMapTexture();
                    textureInitialized = true;
                }
                RefreshUVMapImage();
            }
            else if (selector?.AutoUpdatePreview == true)
            {
                // Only update texture if auto update is enabled and changes are pending
                selector.UpdateTextureIfNeeded();
                RefreshUVMapImage();
            }
            
            // Force scene repaint for selection highlighting
            SceneView.RepaintAll();
        }
        
        private void RefreshUVMapImage()
        {
            if (selector?.UvMapTexture != null)
            {
                uvMapImage.style.backgroundImage = new StyleBackground(selector.UvMapTexture);
            }
            else
            {
                // Clear image if texture is not available
                uvMapImage.style.backgroundImage = StyleKeyword.None;
            }
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
                pendingTextureUpdate = new EditorApplication.CallbackFunction(() =>
                {
                    if (selector != null)
                    {
                        selector.GenerateUVMapTexture();
                        RefreshUVMapImage();
                        lastUpdateTime = Time.realtimeSinceStartup;
                    }
                    pendingTextureUpdate = null;
                });
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
            var normalizedPos = new Vector2(
                localPos.x / UV_MAP_SIZE,
                1f - (localPos.y / UV_MAP_SIZE)
            );
            selector.StartRangeSelection(normalizedPos);
            isRangeSelecting = true;
            UpdateRangeSelectionVisual();
        }
        
        private void UpdateRangeSelection(Vector2 localPos)
        {
            var normalizedPos = new Vector2(
                localPos.x / UV_MAP_SIZE,
                1f - (localPos.y / UV_MAP_SIZE)
            );
            selector.UpdateRangeSelection(normalizedPos);
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
            var normalizedPos = new Vector2(
                currentMagnifyingMousePos.x / UV_MAP_SIZE,
                1f - (currentMagnifyingMousePos.y / UV_MAP_SIZE)
            );
            
            int islandID = selector.GetIslandAtUVCoordinate(normalizedPos);
            
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
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.duringSceneGui += OnSceneGUI;
        }
        
        private void OnDisable()
        {
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
        }
        
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
        }
        
        // Clean up on editor shutdown
        static UVIslandMaskEditor()
        {
            EditorApplication.quitting += () =>
            {
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
        
    }
}