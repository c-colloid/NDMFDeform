using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Collections.Generic;
using System.Linq;

namespace Deform.Masking.Editor.Views
{
    /// <summary>
    /// Custom event fired when current submesh changes
    /// 現在のサブメッシュ変更時に発行されるカスタムイベント
    /// </summary>
    public class SubmeshChangedEvent : EventBase<SubmeshChangedEvent>
    {
        public int NewSubmeshIndex { get; set; }

        public static SubmeshChangedEvent GetPooled(int newIndex)
        {
            var evt = GetPooled();
            evt.NewSubmeshIndex = newIndex;
            return evt;
        }
    }

    /// <summary>
    /// Custom event fired when submesh selection mask changes
    /// サブメッシュ選択マスク変更時に発行されるカスタムイベント
    /// </summary>
    public class SubmeshMaskChangedEvent : EventBase<SubmeshMaskChangedEvent>
    {
        public int NewMask { get; set; }
        public List<int> SelectedSubmeshes { get; set; }

        public static SubmeshMaskChangedEvent GetPooled(int mask, List<int> selected)
        {
            var evt = GetPooled();
            evt.NewMask = mask;
            evt.SelectedSubmeshes = selected;
            return evt;
        }
    }

    /// <summary>
    /// Custom VisualElement for submesh selection and navigation
    /// サブメッシュ選択とナビゲーション用のカスタムVisualElement
    /// </summary>
    public class SubmeshSelectorView : VisualElement
    {
        #region UxmlFactory
        public new class UxmlFactory : UxmlFactory<SubmeshSelectorView, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlIntAttributeDescription m_CurrentSubmesh = new UxmlIntAttributeDescription
            {
                name = "current-submesh",
                defaultValue = 0
            };

            UxmlIntAttributeDescription m_TotalSubmeshes = new UxmlIntAttributeDescription
            {
                name = "total-submeshes",
                defaultValue = 1
            };

            UxmlIntAttributeDescription m_SelectedMask = new UxmlIntAttributeDescription
            {
                name = "selected-mask",
                defaultValue = 1
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var view = ve as SubmeshSelectorView;
                view.TotalSubmeshes = m_TotalSubmeshes.GetValueFromBag(bag, cc);
                view.SelectedMask = m_SelectedMask.GetValueFromBag(bag, cc);
                view.CurrentSubmesh = m_CurrentSubmesh.GetValueFromBag(bag, cc);
            }
        }
        #endregion

        #region USS Class Names (BEM Convention)
        private static readonly string ussClassName = "submesh-selector-view";
        private static readonly string maskFieldUssClassName = ussClassName + "__mask-field";
        private static readonly string navContainerUssClassName = ussClassName + "__nav-container";
        private static readonly string navButtonUssClassName = ussClassName + "__nav-button";
        private static readonly string labelUssClassName = ussClassName + "__label";
        #endregion

        #region UI Elements
        private MaskField maskField;
        private VisualElement navContainer;
        private Button prevButton;
        private Button nextButton;
        private Label currentSubmeshLabel;
        #endregion

        #region Private Fields
        private int m_CurrentSubmesh;
        private int m_TotalSubmeshes = 1;
        private int m_SelectedMask = 1;
        #endregion

        #region Public Properties
        /// <summary>
        /// Current preview submesh index
        /// 現在のプレビューサブメッシュインデックス
        /// </summary>
        public int CurrentSubmesh
        {
            get => m_CurrentSubmesh;
            set
            {
                int newValue = Mathf.Clamp(value, 0, m_TotalSubmeshes - 1);
                if (m_CurrentSubmesh != newValue)
                {
                    m_CurrentSubmesh = newValue;
                    UpdateSubmeshLabel();

                    // Fire event
                    using (var evt = SubmeshChangedEvent.GetPooled(newValue))
                    {
                        evt.target = this;
                        SendEvent(evt);
                    }
                }
            }
        }

        /// <summary>
        /// Total number of submeshes
        /// 総サブメッシュ数
        /// </summary>
        public int TotalSubmeshes
        {
            get => m_TotalSubmeshes;
            set
            {
                m_TotalSubmeshes = Mathf.Max(1, value);
                UpdateMaskFieldChoices();
                UpdateNavigationVisibility();
                UpdateSubmeshLabel();
            }
        }

        /// <summary>
        /// Selected submeshes as bit mask
        /// 選択されたサブメッシュ（ビットマスク）
        /// </summary>
        public int SelectedMask
        {
            get => m_SelectedMask;
            set
            {
                if (m_SelectedMask != value)
                {
                    m_SelectedMask = value;
                    if (maskField != null)
                        maskField.value = value;

                    // Convert mask to list
                    var selectedList = MaskToList(value);

                    // Fire event
                    using (var evt = SubmeshMaskChangedEvent.GetPooled(value, selectedList))
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
        public SubmeshSelectorView()
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
            style.marginBottom = 10;

            // Mask field for submesh selection
            maskField = new MaskField("Selected Submeshes", new List<string>(), m_SelectedMask)
            {
                style = { marginBottom = 5 }
            };
            maskField.AddToClassList(maskFieldUssClassName);
            maskField.RegisterValueChangedCallback(OnMaskFieldChanged);
            Add(maskField);

            // Navigation container
            navContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center
                }
            };
            navContainer.AddToClassList(navContainerUssClassName);

            // Previous button
            prevButton = new Button(OnPrevButtonClicked)
            {
                text = "◀",
                style = { width = 30, marginRight = 5 }
            };
            prevButton.AddToClassList(navButtonUssClassName);
            navContainer.Add(prevButton);

            // Current submesh label
            currentSubmeshLabel = new Label($"Submesh {m_CurrentSubmesh}")
            {
                style =
                {
                    fontSize = 11,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    minWidth = 80
                }
            };
            currentSubmeshLabel.AddToClassList(labelUssClassName);
            navContainer.Add(currentSubmeshLabel);

            // Next button
            nextButton = new Button(OnNextButtonClicked)
            {
                text = "▶",
                style = { width = 30, marginLeft = 5 }
            };
            nextButton.AddToClassList(navButtonUssClassName);
            navContainer.Add(nextButton);

            Add(navContainer);

            // Initialize UI state
            UpdateMaskFieldChoices();
            UpdateNavigationVisibility();
        }
        #endregion

        #region Event Handlers
        private void OnMaskFieldChanged(ChangeEvent<int> evt)
        {
            // Use property setter to fire event
            SelectedMask = evt.newValue;
        }

        private void OnPrevButtonClicked()
        {
            if (m_CurrentSubmesh > 0)
            {
                CurrentSubmesh = m_CurrentSubmesh - 1;
            }
        }

        private void OnNextButtonClicked()
        {
            if (m_CurrentSubmesh < m_TotalSubmeshes - 1)
            {
                CurrentSubmesh = m_CurrentSubmesh + 1;
            }
        }
        #endregion

        #region Helper Methods
        private void UpdateMaskFieldChoices()
        {
            if (maskField == null) return;

            var choices = new List<string>();
            for (int i = 0; i < m_TotalSubmeshes; i++)
            {
                choices.Add($"Submesh {i}");
            }
            maskField.choices = choices;
        }

        private void UpdateNavigationVisibility()
        {
            if (navContainer == null) return;

            // Show navigation only if there are multiple submeshes
            navContainer.style.display = m_TotalSubmeshes > 1 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateSubmeshLabel()
        {
            if (currentSubmeshLabel == null) return;
            currentSubmeshLabel.text = $"Submesh {m_CurrentSubmesh}";
        }

        private List<int> MaskToList(int mask)
        {
            var list = new List<int>();
            for (int i = 0; i < m_TotalSubmeshes; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    list.Add(i);
                }
            }
            return list;
        }

        /// <summary>
        /// Convert list of submesh indices to bit mask
        /// サブメッシュインデックスのリストをビットマスクに変換
        /// </summary>
        public static int ListToMask(List<int> indices)
        {
            int mask = 0;
            if (indices != null)
            {
                foreach (var index in indices)
                {
                    mask |= (1 << index);
                }
            }
            return mask;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Set mask field label (for localization support)
        /// マスクフィールドラベルを設定（多言語化対応）
        /// </summary>
        public void SetMaskFieldLabel(string label)
        {
            if (maskField != null)
                maskField.label = label;
        }

        /// <summary>
        /// Set previous button tooltip (for localization support)
        /// 前ボタンツールチップを設定（多言語化対応）
        /// </summary>
        public void SetPrevButtonTooltip(string tooltip)
        {
            if (prevButton != null)
                prevButton.tooltip = tooltip;
        }

        /// <summary>
        /// Set next button tooltip (for localization support)
        /// 次ボタンツールチップを設定（多言語化対応）
        /// </summary>
        public void SetNextButtonTooltip(string tooltip)
        {
            if (nextButton != null)
                nextButton.tooltip = tooltip;
        }
        #endregion
    }
}
