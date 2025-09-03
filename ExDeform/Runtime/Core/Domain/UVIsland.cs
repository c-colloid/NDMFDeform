using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;

namespace ExDeform.Runtime.Core.Domain
{
    /// <summary>
    /// UV Island domain object - pure domain logic without UI dependencies
    /// UVアイランドドメインオブジェクト - UI依存を排除した純粋なドメインロジック
    /// </summary>
    [System.Serializable]
    public class UVIsland
    {
        #region Core Properties
        public int IslandID { get; private set; }
        public IReadOnlyList<int> VertexIndices => _vertexIndices.AsReadOnly();
        public IReadOnlyList<int> TriangleIndices => _triangleIndices.AsReadOnly();
        public IReadOnlyList<Vector2> UVCoordinates => _uvCoordinates.AsReadOnly();
        public Bounds UVBounds { get; private set; }
        #endregion

        #region Private Fields
        [SerializeField] private List<int> _vertexIndices = new List<int>();
        [SerializeField] private List<int> _triangleIndices = new List<int>();
        [SerializeField] private List<Vector2> _uvCoordinates = new List<Vector2>();
        #endregion

        #region Domain Properties
        public int FaceCount => _triangleIndices.Count;
        public int VertexCount => _vertexIndices.Count;
        public float UVArea => CalculateUVArea();
        public Vector2 UVCenter => UVBounds.center;
        public bool IsValid => _vertexIndices.Count > 0 && _triangleIndices.Count > 0;
        #endregion

        #region Constructors
        public UVIsland(int islandID)
        {
            IslandID = islandID;
        }

        internal UVIsland(int islandID, List<int> vertexIndices, List<int> triangleIndices, List<Vector2> uvCoordinates) : this(islandID)
        {
            _vertexIndices = vertexIndices ?? new List<int>();
            _triangleIndices = triangleIndices ?? new List<int>();
            _uvCoordinates = uvCoordinates ?? new List<Vector2>();
            RecalculateBounds();
        }
        #endregion

        #region Domain Methods
        /// <summary>
        /// Check if a UV point is contained within this island
        /// </summary>
        public bool ContainsPoint(Vector2 point, Vector2[] meshUVs, int[] meshTriangles)
        {
            if (!IsValid || meshUVs == null || meshTriangles == null)
                return false;

            // Quick bounds check first
            if (!UVBounds.Contains(new Vector3(point.x, point.y, 0)))
                return false;

            // Detailed triangle containment check
            foreach (int triangleIndex in _triangleIndices)
            {
                if (IsPointInTriangle(point, triangleIndex, meshUVs, meshTriangles))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if this island overlaps with another island
        /// </summary>
        public bool OverlapsWith(UVIsland other, float tolerance = 0.001f)
        {
            if (other == null || !other.IsValid || !IsValid)
                return false;

            // Quick bounds check
            if (!UVBounds.Intersects(other.UVBounds))
                return false;

            // Check for overlapping UV coordinates
            foreach (var uv1 in _uvCoordinates)
            {
                foreach (var uv2 in other._uvCoordinates)
                {
                    if (Vector2.Distance(uv1, uv2) < tolerance)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Calculate the minimum distance to another island
        /// </summary>
        public float DistanceTo(UVIsland other)
        {
            if (other == null || !other.IsValid || !IsValid)
                return float.MaxValue;

            float minDistance = float.MaxValue;
            foreach (var uv1 in _uvCoordinates)
            {
                foreach (var uv2 in other._uvCoordinates)
                {
                    float distance = Vector2.Distance(uv1, uv2);
                    if (distance < minDistance)
                        minDistance = distance;
                }
            }

            return minDistance;
        }

        /// <summary>
        /// Check if this island can be merged with another (adjacent islands)
        /// </summary>
        public bool CanMergeWith(UVIsland other, float adjacencyTolerance = 0.001f)
        {
            if (other == null || other.IslandID == IslandID)
                return false;

            return DistanceTo(other) <= adjacencyTolerance;
        }

        /// <summary>
        /// Get vertices that are on the border of the island
        /// </summary>
        public IReadOnlyList<int> GetBorderVertices(int[] meshTriangles)
        {
            if (!IsValid || meshTriangles == null)
                return new List<int>().AsReadOnly();

            var edgeCount = new Dictionary<(int, int), int>();
            var borderVertices = new HashSet<int>();

            // Count edge usage across triangles in this island
            foreach (int triangleIndex in _triangleIndices)
            {
                int baseIndex = triangleIndex * 3;
                if (baseIndex + 2 >= meshTriangles.Length) continue;

                var vertices = new int[]
                {
                    meshTriangles[baseIndex],
                    meshTriangles[baseIndex + 1],
                    meshTriangles[baseIndex + 2]
                };

                // Check all three edges of the triangle
                for (int i = 0; i < 3; i++)
                {
                    int v1 = vertices[i];
                    int v2 = vertices[(i + 1) % 3];
                    
                    var edge = v1 < v2 ? (v1, v2) : (v2, v1);
                    edgeCount[edge] = edgeCount.GetValueOrDefault(edge, 0) + 1;
                }
            }

            // Border edges appear only once
            foreach (var kvp in edgeCount.Where(kvp => kvp.Value == 1))
            {
                borderVertices.Add(kvp.Key.Item1);
                borderVertices.Add(kvp.Key.Item2);
            }

            return borderVertices.ToList().AsReadOnly();
        }
        #endregion

        #region Internal Methods
        internal void AddVertex(int vertexIndex, Vector2 uvCoordinate)
        {
            if (!_vertexIndices.Contains(vertexIndex))
            {
                _vertexIndices.Add(vertexIndex);
                _uvCoordinates.Add(uvCoordinate);
            }
        }

        internal void AddTriangle(int triangleIndex)
        {
            if (!_triangleIndices.Contains(triangleIndex))
            {
                _triangleIndices.Add(triangleIndex);
            }
        }

        internal void RecalculateBounds()
        {
            if (_uvCoordinates.Count == 0)
            {
                UVBounds = new Bounds();
                return;
            }

            var min = _uvCoordinates[0];
            var max = _uvCoordinates[0];

            foreach (var uv in _uvCoordinates)
            {
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }

            var center = (min + max) * 0.5f;
            var size = max - min;
            UVBounds = new Bounds(new Vector3(center.x, center.y, 0), new Vector3(size.x, size.y, 0));
        }
        #endregion

        #region Private Helper Methods
        private float CalculateUVArea()
        {
            if (_uvCoordinates.Count < 3)
                return 0f;

            float area = 0f;
            int n = _uvCoordinates.Count;

            // Use shoelace formula for polygon area
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += _uvCoordinates[i].x * _uvCoordinates[j].y;
                area -= _uvCoordinates[j].x * _uvCoordinates[i].y;
            }

            return Mathf.Abs(area) / 2.0f;
        }

        private bool IsPointInTriangle(Vector2 point, int triangleIndex, Vector2[] meshUVs, int[] meshTriangles)
        {
            int baseIndex = triangleIndex * 3;
            if (baseIndex + 2 >= meshTriangles.Length) return false;

            int v0 = meshTriangles[baseIndex];
            int v1 = meshTriangles[baseIndex + 1];
            int v2 = meshTriangles[baseIndex + 2];

            if (v0 >= meshUVs.Length || v1 >= meshUVs.Length || v2 >= meshUVs.Length) 
                return false;

            return IsPointInTriangle(point, meshUVs[v0], meshUVs[v1], meshUVs[v2]);
        }

        private static bool IsPointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
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
        #endregion

        #region Equality and Hash
        public override bool Equals(object obj)
        {
            return obj is UVIsland island && IslandID == island.IslandID;
        }

        public override int GetHashCode()
        {
            return IslandID.GetHashCode();
        }

        public override string ToString()
        {
            return $"UVIsland(ID:{IslandID}, Vertices:{VertexCount}, Triangles:{FaceCount}, Area:{UVArea:F4})";
        }
        #endregion
    }
}