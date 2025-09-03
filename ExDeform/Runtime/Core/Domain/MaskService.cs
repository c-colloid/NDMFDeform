using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using ExDeform.Runtime.Core.Domain.Interfaces;

namespace ExDeform.Runtime.Core.Domain
{
    /// <summary>
    /// Domain service for mask operations - centralized mask management and application logic
    /// マスクドメインサービス - マスク管理と適用ロジックの中央化
    /// </summary>
    public class MaskService : IMaskService
    {
        #region Fields
        private readonly List<Mask> _masks = new List<Mask>();
        private readonly Dictionary<string, Mask> _masksByName = new Dictionary<string, Mask>();
        private readonly Dictionary<Mask, Dictionary<int, float>> _weightCache = new Dictionary<Mask, Dictionary<int, float>>();
        private bool _isDirty = false;
        #endregion

        #region Properties
        public IReadOnlyList<Mask> Masks => _masks.AsReadOnly();
        public int MaskCount => _masks.Count;
        public bool HasMasks => _masks.Count > 0;
        public bool HasActiveMasks => _masks.Any(m => m.IsValid);
        #endregion

        #region Events
        public event Action<Mask> MaskAdded;
        public event Action<Mask> MaskRemoved;
        public event Action<Mask> MaskModified;
        public event Action MasksCleared;
        #endregion

        #region Mask Management
        /// <summary>
        /// Add a mask to the service
        /// </summary>
        public bool AddMask(Mask mask)
        {
            if (mask == null)
                throw new ArgumentNullException(nameof(mask));

            if (_masksByName.ContainsKey(mask.Name))
                return false;

            _masks.Add(mask);
            _masksByName[mask.Name] = mask;
            _isDirty = true;
            
            MaskAdded?.Invoke(mask);
            return true;
        }

        /// <summary>
        /// Remove a mask by reference
        /// </summary>
        public bool RemoveMask(Mask mask)
        {
            if (mask == null || !_masks.Contains(mask))
                return false;

            _masks.Remove(mask);
            _masksByName.Remove(mask.Name);
            _weightCache.Remove(mask);
            _isDirty = true;

            MaskRemoved?.Invoke(mask);
            return true;
        }

        /// <summary>
        /// Remove a mask by name
        /// </summary>
        public bool RemoveMask(string maskName)
        {
            if (string.IsNullOrEmpty(maskName) || !_masksByName.ContainsKey(maskName))
                return false;

            var mask = _masksByName[maskName];
            return RemoveMask(mask);
        }

        /// <summary>
        /// Get mask by name
        /// </summary>
        public Mask GetMask(string maskName)
        {
            _masksByName.TryGetValue(maskName, out var mask);
            return mask;
        }

        /// <summary>
        /// Check if mask exists
        /// </summary>
        public bool HasMask(string maskName)
        {
            return _masksByName.ContainsKey(maskName);
        }

        /// <summary>
        /// Clear all masks
        /// </summary>
        public void ClearMasks()
        {
            _masks.Clear();
            _masksByName.Clear();
            _weightCache.Clear();
            _isDirty = true;

            MasksCleared?.Invoke();
        }

        /// <summary>
        /// Update mask (triggers recalculation)
        /// </summary>
        public void NotifyMaskModified(Mask mask)
        {
            if (mask != null && _masks.Contains(mask))
            {
                _weightCache.Remove(mask);
                _isDirty = true;
                MaskModified?.Invoke(mask);
            }
        }
        #endregion

        #region Mask Application
        /// <summary>
        /// Apply all active masks to mesh vertices
        /// </summary>
        public void ApplyMasks(NativeArray<Vector3> vertices, NativeArray<Vector3> originalVertices, 
            IReadOnlyList<UVIsland> islands = null, float globalStrength = 1.0f)
        {
            if (vertices.Length != originalVertices.Length)
                throw new ArgumentException("Vertex arrays must have the same length");

            if (!HasActiveMasks)
                return;

            var activeMasks = GetActiveMasks();
            if (activeMasks.Count == 0)
                return;

            // Apply masks in order
            foreach (var mask in activeMasks)
            {
                mask.ApplyToMesh(vertices, originalVertices, islands, globalStrength);
            }
        }

        /// <summary>
        /// Calculate combined mask weights for all vertices
        /// </summary>
        public Dictionary<int, float> CalculateCombinedWeights(int vertexCount, IReadOnlyList<UVIsland> islands = null, MaskCombineMode combineMode = MaskCombineMode.Union)
        {
            var combinedWeights = new Dictionary<int, float>();
            var activeMasks = GetActiveMasks();

            if (activeMasks.Count == 0)
            {
                // Default weights (no masking)
                for (int i = 0; i < vertexCount; i++)
                {
                    combinedWeights[i] = 1.0f;
                }
                return combinedWeights;
            }

            // Initialize with first mask
            var firstMask = activeMasks[0];
            var firstWeights = GetOrCalculateWeights(firstMask, vertexCount, islands);
            
            foreach (var kvp in firstWeights)
            {
                combinedWeights[kvp.Key] = kvp.Value;
            }

            // Combine with remaining masks
            for (int i = 1; i < activeMasks.Count; i++)
            {
                var maskWeights = GetOrCalculateWeights(activeMasks[i], vertexCount, islands);
                CombineWeights(combinedWeights, maskWeights, combineMode);
            }

            return combinedWeights;
        }

        /// <summary>
        /// Calculate weights for a specific mask
        /// </summary>
        public Dictionary<int, float> CalculateWeights(Mask mask, int vertexCount, IReadOnlyList<UVIsland> islands = null)
        {
            if (mask == null || !mask.IsValid)
                return new Dictionary<int, float>();

            return GetOrCalculateWeights(mask, vertexCount, islands);
        }

        /// <summary>
        /// Get effective strength at a specific vertex
        /// </summary>
        public float GetVertexStrength(int vertexIndex, IReadOnlyList<UVIsland> islands = null, MaskCombineMode combineMode = MaskCombineMode.Union)
        {
            var activeMasks = GetActiveMasks();
            if (activeMasks.Count == 0)
                return 1.0f;

            float combinedStrength = activeMasks[0].GetVertexWeight(vertexIndex, islands);

            for (int i = 1; i < activeMasks.Count; i++)
            {
                float maskStrength = activeMasks[i].GetVertexWeight(vertexIndex, islands);
                combinedStrength = CombineValues(combinedStrength, maskStrength, combineMode);
            }

            return combinedStrength;
        }
        #endregion

        #region Mask Analysis
        /// <summary>
        /// Validate all masks against current island data
        /// </summary>
        public MaskValidationSummary ValidateAllMasks(IReadOnlyList<UVIsland> islands = null)
        {
            var summary = new MaskValidationSummary();

            foreach (var mask in _masks)
            {
                var result = mask.Validate(islands);
                summary.Results[mask.Name] = result;
                
                if (!result.IsValid)
                {
                    summary.InvalidMasks.Add(mask);
                }
                
                summary.TotalErrors += result.Errors.Count;
                summary.TotalWarnings += result.Warnings.Count;
            }

            summary.TotalMasks = _masks.Count;
            summary.ValidMasks = _masks.Count - summary.InvalidMasks.Count;

            return summary;
        }

        /// <summary>
        /// Get comprehensive statistics for all masks
        /// </summary>
        public MaskServiceStatistics GetStatistics(IReadOnlyList<UVIsland> islands = null)
        {
            var stats = new MaskServiceStatistics
            {
                TotalMasks = _masks.Count,
                ActiveMasks = GetActiveMasks().Count,
                EnabledMasks = _masks.Count(m => m.IsEnabled),
                MaskTypes = _masks.GroupBy(m => m.Type).ToDictionary(g => g.Key, g => g.Count())
            };

            if (islands != null)
            {
                var totalTargetedIslands = _masks.SelectMany(m => m.TargetIslandIDs).Distinct().Count();
                var totalTargetedVertices = _masks.SelectMany(m => m.TargetVertexIndices).Distinct().Count();

                stats.TotalTargetedIslands = totalTargetedIslands;
                stats.TotalTargetedVertices = totalTargetedVertices;
                stats.IslandCoverage = islands.Count > 0 ? (float)totalTargetedIslands / islands.Count : 0f;
            }

            stats.MaskStatistics = _masks.Select(m => m.GetStatistics(islands)).ToList();
            return stats;
        }

        /// <summary>
        /// Find masks affecting a specific island
        /// </summary>
        public List<Mask> FindMasksAffectingIsland(int islandID)
        {
            return _masks.Where(m => m.ContainsIsland(islandID)).ToList();
        }

        /// <summary>
        /// Find masks affecting a specific vertex
        /// </summary>
        public List<Mask> FindMasksAffectingVertex(int vertexIndex)
        {
            return _masks.Where(m => m.ContainsVertex(vertexIndex)).ToList();
        }
        #endregion

        #region Optimization
        /// <summary>
        /// Clear cached weights (call when mesh or islands change)
        /// </summary>
        public void InvalidateCache()
        {
            _weightCache.Clear();
            _isDirty = true;
        }

        /// <summary>
        /// Optimize mask order for performance
        /// </summary>
        public void OptimizeMaskOrder()
        {
            // Sort by complexity (simple masks first)
            _masks.Sort((m1, m2) =>
            {
                int complexity1 = CalculateMaskComplexity(m1);
                int complexity2 = CalculateMaskComplexity(m2);
                return complexity1.CompareTo(complexity2);
            });

            _isDirty = true;
        }

        /// <summary>
        /// Remove redundant or ineffective masks
        /// </summary>
        public int RemoveIneffectiveMasks()
        {
            var toRemove = new List<Mask>();

            foreach (var mask in _masks)
            {
                if (!mask.IsEnabled || mask.Strength <= 0f || !mask.HasTargets)
                {
                    toRemove.Add(mask);
                }
            }

            foreach (var mask in toRemove)
            {
                RemoveMask(mask);
            }

            return toRemove.Count;
        }
        #endregion

        #region Private Methods
        private List<Mask> GetActiveMasks()
        {
            return _masks.Where(m => m.IsValid).ToList();
        }

        private Dictionary<int, float> GetOrCalculateWeights(Mask mask, int vertexCount, IReadOnlyList<UVIsland> islands)
        {
            if (_weightCache.ContainsKey(mask))
            {
                return _weightCache[mask];
            }

            var weights = mask.GenerateWeights(vertexCount, islands);
            _weightCache[mask] = weights;
            return weights;
        }

        private void CombineWeights(Dictionary<int, float> baseWeights, Dictionary<int, float> newWeights, MaskCombineMode combineMode)
        {
            foreach (var kvp in newWeights)
            {
                int vertex = kvp.Key;
                float newWeight = kvp.Value;
                
                if (baseWeights.ContainsKey(vertex))
                {
                    baseWeights[vertex] = CombineValues(baseWeights[vertex], newWeight, combineMode);
                }
                else
                {
                    baseWeights[vertex] = combineMode == MaskCombineMode.Intersection ? 0f : newWeight;
                }
            }
        }

        private float CombineValues(float value1, float value2, MaskCombineMode combineMode)
        {
            return combineMode switch
            {
                MaskCombineMode.Union => Mathf.Max(value1, value2),
                MaskCombineMode.Intersection => Mathf.Min(value1, value2),
                MaskCombineMode.Difference => Mathf.Max(0f, value1 - value2),
                _ => Mathf.Max(value1, value2)
            };
        }

        private int CalculateMaskComplexity(Mask mask)
        {
            return mask.TargetIslandIDs.Count + mask.TargetVertexIndices.Count + (mask.FeatherRadius > 0 ? 10 : 0);
        }
        #endregion

        #region Result Types
        /// <summary>
        /// Summary of mask validation results
        /// </summary>
        public class MaskValidationSummary
        {
            public Dictionary<string, ValidationResult> Results { get; } = new Dictionary<string, ValidationResult>();
            public List<Mask> InvalidMasks { get; } = new List<Mask>();
            public int TotalMasks { get; set; }
            public int ValidMasks { get; set; }
            public int TotalErrors { get; set; }
            public int TotalWarnings { get; set; }

            public bool IsAllValid => InvalidMasks.Count == 0;
            public float ValidationRate => TotalMasks > 0 ? (float)ValidMasks / TotalMasks : 1f;
        }

        /// <summary>
        /// Comprehensive statistics about the mask service state
        /// </summary>
        public class MaskServiceStatistics
        {
            public int TotalMasks { get; set; }
            public int ActiveMasks { get; set; }
            public int EnabledMasks { get; set; }
            public Dictionary<MaskType, int> MaskTypes { get; set; } = new Dictionary<MaskType, int>();
            public int TotalTargetedIslands { get; set; }
            public int TotalTargetedVertices { get; set; }
            public float IslandCoverage { get; set; }
            public List<MaskStatistics> MaskStatistics { get; set; } = new List<MaskStatistics>();

            public override string ToString()
            {
                return $"MaskServiceStats(Total={TotalMasks}, Active={ActiveMasks}, Coverage={IslandCoverage:P1})";
            }
        }
        #endregion
    }
}