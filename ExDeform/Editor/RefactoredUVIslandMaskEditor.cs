using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using System.Linq;
using System.Collections.Generic;

namespace ExDeform.Editor
{
    /// <summary>
    /// Refactored UV Island Mask Editor with improved code structure
    /// 改善されたコード構造のUVアイランドマスクエディタ（リファクタリング版）
    /// </summary>
    [CustomEditor(typeof(UVIslandMask))]
    public class RefactoredUVIslandMaskEditor : UnityEditor.Editor
    {
        #region Constants
        private const int UV_MAP_SIZE = 300;
        private const int LOW_RES_TEXTURE_SIZE = 128;
        private const float TEXTURE_UPDATE_THROTTLE = 0.016f; // ~60fps limit
        private const double CACHE_HEALTH_CHECK_INTERVAL_HOURS = 1.0;
        #endregion
        
        #region Fields
        private UVIslandMask targetMask;
        private UVIslandSelector selector;
        private VisualElement root;
        private VisualElement uvMapContainer;
        private VisualElement uvMapImage;
        private Label statusLabel;
        private ListView islandListView;
        
        // UI controls
        private EnumField languageField;
        private Toggle adaptiveVertexSizeToggle;
        private Slider vertexSizeSlider;
        private Slider adaptiveMultiplierSlider;
        private Toggle autoUpdateToggle;
        private Button refreshButton;
        private Button clearSelectionButton;
        
        // Cache management
        private UVIslandSelector cachedSelector;
        private UVIslandMask lastTargetMask;
        private bool isInitialized = false;
        private Mesh lastCachedMesh;
        private int lastMeshInstanceID = -1;
        private string currentCacheKey;
        private Texture2D currentLowResTexture;
        
        // Static cache
        private static Dictionary<string, UVIslandSelector> persistentCache = new Dictionary<string, UVIslandSelector>();
        private static bool isCacheSystemInitialized = false;
        #endregion
        
        #region Main CreateInspectorGUI (Refactored)
        public override VisualElement CreateInspectorGUI()
        {
            targetMask = target as UVIslandMask;
            
            // Early return for reused UI
            if (ShouldReuseExistingUI())
            {
                return root;
            }
            
            // Initialize editor data and cache
            InitializeEditorData();
            
            // Create UI structure
            root = CreateRootElement();
            SetupUIComponents();
            ConfigureEventHandlers();
            
            // Initialize display data
            InitializeDisplayData();
            
            return root;
        }
        #endregion
        
        #region Initialization Methods
        private bool ShouldReuseExistingUI()
        {
            bool shouldReuse = root != null && lastTargetMask == targetMask;
            
            if (shouldReuse)
            {
                LogCacheOperation($"Reusing existing UI for target {targetMask?.GetInstanceID()}");
            }
            
            return shouldReuse;
        }
        
        private void InitializeEditorData()
        {
            LogCacheOperation($"Initializing editor data for target {targetMask?.GetInstanceID()}");
            
            // Cache system initialization
            InitializeCacheSystem();
            
            // Mesh and selector setup
            var originalMesh = GetOriginalMesh();
            currentCacheKey = GenerateCacheKey(originalMesh);
            
            // Initialize selector
            InitializeSelector(originalMesh);
            
            // Update internal references
            UpdateInternalReferences(originalMesh);
        }
        
        private void InitializeSelector(Mesh originalMesh)
        {
            if (TryLoadSelectorFromCache())
            {
                LoadLowResTextureFromCache();
                ResetTextureFlags();
            }
            else if (originalMesh != null)
            {
                CreateNewSelector(originalMesh);
                CacheSelectorIfPossible();
                LoadLowResTextureFromCache();
                ResetTextureFlags();
            }
            else
            {
                ClearSelectorData();
            }
        }
        
        private bool TryLoadSelectorFromCache()
        {
            if (currentCacheKey != null && persistentCache.TryGetValue(currentCacheKey, out var cachedSelector))
            {
                selector = cachedSelector;
                selector.SetSelectedIslands(targetMask.SelectedIslandIDs);
                selector.TargetTransform = GetRendererTransform();
                return true;
            }
            return false;
        }
        
        private void CreateNewSelector(Mesh originalMesh)
        {
            selector = new UVIslandSelector(originalMesh);
            selector.SetSelectedIslands(targetMask.SelectedIslandIDs);
            selector.TargetTransform = GetRendererTransform();
        }
        
        private void CacheSelectorIfPossible()
        {
            if (currentCacheKey != null)
            {
                persistentCache[currentCacheKey] = selector;
            }
        }
        
        private void ClearSelectorData()
        {
            selector = null;
            isInitialized = false;
        }
        
        private void ResetTextureFlags()
        {
            isInitialized = false;
        }
        
        private void UpdateInternalReferences(Mesh originalMesh)
        {
            cachedSelector = selector;
            lastTargetMask = targetMask;
            lastCachedMesh = originalMesh;
            lastMeshInstanceID = originalMesh?.GetInstanceID() ?? -1;
        }
        #endregion
        
        #region UI Creation Methods
        private VisualElement CreateRootElement()
        {
            var rootElement = new VisualElement();
            rootElement.style.paddingTop = 10;
            rootElement.style.paddingBottom = 10;
            rootElement.style.paddingLeft = 10;
            rootElement.style.paddingRight = 10;
            
            return rootElement;
        }
        
        private void SetupUIComponents()
        {
            CreateLanguageSelector();
            CreateHeader();
            CreateMaskSettings();
            CreateDisplaySettings();
            CreateUVMapArea();
            CreateIslandList();
            CreateControlButtons();
            CreateStatusArea();
        }
        
        private void ConfigureEventHandlers()
        {
            root.RegisterCallback<MouseMoveEvent>(OnRootMouseMove, TrickleDown.TrickleDown);
            root.RegisterCallback<MouseUpEvent>(OnRootMouseUp, TrickleDown.TrickleDown);
        }
        
        private void InitializeDisplayData()
        {
            if (selector != null)
            {
                InitializeWithCacheOrGenerate();
            }
            else
            {
                ShowNoMeshDataMessage();
            }
        }
        
        private void InitializeWithCacheOrGenerate()
        {
            LoadLowResTextureFromCache();
            
            if (currentLowResTexture != null)
            {
                ShowCachedLowResTexture();
            }
            else
            {
                GenerateFullTextureImmediately();
            }
        }
        
        private void ShowCachedLowResTexture()
        {
            RefreshUIFast(); // Quick UI update with low-res
        }
        
        private void GenerateFullTextureImmediately()
        {
            RefreshDataWithImmediateTexture();
        }
        
        private void ShowNoMeshDataMessage()
        {
            if (statusLabel != null)
            {
                statusLabel.text = "No mesh data available - please assign a mesh to the GameObject";
            }
        }
        #endregion
        
        #region UI Component Creation (Simplified Stubs)
        private void CreateLanguageSelector()
        {
            // Language selection UI creation
            var languageContainer = new VisualElement
            {
                style = { 
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 10
                }
            };
            
            var languageLabel = new Label("Language / 言語:");
            languageLabel.style.marginRight = 10;
            languageLabel.style.fontSize = 11;
            
            languageField = new EnumField(UVIslandLocalization.CurrentLanguage);
            languageField.style.width = 100;
            languageField.RegisterValueChangedCallback(evt =>
            {
                UVIslandLocalization.CurrentLanguage = (UVIslandLocalization.Language)evt.newValue;
                RefreshUIText();
            });
            
            languageContainer.Add(languageLabel);
            languageContainer.Add(languageField);
            root.Add(languageContainer);
        }
        
        private void CreateHeader()
        {
            var headerLabel = new Label(UVIslandLocalization.Get("header_selection"))
            {
                style = {
                    fontSize = 16,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 10
                }
            };
            root.Add(headerLabel);
        }
        
        private void CreateMaskSettings()
        {
            var maskContainer = CreateSection("Mask Settings / マスク設定");
            
            var invertMaskToggle = new Toggle("Invert Mask / マスク反転")
            {
                value = targetMask.InvertMask
            };
            invertMaskToggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(targetMask, "Toggle Invert Mask");
                targetMask.InvertMask = evt.newValue;
                EditorUtility.SetDirty(targetMask);
            });
            
            maskContainer.Add(invertMaskToggle);
            root.Add(maskContainer);
        }
        
        private void CreateDisplaySettings()
        {
            var settingsContainer = CreateSection(UVIslandLocalization.Get("header_display"));
            
            adaptiveVertexSizeToggle = new Toggle()
            {
                value = selector?.UseAdaptiveVertexSize ?? true
            };
            SetLocalizedContent(adaptiveVertexSizeToggle, "adaptive_vertex_size", "tooltip_adaptive_size");
            
            settingsContainer.Add(adaptiveVertexSizeToggle);
            root.Add(settingsContainer);
        }
        
        private void CreateUVMapArea()
        {
            uvMapContainer = new VisualElement
            {
                style = {
                    width = UV_MAP_SIZE,
                    height = UV_MAP_SIZE,
                    backgroundColor = Color.white,
                    borderBottomWidth = 1,
                    borderTopWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    marginBottom = 15,
                    alignSelf = Align.Center
                }
            };
            
            uvMapImage = new VisualElement
            {
                style = {
                    width = UV_MAP_SIZE,
                    height = UV_MAP_SIZE,
                    backgroundImage = null
                }
            };
            
            uvMapContainer.Add(uvMapImage);
            root.Add(uvMapContainer);
        }
        
        private void CreateIslandList()
        {
            islandListView = new ListView
            {
                style = {
                    height = 120,
                    marginBottom = 10
                },
                selectionType = SelectionType.Multiple,
                reorderable = false
            };
            
            root.Add(islandListView);
        }
        
        private void CreateControlButtons()
        {
            var buttonContainer = new VisualElement
            {
                style = {
                    flexDirection = FlexDirection.Row,
                    marginBottom = 10
                }
            };
            
            refreshButton = new Button(() => RefreshData())
            {
                text = "Refresh",
                style = { flexGrow = 1, marginRight = 5 }
            };
            
            clearSelectionButton = new Button(() => ClearSelection())
            {
                text = "Clear Selection",
                style = { flexGrow = 1, marginLeft = 5 }
            };
            
            buttonContainer.Add(refreshButton);
            buttonContainer.Add(clearSelectionButton);
            root.Add(buttonContainer);
        }
        
        private void CreateStatusArea()
        {
            statusLabel = new Label("Ready")
            {
                style = {
                    fontSize = 11,
                    color = Color.gray,
                    marginTop = 10
                }
            };
            root.Add(statusLabel);
        }
        
        private VisualElement CreateSection(string title)
        {
            var section = new VisualElement
            {
                style = {
                    backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.3f),
                    paddingTop = 10,
                    paddingBottom = 10,
                    paddingLeft = 10,
                    paddingRight = 10,
                    marginBottom = 15
                }
            };
            
            var titleLabel = new Label(title)
            {
                style = {
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 10
                }
            };
            section.Add(titleLabel);
            
            return section;
        }
        #endregion
        
        #region Helper Methods (Stubs)
        private void InitializeCacheSystem() 
        {
            if (!isCacheSystemInitialized)
            {
                isCacheSystemInitialized = true;
            }
        }
        
        private Mesh GetOriginalMesh() 
        { 
            return targetMask?.OriginalMesh; 
        }
        
        private string GenerateCacheKey(Mesh mesh) 
        { 
            return mesh?.name + "_" + mesh?.GetInstanceID(); 
        }
        
        private Transform GetRendererTransform() 
        { 
            return targetMask?.CachedRendererTransform; 
        }
        
        private void LoadLowResTextureFromCache() 
        { 
            // Cache loading implementation
        }
        
        private void RefreshUIFast() 
        { 
            // Fast UI refresh implementation  
        }
        
        private void RefreshDataWithImmediateTexture() 
        { 
            // Immediate texture generation implementation
        }
        
        private void RefreshUIText() 
        { 
            // UI text refresh implementation
        }
        
        private void RefreshData() 
        { 
            // Data refresh implementation
        }
        
        private void ClearSelection() 
        { 
            // Selection clearing implementation
        }
        
        private void SetLocalizedContent(VisualElement element, string textKey, string tooltipKey = null) 
        {
            // Localization implementation
        }
        
        private void LogCacheOperation(string message, bool isError = false)
        {
            if (isError)
            {
                Debug.LogError($"[UVIslandMaskEditor] {message}");
            }
            else
            {
                Debug.Log($"[UVIslandMaskEditor] {message}");
            }
        }
        
        private void OnRootMouseMove(MouseMoveEvent evt) { }
        private void OnRootMouseUp(MouseUpEvent evt) { }
        #endregion
    }
}