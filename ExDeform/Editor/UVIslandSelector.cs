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
        private List<int> selectedIslandIDs = new List<int>();
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
        public List<int> SelectedIslandIDs => selectedIslandIDs;
        public Texture2D UvMapTexture => uvMapTexture;
        public Mesh TargetMesh => targetMesh;
        public bool HasSelectedIslands => selectedIslandIDs.Count > 0;
        public int[] TriangleMask => triangleMask;
        public Transform TargetTransform { get => targetTransform; set => targetTransform = value; }
        public int[] VertexMask => vertexMask;
        public Mesh DynamicMesh { get => dynamicMesh; set { dynamicMesh = value; useDynamicMeshForHighlight = value != null; } }
        
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
            
            uvIslands = UVIslandAnalyzer.AnalyzeUVIslands(targetMesh);
            UpdateMasks();
            
            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }
        
        private void CalculateAdaptiveVertexSphereSize()
        {
            if (targetMesh == null) return;
            
            var bounds = targetMesh.bounds;
            var maxBoundsSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            adaptiveVertexSphereSize = Mathf.Clamp(maxBoundsSize * adaptiveSizeMultiplier, MIN_ADAPTIVE_VERTEX_SIZE, MAX_ADAPTIVE_VERTEX_SIZE);
        }
        
        /// <summary>
        /// Toggles the selection state of a UV island
        /// UVアイランドの選択状態を切り替え
        /// </summary>
        /// <param name="islandID">The ID of the island to toggle</param>
        public void ToggleIslandSelection(int islandID)
        {
            bool wasSelected = selectedIslandIDs.Contains(islandID);
            if (wasSelected)
            {
                selectedIslandIDs.Remove(islandID);
                RemoveIslandFromMasks(islandID);
            }
            else
            {
                selectedIslandIDs.Add(islandID);
                AddIslandToMasks(islandID);
            }
            
            // Mark texture as dirty instead of regenerating immediately
            if (autoUpdatePreview)
            {
                MarkTextureForUpdate();
            }
        }
        
        public void ClearSelection()
        {
            selectedIslandIDs.Clear();
            UpdateMasks();
            
            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }
        
        public void SetSelectedIslands(List<int> islandIDs)
        {
            selectedIslandIDs = islandIDs ?? new List<int>();
            UpdateMasks();
            
            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
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
            
            if (removeFromSelection)
            {
                foreach (var islandID in islandsInRange)
                {
                    selectedIslandIDs.Remove(islandID);
                }
            }
            else if (addToSelection)
            {
                foreach (var islandID in islandsInRange)
                {
                    if (!selectedIslandIDs.Contains(islandID))
                    {
                        selectedIslandIDs.Add(islandID);
                    }
                }
            }
            else
            {
                selectedIslandIDs.Clear();
                selectedIslandIDs.AddRange(islandsInRange);
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
            
            int closestIsland = -1;
            float closestDistance = float.MaxValue;
            
            // Try exact match first
            foreach (var island in uvIslands)
            {
                if (island.uvBounds.Contains(new Vector3(uvCoord.x, uvCoord.y, 0)))
                {
                    bool inTriangle = UVIslandAnalyzer.IsPointInUVIsland(uvCoord, island, targetMesh.uv, targetMesh.triangles);
                    if (inTriangle)
                    {
                        return island.islandID;
                    }
                }
                
                // Calculate distance to island center for fallback
                var centerDistance = Vector2.Distance(uvCoord, new Vector2(island.uvBounds.center.x, island.uvBounds.center.y));
                if (centerDistance < closestDistance)
                {
                    closestDistance = centerDistance;
                    closestIsland = island.islandID;
                }
            }
            
            // If no exact match, use closest island within reasonable distance
            if (closestDistance < UV_DISTANCE_THRESHOLD && closestIsland >= 0)
            {
                return closestIsland;
            }
            
            return -1;
        }
        
        private void UpdateMasks()
        {
            if (uvIslands.Count == 0)
            {
                vertexMask = new int[0];
                triangleMask = new int[0];
                return;
            }
            
            var maskedVertices = new List<int>();
            var maskedTriangles = new List<int>();
            
            foreach (var islandID in selectedIslandIDs)
            {
                var island = uvIslands.FirstOrDefault(i => i.islandID == islandID);
                if (island != null)
                {
                    maskedVertices.AddRange(island.vertexIndices);
                    maskedTriangles.AddRange(island.triangleIndices);
                }
            }
            
            vertexMaskSet = maskedVertices.ToHashSet();
            triangleMaskList = maskedTriangles;
            
            vertexMask = vertexMaskSet.ToArray();
            triangleMask = triangleMaskList.ToArray();
        }
        
        // Optimized incremental mask updates
        private void AddIslandToMasks(int islandID)
        {
            var island = uvIslands.FirstOrDefault(i => i.islandID == islandID);
            if (island != null)
            {
                foreach (var vertIndex in island.vertexIndices)
                {
                    vertexMaskSet.Add(vertIndex);
                }
                triangleMaskList.AddRange(island.triangleIndices);
                
                // Update arrays
                vertexMask = vertexMaskSet.ToArray();
                triangleMask = triangleMaskList.ToArray();
            }
        }
        
        private void RemoveIslandFromMasks(int islandID)
        {
            var island = uvIslands.FirstOrDefault(i => i.islandID == islandID);
            if (island != null)
            {
                foreach (var vertIndex in island.vertexIndices)
                {
                    // Only remove if no other selected islands contain this vertex
                    bool vertexUsedElsewhere = false;
                    foreach (var otherIslandID in selectedIslandIDs)
                    {
                        if (otherIslandID == islandID) continue;
                        var otherIsland = uvIslands.FirstOrDefault(i => i.islandID == otherIslandID);
                        if (otherIsland != null && otherIsland.vertexIndices.Contains(vertIndex))
                        {
                            vertexUsedElsewhere = true;
                            break;
                        }
                    }
                    if (!vertexUsedElsewhere)
                    {
                        vertexMaskSet.Remove(vertIndex);
                    }
                }
                
                // Remove triangles
                foreach (var triIndex in island.triangleIndices)
                {
                    triangleMaskList.Remove(triIndex);
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
            
            // Draw UV islands
            UVTextureRenderer.DrawUVIslands(pixels, width, height, transformMatrix, uvIslands, selectedIslandIDs, targetMesh);
            
            texture.SetPixels(pixels);
            texture.Apply();
            
            uvMapTexture = texture;
            return texture;
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
            UVTextureRenderer.DrawMagnifyingContent(pixels, size, size, centerUV, radius, uvIslands, selectedIslandIDs, targetMesh);
            
            texture.SetPixels(pixels);
            texture.Apply();
            
            return texture;
        }
        
        
        /// <summary>
        /// Draw selected faces in scene view using dynamic mesh if available
        /// 動的メッシュが利用可能な場合はそれを使用してシーンビューで選択された面を描画
        /// </summary>
        public void DrawSelectedFacesInScene()
        {
            if (triangleMask == null || targetTransform == null) return;
            if (!HasSelectedIslands) return;
            
            // Use dynamic mesh for highlighting if available, otherwise fallback to original mesh
            Mesh meshForHighlight = useDynamicMeshForHighlight && dynamicMesh != null ? dynamicMesh : targetMesh;
            if (meshForHighlight == null) return;
            
            var vertices = meshForHighlight.vertices;
            var triangles = meshForHighlight.triangles;
            
            // Validate that triangle mask is compatible with current mesh
            if (triangles.Length == 0) return;
            
            // Set up handles for drawing
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = new Color(1f, 0.5f, 0f, 0.8f); // Orange with transparency
            
            // triangleMask contains triangle indices, not a boolean mask
            // Draw selected triangles using triangle indices from mask
            for (int maskIndex = 0; maskIndex < triangleMask.Length; maskIndex++)
            {
                int triangleIndex = triangleMask[maskIndex];
                int baseIndex = triangleIndex * 3;
                
                // Safety check for array bounds
                if (baseIndex + 2 < triangles.Length)
                {
                    var v0 = targetTransform.TransformPoint(vertices[triangles[baseIndex]]);
                    var v1 = targetTransform.TransformPoint(vertices[triangles[baseIndex + 1]]);
                    var v2 = targetTransform.TransformPoint(vertices[triangles[baseIndex + 2]]);
                    
                    // Draw triangle face
                    Handles.DrawAAConvexPolygon(v0, v1, v2);
                }
            }
            
            // Draw wireframe
            Handles.color = new Color(1f, 0.3f, 0f, 1f); // Solid orange for wireframe
            for (int maskIndex = 0; maskIndex < triangleMask.Length; maskIndex++)
            {
                int triangleIndex = triangleMask[maskIndex];
                int baseIndex = triangleIndex * 3;
                
                // Safety check for array bounds
                if (baseIndex + 2 < triangles.Length)
                {
                    var v0 = targetTransform.TransformPoint(vertices[triangles[baseIndex]]);
                    var v1 = targetTransform.TransformPoint(vertices[triangles[baseIndex + 1]]);
                    var v2 = targetTransform.TransformPoint(vertices[triangles[baseIndex + 2]]);
                    
                    Handles.DrawLine(v0, v1);
                    Handles.DrawLine(v1, v2);
                    Handles.DrawLine(v2, v0);
                }
            }
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