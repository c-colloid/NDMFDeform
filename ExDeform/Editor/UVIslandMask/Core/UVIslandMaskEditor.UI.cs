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
        #region Island Label Information
        // アイランドラベル情報
        // Data structures for label collision detection and display management

        /// <summary>
        /// Information about a label displayed on the UV map
        /// UVマップ上に表示されるラベルの情報
        /// </summary>
        private class IslandLabelInfo
        {
            public int islandID;
            public int submeshIndex;
            public string displayName;
            public Vector2 centerPos;       // Center position in screen coordinates
            public Rect boundingBox;        // Bounding box in screen coordinates (for full name)
            public Rect abbreviatedBox;     // Bounding box for circled number
            public float area;              // Island area (for priority calculation)
            public bool isSelected;         // Is this island selected?
            public int lineCount;           // Number of lines in the label
            public string circledNumber;    // Circled number text (①②③)
        }

        /// <summary>
        /// Current mouse position on UV map (in local coordinates)
        /// UVマップ上の現在のマウス位置（ローカル座標）
        /// </summary>
        private Vector2 currentUVMapMousePos = Vector2.zero;

        /// <summary>
        /// Island ID currently under mouse hover (-1 if none)
        /// マウスホバー中のアイランドID（なければ-1）
        /// </summary>
        private int hoveredIslandID = -1;

        /// <summary>
        /// Cached label information for collision detection
        /// 衝突検出用のキャッシュされたラベル情報
        /// </summary>
        private List<IslandLabelInfo> cachedLabelInfo = new List<IslandLabelInfo>();

        /// <summary>
        /// Hover tooltip overlay for displaying island name on mouse over
        /// マウスオーバー時にアイランド名を表示するホバーツールチップオーバーレイ
        /// </summary>
        private VisualElement hoverTooltipOverlay;

        #endregion

        #region Circled Number Generation
        // 丸数字生成
        // Helper methods for generating circled numbers

        /// <summary>
        /// Convert a number (1-50) to a circled number character
        /// 数字（1-50）を丸数字文字に変換
        /// </summary>
        /// <param name="number">Number to convert (1-50)</param>
        /// <returns>Circled number string (①-⑳, ㉑-㊿, or (51)+ for larger numbers)</returns>
        private string GetCircledNumber(int number)
        {
            if (number < 1)
                return "(0)";

            // Unicode circled numbers 1-20: ① (U+2460) to ⑳ (U+2473)
            if (number <= 20)
                return char.ConvertFromUtf32(0x2460 + number - 1);

            // Unicode circled numbers 21-35: ㉑ (U+3251) to ㉟ (U+325F)
            if (number <= 35)
                return char.ConvertFromUtf32(0x3251 + number - 21);

            // Unicode circled numbers 36-50: ㊱ (U+32B1) to ㊿ (U+32BF)
            if (number <= 50)
                return char.ConvertFromUtf32(0x32B1 + number - 36);

            // For numbers > 50, use parentheses format
            return $"({number})";
        }

        /// <summary>
        /// Get the display index (1-based) for an island in the current list
        /// 現在のリスト内でのアイランドの表示インデックス（1始まり）を取得
        /// </summary>
        private int GetIslandDisplayIndex(int islandID, int submeshIndex)
        {
            if (selector?.UVIslands == null)
                return -1;

            for (int i = 0; i < selector.UVIslands.Count; i++)
            {
                var island = selector.UVIslands[i];
                if (island.islandID == islandID && island.submeshIndex == submeshIndex)
                    return i + 1; // 1-based index
            }

            return -1;
        }

        #endregion

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

            // Hover tooltip settings row
            var hoverSettings = new VisualElement
            {
                name = "hoverSettings",
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 10
                }
            };

            var hoverLabel = new Label("ホバー表示:")
            {
                style = {
                    fontSize = 11,
                    marginRight = 5,
                    width = 80
                }
            };

            var hoverFontSizeSlider = new Slider("サイズ", 8f, 32f)
            {
                value = selector?.HoverTooltipFontSize ?? 18f,
                style = { flexGrow = 1, marginRight = 5, maxWidth = 300 }
            };
            hoverFontSizeSlider.tooltip = "ホバー時の名前表示サイズ (8-32pt, ズーム非依存)";
            hoverFontSizeSlider.RegisterValueChangedCallback(evt =>
            {
                if (selector != null)
                {
                    selector.HoverTooltipFontSize = evt.newValue;
                    if (selector.ShowIslandNames && hoveredIslandID >= 0)
                    {
                        UpdateHoverTooltipOverlay();
                    }
                }
            });

            hoverSettings.Add(hoverLabel);
            hoverSettings.Add(hoverFontSizeSlider);
            root.Add(hoverSettings);

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

            // Create hover tooltip overlay (must be added last to be on top)
            CreateHoverTooltipOverlay();
            uvMapContainer.Add(hoverTooltipOverlay);

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

            // Step 1: Calculate label information for all islands
            var labelInfoList = CalculateLabelInfo(uvIslands, transformMatrix, width, height, fontSize);
            cachedLabelInfo = labelInfoList; // Cache for hover detection

            // Step 2: Detect collisions between full name labels
            var collisionGroups = DetectLabelCollisions(labelInfoList);

            // Step 3: Create sets for display modes
            var labelsToAbbreviate = new HashSet<int>();  // Show as circled numbers
            var labelsToShowFull = new HashSet<int>();     // Show full names

            foreach (var group in collisionGroups)
            {
                // Determine which label to show fully (prioritize hovered and selected)
                var labelToShow = GetLabelToShow(group, hoveredIslandID);
                if (labelToShow != null)
                {
                    labelsToShowFull.Add(labelToShow.islandID);
                }

                // Mark others for abbreviation (circled numbers)
                foreach (var label in group)
                {
                    if (labelToShow == null || label.islandID != labelToShow.islandID)
                    {
                        labelsToAbbreviate.Add(label.islandID);
                    }
                }
            }

            // Step 4: Draw labels
            foreach (var labelInfo in labelInfoList)
            {
                // Skip if outside visible bounds
                if (labelInfo.centerPos.x < 0 || labelInfo.centerPos.x >= width ||
                    labelInfo.centerPos.y < 0 || labelInfo.centerPos.y >= height)
                    continue;

                if (labelsToAbbreviate.Contains(labelInfo.islandID))
                {
                    // Draw circled number
                    DrawAbbreviatedLabel(mgc, labelInfo, fontSize);
                }
                else
                {
                    // Skip if this is the hovered island (will be drawn by hover tooltip)
                    if (labelInfo.islandID == hoveredIslandID)
                        continue;

                    // Draw full label with color based on selection state
                    Color textColor = labelInfo.isSelected ? new Color(1f, 0.6f, 0.2f) : Color.white;
                    DrawTextMultilineCentered(mgc, labelInfo.displayName, labelInfo.centerPos, fontSize, textColor);
                }
            }
        }

        /// <summary>
        /// Draw abbreviated label indicator (circled number)
        /// 省略されたラベルインジケーターを描画（丸数字）
        /// </summary>
        private void DrawAbbreviatedLabel(MeshGenerationContext mgc, IslandLabelInfo labelInfo, float fontSize)
        {
            // Get circled number based on list position
            int displayIndex = GetIslandDisplayIndex(labelInfo.islandID, labelInfo.submeshIndex);
            if (displayIndex < 0) return;

            string circledNumber = GetCircledNumber(displayIndex);

            // Draw with outline for visibility
            Vector2 pos = labelInfo.centerPos;

            // Calculate dimensions for centering
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) return;

            // Normalize font size across different Unicode circled number ranges
            // Different ranges have different visual sizes, so we apply scaling factors
            float adjustedFontSize = fontSize;
            if (displayIndex >= 1 && displayIndex <= 9)
            {
                // ① - ⑨: Baseline size
                adjustedFontSize = fontSize;
            }
            else if (displayIndex >= 10 && displayIndex <= 20)
            {
                // ⑩ - ⑳: Slightly larger visually, scale down
                adjustedFontSize = fontSize * 0.95f;
            }
            else if (displayIndex >= 21 && displayIndex <= 35)
            {
                // ㉑ - ㉟: Much larger visually, scale down more
                adjustedFontSize = fontSize * 0.85f;
            }
            else if (displayIndex >= 36 && displayIndex <= 50)
            {
                // ㊱ - ㊿: Much larger visually, scale down more
                adjustedFontSize = fontSize * 0.85f;
            }
            else
            {
                // (51)+ : Parenthesized numbers, smaller visually, scale up slightly
                adjustedFontSize = fontSize * 1.1f;
            }

            font.RequestCharactersInTexture(circledNumber, (int)adjustedFontSize, FontStyle.Normal);
            Vector2 textDimensions = CalculateTextDimensions(circledNumber, adjustedFontSize);
            Vector2 centeredPos = new Vector2(
                pos.x - textDimensions.x * 0.5f,
                pos.y - textDimensions.y * 0.5f
            );

            // Draw outline (4-direction) for better visibility
            mgc.DrawText(circledNumber, centeredPos + new Vector2(-1, 0), adjustedFontSize, Color.black, null);
            mgc.DrawText(circledNumber, centeredPos + new Vector2(1, 0), adjustedFontSize, Color.black, null);
            mgc.DrawText(circledNumber, centeredPos + new Vector2(0, -1), adjustedFontSize, Color.black, null);
            mgc.DrawText(circledNumber, centeredPos + new Vector2(0, 1), adjustedFontSize, Color.black, null);

            // Draw main circled number with white color for consistency with full names
            mgc.DrawText(circledNumber, centeredPos, adjustedFontSize, Color.white, null);
        }

        /// <summary>
        /// Draw multiline text with each line center-aligned
        /// </summary>
        /// <param name="mgc">MeshGenerationContext for drawing</param>
        /// <param name="text">Text to draw (supports multiline)</param>
        /// <param name="centerPos">Center position where text should be drawn</param>
        /// <param name="fontSize">Font size</param>
        /// <param name="textColor">Text color (defaults to white)</param>
        private void DrawTextMultilineCentered(MeshGenerationContext mgc, string text, Vector2 centerPos, float fontSize, Color? textColor = null)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Get the font
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                Debug.LogWarning("LegacyRuntime.ttf not found");
                return;
            }

            // Split into lines
            string[] del = {"\r\n","\n"};
            string[] lines = System.Text.RegularExpressions.Regex.Unescape(text).Split(del, StringSplitOptions.None);

            // Request all characters to be loaded
            font.RequestCharactersInTexture(text, (int)fontSize, FontStyle.Normal);

            // Calculate overall text dimensions
            Vector2 textDimensions = CalculateTextDimensions(text, fontSize);

            // Starting Y position (top of text block)
            const float LINE_HEIGHT_MULTIPLIER = 1.2f;
            float lineHeight = fontSize * LINE_HEIGHT_MULTIPLIER;
            float startY = centerPos.y - textDimensions.y * 0.5f;

            // Use provided color or default to white
            Color finalColor = textColor ?? Color.white;

            // Draw each line center-aligned
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                float lineWidth = CalculateLineWidth(line, fontSize, font);

                // Calculate centered X position for this line
                float lineX = centerPos.x - lineWidth * 0.5f;
                float lineY = startY + i * lineHeight;
                Vector2 linePos = new Vector2(lineX, lineY);

                // Draw outline (4-direction)
                mgc.DrawText(line, linePos + new Vector2(-1, 0), fontSize, Color.black, null);
                mgc.DrawText(line, linePos + new Vector2(1, 0), fontSize, Color.black, null);
                mgc.DrawText(line, linePos + new Vector2(0, -1), fontSize, Color.black, null);
                mgc.DrawText(line, linePos + new Vector2(0, 1), fontSize, Color.black, null);

                // Draw main text with specified color
                mgc.DrawText(line, linePos, fontSize, finalColor, null);
            }
        }

        /// <summary>
        /// Calculate the width of a single line of text
        /// </summary>
        /// <param name="line">Single line of text</param>
        /// <param name="fontSize">Font size</param>
        /// <param name="font">Font to use</param>
        /// <returns>Width of the line in pixels</returns>
        private float CalculateLineWidth(string line, float fontSize, Font font)
        {
            if (string.IsNullOrEmpty(line))
                return 0f;

            float lineWidth = 0f;
            foreach (char c in line)
            {
                CharacterInfo charInfo;
                if (font.GetCharacterInfo(c, out charInfo, (int)fontSize, FontStyle.Normal))
                {
                    lineWidth += charInfo.advance;
                }
                else
                {
                    // Fallback if character info not available
                    lineWidth += fontSize * 0.6f;
                }
            }
            return lineWidth;
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

        private void CreateHoverTooltipOverlay()
        {
            hoverTooltipOverlay = new VisualElement
            {
                name = "hoverTooltipOverlay",
                pickingMode = PickingMode.Ignore, // Don't interfere with mouse events
                style = {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    right = 0,
                    bottom = 0
                }
            };

            // Use generateVisualContent for efficient hover tooltip rendering
            hoverTooltipOverlay.generateVisualContent += OnGenerateHoverTooltipContent;
        }

        private void OnGenerateHoverTooltipContent(MeshGenerationContext mgc)
        {
            // Only show tooltip if hovering over an island and names are enabled
            if (selector == null || !selector.ShowIslandNames) return;
            if (hoveredIslandID < 0) return;
            if (selector.UVIslands == null || selector.UVIslands.Count == 0) return;

            // Find the hovered island
            var hoveredIsland = selector.UVIslands.Find(i => i.islandID == hoveredIslandID);
            if (hoveredIsland == null) return;

            string displayName = !string.IsNullOrEmpty(hoveredIsland.customName)
                ? hoveredIsland.customName
                : $"Island {hoveredIsland.islandID}";

            // Calculate position
            var transformMatrix = selector.CalculateUVTransformMatrix();
            Vector2 islandCenter = hoveredIsland.uvBounds.center;
            Vector3 uvPos = new Vector3(islandCenter.x, islandCenter.y, 0f);
            Vector3 transformedPos = transformMatrix.MultiplyPoint3x4(uvPos);

            int width = UV_MAP_SIZE;
            int height = UV_MAP_SIZE;
            float x = transformedPos.x * width;
            float y = (1f - transformedPos.y) * height;

            // Skip if outside bounds
            if (x < 0 || x >= width || y < 0 || y >= height)
                return;

            // Calculate background label font size (zoom-dependent)
            float zoom = selector.UvMapZoom;
            float baseFontSize = selector.IslandNameFontSize;
            float backgroundFontSize = baseFontSize * zoom;

            // Use fixed font size for hover tooltip (zoom-independent)
            float hoverFontSize = selector.HoverTooltipFontSize;

            // Only show hover tooltip if it's larger than background label
            // This prevents obscuring larger background text with smaller hover text
            if (hoverFontSize < backgroundFontSize)
                return;

            // Draw tooltip with prominent background
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) return;

            font.RequestCharactersInTexture(displayName, (int)hoverFontSize, FontStyle.Normal);
            Vector2 textDimensions = CalculateTextDimensions(displayName, hoverFontSize);

            // Add padding around text
            float padding = 8f;
            Rect bgRect = new Rect(
                x - textDimensions.x * 0.5f - padding,
                y - textDimensions.y * 0.5f - padding,
                textDimensions.x + padding * 2,
                textDimensions.y + padding * 2
            );

            // Check if island is selected
            bool isSelected = selector.SelectedIslandIDs.Contains(hoveredIslandID);

            // Draw border outline to show which island is being hovered (no fill background)
            // Use different color for selected vs unselected islands
            Color outlineColor = isSelected
                ? new Color(1f, 0.5f, 0f, 0.8f)  // Orange for selected
                : new Color(0.6f, 0.6f, 0.6f, 0.7f);  // Gray for unselected

            float borderWidth = 2f;

            // Draw border as 4 rectangles (top, right, bottom, left)
            // Top border
            var topBorder = mgc.Allocate(4, 6);
            topBorder.SetAllVertices(new Vertex[] {
                new Vertex { position = new Vector3(bgRect.xMin, bgRect.yMin, Vertex.nearZ), tint = outlineColor },
                new Vertex { position = new Vector3(bgRect.xMax, bgRect.yMin, Vertex.nearZ), tint = outlineColor },
                new Vertex { position = new Vector3(bgRect.xMax, bgRect.yMin + borderWidth, Vertex.nearZ), tint = outlineColor },
                new Vertex { position = new Vector3(bgRect.xMin, bgRect.yMin + borderWidth, Vertex.nearZ), tint = outlineColor }
            });
            topBorder.SetAllIndices(new ushort[] { 0, 1, 2, 0, 2, 3 });

            // Right border
            var rightBorder = mgc.Allocate(4, 6);
            rightBorder.SetAllVertices(new Vertex[] {
                new Vertex { position = new Vector3(bgRect.xMax - borderWidth, bgRect.yMin, Vertex.nearZ), tint = outlineColor },
                new Vertex { position = new Vector3(bgRect.xMax, bgRect.yMin, Vertex.nearZ), tint = outlineColor },
                new Vertex { position = new Vector3(bgRect.xMax, bgRect.yMax, Vertex.nearZ), tint = outlineColor },
                new Vertex { position = new Vector3(bgRect.xMax - borderWidth, bgRect.yMax, Vertex.nearZ), tint = outlineColor }
            });
            rightBorder.SetAllIndices(new ushort[] { 0, 1, 2, 0, 2, 3 });

            // Bottom border
            var bottomBorder = mgc.Allocate(4, 6);
            bottomBorder.SetAllVertices(new Vertex[] {
                new Vertex { position = new Vector3(bgRect.xMin, bgRect.yMax - borderWidth, Vertex.nearZ), tint = outlineColor },
                new Vertex { position = new Vector3(bgRect.xMax, bgRect.yMax - borderWidth, Vertex.nearZ), tint = outlineColor },
                new Vertex { position = new Vector3(bgRect.xMax, bgRect.yMax, Vertex.nearZ), tint = outlineColor },
                new Vertex { position = new Vector3(bgRect.xMin, bgRect.yMax, Vertex.nearZ), tint = outlineColor }
            });
            bottomBorder.SetAllIndices(new ushort[] { 0, 1, 2, 0, 2, 3 });

            // Left border
            var leftBorder = mgc.Allocate(4, 6);
            leftBorder.SetAllVertices(new Vertex[] {
                new Vertex { position = new Vector3(bgRect.xMin, bgRect.yMin, Vertex.nearZ), tint = outlineColor },
                new Vertex { position = new Vector3(bgRect.xMin + borderWidth, bgRect.yMin, Vertex.nearZ), tint = outlineColor },
                new Vertex { position = new Vector3(bgRect.xMin + borderWidth, bgRect.yMax, Vertex.nearZ), tint = outlineColor },
                new Vertex { position = new Vector3(bgRect.xMin, bgRect.yMax, Vertex.nearZ), tint = outlineColor }
            });
            leftBorder.SetAllIndices(new ushort[] { 0, 1, 2, 0, 2, 3 });

            // Draw highlighted text with color based on selection state
            Color textColor = isSelected ? new Color(1f, 0.6f, 0.2f) : Color.white;
            DrawTextMultilineCentered(mgc, displayName, new Vector2(x, y), hoverFontSize, textColor);
        }

        private void UpdateHoverTooltipOverlay()
        {
            if (hoverTooltipOverlay != null)
            {
                hoverTooltipOverlay.MarkDirtyRepaint();
            }
        }

        /// <summary>
        /// Calculate label information for all islands with bounding boxes
        /// 全アイランドのバウンディングボックス付きラベル情報を計算
        /// </summary>
        private List<IslandLabelInfo> CalculateLabelInfo(List<UVIslandAnalyzer.UVIsland> islands,
            Matrix4x4 transformMatrix, int width, int height, float fontSize)
        {
            var labelInfoList = new List<IslandLabelInfo>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) return labelInfoList;

            for (int i = 0; i < islands.Count; i++)
            {
                var island = islands[i];
                string displayName = !string.IsNullOrEmpty(island.customName)
                    ? island.customName
                    : $"Island {island.islandID}";

                // Calculate position
                Vector2 islandCenter = island.uvBounds.center;
                Vector3 uvPos = new Vector3(islandCenter.x, islandCenter.y, 0f);
                Vector3 transformedPos = transformMatrix.MultiplyPoint3x4(uvPos);

                float x = transformedPos.x * width;
                float y = (1f - transformedPos.y) * height;

                // Skip if outside bounds
                if (x < -width || x >= width * 2 || y < -height || y >= height * 2)
                    continue;

                // Calculate text dimensions for full name
                font.RequestCharactersInTexture(displayName, (int)fontSize, FontStyle.Normal);
                Vector2 textDimensions = CalculateTextDimensions(displayName, fontSize);

                // Calculate bounding box for full name
                Rect boundingBox = new Rect(
                    x - textDimensions.x * 0.5f,
                    y - textDimensions.y * 0.5f,
                    textDimensions.x,
                    textDimensions.y
                );

                // Generate circled number (based on list position, 1-indexed)
                string circledNumber = GetCircledNumber(i + 1);
                font.RequestCharactersInTexture(circledNumber, (int)fontSize, FontStyle.Normal);
                Vector2 abbreviatedDimensions = CalculateTextDimensions(circledNumber, fontSize);

                // Calculate bounding box for circled number
                Rect abbreviatedBox = new Rect(
                    x - abbreviatedDimensions.x * 0.5f,
                    y - abbreviatedDimensions.y * 0.5f,
                    abbreviatedDimensions.x,
                    abbreviatedDimensions.y
                );

                // Count lines
                string[] del = {"\r\n", "\n"};
                int lineCount = System.Text.RegularExpressions.Regex.Unescape(displayName).Split(del, StringSplitOptions.None).Length;

                // Calculate area (for priority)
                float area = island.uvBounds.size.x * island.uvBounds.size.y;

                // Check if selected
                bool isSelected = selector.SelectedIslandIDs.Contains(island.islandID);

                labelInfoList.Add(new IslandLabelInfo
                {
                    islandID = island.islandID,
                    submeshIndex = island.submeshIndex,
                    displayName = displayName,
                    centerPos = new Vector2(x, y),
                    boundingBox = boundingBox,
                    abbreviatedBox = abbreviatedBox,
                    area = area,
                    isSelected = isSelected,
                    lineCount = lineCount,
                    circledNumber = circledNumber
                });
            }

            return labelInfoList;
        }

        /// <summary>
        /// Detect collisions between label bounding boxes
        /// Returns groups of colliding labels
        /// ラベルのバウンディングボックス間の衝突を検出
        /// 衝突しているラベルのグループを返す
        /// </summary>
        private List<List<IslandLabelInfo>> DetectLabelCollisions(List<IslandLabelInfo> labelInfoList)
        {
            var collisionGroups = new List<List<IslandLabelInfo>>();
            var processed = new HashSet<int>();

            for (int i = 0; i < labelInfoList.Count; i++)
            {
                if (processed.Contains(i)) continue;

                var group = new List<IslandLabelInfo> { labelInfoList[i] };
                processed.Add(i);

                // Find all labels that collide with this one or with any in the group
                bool foundNew = true;
                while (foundNew)
                {
                    foundNew = false;
                    for (int j = 0; j < labelInfoList.Count; j++)
                    {
                        if (processed.Contains(j)) continue;

                        // Check if this label collides with any in the group
                        bool collides = false;
                        foreach (var groupLabel in group)
                        {
                            if (groupLabel.boundingBox.Overlaps(labelInfoList[j].boundingBox))
                            {
                                collides = true;
                                break;
                            }
                        }

                        if (collides)
                        {
                            group.Add(labelInfoList[j]);
                            processed.Add(j);
                            foundNew = true;
                        }
                    }
                }

                // Only add groups with actual collisions (2 or more labels)
                if (group.Count >= 2)
                {
                    collisionGroups.Add(group);
                }
            }

            return collisionGroups;
        }

        /// <summary>
        /// Calculate display priority for a label
        /// Higher priority = more important to display
        /// Priority: Selected > Large Area > Small Area
        /// ラベルの表示優先度を計算
        /// 優先度が高い = 表示が重要
        /// 優先度: 選択中 > 大きい面積 > 小さい面積
        /// </summary>
        private float CalculateDisplayPriority(IslandLabelInfo labelInfo)
        {
            float priority = 0f;

            // Selected islands have highest priority
            if (labelInfo.isSelected)
            {
                priority += 1000000f;
            }

            // Area contributes to priority
            priority += labelInfo.area * 1000f;

            return priority;
        }

        /// <summary>
        /// Determine which label to show in a collision group
        /// 衝突グループ内でどのラベルを表示するかを決定
        /// </summary>
        private IslandLabelInfo GetLabelToShow(List<IslandLabelInfo> collisionGroup, int hoveredIslandID)
        {
            // If hovering over an island in this group, show that one
            var hoveredLabel = collisionGroup.Find(l => l.islandID == hoveredIslandID);
            if (hoveredLabel != null)
                return hoveredLabel;

            // Otherwise, show the one with highest priority
            IslandLabelInfo bestLabel = null;
            float bestPriority = float.MinValue;

            foreach (var label in collisionGroup)
            {
                float priority = CalculateDisplayPriority(label);
                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    bestLabel = label;
                }
            }

            return bestLabel;
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
                    // Add circled number prefix based on list position (1-indexed)
                    string circledNumber = GetCircledNumber(index + 1);
                    string displayText = !string.IsNullOrEmpty(customName)
                        ? customName
                        : $"Island {island.islandID} (SM{island.submeshIndex})";
                    nameLabel.text = $"{circledNumber} {displayText}";
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
