using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;
using Deform.Masking.Editor;
using Deform.Masking;

namespace DeformEditor.Masking
{
    /// <summary>
    /// Async Initialization partial class for UVIslandMaskEditor
    /// Contains asynchronous initialization and progress monitoring logic
    /// </summary>
    public partial class UVIslandMaskEditor
    {
        #region Async Initialization
        // 非同期初期化
        // Async initialization and progress monitoring

        /// <summary>
        /// Show placeholder message while UV map is being initialized
        /// UV マップ初期化中のプレースホルダーメッセージを表示
        /// </summary>
        private void ShowPlaceholderMessage(string message)
        {
            // Update status label
            if (statusLabel != null)
            {
                statusLabel.text = message;
            }

            // Show placeholder in UV map area
            if (uvMapImage != null)
            {
                // Set a neutral gray background to indicate loading state
                uvMapImage.style.backgroundColor = new StyleColor(new Color(0.22f, 0.22f, 0.22f, 1f));
                // Image will be replaced when texture loads, no need to explicitly clear
            }

            // Clear island list during initialization
            if (islandListView != null)
            {
                islandListView.itemsSource = null;
                islandListView.Rebuild();
            }
        }

        // ShowProgressUI and HideProgressUI methods removed - replaced with InitializationProgressView

        /// <summary>
        /// Monitor async initialization progress and update UI elements
        /// 非同期初期化の進捗を監視し、UI要素を更新
        /// </summary>
        private void MonitorAsyncInitialization()
        {
            if (asyncInitManager == null || !asyncInitManager.IsRunning)
            {
                // Stop monitoring when initialization is not running
                EditorApplication.update -= MonitorAsyncInitialization;

                // Hide progress view when monitoring stops
                if (progressView != null)
                {
                    progressView.style.display = DisplayStyle.None;
                }
                return;
            }

            // Update progress view with latest status
            if (progressView != null)
            {
                progressView.Progress = asyncInitManager.Progress;
                progressView.StatusMessage = asyncInitManager.StatusMessage;
            }

            // Update status label
            if (statusLabel != null)
            {
                statusLabel.text = asyncInitManager.StatusMessage;
            }

            // Update UV image with incremental textures
            if (selector != null && selector.UvMapTexture != null && uvMapImage != null)
            {
                uvMapImage.style.backgroundImage = new StyleBackground(selector.UvMapTexture);
            }
        }

        /// <summary>
        /// Callback when async initialization is completed
        /// 非同期初期化完了時のコールバック
        /// </summary>
        private void OnAsyncInitializationCompleted(UVIslandSelector completedSelector)
        {
            Debug.Log("[UVIslandMaskEditor] OnAsyncInitializationCompleted called");

            // Stop monitoring async initialization
            EditorApplication.update -= MonitorAsyncInitialization;

            // Clear async initialization flag
            asyncInitializationInProgress = false;

            if (completedSelector == null)
            {
                Debug.LogError("[UVIslandMaskEditor] Completion callback received null selector");
                if (progressView != null)
                {
                    progressView.style.display = DisplayStyle.None;
                }
                if (statusLabel != null)
                {
                    statusLabel.text = "Initialization failed";
                }
                return;
            }

            Debug.Log($"[UVIslandMaskEditor] Selector initialized with {completedSelector.UVIslands?.Count ?? 0} islands");

            // Step 1: Restore island selections now that UV analysis is complete
            if (targetMask != null)
            {
                // Load per-submesh selections (new format)
                if (targetMask.PerSubmeshSelections.Count > 0)
                {
                    Debug.Log($"[UVIslandMaskEditor] Restoring {targetMask.PerSubmeshSelections.Count} per-submesh selections");
                    selector.SetAllSelectedIslands(targetMask.GetPerSubmeshSelections());
                }
                // Fallback to legacy flat list if new format is empty (backward compatibility)
                else if (targetMask.SelectedIslandIDs.Count > 0)
                {
                    Debug.Log($"[UVIslandMaskEditor] Restoring {targetMask.SelectedIslandIDs.Count} legacy island selections");
                    selector.SetSelectedIslands(targetMask.SelectedIslandIDs);
                }
            }

            // Step 2: Update flags BEFORE UI refresh
            // textureInitialized = ... (removed)
            // isInitialized = ... (removed)
            // isLoadingFromCache = ... (removed)
            // shouldShowLowResUntilInteraction = ... (removed)

            // Step 3: Re-enable auto preview
            selector.AutoUpdatePreview = true;

            // Step 4: Clear placeholder background color
            if (uvMapImage != null)
            {
                uvMapImage.style.backgroundColor = StyleKeyword.Null;
                uvMapImage.MarkDirtyRepaint();
            }

            // Step 5: Save low-res texture to cache for next reload
            

            // Step 6: Full UI refresh to update all elements
            Debug.Log("[UVIslandMaskEditor] Refreshing UI after initialization");
            RefreshUI(false);

            // Step 7: Update status message
            if (statusLabel != null)
            {
                int islandCount = selector.UVIslands?.Count ?? 0;
                statusLabel.text = UVIslandLocalization.Get("status_islands_found", islandCount);
            }

            // Step 8: Hide progress view LAST to ensure all UI is updated first
            if (progressView != null)
            {
                progressView.style.display = DisplayStyle.None;
            }

            Debug.Log("[UVIslandMaskEditor] Async initialization completed successfully");
        }

        /// <summary>
        /// Force immediate data refresh with texture generation - used for initial load
        /// </summary>
        private void RefreshDataWithImmediteTexture()
        {
            if (selector == null) 
            {
                if (statusLabel != null)
                {
                    statusLabel.text = "No mesh data available";
                }
                return;
            }
            
            if (statusLabel != null)
            {
                statusLabel.text = UVIslandLocalization.Get("status_refreshing");
            }
            
            try 
            {
                // Force mesh data update
                selector.UpdateMeshData();
                
                // Always generate texture immediately on first load
                selector.GenerateUVMapTexture();
                // textureInitialized = ... (removed)
                // isInitialized = ... (removed)
                // isLoadingFromCache = ... (removed)
                // shouldShowLowResUntilInteraction = ... (removed)

                // Save low-res texture to cache for next reload
                

                // Clear placeholder background color
                if (uvMapImage != null)
                {
                    uvMapImage.style.backgroundColor = StyleKeyword.Null;
                }

                // Immediate UI refresh
                RefreshUVMapImage();

                if (selector?.UVIslands != null)
                {
                    islandListView.itemsSource = selector.UVIslands;
                    islandListView.Rebuild(); // Use Rebuild instead of RefreshItems to ensure full update
                }

                // Update UI elements that were created with null selector
                UpdateSubmeshSelectorUI();
                UpdateHighlightSettingsUI();
                UpdateDisplaySettingsUI();
                UpdateSubmeshLabel();

                UpdateStatus();

                int islandCount = selector.UVIslands?.Count ?? 0;
                if (statusLabel != null)
                {
                    statusLabel.text = UVIslandLocalization.Get("status_islands_found", islandCount);
                }
            }
            catch (System.Exception ex)
            {
                if (statusLabel != null)
                {
                    statusLabel.text = $"Error: {ex.Message}";
                }
                Debug.LogError($"[UVIslandMaskEditor] Error refreshing data: {ex}");
            }
        }
        
        private void RefreshUI(bool forceSceneRepaint = false)
        {
            // Always refresh the texture when UI updates
            if (selector?.AutoUpdatePreview ?? false)
            {
                selector.UpdateTextureIfNeeded(); // Use deferred update instead of direct generation
            }
            
            RefreshUVMapImage();
            
            if (selector?.UVIslands != null)
            {
                islandListView.itemsSource = selector.UVIslands;
                islandListView.RefreshItems();
            }
            
            UpdateStatus();
            
            if (forceSceneRepaint)
            {
                SceneView.RepaintAll();
            }
        }
        
        // Fast UI refresh for frequent operations like selection changes
        private void RefreshUI(false)
        {
            // Update essential UI elements immediately
            UpdateStatus();
            
            // Update list view selection state without full refresh
            if (islandListView != null && selector?.UVIslands != null)
            {
                // Only refresh if needed
                if (islandListView.itemsSource != selector.UVIslands)
                {
                    islandListView.itemsSource = selector.UVIslands;
                }
                islandListView.RefreshItems();
            }
            
            // Only generate texture if needed and not showing low-res until interaction
            if (false) // shouldShowLowResUntilInteraction removed
            {
                if (selector.UvMapTexture == null)
                {
                    selector.GenerateUVMapTexture();
                    // textureInitialized = ... (removed)
                }
            }
            
            // Always refresh image (may show low-res or full-res based on state)
            RefreshUVMapImage();

            // Only repaint scene if there are selected islands to display
            if (selector?.HasSelectedIslands ?? false)
            {
                SceneView.RepaintAll();
            }
        }
        
        private void RefreshUVMapImage()
        {
            if (false) // shouldShowLowResUntilInteraction removed
            {
                // Show full resolution texture
                uvMapImage.style.backgroundImage = new StyleBackground(selector.UvMapTexture);
                ClearLowResDisplayState();
            }
            if (false) // shouldShowLowResUntilInteraction removed)
            {
                // Show low-resolution cached texture until user interaction
                uvMapImage.style.backgroundImage = new StyleBackground(currentLowResTexture);
            }
            else if (selector?.UvMapTexture != null)
            {
                // Fallback to full texture if low-res is not available
                uvMapImage.style.backgroundImage = new StyleBackground(selector.UvMapTexture);
                ClearLowResDisplayState();
            }
            else
            {
                // Clear image if no texture is available
                uvMapImage.style.backgroundImage = StyleKeyword.None;
            }
        }
        
        /// <summary>
        /// Centralized method to clear low-res display state
        /// </summary>
        private void ClearLowResDisplayState()
        {
            // isLoadingFromCache = ... (removed)
            // shouldShowLowResUntilInteraction = ... (removed)
        }
        
        // Throttled immediate texture update for interactive operations
        private void UpdateTextureWithThrottle()
        {
            if (selector == null) return;
            
            float currentTime = Time.realtimeSinceStartup;
            if (currentTime - lastUpdateTime >= TEXTURE_UPDATE_THROTTLE)
            {
                // Immediate update if enough time has passed
                selector.GenerateUVMapTexture();
                RefreshUVMapImage();
                lastUpdateTime = currentTime;
            }
            else if (pendingTextureUpdate == null)
            {
                // Schedule single deferred update if throttled and none pending
                pendingTextureUpdate = () =>
                {
                    if (selector != null)
                    {
                        selector.GenerateUVMapTexture();
                        RefreshUVMapImage();
                        lastUpdateTime = Time.realtimeSinceStartup;
                    }
                    pendingTextureUpdate = null;
                };
                EditorApplication.delayCall += pendingTextureUpdate;
            }
            // If there's already a pending update, do nothing to avoid duplicates
        }
        
        private void UpdateStatus()
        {
            if (selector == null) return;
            
            // Show total selected islands across all submeshes
            var totalSelectedCount = selector.AllSelectedIslandIDs?.Count ?? 0;
            var currentSubmeshSelectedCount = selector.SelectedIslandIDs?.Count ?? 0;
            var maskedVertexCount = selector.VertexMask?.Length ?? 0;
            var maskedFaceCount = (selector.TriangleMask?.Length ?? 0) / 3;

            if (totalSelectedCount > 0)
            {
                // Show both current submesh selection and total across all submeshes
                if (selector.SelectedSubmeshIndices.Count > 1)
                {
                    statusLabel.text = $"Selected: {currentSubmeshSelectedCount} islands (Submesh {selector.CurrentPreviewSubmesh}) | Total: {totalSelectedCount} islands across all submeshes | {maskedVertexCount} vertices, {maskedFaceCount} faces";
                }
                else
                {
                    statusLabel.text = UVIslandLocalization.Get("status_islands_selected",
                        currentSubmeshSelectedCount, maskedVertexCount, maskedFaceCount);
                }
            }
            else
            {
                int islandCount = selector.UVIslands?.Count ?? 0;
                statusLabel.text = UVIslandLocalization.Get("status_islands_found", islandCount);
            }
        }

        #endregion
    }
}
