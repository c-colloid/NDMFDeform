using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace ExDeform.Runtime.Data
{
    /// <summary>
    /// UV Island analysis utility
    /// UVアイランド解析ユーティリティ
    /// </summary>
    public static class UVIslandAnalyzer
    {
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
        /// Analyze UV islands from mesh
        /// メッシュからUVアイランドを解析
        /// </summary>
        public static List<UVIsland> AnalyzeUVIslands(Mesh mesh)
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
        
        private static void FindAdjacentTrianglesOptimized(int triangleIndex, int[] triangles, Vector2[] uvs, 
            Queue<int> trianglesToProcess, HashSet<int> processedTriangles, Dictionary<int, List<int>> vertexToTriangles)
        {
            int triStart = triangleIndex * 3;
            var uvTolerance = 0.001f;
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
                        
                        for (int j = 0; j < 3; j++)
                        {
                            var otherVertIndex = triangles[otherTriStart + j];
                            if (Vector2.Distance(currentUV, uvs[otherVertIndex]) < uvTolerance)
                            {
                                hasSharedUV = true;
                                break;
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
            if (Mathf.Abs(denom) < 1e-6f) return false;
            
            float invDenom = 1 / denom;
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
            
            return (u >= 0) && (v >= 0) && (u + v <= 1);
        }
    }
}