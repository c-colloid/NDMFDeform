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
        private VisualElement submeshSelector;
        private Label currentSubmeshLabel;
        private Button prevSubmeshButton;
        private Button nextSubmeshButton;
        private Slider zoomSlider;
        private Button resetZoomButton;
        private Toggle rangeSelectionToggle;
        
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

        // Texture throttling
        private float lastUpdateTime = 0f;
		private const float TEXTURE_UPDATE_THROTTLE = 0.016f; // ~60fps limit

        // Async initialization
        private AsyncInitializationManager asyncInitManager;
        private InitializationProgressView progressView;

        // Custom Views
        private HighlightSettingsView highlightSettingsView;
        private SubmeshSelectorView submeshSelectorView;
        #endregion

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

            // Cancel any ongoing async initialization
            if (asyncInitManager != null && asyncInitManager.IsRunning)
            {
                asyncInitManager.Cancel();
                asyncInitManager = null;
            }

            // Get original mesh for UV mapping
            var originalMesh = GetOriginalMesh();

            // Create empty selector (will be initialized asynchronously)
            selector = new UVIslandSelector();

            // Create UI root
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

            CreateHeader();
            CreateMaskSettings();
            CreateSubmeshSelector();
            CreateHighlightSettings();
            CreateUVMapArea();
            CreateIslandList();
            CreateControlButtons();
            CreateStatusArea();

            // Register global mouse events
            root.RegisterCallback<MouseMoveEvent>(OnRootMouseMove, TrickleDown.TrickleDown);
            root.RegisterCallback<MouseUpEvent>(OnRootMouseUp, TrickleDown.TrickleDown);

            // Start async initialization if we have a mesh
            if (originalMesh != null)
            {
                // Show progress view
                if (progressView != null)
                {
                    progressView.Progress = 0f;
                    progressView.StatusMessage = "Initializing UV Map...";
                    progressView.style.display = DisplayStyle.Flex;
                }

                // Configure selector before initialization
                selector.SetMeshWithoutAnalysis(originalMesh);
                selector.SetSelectedSubmeshes(targetMask.SelectedSubmeshes);
                selector.SetPreviewSubmesh(targetMask.CurrentPreviewSubmesh);
                selector.TargetTransform = GetRendererTransform();

                // Start async initialization
                asyncInitManager = new AsyncInitializationManager();
                asyncInitManager.StartInitialization(originalMesh, selector, OnAsyncInitializationCompleted);
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
            
            statusLabel.text = "更新中...";
            
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
                statusLabel.text = $"{islandCount}個のUVアイランドが見つかりました";
            }
            catch (System.Exception ex)
            {
                statusLabel.text = $"Error: {ex.Message}";
                Debug.LogError($"[UVIslandMaskEditor] Error refreshing data: {ex}");
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

            // Always generate full texture
            selector.GenerateUVMapTexture();

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

                // Always generate full texture
                selector.GenerateUVMapTexture();
                RefreshUVMapImage();

                // Update magnifying glass to show the selection change immediately
                // ルーペ表示を即座に更新して選択変更を反映
                if (isMagnifyingGlassActive)
                {
                    UpdateMagnifyingGlass(currentMagnifyingMousePos);
                }

                RefreshUI(false);
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
            RefreshUI(false);
        }

        #endregion

        #region Editor Lifecycle (continued)
        // エディタライフサイクル（続き）
        // Editor lifecycle callbacks

        private void OnEnable()
        {
            base.OnEnable(); // Call parent class initialization

            targetMask = target as UVIslandMask;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            // Stop monitoring async initialization
            EditorApplication.update -= MonitorAsyncInitialization;

            // Cancel any ongoing async initialization
            if (asyncInitManager != null && asyncInitManager.IsRunning)
            {
                asyncInitManager.Cancel();
                asyncInitManager = null;
            }

            // Dispose selector
            if (selector != null)
            {
                selector.Dispose();
                selector = null;
            }

            // Clean up textures
            if (magnifyingGlassTexture != null)
            {
                DestroyImmediate(magnifyingGlassTexture);
                magnifyingGlassTexture = null;
            }

            Undo.undoRedoPerformed -= OnUndoRedo;
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
        /// Update zoom slider label to show current zoom value
        /// ズームスライダーのラベルを更新して現在のズーム値を表示
        /// </summary>
        private void UpdateZoomSliderLabel()
        {
            if (zoomSlider != null && selector != null)
            {
                zoomSlider.label = $"ズーム (x{selector.UvMapZoom:F1})";
            }
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


        
    }
}