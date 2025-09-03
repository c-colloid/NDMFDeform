using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace ExDeform.Runtime.Core.Domain.Interfaces
{
    /// <summary>
    /// Repository interface for UV island data persistence and caching
    /// UVアイランドデータの永続化とキャッシュのリポジトリインターフェース
    /// </summary>
    public interface IUVIslandRepository
    {
        /// <summary>
        /// Get cached islands for a mesh
        /// </summary>
        Task<List<UVIsland>> GetIslandsAsync(Mesh mesh);

        /// <summary>
        /// Save islands for a mesh
        /// </summary>
        Task SaveIslandsAsync(Mesh mesh, IReadOnlyList<UVIsland> islands);

        /// <summary>
        /// Check if islands are cached for a mesh
        /// </summary>
        Task<bool> HasCachedIslandsAsync(Mesh mesh);

        /// <summary>
        /// Clear cached islands for a mesh
        /// </summary>
        Task ClearIslandsAsync(Mesh mesh);

        /// <summary>
        /// Clear all cached islands
        /// </summary>
        Task ClearAllAsync();

        /// <summary>
        /// Get cache statistics
        /// </summary>
        Task<CacheStatistics> GetCacheStatisticsAsync();
    }

    /// <summary>
    /// Cache statistics for the repository
    /// </summary>
    public class CacheStatistics
    {
        public int CachedMeshes { get; set; }
        public int TotalIslands { get; set; }
        public long TotalCacheSize { get; set; }
        public System.DateTime LastAccess { get; set; }
        public System.TimeSpan TotalAnalysisTimeSaved { get; set; }
        
        public override string ToString()
        {
            return $"CacheStats(Meshes={CachedMeshes}, Islands={TotalIslands}, Size={TotalCacheSize} bytes)";
        }
    }
}