using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using ExDeform.Core.Interfaces;
using ExDeform.Runtime.Core.Domain.Interfaces;

namespace ExDeform.Runtime.Core.Domain
{
    /// <summary>
    /// Domain-driven UV Island Mask Deformer - business logic separated from UI concerns
    /// ドメイン駆動UVアイランドマスクデフォーマー - ビジネスロジックをUI関心事から分離
    /// </summary>
    public class UVIslandMaskDeformer : IExDeformer
    {
        #region IExDeformer Implementation
        public override string DeformerName => "UV Island Mask (Domain)";
        public override DeformerCategory Category => DeformerCategory.Mask;
        public override string Description => "Domain-driven UV island masking with improved testability";
        public override System.Version CompatibleDeformVersion => new System.Version(1, 0, 0);
        public override bool IsVisibleInEditor => _configuration.IsVisibleInEditor;
        public override bool IsEnabledInRuntime => _configuration.IsEnabled && IsValidConfiguration();
        #endregion

        #region Fields
        private readonly IUVIslandAnalysisService _analysisService;
        private readonly IMaskService _maskService;
        private readonly IUVIslandRepository _repository;
        private readonly DeformerConfiguration _configuration;

        // Domain objects
        private List<UVIsland> _islands = new List<UVIsland>();
        private Mesh _currentMesh;
        private bool _isInitialized = false;
        private object _externalDeformable;
        #endregion

        #region Constructor
        public UVIslandMaskDeformer(
            IUVIslandAnalysisService analysisService = null,
            IMaskService maskService = null,
            IUVIslandRepository repository = null,
            DeformerConfiguration configuration = null)
        {
            _analysisService = analysisService ?? new UVIslandAnalysisService();
            _maskService = maskService ?? new MaskService();
            _repository = repository; // Optional - null means no caching
            _configuration = configuration ?? DeformerConfiguration.Default;

            InitializeDomainServices();
        }
        #endregion

        #region Properties
        public IReadOnlyList<UVIsland> Islands => _islands.AsReadOnly();
        public IReadOnlyList<Mask> Masks => _maskService.Masks;
        public DeformerConfiguration Configuration => _configuration;
        public bool IsInitialized => _isInitialized;
        #endregion

        #region IExDeformer Implementation
        public override bool Initialize(object deformable)
        {
            _externalDeformable = deformable;
            _isInitialized = true;
            return IsValidConfiguration();
        }

	    public override JobHandle ProcessMesh(Deform.MeshData meshData, JobHandle dependency)
        {
            if (!_isInitialized || meshData == null)
                return dependency;

            // Extract mesh from external data
            var mesh = ExtractMeshFromData(meshData);
            if (mesh == null)
                return dependency;

            // Update islands if mesh changed
            if (_currentMesh != mesh)
            {
                _ = UpdateIslandsAsync(mesh); // Fire and forget for now
                _currentMesh = mesh;
            }

            // Apply masks if we have valid data
            if (_islands.Count > 0 && _maskService.HasActiveMasks)
            {
                return CreateMaskApplicationJob(meshData, dependency);
            }

            return dependency;
        }

        public override void Cleanup()
        {
            _islands.Clear();
            _maskService.ClearMasks();
            _currentMesh = null;
            _isInitialized = false;
        }
        #endregion

        #region Public API
        /// <summary>
        /// Add a mask targeting specific islands
        /// </summary>
        public Mask AddIslandMask(string maskName, IEnumerable<int> islandIDs, float strength = 1.0f, bool inverted = false)
        {
            var mask = new Mask(maskName, MaskType.UVIsland, strength, inverted);
            mask.SetTargetIslands(islandIDs);
            
            if (_maskService.AddMask(mask))
            {
                return mask;
            }
            
            return null;
        }

        /// <summary>
        /// Add a mask targeting specific vertices
        /// </summary>
        public Mask AddVertexMask(string maskName, IEnumerable<int> vertexIndices, float strength = 1.0f, bool inverted = false)
        {
            var mask = new Mask(maskName, MaskType.VertexGroup, strength, inverted);
            mask.SetTargetVertices(vertexIndices);
            
            if (_maskService.AddMask(mask))
            {
                return mask;
            }
            
            return null;
        }

        /// <summary>
        /// Get mask by name
        /// </summary>
        public Mask GetMask(string maskName) => _maskService.GetMask(maskName);

        /// <summary>
        /// Remove mask
        /// </summary>
        public bool RemoveMask(string maskName) => _maskService.RemoveMask(maskName);

        /// <summary>
        /// Find islands containing a UV point
        /// </summary>
        public List<UVIsland> FindIslandsAt(Vector2 uvPoint)
        {
            if (_currentMesh == null) return new List<UVIsland>();
            return _analysisService.FindIslandsContaining(_islands, uvPoint, _currentMesh);
        }

        /// <summary>
        /// Get comprehensive statistics
        /// </summary>
        public DeformerStatistics GetStatistics()
        {
            var maskStats = _maskService.GetStatistics(_islands);
            var islandStats = CalculateIslandStatistics();

            return new DeformerStatistics
            {
                IslandCount = _islands.Count,
                MaskCount = _maskService.MaskCount,
                ActiveMaskCount = maskStats.ActiveMasks,
                TotalUVArea = _islands.Sum(i => i.UVArea),
                AverageIslandSize = islandStats.AverageSize,
                LargestIslandSize = islandStats.LargestSize,
                SmallestIslandSize = islandStats.SmallestSize,
                MaskCoverage = maskStats.IslandCoverage,
                IsValid = IsValidConfiguration()
            };
        }

        /// <summary>
        /// Validate current configuration
        /// </summary>
        public ValidationResult ValidateConfiguration()
        {
            var result = new ValidationResult();

            // Validate islands
            if (_islands.Count == 0)
            {
                result.AddWarning("No UV islands analyzed");
            }

            // Validate masks
            var maskValidation = _maskService.ValidateAllMasks(_islands);
            foreach (var kvp in maskValidation.Results)
            {
                foreach (var error in kvp.Value.Errors)
                {
                    result.AddError($"Mask '{kvp.Key}': {error}");
                }
                foreach (var warning in kvp.Value.Warnings)
                {
                    result.AddWarning($"Mask '{kvp.Key}': {warning}");
                }
            }

            // Validate configuration
            if (_configuration.Strength <= 0f)
            {
                result.AddError("Deformer strength must be greater than 0");
            }

            return result;
        }
        #endregion

        #region Private Methods
        private void InitializeDomainServices()
        {
            // Subscribe to mask service events for cache invalidation
            _maskService.MaskAdded += OnMaskChanged;
            _maskService.MaskRemoved += OnMaskChanged;
            _maskService.MaskModified += OnMaskChanged;
        }

        private void OnMaskChanged(Mask mask)
        {
            // Invalidate cached computations when masks change
            _maskService.InvalidateCache();
        }

        private async System.Threading.Tasks.Task UpdateIslandsAsync(Mesh mesh)
        {
            try
            {
                // Try to get from cache first
                if (_repository != null && await _repository.HasCachedIslandsAsync(mesh))
                {
                    _islands = await _repository.GetIslandsAsync(mesh);
                    return;
                }

                // Analyze mesh
                var analysisResult = _analysisService.AnalyzeMesh(mesh);
                if (analysisResult.IsSuccess)
                {
                    _islands = analysisResult.Islands;
                    
                    // Cache results if repository available
                    if (_repository != null)
                    {
                        await _repository.SaveIslandsAsync(mesh, _islands);
                    }
                }
                else
                {
                    Debug.LogError($"UV island analysis failed: {analysisResult.ErrorMessage}");
                    _islands.Clear();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to update UV islands: {ex.Message}");
                _islands.Clear();
            }
        }

        private Mesh ExtractMeshFromData(object meshData)
        {
            // This would need to be implemented based on the external Deform system's mesh data format
            // For now, return null to indicate we need mesh data extraction logic
            return null;
        }

        private JobHandle CreateMaskApplicationJob(object meshData, JobHandle dependency)
        {
            // This would create a Unity Job to apply the masks
            // The job would use the domain objects to calculate vertex weights and apply them
            // For now, return the dependency unchanged
            return dependency;
        }

        private bool IsValidConfiguration()
        {
            return _configuration.IsEnabled && 
                   _configuration.Strength > 0f &&
                   (_maskService.HasActiveMasks || _configuration.AllowEmptyMasks);
        }

        private IslandStatistics CalculateIslandStatistics()
        {
            if (_islands.Count == 0)
            {
                return new IslandStatistics();
            }

            var vertexCounts = _islands.Select(i => i.VertexCount).ToList();
            return new IslandStatistics
            {
                AverageSize = (float)vertexCounts.Average(),
                LargestSize = vertexCounts.Max(),
                SmallestSize = vertexCounts.Min()
            };
        }
        #endregion

        #region Configuration and Statistics Types
        [System.Serializable]
        public class DeformerConfiguration
        {
            public bool IsEnabled = true;
            public float Strength = 1.0f;
            public bool IsVisibleInEditor = true;
            public bool AllowEmptyMasks = false;
            public bool EnableCaching = true;
            public bool UseOptimizedAnalysis = true;

            public static DeformerConfiguration Default => new DeformerConfiguration();
        }

        public class DeformerStatistics
        {
            public int IslandCount { get; set; }
            public int MaskCount { get; set; }
            public int ActiveMaskCount { get; set; }
            public float TotalUVArea { get; set; }
            public float AverageIslandSize { get; set; }
            public int LargestIslandSize { get; set; }
            public int SmallestIslandSize { get; set; }
            public float MaskCoverage { get; set; }
            public bool IsValid { get; set; }

            public override string ToString()
            {
                return $"DeformerStats(Islands={IslandCount}, Masks={ActiveMaskCount}/{MaskCount}, Coverage={MaskCoverage:P1})";
            }
        }

        private class IslandStatistics
        {
            public float AverageSize { get; set; }
            public int LargestSize { get; set; }
            public int SmallestSize { get; set; }
        }
        #endregion
    }
}