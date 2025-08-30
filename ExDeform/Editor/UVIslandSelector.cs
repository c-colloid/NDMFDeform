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
        // Core data
        private Mesh targetMesh;
        private List<UVIslandAnalyzer.UVIsland> uvIslands = new List<UVIslandAnalyzer.UVIsland>();
        private List<int> selectedIslandIDs = new List<int>();
        private Texture2D uvMapTexture;
        
        // Display settings
        private bool useAdaptiveVertexSize = true;
        private float manualVertexSphereSize = 0.01f;
        private float adaptiveSizeMultiplier = 0.007f;
        private float adaptiveVertexSphereSize = 0.01f;
        
        // UV Map preview settings
        private bool autoUpdatePreview = true;
        private float uvMapZoom = 1f;
        private Vector2 uvMapPanOffset = Vector2.zero;
        private bool enableMagnifyingGlass = true;
        private float magnifyingGlassZoom = 8f;
        private float magnifyingGlassSize = 100f;
        
        // Selection settings  
        private bool enableRangeSelection = true;
        private bool isRangeSelecting = false;
        private Vector2 rangeSelectionStart = Vector2.zero;
        private Vector2 rangeSelectionEnd = Vector2.zero;
        
        // Performance settings
        private int maxDisplayVertices = 1000;
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
        
        // Properties
        public List<UVIslandAnalyzer.UVIsland> UVIslands => uvIslands;
        public List<int> SelectedIslandIDs => selectedIslandIDs;
        public Texture2D UvMapTexture => uvMapTexture;
        public Mesh TargetMesh => targetMesh;
        public bool HasSelectedIslands => selectedIslandIDs.Count > 0;
        public int[] TriangleMask => triangleMask;
        public Transform TargetTransform { get => targetTransform; set => targetTransform = value; }
        public int[] VertexMask => vertexMask;
        
        // Display properties
        public bool UseAdaptiveVertexSize { get => useAdaptiveVertexSize; set => useAdaptiveVertexSize = value; }
        public float ManualVertexSphereSize { get => manualVertexSphereSize; set => manualVertexSphereSize = value; }
        public float AdaptiveSizeMultiplier { get => adaptiveSizeMultiplier; set => adaptiveSizeMultiplier = value; }
        public float AdaptiveVertexSphereSize => useAdaptiveVertexSize ? adaptiveVertexSphereSize : manualVertexSphereSize;
        public int MaxDisplayVertices => maxDisplayVertices;
        public bool EnablePerformanceOptimization => enablePerformanceOptimization;
        
        // Preview properties
        public bool AutoUpdatePreview { get => autoUpdatePreview; set => autoUpdatePreview = value; }
        public float UvMapZoom { get => uvMapZoom; set => uvMapZoom = Mathf.Clamp(value, 1f, 8f); }
        public Vector2 UvMapPanOffset => uvMapPanOffset;
        public bool EnableMagnifyingGlass { get => enableMagnifyingGlass; set => enableMagnifyingGlass = value; }
        public float MagnifyingGlassZoom { get => magnifyingGlassZoom; set => magnifyingGlassZoom = value; }
        public float MagnifyingGlassSize { get => magnifyingGlassSize; set => magnifyingGlassSize = value; }
        
        // Selection properties
        public bool EnableRangeSelection { get => enableRangeSelection; set => enableRangeSelection = value; }
        public bool IsRangeSelecting => isRangeSelecting;
        
        public UVIslandSelector(Mesh mesh)
        {
            SetMesh(mesh);
        }
        
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
            adaptiveVertexSphereSize = Mathf.Clamp(maxBoundsSize * adaptiveSizeMultiplier, 0.001f, 0.05f);
        }
        
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
            var centerOffset = Matrix4x4.Translate(new Vector3(-0.5f, -0.5f, 0f));
            var scaleMatrix = Matrix4x4.Scale(new Vector3(uvMapZoom, uvMapZoom, 1f));
            var panMatrix = Matrix4x4.Translate(new Vector3(uvMapPanOffset.x, uvMapPanOffset.y, 0f));
            var recenterMatrix = Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0f));
            
            return recenterMatrix * panMatrix * scaleMatrix * centerOffset;
        }
        
        public void SetZoomLevel(float zoom)
        {
            uvMapZoom = Mathf.Clamp(zoom, 1f, 8f);
        }
        
        public void SetPanOffset(Vector2 offset)
        {
            // Constrain pan offset based on zoom level for better UX
            float maxOffset = (uvMapZoom - 1f) * 0.5f;
            uvMapPanOffset = new Vector2(
                Mathf.Clamp(offset.x, -maxOffset, maxOffset),
                Mathf.Clamp(offset.y, -maxOffset, maxOffset)
            );
        }
        
        public void ResetViewTransform()
        {
            uvMapZoom = 1f;
            uvMapPanOffset = Vector2.zero;
        }
        
        public void ZoomAtPoint(Vector2 zoomPoint, float zoomDelta)
        {
            float oldZoom = uvMapZoom;
            float newZoom = Mathf.Clamp(uvMapZoom + zoomDelta, 1f, 8f);
            
            if (Mathf.Approximately(oldZoom, newZoom)) return;
            
            // Adjust pan offset to zoom around the specified point
            Vector2 centerPoint = zoomPoint - new Vector2(0.5f, 0.5f);
            float zoomRatio = newZoom / oldZoom;
            
            Vector2 oldOffset = uvMapPanOffset;
            Vector2 newOffset = (oldOffset - centerPoint) * zoomRatio + centerPoint;
            
            uvMapZoom = newZoom;
            SetPanOffset(newOffset);
        }
        
        public int GetIslandAtUVCoordinate(Vector2 uvCoord)
        {
            UnityEngine.Debug.Log($"[UV Selection] Click: ({uvCoord.x:F3},{uvCoord.y:F3})");
            
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
                        UnityEngine.Debug.Log($"[SUCCESS] Exact match - Island: {island.islandID}");
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
            
            // If no exact match, use closest island within reasonable distance (0.1 units)
            if (closestDistance < 0.1f && closestIsland >= 0)
            {
                UnityEngine.Debug.Log($"[SUCCESS] Closest match - Island: {closestIsland}, Distance: {closestDistance:F3}");
                return closestIsland;
            }
            
            UnityEngine.Debug.Log($"[FAILED] No island found (closest: {closestIsland}, distance: {closestDistance:F3})");
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
        public Texture2D GenerateUVMapTexture(int width = 512, int height = 512)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            
            // Fill background
            var backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1.0f);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = backgroundColor;
            }
            
            var transformMatrix = CalculateUVTransformMatrix();
            
            // Draw simple UV grid
            DrawSimpleGrid(pixels, width, height, transformMatrix);
            
            // Draw UV islands
            DrawUVIslands(pixels, width, height, transformMatrix);
            
            texture.SetPixels(pixels);
            texture.Apply();
            
            uvMapTexture = texture;
            return texture;
        }
        
        // Magnifying glass texture generation
        public Texture2D GenerateMagnifyingGlassTexture(Vector2 centerUV, int size = 100)
        {
            if (!enableMagnifyingGlass) return null;
            
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            
            // Fill background
            var backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1.0f);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = backgroundColor;
            }
            
            // Calculate magnifying area
            float radius = 0.5f / magnifyingGlassZoom;
            DrawMagnifyingContent(pixels, size, size, centerUV, radius);
            
            texture.SetPixels(pixels);
            texture.Apply();
            
            return texture;
        }
        
        // Simplified drawing methods
        private void DrawSimpleGrid(Color[] pixels, int width, int height, Matrix4x4 transform)
        {
            var gridColor = new Color(0.3f, 0.3f, 0.3f, 1.0f);
            
            for (float gridPos = 0f; gridPos <= 1f; gridPos += 0.1f)
            {
                // Vertical lines
                DrawTransformedLine(new Vector2(gridPos, 0f), new Vector2(gridPos, 1f), pixels, width, height, transform, gridColor);
                // Horizontal lines  
                DrawTransformedLine(new Vector2(0f, gridPos), new Vector2(1f, gridPos), pixels, width, height, transform, gridColor);
            }
        }
        
        private void DrawUVIslands(Color[] pixels, int width, int height, Matrix4x4 transform)
        {
            if (targetMesh == null || targetMesh.uv == null || targetMesh.triangles == null) return;
            
            var uvs = targetMesh.uv;
            var triangles = targetMesh.triangles;
            
            foreach (var island in uvIslands)
            {
                var color = selectedIslandIDs.Contains(island.islandID) ? 
                    new Color(island.maskColor.r, island.maskColor.g, island.maskColor.b, 0.7f) :
                    new Color(0.5f, 0.5f, 0.5f, 0.3f);
                
                // Draw island outline using triangle indices
                foreach (int triangleIndex in island.triangleIndices)
                {
                    int baseIndex = triangleIndex * 3;
                    
                    // Safety check for array bounds
                    if (baseIndex + 2 >= triangles.Length) continue;
                    
                    // Get vertex indices from triangles array
                    int vertIndex0 = triangles[baseIndex];
                    int vertIndex1 = triangles[baseIndex + 1];
                    int vertIndex2 = triangles[baseIndex + 2];
                    
                    // Safety check for UV array bounds
                    if (vertIndex0 >= uvs.Length || vertIndex1 >= uvs.Length || vertIndex2 >= uvs.Length) continue;
                    
                    var uv0 = uvs[vertIndex0];
                    var uv1 = uvs[vertIndex1];
                    var uv2 = uvs[vertIndex2];
                    
                    DrawTransformedLine(uv0, uv1, pixels, width, height, transform, color);
                    DrawTransformedLine(uv1, uv2, pixels, width, height, transform, color);
                    DrawTransformedLine(uv2, uv0, pixels, width, height, transform, color);
                }
            }
        }
        
        private void DrawMagnifyingContent(Color[] pixels, int width, int height, Vector2 centerUV, float radius)
        {
            if (targetMesh == null || targetMesh.uv == null || targetMesh.triangles == null) return;
            
            var uvs = targetMesh.uv;
            var triangles = targetMesh.triangles;
            var detailColor = new Color(1f, 1f, 1f, 0.8f);
            
            foreach (var island in uvIslands)
            {
                var color = selectedIslandIDs.Contains(island.islandID) ? 
                    island.maskColor : new Color(0.7f, 0.7f, 0.7f, 0.6f);
                
                foreach (int triangleIndex in island.triangleIndices)
                {
                    int baseIndex = triangleIndex * 3;
                    
                    // Safety check for array bounds
                    if (baseIndex + 2 >= triangles.Length) continue;
                    
                    // Get vertex indices from triangles array
                    int vertIndex0 = triangles[baseIndex];
                    int vertIndex1 = triangles[baseIndex + 1];
                    int vertIndex2 = triangles[baseIndex + 2];
                    
                    // Safety check for UV array bounds
                    if (vertIndex0 >= uvs.Length || vertIndex1 >= uvs.Length || vertIndex2 >= uvs.Length) continue;
                    
                    var uv0 = uvs[vertIndex0];
                    var uv1 = uvs[vertIndex1];
                    var uv2 = uvs[vertIndex2];
                    
                    // Check if triangle is within magnifying area
                    if (IsTriangleInRadius(uv0, uv1, uv2, centerUV, radius))
                    {
                        DrawMagnifyingTriangleOutline(uv0, uv1, uv2, centerUV, radius, pixels, width, height, color);
                    }
                }
            }
        }
        
        private bool IsTriangleInRadius(Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 center, float radius)
        {
            return (Vector2.Distance(uv0, center) <= radius ||
                    Vector2.Distance(uv1, center) <= radius ||
                    Vector2.Distance(uv2, center) <= radius);
        }
        
        private void DrawMagnifyingTriangleOutline(Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 centerUV, float radius, 
            Color[] pixels, int width, int height, Color color)
        {
            DrawMagnifyingLine(uv0, uv1, centerUV, radius, pixels, width, height, color);
            DrawMagnifyingLine(uv1, uv2, centerUV, radius, pixels, width, height, color);
            DrawMagnifyingLine(uv2, uv0, centerUV, radius, pixels, width, height, color);
        }
        
        private void DrawMagnifyingLine(Vector2 start, Vector2 end, Vector2 centerUV, float radius, 
            Color[] pixels, int width, int height, Color color)
        {
            // Transform UV coordinates to magnifying glass coordinates
            var startLocal = (start - centerUV) / radius * 0.5f + Vector2.one * 0.5f;
            var endLocal = (end - centerUV) / radius * 0.5f + Vector2.one * 0.5f;
            
            DrawSimpleLine(startLocal, endLocal, pixels, width, height, color);
        }
        
        private void DrawTransformedLine(Vector2 start, Vector2 end, Color[] pixels, int width, int height, Matrix4x4 transform, Color color)
        {
            var startTransformed = transform.MultiplyPoint3x4(new Vector3(start.x, start.y, 0f));
            var endTransformed = transform.MultiplyPoint3x4(new Vector3(end.x, end.y, 0f));
            
            DrawSimpleLine(new Vector2(startTransformed.x, startTransformed.y), 
                          new Vector2(endTransformed.x, endTransformed.y), pixels, width, height, color);
        }
        
        private void DrawSimpleLine(Vector2 start, Vector2 end, Color[] pixels, int width, int height, Color color)
        {
            int x0 = Mathf.RoundToInt(start.x * width);
            int y0 = Mathf.RoundToInt(start.y * height);
            int x1 = Mathf.RoundToInt(end.x * width);
            int y1 = Mathf.RoundToInt(end.y * height);
            
            // Simple Bresenham line algorithm
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            
            while (true)
            {
                if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                {
                    int index = y0 * width + x0;
                    if (index >= 0 && index < pixels.Length)
                    {
                        pixels[index] = Color.Lerp(pixels[index], color, 0.7f);
                    }
                }
                
                if (x0 == x1 && y0 == y1) break;
                
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }
        
        /// <summary>
        /// Draw selected faces in scene view
        /// 選択された面をシーンビューで描画
        /// </summary>
        public void DrawSelectedFacesInScene()
        {
            if (targetMesh == null || triangleMask == null || targetTransform == null) return;
            if (!HasSelectedIslands) return;
            
            var vertices = targetMesh.vertices;
            var triangles = targetMesh.triangles;
            
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
    }
}