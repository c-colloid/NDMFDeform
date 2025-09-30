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
	    public static float UVTolerance = 0.005f; // Further increased from 0.001f to solve remaining splitting issues
        
        /// <summary>
        /// Triangle degenerate tolerance. Relaxed to include more triangles in analysis.
        /// 三角形の縮退判定許容値。解析により多くの三角形を含むため緩和。
        /// </summary>
        public static float TriangleTolerance = 1e-5f;
        
        /// <summary>
        /// Use advanced topology-aware algorithm. Set to false for legacy compatibility.
	    /// 高度なトポロジー認識アルゴリズムを使用 = true。レガシー互換性 = false
        /// </summary>
        public static bool UseAdvancedAlgorithm = true;
        
        /// <summary>
	    /// Debug logging for UV island analysis troubleshooting.
        /// UVアイランド解析のトラブルシューティング用デバッグログ
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
                
	        var uvs = new List<Vector2>();
	        mesh.GetUVs(0, uvs);
	        var submesh = 0;
	        var triangles = mesh.GetTriangles(submesh);
            var islands = new List<UVIsland>();
            var processedTriangles = new HashSet<int>();
            
            // Build comprehensive connectivity information
	        var vertexToTriangles = BuildVertexToTriangleMapping(triangles, uvs.Count);
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
                
	        var uvs = new List<Vector2>();
	        mesh.GetUVs(0, uvs);
	        var submesh = 0;
	        var triangles = mesh.GetTriangles(submesh);
            var islands = new List<UVIsland>();
            var processedTriangles = new HashSet<int>();
            
            // Pre-build vertex to triangle mapping for faster adjacency lookup
	        var vertexToTriangles = BuildVertexToTriangleMapping(triangles, uvs.Count);
            
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
	    private static Dictionary<int, HashSet<int>> BuildTriangleAdjacencyGraph(int[] triangles, List<Vector2> uvs, Dictionary<int, List<int>> vertexToTriangles)
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
	    private static bool AreTrianglesUVConnected(int tri1, int tri2, int[] triangles, List<Vector2> uvs, (int, int) sharedEdge)
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
	    private static void AddUVProximityConnections(Dictionary<int, HashSet<int>> adjacency, int[] triangles, List<Vector2> uvs, Dictionary<int, List<int>> vertexToTriangles)
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
        /// Add connections using Union-Find with weighted edge analysis
        /// 重み付きエッジ解析とUnion-Findを使用した接続追加
        /// </summary>
	    private static void AddAggressiveProximityConnections(Dictionary<int, HashSet<int>> adjacency, int[] triangles, List<Vector2> uvs)
        {
            int triangleCount = triangles.Length / 3;
            
            // Build potential connections with confidence weights
            var potentialConnections = BuildWeightedConnections(triangles, uvs);
            
            // Use Union-Find to merge islands based on connection strength
            var unionFind = new UnionFind(triangleCount);
            
            // Sort connections by strength (highest first)
            potentialConnections.Sort((a, b) => b.weight.CompareTo(a.weight));
            
            // Add connections in order of strength, avoiding over-connection
            foreach (var connection in potentialConnections)
            {
                int tri1 = connection.triangle1;
                int tri2 = connection.triangle2;
                
                // Skip if already connected or would create excessive connections
                if (adjacency[tri1].Contains(tri2) || 
                    adjacency[tri1].Count >= 15 || adjacency[tri2].Count >= 15) continue;
                
                // Only connect if islands are not already merged and connection is strong
                if (!unionFind.AreConnected(tri1, tri2) && connection.weight > 0.7f)
                {
                    unionFind.Union(tri1, tri2);
                    adjacency[tri1].Add(tri2);
                    adjacency[tri2].Add(tri1);
                }
            }
        }
        
        /// <summary>
        /// Connection with confidence weight
        /// </summary>
        private struct WeightedConnection
        {
            public int triangle1;
            public int triangle2;
            public float weight; // 0.0 to 1.0, higher = more likely to be connected
        }
        
        /// <summary>
        /// Union-Find data structure for efficient connected component tracking
        /// 効率的な連結成分追跡のためのUnion-Findデータ構造
        /// </summary>
        private class UnionFind
        {
            private int[] parent;
            private int[] rank;
            
            public UnionFind(int size)
            {
                parent = new int[size];
                rank = new int[size];
                for (int i = 0; i < size; i++)
                {
                    parent[i] = i;
                    rank[i] = 0;
                }
            }
            
            public int Find(int x)
            {
                if (parent[x] != x)
                    parent[x] = Find(parent[x]); // Path compression
                return parent[x];
            }
            
            public bool Union(int x, int y)
            {
                int rootX = Find(x);
                int rootY = Find(y);
                
                if (rootX == rootY) return false;
                
                // Union by rank
                if (rank[rootX] < rank[rootY])
                {
                    parent[rootX] = rootY;
                }
                else if (rank[rootX] > rank[rootY])
                {
                    parent[rootY] = rootX;
                }
                else
                {
                    parent[rootY] = rootX;
                    rank[rootX]++;
                }
                
                return true;
            }
            
            public bool AreConnected(int x, int y)
            {
                return Find(x) == Find(y);
            }
        }
        
        /// <summary>
        /// Build weighted connections using multiple heuristics
        /// 複数のヒューリスティックを使用して重み付き接続を構築
        /// </summary>
	    private static List<WeightedConnection> BuildWeightedConnections(int[] triangles, List<Vector2> uvs)
        {
            int triangleCount = triangles.Length / 3;
            var connections = new List<WeightedConnection>();
            
            // Build efficient lookup structures
            var vertexUVMap = BuildVertexUVClusters(triangles, uvs);
            
            for (int i = 0; i < triangleCount - 1; i++)
            {
                var candidates = FindConnectionCandidates(i, triangles, uvs, vertexUVMap);
                
                foreach (int j in candidates)
                {
                    if (j <= i) continue;
                    
                    float weight = CalculateConnectionWeight(i, j, triangles, uvs);
                    if (weight > 0.3f) // Only consider reasonably strong connections
                    {
                        connections.Add(new WeightedConnection
                        {
                            triangle1 = i,
                            triangle2 = j,
                            weight = weight
                        });
                    }
                }
            }
            
            return connections;
        }
        
        /// <summary>
        /// Build UV clustering for efficient candidate finding
        /// </summary>
        private static Dictionary<Vector2, HashSet<int>> BuildVertexUVClusters(int[] triangles, List<Vector2> uvs)
        {
            var clusters = new Dictionary<Vector2, HashSet<int>>();
            int triangleCount = triangles.Length / 3;
            
            for (int i = 0; i < triangleCount; i++)
            {
                int baseIndex = i * 3;
                for (int v = 0; v < 3; v++)
                {
                    var uv = uvs[triangles[baseIndex + v]];
                    var clusterKey = QuantizeUV(uv, UVTolerance * 0.5f);
                    
                    if (!clusters.ContainsKey(clusterKey))
                        clusters[clusterKey] = new HashSet<int>();
                    clusters[clusterKey].Add(i);
                }
            }
            
            return clusters;
        }
        
        /// <summary>
        /// Quantize UV coordinate to cluster key
        /// </summary>
        private static Vector2 QuantizeUV(Vector2 uv, float step)
        {
            return new Vector2(
                Mathf.Round(uv.x / step) * step,
                Mathf.Round(uv.y / step) * step
            );
        }
        
        /// <summary>
        /// Find potential connection candidates for a triangle
        /// </summary>
        private static HashSet<int> FindConnectionCandidates(int triangleIndex, int[] triangles, List<Vector2> uvs, Dictionary<Vector2, HashSet<int>> clusters)
        {
            var candidates = new HashSet<int>();
            int baseIndex = triangleIndex * 3;
            
            for (int v = 0; v < 3; v++)
            {
                var uv = uvs[triangles[baseIndex + v]];
                var clusterKey = QuantizeUV(uv, UVTolerance * 0.5f);
                
                if (clusters.ContainsKey(clusterKey))
                {
                    foreach (int candidate in clusters[clusterKey])
                    {
                        if (candidate != triangleIndex)
                            candidates.Add(candidate);
                    }
                }
                
                // Also check adjacent clusters
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        
                        var adjacentKey = clusterKey + new Vector2(dx, dy) * UVTolerance * 0.5f;
                        if (clusters.ContainsKey(adjacentKey))
                        {
                            foreach (int candidate in clusters[adjacentKey])
                            {
                                if (candidate != triangleIndex)
                                    candidates.Add(candidate);
                            }
                        }
                    }
                }
            }
            
            return candidates;
        }
        
        /// <summary>
        /// Calculate connection weight using multiple criteria
        /// 複数の基準を使用して接続重みを計算
        /// </summary>
        private static float CalculateConnectionWeight(int tri1, int tri2, int[] triangles, List<Vector2> uvs)
        {
            float weight = 0f;
            int base1 = tri1 * 3;
            int base2 = tri2 * 3;
            
            // Criterion 1: Vertex proximity (40% of weight)
            float minVertexDist = float.MaxValue;
            int sharedVertices = 0;
            
            for (int v1 = 0; v1 < 3; v1++)
            {
                var uv1 = uvs[triangles[base1 + v1]];
                for (int v2 = 0; v2 < 3; v2++)
                {
                    var uv2 = uvs[triangles[base2 + v2]];
                    float dist = Vector2.Distance(uv1, uv2);
                    
                    if (dist < UVTolerance)
                        sharedVertices++;
                    
                    minVertexDist = Mathf.Min(minVertexDist, dist);
                }
            }
            
            weight += (1f - Mathf.Clamp01(minVertexDist / UVTolerance)) * 0.4f;
            weight += (sharedVertices / 3f) * 0.2f; // Bonus for shared vertices
            
            // Criterion 2: Area overlap (30% of weight)
            float overlap = CalculateTriangleUVOverlap(tri1, tri2, triangles, uvs);
            weight += overlap * 0.3f;
            
            // Criterion 3: Edge continuity (30% of weight)
            float edgeContinuity = CalculateEdgeContinuity(tri1, tri2, triangles, uvs);
            weight += edgeContinuity * 0.3f;
            
            return Mathf.Clamp01(weight);
        }
        
        /// <summary>
        /// Calculate UV area overlap between two triangles
        /// </summary>
        private static float CalculateTriangleUVOverlap(int tri1, int tri2, int[] triangles, List<Vector2> uvs)
        {
            // Simplified overlap calculation using bounding boxes
            var bounds1 = GetTriangleUVBounds(tri1, triangles, uvs);
            var bounds2 = GetTriangleUVBounds(tri2, triangles, uvs);
            
            float overlapArea = Mathf.Max(0, Mathf.Min(bounds1.max.x, bounds2.max.x) - Mathf.Max(bounds1.min.x, bounds2.min.x)) *
                               Mathf.Max(0, Mathf.Min(bounds1.max.y, bounds2.max.y) - Mathf.Max(bounds1.min.y, bounds2.min.y));
            
            float totalArea = (bounds1.size.x * bounds1.size.y) + (bounds2.size.x * bounds2.size.y);
            
            return totalArea > 0 ? (2f * overlapArea) / totalArea : 0f;
        }
        
        /// <summary>
        /// Calculate edge continuity between triangles
        /// </summary>
        private static float CalculateEdgeContinuity(int tri1, int tri2, int[] triangles, List<Vector2> uvs)
        {
            int base1 = tri1 * 3;
            int base2 = tri2 * 3;
            
            float bestContinuity = 0f;
            
            // Check all edge pairs for continuity
            for (int e1 = 0; e1 < 3; e1++)
            {
                var edge1Start = uvs[triangles[base1 + e1]];
                var edge1End = uvs[triangles[base1 + (e1 + 1) % 3]];
                
                for (int e2 = 0; e2 < 3; e2++)
                {
                    var edge2Start = uvs[triangles[base2 + e2]];
                    var edge2End = uvs[triangles[base2 + (e2 + 1) % 3]];
                    
                    // Check if edges are continuous
                    float continuity = CalculateEdgePairContinuity(edge1Start, edge1End, edge2Start, edge2End);
                    bestContinuity = Mathf.Max(bestContinuity, continuity);
                }
            }
            
            return bestContinuity;
        }
        
        /// <summary>
        /// Calculate continuity between two edges
        /// </summary>
        private static float CalculateEdgePairContinuity(Vector2 e1Start, Vector2 e1End, Vector2 e2Start, Vector2 e2End)
        {
            // Check if edges share endpoints
            float d1 = Vector2.Distance(e1End, e2Start);
            float d2 = Vector2.Distance(e1Start, e2End);
            
            float minEndpointDist = Mathf.Min(d1, d2);
            if (minEndpointDist > UVTolerance) return 0f;
            
            // Check parallelism
            Vector2 dir1 = (e1End - e1Start).normalized;
            Vector2 dir2 = (e2End - e2Start).normalized;
            float parallelism = Mathf.Abs(Vector2.Dot(dir1, dir2));
            
            return (1f - minEndpointDist / UVTolerance) * parallelism;
        }
        
        /// <summary>
        /// Get UV bounds for a triangle
        /// </summary>
        private static Bounds GetTriangleUVBounds(int triangleIndex, int[] triangles, List<Vector2> uvs)
        {
            int baseIndex = triangleIndex * 3;
            var uv0 = uvs[triangles[baseIndex]];
            var uv1 = uvs[triangles[baseIndex + 1]];
            var uv2 = uvs[triangles[baseIndex + 2]];
            
            var min = Vector2.Min(Vector2.Min(uv0, uv1), uv2);
            var max = Vector2.Max(Vector2.Max(uv0, uv1), uv2);
            
            return new Bounds((min + max) * 0.5f, max - min);
        }
        
        /// <summary>
        /// Calculate the UV center of a triangle
        /// 三角形のUV中心を計算
        /// </summary>
        private static Vector2 GetTriangleUVCenter(int[] triangles, List<Vector2> uvs, int baseIndex)
        {
            var uv0 = uvs[triangles[baseIndex]];
            var uv1 = uvs[triangles[baseIndex + 1]];
            var uv2 = uvs[triangles[baseIndex + 2]];
            return (uv0 + uv1 + uv2) / 3f;
        }
        
        private static void FindAdjacentTrianglesOptimized(int triangleIndex, int[] triangles, List<Vector2> uvs, 
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
        public static bool IsPointInUVIsland(Vector2 point, UVIsland island, List<Vector2> uvs, int[] triangles)
        {
            return IsPointInUVIslandOptimized(point, island, uvs, triangles);
        }
        
        /// <summary>
        /// Optimized point-in-island test using hybrid approach for better performance on large islands
        /// 大きなアイランドでのパフォーマンス向上のためのハイブリッドアプローチによる最適化されたポイントインアイランドテスト
        /// </summary>
        public static bool IsPointInUVIslandOptimized(Vector2 point, UVIsland island, List<Vector2> uvs, int[] triangles)
        {
            // Early rejection: check if point is within island bounds with small padding
            var bounds = island.uvBounds;
            const float BOUNDS_PADDING = 0.001f;
            if (point.x < bounds.min.x - BOUNDS_PADDING || point.x > bounds.max.x + BOUNDS_PADDING ||
                point.y < bounds.min.y - BOUNDS_PADDING || point.y > bounds.max.y + BOUNDS_PADDING)
            {
                return false;
            }
            
            // For small islands, use direct triangle testing (fast path)
            if (island.triangleIndices.Count <= 20)
            {
                return IsPointInTrianglesDirect(point, island, uvs, triangles);
            }
            
            // For large islands, use ray casting algorithm (more reliable for complex shapes)
            return IsPointInIslandRayCasting(point, island, uvs, triangles);
        }
        
        /// <summary>
        /// Direct triangle testing - fast for small islands
        /// </summary>
        private static bool IsPointInTrianglesDirect(Vector2 point, UVIsland island, List<Vector2> uvs, int[] triangles)
        {
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
	            if (vertIndex0 >= uvs.Count || vertIndex1 >= uvs.Count || vertIndex2 >= uvs.Count) continue;
                
                var uv0 = uvs[vertIndex0];
                var uv1 = uvs[vertIndex1];
                var uv2 = uvs[vertIndex2];
                
                if (IsPointInTriangle(point, uv0, uv1, uv2))
                    return true;
            }
            return false;
        }
        
        /// <summary>
        /// Ray casting algorithm - reliable for large complex islands
        /// レイキャスティングアルゴリズム - 大きな複雑なアイランドに対して信頼性が高い
        /// </summary>
        private static bool IsPointInIslandRayCasting(Vector2 point, UVIsland island, List<Vector2> uvs, int[] triangles)
        {
            int intersections = 0;
            var rayEnd = new Vector2(island.uvBounds.max.x + 0.1f, point.y); // Horizontal ray to the right
            
            // Check intersections with all triangle edges
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
	            if (vertIndex0 >= uvs.Count || vertIndex1 >= uvs.Count || vertIndex2 >= uvs.Count) continue;
                
                var uv0 = uvs[vertIndex0];
                var uv1 = uvs[vertIndex1];
                var uv2 = uvs[vertIndex2];
                
                // Check each edge of the triangle
                if (LineSegmentsIntersect(point, rayEnd, uv0, uv1)) intersections++;
                if (LineSegmentsIntersect(point, rayEnd, uv1, uv2)) intersections++;
                if (LineSegmentsIntersect(point, rayEnd, uv2, uv0)) intersections++;
            }
            
            // Odd number of intersections means point is inside
            return (intersections % 2) == 1;
        }
        
        /// <summary>
        /// Check if two line segments intersect (for ray casting)
        /// 2つの線分が交差するかチェック（レイキャスティング用）
        /// </summary>
        private static bool LineSegmentsIntersect(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
        {
            float d1 = Direction(p2, q2, p1);
            float d2 = Direction(p2, q2, q1);
            float d3 = Direction(p1, q1, p2);
            float d4 = Direction(p1, q1, q2);
            
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                return true;
            else if (d1 == 0 && OnSegment(p2, q2, p1)) return true;
            else if (d2 == 0 && OnSegment(p2, q2, q1)) return true;
            else if (d3 == 0 && OnSegment(p1, q1, p2)) return true;
            else if (d4 == 0 && OnSegment(p1, q1, q2)) return true;
            else return false;
        }
        
        private static float Direction(Vector2 a, Vector2 b, Vector2 c)
        {
            return (c.x - a.x) * (b.y - a.y) - (b.x - a.x) * (c.y - a.y);
        }
        
        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 c)
        {
            return Mathf.Min(a.x, b.x) <= c.x && c.x <= Mathf.Max(a.x, b.x) &&
                   Mathf.Min(a.y, b.y) <= c.y && c.y <= Mathf.Max(a.y, b.y);
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