using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using System.Linq;
using System.Collections.Generic;

namespace Deform.Masking.Editor
{
    /// <summary>
    /// UVIslandSelectorのカスタムエディタ（面ハイライト対応版）
    /// </summary>
    [CustomEditor(typeof(UVIslandSelector))]
    public class UVIslandSelectorEditor : UnityEditor.Editor
    {
        private UVIslandSelector targetComponent;
        private VisualElement root;
        private VisualElement uvMapContainer;
        private VisualElement uvMapImage;
        private Label statusLabel;
        private ListView islandListView;
        private Button refreshButton;
        private Button clearSelectionButton;
        private Toggle highlightInSceneToggle;
        private Button exportToDeformerButton;
        
        // 新機能用UI要素
        private Slider vertexSizeSlider;
        private Toggle useAdaptiveSizeToggle;
        private Slider adaptiveMultiplierSlider;
        private Toggle autoUpdatePreviewToggle;
        private Slider zoomSlider;
        private Button resetZoomButton;
        private Toggle rangeSelectionToggle;
        private Toggle magnifyingGlassToggle;
        private Slider magnifyingGlassZoomSlider;
        private Slider magnifyingGlassSizeSlider;
        
        // 範囲選択UI要素
        private VisualElement rangeSelectionOverlay;
        
        // ルーペUI要素
        private VisualElement magnifyingGlassOverlay;
        private VisualElement magnifyingGlassImage;
        private Label magnifyingGlassLabel;
        
        private const int UV_MAP_SIZE = 300;
        private static bool highlightInScene = true;
        private bool isDraggingUVMap = false;
        private Vector2 lastMousePos;
        private Texture2D magnifyingGlassTexture;
        
        // ルーペ状態管理
        private bool isMagnifyingGlassActive = false;
        private Vector2 currentMagnifyingMousePos;
        
        public override VisualElement CreateInspectorGUI()
        {
            targetComponent = target as UVIslandSelector;
            
            // ルート要素を作成
            root = new VisualElement();
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            
            // ヘッダーを追加
            CreateHeader();
            
            // レンダラー情報表示
            CreateRendererInfo();
            
            // 表示設定セクションを作成
            CreateDisplaySettings();
            
            // UVマップ表示エリアを作成
            CreateUVMapArea();
            
            // アイランドリストを作成
            CreateIslandList();
            
            // ハイライトオプション
            CreateHighlightOptions();
            
            // コントロールボタンを作成
            CreateControlButtons();
            
            // Deformerとの連携
            CreateDeformerIntegration();
            
            // ステータス表示を作成
            CreateStatusArea();
            
            // root要素にもマウスイベントを登録（範囲選択時のマウス追従用）
            root.RegisterCallback<MouseMoveEvent>(OnRootMouseMove, TrickleDown.TrickleDown);
            root.RegisterCallback<MouseUpEvent>(OnRootMouseUp, TrickleDown.TrickleDown);
            
            // 初期データを読み込み
            RefreshData();
            
            return root;
        }
        
        /// <summary>
        /// ヘッダー部分を作成
        /// </summary>
        private void CreateHeader()
        {
            var headerLabel = new Label("UV Island Selector")
            {
                style = {
                    fontSize = 16,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 10
                }
            };
            root.Add(headerLabel);
            
            var description = new Label("Automatically detect and select UV islands. Click on UV islands or select from the list below. Compatible with Deform framework.")
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
        
        /// <summary>
        /// レンダラー情報を表示
        /// </summary>
        private void CreateRendererInfo()
        {
            var infoContainer = new VisualElement
            {
                style = {
                    backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f),
                    paddingTop = 8,
                    paddingBottom = 8,
                    paddingLeft = 10,
                    paddingRight = 10,
                    marginBottom = 15
                    // borderRadius = 4  // Not supported in this Unity version
                }
            };
            
            var rendererType = "Unknown";
            var meshName = "None";
            
            if (targetComponent != null)
            {
                var renderer = targetComponent.GetComponent<Renderer>();
                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                {
                    rendererType = "SkinnedMeshRenderer";
                    meshName = skinnedRenderer.sharedMesh?.name ?? "None";
                }
                else if (renderer is MeshRenderer)
                {
                    rendererType = "MeshRenderer";
                    var meshFilter = targetComponent.GetComponent<MeshFilter>();
                    meshName = meshFilter?.sharedMesh?.name ?? "None";
                }
            }
            
            var rendererLabel = new Label($"Renderer: {rendererType}")
            {
                style = {
                    fontSize = 12,
                    marginBottom = 2
                }
            };
            
            var meshLabel = new Label($"Mesh: {meshName}")
            {
                style = {
                    fontSize = 12
                }
            };
            
            infoContainer.Add(rendererLabel);
            infoContainer.Add(meshLabel);
            root.Add(infoContainer);
        }
        
        /// <summary>
        /// 表示設定セクションを作成
        /// </summary>
        private void CreateDisplaySettings()
        {
            var settingsContainer = new VisualElement
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
            
            var settingsLabel = new Label("Display Settings")
            {
                style = {
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 10
                }
            };
            settingsContainer.Add(settingsLabel);
            
            // 頂点球サイズ設定
            var vertexSizeContainer = new VisualElement { style = { marginBottom = 5 } };
            
            useAdaptiveSizeToggle = new Toggle("Use Adaptive Vertex Size")
            {
                value = targetComponent?.UseAdaptiveVertexSize ?? true
            };
            useAdaptiveSizeToggle.RegisterValueChangedCallback(evt =>
            {
                if (targetComponent != null)
                {
                    Undo.RecordObject(targetComponent, "Toggle Adaptive Vertex Size");
                    var so = new SerializedObject(targetComponent);
                    so.FindProperty("useAdaptiveVertexSize").boolValue = evt.newValue;
                    so.ApplyModifiedProperties();
                    
                    vertexSizeSlider.SetEnabled(!evt.newValue);
                    adaptiveMultiplierSlider.SetEnabled(evt.newValue);
                    SceneView.RepaintAll();
                }
            });
            
            vertexSizeSlider = new Slider("Manual Vertex Size", 0.001f, 0.1f)
            {
                value = targetComponent?.ManualVertexSphereSize ?? 0.01f
            };
            vertexSizeSlider.RegisterValueChangedCallback(evt =>
            {
                if (targetComponent != null)
                {
                    Undo.RecordObject(targetComponent, "Change Manual Vertex Size");
                    var so = new SerializedObject(targetComponent);
                    so.FindProperty("manualVertexSphereSize").floatValue = evt.newValue;
                    so.ApplyModifiedProperties();
                    SceneView.RepaintAll();
                }
            });
            
            adaptiveMultiplierSlider = new Slider("Adaptive Size Multiplier", 0.001f, 0.02f)
            {
                value = 0.007f
            };
            adaptiveMultiplierSlider.RegisterValueChangedCallback(evt =>
            {
                if (targetComponent != null)
                {
                    Undo.RecordObject(targetComponent, "Change Adaptive Size Multiplier");
                    var so = new SerializedObject(targetComponent);
                    so.FindProperty("adaptiveSizeMultiplier").floatValue = evt.newValue;
                    so.ApplyModifiedProperties();
                    targetComponent.UpdateMeshData(); // 再計算
                    SceneView.RepaintAll();
                }
            });
            
            vertexSizeContainer.Add(useAdaptiveSizeToggle);
            vertexSizeContainer.Add(vertexSizeSlider);
            vertexSizeContainer.Add(adaptiveMultiplierSlider);
            settingsContainer.Add(vertexSizeContainer);
            
            // UVプレビュー設定
            var uvPreviewContainer = new VisualElement { style = { marginTop = 10 } };
            
            autoUpdatePreviewToggle = new Toggle("Auto Update Preview")
            {
                value = targetComponent?.AutoUpdatePreview ?? true
            };
            autoUpdatePreviewToggle.RegisterValueChangedCallback(evt =>
            {
                if (targetComponent != null)
                {
                    Undo.RecordObject(targetComponent, "Toggle Auto Update Preview");
                    var so = new SerializedObject(targetComponent);
                    so.FindProperty("autoUpdatePreview").boolValue = evt.newValue;
                    so.ApplyModifiedProperties();
                }
            });
            
            var zoomContainer = new VisualElement 
            { 
                style = { 
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginTop = 5
                } 
            };
            
            zoomSlider = new Slider("Zoom", 1f, 8f)
            {
                value = targetComponent?.UvMapZoom ?? 1f,
                style = { flexGrow = 1, marginRight = 10 }
            };
            zoomSlider.RegisterValueChangedCallback(evt =>
            {
                if (targetComponent != null)
                {
                    targetComponent.SetZoomLevel(evt.newValue);
                    if (targetComponent.AutoUpdatePreview)
                        RefreshUVMapImage();
                }
            });
            
            resetZoomButton = new Button(() => 
            {
                if (targetComponent != null)
                {
                    targetComponent.ResetViewTransform();
                    zoomSlider.value = 1f;
                    if (targetComponent.AutoUpdatePreview)
                        RefreshUVMapImage();
                }
            })
            {
                text = "Reset",
                style = { width = 50 }
            };
            
            zoomContainer.Add(zoomSlider);
            zoomContainer.Add(resetZoomButton);
            
            uvPreviewContainer.Add(autoUpdatePreviewToggle);
            uvPreviewContainer.Add(zoomContainer);
            settingsContainer.Add(uvPreviewContainer);
            
            // 選択機能設定
            var selectionContainer = new VisualElement { style = { marginTop = 10 } };
            
            rangeSelectionToggle = new Toggle("Enable Range Selection")
            {
                value = targetComponent?.EnableRangeSelection ?? true
            };
            rangeSelectionToggle.RegisterValueChangedCallback(evt =>
            {
                if (targetComponent != null)
                {
                    Undo.RecordObject(targetComponent, "Toggle Range Selection");
                    var so = new SerializedObject(targetComponent);
                    so.FindProperty("enableRangeSelection").boolValue = evt.newValue;
                    so.ApplyModifiedProperties();
                }
            });
            
            selectionContainer.Add(rangeSelectionToggle);
            settingsContainer.Add(selectionContainer);
            
            // ルーペ機能設定
            var magnifyingContainer = new VisualElement { style = { marginTop = 10 } };
            
            magnifyingGlassToggle = new Toggle("Enable Magnifying Glass")
            {
                value = targetComponent?.EnableMagnifyingGlass ?? true
            };
            magnifyingGlassToggle.RegisterValueChangedCallback(evt =>
            {
                if (targetComponent != null)
                {
                    Undo.RecordObject(targetComponent, "Toggle Magnifying Glass");
                    var so = new SerializedObject(targetComponent);
                    so.FindProperty("enableMagnifyingGlass").boolValue = evt.newValue;
                    so.ApplyModifiedProperties();
                    
                    magnifyingGlassZoomSlider.SetEnabled(evt.newValue);
                    magnifyingGlassSizeSlider.SetEnabled(evt.newValue);
                }
            });
            
            magnifyingGlassZoomSlider = new Slider("Magnifying Zoom", 2f, 10f)
            {
                value = targetComponent?.MagnifyingGlassZoom ?? 4f
            };
            magnifyingGlassZoomSlider.RegisterValueChangedCallback(evt =>
            {
                if (targetComponent != null)
                {
                    Undo.RecordObject(targetComponent, "Change Magnifying Glass Zoom");
                    var so = new SerializedObject(targetComponent);
                    so.FindProperty("magnifyingGlassZoom").floatValue = evt.newValue;
                    so.ApplyModifiedProperties();
                }
            });
            
            magnifyingGlassSizeSlider = new Slider("Magnifying Size", 50f, 200f)
            {
                value = targetComponent?.MagnifyingGlassSize ?? 100f
            };
            magnifyingGlassSizeSlider.RegisterValueChangedCallback(evt =>
            {
                if (targetComponent != null)
                {
                    Undo.RecordObject(targetComponent, "Change Magnifying Glass Size");
                    var so = new SerializedObject(targetComponent);
                    so.FindProperty("magnifyingGlassSize").floatValue = evt.newValue;
                    so.ApplyModifiedProperties();
                }
            });
            
            magnifyingContainer.Add(magnifyingGlassToggle);
            magnifyingContainer.Add(magnifyingGlassZoomSlider);
            magnifyingContainer.Add(magnifyingGlassSizeSlider);
            settingsContainer.Add(magnifyingContainer);
            
            root.Add(settingsContainer);
            
            // 初期状態を設定
            UpdateSettingsUI();
        }
        
        /// <summary>
        /// 設定UIを更新
        /// </summary>
        private void UpdateSettingsUI()
        {
            if (targetComponent == null) return;
            
            vertexSizeSlider.SetEnabled(!targetComponent.UseAdaptiveVertexSize);
            adaptiveMultiplierSlider.SetEnabled(targetComponent.UseAdaptiveVertexSize);
            magnifyingGlassZoomSlider.SetEnabled(targetComponent.EnableMagnifyingGlass);
            magnifyingGlassSizeSlider.SetEnabled(targetComponent.EnableMagnifyingGlass);
        }
        
        /// <summary>
        /// UVマップ表示エリアを作成
        /// </summary>
        private void CreateUVMapArea()
        {
            var uvMapLabel = new Label("UV Map Preview")
            {
                style = {
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 5
                }
            };
            root.Add(uvMapLabel);
            
            // UVマップコンテナ
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
            
            // UVマップ画像要素
            uvMapImage = new VisualElement
            {
                style = {
                    width = UV_MAP_SIZE,
                    height = UV_MAP_SIZE,
                    backgroundImage = null
                }
            };
            
            // インタラクションイベントを登録（キャプチャフェーズでも取得）
            uvMapImage.RegisterCallback<MouseDownEvent>(OnUVMapMouseDown, TrickleDown.TrickleDown);
            uvMapImage.RegisterCallback<MouseMoveEvent>(OnUVMapMouseMove, TrickleDown.TrickleDown);
            uvMapImage.RegisterCallback<MouseUpEvent>(OnUVMapMouseUp, TrickleDown.TrickleDown);
            uvMapImage.RegisterCallback<WheelEvent>(OnUVMapWheel, TrickleDown.TrickleDown);
            
            // コンテナレベルでもマウスイベントを取得
            uvMapContainer.RegisterCallback<MouseMoveEvent>(OnUVMapContainerMouseMove, TrickleDown.TrickleDown);
            uvMapContainer.RegisterCallback<MouseUpEvent>(OnUVMapContainerMouseUp, TrickleDown.TrickleDown);
            
            // ツールチップ
            uvMapImage.tooltip = "Click: select islands, Drag: pan view, Wheel: zoom, Middle-click: magnifying glass";
            
            uvMapContainer.Add(uvMapImage);
            
            // 範囲選択オーバーレイを追加
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
            
            uvMapContainer.Add(rangeSelectionOverlay);
            
            // ルーペオーバーレイを追加
            CreateMagnifyingGlassOverlay();
            uvMapContainer.Add(magnifyingGlassOverlay);
            
            root.Add(uvMapContainer);
        }
        
        /// <summary>
        /// ルーペオーバーレイを作成
        /// </summary>
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
            
            // UV座標ラベル
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
            
            // ルーペ画像エリア
            magnifyingGlassImage = new VisualElement
            {
                style = {
                    flexGrow = 1,
                    backgroundColor = Color.black,
                    position = Position.Relative
                }
            };
            
            // レティクル（十字線）を追加
            CreateMagnifyingGlassReticle();
            
            magnifyingGlassOverlay.Add(magnifyingGlassLabel);
            magnifyingGlassOverlay.Add(magnifyingGlassImage);
        }
        
        /// <summary>
        /// ルーペにレティクル（十字線）を作成
        /// </summary>
        private void CreateMagnifyingGlassReticle()
        {
            // 縦線
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
            
            // 横線
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
        
        /// <summary>
        /// アイランドリストを作成
        /// </summary>
        private void CreateIslandList()
        {
            var listLabel = new Label("UV Islands")
            {
                style = {
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 5
                }
            };
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
            
            // リストアイテムの作成
            islandListView.makeItem = () =>
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
                        // borderRadius = 2  // Not supported in this Unity version
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
            };
            
            // リストアイテムのバインド
            islandListView.bindItem = (element, index) =>
            {
                if (targetComponent.UVIslands != null && index < targetComponent.UVIslands.Count)
                {
                    var island = targetComponent.UVIslands[index];
                    var container = element;
                    var colorBox = container[0];
                    var label = container[1] as Label;
                    var detailsContainer = container[2];
                    var vertexCountLabel = detailsContainer[0] as Label;
                    var faceCountLabel = detailsContainer[1] as Label;
                    
                    colorBox.style.backgroundColor = island.maskColor;
                    label.text = $"Island {island.islandID}";
                    vertexCountLabel.text = $"{island.vertexIndices.Count} verts";
                    faceCountLabel.text = $"{island.faceCount} faces";
                    
                    // 選択状態を反映
                    var isSelected = targetComponent.SelectedIslandIDs.Contains(island.islandID);
                    container.style.backgroundColor = isSelected ? 
                        new Color(0.3f, 0.5f, 0.8f, 0.3f) : Color.clear;
                }
            };
            
            // 選択変更イベント
            islandListView.onSelectionChange += OnIslandListSelectionChanged;
            
            root.Add(islandListView);
        }
        
        /// <summary>
        /// ハイライトオプションを作成
        /// </summary>
        private void CreateHighlightOptions()
        {
            var optionsContainer = new VisualElement
            {
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 10,
                    paddingLeft = 5
                }
            };
            
            highlightInSceneToggle = new Toggle("Highlight faces in Scene View")
            {
                value = highlightInScene,
                style = {
                    flexGrow = 1
                }
            };
            
            highlightInSceneToggle.RegisterValueChangedCallback(evt =>
            {
                highlightInScene = evt.newValue;
                SceneView.RepaintAll();
            });
            
            optionsContainer.Add(highlightInSceneToggle);
            root.Add(optionsContainer);
        }
        
        /// <summary>
        /// コントロールボタンを作成
        /// </summary>
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
                text = "Refresh",
                style = {
                    flexGrow = 1,
                    marginRight = 5
                }
            };
            
            clearSelectionButton = new Button(() => ClearSelection())
            {
                text = "Clear Selection",
                style = {
                    flexGrow = 1,
                    marginLeft = 5
                }
            };
            
            buttonContainer.Add(refreshButton);
            buttonContainer.Add(clearSelectionButton);
            root.Add(buttonContainer);
        }
        
        /// <summary>
        /// Deformerとの連携機能を作成
        /// </summary>
        private void CreateDeformerIntegration()
        {
            var integrationContainer = new VisualElement
            {
                style = {
                    backgroundColor = new Color(0.1f, 0.3f, 0.1f, 0.3f),
                    paddingTop = 10,
                    paddingBottom = 10,
                    paddingLeft = 10,
                    paddingRight = 10,
                    marginBottom = 15
                    // borderRadius = 4  // Not supported in this Unity version
                }
            };
            
            var integrationLabel = new Label("Deform Integration")
            {
                style = {
                    fontSize = 13,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 5
                }
            };
            
            var integrationDescription = new Label("Export selected islands to UVIslandMask deformer for use in mesh deformation.")
            {
                style = {
                    fontSize = 11,
                    color = Color.gray,
                    marginBottom = 10,
                    whiteSpace = WhiteSpace.Normal
                }
            };
            
            exportToDeformerButton = new Button(() => ExportToDeformer())
            {
                text = "Export to UVIslandMask Deformer",
                style = {
                    height = 25
                }
            };
            
            integrationContainer.Add(integrationLabel);
            integrationContainer.Add(integrationDescription);
            integrationContainer.Add(exportToDeformerButton);
            root.Add(integrationContainer);
        }
        
        /// <summary>
        /// ステータスエリアを作成
        /// </summary>
        private void CreateStatusArea()
        {
            statusLabel = new Label("Ready")
            {
                style = {
                    fontSize = 11,
                    color = Color.gray,
                    marginTop = 10
                }
            };
            root.Add(statusLabel);
        }
        
        /// <summary>
        /// UVマップマウスダウンイベント
        /// </summary>
        private void OnUVMapMouseDown(MouseDownEvent evt)
        {
            if (targetComponent == null) return;
            
            var localPosition = evt.localMousePosition;
            
            if (evt.button == 0) // 左クリック
            {
                // ルーペ表示中は通常のクリック選択を無効化
                if (isMagnifyingGlassActive)
                {
                    evt.StopPropagation();
                    return;
                }
                
                if (targetComponent.EnableRangeSelection && evt.shiftKey)
                {
                    // 範囲選択開始
                    StartRangeSelection(localPosition);
                    isDraggingUVMap = false;
                }
                else
                {
                    // 通常のクリック選択またはパン開始
                    var normalizedPos = new Vector2(
                        localPosition.x / UV_MAP_SIZE,
                        1f - (localPosition.y / UV_MAP_SIZE)
                    );
                    
                    // デバッグ情報
                    Debug.Log($"[UVIslandSelector] Click at local: {localPosition}, normalized: {normalizedPos}");
                    
                    int islandID = targetComponent.GetIslandAtUVCoordinate(normalizedPos);
                    
                    if (islandID >= 0)
                    {
                        // アイランドの選択を切り替え
                        Undo.RecordObject(targetComponent, "Toggle UV Island Selection");
                        targetComponent.ToggleIslandSelection(islandID);
                        RefreshUI();
                        isDraggingUVMap = false;
                    }
                    else
                    {
                        // パン開始
                        isDraggingUVMap = true;
                        lastMousePos = localPosition;
                    }
                }
                evt.StopPropagation();
            }
            else if (evt.button == 2 && targetComponent.EnableMagnifyingGlass) // 中ボタン
            {
                // ルーペ表示開始（通常のクリック選択を無効化）
                StartMagnifyingGlass(localPosition);
                evt.StopPropagation();
                // マウスダウン時点でisMagnifyingGlassActiveがtrueになるため、他の処理をブロック
                return;
            }
        }
        
        /// <summary>
        /// UVマップマウスムーブイベント
        /// </summary>
        private void OnUVMapMouseMove(MouseMoveEvent evt)
        {
            HandleMouseMove(evt);
        }
        
        /// <summary>
        /// マウスムーブイベントを処理
        /// </summary>
        private void HandleMouseMove(MouseMoveEvent evt)
        {
            if (targetComponent == null) return;
            
            var localPosition = evt.localMousePosition;
            
            if (isMagnifyingGlassActive)
            {
                // ルーペ更新（マウス移動に追従）
                UpdateMagnifyingGlass(localPosition);
                evt.StopPropagation();
            }
            else if (targetComponent.IsRangeSelecting)
            {
                // 範囲選択更新
                UpdateRangeSelection(localPosition);
                evt.StopPropagation();
            }
            else if (isDraggingUVMap && !isMagnifyingGlassActive)
            {
                // パン更新（正確な座標変換）- ルーペ表示中は無効化
                var deltaPos = localPosition - lastMousePos;
                
                // マウス移動量をUV座標空間での移動量に変換
                // ズーム時でもマウス移動と画面移動が一致するように計算
                var uvDelta = new Vector2(
                    deltaPos.x / (UV_MAP_SIZE * targetComponent.UvMapZoom),
                    -deltaPos.y / (UV_MAP_SIZE * targetComponent.UvMapZoom)
                );
                
                var currentOffset = targetComponent.UvMapPanOffset;
                targetComponent.SetPanOffset(currentOffset + uvDelta);
                
                lastMousePos = localPosition;
                
                if (targetComponent.AutoUpdatePreview)
                    RefreshUVMapImage();
                
                evt.StopPropagation();
            }
        }
        
        /// <summary>
        /// UVマップマウスアップイベント
        /// </summary>
        private void OnUVMapMouseUp(MouseUpEvent evt)
        {
            HandleMouseUp(evt);
        }
        
        /// <summary>
        /// UVマップコンテナマウスムーブイベント
        /// </summary>
        private void OnUVMapContainerMouseMove(MouseMoveEvent evt)
        {
            HandleMouseMove(evt);
        }
        
        /// <summary>
        /// UVマップコンテナマウスアップイベント
        /// </summary>
        private void OnUVMapContainerMouseUp(MouseUpEvent evt)
        {
            HandleMouseUp(evt);
        }
        
        /// <summary>
        /// Root要素マウスムーブイベント（グローバル追従用）
        /// </summary>
        private void OnRootMouseMove(MouseMoveEvent evt)
        {
            if (targetComponent == null) return;
            
            // root座標をUVマップコンテナ相対座標に変換
            var containerWorldBound = uvMapContainer.worldBound;
            
            // マウス座標をuvMapContainerのローカル座標に変換
            var relativeX = evt.mousePosition.x - containerWorldBound.x;
            var relativeY = evt.mousePosition.y - containerWorldBound.y;
            
            var localPos = new Vector2(relativeX, relativeY);
            
            // 範囲選択中の処理
            if (targetComponent.IsRangeSelecting)
            {
                // 範囲をUVマップエリア内にクランプ（表示用）
                var clampedPos = new Vector2(
                    Mathf.Clamp(localPos.x, 0, UV_MAP_SIZE),
                    Mathf.Clamp(localPos.y, 0, UV_MAP_SIZE)
                );
                
                // UV座標変換（クランプされた座標を使用）
                var uvCoord = LocalPosToUV(clampedPos);
                targetComponent.UpdateRangeSelection(uvCoord);
                UpdateRangeSelectionVisual();
                
                evt.StopPropagation();
            }
            // ルーペ表示中の処理
            else if (isMagnifyingGlassActive)
            {
                // マウス位置をUVマップ内にクランプしてルーペ更新
                var clampedPos = new Vector2(
                    Mathf.Clamp(localPos.x, 0, UV_MAP_SIZE),
                    Mathf.Clamp(localPos.y, 0, UV_MAP_SIZE)
                );
                
                UpdateMagnifyingGlass(clampedPos);
                evt.StopPropagation();
            }
            // パンドラッグ中の処理
            else if (isDraggingUVMap)
            {
                // ドラッグでのパン操作を継続
                var clampedPos = new Vector2(
                    Mathf.Clamp(localPos.x, 0, UV_MAP_SIZE),
                    Mathf.Clamp(localPos.y, 0, UV_MAP_SIZE)
                );
                
                var deltaPos = clampedPos - lastMousePos;
                
                // 正確な座標変換でマウス移動と画面移動を一致させる
                var uvDelta = new Vector2(
                    deltaPos.x / (UV_MAP_SIZE * targetComponent.UvMapZoom),
                    -deltaPos.y / (UV_MAP_SIZE * targetComponent.UvMapZoom)
                );
                
                var currentOffset = targetComponent.UvMapPanOffset;
                targetComponent.SetPanOffset(currentOffset + uvDelta);
                
                lastMousePos = clampedPos;
                
                if (targetComponent.AutoUpdatePreview)
                    RefreshUVMapImage();
                
                evt.StopPropagation();
            }
        }
        
        /// <summary>
        /// Root要素マウスアップイベント
        /// </summary>
        private void OnRootMouseUp(MouseUpEvent evt)
        {
            if (targetComponent == null) return;
            
            if (evt.button == 0) // 左ボタン
            {
                // 範囲選択の終了
                if (targetComponent.IsRangeSelecting)
                {
                    bool addToSelection = evt.shiftKey && !evt.ctrlKey;
                    bool removeFromSelection = evt.ctrlKey && evt.shiftKey;
                    FinishRangeSelection(addToSelection, removeFromSelection);
                    Debug.Log($"[UVIslandSelector] Range selection finished from root: add={addToSelection}, remove={removeFromSelection}");
                    evt.StopPropagation();
                }
                // パンドラッグの終了
                else if (isDraggingUVMap)
                {
                    isDraggingUVMap = false;
                    Debug.Log("[UVIslandSelector] Pan drag ended from root");
                    evt.StopPropagation();
                }
            }
            else if (evt.button == 2) // 中ボタン
            {
                // ルーペの終了
                if (isMagnifyingGlassActive)
                {
                    StopMagnifyingGlass();
                    Debug.Log("[UVIslandSelector] Magnifying glass stopped from root");
                    evt.StopPropagation();
                }
            }
        }
        
        /// <summary>
        /// マウスアップイベントを処理
        /// </summary>
        private void HandleMouseUp(MouseUpEvent evt)
        {
            if (evt.button == 0) // 左クリック
            {
                Debug.Log($"[UVIslandSelector] Mouse up detected: IsRangeSelecting={targetComponent?.IsRangeSelecting}, IsDragging={isDraggingUVMap}");
                
                if (isMagnifyingGlassActive)
                {
                    // ルーペ中央でクリック選択
                    HandleMagnifyingGlassClick(evt);
                }
                else if (targetComponent?.IsRangeSelecting == true)
                {
                    // 範囲選択完了
                    bool addToSelection = evt.shiftKey && !evt.ctrlKey;
                    bool removeFromSelection = evt.ctrlKey && evt.shiftKey;
                    FinishRangeSelection(addToSelection, removeFromSelection);
                    Debug.Log($"[UVIslandSelector] Range selection finished: add={addToSelection}, remove={removeFromSelection}");
                }
                
                isDraggingUVMap = false;
                evt.StopPropagation();
            }
            else if (evt.button == 2) // 中ボタン
            {
                // ルーペ終了
                StopMagnifyingGlass();
                evt.StopPropagation();
            }
        }
        
        /// <summary>
        /// UVマップホイールイベント
        /// </summary>
        private void OnUVMapWheel(WheelEvent evt)
        {
            if (targetComponent == null) return;
            
            var localPosition = evt.localMousePosition;
            var zoomPoint = LocalPosToUV(localPosition);
            var zoomDelta = -evt.delta.y * 0.1f;
            
            targetComponent.ZoomAtPoint(zoomPoint, zoomDelta);
            zoomSlider.value = targetComponent.UvMapZoom;
            
            if (targetComponent.AutoUpdatePreview)
                RefreshUVMapImage();
            
            evt.StopPropagation();
        }
        
        /// <summary>
        /// ローカル座標をUV座標に変換（変換行列考慮）
        /// </summary>
        private Vector2 LocalPosToUV(Vector2 localPos)
        {
            if (targetComponent == null) 
                return Vector2.zero;
            
            // ローカル座標を正規化（Y軸反転）
            var normalizedPos = new Vector2(
                localPos.x / UV_MAP_SIZE,
                1f - (localPos.y / UV_MAP_SIZE)
            );
            
            // 変換行列の逆行列を使って実際のUV座標を取得
            var transform = targetComponent.CalculateUVTransformMatrix();
            var inverseTransform = transform.inverse;
            var actualUV = inverseTransform.MultiplyPoint3x4(new Vector3(normalizedPos.x, normalizedPos.y, 0f));
            
            // デバッグ情報
            Debug.Log($"[LocalPosToUV] LocalPos: {localPos}, Normalized: {normalizedPos}, ActualUV: {actualUV}");
            
            return new Vector2(actualUV.x, actualUV.y);
        }
        
        /// <summary>
        /// UV座標をローカル座標に変換（変換行列考慮）
        /// </summary>
        private Vector2 UVToLocalPos(Vector2 uvCoord)
        {
            if (targetComponent == null) 
                return Vector2.zero;
            
            // 変換行列を適用
            var transform = targetComponent.CalculateUVTransformMatrix();
            var transformedUV = transform.MultiplyPoint3x4(new Vector3(uvCoord.x, uvCoord.y, 0f));
            
            // ローカル座標に変換（Y軸反転）
            return new Vector2(
                transformedUV.x * UV_MAP_SIZE,
                (1f - transformedUV.y) * UV_MAP_SIZE
            );
        }
        
        /// <summary>
        /// 範囲選択を開始
        /// </summary>
        private void StartRangeSelection(Vector2 localPos)
        {
            var uvCoord = LocalPosToUV(localPos);
            targetComponent.StartRangeSelection(uvCoord);
            
            // 初期の範囲選択表示を更新
            UpdateRangeSelectionVisual();
        }
        
        /// <summary>
        /// 範囲選択を更新
        /// </summary>
        private void UpdateRangeSelection(Vector2 localPos)
        {
            var uvCoord = LocalPosToUV(localPos);
            targetComponent.UpdateRangeSelection(uvCoord);
            
            // 範囲選択矩形を視覚的に表示
            UpdateRangeSelectionVisual();
        }
        
        /// <summary>
        /// 範囲選択の視覚的表示を更新
        /// </summary>
        private void UpdateRangeSelectionVisual()
        {
            if (targetComponent == null || !targetComponent.IsRangeSelecting)
            {
                rangeSelectionOverlay.style.display = DisplayStyle.None;
                return;
            }
            
            var selectionRect = targetComponent.GetCurrentSelectionRect();
            
            // UV座標をローカル座標に変換
            var startLocal = UVToLocalPos(new Vector2(selectionRect.xMin, selectionRect.yMax));
            var endLocal = UVToLocalPos(new Vector2(selectionRect.xMax, selectionRect.yMin));
            
            // 矩形の位置とサイズを設定
            var left = Mathf.Min(startLocal.x, endLocal.x);
            var top = Mathf.Min(startLocal.y, endLocal.y);
            var width = Mathf.Abs(endLocal.x - startLocal.x);
            var height = Mathf.Abs(endLocal.y - startLocal.y);
            
            rangeSelectionOverlay.style.left = left;
            rangeSelectionOverlay.style.top = top;
            rangeSelectionOverlay.style.width = width;
            rangeSelectionOverlay.style.height = height;
            
            // Ctrl+Shiftキーの状態を確認して色を変更
            bool isCtrlShiftPressed = Event.current != null && Event.current.control && Event.current.shift;
            if (isCtrlShiftPressed)
            {
                // 選択解除モードの色（赤系）
                rangeSelectionOverlay.style.backgroundColor = new Color(0.8f, 0.3f, 0.3f, 0.3f);
                rangeSelectionOverlay.style.borderLeftColor = new Color(0.8f, 0.3f, 0.3f, 0.8f);
                rangeSelectionOverlay.style.borderRightColor = new Color(0.8f, 0.3f, 0.3f, 0.8f);
                rangeSelectionOverlay.style.borderTopColor = new Color(0.8f, 0.3f, 0.3f, 0.8f);
                rangeSelectionOverlay.style.borderBottomColor = new Color(0.8f, 0.3f, 0.3f, 0.8f);
            }
            else
            {
                // 通常の選択モードの色（青系）
                rangeSelectionOverlay.style.backgroundColor = new Color(0.3f, 0.5f, 0.8f, 0.3f);
                rangeSelectionOverlay.style.borderLeftColor = new Color(0.3f, 0.5f, 0.8f, 0.8f);
                rangeSelectionOverlay.style.borderRightColor = new Color(0.3f, 0.5f, 0.8f, 0.8f);
                rangeSelectionOverlay.style.borderTopColor = new Color(0.3f, 0.5f, 0.8f, 0.8f);
                rangeSelectionOverlay.style.borderBottomColor = new Color(0.3f, 0.5f, 0.8f, 0.8f);
            }
            
            rangeSelectionOverlay.style.display = DisplayStyle.Flex;
        }
        
        /// <summary>
        /// 範囲選択を完了
        /// </summary>
        private void FinishRangeSelection(bool addToSelection, bool removeFromSelection = false)
        {
            targetComponent.FinishRangeSelection(addToSelection, removeFromSelection);
            
            // 範囲選択オーバーレイを非表示
            rangeSelectionOverlay.style.display = DisplayStyle.None;
            
            RefreshUI();
        }
        
        /// <summary>
        /// ルーペ表示を開始
        /// </summary>
        private void StartMagnifyingGlass(Vector2 localPos)
        {
            if (!targetComponent.EnableMagnifyingGlass) return;
            
            isMagnifyingGlassActive = true;
            currentMagnifyingMousePos = localPos;
            UpdateMagnifyingGlass(localPos);
        }
        
        /// <summary>
        /// ルーペを更新
        /// </summary>
        private void UpdateMagnifyingGlass(Vector2 localPos)
        {
            if (!isMagnifyingGlassActive || !targetComponent.EnableMagnifyingGlass) return;
            
            currentMagnifyingMousePos = localPos;
            
            // 範囲選択と同じ座標変換を使用
            var uvCoord = LocalPosToUV(localPos);
            var size = Mathf.RoundToInt(targetComponent.MagnifyingGlassSize);
            
            // デバッグ情報
            Debug.Log($"[Magnifying Glass Editor] LocalPos: {localPos}, UV (LocalPosToUV): {uvCoord}, Zoom: {targetComponent.UvMapZoom}, Pan: {targetComponent.UvMapPanOffset}");
            
            // 既存のテクスチャを破棄
            if (magnifyingGlassTexture != null)
            {
                DestroyImmediate(magnifyingGlassTexture);
            }
            
            // 範囲選択と同じUV座標変換を使ってルーペテクスチャを生成
            magnifyingGlassTexture = targetComponent.GenerateMagnifyingGlassTexture(uvCoord, size);
            
            if (magnifyingGlassTexture != null)
            {
                // ルーペの位置をマウス位置近くに設定
                var overlaySize = 120f;
                var posX = Mathf.Clamp(localPos.x + 10, 0, UV_MAP_SIZE - overlaySize);
                var posY = Mathf.Clamp(localPos.y - overlaySize - 10, 0, UV_MAP_SIZE - 140);
                
                magnifyingGlassOverlay.style.left = posX;
                magnifyingGlassOverlay.style.top = posY;
                
                // UV座標を表示（範囲選択と同じ変換）
                magnifyingGlassLabel.text = $"UV: ({uvCoord.x:F3}, {uvCoord.y:F3})";
                
                // ルーペ画像を設定
                magnifyingGlassImage.style.backgroundImage = new StyleBackground(magnifyingGlassTexture);
                magnifyingGlassImage.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
                
                // 表示
                magnifyingGlassOverlay.style.display = DisplayStyle.Flex;
            }
        }
        
        /// <summary>
        /// ルーペ表示を停止
        /// </summary>
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
        
        /// <summary>
        /// ルーペ中央でのクリック処理
        /// </summary>
        private void HandleMagnifyingGlassClick(MouseUpEvent evt)
        {
            // 通常のクリック選択と同じ正規化座標を使用
            var normalizedPos = new Vector2(
                currentMagnifyingMousePos.x / UV_MAP_SIZE,
                1f - (currentMagnifyingMousePos.y / UV_MAP_SIZE)
            );
            
            int islandID = targetComponent.GetIslandAtUVCoordinate(normalizedPos);
            
            Debug.Log($"[Magnifying Glass Click] Normalized Pos: {normalizedPos}, Mouse Pos: {currentMagnifyingMousePos}, Island ID: {islandID}");
            
            if (islandID >= 0)
            {
                Undo.RecordObject(targetComponent, "Select UV Island from Magnifying Glass");
                targetComponent.ToggleIslandSelection(islandID);
                RefreshUI();
                Debug.Log($"[UVIslandSelector] Selected island {islandID} via magnifying glass at normalized pos: {normalizedPos}");
            }
            else
            {
                Debug.Log($"[UVIslandSelector] No island found at magnifying glass normalized pos: {normalizedPos}");
            }
        }
        
        /// <summary>
        /// UVマップ画像のみを更新
        /// </summary>
        private void RefreshUVMapImage()
        {
            if (targetComponent?.UvMapTexture != null)
            {
                uvMapImage.style.backgroundImage = new StyleBackground(targetComponent.UvMapTexture);
            }
        }
        
        /// <summary>
        /// UVマップクリックイベント（旧版・互換性のため残存）
        /// </summary>
        private void OnUVMapClick(MouseDownEvent evt)
        {
            if (targetComponent == null) return;
            
            // クリック位置をUV座標に変換
            var localPosition = evt.localMousePosition;
            var uvCoord = new Vector2(
                localPosition.x / UV_MAP_SIZE,
                1f - (localPosition.y / UV_MAP_SIZE) // Yを反転
            );
            
            // クリック位置のアイランドを検索
            int islandID = targetComponent.GetIslandAtUVCoordinate(uvCoord);
            if (islandID >= 0)
            {
                // アイランドの選択を切り替え
                Undo.RecordObject(targetComponent, "Toggle UV Island Selection");
                targetComponent.ToggleIslandSelection(islandID);
                
                RefreshUI();
                evt.StopPropagation();
            }
        }
        
        /// <summary>
        /// アイランドの面を描画（オフセット付き、ZFighting修正）
        /// </summary>
        private void DrawIslandFacesWithOffset(UVIslandSelector.UVIsland island, Vector3[] vertices, Transform transform, Color baseColor)
        {
            var mesh = targetComponent.TargetMesh;
            var normals = mesh.normals;
            const float offset = 0.002f; // オフセット量
            
            // 面のワイヤーフレーム（明るい色、太い線）
            Handles.color = baseColor;
            
            for (int i = 0; i < island.triangleIndices.Count; i += 3)
            {
                if (i + 2 >= island.triangleIndices.Count) break;
                
                var v0Index = island.triangleIndices[i];
                var v1Index = island.triangleIndices[i + 1];
                var v2Index = island.triangleIndices[i + 2];
                
                if (v0Index < vertices.Length && v1Index < vertices.Length && v2Index < vertices.Length)
                {
                    Vector3 worldV0, worldV1, worldV2;
                    
                    // 法線オフセットを適用
                    if (normals != null && normals.Length > v0Index && normals.Length > v1Index && normals.Length > v2Index)
                    {
                        worldV0 = transform.TransformPoint(vertices[v0Index] + normals[v0Index] * offset);
                        worldV1 = transform.TransformPoint(vertices[v1Index] + normals[v1Index] * offset);
                        worldV2 = transform.TransformPoint(vertices[v2Index] + normals[v2Index] * offset);
                    }
                    else
                    {
                        // 法線がない場合はカメラ方向でオフセット
                        var cameraTransform = SceneView.currentDrawingSceneView?.camera?.transform;
                        var offsetDirection = cameraTransform != null ? -cameraTransform.forward : Vector3.up;
                        
                        worldV0 = transform.TransformPoint(vertices[v0Index]) + offsetDirection * offset;
                        worldV1 = transform.TransformPoint(vertices[v1Index]) + offsetDirection * offset;
                        worldV2 = transform.TransformPoint(vertices[v2Index]) + offsetDirection * offset;
                    }
                    
                    // 三角形のエッジを太い線で描画
                    Handles.DrawLine(worldV0, worldV1, 4f);
                    Handles.DrawLine(worldV1, worldV2, 4f);
                    Handles.DrawLine(worldV2, worldV0, 4f);
                }
            }
            
            // 面の半透明填りつぶし（さらにオフセット）
            var fillColor = baseColor;
            fillColor.a = 0.25f;
            Handles.color = fillColor;
            
            for (int i = 0; i < island.triangleIndices.Count; i += 3)
            {
                if (i + 2 >= island.triangleIndices.Count) break;
                
                var v0Index = island.triangleIndices[i];
                var v1Index = island.triangleIndices[i + 1];
                var v2Index = island.triangleIndices[i + 2];
                
                if (v0Index < vertices.Length && v1Index < vertices.Length && v2Index < vertices.Length)
                {
                    Vector3 worldV0, worldV1, worldV2;
                    
                    // 法線オフセットを適用（より大きなオフセット）
                    if (normals != null && normals.Length > v0Index && normals.Length > v1Index && normals.Length > v2Index)
                    {
                        worldV0 = transform.TransformPoint(vertices[v0Index] + normals[v0Index] * offset * 3f);
                        worldV1 = transform.TransformPoint(vertices[v1Index] + normals[v1Index] * offset * 3f);
                        worldV2 = transform.TransformPoint(vertices[v2Index] + normals[v2Index] * offset * 3f);
                    }
                    else
                    {
                        var cameraTransform = SceneView.currentDrawingSceneView?.camera?.transform;
                        var offsetDirection = cameraTransform != null ? -cameraTransform.forward : Vector3.up;
                        
                        worldV0 = transform.TransformPoint(vertices[v0Index]) + offsetDirection * offset * 3f;
                        worldV1 = transform.TransformPoint(vertices[v1Index]) + offsetDirection * offset * 3f;
                        worldV2 = transform.TransformPoint(vertices[v2Index]) + offsetDirection * offset * 3f;
                    }
                    
                    // 三角形の填りつぶし
                    Handles.DrawAAConvexPolygon(worldV0, worldV1, worldV2);
                }
            }
        }
        
        /// <summary>
        /// アイランドリスト選択変更イベント
        /// </summary>
        private void OnIslandListSelectionChanged(System.Collections.Generic.IEnumerable<object> selectedItems)
        {
            if (targetComponent == null) return;
            
            var selectedIndices = islandListView.selectedIndices.ToArray();
            
            Undo.RecordObject(targetComponent, "Select UV Islands");
            targetComponent.ClearSelection();
            
            foreach (int index in selectedIndices)
            {
                if (index < targetComponent.UVIslands.Count)
                {
                    int islandID = targetComponent.UVIslands[index].islandID;
                    targetComponent.ToggleIslandSelection(islandID);
                }
            }
            
            RefreshUI();
        }
        
        /// <summary>
        /// データを更新
        /// </summary>
        private void RefreshData()
        {
            if (targetComponent == null) return;
            
            statusLabel.text = "Refreshing...";
            
            // メッシュデータを更新
            targetComponent.UpdateMeshData();
            
            // UVマップテクスチャを生成
            var uvTexture = targetComponent.GenerateUVMapTexture(UV_MAP_SIZE, UV_MAP_SIZE);
            
            RefreshUI();
            
            statusLabel.text = $"Found {targetComponent.UVIslands.Count} UV islands";
        }
        
        /// <summary>
        /// UIを更新
        /// </summary>
        private void RefreshUI()
        {
            // UVマップ画像を更新
            RefreshUVMapImage();
            
            // リストビューを更新
            if (targetComponent.UVIslands != null)
            {
                islandListView.itemsSource = targetComponent.UVIslands;
                islandListView.RefreshItems();
            }
            
            // 設定UIを更新
            UpdateSettingsUI();
            
            // 選択数を更新
            var selectedCount = targetComponent.SelectedIslandIDs?.Count ?? 0;
            var maskedVertexCount = targetComponent.VertexMask?.Length ?? 0;
            var maskedFaceCount = (targetComponent.TriangleMask?.Length ?? 0) / 3;
            
            if (selectedCount > 0)
            {
                statusLabel.text = $"{selectedCount} islands selected, {maskedVertexCount} vertices, {maskedFaceCount} faces masked";
            }
            else
            {
                statusLabel.text = $"Found {targetComponent.UVIslands?.Count ?? 0} UV islands";
            }
            
            // エクスポートボタンの有効/無効
            exportToDeformerButton.SetEnabled(selectedCount > 0);
            
            // シーンビューを更新
            SceneView.RepaintAll();
        }
        
        /// <summary>
        /// 選択をクリア
        /// </summary>
        private void ClearSelection()
        {
            if (targetComponent == null) return;
            
            Undo.RecordObject(targetComponent, "Clear UV Island Selection");
            targetComponent.ClearSelection();
            
            RefreshUI();
        }
        
        /// <summary>
        /// Deformerにエクスポート
        /// </summary>
        private void ExportToDeformer()
	    {
		    /*
            var uvIslandMask = targetComponent.GetComponent<UVIslandMask>();
            if (uvIslandMask == null)
            {
                // UVIslandMaskコンポーネントを追加
                uvIslandMask = Undo.AddComponent<UVIslandMask>(targetComponent.gameObject);
            }
            
            // 選択ポイントを生成してDeformerに設定
            var selectionPoints = targetComponent.GenerateSelectionPointsForDeformer();
            
            Undo.RecordObject(uvIslandMask, "Export UV Island Selection to Deformer");
            uvIslandMask.SelectionPoints = selectionPoints;
            
            EditorUtility.SetDirty(uvIslandMask);
            
            statusLabel.text = $"Exported {targetComponent.SelectedIslandIDs.Count} islands to UVIslandMask deformer";
            
            // 少し遅らせてInspectorを更新
            EditorApplication.delayCall += () => 
            {
                Selection.activeGameObject = targetComponent.gameObject;
		    };
		    */
        }
        
        /// <summary>
        /// エディタの有効化
        /// </summary>
        private void OnEnable()
        {
            targetComponent = target as UVIslandSelector;
            Undo.undoRedoPerformed += RefreshUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }
        
        /// <summary>
        /// エディタの無効化
        /// </summary>
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshUI;
            SceneView.duringSceneGui -= OnSceneGUI;
        }
        
        /// <summary>
        /// シーンGUIを描画
        /// </summary>
        private void OnSceneGUI(SceneView sceneView)
        {
            if (targetComponent == null || !targetComponent.HasSelectedIslands || !highlightInScene) 
                return;
            
            // 選択された面をシーンビューで強調表示
            DrawSelectedFacesInScene();
        }
        
        /// <summary>
        /// 選択された面をシーンビューで描画（ZFighting修正版）
        /// </summary>
        private void DrawSelectedFacesInScene()
        {
            var mesh = targetComponent.TargetMesh;
            if (mesh == null || targetComponent.TriangleMask == null) return;
            
            var vertices = mesh.vertices;
            var transform = targetComponent.transform;
            
            // ZFightingを避けるための設定
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always; // 常に最前面に表示
            
            // 選択されたアイランドごとに異なる色で描画
            var islandColors = new Color[] { 
                Color.red, Color.green, Color.blue, Color.yellow, 
                Color.magenta, Color.cyan, Color.white 
            };
            
            int colorIndex = 0;
            foreach (var islandID in targetComponent.SelectedIslandIDs)
            {
                var island = targetComponent.UVIslands.FirstOrDefault(i => i.islandID == islandID);
                if (island == null) continue;
                
                var baseColor = islandColors[colorIndex % islandColors.Length];
                colorIndex++;
                
                // アイランドの面を描画（オフセット付き）
                DrawIslandFacesWithOffset(island, vertices, transform, baseColor);
            }
            
            // 選択された頂点をポイントで描画
            DrawSelectedVertices(vertices, transform);
            
            // 元の設定に戻す
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
        }
        
        /// <summary>
        /// アイランドの面を描画
        /// </summary>
        private void DrawIslandFaces(UVIslandSelector.UVIsland island, Vector3[] vertices, Transform transform)
        {
            // 面の塗りつぶし（半透明）
            var fillColor = Handles.color;
            fillColor.a = 0.2f;
            Handles.color = fillColor;
            
            for (int i = 0; i < island.triangleIndices.Count; i += 3)
            {
                if (i + 2 >= island.triangleIndices.Count) break;
                
                var v0Index = island.triangleIndices[i];
                var v1Index = island.triangleIndices[i + 1];
                var v2Index = island.triangleIndices[i + 2];
                
                if (v0Index < vertices.Length && v1Index < vertices.Length && v2Index < vertices.Length)
                {
                    var worldV0 = transform.TransformPoint(vertices[v0Index]);
                    var worldV1 = transform.TransformPoint(vertices[v1Index]);
                    var worldV2 = transform.TransformPoint(vertices[v2Index]);
                    
                    // 三角形の塗りつぶし
                    Handles.DrawAAConvexPolygon(worldV0, worldV1, worldV2);
                }
            }
            
            // 面のワイヤーフレーム（不透明）
            fillColor.a = 0.8f;
            Handles.color = fillColor;
            
            for (int i = 0; i < island.triangleIndices.Count; i += 3)
            {
                if (i + 2 >= island.triangleIndices.Count) break;
                
                var v0Index = island.triangleIndices[i];
                var v1Index = island.triangleIndices[i + 1];
                var v2Index = island.triangleIndices[i + 2];
                
                if (v0Index < vertices.Length && v1Index < vertices.Length && v2Index < vertices.Length)
                {
                    var worldV0 = transform.TransformPoint(vertices[v0Index]);
                    var worldV1 = transform.TransformPoint(vertices[v1Index]);
                    var worldV2 = transform.TransformPoint(vertices[v2Index]);
                    
                    // 三角形のエッジ
                    Handles.DrawLine(worldV0, worldV1);
                    Handles.DrawLine(worldV1, worldV2);
                    Handles.DrawLine(worldV2, worldV0);
                }
            }
        }
        
        /// <summary>
        /// 選択された頂点を描画
        /// </summary>
        private void DrawSelectedVertices(Vector3[] vertices, Transform transform)
        {
            if (targetComponent.VertexMask == null) return;
            
            Handles.color = Color.white;
            
            // 適応的または手動設定の頂点球サイズを取得
            float sphereSize = targetComponent.AdaptiveVertexSphereSize;
            int drawnCount = 0;
            int maxVertices = targetComponent.MaxDisplayVertices;
            
            foreach (int vertexIndex in targetComponent.VertexMask)
            {
                if (vertexIndex < vertices.Length)
                {
                    var worldPos = transform.TransformPoint(vertices[vertexIndex]);
                    Handles.SphereHandleCap(0, worldPos, Quaternion.identity, sphereSize, EventType.Repaint);
                    
                    drawnCount++;
                    
                    // パフォーマンス最適化：最大表示数に達したら停止
                    if (targetComponent.EnablePerformanceOptimization && drawnCount >= maxVertices)
                    {
                        break;
                    }
                }
            }
        }
    }
}