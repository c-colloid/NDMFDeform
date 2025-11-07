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

        private void CreateHeader()
        {
            var headerLabel = new Label("UVアイランド選択")
            {
                style = {
                    fontSize = 16,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 10
                }
            };
            root.Add(headerLabel);

            var description = new Label("メッシュ変形のためのUVアイランドベースマスク")
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
                    UpdateIslandNamesOverlay();
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

            var uvMapLabel = new Label("UVマッププレビュー");
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

            zoomSlider = new Slider("ズーム", 1f, 8f)
            {
                value = selector?.UvMapZoom ?? 1f,
                style = { flexGrow = 1, marginRight = 10, width = 120 }
            };
            zoomSlider.tooltip = "UVマッププレビューのズームレベル（1倍 = 通常、8倍 = 最大）";

            // Update slider label to show current zoom value
            UpdateZoomSliderLabel();

            zoomSlider.RegisterValueChangedCallback(evt =>
            {
                if (selector != null)
                {
                    // Use center point for zoom when using slider
                    selector.SetZoomLevelAroundCenter(evt.newValue);
                    UpdateZoomSliderLabel();
                    UpdateTextureWithThrottle(); // Always update with throttling
                }
            });

            // Show island names toggle
            var showNamesToggle = new Toggle("名前表示")
            {
                value = selector?.ShowIslandNames ?? false,
                style = { marginLeft = 15, marginRight = 5 }
            };
            showNamesToggle.tooltip = "UVマップ上にアイランド名を表示";
            showNamesToggle.RegisterValueChangedCallback(evt =>
            {
                if (selector != null)
                {
                    selector.ShowIslandNames = evt.newValue;
                    UpdateIslandNamesOverlay();
                }
            });

            // Font size slider for island names
	        var fontSizeSlider = new Slider("", 1f, 24f)
            {
                value = selector?.IslandNameFontSize ?? 14f,
                style = { width = 60, marginLeft = 5, marginRight = 5 }
            };
	        fontSizeSlider.tooltip = "アイランド名のフォントサイズ (1-24pt, ズームで拡大)";
            fontSizeSlider.RegisterValueChangedCallback(evt =>
            {
                if (selector != null)
                {
                    selector.IslandNameFontSize = evt.newValue;
                    if (selector.ShowIslandNames)
                    {
                        UpdateIslandNamesOverlay();
                    }
                }
            });

            resetZoomButton = new Button(() =>
            {
                if (selector != null)
                {
                    selector.ResetViewTransform();
                    zoomSlider.value = 1f;
                    UpdateZoomSliderLabel();
                    selector.GenerateUVMapTexture(); // Always update immediately for reset button
                    RefreshUVMapImage();
                }
            })
            {
                text = "リセット",
                style = { width = 60, marginLeft = 10 }
            };

            previewSettings.Add(zoomSlider);
            previewSettings.Add(showNamesToggle);
            previewSettings.Add(fontSizeSlider);
            previewSettings.Add(resetZoomButton);
            root.Add(previewSettings);
            
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
            
            uvMapImage.tooltip = "左クリック: アイランド選択、\n中ドラッグ: 視点移動、\nホイール: ズーム、\n右クリック: ルーペ";

            uvMapContainer.Add(uvMapImage);

            // Create overlays
            CreateRangeSelectionOverlay();
            CreateMagnifyingGlassOverlay();
            CreateIslandNamesOverlay();

            uvMapContainer.Add(rangeSelectionOverlay);
            uvMapContainer.Add(magnifyingGlassOverlay);
            uvMapContainer.Add(islandNamesOverlay);
            
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

        private void CreateIslandNamesOverlay()
        {
            islandNamesOverlay = new VisualElement
            {
                name = "islandNamesOverlay",
                pickingMode = PickingMode.Ignore, // Don't interfere with mouse events
                style = {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    right = 0,
                    bottom = 0
                }
            };

            // Use generateVisualContent for efficient text rendering
            islandNamesOverlay.generateVisualContent += OnGenerateIslandNamesContent;
        }

        private void OnGenerateIslandNamesContent(MeshGenerationContext mgc)
        {
            if (selector == null || !selector.ShowIslandNames) return;
            if (selector.UVIslands == null || selector.UVIslands.Count == 0) return;

            var uvIslands = selector.UVIslands;
            var transformMatrix = selector.CalculateUVTransformMatrix();
            int width = UV_MAP_SIZE;
            int height = UV_MAP_SIZE;

            // Calculate font size based on zoom level
            float zoom = selector.UvMapZoom;
            float baseFontSize = selector.IslandNameFontSize;
            float fontSize = baseFontSize * zoom;

            // Draw text for each island
            foreach (var island in uvIslands)
            {
                string displayName = !string.IsNullOrEmpty(island.customName)
                    ? island.customName
                    : $"Island {island.islandID}";

                // Calculate position
                Vector2 islandCenter = island.uvBounds.center;
                Vector3 uvPos = new Vector3(islandCenter.x, islandCenter.y, 0f);
                Vector3 transformedPos = transformMatrix.MultiplyPoint3x4(uvPos);

                float x = transformedPos.x * width;
                float y = (1f - transformedPos.y) * height; // Flip Y for UI Toolkit coordinates

                // Skip if outside bounds
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;

                // Calculate accurate text dimensions with line and character-type detection
                Vector2 textDimensions = CalculateTextDimensions(displayName, fontSize);

                // Center the text at the island position
                Vector2 centeredPos = new Vector2(
                    x - textDimensions.x * 0.5f,
                    y - textDimensions.y * 0.5f
                );

                // Draw text with outline for better visibility
                // Black outline (4-direction)
                mgc.DrawText(displayName, centeredPos + new Vector2(-1, 0), fontSize, Color.black, null);
                mgc.DrawText(displayName, centeredPos + new Vector2(1, 0), fontSize, Color.black, null);
                mgc.DrawText(displayName, centeredPos + new Vector2(0, -1), fontSize, Color.black, null);
                mgc.DrawText(displayName, centeredPos + new Vector2(0, 1), fontSize, Color.black, null);

                // White text
                mgc.DrawText(displayName, centeredPos, fontSize, Color.white, null);
            }
        }

        /// <summary>
        /// Calculate text dimensions with accurate width and height estimation
        /// Uses Font.GetCharacterInfo to get exact character advance widths
        /// Supports multiline text
        /// </summary>
        /// <param name="text">Text to measure</param>
        /// <param name="fontSize">Font size in points</param>
        /// <returns>Vector2 with (width, height) of the text</returns>
        private Vector2 CalculateTextDimensions(string text, float fontSize)
        {
            if (string.IsNullOrEmpty(text))
                return Vector2.zero;

            // Get the font used by UI Toolkit DrawText (LegacyRuntime.ttf)
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                // Fallback to estimated calculation if font not available
                Debug.LogWarning("LegacyRuntime.ttf not found, using estimated text dimensions");
                return CalculateTextDimensionsEstimated(text, fontSize);
            }

	        string[] del = {"\r\n","\n"};

            // Split into lines for multiline support
	        string[] lines = System.Text.RegularExpressions.Regex.Unescape(text).Split(del,StringSplitOptions.None);

            // Request all characters in the text to be loaded into font texture
            font.RequestCharactersInTexture(text, (int)fontSize, FontStyle.Normal);

            // Calculate width of each line using accurate character metrics
            float maxWidth = 0f;
            foreach (var line in lines)
            {
                float lineWidth = 0f;
                foreach (char c in line)
                {
                    CharacterInfo charInfo;
                    if (font.GetCharacterInfo(c, out charInfo, (int)fontSize, FontStyle.Normal))
                    {
                        // Use advance for accurate character width
                        lineWidth += charInfo.advance;
                    }
                    else
                    {
                        // Fallback if character info not available
                        lineWidth += fontSize * 0.6f;
                    }
                }
                maxWidth = Mathf.Max(maxWidth, lineWidth);
            }

            // Calculate total height with line spacing (120% = 1.2x line height)
            const float LINE_HEIGHT_MULTIPLIER = 1.2f;
            float totalHeight = lines.Length * fontSize * LINE_HEIGHT_MULTIPLIER;

            return new Vector2(maxWidth, totalHeight);
        }

        /// <summary>
        /// Fallback method for estimated text dimensions (used if font loading fails)
        /// </summary>
        private Vector2 CalculateTextDimensionsEstimated(string text, float fontSize)
        {
            if (string.IsNullOrEmpty(text))
                return Vector2.zero;

            string[] lines = text.Split('\n');
            float maxWidth = 0f;

            foreach (var line in lines)
            {
                float lineWidth = 0f;
                foreach (char c in line)
                {
                    if (IsFullWidthCharacter(c))
                        lineWidth += fontSize;
                    else
                        lineWidth += fontSize * 0.6f;
                }
                maxWidth = Mathf.Max(maxWidth, lineWidth);
            }

            const float LINE_HEIGHT_MULTIPLIER = 1.2f;
            float totalHeight = lines.Length * fontSize * LINE_HEIGHT_MULTIPLIER;

            return new Vector2(maxWidth, totalHeight);
        }

        /// <summary>
        /// Check if a character is full-width (CJK characters, etc.)
        /// </summary>
        private bool IsFullWidthCharacter(char c)
        {
            // CJK Unified Ideographs (Chinese/Japanese Kanji): U+4E00 - U+9FFF
            if (c >= 0x4E00 && c <= 0x9FFF) return true;

            // Hiragana: U+3040 - U+309F
            if (c >= 0x3040 && c <= 0x309F) return true;

            // Katakana: U+30A0 - U+30FF
            if (c >= 0x30A0 && c <= 0x30FF) return true;

            // Hangul Syllables (Korean): U+AC00 - U+D7AF
            if (c >= 0xAC00 && c <= 0xD7AF) return true;

            // CJK Symbols and Punctuation: U+3000 - U+303F
            if (c >= 0x3000 && c <= 0x303F) return true;

            // Fullwidth Forms (full-width ASCII variants): U+FF00 - U+FFEF
            if (c >= 0xFF00 && c <= 0xFFEF) return true;

            // CJK Compatibility Ideographs: U+F900 - U+FAFF
            if (c >= 0xF900 && c <= 0xFAFF) return true;

            // Default: half-width
            return false;
        }

        private void UpdateIslandNamesOverlay()
        {
            if (islandNamesOverlay != null)
            {
                islandNamesOverlay.MarkDirtyRepaint();
            }
        }

        private void CreateIslandList()
        {
            // Header with label and edit button
            var headerContainer = new VisualElement
            {
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 5
                }
            };

            var listLabel = new Label("UV Islands");
            listLabel.style.fontSize = 14;
            listLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            listLabel.style.flexGrow = 1;
            headerContainer.Add(listLabel);

            var editModeToggle = new Button(() =>
            {
                islandNamesEditMode = !islandNamesEditMode;
                editingIslands.Clear(); // Clear individual edit states when toggling mode
                RebuildIslandList();
            })
            {
                text = "名前編集",
                style = { width = 70 }
            };
            editModeToggle.tooltip = "アイランド名の編集モードを切り替え";
            headerContainer.Add(editModeToggle);

            root.Add(headerContainer);
            
            islandListView = new ListView
            {
                style = {
                    height = 120,
                    marginBottom = 10
                },
	            selectionType = SelectionType.Multiple,
	            virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
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
                name = "colorBox",
                style = {
                    width = 16,
                    height = 16,
                    marginRight = 8,
                    backgroundColor = Color.gray
                }
            };

            // Container for name label and field (both occupy same space)
            var nameContainer = new VisualElement
            {
                name = "nameContainer",
                style = {
                    flexGrow = 1,
                    flexShrink = 1
                }
            };

            // Name label (display mode)
            var nameLabel = new Label
            {
                name = "nameLabel",
                style = {
                    fontSize = 12,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };

            // Register double-click to enter edit mode for this item
            nameLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2)
                {
                    var islandData = nameLabel.userData as (int islandID, int submeshIndex)?;
                    if (islandData.HasValue)
                    {
                        editingIslands.Add(islandData.Value);
                        RebuildIslandList();
                    }
                }
            });

            // Name field (edit mode)
            var nameField = new TextField
            {
                name = "nameField",
                style = {
                    fontSize = 11,
                    marginLeft = 0,
                    marginRight = 0
                }
            };

            // Register Enter (non-IME) and FocusOut to exit edit mode
            nameField.RegisterCallback<KeyDownEvent>(evt =>
            {
                // Enter key pressed and not in IME composition
                if (evt.keyCode == KeyCode.Return && (evt.character == '\n' || evt.character == '\r'))
                {
                    var islandData = nameField.userData as (int islandID, int submeshIndex)?;
                    if (islandData.HasValue)
                    {
                        editingIslands.Remove(islandData.Value);
                        RebuildIslandList();
                    }
                    evt.StopPropagation();
                }
            });

            // Exit edit mode when focus is lost (clicked outside)
            nameField.RegisterCallback<FocusOutEvent>(evt =>
            {
                var islandData = nameField.userData as (int islandID, int submeshIndex)?;
                if (islandData.HasValue)
                {
                    editingIslands.Remove(islandData.Value);
                    RebuildIslandList();
                }
            });
            nameField.RegisterValueChangedCallback(evt =>
            {
                // Store island ID and submesh index in user data for later retrieval
                var islandData = nameField.userData as (int islandID, int submeshIndex)?;
                if (islandData.HasValue && targetMask != null)
                {
                    Undo.RecordObject(targetMask, "Change Island Name");
                    targetMask.SetIslandCustomName(islandData.Value.islandID, islandData.Value.submeshIndex, evt.newValue);
                    EditorUtility.SetDirty(targetMask);

                    // Update island's custom name in selector's live list
                    if (selector?.UVIslands != null)
                    {
                        var island = selector.UVIslands.Find(i =>
                            i.islandID == islandData.Value.islandID &&
                            i.submeshIndex == islandData.Value.submeshIndex);
                        if (island != null)
                        {
                            island.customName = evt.newValue;
                        }
                    }

                    // Update island names overlay if names are being displayed
                    if (selector != null && selector.ShowIslandNames)
                    {
                        UpdateIslandNamesOverlay();
                    }
                }
            });

            var detailsContainer = new VisualElement
            {
                name = "detailsContainer",
                style = {
                    alignItems = Align.FlexEnd
                }
            };

            var vertexCountLabel = new Label
            {
                name = "vertexCountLabel",
                style = {
                    color = Color.gray,
                    fontSize = 10
                }
            };

            var faceCountLabel = new Label
            {
                name = "faceCountLabel",
                style = {
                    color = Color.gray,
                    fontSize = 10
                }
            };

            detailsContainer.Add(vertexCountLabel);
            detailsContainer.Add(faceCountLabel);

            // Add name label and field to name container
            nameContainer.Add(nameLabel);
            nameContainer.Add(nameField);

            // Build final container
            container.Add(colorBox);
            container.Add(nameContainer);
            container.Add(detailsContainer);

            return container;
        }
        
        private void BindIslandListItem(VisualElement element, int index)
        {
            if (selector?.UVIslands != null && index < selector.UVIslands.Count)
            {
                var island = selector.UVIslands[index];
                var container = element;

                // Get elements by name
                var colorBox = container.Q<VisualElement>("colorBox");
                var nameLabel = container.Q<Label>("nameLabel");
                var nameField = container.Q<TextField>("nameField");
                var vertexCountLabel = container.Q<Label>("vertexCountLabel");
                var faceCountLabel = container.Q<Label>("faceCountLabel");

                // Load custom name
                string customName = targetMask?.GetIslandCustomName(island.islandID, island.submeshIndex) ?? "";
                if (string.IsNullOrEmpty(customName))
                {
                    customName = island.customName; // Fallback to island's own custom name if mask doesn't have one
                }

                // Update the island's customName field to keep in sync
                island.customName = customName;

                // Determine if this item should be in edit mode
                var islandKey = (island.islandID, island.submeshIndex);
                bool isEditing = islandNamesEditMode || editingIslands.Contains(islandKey);

                // Update nameLabel (display mode)
                if (nameLabel != null)
                {
                    string displayText = !string.IsNullOrEmpty(customName)
                        ? customName
                        : $"Island {island.islandID} (SM{island.submeshIndex})";
                    nameLabel.text = displayText;
                    nameLabel.userData = islandKey;
                    nameLabel.style.display = isEditing ? DisplayStyle.None : DisplayStyle.Flex;
                }

                // Update nameField (edit mode)
                if (nameField != null)
                {
                    nameField.SetValueWithoutNotify(customName);
                    nameField.userData = islandKey;
                    nameField.style.display = isEditing ? DisplayStyle.Flex : DisplayStyle.None;

                    // Focus the field when entering edit mode for this specific item
                    if (editingIslands.Contains(islandKey) && nameField.style.display == DisplayStyle.Flex)
                    {
                        nameField.Focus();
                    }
                }

                // Update other elements
                if (colorBox != null)
                    colorBox.style.backgroundColor = island.maskColor;

                if (vertexCountLabel != null)
                    vertexCountLabel.text = $"{island.vertexIndices.Count}頂点";

                if (faceCountLabel != null)
                    faceCountLabel.text = $"{island.faceCount}面";

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
                text = "更新",
                style = {
                    width = 60,
                    marginRight = 5
                }
            };

            clearSelectionButton = new Button(() => ClearSelection())
            {
                text = "選択解除",
                style = {
                    flexGrow = 1,
                    marginLeft = 5
                }
            };
            
            buttonContainer.Add(refreshButton);
            buttonContainer.Add(clearSelectionButton);
            root.Add(buttonContainer);
            
        }
        
        private void CreateStatusArea()
        {
            statusLabel = new Label("待機中")
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
