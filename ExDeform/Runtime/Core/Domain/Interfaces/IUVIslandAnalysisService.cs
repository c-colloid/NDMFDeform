using System.Collections.Generic;
using UnityEngine;

namespace ExDeform.Runtime.Core.Domain.Interfaces
{
    /// <summary>
    /// Interface for UV island analysis service - enables dependency injection and testing
    /// UVアイランド解析サービスインターフェース - 依存性注入とテスト容易性を実現
    /// </summary>
    public interface IUVIslandAnalysisService
    {
        /// <summary>
        /// Analyze UV islands from mesh data
        /// </summary>
        UVIslandAnalysisService.AnalysisResult AnalyzeMesh(Mesh mesh);

        /// <summary>
        /// Find islands that contain specific UV coordinates
        /// </summary>
        List<UVIsland> FindIslandsContaining(IReadOnlyList<UVIsland> islands, Vector2 uvPoint, Mesh mesh);

        /// <summary>
        /// Find adjacent islands (islands that share UV space boundaries)
        /// </summary>
        Dictionary<UVIsland, List<UVIsland>> FindAdjacentIslands(IReadOnlyList<UVIsland> islands);

        /// <summary>
        /// Merge adjacent islands that meet criteria
        /// </summary>
        List<UVIsland> MergeAdjacentIslands(IReadOnlyList<UVIsland> islands, float mergeThreshold = 0.001f);

        /// <summary>
        /// Validate island data integrity
        /// </summary>
        ValidationResult ValidateIslands(IReadOnlyList<UVIsland> islands, Mesh mesh);
    }
}