using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

namespace ExDeform.Runtime.Core.Domain.Interfaces
{
    /// <summary>
    /// Interface for mask service - enables dependency injection and testing
    /// マスクサービスインターフェース - 依存性注入とテスト容易性を実現
    /// </summary>
    public interface IMaskService
    {
        #region Properties
        IReadOnlyList<Mask> Masks { get; }
        int MaskCount { get; }
        bool HasMasks { get; }
        bool HasActiveMasks { get; }
        #endregion

        #region Events
        event Action<Mask> MaskAdded;
        event Action<Mask> MaskRemoved;
        event Action<Mask> MaskModified;
        event Action MasksCleared;
        #endregion

        #region Mask Management
        bool AddMask(Mask mask);
        bool RemoveMask(Mask mask);
        bool RemoveMask(string maskName);
        Mask GetMask(string maskName);
        bool HasMask(string maskName);
        void ClearMasks();
        void NotifyMaskModified(Mask mask);
        #endregion

        #region Mask Application
        void ApplyMasks(NativeArray<Vector3> vertices, NativeArray<Vector3> originalVertices, 
            IReadOnlyList<UVIsland> islands = null, float globalStrength = 1.0f);
        
        Dictionary<int, float> CalculateCombinedWeights(int vertexCount, IReadOnlyList<UVIsland> islands = null, 
            MaskCombineMode combineMode = MaskCombineMode.Union);
        
        Dictionary<int, float> CalculateWeights(Mask mask, int vertexCount, IReadOnlyList<UVIsland> islands = null);
        
        float GetVertexStrength(int vertexIndex, IReadOnlyList<UVIsland> islands = null, 
            MaskCombineMode combineMode = MaskCombineMode.Union);
        #endregion

        #region Analysis
        MaskService.MaskValidationSummary ValidateAllMasks(IReadOnlyList<UVIsland> islands = null);
        MaskService.MaskServiceStatistics GetStatistics(IReadOnlyList<UVIsland> islands = null);
        List<Mask> FindMasksAffectingIsland(int islandID);
        List<Mask> FindMasksAffectingVertex(int vertexIndex);
        #endregion

        #region Optimization
        void InvalidateCache();
        void OptimizeMaskOrder();
        int RemoveIneffectiveMasks();
        #endregion
    }
}