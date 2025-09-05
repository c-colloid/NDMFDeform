using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace Deform.Masking
{
    /// <summary>
    /// UV Island analysis utility
    /// UVアイランド解析ユーティリティ
    /// </summary>
    public static class UVIslandAnalyzer
    {
        /// <summary>
        /// UV tolerance for connecting islands. Increased to allow better island merging.
        /// UVアイランドの接続許容範囲。アイランドの結合を改善するため増加。
        /// </summary>
        public static float UVTolerance = 0.05f; // Further increased from 0.01f to solve remaining splitting issues
        
        /// <summary>
        /// Triangle degenerate tolerance. Relaxed to include more triangles in analysis.
        /// 三角形の縮退判定許容値。解析により多くの三角形を含むため緩和。
        /// </summary>
        public static float TriangleTolerance = 1e-5f;
        
        /// <summary>
        /// Use advanced topology-aware algorithm. Set to false for legacy compatibility.
        /// 高度なトポロジー認識アルゴリズムを使用。レガシー互換性のためにfalseに設定。
        /// </summary>
        public static bool UseAdvancedAlgorithm = true;
        
        /// <summary>
        /// Enable debug logging for UV island analysis troubleshooting.
        /// UVアイランド解析のトラブルシューティング用デバッグログを有効化。
        /// </summary>
        public static bool EnableDebugLogging = false;
        /// <summary>
        /// UV Island data structure
        /// UVアイランドデータ構造
        /// </summary>
        [System.Serializable]
        public class UVIsland
        {
            public int islandID;
            public List<int> vertexIndices = new List<int>();
            public List<int> triangleIndices = new List<int>();
            public List<Vector2> uvCoordinates = new List<Vector2>();
            public Bounds uvBounds;
            public Color maskColor = Color.red;
            public int faceCount => triangleIndices.Count;
        }
        
        /// <summary>
        /// Analyze UV islands from mesh using configurable algorithm
        /// 設定可能なアルゴリズムを使用してメッシュからUVアイランドを解析
        /// </summary>
        public static List<UVIsland> AnalyzeUVIslands(Mesh mesh)
        {
            if (UseAdvancedAlgorithm)
            {
                return AnalyzeUVIslandsAdvanced(mesh);
            }
            else
            {
                return AnalyzeUVIslandsLegacy(mesh);
            }
        }
        
        /// <summary>
        /// Advanced topology-aware UV island analysis
        /// 高度なトポロジー認識UVアイランド解析
        /// </summary>
        private static List<UVIsland> AnalyzeUVIslandsAdvanced(Mesh mesh)
        {
            if (mesh == null || mesh.uv == null || mesh.uv.Length == 0)
                return new List<UVIsland>();
                
            if (mesh.triangles == null || mesh.triangles.Length == 0)
                return new List<UVIsland>();
                
            var uvs = mesh.uv;
            var triangles = mesh.triangles;
            var islands = new List<UVIsland>();
            var processedTriangles = new HashSet<int>();
            
            // Build comprehensive connectivity information
            var vertexToTriangles = BuildVertexToTriangleMapping(triangles, uvs.Length);
            var triangleAdjacency = BuildTriangleAdjacencyGraph(triangles, uvs, vertexToTriangles);
            
            // Process triangles using improved connectivity analysis
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int triIndex = i / 3;
                if (processedTriangles.Contains(triIndex))
                    continue;
                    
                var island = new UVIsland
                {
                    islandID = islands.Count,
                    maskColor = GenerateIslandColor(islands.Count)
                };
                
                // Advanced flood fill with topology-aware connectivity
                var trianglesToProcess = new Queue<int>();
                trianglesToProcess.Enqueue(triIndex);
                
                while (trianglesToProcess.Count > 0)
                {
                    int currentTriIndex = trianglesToProcess.Dequeue();
                    if (processedTriangles.Contains(currentTriIndex))
                        continue;
                        
                    processedTriangles.Add(currentTriIndex);
                    
                    // Add triangle index to island
                    island.triangleIndices.Add(currentTriIndex);
                    
                    // Add triangle vertices to island
                    int triStart = currentTriIndex * 3;
                    for (int v = 0; v < 3; v++)
                    {
                        int vertIndex = triangles[triStart + v];
                        if (!island.vertexIndices.Contains(vertIndex))
                        {
                            island.vertexIndices.Add(vertIndex);
                            island.uvCoordinates.Add(uvs[vertIndex]);
                        }
                    }
                    
                    // Find adjacent triangles using improved connectivity analysis
                    if (triangleAdjacency.ContainsKey(currentTriIndex))
                    {
                        foreach (var adjacentTriIndex in triangleAdjacency[currentTriIndex])
                        {
                            if (!processedTriangles.Contains(adjacentTriIndex))
                            {
                                trianglesToProcess.Enqueue(adjacentTriIndex);
                            }
                        }
                    }
                }
                
                // Calculate UV bounds for the island
                island.uvBounds = CalculateUVBounds(island.uvCoordinates);
                islands.Add(island);
            }
            
            return islands;
        }
        
        /// <summary>
        /// Legacy UV island analysis for compatibility
        /// 互換性のためのレガシーUVアイランド解析
        /// </summary>
        private static List<UVIsland> AnalyzeUVIslandsLegacy(Mesh mesh)
        {
            if (mesh == null || mesh.uv == null || mesh.uv.Length == 0)
                return new List<UVIsland>();
                
            if (mesh.triangles == null || mesh.triangles.Length == 0)
                return new List<UVIsland>();
                
            var uvs = mesh.uv;
            var triangles = mesh.triangles;
            var islands = new List<UVIsland>();
            var processedTriangles = new HashSet<int>();
            
            // Pre-build vertex to triangle mapping for faster adjacency lookup
            var vertexToTriangles = BuildVertexToTriangleMapping(triangles, uvs.Length);
            
            // Group triangles by connected UV coordinates
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int triIndex = i / 3;
                if (processedTriangles.Contains(triIndex))
                    continue;
                    
                var island = new UVIsland
                {
                    islandID = islands.Count,
                    maskColor = GenerateIslandColor(islands.Count)
                };
                
                // Flood fill to find all connected triangles
                var trianglesToProcess = new Queue<int>();
                trianglesToProcess.Enqueue(triIndex);
                
                while (trianglesToProcess.Count > 0)
                {
                    int currentTriIndex = trianglesToProcess.Dequeue();
                    if (processedTriangles.Contains(currentTriIndex))
                        continue;
                        
                    processedTriangles.Add(currentTriIndex);
                    
                    // Add triangle index to island (not vertex indices!)
                    island.triangleIndices.Add(currentTriIndex);
                    
                    // Add triangle vertices to island
                    int triStart = currentTriIndex * 3;
                    for (int v = 0; v < 3; v++)
                    {
                        int vertIndex = triangles[triStart + v];
                        if (!island.vertexIndices.Contains(vertIndex))
                        {
                            island.vertexIndices.Add(vertIndex);
                            island.uvCoordinates.Add(uvs[vertIndex]);
                        }
                    }
                    
                    // Find adjacent triangles with shared UV coordinates using pre-built mapping
                    FindAdjacentTrianglesOptimized(currentTriIndex, triangles, uvs, trianglesToProcess, 
                        processedTriangles, vertexToTriangles);
                }
                
                // Calculate UV bounds for the island
                island.uvBounds = CalculateUVBounds(island.uvCoordinates);
                islands.Add(island);
            }
            
            return islands;
        }
        
        private static Dictionary<int, List<int>> BuildVertexToTriangleMapping(int[] triangles, int vertexCount)
        {
            var mapping = new Dictionary<int, List<int>>();
            
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int triIndex = i / 3;
                
                for (int j = 0; j < 3; j++)
                {
                    int vertIndex = triangles[i + j];
                    if (!mapping.ContainsKey(vertIndex))
                    {
                        mapping[vertIndex] = new List<int>();
                    }
                    mapping[vertIndex].Add(triIndex);
                }
            }
            
            return mapping;
        }
        
        /// <summary>
        /// Build advanced triangle adjacency graph using multiple connectivity criteria
        /// 複数の接続基準を使用して高度な三角形隣接グラフを構築
        /// </summary>
        private static Dictionary<int, HashSet<int>> BuildTriangleAdjacencyGraph(int[] triangles, Vector2[] uvs, Dictionary<int, List<int>> vertexToTriangles)
        {
            var adjacency = new Dictionary<int, HashSet<int>>();
            int triangleCount = triangles.Length / 3;
            
            // Initialize adjacency sets
            for (int i = 0; i < triangleCount; i++)
            {
                adjacency[i] = new HashSet<int>();
            }
            
            // Build edge-based adjacency (primary method - most reliable)
            var edgeToTriangles = new Dictionary<(int, int), List<int>>();
            
            for (int i = 0; i < triangleCount; i++)
            {
                int baseIndex = i * 3;
                int v0 = triangles[baseIndex];
                int v1 = triangles[baseIndex + 1];
                int v2 = triangles[baseIndex + 2];
                
                // Add all three edges of the triangle
                AddEdgeToMapping(edgeToTriangles, v0, v1, i);
                AddEdgeToMapping(edgeToTriangles, v1, v2, i);
                AddEdgeToMapping(edgeToTriangles, v2, v0, i);
            }
            
            // Connect triangles that share edges
            foreach (var kvp in edgeToTriangles)
            {
                var trianglesOnEdge = kvp.Value;
                if (trianglesOnEdge.Count >= 2)
                {
                    // Check if triangles sharing this edge should be in the same UV island
                    for (int i = 0; i < trianglesOnEdge.Count; i++)
                    {
                        for (int j = i + 1; j < trianglesOnEdge.Count; j++)
                        {
                            int tri1 = trianglesOnEdge[i];
                            int tri2 = trianglesOnEdge[j];
                            
                            if (AreTrianglesUVConnected(tri1, tri2, triangles, uvs, kvp.Key))
                            {
                                adjacency[tri1].Add(tri2);
                                adjacency[tri2].Add(tri1);
                            }
                        }
                    }
                }
            }
            
            // Add UV-space proximity connections as secondary method
            // This handles cases where mesh topology might have gaps but UV space is continuous
            AddUVProximityConnections(adjacency, triangles, uvs, vertexToTriangles);
            
            // Add aggressive proximity connections as tertiary method to catch remaining splits
            AddAggressiveProximityConnections(adjacency, triangles, uvs);
            
            return adjacency;
        }
        
        /// <summary>
        /// Add edge to triangle mapping with proper ordering
        /// </summary>
        private static void AddEdgeToMapping(Dictionary<(int, int), List<int>> edgeToTriangles, int v1, int v2, int triangleIndex)
        {
            // Always store edge with smaller vertex index first for consistency
            var edge = v1 < v2 ? (v1, v2) : (v2, v1);
            
            if (!edgeToTriangles.ContainsKey(edge))
            {
                edgeToTriangles[edge] = new List<int>();
            }
            edgeToTriangles[edge].Add(triangleIndex);
        }
        
        /// <summary>
        /// Check if two triangles sharing an edge should be UV-connected
        /// エッジを共有する2つの三角形がUV接続されるべきかチェック
        /// </summary>
        private static bool AreTrianglesUVConnected(int tri1, int tri2, int[] triangles, Vector2[] uvs, (int, int) sharedEdge)
        {
            // The key insight: triangles sharing an edge are UV-connected if their UV coordinates 
            // are continuous across the shared edge. If there's a UV seam, they belong to different islands.
            
            // For triangles sharing a geometric edge to be in the same UV island,
            // the UV coordinates at the shared edge vertices must be nearly identical
            // (within tolerance) for both triangles.
            
            var edgeUV1 = uvs[sharedEdge.Item1];
            var edgeUV2 = uvs[sharedEdge.Item2];
            
            // Calculate the UV distance of the shared edge
            var uvEdgeLength = Vector2.Distance(edgeUV1, edgeUV2);
            
            // If the UV edge length is reasonable (not degenerate), these triangles are likely connected
            // A very small UV edge might indicate collapsed/degenerate UVs
            if (uvEdgeLength < TriangleTolerance) return false;
            
            // Additional check: ensure the triangles are actually adjacent in UV space
            // by checking if any vertices from both triangles have close UV coordinates
            int base1 = tri1 * 3;
            int base2 = tri2 * 3;
            
            for (int i = 0; i < 3; i++)
            {
                var uv1 = uvs[triangles[base1 + i]];
                for (int j = 0; j < 3; j++)
                {
                    var uv2 = uvs[triangles[base2 + j]];
                    if (Vector2.Distance(uv1, uv2) < UVTolerance)
                    {
                        return true; // Found close UV coordinates, triangles are connected
                    }
                }
            }
            
            return false; // No close UV coordinates found, triangles are in different islands
        }
        
        /// <summary>
        /// Add UV proximity-based connections for cases where topology might have gaps
        /// トポロジーにギャップがある場合のUV近接性ベースの接続を追加
        /// </summary>
        private static void AddUVProximityConnections(Dictionary<int, HashSet<int>> adjacency, int[] triangles, Vector2[] uvs, Dictionary<int, List<int>> vertexToTriangles)
        {
            // This method adds connections based on UV coordinate proximity
            // It's a fallback for cases where edge-based detection might miss connections
            
            int triangleCount = triangles.Length / 3;
            
            for (int i = 0; i < triangleCount; i++)
            {
                // Removed adjacency count limit that was causing UV island splitting
                // if (adjacency[i].Count >= 6) continue;
                
                int base1 = i * 3;
                for (int v = 0; v < 3; v++)
                {
                    int vertIndex = triangles[base1 + v];
                    var currentUV = uvs[vertIndex];
                    
                    if (vertexToTriangles.ContainsKey(vertIndex))
                    {
                        foreach (var otherTriIndex in vertexToTriangles[vertIndex])
                        {
                            if (otherTriIndex == i || adjacency[i].Contains(otherTriIndex)) continue;
                            
                            int base2 = otherTriIndex * 3;
                            for (int v2 = 0; v2 < 3; v2++)
                            {
                                int otherVertIndex = triangles[base2 + v2];
                                var otherUV = uvs[otherVertIndex];
                                
                                if (Vector2.Distance(currentUV, otherUV) < UVTolerance)
                                {
                                    adjacency[i].Add(otherTriIndex);
                                    adjacency[otherTriIndex].Add(i);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Add aggressive proximity-based connections to catch islands that should be merged
        /// マージされるべきアイランドを捉えるための積極的な近接接続を追加
        /// </summary>
        private static void AddAggressiveProximityConnections(Dictionary<int, HashSet<int>> adjacency, int[] triangles, Vector2[] uvs)
        {
            int triangleCount = triangles.Length / 3;
            float aggressiveTolerance = UVTolerance * 2.0f; // More aggressive tolerance
            
            // Check all triangle pairs for potential connections
            // This is more computationally intensive but catches edge cases
            for (int i = 0; i < triangleCount - 1; i++)
            {
                if (adjacency[i].Count > 20) continue; // Reasonable limit to prevent excessive connections
                
                int base1 = i * 3;
                Vector2 center1 = GetTriangleUVCenter(triangles, uvs, base1);
                
                for (int j = i + 1; j < triangleCount; j++)
                {
                    if (adjacency[i].Contains(j)) continue; // Already connected
                    
                    int base2 = j * 3;
                    Vector2 center2 = GetTriangleUVCenter(triangles, uvs, base2);
                    
                    // Check if triangle centers are close in UV space
                    if (Vector2.Distance(center1, center2) < aggressiveTolerance)
                    {
                        // Additional validation: check if any vertices are close
                        bool hasCloseVertices = false;
                        for (int v1 = 0; v1 < 3; v1++)
                        {
                            var uv1 = uvs[triangles[base1 + v1]];
                            for (int v2 = 0; v2 < 3; v2++)
                            {
                                var uv2 = uvs[triangles[base2 + v2]];
                                if (Vector2.Distance(uv1, uv2) < UVTolerance)
                                {
                                    hasCloseVertices = true;
                                    break;
                                }
                            }
                            if (hasCloseVertices) break;
                        }
                        
                        if (hasCloseVertices)
                        {
                            adjacency[i].Add(j);
                            adjacency[j].Add(i);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Calculate the UV center of a triangle
        /// 三角形のUV中心を計算
        /// </summary>
        private static Vector2 GetTriangleUVCenter(int[] triangles, Vector2[] uvs, int baseIndex)
        {
            var uv0 = uvs[triangles[baseIndex]];
            var uv1 = uvs[triangles[baseIndex + 1]];
            var uv2 = uvs[triangles[baseIndex + 2]];
            return (uv0 + uv1 + uv2) / 3f;
        }
        
        private static void FindAdjacentTrianglesOptimized(int triangleIndex, int[] triangles, Vector2[] uvs, 
            Queue<int> trianglesToProcess, HashSet<int> processedTriangles, Dictionary<int, List<int>> vertexToTriangles)
        {
            int triStart = triangleIndex * 3;
            var uvTolerance = UVTolerance; // Use configurable UV tolerance for island merging
            var adjacentTriangles = new HashSet<int>();
            
            // For each vertex in the current triangle
            for (int i = 0; i < 3; i++)
            {
                int vertIndex = triangles[triStart + i];
                var currentUV = uvs[vertIndex];
                
                // Only check triangles that share this vertex index (much faster)
                if (vertexToTriangles.ContainsKey(vertIndex))
                {
                    foreach (var otherTriIndex in vertexToTriangles[vertIndex])
                    {
                        if (processedTriangles.Contains(otherTriIndex) || otherTriIndex == triangleIndex)
                            continue;
                        
                        // Check if this triangle has vertices with similar UV coordinates
                        int otherTriStart = otherTriIndex * 3;
                        bool hasSharedUV = false;
                        
                        // Check all combinations of vertices between triangles for UV connectivity
                        for (int j = 0; j < 3; j++)
                        {
                            var otherVertIndex = triangles[otherTriStart + j];
                            var otherUV = uvs[otherVertIndex];
                            
                            // Check if UVs are close enough to be considered connected
                            if (Vector2.Distance(currentUV, otherUV) < uvTolerance)
                            {
                                hasSharedUV = true;
                                break;
                            }
                        }
                        
                        // Additional check: if triangles share vertex indices and UV coordinates are similar
                        if (!hasSharedUV)
                        {
                            for (int j = 0; j < 3; j++)
                            {
                                var otherVertIndex = triangles[otherTriStart + j];
                                if (vertIndex == otherVertIndex)
                                {
                                    // Same vertex index, definitely connected in UV space
                                    hasSharedUV = true;
                                    break;
                                }
                            }
                        }
                        
                        if (hasSharedUV)
                        {
                            adjacentTriangles.Add(otherTriIndex);
                        }
                    }
                }
            }
            
            // Add all adjacent triangles to processing queue
            foreach (var adjTri in adjacentTriangles)
            {
                trianglesToProcess.Enqueue(adjTri);
            }
        }
        
        
        private static Bounds CalculateUVBounds(List<Vector2> uvCoordinates)
        {
            if (uvCoordinates.Count == 0)
                return new Bounds();
                
            var min = uvCoordinates[0];
            var max = uvCoordinates[0];
            
            foreach (var uv in uvCoordinates)
            {
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }
            
            var center = (min + max) * 0.5f;
            var size = max - min;
            return new Bounds(new Vector3(center.x, center.y, 0), new Vector3(size.x, size.y, 0));
        }
        
        private static Color GenerateIslandColor(int index)
        {
            var colors = new Color[]
            {
                new Color(1f, 0.2f, 0.2f, 0.8f), // Red
                new Color(0.2f, 1f, 0.2f, 0.8f), // Green  
                new Color(0.2f, 0.2f, 1f, 0.8f), // Blue
                new Color(1f, 1f, 0.2f, 0.8f),   // Yellow
                new Color(1f, 0.2f, 1f, 0.8f),   // Magenta
                new Color(0.2f, 1f, 1f, 0.8f),   // Cyan
                new Color(1f, 0.6f, 0.2f, 0.8f), // Orange
                new Color(0.6f, 0.2f, 1f, 0.8f)  // Purple
            };
            return colors[index % colors.Length];
        }
        
        /// <summary>
        /// Check if point is inside UV island
        /// 点がUVアイランド内にあるかチェック
        /// </summary>
        public static bool IsPointInUVIsland(Vector2 point, UVIsland island, Vector2[] uvs, int[] triangles)
        {
            // island.triangleIndices contains triangle indices, not vertex indices
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
                
                if (IsPointInTriangle(point, uv0, uv1, uv2))
                    return true;
            }
            return false;
        }
        
        public static bool IsPointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = c - a;
            Vector2 v1 = b - a;
            Vector2 v2 = point - a;
            
            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);
            
            float denom = dot00 * dot11 - dot01 * dot01;
            if (Mathf.Abs(denom) < TriangleTolerance) return false;
            
            float invDenom = 1 / denom;
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
            
            return (u >= 0) && (v >= 0) && (u + v <= 1);
        }
    }
}