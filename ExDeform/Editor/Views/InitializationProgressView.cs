using UnityEngine;
using UnityEngine.UIElements;

namespace Deform.Masking.Editor.Views
{
    /// <summary>
    /// Custom VisualElement for displaying asynchronous initialization progress
    /// 非同期初期化の進捗を表示するカスタムVisualElement
    /// </summary>
    public class InitializationProgressView : VisualElement
    {
        #region UxmlFactory
        public new class UxmlFactory : UxmlFactory<InitializationProgressView, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlFloatAttributeDescription m_Progress = new UxmlFloatAttributeDescription
            {
                name = "progress",
                defaultValue = 0f
            };

            UxmlStringAttributeDescription m_StatusMessage = new UxmlStringAttributeDescription
            {
                name = "status-message",
                defaultValue = "Initializing..."
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var view = ve as InitializationProgressView;
                view.Progress = m_Progress.GetValueFromBag(bag, cc);
                view.StatusMessage = m_StatusMessage.GetValueFromBag(bag, cc);
            }
        }
        #endregion

        #region USS Class Names (BEM Convention)
        private static readonly string ussClassName = "init-progress-view";
        private static readonly string labelUssClassName = ussClassName + "__label";
        private static readonly string barUssClassName = ussClassName + "__bar";
        #endregion

        #region UI Elements
        private ProgressBar progressBar;
        private Label statusLabel;
        #endregion

        #region Private Fields
        private float m_Progress;
        private string m_StatusMessage;
        #endregion

        #region Public Properties
        /// <summary>
        /// Progress value (0.0 to 1.0)
        /// 進捗値（0.0～1.0）
        /// </summary>
        public float Progress
        {
            get => m_Progress;
            set
            {
                m_Progress = Mathf.Clamp01(value);
                if (progressBar != null)
                    progressBar.value = m_Progress;
            }
        }

        /// <summary>
        /// Status message text
        /// ステータスメッセージテキスト
        /// </summary>
        public string StatusMessage
        {
            get => m_StatusMessage;
            set
            {
                m_StatusMessage = value ?? string.Empty;
                if (statusLabel != null)
                    statusLabel.text = m_StatusMessage;
            }
        }
        #endregion

        #region Constructor
        /// <summary>
        /// Default constructor
        /// デフォルトコンストラクタ
        /// </summary>
        public InitializationProgressView()
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
            style.paddingTop = 10;
            style.paddingBottom = 10;
            style.paddingLeft = 15;
            style.paddingRight = 15;
            style.backgroundColor = new Color(0.22f, 0.22f, 0.22f, 0.8f);
            style.borderBottomLeftRadius = 4;
            style.borderBottomRightRadius = 4;
            style.borderTopLeftRadius = 4;
            style.borderTopRightRadius = 4;

            // Status label
            statusLabel = new Label(m_StatusMessage)
            {
                style =
                {
                    fontSize = 11,
                    color = new Color(0.8f, 0.8f, 0.8f, 1f),
                    marginBottom = 5,
                    unityTextAlign = TextAnchor.MiddleCenter
                }
            };
            statusLabel.AddToClassList(labelUssClassName);
            Add(statusLabel);

            // Progress bar
            progressBar = new ProgressBar
            {
                lowValue = 0f,
                highValue = 1f,
                value = m_Progress,
                style = { height = 20 }
            };
            progressBar.AddToClassList(barUssClassName);
            Add(progressBar);
        }
        #endregion
    }
}
