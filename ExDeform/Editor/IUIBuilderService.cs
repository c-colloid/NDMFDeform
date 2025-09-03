using System;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using ExDeform.Runtime.Deformers;

namespace ExDeform.Editor
{
    /// <summary>
    /// Interface for UI builder service that handles VisualElement construction,
    /// event handler setup, and layout management for UV Island Mask Editor
    /// VisualElement構築、イベントハンドラー設定、レイアウト管理を担当するサービス
    /// </summary>
    public interface IUIBuilderService
    {
        #region Section Creation Methods
        
        /// <summary>
        /// Create a styled section container with title
        /// </summary>
        /// <param name="title">Section title</param>
        /// <returns>Container VisualElement</returns>
        VisualElement CreateSection(string title);
        
        /// <summary>
        /// Create language selector UI component
        /// </summary>
        /// <param name="onLanguageChanged">Callback when language changes</param>
        /// <returns>Language selector container</returns>
        VisualElement CreateLanguageSelector(Action<UVIslandLocalization.Language> onLanguageChanged);
        
        /// <summary>
        /// Create header section with title and description
        /// </summary>
        /// <returns>Header container</returns>
        VisualElement CreateHeader();
        
        /// <summary>
        /// Create mask settings section
        /// </summary>
        /// <param name="targetMask">Target UV island mask</param>
        /// <returns>Mask settings container</returns>
        VisualElement CreateMaskSettings(UVIslandMask targetMask);
        
        /// <summary>
        /// Create display settings section
        /// </summary>
        /// <param name="selector">UV island selector</param>
        /// <returns>Display settings container</returns>
        VisualElement CreateDisplaySettings(UVIslandSelector selector);
        
        /// <summary>
        /// Create status area with status label
        /// </summary>
        /// <returns>Status container and label</returns>
        (VisualElement container, Label statusLabel) CreateStatusArea();
        
        #endregion
        
        #region UV Map UI Creation
        
        /// <summary>
        /// Create UV map preview area with controls
        /// </summary>
        /// <param name="config">UV map configuration</param>
        /// <returns>UV map components</returns>
        UVMapComponents CreateUVMapArea(UVMapConfig config);
        
        /// <summary>
        /// Create range selection overlay
        /// </summary>
        /// <returns>Range selection overlay element</returns>
        VisualElement CreateRangeSelectionOverlay();
        
        /// <summary>
        /// Create magnifying glass overlay with reticle
        /// </summary>
        /// <returns>Magnifying glass overlay components</returns>
        MagnifyingGlassComponents CreateMagnifyingGlassOverlay();
        
        #endregion
        
        #region List and Controls Creation
        
        /// <summary>
        /// Create UV islands list view
        /// </summary>
        /// <param name="config">List configuration</param>
        /// <returns>Configured ListView</returns>
        ListView CreateIslandList(IslandListConfig config);
        
        /// <summary>
        /// Create control buttons section
        /// </summary>
        /// <param name="onRefresh">Refresh button callback</param>
        /// <param name="onClearSelection">Clear selection button callback</param>
        /// <returns>Control buttons container</returns>
        VisualElement CreateControlButtons(Action onRefresh, Action onClearSelection);
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Set localized content for UI elements
        /// </summary>
        /// <param name="element">Target element</param>
        /// <param name="textKey">Text localization key</param>
        /// <param name="tooltipKey">Tooltip localization key (optional)</param>
        void SetLocalizedContent(VisualElement element, string textKey, string tooltipKey = null);
        
        /// <summary>
        /// Set localized tooltip for UI elements
        /// </summary>
        /// <param name="element">Target element</param>
        /// <param name="tooltipKey">Tooltip localization key</param>
        void SetLocalizedTooltip(VisualElement element, string tooltipKey);
        
        /// <summary>
        /// Register mouse event handlers for UV map interaction
        /// </summary>
        /// <param name="element">Target element</param>
        /// <param name="handlers">Mouse event handlers</param>
        void RegisterMouseEventHandlers(VisualElement element, UVMapMouseHandlers handlers);
        
        #endregion
    }
    
    #region Configuration Classes
    
    /// <summary>
    /// Configuration for UV map area creation
    /// </summary>
    public class UVMapConfig
    {
        public int MapSize { get; set; } = 300;
        public UVIslandSelector Selector { get; set; }
        public UVMapMouseHandlers MouseHandlers { get; set; }
    }
    
    /// <summary>
    /// Configuration for island list creation
    /// </summary>
    public class IslandListConfig
    {
        public Func<VisualElement> MakeItem { get; set; }
        public Action<VisualElement, int> BindItem { get; set; }
        public Action<System.Collections.Generic.IEnumerable<object>> OnSelectionChanged { get; set; }
        public int Height { get; set; } = 120;
    }
    
    /// <summary>
    /// Mouse event handlers for UV map interaction
    /// </summary>
    public class UVMapMouseHandlers
    {
        public Action<MouseDownEvent> OnMouseDown { get; set; }
        public Action<MouseMoveEvent> OnMouseMove { get; set; }
        public Action<MouseUpEvent> OnMouseUp { get; set; }
        public Action<WheelEvent> OnWheel { get; set; }
        public Action<MouseMoveEvent> OnContainerMouseMove { get; set; }
        public Action<MouseUpEvent> OnContainerMouseUp { get; set; }
    }
    
    #endregion
    
    #region Component Containers
    
    /// <summary>
    /// UV map UI components
    /// </summary>
    public struct UVMapComponents
    {
        public VisualElement Container { get; set; }
        public VisualElement ImageElement { get; set; }
        public Toggle AutoUpdateToggle { get; set; }
        public Slider ZoomSlider { get; set; }
        public Button ResetZoomButton { get; set; }
        public Toggle MagnifyingToggle { get; set; }
        public Slider MagnifyingSizeSlider { get; set; }
    }
    
    /// <summary>
    /// Magnifying glass UI components
    /// </summary>
    public struct MagnifyingGlassComponents
    {
        public VisualElement Overlay { get; set; }
        public VisualElement ImageElement { get; set; }
        public Label InfoLabel { get; set; }
    }
    
    #endregion
}