using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Deform.Masking.Editor
{
    /// <summary>
    /// Manages asynchronous initialization of UV island data to prevent Unity Editor freezing
    /// UVアイランドデータの非同期初期化を管理し、Unityエディタのフリーズを防止
    /// </summary>
    public class AsyncInitializationManager
    {
        #region Constants
        private const float MAX_FRAME_TIME_MS = 16f; // Maximum time per frame (60 FPS target)
        private const int LOW_RES_TEXTURE_SIZE = 128;
        private const int MID_RES_TEXTURE_SIZE = 256;
        private const int FULL_RES_TEXTURE_SIZE = 512;
        #endregion

        #region State Machine
        public enum InitializationState
        {
            Idle,
            AnalyzingUVIslands,
            GeneratingLowResTexture,
            GeneratingMidResTexture,
            GeneratingFullResTexture,
            Completed,
            Cancelled
        }
        #endregion

        #region Public Properties
        public InitializationState CurrentState { get; private set; } = InitializationState.Idle;
        public float Progress { get; private set; } = 0f;
        public string StatusMessage { get; private set; } = "";
        public bool IsRunning => CurrentState != InitializationState.Idle &&
                                  CurrentState != InitializationState.Completed &&
                                  CurrentState != InitializationState.Cancelled;
        public bool IsCompleted => CurrentState == InitializationState.Completed;
        #endregion

        #region Private Fields
        private UVIslandSelector targetSelector;
        private Mesh targetMesh;
        private Action<UVIslandSelector> onCompleted;
        private System.Diagnostics.Stopwatch frameStopwatch;

        // UV analysis state
        private List<UVIslandAnalyzer.UVIsland> analyzedIslands;
        private int currentSubmeshIndex = 0;
        private int totalSubmeshCount = 0;

        // Texture generation state
        private bool lowResTextureGenerated = false;
        private bool midResTextureGenerated = false;

        // Cancellation flag
        private bool cancellationRequested = false;
        #endregion

        #region Constructor
        public AsyncInitializationManager()
        {
            frameStopwatch = new System.Diagnostics.Stopwatch();
        }
        #endregion

        #region Public API
        /// <summary>
        /// Start asynchronous initialization process
        /// 非同期初期化プロセスを開始
        /// </summary>
        public void StartInitialization(
            Mesh mesh,
            UVIslandSelector selector,
            Action<UVIslandSelector> onCompleted)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[AsyncInitializationManager] Initialization already in progress");
                return;
            }

            if (mesh == null)
            {
                Debug.LogError("[AsyncInitializationManager] Cannot initialize with null mesh");
                return;
            }

            if (selector == null)
            {
                Debug.LogError("[AsyncInitializationManager] Cannot initialize with null selector");
                return;
            }

            // Initialize state
            this.targetMesh = mesh;
            this.targetSelector = selector;
            this.onCompleted = onCompleted;

            CurrentState = InitializationState.AnalyzingUVIslands;
            Progress = 0f;
            cancellationRequested = false;

            currentSubmeshIndex = 0;
            totalSubmeshCount = mesh.subMeshCount;
            analyzedIslands = new List<UVIslandAnalyzer.UVIsland>();

            lowResTextureGenerated = false;
            midResTextureGenerated = false;

            // Register update callback
            EditorApplication.update += UpdateInitialization;

            UpdateStatus(0f, "Preparing UV analysis...");
        }

        /// <summary>
        /// Cancel ongoing initialization
        /// 進行中の初期化をキャンセル
        /// </summary>
        public void Cancel()
        {
            if (!IsRunning) return;

            cancellationRequested = true;
            CurrentState = InitializationState.Cancelled;
            CleanupAndComplete();
        }

        /// <summary>
        /// Clear all callbacks to prevent them from being invoked
        /// すべてのコールバックをクリアして呼び出しを防止
        /// </summary>
        public void ClearCallbacks()
        {
            onCompleted = null;
        }

        /// <summary>
        /// Force completion with full-resolution texture (called on user interaction)
        /// フル解像度テクスチャで強制完了（ユーザー操作時に呼び出し）
        /// </summary>
        public void ForceFullResolution()
        {
            if (!IsRunning) return;

            // Skip to full resolution texture generation
            if (CurrentState == InitializationState.AnalyzingUVIslands && analyzedIslands.Count > 0)
            {
                // Complete any remaining UV analysis immediately
                CompleteUVAnalysis();
            }

            // Generate full resolution texture immediately
            if (targetSelector != null && CurrentState != InitializationState.GeneratingFullResTexture)
            {
                CurrentState = InitializationState.GeneratingFullResTexture;
                GenerateFullResTexture();
                CompleteInitialization();
            }
        }
        #endregion

        #region Update Loop
        private void UpdateInitialization()
        {
            if (!IsRunning || cancellationRequested)
            {
                CleanupAndComplete();
                return;
            }

            frameStopwatch.Restart();

            try
            {
                switch (CurrentState)
                {
                    case InitializationState.AnalyzingUVIslands:
                        ProcessUVAnalysisIncremental();
                        break;

                    case InitializationState.GeneratingLowResTexture:
                        GenerateLowResTexture();
                        break;

                    case InitializationState.GeneratingMidResTexture:
                        GenerateMidResTexture();
                        break;

                    case InitializationState.GeneratingFullResTexture:
                        GenerateFullResTexture();
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AsyncInitializationManager] Error during initialization: {e.Message}\n{e.StackTrace}");
                CurrentState = InitializationState.Cancelled;
                CleanupAndComplete();
            }
            finally
            {
                frameStopwatch.Stop();
            }
        }
        #endregion

        #region UV Analysis
        private void ProcessUVAnalysisIncremental()
        {
            if (targetMesh == null)
            {
                Debug.LogError("[AsyncInitializationManager] Target mesh is null during UV analysis");
                CurrentState = InitializationState.Cancelled;
                return;
            }

            if (targetSelector == null)
            {
                Debug.LogError("[AsyncInitializationManager] Target selector is null during UV analysis");
                CurrentState = InitializationState.Cancelled;
                return;
            }

            frameStopwatch.Restart();

            // Process one submesh at a time to avoid freezing
            while (currentSubmeshIndex < totalSubmeshCount)
            {
                // Analyze current submesh
                var submeshIslands = AnalyzeSubmesh(currentSubmeshIndex);
                if (submeshIslands != null && submeshIslands.Count > 0)
                {
                    analyzedIslands.AddRange(submeshIslands);
                    Debug.Log($"[AsyncInitializationManager] Analyzed submesh {currentSubmeshIndex}: found {submeshIslands.Count} islands");
                }

                currentSubmeshIndex++;

                // Update progress
                float analysisProgress = (float)currentSubmeshIndex / totalSubmeshCount;
                UpdateStatus(analysisProgress * 0.5f, $"Analyzing UV islands ({currentSubmeshIndex}/{totalSubmeshCount})...");

                // Check if we've exceeded frame time budget
                if (frameStopwatch.ElapsedMilliseconds > MAX_FRAME_TIME_MS)
                {
                    // Continue in next frame
                    Debug.Log($"[AsyncInitializationManager] Frame time budget exceeded ({frameStopwatch.ElapsedMilliseconds}ms), continuing in next frame");
                    return;
                }
            }

            // UV analysis completed
            Debug.Log($"[AsyncInitializationManager] UV analysis completed: {analyzedIslands.Count} total islands");

            if (targetSelector != null)
            {
                targetSelector.SetAnalyzedIslands(analyzedIslands);
            }

            // Move to texture generation
            CurrentState = InitializationState.GeneratingLowResTexture;
            UpdateStatus(0.5f, "Generating low-res preview...");
        }

        private List<UVIslandAnalyzer.UVIsland> AnalyzeSubmesh(int submeshIndex)
        {
            try
            {
                // Use existing analyzer with single submesh
                return UVIslandAnalyzer.AnalyzeUVIslands(targetMesh, new List<int> { submeshIndex });
            }
            catch (Exception e)
            {
                Debug.LogError($"[AsyncInitializationManager] Failed to analyze submesh {submeshIndex}: {e.Message}");
                return new List<UVIslandAnalyzer.UVIsland>();
            }
        }

        private void CompleteUVAnalysis()
        {
            // Process any remaining submeshes immediately
            while (currentSubmeshIndex < totalSubmeshCount)
            {
                var submeshIslands = AnalyzeSubmesh(currentSubmeshIndex);
                if (submeshIslands != null)
                {
                    analyzedIslands.AddRange(submeshIslands);
                }
                currentSubmeshIndex++;
            }

            if (targetSelector != null)
            {
                targetSelector.SetAnalyzedIslands(analyzedIslands);
            }
        }
        #endregion

        #region Texture Generation
        private void GenerateLowResTexture()
        {
            if (lowResTextureGenerated || targetSelector == null)
            {
                CurrentState = InitializationState.GeneratingMidResTexture;
                return;
            }

            try
            {
                Debug.Log($"[AsyncInitializationManager] Generating low-res texture ({LOW_RES_TEXTURE_SIZE}x{LOW_RES_TEXTURE_SIZE})...");
                targetSelector.GenerateUVMapTexture(LOW_RES_TEXTURE_SIZE, LOW_RES_TEXTURE_SIZE);
                lowResTextureGenerated = true;

                Debug.Log("[AsyncInitializationManager] Low-res texture generated successfully");
                UpdateStatus(0.7f, "Low-resolution preview ready");
                CurrentState = InitializationState.GeneratingMidResTexture;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AsyncInitializationManager] Failed to generate low-res texture: {e.Message}\n{e.StackTrace}");
                CurrentState = InitializationState.GeneratingMidResTexture;
            }
        }

        private void GenerateMidResTexture()
        {
            if (midResTextureGenerated || targetSelector == null)
            {
                CurrentState = InitializationState.GeneratingFullResTexture;
                return;
            }

            try
            {
                Debug.Log($"[AsyncInitializationManager] Generating mid-res texture ({MID_RES_TEXTURE_SIZE}x{MID_RES_TEXTURE_SIZE})...");
                targetSelector.GenerateUVMapTexture(MID_RES_TEXTURE_SIZE, MID_RES_TEXTURE_SIZE);
                midResTextureGenerated = true;

                Debug.Log("[AsyncInitializationManager] Mid-res texture generated successfully");
                UpdateStatus(0.85f, "Medium-resolution preview ready");
                CurrentState = InitializationState.GeneratingFullResTexture;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AsyncInitializationManager] Failed to generate mid-res texture: {e.Message}\n{e.StackTrace}");
                CurrentState = InitializationState.GeneratingFullResTexture;
            }
        }

        private void GenerateFullResTexture()
        {
            if (targetSelector == null)
            {
                Debug.LogError("[AsyncInitializationManager] Target selector is null during full-res generation");
                CurrentState = InitializationState.Cancelled;
                return;
            }

            try
            {
                Debug.Log($"[AsyncInitializationManager] Generating full-res texture ({FULL_RES_TEXTURE_SIZE}x{FULL_RES_TEXTURE_SIZE})...");
                targetSelector.GenerateUVMapTexture(FULL_RES_TEXTURE_SIZE, FULL_RES_TEXTURE_SIZE);

                Debug.Log("[AsyncInitializationManager] Full-res texture generated successfully");
                UpdateStatus(1f, "Initialization complete");
                CompleteInitialization();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AsyncInitializationManager] Failed to generate full-res texture: {e.Message}\n{e.StackTrace}");
                CurrentState = InitializationState.Cancelled;
                CleanupAndComplete();
            }
        }
        #endregion

        #region Completion
        private void CompleteInitialization()
        {
            Debug.Log("[AsyncInitializationManager] CompleteInitialization started");

            // IMPORTANT: Call onCompleted FIRST, before unregistering update or changing state
            // This ensures the callback sees the Completed state and can properly update UI
            var completedCallback = onCompleted;
            var completedSelector = targetSelector;

            // Clear references before calling callback to prevent reentry issues
            targetSelector = null;
            targetMesh = null;
            onCompleted = null;

            // Unregister update callback
            EditorApplication.update -= UpdateInitialization;

            // Set state to completed
            CurrentState = InitializationState.Completed;

            // Now invoke the callback
            if (completedCallback != null && completedSelector != null)
            {
                Debug.Log("[AsyncInitializationManager] Invoking completion callback");
                completedCallback.Invoke(completedSelector);
            }

            Debug.Log("[AsyncInitializationManager] CompleteInitialization finished");
        }

        private void CleanupAndComplete()
        {
            Debug.Log($"[AsyncInitializationManager] CleanupAndComplete called, state: {CurrentState}");

            EditorApplication.update -= UpdateInitialization;

            if (CurrentState == InitializationState.Cancelled)
            {
                // Don't call UpdateStatus() when cancelled - this prevents "Initialization cancelled"
                // from persisting in the UI when callbacks haven't been cleared yet
                Debug.LogWarning("[AsyncInitializationManager] Initialization was cancelled");
            }

            // Clear references
            targetSelector = null;
            targetMesh = null;
            onCompleted = null;
        }
        #endregion

        #region Helper Methods
        private void UpdateStatus(float progress, string message)
        {
            Progress = Mathf.Clamp01(progress);
            StatusMessage = message;
            // Note: No longer invoking callback - Editor will poll state instead
        }
        #endregion
    }
}
