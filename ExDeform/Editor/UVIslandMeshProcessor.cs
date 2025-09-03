using UnityEngine;
using UnityEditor;
using ExDeform.Runtime.Deformers;

#if EXDEFORM_DEFORM_AVAILABLE
using Deform;
#endif

namespace ExDeform.Editor
{
    /// <summary>
    /// Mesh processing and analysis for UV Island operations
    /// UVアイランド操作のためのメッシュ処理と解析
    /// </summary>
    public static class UVIslandMeshProcessor
    {
        #region Constants
        
        private const int UV_MAP_SIZE = 300;
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Get mesh data from target mask with fallback options
        /// フォールバックオプション付きでターゲットマスクからメッシュデータを取得
        /// </summary>
        public static Mesh GetMeshData(UVIslandMask targetMask)
        {
            // Try to get mesh from Deformable component first
            if (targetMask?.CachedMesh != null)
            {
                var mesh = targetMask.CachedMesh;
                if (mesh != null)
                {
                    return mesh;
                }
            }
            
            // Fallback: try to get original mesh from mesh sources
            var meshFilter = targetMask.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return meshFilter.sharedMesh;
            }
            
            var skinnedMeshRenderer = targetMask.GetComponent<SkinnedMeshRenderer>();
            if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
            {
                return skinnedMeshRenderer.sharedMesh;
            }
            
            return null;
        }
        
        /// <summary>
        /// Get original mesh data for UV mapping and caching
        /// UVマッピングとキャッシングのための元のメッシュデータを取得
        /// </summary>
        public static Mesh GetOriginalMesh(UVIslandMask targetMask)
        {
            // Try to get mesh from Deformable component first
            if (targetMask?.OriginalMesh != null)
            {
                var mesh = targetMask.OriginalMesh;
                if (mesh != null)
                {
                    return mesh;
                }
            }
            
            // Always get the original mesh for UV mapping and caching
            var meshFilter = targetMask.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return meshFilter.sharedMesh;
            }
            
            var skinnedMeshRenderer = targetMask.GetComponent<SkinnedMeshRenderer>();
            if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
            {
                return skinnedMeshRenderer.sharedMesh;
            }
            
            return null;
        }
        
        /// <summary>
        /// Get dynamic mesh for highlighting in scene view
        /// シーンビューでのハイライト用の動的メッシュを取得
        /// </summary>
        public static Mesh GetDynamicMesh(UVIslandMask targetMask)
        {
            // Get the current dynamic mesh for highlighting
            if (targetMask?.CachedMesh != null)
            {
                return targetMask.CachedMesh;
            }
            
            // Fallback to original mesh if dynamic mesh is not available
            return GetOriginalMesh(targetMask);
        }
        
        /// <summary>
        /// Get renderer transform with caching
        /// キャッシュ付きでレンダラートランスフォームを取得
        /// </summary>
        public static Transform GetRendererTransform(UVIslandMask targetMask)
        {
            // Try to get cached renderer transform from the UVIslandMask component
            if (targetMask?.CachedRendererTransform != null)
            {
                return targetMask.CachedRendererTransform;
            }
            
            // Update renderer cache if not available
            targetMask?.UpdateRendererCache();
            
            // Return the updated cached transform
            return targetMask?.CachedRendererTransform;
        }
        
        /// <summary>
        /// Convert local UI position to UV coordinates with proper transformation
        /// 適切な変換を使用してローカルUI位置をUV座標に変換
        /// </summary>
        public static Vector2 LocalPosToUV(Vector2 localPos, UVIslandSelector selector)
        {
            if (selector == null) return Vector2.zero;
            
            var normalizedPos = new Vector2(
                localPos.x / UV_MAP_SIZE,
                1f - (localPos.y / UV_MAP_SIZE)
            );
            
            var transform = selector.CalculateUVTransformMatrix();
            var inverseTransform = transform.inverse;
            var actualUV = inverseTransform.MultiplyPoint3x4(new Vector3(normalizedPos.x, normalizedPos.y, 0f));
            
            return new Vector2(actualUV.x, actualUV.y);
        }
        
        /// <summary>
        /// Handle island selection logic at given local position
        /// 指定されたローカル位置でアイランド選択ロジックを処理
        /// </summary>
        public static SelectionResult HandleIslandSelection(Vector2 localPosition, UVIslandSelector selector, UVIslandMask targetMask)
        {
            if (selector == null || targetMask == null)
            {
                return new SelectionResult { success = false, isDragging = false };
            }
            
            // Use proper coordinate transformation that accounts for zoom and pan
            var uvCoordinate = LocalPosToUV(localPosition, selector);
            
            int islandID = selector.GetIslandAtUVCoordinate(uvCoordinate);
            
            if (islandID >= 0)
            {
                Undo.RecordObject(targetMask, "Toggle UV Island Selection");
                selector.ToggleIslandSelection(islandID);
                targetMask.SetSelectedIslands(selector.SelectedIslandIDs);
                EditorUtility.SetDirty(targetMask);
                
                return new SelectionResult { success = true, isDragging = false, selectedIslandID = islandID };
            }
            else
            {
                return new SelectionResult { success = false, isDragging = true };
            }
        }
        
        /// <summary>
        /// Initialize UV island selector with proper configuration
        /// 適切な設定でUVアイランドセレクタを初期化
        /// </summary>
        public static UVIslandSelector InitializeSelector(Mesh originalMesh, UVIslandMask targetMask)
        {
            if (originalMesh == null || targetMask == null) return null;
            
            var selector = new UVIslandSelector(originalMesh);
            selector.SetSelectedIslands(targetMask.SelectedIslandIDs);
            selector.TargetTransform = GetRendererTransform(targetMask);
            
            return selector;
        }
        
        /// <summary>
        /// Update selector configuration based on UI settings
        /// UI設定に基づいてセレクタ設定を更新
        /// </summary>
        public static void UpdateSelectorConfiguration(UVIslandSelector selector, SelectorConfiguration config)
        {
            if (selector == null) return;
            
            selector.UseAdaptiveVertexSize = config.useAdaptiveVertexSize;
            selector.ManualVertexSphereSize = config.manualVertexSize;
            selector.AdaptiveSizeMultiplier = config.adaptiveMultiplier;
            selector.AutoUpdatePreview = config.autoUpdate;
            selector.EnableMagnifyingGlass = config.enableMagnifying;
            selector.MagnifyingGlassZoom = config.magnifyingZoom;
            selector.MagnifyingGlassSize = config.magnifyingSize;
        }
        
        #endregion
        
        #region Data Structures
        
        /// <summary>
        /// Result of island selection operation
        /// アイランド選択操作の結果
        /// </summary>
        public struct SelectionResult
        {
            public bool success;
            public bool isDragging;
            public int selectedIslandID;
        }
        
        /// <summary>
        /// Configuration for UV island selector
        /// UVアイランドセレクタの設定
        /// </summary>
        public struct SelectorConfiguration
        {
            public bool useAdaptiveVertexSize;
            public float manualVertexSize;
            public float adaptiveMultiplier;
            public bool autoUpdate;
            public bool enableMagnifying;
            public float magnifyingZoom;
            public float magnifyingSize;
        }
        
        #endregion
    }
}