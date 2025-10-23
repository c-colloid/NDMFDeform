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
    /// UI Creation partial class for UVIslandMaskEditor
    /// Contains all UI creation and layout methods
    /// </summary>
    public partial class UVIslandMaskEditor
    {
        #region UI Creation - Inspector Setup
        // UIの作成 - インスペクターのセットアップ
        // UI creation methods for building the inspector interface

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
            var submeshSection = CreateSection("Submesh Selection / サブメッシュ選択");

            // Calculate initial mask from selected submeshes
            int initialMask = SubmeshSelectorView.ListToMask(targetMask.SelectedSubmeshes);

            // Create SubmeshSelectorView
            var submeshSelector = new SubmeshSelectorView
            {
                TotalSubmeshes = selector?.SubmeshCount ?? 1,
                CurrentSubmesh = selector?.CurrentPreviewSubmesh ?? 0,
                SelectedMask = initialMask
            };

            // Set localized labels and tooltips
            submeshSelector.SetMaskFieldLabel("Selected Submeshes");
            submeshSelector.SetPrevButtonTooltip("Previous submesh / 前のサブメッシュ");
            submeshSelector.SetNextButtonTooltip("Next submesh / 次のサブメッシュ");

            // Register event handlers
            submeshSelector.RegisterCallback<SubmeshChangedEvent>(evt =>
            {
                if (selector != null)
                {
                    Undo.RecordObject(targetMask, "Change Preview Submesh");
                    selector.SetPreviewSubmesh(evt.NewSubmeshIndex);
                    targetMask.CurrentPreviewSubmesh = evt.NewSubmeshIndex;
                    EditorUtility.SetDirty(targetMask);

                    // Update UI
                    UpdateSubmeshLabel();
                    RebuildIslandList();
                    RefreshUI(false);
                }
            });

            submeshSelector.RegisterCallback<SubmeshMaskChangedEvent>(evt =>
            {
                Undo.RecordObject(targetMask, "Change Submesh Selection");
                var submeshes = evt.SelectedSubmeshes;

                // Ensure at least one submesh is selected
                if (submeshes.Count == 0)
                {
                    submeshes.Add(0);
                    submeshSelector.SelectedMask = 1;
                }

                targetMask.SetSelectedSubmeshes(submeshes);
                if (selector != null)
                {
                    selector.SetSelectedSubmeshes(submeshes);
                }
                EditorUtility.SetDirty(targetMask);
                RefreshUI(false);
            });

            submeshSection.Add(submeshSelector);
            root.Add(submeshSection);

            // Store reference for later updates
            submeshSelectorView = submeshSelector;
        }

        private void CreateHighlightSettings()
        {
            var highlightSection = CreateSection("Highlight Settings / ハイライト設定");

            // Create HighlightSettingsView
            var highlightSettings = new HighlightSettingsView
            {
                HighlightOpacity = selector?.HighlightOpacity ?? 0.6f
            };

            // Set localized labels and tooltips
            highlightSettings.SetOpacitySliderLabel("Highlight Opacity / ハイライト不透明度");
            highlightSettings.SetOpacitySliderTooltip("ハイライトの不透明度を調整します (0 = 完全に透明, 1 = 不透明)\nAdjust highlight opacity (0 = fully transparent, 1 = opaque)");

            // Register event handler
            highlightSettings.RegisterCallback<HighlightOpacityChangedEvent>(evt =>
            {
                if (selector != null)
                {
                    selector.HighlightOpacity = evt.Opacity;
                    // Repaint scene view immediately to show opacity change
                    if (selector.HasSelectedIslands)
                    {
                        SceneView.RepaintAll();
                    }
                }
            });

            highlightSection.Add(highlightSettings);
            root.Add(highlightSection);

            // Store reference for later updates
            highlightSettingsView = highlightSettings;
        }

        private void CreateDisplaySettings()
        {
            var settingsContainer = CreateSection(UVIslandLocalization.Get("header_display"));

            // Display settings section is now empty (adaptive vertex size removed)
            // Will be populated with HighlightSettingsView in Phase 3

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

	        /*
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
                // Switch to previous submesh
                selector.PreviousPreviewSubmesh();

                // Update UI
                UpdateSubmeshLabel();
                RebuildIslandList();
                RefreshUI(false);
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
                // Switch to next submesh
                selector.NextPreviewSubmesh();

                // Update UI
                UpdateSubmeshLabel();
                RebuildIslandList();
                RefreshUI(false);
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
	        */

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
	            value = selector?.AutoUpdatePreview ?? true,
	            text = "Auto Update"
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
                name = "status-label",
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
        #endregion
    }
}
