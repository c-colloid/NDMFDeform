using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ExDeform.Runtime.Core.Domain.Interfaces;

namespace ExDeform.Runtime.Core.Domain
{
    /// <summary>
    /// Domain service for UV island analysis - encapsulates complex analysis logic
    /// UVアイランド解析ドメインサービス - 複雑な解析ロジックをカプセル化
    /// </summary>
    public class UVIslandAnalysisService : IUVIslandAnalysisService
    {
        #region Configuration
        public struct AnalysisSettings
        {
            public float uvTolerance;
            public bool optimizePerformance;
            public bool includeDegenerate;
            public int maxIslandCount;

            public static AnalysisSettings Default => new AnalysisSettings
            {
                uvTolerance = 0.001f,
                optimizePerformance = true,
                includeDegenerate = false,
                maxIslandCount = 1000
            };
        }
        #endregion

        #region Fields
        private readonly AnalysisSettings _settings;
        private Dictionary<int, List<int>> _vertexToTriangleMapping;
        #endregion

        #region Constructor
        public UVIslandAnalysisService(AnalysisSettings settings = default)
        {
            _settings = settings.Equals(default(AnalysisSettings)) ? AnalysisSettings.Default : settings;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Analyze UV islands from mesh data
        /// </summary>
        public AnalysisResult AnalyzeMesh(Mesh mesh)
        {
            var result = new AnalysisResult();

            if (!ValidateMeshData(mesh, result))
            {
                return result;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var islands = PerformAnalysis(mesh);
                
                result.Islands = islands;
                result.IslandCount = islands.Count;
                result.TotalVertices = mesh.vertexCount;
                result.TotalTriangles = mesh.triangles.Length / 3;
                result.AnalysisTimeMs = stopwatch.ElapsedMilliseconds;
                result.IsSuccess = true;

                CalculateStatistics(result);
            }
            catch (System.Exception ex)
            {
                result.ErrorMessage = $"Analysis failed: {ex.Message}";
                result.IsSuccess = false;
            }

            stopwatch.Stop();
            return result;
        }

        /// <summary>
        /// Find islands that contain specific UV coordinates
        /// </summary>
        public List<UVIsland> FindIslandsContaining(IReadOnlyList<UVIsland> islands, Vector2 uvPoint, Mesh mesh)
        {
            var result = new List<UVIsland>();
            var uvs = mesh.uv;
            var triangles = mesh.triangles;

            foreach (var island in islands)
            {
                if (island.ContainsPoint(uvPoint, uvs, triangles))
                {
                    result.Add(island);
                }
            }

            return result;
        }

        /// <summary>
        /// Find adjacent islands (islands that share UV space boundaries)
        /// </summary>
        public Dictionary<UVIsland, List<UVIsland>> FindAdjacentIslands(IReadOnlyList<UVIsland> islands)
        {
            var adjacencyMap = new Dictionary<UVIsland, List<UVIsland>>();

            for (int i = 0; i < islands.Count; i++)
            {
                var island1 = islands[i];
                adjacencyMap[island1] = new List<UVIsland>();

                for (int j = i + 1; j < islands.Count; j++)
                {
                    var island2 = islands[j];
                    
                    if (island1.CanMergeWith(island2, _settings.uvTolerance))
                    {
                        adjacencyMap[island1].Add(island2);
                        
                        if (!adjacencyMap.ContainsKey(island2))
                        {
                            adjacencyMap[island2] = new List<UVIsland>();
                        }
                        adjacencyMap[island2].Add(island1);
                    }
                }
            }

            return adjacencyMap;
        }

        /// <summary>
        /// Merge adjacent islands that meet criteria
        /// </summary>
        public List<UVIsland> MergeAdjacentIslands(IReadOnlyList<UVIsland> islands, float mergeThreshold = 0.001f)
        {
            var adjacencyMap = FindAdjacentIslands(islands);
            var processed = new HashSet<int>();
            var mergedIslands = new List<UVIsland>();
            int newIslandId = 0;

            foreach (var island in islands)
            {
                if (processed.Contains(island.IslandID))
                    continue;

                var islandGroup = new List<UVIsland> { island };
                var toProcess = new Queue<UVIsland>();
                toProcess.Enqueue(island);
                processed.Add(island.IslandID);

                // Find all connected islands
                while (toProcess.Count > 0)
                {
                    var current = toProcess.Dequeue();
                    if (adjacencyMap.ContainsKey(current))
                    {
                        foreach (var adjacent in adjacencyMap[current])
                        {
                            if (!processed.Contains(adjacent.IslandID))
                            {
                                islandGroup.Add(adjacent);
                                toProcess.Enqueue(adjacent);
                                processed.Add(adjacent.IslandID);
                            }
                        }
                    }
                }

                // Create merged island
                if (islandGroup.Count > 1)
                {
                    var mergedIsland = CreateMergedIsland(islandGroup, newIslandId++);
                    mergedIslands.Add(mergedIsland);
                }
                else
                {
                    mergedIslands.Add(island);
                }
            }

            return mergedIslands;
        }

        /// <summary>
        /// Validate island data integrity
        /// </summary>
        public ValidationResult ValidateIslands(IReadOnlyList<UVIsland> islands, Mesh mesh)
        {
            var result = new ValidationResult();

            if (mesh?.triangles == null || mesh.uv == null)
            {
                result.AddError("Mesh data is invalid");
                return result;
            }

            var triangles = mesh.triangles;
            var uvs = mesh.uv;
            var usedTriangles = new HashSet<int>();
            var usedVertices = new HashSet<int>();

            foreach (var island in islands)
            {
                // Validate island data
                if (island == null)
                {
                    result.AddError("Found null island");
                    continue;
                }

                if (!island.IsValid)
                {
                    result.AddError($"Island {island.IslandID} is invalid");
                    continue;
                }

                // Check for duplicate triangle usage
                foreach (var triIndex in island.TriangleIndices)
                {
                    if (usedTriangles.Contains(triIndex))
                    {
                        result.AddError($"Triangle {triIndex} is used by multiple islands");
                    }
                    else
                    {
                        usedTriangles.Add(triIndex);
                    }
                }

                // Validate triangle indices
                foreach (var triIndex in island.TriangleIndices)
                {
                    int baseIndex = triIndex * 3;
                    if (baseIndex + 2 >= triangles.Length)
                    {
                        result.AddError($"Triangle index {triIndex} is out of bounds");
                    }
                }

                // Validate vertex indices
                foreach (var vertIndex in island.VertexIndices)
                {
                    if (vertIndex >= uvs.Length)
                    {
                        result.AddError($"Vertex index {vertIndex} is out of bounds");
                    }
                }
            }

            // Check coverage
            int expectedTriangles = triangles.Length / 3;
            if (usedTriangles.Count != expectedTriangles)
            {
                result.AddWarning($"Island coverage incomplete: {usedTriangles.Count}/{expectedTriangles} triangles");
            }

            return result;
        }
        #endregion

        #region Private Methods
        private bool ValidateMeshData(Mesh mesh, AnalysisResult result)
        {
            if (mesh == null)
            {
                result.ErrorMessage = "Mesh is null";
                return false;
            }

            if (mesh.uv == null || mesh.uv.Length == 0)
            {
                result.ErrorMessage = "Mesh has no UV coordinates";
                return false;
            }

            if (mesh.triangles == null || mesh.triangles.Length == 0)
            {
                result.ErrorMessage = "Mesh has no triangles";
                return false;
            }

            if (mesh.triangles.Length % 3 != 0)
            {
                result.ErrorMessage = "Invalid triangle data";
                return false;
            }

            return true;
        }

        private List<UVIsland> PerformAnalysis(Mesh mesh)
        {
            var uvs = mesh.uv;
            var triangles = mesh.triangles;
            var islands = new List<UVIsland>();
            var processedTriangles = new HashSet<int>();

            // Build optimization structures if enabled
            if (_settings.optimizePerformance)
            {
                _vertexToTriangleMapping = BuildVertexToTriangleMapping(triangles, uvs.Length);
            }

            // Analyze triangles
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int triIndex = i / 3;
                if (processedTriangles.Contains(triIndex))
                    continue;

                if (islands.Count >= _settings.maxIslandCount)
                {
                    Debug.LogWarning($"Reached maximum island count ({_settings.maxIslandCount})");
                    break;
                }

                var island = CreateIslandFromTriangle(triIndex, islands.Count, triangles, uvs, processedTriangles);
                if (island.IsValid && (_settings.includeDegenerate || island.UVArea > 1e-6f))
                {
                    islands.Add(island);
                }
            }

            return islands;
        }

        private UVIsland CreateIslandFromTriangle(int startTriIndex, int islandId, int[] triangles, Vector2[] uvs, HashSet<int> processedTriangles)
        {
            var island = new UVIsland(islandId);
            var trianglesToProcess = new Queue<int>();
            trianglesToProcess.Enqueue(startTriIndex);

            while (trianglesToProcess.Count > 0)
            {
                int currentTriIndex = trianglesToProcess.Dequeue();
                if (processedTriangles.Contains(currentTriIndex))
                    continue;

                processedTriangles.Add(currentTriIndex);
                island.AddTriangle(currentTriIndex);

                // Add triangle vertices
                int triStart = currentTriIndex * 3;
                for (int v = 0; v < 3; v++)
                {
                    int vertIndex = triangles[triStart + v];
                    island.AddVertex(vertIndex, uvs[vertIndex]);
                }

                // Find adjacent triangles
                FindAdjacentTriangles(currentTriIndex, triangles, uvs, trianglesToProcess, processedTriangles);
            }

            island.RecalculateBounds();
            return island;
        }

        private void FindAdjacentTriangles(int triangleIndex, int[] triangles, Vector2[] uvs, Queue<int> trianglesToProcess, HashSet<int> processedTriangles)
        {
            int triStart = triangleIndex * 3;
            var adjacentTriangles = new HashSet<int>();

            for (int i = 0; i < 3; i++)
            {
                int vertIndex = triangles[triStart + i];
                var currentUV = uvs[vertIndex];

                if (_settings.optimizePerformance && _vertexToTriangleMapping != null)
                {
                    FindAdjacentTrianglesOptimized(vertIndex, currentUV, uvs, triangles, adjacentTriangles, processedTriangles, triangleIndex);
                }
                else
                {
                    FindAdjacentTrianglesBruteForce(currentUV, uvs, triangles, adjacentTriangles, processedTriangles, triangleIndex);
                }
            }

            foreach (var adjTri in adjacentTriangles)
            {
                trianglesToProcess.Enqueue(adjTri);
            }
        }

        private void FindAdjacentTrianglesOptimized(int vertIndex, Vector2 currentUV, Vector2[] uvs, int[] triangles, 
            HashSet<int> adjacentTriangles, HashSet<int> processedTriangles, int currentTriangleIndex)
        {
            if (!_vertexToTriangleMapping.ContainsKey(vertIndex))
                return;

            foreach (var otherTriIndex in _vertexToTriangleMapping[vertIndex])
            {
                if (processedTriangles.Contains(otherTriIndex) || otherTriIndex == currentTriangleIndex)
                    continue;

                if (TriangleHasUVCoordinate(otherTriIndex, currentUV, uvs, triangles))
                {
                    adjacentTriangles.Add(otherTriIndex);
                }
            }
        }

        private void FindAdjacentTrianglesBruteForce(Vector2 currentUV, Vector2[] uvs, int[] triangles, 
            HashSet<int> adjacentTriangles, HashSet<int> processedTriangles, int currentTriangleIndex)
        {
            for (int tri = 0; tri < triangles.Length; tri += 3)
            {
                int triIndex = tri / 3;
                if (processedTriangles.Contains(triIndex) || triIndex == currentTriangleIndex)
                    continue;

                if (TriangleHasUVCoordinate(triIndex, currentUV, uvs, triangles))
                {
                    adjacentTriangles.Add(triIndex);
                }
            }
        }

        private bool TriangleHasUVCoordinate(int triIndex, Vector2 targetUV, Vector2[] uvs, int[] triangles)
        {
            int triStart = triIndex * 3;
            for (int j = 0; j < 3; j++)
            {
                if (Vector2.Distance(targetUV, uvs[triangles[triStart + j]]) < _settings.uvTolerance)
                {
                    return true;
                }
            }
            return false;
        }

        private Dictionary<int, List<int>> BuildVertexToTriangleMapping(int[] triangles, int vertexCount)
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

        private UVIsland CreateMergedIsland(List<UVIsland> islandGroup, int newId)
        {
            var mergedVertices = new List<int>();
            var mergedTriangles = new List<int>();
            var mergedUVs = new List<Vector2>();

            foreach (var island in islandGroup)
            {
                mergedVertices.AddRange(island.VertexIndices);
                mergedTriangles.AddRange(island.TriangleIndices);
                mergedUVs.AddRange(island.UVCoordinates);
            }

            // Remove duplicates while preserving order
            mergedVertices = mergedVertices.Distinct().ToList();
            mergedTriangles = mergedTriangles.Distinct().ToList();
            mergedUVs = mergedUVs.Distinct().ToList();

            return new UVIsland(newId, mergedVertices, mergedTriangles, mergedUVs);
        }

        private void CalculateStatistics(AnalysisResult result)
        {
            if (result.Islands == null || result.Islands.Count == 0)
                return;

            result.AverageIslandSize = (float)result.Islands.Average(i => i.VertexCount);
            result.LargestIslandSize = result.Islands.Max(i => i.VertexCount);
            result.SmallestIslandSize = result.Islands.Min(i => i.VertexCount);
            result.TotalUVArea = result.Islands.Sum(i => i.UVArea);
            result.AverageUVArea = result.Islands.Average(i => i.UVArea);
        }
        #endregion

        #region Result Types
        /// <summary>
        /// Result of UV island analysis
        /// </summary>
        public class AnalysisResult
        {
            public List<UVIsland> Islands { get; set; } = new List<UVIsland>();
            public bool IsSuccess { get; set; }
            public string ErrorMessage { get; set; }
            public long AnalysisTimeMs { get; set; }

            // Statistics
            public int IslandCount { get; set; }
            public int TotalVertices { get; set; }
            public int TotalTriangles { get; set; }
            public float AverageIslandSize { get; set; }
            public int LargestIslandSize { get; set; }
            public int SmallestIslandSize { get; set; }
            public float TotalUVArea { get; set; }
            public float AverageUVArea { get; set; }

            public override string ToString()
            {
                return $"AnalysisResult(Success={IsSuccess}, Islands={IslandCount}, Time={AnalysisTimeMs}ms)";
            }
        }
        #endregion
    }
}