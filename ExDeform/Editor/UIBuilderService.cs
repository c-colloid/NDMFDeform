using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace ExDeform.Editor
{
    /// <summary>
    /// Service implementation for UI builder that handles VisualElement construction,
    /// event handler setup, and layout management for UV Island Mask Editor
    /// VisualElement構築、イベントハンドラー設定、レイアウト管理を担当するサービス実装
    /// </summary>
    public class UIBuilderService : IUIBuilderService
    {
        #region Constants
        
        private const float SECTION_PADDING = 10f;
        private const float SECTION_MARGIN = 15f;
        private const int DEFAULT_FONT_SIZE_LABEL = 11;
        private const int DEFAULT_FONT_SIZE_TITLE = 14;
        private const int DEFAULT_FONT_SIZE_HEADER = 16;
        
        #endregion
        
        #region Section Creation Methods
        
        public VisualElement CreateSection(string title)
        {
            var section = new VisualElement
            {
                style = {
                    backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.3f),
                    paddingTop = SECTION_PADDING,
                    paddingBottom = SECTION_PADDING,
                    paddingLeft = SECTION_PADDING,
                    paddingRight = SECTION_PADDING,
                    marginBottom = SECTION_MARGIN
                }
            };
            
            var titleLabel = new Label(title)
            {
                style = {
                    fontSize = DEFAULT_FONT_SIZE_TITLE,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 10
                }
            };
            section.Add(titleLabel);
            
            return section;
        }
        
        public VisualElement CreateLanguageSelector(Action<UVIslandLocalization.Language> onLanguageChanged)
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
            languageLabel.style.fontSize = DEFAULT_FONT_SIZE_LABEL;
            
            var languageField = new EnumField(UVIslandLocalization.CurrentLanguage);
            languageField.style.width = 100;
            languageField.RegisterValueChangedCallback(evt =>
            {
                var newLanguage = (UVIslandLocalization.Language)evt.newValue;
                UVIslandLocalization.CurrentLanguage = newLanguage;
                onLanguageChanged?.Invoke(newLanguage);
            });
            
            languageContainer.Add(languageLabel);
            languageContainer.Add(languageField);
            
            return languageContainer;
        }
        
        public VisualElement CreateHeader()
        {
            var container = new VisualElement();
            
            var headerLabel = new Label(UVIslandLocalization.Get("header_selection"))
            {
                style = {
                    fontSize = DEFAULT_FONT_SIZE_HEADER,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 10
                }
            };
            container.Add(headerLabel);
            
            var description = new Label("UV Island based mask for mesh deformation")
            {
                style = {
                    fontSize = DEFAULT_FONT_SIZE_LABEL,
                    color = Color.gray,
                    marginBottom = 15,
                    whiteSpace = WhiteSpace.Normal
                }
            };
            container.Add(description);
            
            return container;
        }
        
        public VisualElement CreateMaskSettings(UVIslandMask targetMask)
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
            
            return maskContainer;
        }
        
        public VisualElement CreateDisplaySettings(UVIslandSelector selector)
        {
            var settingsContainer = CreateSection(UVIslandLocalization.Get("header_display"));
            
            // Adaptive vertex size
            var adaptiveVertexSizeToggle = new Toggle()
            {
                value = selector?.UseAdaptiveVertexSize ?? true
            };
            SetLocalizedContent(adaptiveVertexSizeToggle, "adaptive_vertex_size", "tooltip_adaptive_size");
            adaptiveVertexSizeToggle.RegisterValueChangedCallback(evt =>
            {
                if (selector != null)
                {
                    selector.UseAdaptiveVertexSize = evt.newValue;
                    // Note: Parent class should handle UI element updates
                    SceneView.RepaintAll();
                }
            });
            
            // Manual vertex size
            var vertexSizeSlider = new Slider("", 0.001f, 0.1f)
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
            var adaptiveMultiplierSlider = new Slider("", 0.001f, 0.02f)
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
            
            // Store references for cross-element interactions
            adaptiveVertexSizeToggle.RegisterValueChangedCallback(evt =>
            {
                vertexSizeSlider.SetEnabled(!evt.newValue);
                adaptiveMultiplierSlider.SetEnabled(evt.newValue);
            });
            
            // Set initial enabled states
            vertexSizeSlider.SetEnabled(!adaptiveVertexSizeToggle.value);
            adaptiveMultiplierSlider.SetEnabled(adaptiveVertexSizeToggle.value);
            
            settingsContainer.Add(adaptiveVertexSizeToggle);
            settingsContainer.Add(vertexSizeSlider);
            settingsContainer.Add(adaptiveMultiplierSlider);
            
            return settingsContainer;
        }
        
        public (VisualElement container, Label statusLabel) CreateStatusArea()
        {
            var container = new VisualElement();
            
            var statusLabel = new Label(UVIslandLocalization.Get("status_ready"))
            {
                style = {
                    fontSize = DEFAULT_FONT_SIZE_LABEL,
                    color = Color.gray,
                    marginTop = 10
                }
            };
            container.Add(statusLabel);
            
            return (container, statusLabel);
        }
        
        #endregion
        
        #region UV Map UI Creation
        
        public UVMapComponents CreateUVMapArea(UVMapConfig config)
        {
            // UV map label
            var uvMapLabel = new Label();
            SetLocalizedContent(uvMapLabel, "header_preview");
            uvMapLabel.style.fontSize = DEFAULT_FONT_SIZE_TITLE;
            uvMapLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            uvMapLabel.style.marginBottom = 5;
            
            // Create container for the entire UV map area
            var mainContainer = new VisualElement();
            mainContainer.Add(uvMapLabel);
            
            // Preview settings
            var previewSettings = CreateUVMapPreviewSettings(config);
            mainContainer.Add(previewSettings.container);
            
            // Magnifying glass settings
            var magnifyingSettings = CreateMagnifyingGlassSettings(config);
            mainContainer.Add(magnifyingSettings.container);
            
            // UV map container
            var uvMapContainer = new VisualElement
            {
                style = {
                    width = config.MapSize,
                    height = config.MapSize,
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
            
            var uvMapImage = new VisualElement
            {
                style = {
                    width = config.MapSize,
                    height = config.MapSize,
                    backgroundImage = null
                }
            };
            
            // Register mouse event handlers
            if (config.MouseHandlers != null)
            {
                RegisterMouseEventHandlers(uvMapImage, config.MouseHandlers);
                
                // Container mouse events
                if (config.MouseHandlers.OnContainerMouseMove != null)
                {
                    uvMapContainer.RegisterCallback<MouseMoveEvent>(config.MouseHandlers.OnContainerMouseMove, TrickleDown.TrickleDown);
                }
                if (config.MouseHandlers.OnContainerMouseUp != null)
                {
                    uvMapContainer.RegisterCallback<MouseUpEvent>(config.MouseHandlers.OnContainerMouseUp, TrickleDown.TrickleDown);
                }
            }
            
            SetLocalizedTooltip(uvMapImage, "controls_uv_map");
            uvMapContainer.Add(uvMapImage);
            mainContainer.Add(uvMapContainer);
            
            return new UVMapComponents
            {
                Container = mainContainer,
                ImageElement = uvMapImage,
                AutoUpdateToggle = previewSettings.autoUpdateToggle,
                ZoomSlider = previewSettings.zoomSlider,
                ResetZoomButton = previewSettings.resetZoomButton,
                MagnifyingToggle = magnifyingSettings.magnifyingToggle,
                MagnifyingSizeSlider = magnifyingSettings.magnifyingSizeSlider
            };
        }
        
        private (VisualElement container, Toggle autoUpdateToggle, Slider zoomSlider, Button resetZoomButton) CreateUVMapPreviewSettings(UVMapConfig config)
        {
            var previewSettings = new VisualElement
            {
                style = { 
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 5
                }
            };
            
            var autoUpdateToggle = new Toggle()
            {
                value = config.Selector?.AutoUpdatePreview ?? true
            };
            SetLocalizedContent(autoUpdateToggle, "auto_update", "tooltip_auto_update");
            autoUpdateToggle.RegisterValueChangedCallback(evt =>
            {
                if (config.Selector != null)
                {
                    config.Selector.AutoUpdatePreview = evt.newValue;
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
            
            var zoomSlider = new Slider("", 1f, 8f)
            {
                value = config.Selector?.UvMapZoom ?? 1f,
                style = { flexGrow = 1, marginRight = 10, width = 100 }
            };
            SetLocalizedContent(zoomSlider, "zoom_level", "tooltip_zoom");
            zoomSlider.RegisterValueChangedCallback(evt =>
            {
                if (config.Selector != null)
                {
                    config.Selector.SetZoomLevel(evt.newValue);
                }
            });
            
            var resetZoomButton = new Button()
            {
                style = { width = 50 }
            };
            SetLocalizedContent(resetZoomButton, "reset");
            resetZoomButton.clicked += () => 
            {
                if (config.Selector != null)
                {
                    config.Selector.ResetViewTransform();
                    zoomSlider.value = 1f;
                }
            };
            
            zoomContainer.Add(zoomSlider);
            zoomContainer.Add(resetZoomButton);
            
            previewSettings.Add(autoUpdateToggle);
            previewSettings.Add(zoomContainer);
            
            return (previewSettings, autoUpdateToggle, zoomSlider, resetZoomButton);
        }
        
        private (VisualElement container, Toggle magnifyingToggle, Slider magnifyingSizeSlider) CreateMagnifyingGlassSettings(UVMapConfig config)
        {
            var magnifyingSettings = new VisualElement
            {
                style = { 
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 5
                }
            };
            
            var magnifyingToggle = new Toggle()
            {
                value = config.Selector?.EnableMagnifyingGlass ?? true
            };
            SetLocalizedContent(magnifyingToggle, "magnifying_glass", "tooltip_magnifying");
            magnifyingToggle.RegisterValueChangedCallback(evt =>
            {
                if (config.Selector != null)
                {
                    config.Selector.EnableMagnifyingGlass = evt.newValue;
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
            
            // Zoom level buttons
            var zoomButtonContainer = new VisualElement 
            { 
                style = { 
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center
                } 
            };
            
            var zoom2xButton = new Button(() => SetMagnifyingZoom(config.Selector, 2f)) { text = "x2" };
            var zoom4xButton = new Button(() => SetMagnifyingZoom(config.Selector, 4f)) { text = "x4" };
            var zoom8xButton = new Button(() => SetMagnifyingZoom(config.Selector, 8f)) { text = "x8" };
            var zoom16xButton = new Button(() => SetMagnifyingZoom(config.Selector, 16f)) { text = "x16" };
            
            zoom2xButton.style.marginRight = 2;
            zoom4xButton.style.marginRight = 2;
            zoom8xButton.style.marginRight = 2;
            zoom16xButton.style.marginRight = 2;
            
            zoomButtonContainer.Add(zoom2xButton);
            zoomButtonContainer.Add(zoom4xButton);
            zoomButtonContainer.Add(zoom8xButton);
            zoomButtonContainer.Add(zoom16xButton);
            
            var magnifyingSizeSlider = new Slider("", 80f, 150f)
            {
                value = config.Selector?.MagnifyingGlassSize ?? 100f,
                style = { flexGrow = 1, marginRight = 10, width = 80 }
            };
            SetLocalizedContent(magnifyingSizeSlider, "magnifying_size", "tooltip_magnifying_size");
            magnifyingSizeSlider.RegisterValueChangedCallback(evt =>
            {
                if (config.Selector != null)
                {
                    config.Selector.MagnifyingGlassSize = evt.newValue;
                }
            });
            
            // Set enabled states
            magnifyingSizeSlider.SetEnabled(magnifyingToggle.value);
            magnifyingToggle.RegisterValueChangedCallback(evt =>
            {
                magnifyingSizeSlider.SetEnabled(evt.newValue);
            });
            
            magnifyingContainer.Add(zoomButtonContainer);
            magnifyingContainer.Add(magnifyingSizeSlider);
            magnifyingSettings.Add(magnifyingToggle);
            magnifyingSettings.Add(magnifyingContainer);
            
            return (magnifyingSettings, magnifyingToggle, magnifyingSizeSlider);
        }
        
        public VisualElement CreateRangeSelectionOverlay()
        {
            return new VisualElement
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
        
        public MagnifyingGlassComponents CreateMagnifyingGlassOverlay()
        {
            var magnifyingGlassOverlay = new VisualElement
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
            
            var magnifyingGlassLabel = new Label
            {
                style = {
                    fontSize = 10,
                    color = Color.white,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    height = 15,
                    marginBottom = 3
                }
            };
            
            var magnifyingGlassImage = new VisualElement
            {
                style = {
                    flexGrow = 1,
                    backgroundColor = Color.black,
                    position = Position.Relative
                }
            };
            
            CreateMagnifyingGlassReticle(magnifyingGlassImage);
            
            magnifyingGlassOverlay.Add(magnifyingGlassLabel);
            magnifyingGlassOverlay.Add(magnifyingGlassImage);
            
            return new MagnifyingGlassComponents
            {
                Overlay = magnifyingGlassOverlay,
                ImageElement = magnifyingGlassImage,
                InfoLabel = magnifyingGlassLabel
            };
        }
        
        private void CreateMagnifyingGlassReticle(VisualElement magnifyingGlassImage)
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
        
        #endregion
        
        #region List and Controls Creation
        
        public ListView CreateIslandList(IslandListConfig config)
        {
            var islandListView = new ListView
            {
                style = {
                    height = config.Height,
                    marginBottom = 10
                },
                selectionType = SelectionType.Multiple,
                reorderable = false
            };
            
            islandListView.makeItem = config.MakeItem ?? CreateDefaultIslandListItem;
            islandListView.bindItem = config.BindItem ?? ((element, index) => { });
            
            if (config.OnSelectionChanged != null)
            {
                islandListView.onSelectionChange += config.OnSelectionChanged;
            }
            
            return islandListView;
        }
        
        private VisualElement CreateDefaultIslandListItem()
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
        
        public VisualElement CreateControlButtons(Action onRefresh, Action onClearSelection)
        {
            var buttonContainer = new VisualElement
            {
                style = {
                    flexDirection = FlexDirection.Row,
                    marginBottom = 10
                }
            };
            
            var refreshButton = new Button(onRefresh)
            {
                style = {
                    flexGrow = 1,
                    marginRight = 5
                }
            };
            SetLocalizedContent(refreshButton, "refresh");
            
            var clearSelectionButton = new Button(onClearSelection)
            {
                style = {
                    flexGrow = 1,
                    marginLeft = 5
                }
            };
            SetLocalizedContent(clearSelectionButton, "clear_selection");
            
            buttonContainer.Add(refreshButton);
            buttonContainer.Add(clearSelectionButton);
            
            return buttonContainer;
        }
        
        #endregion
        
        #region Utility Methods
        
        public void SetLocalizedContent(VisualElement element, string textKey, string tooltipKey = null)
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
        
        public void SetLocalizedTooltip(VisualElement element, string tooltipKey)
        {
            element.tooltip = UVIslandLocalization.Get(tooltipKey);
        }
        
        public void RegisterMouseEventHandlers(VisualElement element, UVMapMouseHandlers handlers)
        {
            if (handlers.OnMouseDown != null)
            {
                element.RegisterCallback<MouseDownEvent>(handlers.OnMouseDown, TrickleDown.TrickleDown);
            }
            
            if (handlers.OnMouseMove != null)
            {
                element.RegisterCallback<MouseMoveEvent>(handlers.OnMouseMove, TrickleDown.TrickleDown);
            }
            
            if (handlers.OnMouseUp != null)
            {
                element.RegisterCallback<MouseUpEvent>(handlers.OnMouseUp, TrickleDown.TrickleDown);
            }
            
            if (handlers.OnWheel != null)
            {
                element.RegisterCallback<WheelEvent>(handlers.OnWheel, TrickleDown.TrickleDown);
            }
        }
        
        #endregion
        
        #region Private Helper Methods
        
        private void SetMagnifyingZoom(UVIslandSelector selector, float zoomLevel)
        {
            if (selector != null)
            {
                selector.MagnifyingGlassZoom = zoomLevel;
            }
        }
        
        #endregion
    }
}