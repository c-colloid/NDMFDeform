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
        
        private const int UV_MAP_SIZE = 300;
        private static bool highlightInScene = true;
        
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
            
            // クリックイベントを登録
            uvMapImage.RegisterCallback<MouseDownEvent>(OnUVMapClick);
            
            // ツールチップ
            uvMapImage.tooltip = "Click on UV islands to select/deselect them";
            
            uvMapContainer.Add(uvMapImage);
            root.Add(uvMapContainer);
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
        /// UVマップクリックイベント
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
            if (targetComponent.UvMapTexture != null)
            {
                uvMapImage.style.backgroundImage = new StyleBackground(targetComponent.UvMapTexture);
            }
            
            // リストビューを更新
            if (targetComponent.UVIslands != null)
            {
                islandListView.itemsSource = targetComponent.UVIslands;
                islandListView.RefreshItems();
            }
            
            // 選択数を更新
            var selectedCount = targetComponent.SelectedIslandIDs?.Count ?? 0;
            var maskedVertexCount = targetComponent.VertexMask?.Length ?? 0;
            var maskedFaceCount = (targetComponent.TriangleMask?.Length ?? 0) / 3;
            
            if (selectedCount > 0)
            {
                statusLabel.text = $"{selectedCount} islands selected, {maskedVertexCount} vertices, {maskedFaceCount} faces masked";
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
            
            foreach (int vertexIndex in targetComponent.VertexMask)
            {
                if (vertexIndex < vertices.Length)
                {
                    var worldPos = transform.TransformPoint(vertices[vertexIndex]);
                    Handles.SphereHandleCap(0, worldPos, Quaternion.identity, 0.01f, EventType.Repaint);
                }
            }
        }
    }
}