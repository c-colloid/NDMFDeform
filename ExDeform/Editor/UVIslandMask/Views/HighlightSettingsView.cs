using UnityEngine;
using UnityEngine.UIElements;

namespace Deform.Masking.Editor.Views
{
    /// <summary>
    /// Custom event fired when highlight opacity changes
    /// ハイライト透明度変更時に発行されるカスタムイベント
    /// </summary>
    public class HighlightOpacityChangedEvent : EventBase<HighlightOpacityChangedEvent>
    {
        public float Opacity { get; set; }

        public static HighlightOpacityChangedEvent GetPooled(float opacity)
        {
            var evt = GetPooled();
            evt.Opacity = opacity;
            return evt;
        }
    }

    /// <summary>
    /// Custom VisualElement for scene view highlight settings
    /// シーンビューハイライト設定用のカスタムVisualElement
    /// </summary>
    public class HighlightSettingsView : VisualElement
    {
        #region UxmlFactory
        public new class UxmlFactory : UxmlFactory<HighlightSettingsView, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlFloatAttributeDescription m_HighlightOpacity = new UxmlFloatAttributeDescription
            {
                name = "highlight-opacity",
                defaultValue = 0.6f
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var view = ve as HighlightSettingsView;
                view.HighlightOpacity = m_HighlightOpacity.GetValueFromBag(bag, cc);
            }
        }
        #endregion

        #region USS Class Names (BEM Convention)
        private static readonly string ussClassName = "highlight-settings-view";
        private static readonly string opacitySliderUssClassName = ussClassName + "__opacity-slider";
        #endregion

        #region UI Elements
        private Slider opacitySlider;
        #endregion

        #region Private Fields
        private float m_HighlightOpacity = 0.6f;
        #endregion

        #region Public Properties
        /// <summary>
        /// Highlight opacity (0.0 = fully transparent, 1.0 = fully opaque)
        /// ハイライト透明度（0.0 = 完全透明、1.0 = 完全不透明）
        /// </summary>
        public float HighlightOpacity
        {
            get => m_HighlightOpacity;
            set
            {
                float newValue = Mathf.Clamp01(value);
                if (!Mathf.Approximately(m_HighlightOpacity, newValue))
                {
                    m_HighlightOpacity = newValue;
                    if (opacitySlider != null)
                        opacitySlider.value = m_HighlightOpacity;

                    // Fire event
                    using (var evt = HighlightOpacityChangedEvent.GetPooled(m_HighlightOpacity))
                    {
                        evt.target = this;
                        SendEvent(evt);
                    }
                }
            }
        }
        #endregion

        #region Constructor
        /// <summary>
        /// Default constructor
        /// デフォルトコンストラクタ
        /// </summary>
        public HighlightSettingsView()
        {
            AddToClassList(ussClassName);
            BuildUI();
        }
        #endregion

        #region UI Construction
        private void BuildUI()
        {
            // Container styling
            style.flexDirection = FlexDirection.Column;
            style.marginTop = 5;

            // Opacity slider
            opacitySlider = new Slider("Highlight Opacity", 0f, 1f)
            {
                value = m_HighlightOpacity,
                style = { marginBottom = 5 }
            };
            opacitySlider.AddToClassList(opacitySliderUssClassName);
            opacitySlider.RegisterValueChangedCallback(OnOpacitySliderChanged);
            Add(opacitySlider);
        }
        #endregion

        #region Event Handlers
        private void OnOpacitySliderChanged(ChangeEvent<float> evt)
        {
            // Use property setter to fire event
            HighlightOpacity = evt.newValue;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Set slider label text (for localization support)
        /// スライダーラベルテキストを設定（多言語化対応）
        /// </summary>
        public void SetOpacitySliderLabel(string label)
        {
            if (opacitySlider != null)
                opacitySlider.label = label;
        }

        /// <summary>
        /// Set slider tooltip (for localization support)
        /// スライダーツールチップを設定（多言語化対応）
        /// </summary>
        public void SetOpacitySliderTooltip(string tooltip)
        {
            if (opacitySlider != null)
                opacitySlider.tooltip = tooltip;
        }
        #endregion
    }
}
