using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;

namespace ExDeform.Runtime.Core.Domain
{
    /// <summary>
    /// Mask domain object - encapsulates masking logic without UI dependencies
    /// マスクドメインオブジェクト - UI依存を排除したマスキングロジック
    /// </summary>
    [System.Serializable]
    public class Mask
    {
        #region Core Properties
        public string Name { get; private set; }
        public MaskType Type { get; private set; }
        public float Strength { get; private set; }
        public bool IsInverted { get; private set; }
        public float FeatherRadius { get; private set; }
        public bool IsEnabled { get; private set; }
        #endregion

        #region Private Fields
        [SerializeField] private List<int> _targetIslandIDs = new List<int>();
        [SerializeField] private List<int> _targetVertexIndices = new List<int>();
        [SerializeField] private Dictionary<int, float> _vertexWeights = new Dictionary<int, float>();
        #endregion

        #region Domain Properties
        public IReadOnlyList<int> TargetIslandIDs => _targetIslandIDs.AsReadOnly();
        public IReadOnlyList<int> TargetVertexIndices => _targetVertexIndices.AsReadOnly();
        public IReadOnlyDictionary<int, float> VertexWeights => _vertexWeights.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        public bool HasTargets => _targetIslandIDs.Count > 0 || _targetVertexIndices.Count > 0;
        public bool IsValid => IsEnabled && HasTargets && Strength > 0f;
        #endregion

        #region Constructors
        public Mask(string name, MaskType type = MaskType.UVIsland)
        {
            Name = name ?? "Unnamed Mask";
            Type = type;
            Strength = 1.0f;
            IsInverted = false;
            FeatherRadius = 0.01f;
            IsEnabled = true;
        }

        public Mask(string name, MaskType type, float strength, bool inverted = false, float featherRadius = 0.01f) : this(name, type)
        {
            SetStrength(strength);
            SetInverted(inverted);
            SetFeatherRadius(featherRadius);
        }
        #endregion

        #region Configuration Methods
        public void SetStrength(float strength)
        {
            Strength = Mathf.Clamp01(strength);
        }

        public void SetInverted(bool inverted)
        {
            IsInverted = inverted;
        }

        public void SetFeatherRadius(float radius)
        {
            FeatherRadius = Mathf.Clamp(radius, 0f, 0.5f);
        }

        public void SetEnabled(bool enabled)
        {
            IsEnabled = enabled;
        }

        public void SetName(string name)
        {
            Name = name ?? "Unnamed Mask";
        }
        #endregion

        #region Target Management
        public void SetTargetIslands(IEnumerable<int> islandIDs)
        {
            _targetIslandIDs.Clear();
            if (islandIDs != null)
            {
                _targetIslandIDs.AddRange(islandIDs.Where(id => id >= 0));
            }
            InvalidateWeights();
        }

        public void AddTargetIsland(int islandID)
        {
            if (islandID >= 0 && !_targetIslandIDs.Contains(islandID))
            {
                _targetIslandIDs.Add(islandID);
                InvalidateWeights();
            }
        }

        public void RemoveTargetIsland(int islandID)
        {
            if (_targetIslandIDs.Remove(islandID))
            {
                InvalidateWeights();
            }
        }

        public void SetTargetVertices(IEnumerable<int> vertexIndices)
        {
            _targetVertexIndices.Clear();
            if (vertexIndices != null)
            {
                _targetVertexIndices.AddRange(vertexIndices.Where(idx => idx >= 0));
            }
            InvalidateWeights();
        }

        public void AddTargetVertex(int vertexIndex)
        {
            if (vertexIndex >= 0 && !_targetVertexIndices.Contains(vertexIndex))
            {
                _targetVertexIndices.Add(vertexIndex);
                InvalidateWeights();
            }
        }

        public void RemoveTargetVertex(int vertexIndex)
        {
            if (_targetVertexIndices.Remove(vertexIndex))
            {
                InvalidateWeights();
            }
        }

        public bool ContainsIsland(int islandID)
        {
            return _targetIslandIDs.Contains(islandID);
        }

        public bool ContainsVertex(int vertexIndex)
        {
            return _targetVertexIndices.Contains(vertexIndex);
        }
        #endregion

        #region Mask Application
        /// <summary>
        /// Calculate mask weight for a specific vertex
        /// </summary>
        public float GetVertexWeight(int vertexIndex, IReadOnlyList<UVIsland> islands = null)
        {
            if (!IsValid)
                return IsInverted ? 1f : 0f;

            // Check cached weights first
            if (_vertexWeights.ContainsKey(vertexIndex))
            {
                return _vertexWeights[vertexIndex];
            }

            float weight = CalculateBaseWeight(vertexIndex, islands);
            
            // Apply feathering if radius > 0
            if (FeatherRadius > 0f && islands != null)
            {
                weight = ApplyFeathering(vertexIndex, weight, islands);
            }

            // Apply inversion
            if (IsInverted)
            {
                weight = 1f - weight;
            }

            // Apply strength
            weight = Mathf.Lerp(IsInverted ? 1f : 0f, weight, Strength);

            // Cache the result
            _vertexWeights[vertexIndex] = weight;
            return weight;
        }

        /// <summary>
        /// Apply mask to a mesh data array
        /// </summary>
        public void ApplyToMesh(NativeArray<Vector3> vertices, NativeArray<Vector3> originalVertices, 
            IReadOnlyList<UVIsland> islands = null, float globalStrength = 1.0f)
        {
            if (!IsValid || vertices.Length != originalVertices.Length)
                return;

            for (int i = 0; i < vertices.Length; i++)
            {
                float weight = GetVertexWeight(i, islands);
                weight *= globalStrength;

                if (weight < 1.0f)
                {
                    // Blend between deformed and original vertex based on mask weight
                    vertices[i] = Vector3.Lerp(originalVertices[i], vertices[i], weight);
                }
            }
        }

        /// <summary>
        /// Generate mask weights for all vertices in a mesh
        /// </summary>
        public Dictionary<int, float> GenerateWeights(int vertexCount, IReadOnlyList<UVIsland> islands = null)
        {
            var weights = new Dictionary<int, float>();
            
            for (int i = 0; i < vertexCount; i++)
            {
                weights[i] = GetVertexWeight(i, islands);
            }

            return weights;
        }
        #endregion

        #region Validation and Analysis
        /// <summary>
        /// Validate mask configuration
        /// </summary>
        public ValidationResult Validate(IReadOnlyList<UVIsland> islands = null)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(Name))
            {
                result.AddError("Mask name cannot be empty");
            }

            if (Strength <= 0f)
            {
                result.AddError("Mask strength must be greater than 0");
            }

            if (!HasTargets)
            {
                result.AddWarning("No target islands or vertices specified");
            }

            if (Type == MaskType.UVIsland && islands != null)
            {
                var validIslandIDs = islands.Select(i => i.IslandID).ToHashSet();
                var invalidIDs = _targetIslandIDs.Where(id => !validIslandIDs.Contains(id)).ToList();
                
                if (invalidIDs.Any())
                {
                    result.AddWarning($"Target islands {string.Join(", ", invalidIDs)} do not exist");
                }
            }

            return result;
        }

        /// <summary>
        /// Get statistics about the mask
        /// </summary>
        public MaskStatistics GetStatistics(IReadOnlyList<UVIsland> islands = null)
        {
            var stats = new MaskStatistics
            {
                Name = Name,
                Type = Type,
                TargetIslandCount = _targetIslandIDs.Count,
                TargetVertexCount = _targetVertexIndices.Count,
                IsEnabled = IsEnabled,
                Strength = Strength,
                IsInverted = IsInverted,
                FeatherRadius = FeatherRadius
            };

            if (islands != null)
            {
                var targetIslands = islands.Where(i => _targetIslandIDs.Contains(i.IslandID)).ToList();
                stats.TotalUVArea = targetIslands.Sum(i => i.UVArea);
                stats.AverageIslandSize = targetIslands.Count > 0 ? (float)targetIslands.Average(i => i.VertexCount) : 0;
            }

            return stats;
        }
        #endregion

        #region Private Methods
        private float CalculateBaseWeight(int vertexIndex, IReadOnlyList<UVIsland> islands)
        {
            // Direct vertex targeting
            if (_targetVertexIndices.Contains(vertexIndex))
            {
                return 1f;
            }

            // Island-based targeting
            if (Type == MaskType.UVIsland && islands != null)
            {
                foreach (var island in islands)
                {
                    if (_targetIslandIDs.Contains(island.IslandID) && 
                        island.VertexIndices.Contains(vertexIndex))
                    {
                        return 1f;
                    }
                }
            }

            return 0f;
        }

        private float ApplyFeathering(int vertexIndex, float baseWeight, IReadOnlyList<UVIsland> islands)
        {
            if (baseWeight > 0f || islands == null)
                return baseWeight;

            // Find the closest target vertex/island and apply distance-based falloff
            float minDistance = float.MaxValue;

            // Check distance to target vertices
            foreach (int targetVertex in _targetVertexIndices)
            {
                // This would require UV coordinates to calculate proper distance
                // For now, use a simplified approach
                float distance = Mathf.Abs(vertexIndex - targetVertex) * 0.001f; // Placeholder
                minDistance = Mathf.Min(minDistance, distance);
            }

            // Check distance to target islands
            foreach (var island in islands)
            {
                if (_targetIslandIDs.Contains(island.IslandID))
                {
                    foreach (int islandVertex in island.VertexIndices)
                    {
                        float distance = Mathf.Abs(vertexIndex - islandVertex) * 0.001f; // Placeholder
                        minDistance = Mathf.Min(minDistance, distance);
                    }
                }
            }

            // Apply falloff within feather radius
            if (minDistance < FeatherRadius)
            {
                return 1f - (minDistance / FeatherRadius);
            }

            return 0f;
        }

        private void InvalidateWeights()
        {
            _vertexWeights.Clear();
        }
        #endregion

        #region Operators and Utility
        public static Mask Combine(Mask mask1, Mask mask2, MaskCombineMode mode)
        {
            if (mask1 == null) return mask2;
            if (mask2 == null) return mask1;

            var combinedMask = new Mask($"{mask1.Name}+{mask2.Name}", mask1.Type);
            
            switch (mode)
            {
                case MaskCombineMode.Union:
                    combinedMask._targetIslandIDs.AddRange(mask1._targetIslandIDs);
                    combinedMask._targetIslandIDs.AddRange(mask2._targetIslandIDs.Except(mask1._targetIslandIDs));
                    break;
                    
                case MaskCombineMode.Intersection:
                    combinedMask._targetIslandIDs.AddRange(mask1._targetIslandIDs.Intersect(mask2._targetIslandIDs));
                    break;
                    
                case MaskCombineMode.Difference:
                    combinedMask._targetIslandIDs.AddRange(mask1._targetIslandIDs.Except(mask2._targetIslandIDs));
                    break;
            }

            combinedMask.InvalidateWeights();
            return combinedMask;
        }

        public Mask Clone()
        {
            var clone = new Mask(Name, Type, Strength, IsInverted, FeatherRadius);
            clone._targetIslandIDs.AddRange(_targetIslandIDs);
            clone._targetVertexIndices.AddRange(_targetVertexIndices);
            clone.SetEnabled(IsEnabled);
            return clone;
        }
        #endregion

        #region Equality and Hash
        public override bool Equals(object obj)
        {
            return obj is Mask mask && Name == mask.Name && Type == mask.Type;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, Type);
        }

        public override string ToString()
        {
            return $"Mask({Name}, {Type}, Islands:{_targetIslandIDs.Count}, Vertices:{_targetVertexIndices.Count}, Strength:{Strength:F2})";
        }
        #endregion
    }

    #region Supporting Types
    /// <summary>
    /// Types of masking strategies
    /// </summary>
    public enum MaskType
    {
        UVIsland = 0,
        VertexGroup = 1,
        Gradient = 2,
        Texture = 3,
        Distance = 4
    }

    /// <summary>
    /// Modes for combining multiple masks
    /// </summary>
    public enum MaskCombineMode
    {
        Union = 0,
        Intersection = 1,
        Difference = 2
    }

    /// <summary>
    /// Validation result for mask configuration
    /// </summary>
    public class ValidationResult
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0;
        
        public void AddError(string error) => Errors.Add(error);
        public void AddWarning(string warning) => Warnings.Add(warning);
    }

    /// <summary>
    /// Statistics about a mask's configuration and impact
    /// </summary>
    public class MaskStatistics
    {
        public string Name { get; set; }
        public MaskType Type { get; set; }
        public int TargetIslandCount { get; set; }
        public int TargetVertexCount { get; set; }
        public bool IsEnabled { get; set; }
        public float Strength { get; set; }
        public bool IsInverted { get; set; }
        public float FeatherRadius { get; set; }
        public float TotalUVArea { get; set; }
        public float AverageIslandSize { get; set; }

        public override string ToString()
        {
            return $"MaskStats({Name}: Islands={TargetIslandCount}, Vertices={TargetVertexCount}, Area={TotalUVArea:F4})";
        }
    }
    #endregion
}