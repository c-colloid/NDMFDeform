using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;

namespace Deform.Masking.Editor
{
    /// <summary>
    /// UV Island selection and editing utility (Editor-only)
    /// UVアイランド選択・編集ユーティリティ（エディタ専用）
    /// </summary>
    public class UVIslandSelector
    {
        #region Constants
        private const float MIN_ZOOM = 1f;
        private const float MAX_ZOOM = 8f;
        private const float DEFAULT_MANUAL_VERTEX_SIZE = 0.01f;
        private const float DEFAULT_ADAPTIVE_SIZE_MULTIPLIER = 0.007f;
        private const float DEFAULT_MAGNIFYING_GLASS_ZOOM = 8f;
        private const float DEFAULT_MAGNIFYING_GLASS_SIZE = 100f;
        private const int DEFAULT_MAX_DISPLAY_VERTICES = 1000;
        private const float MIN_ADAPTIVE_VERTEX_SIZE = 0.001f;
        private const float MAX_ADAPTIVE_VERTEX_SIZE = 0.05f;
        private const float UV_DISTANCE_THRESHOLD = 0.1f;
        private const float CENTER_OFFSET = -0.5f;
        private const float RECENTER_OFFSET = 0.5f;
        private const int DEFAULT_TEXTURE_SIZE = 512;
        private const int DEFAULT_MAGNIFYING_GLASS_SIZE_INT = 100;
        private const int JOB_BATCH_SIZE = 64;
        private const float BLEND_FACTOR = 0.7f;
        private const float GRID_SPACING = 0.1f;
        
        // Color constants
        private static readonly Color BACKGROUND_COLOR = new Color(0.1f, 0.1f, 0.1f, 1.0f);
        private static readonly Color MAGNIFYING_BACKGROUND_COLOR = new Color(0.15f, 0.15f, 0.15f, 1.0f);
        private static readonly Color GRID_COLOR = new Color(0.3f, 0.3f, 0.3f, 1.0f);
        private static readonly Color UNSELECTED_ISLAND_COLOR = new Color(0.5f, 0.5f, 0.5f, 0.3f);
        #endregion
        // Core data
        private Mesh targetMesh;
        private List<UVIslandAnalyzer.UVIsland> uvIslands = new List<UVIslandAnalyzer.UVIsland>();
        private List<UVIslandAnalyzer.UVIsland> allUVIslands = new List<UVIslandAnalyzer.UVIsland>(); // All islands from all submeshes
        private List<int> selectedSubmeshIndices = new List<int> { 0 }; // Selected submeshes for filtering
        private int currentPreviewSubmesh = 0; // Current submesh being previewed

        // Per-submesh selection storage: Key=submeshIndex, Value=set of island IDs
        private Dictionary<int, HashSet<int>> selectedIslandsPerSubmesh = new Dictionary<int, HashSet<int>>();

        private Texture2D uvMapTexture;
        
        #region Fields
        // Display settings
        private bool useAdaptiveVertexSize = true;
        private float manualVertexSphereSize = DEFAULT_MANUAL_VERTEX_SIZE;
        private float adaptiveSizeMultiplier = DEFAULT_ADAPTIVE_SIZE_MULTIPLIER;
        private float adaptiveVertexSphereSize = DEFAULT_MANUAL_VERTEX_SIZE;
        
        // UV Map preview settings
        private bool autoUpdatePreview = true;
        private float uvMapZoom = MIN_ZOOM;
        private Vector2 uvMapPanOffset = Vector2.zero;
        private bool enableMagnifyingGlass = true;
        private float magnifyingGlassZoom = DEFAULT_MAGNIFYING_GLASS_ZOOM;
        private float magnifyingGlassSize = DEFAULT_MAGNIFYING_GLASS_SIZE;
        
        // Selection settings  
        private bool enableRangeSelection = true;
        private bool isRangeSelecting = false;
        private Vector2 rangeSelectionStart = Vector2.zero;
        private Vector2 rangeSelectionEnd = Vector2.zero;
        
        // Performance settings
        private int maxDisplayVertices = DEFAULT_MAX_DISPLAY_VERTICES;
        private bool enablePerformanceOptimization = true;
        
        // Cache
        private int[] vertexMask;
        private int[] triangleMask;

        // Per-submesh triangle masks for accurate scene highlighting
        private Dictionary<int, List<int>> triangleMaskPerSubmesh = new Dictionary<int, List<int>>();
        
        // Performance optimization
        private bool textureNeedsUpdate = false;
        private HashSet<int> vertexMaskSet = new HashSet<int>();
        private List<int> triangleMaskList = new List<int>();
        
        // Scene display
        private Transform targetTransform;
        
        // Dynamic mesh support for highlighting
        private Mesh dynamicMesh;
	    private bool useDynamicMeshForHighlight = false;
        #endregion
        
        // Properties
        public List<UVIslandAnalyzer.UVIsland> UVIslands => uvIslands;
        public List<UVIslandAnalyzer.UVIsland> AllUVIslands => allUVIslands;
        public List<int> SelectedSubmeshIndices => selectedSubmeshIndices;
        public int CurrentPreviewSubmesh => currentPreviewSubmesh;

        // Get selected island IDs for current preview submesh only
        public List<int> SelectedIslandIDs
        {
            get
            {
                if (selectedIslandsPerSubmesh.TryGetValue(currentPreviewSubmesh, out var islandSet))
                    return new List<int>(islandSet);
                return new List<int>();
            }
        }

        // Get all selected island IDs across all submeshes (for compatibility)
        public List<int> AllSelectedIslandIDs
        {
            get
            {
                var allIDs = new List<int>();
                foreach (var kvp in selectedIslandsPerSubmesh)
                {
                    allIDs.AddRange(kvp.Value);
                }
                return allIDs;
            }
        }

        public Texture2D UvMapTexture => uvMapTexture;
        public Mesh TargetMesh => targetMesh;
        public bool HasSelectedIslands => selectedIslandsPerSubmesh.Values.Any(set => set.Count > 0);
        public int[] TriangleMask => triangleMask;
        public Transform TargetTransform { get => targetTransform; set => targetTransform = value; }
        public int[] VertexMask => vertexMask;
        public Mesh DynamicMesh { get => dynamicMesh; set { dynamicMesh = value; useDynamicMeshForHighlight = value != null; } }
        public int SubmeshCount => targetMesh?.subMeshCount ?? 0;
        
        // Display properties
        public bool UseAdaptiveVertexSize { get => useAdaptiveVertexSize; set => useAdaptiveVertexSize = value; }
        public float ManualVertexSphereSize { get => manualVertexSphereSize; set => manualVertexSphereSize = value; }
        public float AdaptiveSizeMultiplier { get => adaptiveSizeMultiplier; set => adaptiveSizeMultiplier = value; }
        public float AdaptiveVertexSphereSize => useAdaptiveVertexSize ? adaptiveVertexSphereSize : manualVertexSphereSize;
        public int MaxDisplayVertices => maxDisplayVertices;
        public bool EnablePerformanceOptimization => enablePerformanceOptimization;
        
        // Preview properties
        public bool AutoUpdatePreview { get => autoUpdatePreview; set => autoUpdatePreview = value; }
        public float UvMapZoom { get => uvMapZoom; set => uvMapZoom = Mathf.Clamp(value, MIN_ZOOM, MAX_ZOOM); }
        public Vector2 UvMapPanOffset => uvMapPanOffset;
        public bool EnableMagnifyingGlass { get => enableMagnifyingGlass; set => enableMagnifyingGlass = value; }
        public float MagnifyingGlassZoom { get => magnifyingGlassZoom; set => magnifyingGlassZoom = value; }
        public float MagnifyingGlassSize { get => magnifyingGlassSize; set => magnifyingGlassSize = value; }
        
        // Selection properties
        public bool EnableRangeSelection { get => enableRangeSelection; set => enableRangeSelection = value; }
        public bool IsRangeSelecting => isRangeSelecting;
        
        /// <summary>
        /// Creates a new UVIslandSelector with the specified mesh
        /// 指定されたメッシュで新しいUVIslandSelectorを作成
        /// </summary>
        /// <param name="mesh">The mesh to analyze for UV islands</param>
        public UVIslandSelector(Mesh mesh)
        {
            SetMesh(mesh);
        }
        
        /// <summary>
        /// Sets the target mesh and updates UV island data
        /// ターゲットメッシュを設定し、UVアイランドデータを更新
        /// </summary>
        /// <param name="mesh">The mesh to analyze, or null to clear current mesh</param>
        public void SetMesh(Mesh mesh)
        {
            targetMesh = mesh;
            if (mesh != null)
            {
                CalculateAdaptiveVertexSphereSize();
                UpdateMeshData();
            }
        }
        
        public void UpdateMeshData()
        {
            if (targetMesh == null) return;

            // Analyze all submeshes
            allUVIslands = UVIslandAnalyzer.AnalyzeUVIslands(targetMesh);

            // Initialize current preview submesh if not set
            if (selectedSubmeshIndices != null && selectedSubmeshIndices.Count > 0 &&
                !selectedSubmeshIndices.Contains(currentPreviewSubmesh))
            {
                currentPreviewSubmesh = selectedSubmeshIndices[0];
            }

            // Filter islands for current preview submesh only (not all selected submeshes)
            FilterIslandsByCurrentPreviewSubmesh();

            UpdateMasks();

            // Always generate texture when mesh data is updated to ensure immediate visibility
            GenerateUVMapTexture();
        }

        private void FilterIslandsBySelectedSubmeshes()
        {
            if (selectedSubmeshIndices == null || selectedSubmeshIndices.Count == 0)
            {
                uvIslands = new List<UVIslandAnalyzer.UVIsland>(allUVIslands);
                return;
            }

            uvIslands = allUVIslands.Where(island => selectedSubmeshIndices.Contains(island.submeshIndex)).ToList();
        }

        private void FilterIslandsByCurrentPreviewSubmesh()
        {
            if (currentPreviewSubmesh < 0 || targetMesh == null || currentPreviewSubmesh >= targetMesh.subMeshCount)
            {
                uvIslands = new List<UVIslandAnalyzer.UVIsland>(allUVIslands);
                return;
            }

            uvIslands = allUVIslands.Where(island => island.submeshIndex == currentPreviewSubmesh).ToList();
        }
        
        private void CalculateAdaptiveVertexSphereSize()
        {
            if (targetMesh == null) return;
            
            var bounds = targetMesh.bounds;
            var maxBoundsSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            adaptiveVertexSphereSize = Mathf.Clamp(maxBoundsSize * adaptiveSizeMultiplier, MIN_ADAPTIVE_VERTEX_SIZE, MAX_ADAPTIVE_VERTEX_SIZE);
        }
        
        /// <summary>
        /// Toggles the selection state of a UV island in current preview submesh
        /// 現在のプレビューサブメッシュ内のUVアイランドの選択状態を切り替え
        /// </summary>
        /// <param name="islandID">The ID of the island to toggle</param>
        public void ToggleIslandSelection(int islandID)
        {
            // Ensure the submesh entry exists
            if (!selectedIslandsPerSubmesh.ContainsKey(currentPreviewSubmesh))
            {
                selectedIslandsPerSubmesh[currentPreviewSubmesh] = new HashSet<int>();
            }

            var islandSet = selectedIslandsPerSubmesh[currentPreviewSubmesh];
            bool wasSelected = islandSet.Contains(islandID);

            if (wasSelected)
            {
                islandSet.Remove(islandID);
                RemoveIslandFromMasks(islandID, currentPreviewSubmesh);
            }
            else
            {
                islandSet.Add(islandID);
                AddIslandToMasks(islandID, currentPreviewSubmesh);
            }

            // Mark texture as dirty instead of regenerating immediately
            if (autoUpdatePreview)
            {
                MarkTextureForUpdate();
            }
        }
        
        public void ClearSelection()
        {
            selectedIslandsPerSubmesh.Clear();
            UpdateMasks();

            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }

        /// <summary>
        /// Set selected islands for current preview submesh
        /// 現在のプレビューサブメッシュの選択アイランドを設定
        /// </summary>
        public void SetSelectedIslands(List<int> islandIDs)
        {
            if (!selectedIslandsPerSubmesh.ContainsKey(currentPreviewSubmesh))
            {
                selectedIslandsPerSubmesh[currentPreviewSubmesh] = new HashSet<int>();
            }

            selectedIslandsPerSubmesh[currentPreviewSubmesh] = new HashSet<int>(islandIDs ?? new List<int>());
            UpdateMasks();

            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }

        /// <summary>
        /// Set all selected islands across all submeshes (for backward compatibility)
        /// 全サブメッシュの選択アイランドを設定（後方互換性用）
        /// </summary>
        public void SetAllSelectedIslands(Dictionary<int, HashSet<int>> selections)
        {
            selectedIslandsPerSubmesh = selections ?? new Dictionary<int, HashSet<int>>();
            UpdateMasks();

            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }

        public void SetSelectedSubmeshes(List<int> submeshIndices)
        {
            selectedSubmeshIndices = submeshIndices ?? new List<int> { 0 };

            // Update current preview submesh if it's no longer in selected submeshes
            if (!selectedSubmeshIndices.Contains(currentPreviewSubmesh))
            {
                currentPreviewSubmesh = selectedSubmeshIndices[0];
            }

            // Filter by current preview submesh only (not all selected)
            FilterIslandsByCurrentPreviewSubmesh();
            UpdateMasks();

            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }

        public void SetPreviewSubmesh(int submeshIndex)
        {
            if (submeshIndex < 0 || targetMesh == null || submeshIndex >= targetMesh.subMeshCount)
                return;

            currentPreviewSubmesh = submeshIndex;
            FilterIslandsByCurrentPreviewSubmesh();

            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }

        public void NextPreviewSubmesh()
        {
            if (selectedSubmeshIndices == null || selectedSubmeshIndices.Count == 0)
                return;

            int currentIndex = selectedSubmeshIndices.IndexOf(currentPreviewSubmesh);
            int nextIndex = (currentIndex + 1) % selectedSubmeshIndices.Count;
            SetPreviewSubmesh(selectedSubmeshIndices[nextIndex]);
        }

        public void PreviousPreviewSubmesh()
        {
            if (selectedSubmeshIndices == null || selectedSubmeshIndices.Count == 0)
                return;

            int currentIndex = selectedSubmeshIndices.IndexOf(currentPreviewSubmesh);
            int prevIndex = (currentIndex - 1 + selectedSubmeshIndices.Count) % selectedSubmeshIndices.Count;
            SetPreviewSubmesh(selectedSubmeshIndices[prevIndex]);
        }
        
        // Range selection methods
        public void StartRangeSelection(Vector2 startPoint)
        {
            if (!enableRangeSelection) return;
            
            isRangeSelecting = true;
            rangeSelectionStart = startPoint;
            rangeSelectionEnd = startPoint;
        }
        
        public void UpdateRangeSelection(Vector2 currentPoint)
        {
            if (!isRangeSelecting) return;
            rangeSelectionEnd = currentPoint;
        }
        
        public void FinishRangeSelection(bool addToSelection = false, bool removeFromSelection = false)
        {
            if (!isRangeSelecting) return;

            var selectionRect = new Rect(
                Mathf.Min(rangeSelectionStart.x, rangeSelectionEnd.x),
                Mathf.Min(rangeSelectionStart.y, rangeSelectionEnd.y),
                Mathf.Abs(rangeSelectionEnd.x - rangeSelectionStart.x),
                Mathf.Abs(rangeSelectionEnd.y - rangeSelectionStart.y)
            );

            var islandsInRange = GetIslandsInRect(selectionRect);

            // Ensure the submesh entry exists
            if (!selectedIslandsPerSubmesh.ContainsKey(currentPreviewSubmesh))
            {
                selectedIslandsPerSubmesh[currentPreviewSubmesh] = new HashSet<int>();
            }

            var islandSet = selectedIslandsPerSubmesh[currentPreviewSubmesh];

            if (removeFromSelection)
            {
                foreach (var islandID in islandsInRange)
                {
                    islandSet.Remove(islandID);
                }
            }
            else if (addToSelection)
            {
                foreach (var islandID in islandsInRange)
                {
                    islandSet.Add(islandID);
                }
            }
            else
            {
                islandSet.Clear();
                foreach (var islandID in islandsInRange)
                {
                    islandSet.Add(islandID);
                }
            }

            isRangeSelecting = false;
            UpdateMasks();

            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }
        
        public Rect GetCurrentSelectionRect()
        {
            if (!isRangeSelecting) return new Rect();
            
            return new Rect(
                Mathf.Min(rangeSelectionStart.x, rangeSelectionEnd.x),
                Mathf.Min(rangeSelectionStart.y, rangeSelectionEnd.y),
                Mathf.Abs(rangeSelectionEnd.x - rangeSelectionStart.x),
                Mathf.Abs(rangeSelectionEnd.y - rangeSelectionStart.y)
            );
        }
        
        private List<int> GetIslandsInRect(Rect rect)
        {
            var islandsInRect = new List<int>();
            
            foreach (var island in uvIslands)
            {
                var bounds = island.uvBounds;
                var islandRect = new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
                
                if (rect.Overlaps(islandRect))
                {
                    islandsInRect.Add(island.islandID);
                }
            }
            
            return islandsInRect;
        }
        
        // Coordinate transformation
        public Matrix4x4 CalculateUVTransformMatrix()
        {
            var centerOffset = Matrix4x4.Translate(new Vector3(CENTER_OFFSET, CENTER_OFFSET, 0f));
            var scaleMatrix = Matrix4x4.Scale(new Vector3(uvMapZoom, uvMapZoom, 1f));
            var panMatrix = Matrix4x4.Translate(new Vector3(uvMapPanOffset.x, uvMapPanOffset.y, 0f));
            var recenterMatrix = Matrix4x4.Translate(new Vector3(RECENTER_OFFSET, RECENTER_OFFSET, 0f));
            
            return recenterMatrix * panMatrix * scaleMatrix * centerOffset;
        }
        
        public void SetZoomLevel(float zoom)
        {
            uvMapZoom = Mathf.Clamp(zoom, MIN_ZOOM, MAX_ZOOM);
        }
        
        public void SetPanOffset(Vector2 offset)
        {
            // Constrain pan offset based on zoom level for better UX
            float maxOffset = (uvMapZoom - MIN_ZOOM) * RECENTER_OFFSET;
            uvMapPanOffset = new Vector2(
                Mathf.Clamp(offset.x, -maxOffset, maxOffset),
                Mathf.Clamp(offset.y, -maxOffset, maxOffset)
            );
        }
        
        public void ResetViewTransform()
        {
            uvMapZoom = MIN_ZOOM;
            uvMapPanOffset = Vector2.zero;
        }
        
        public void ZoomAtPoint(Vector2 zoomPoint, float zoomDelta)
        {
            float oldZoom = uvMapZoom;
            float newZoom = Mathf.Clamp(uvMapZoom + zoomDelta, MIN_ZOOM, MAX_ZOOM);
            
            if (Mathf.Approximately(oldZoom, newZoom)) return;
            
            // Adjust pan offset to zoom around the specified point
            Vector2 centerPoint = zoomPoint - new Vector2(RECENTER_OFFSET, RECENTER_OFFSET);
            float zoomRatio = newZoom / oldZoom;
            
            Vector2 oldOffset = uvMapPanOffset;
            Vector2 newOffset = (oldOffset - centerPoint) * zoomRatio + centerPoint;
            
            uvMapZoom = newZoom;
            SetPanOffset(newOffset);
        }
        
        public int GetIslandAtUVCoordinate(Vector2 uvCoord)
        {
            // Phase 1: Try exact triangle hit detection for all islands
            foreach (var island in uvIslands)
            {
                // Use optimized point-in-island test with submesh info
                bool inIsland = UVIslandAnalyzer.IsPointInUVIsland(uvCoord, island, targetMesh);
                if (inIsland)
                {
                    return island.islandID;
                }
            }
            
            // Phase 2: If no exact hit, try closest island within bounds (more generous approach)
            const float BOUNDS_TOLERANCE = 0.005f; // Small tolerance for near-miss clicks
            int closestIsland = -1;
            float closestDistance = float.MaxValue;
            
            foreach (var island in uvIslands)
            {
                // Check if point is near the island bounds
                var bounds = island.uvBounds;
                var expandedBounds = new Bounds(bounds.center, bounds.size + Vector3.one * BOUNDS_TOLERANCE * 2);
                
                if (expandedBounds.Contains(new Vector3(uvCoord.x, uvCoord.y, 0)))
                {
                    // Calculate distance to closest edge of the island bounds
                    float distanceToBounds = GetDistanceToRectangleBounds(uvCoord, bounds);
                    
                    if (distanceToBounds < closestDistance)
                    {
                        closestDistance = distanceToBounds;
                        closestIsland = island.islandID;
                    }
                }
            }
            
            return closestDistance <= BOUNDS_TOLERANCE ? closestIsland : -1;
        }
        
        /// <summary>
        /// Calculate distance from point to rectangle bounds (for improved fallback selection)
        /// </summary>
        private float GetDistanceToRectangleBounds(Vector2 point, Bounds bounds)
        {
            float dx = Mathf.Max(0, Mathf.Max(bounds.min.x - point.x, point.x - bounds.max.x));
            float dy = Mathf.Max(0, Mathf.Max(bounds.min.y - point.y, point.y - bounds.max.y));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
        
        private void UpdateMasks()
        {
            if (allUVIslands.Count == 0)
            {
                vertexMask = new int[0];
                triangleMask = new int[0];
                triangleMaskPerSubmesh.Clear();
                return;
            }

            var maskedVertices = new HashSet<int>();
            var maskedTriangles = new List<int>();
            triangleMaskPerSubmesh.Clear();

            // Iterate through all selected islands across all submeshes
            foreach (var kvp in selectedIslandsPerSubmesh)
            {
                int submeshIndex = kvp.Key;
                var islandIDs = kvp.Value;

                // Initialize submesh triangle list
                if (!triangleMaskPerSubmesh.ContainsKey(submeshIndex))
                {
                    triangleMaskPerSubmesh[submeshIndex] = new List<int>();
                }

                foreach (var islandID in islandIDs)
                {
                    // Find island from all islands by matching both submesh and island ID
                    var island = allUVIslands.FirstOrDefault(i => i.submeshIndex == submeshIndex && i.islandID == islandID);
                    if (island != null)
                    {
                        foreach (var vertIndex in island.vertexIndices)
                        {
                            maskedVertices.Add(vertIndex);
                        }
                        maskedTriangles.AddRange(island.triangleIndices);

                        // Store triangles per submesh for accurate scene highlighting
                        triangleMaskPerSubmesh[submeshIndex].AddRange(island.triangleIndices);
                    }
                }
            }

            vertexMaskSet = maskedVertices;
            triangleMaskList = maskedTriangles;

            vertexMask = vertexMaskSet.ToArray();
            triangleMask = triangleMaskList.ToArray();
        }
        
        // Optimized incremental mask updates
        private void AddIslandToMasks(int islandID, int submeshIndex)
        {
            // Find island from current preview submesh only
            var island = uvIslands.FirstOrDefault(i => i.islandID == islandID && i.submeshIndex == submeshIndex);
            if (island != null)
            {
                foreach (var vertIndex in island.vertexIndices)
                {
                    vertexMaskSet.Add(vertIndex);
                }
                triangleMaskList.AddRange(island.triangleIndices);

                // Update per-submesh triangle mask for scene highlighting
                if (!triangleMaskPerSubmesh.ContainsKey(submeshIndex))
                {
                    triangleMaskPerSubmesh[submeshIndex] = new List<int>();
                }
                triangleMaskPerSubmesh[submeshIndex].AddRange(island.triangleIndices);

                // Update arrays
                vertexMask = vertexMaskSet.ToArray();
                triangleMask = triangleMaskList.ToArray();
            }
        }

        private void RemoveIslandFromMasks(int islandID, int submeshIndex)
        {
            // Find island from current preview submesh only
            var island = uvIslands.FirstOrDefault(i => i.islandID == islandID && i.submeshIndex == submeshIndex);
            if (island != null)
            {
                foreach (var vertIndex in island.vertexIndices)
                {
                    // Only remove if no other selected islands contain this vertex
                    bool vertexUsedElsewhere = false;

                    // Check all selected islands across all submeshes
                    foreach (var kvp in selectedIslandsPerSubmesh)
                    {
                        foreach (var otherIslandID in kvp.Value)
                        {
                            if (kvp.Key == submeshIndex && otherIslandID == islandID) continue;

                            var otherIsland = allUVIslands.FirstOrDefault(i => i.submeshIndex == kvp.Key && i.islandID == otherIslandID);
                            if (otherIsland != null && otherIsland.vertexIndices.Contains(vertIndex))
                            {
                                vertexUsedElsewhere = true;
                                break;
                            }
                        }
                        if (vertexUsedElsewhere) break;
                    }

                    if (!vertexUsedElsewhere)
                    {
                        vertexMaskSet.Remove(vertIndex);
                    }
                }

                // Remove triangles from global list
                foreach (var triIndex in island.triangleIndices)
                {
                    triangleMaskList.Remove(triIndex);
                }

                // Remove triangles from per-submesh list for scene highlighting
                if (triangleMaskPerSubmesh.ContainsKey(submeshIndex))
                {
                    foreach (var triIndex in island.triangleIndices)
                    {
                        triangleMaskPerSubmesh[submeshIndex].Remove(triIndex);
                    }

                    // Clean up empty submesh entries
                    if (triangleMaskPerSubmesh[submeshIndex].Count == 0)
                    {
                        triangleMaskPerSubmesh.Remove(submeshIndex);
                    }
                }

                // Update arrays
                vertexMask = vertexMaskSet.ToArray();
                triangleMask = triangleMaskList.ToArray();
            }
        }
        
        // Deferred texture update
        private void MarkTextureForUpdate()
        {
            textureNeedsUpdate = true;
        }
        
        public void UpdateTextureIfNeeded()
        {
            if (textureNeedsUpdate)
            {
                GenerateUVMapTexture();
                textureNeedsUpdate = false;
            }
        }
        
        // Simplified texture generation for preview only
        public Texture2D GenerateUVMapTexture(int width = DEFAULT_TEXTURE_SIZE, int height = DEFAULT_TEXTURE_SIZE)
        {
            // Properly dispose of old texture to prevent memory leaks
            if (uvMapTexture != null)
            {
                Object.DestroyImmediate(uvMapTexture);
                uvMapTexture = null;
            }
            
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            
            // Fill background
            var backgroundColor = BACKGROUND_COLOR;
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = backgroundColor;
            }
            
            var transformMatrix = CalculateUVTransformMatrix();

            // Draw simple UV grid
            UVTextureRenderer.DrawSimpleGrid(pixels, width, height, transformMatrix);

            // Draw UV islands with current preview submesh selections
            UVTextureRenderer.DrawUVIslands(pixels, width, height, transformMatrix, uvIslands, SelectedIslandIDs, targetMesh);

            texture.SetPixels(pixels);
            texture.Apply();

            uvMapTexture = texture;
            return texture;
        }
        
        /// <summary>
        /// Generate UV texture for low-resolution cache, ignoring zoom and pan state
        /// 低解像度キャッシュ用のUVテクスチャ生成（ズーム・パン状態を無視）
        /// </summary>
        public Texture2D GenerateLowResUVMapTexture(int width = DEFAULT_TEXTURE_SIZE, int height = DEFAULT_TEXTURE_SIZE)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            
            // Fill background
            var backgroundColor = BACKGROUND_COLOR;
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = backgroundColor;
            }
            
            // Use identity transform matrix to show full UV map without zoom/pan
            var transformMatrix = CalculateFullViewTransformMatrix();

            // Draw simple UV grid
            UVTextureRenderer.DrawSimpleGrid(pixels, width, height, transformMatrix);

            // Draw UV islands with current preview submesh selections
            UVTextureRenderer.DrawUVIslands(pixels, width, height, transformMatrix, uvIslands, SelectedIslandIDs, targetMesh);

            texture.SetPixels(pixels);
            texture.Apply();

            return texture;
        }
        
        /// <summary>
        /// Calculate transform matrix for full view without zoom/pan (for cache generation)
        /// ズーム/パンなしの全体表示用変換マトリクス計算（キャッシュ生成用）
        /// </summary>
        public Matrix4x4 CalculateFullViewTransformMatrix()
        {
            var centerOffset = Matrix4x4.Translate(new Vector3(CENTER_OFFSET, CENTER_OFFSET, 0f));
            var scaleMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, 1f)); // Always use 1:1 scale
            var panMatrix = Matrix4x4.Translate(new Vector3(0f, 0f, 0f)); // No pan offset
            var recenterMatrix = Matrix4x4.Translate(new Vector3(RECENTER_OFFSET, RECENTER_OFFSET, 0f));
            
            return recenterMatrix * panMatrix * scaleMatrix * centerOffset;
        }
        
        // Magnifying glass texture generation
        public Texture2D GenerateMagnifyingGlassTexture(Vector2 centerUV, int size = DEFAULT_MAGNIFYING_GLASS_SIZE_INT)
        {
            if (!enableMagnifyingGlass) return null;
            
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            
            // Fill background
            var backgroundColor = MAGNIFYING_BACKGROUND_COLOR;
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = backgroundColor;
            }
            
            // Calculate magnifying area
            float radius = RECENTER_OFFSET / magnifyingGlassZoom;
            UVTextureRenderer.DrawMagnifyingContent(pixels, size, size, centerUV, radius, uvIslands, SelectedIslandIDs, targetMesh);

            texture.SetPixels(pixels);
            texture.Apply();

            return texture;
        }
        
        
        /// <summary>
        /// Draw selected faces in scene view using GL with polygon offset for perfect Z-fighting prevention
        /// GLとポリゴンオフセットを使用してシーンビューで選択された面を描画（完全なZファイティング防止）
        /// </summary>
        public void DrawSelectedFacesInScene()
        {
            if (triangleMaskPerSubmesh == null || triangleMaskPerSubmesh.Count == 0 || targetTransform == null) return;
            if (!HasSelectedIslands) return;

            // Use dynamic mesh for highlighting if available, otherwise fallback to original mesh
            Mesh meshForHighlight = useDynamicMeshForHighlight && dynamicMesh != null ? dynamicMesh : targetMesh;
            if (meshForHighlight == null) return;

            var vertices = new List<Vector3>();
            meshForHighlight.GetVertices(vertices);

            // Draw faces only for submeshes that have selected islands
            foreach (var kvp in triangleMaskPerSubmesh)
            {
                int submeshIndex = kvp.Key;
                var triangleMaskForSubmesh = kvp.Value;

                if (triangleMaskForSubmesh.Count == 0)
                    continue;

                if (submeshIndex < 0 || submeshIndex >= meshForHighlight.subMeshCount)
                    continue;

                var triangles = meshForHighlight.GetTriangles(submeshIndex);

                // Validate that triangle mask is compatible with current mesh
                if (triangles.Length == 0)
                    continue;

                DrawSelectedFacesWithGL(vertices, triangles, triangleMaskForSubmesh);
            }
        }

        /// <summary>
        /// Triangle rendering data (precomputed for all passes)
        /// 三角形レンダリングデータ（全パス共通で事前計算）
        /// </summary>
        private struct TriangleRenderData
        {
            public Vector3 v0Local, v1Local, v2Local;  // Local coordinates for GL
            public Vector3 v0World, v1World, v2World;  // World coordinates for Handles
        }

        /// <summary>
        /// Draw selected faces using GL immediate mode with hardware polygon offset
        /// ハードウェアポリゴンオフセットを使用したGL即時モードで選択された面を描画
        /// </summary>
        private void DrawSelectedFacesWithGL(List<Vector3> vertices, int[] triangles, List<int> triangleMaskForSubmesh)
        {
            // Create material for GL rendering if needed
            if (!CreateGLMaterial())
                return;

            var sceneCamera = SceneView.currentDrawingSceneView?.camera;
            if (sceneCamera == null) return;

            // Precompute all triangle data in a single pass
            var triangleDataList = new List<TriangleRenderData>(triangleMaskForSubmesh.Count);

            for (int maskIndex = 0; maskIndex < triangleMaskForSubmesh.Count; maskIndex++)
            {
                int triangleIndex = triangleMaskForSubmesh[maskIndex];
                int baseIndex = triangleIndex * 3;

                if (baseIndex + 2 < triangles.Length)
                {
                    var idx0 = triangles[baseIndex];
                    var idx1 = triangles[baseIndex + 1];
                    var idx2 = triangles[baseIndex + 2];

                    var data = new TriangleRenderData
                    {
                        v0Local = vertices[idx0],
                        v1Local = vertices[idx1],
                        v2Local = vertices[idx2],
                        v0World = targetTransform.TransformPoint(vertices[idx0]),
                        v1World = targetTransform.TransformPoint(vertices[idx1]),
                        v2World = targetTransform.TransformPoint(vertices[idx2])
                    };

                    triangleDataList.Add(data);
                }
            }

            var originalZTest = Handles.zTest;

            // Method 1: Background pass - always visible (no depth test)
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = new Color(0.2f, 0.7f, 1f, 0.25f);

            foreach (var tri in triangleDataList)
            {
                Handles.DrawAAConvexPolygon(tri.v0World, tri.v1World, tri.v2World);
            }

            // Method 2: Foreground pass with hardware polygon offset (GL with custom shader)
            GL.PushMatrix();
            GL.MultMatrix(targetTransform.localToWorldMatrix);

            glMaterial.SetColor("_Color", new Color(1f, 0.5f, 0f, 0.7f));
            glMaterial.SetPass(0);

            GL.Begin(GL.TRIANGLES);
            GL.Color(new Color(1f, 1f, 1f, 1f));  // GL.Color is multiplied with material color

            foreach (var tri in triangleDataList)
            {
                GL.Vertex(tri.v0Local);
                GL.Vertex(tri.v1Local);
                GL.Vertex(tri.v2Local);
            }

            GL.End();
	        GL.PopMatrix();

            // Method 3: Wireframe outline
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.color = new Color(1f, 0.3f, 0f, 1f);

            foreach (var tri in triangleDataList)
            {
                Handles.DrawLine(tri.v0World, tri.v1World);
                Handles.DrawLine(tri.v1World, tri.v2World);
                Handles.DrawLine(tri.v2World, tri.v0World);
            }

            Handles.zTest = originalZTest;
        }

        // GL material for rendering
        private static Material glMaterial;

        /// <summary>
        /// Create or get GL material for immediate mode rendering with polygon offset support
        /// ポリゴンオフセットサポート付きGL即時モードレンダリング用のマテリアルを作成または取得
        /// </summary>
        private bool CreateGLMaterial()
        {
            if (glMaterial != null)
                return true;

            // Use custom shader with polygon offset support
            var shader = Shader.Find("Hidden/UVIslandHighlight");
            if (shader == null)
            {
                // Fallback to built-in shader
                shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                {
                    Debug.LogError("Could not find shader for GL rendering");
                    return false;
                }
            }

            glMaterial = new Material(shader);
            glMaterial.hideFlags = HideFlags.HideAndDontSave;

            return true;
        }
        
        /// <summary>
        /// Clean up resources to prevent memory leaks
        /// </summary>
        public void Dispose()
        {
            if (uvMapTexture != null)
            {
                Object.DestroyImmediate(uvMapTexture);
                uvMapTexture = null;
            }
        }
    }
}