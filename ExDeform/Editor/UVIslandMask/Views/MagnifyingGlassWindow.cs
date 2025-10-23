using UnityEngine;
using UnityEditor;

namespace Deform.Masking.Editor
{
    /// <summary>
    /// ルーペ機能用のエディタウィンドウ
    /// </summary>
    public class MagnifyingGlassWindow : EditorWindow
    {
        private Texture2D magnifyingTexture;
        private Vector2 mousePosition;
        private Vector2 uvCoordinate;
        private static MagnifyingGlassWindow instance;

        /// <summary>
        /// ルーペウィンドウを表示
        /// </summary>
        public static void ShowWindow(Texture2D texture, Vector2 mousePos, Vector2 uvCoord)
        {
            if (instance != null)
            {
                instance.Close();
            }

            instance = CreateInstance<MagnifyingGlassWindow>();
            instance.magnifyingTexture = texture;
            instance.mousePosition = mousePos;
            instance.uvCoordinate = uvCoord;

            // ウィンドウサイズを設定
            var size = new Vector2(texture.width + 40, texture.height + 60);
            instance.position = new Rect(
                Event.current.mousePosition + Vector2.one * 10,
                size
            );

            instance.titleContent = new GUIContent("Magnifying Glass");
            instance.ShowPopup();

            // 3秒後に自動で閉じる
            EditorApplication.delayCall += () =>
            {
                if (instance != null)
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (instance != null)
                        {
                            instance.Close();
                        }
                    };
                }
            };
        }

        private void OnGUI()
        {
            if (magnifyingTexture == null)
            {
                Close();
                return;
            }

            EditorGUILayout.BeginVertical();

            // UV座標情報を表示
            EditorGUILayout.LabelField($"UV: ({uvCoordinate.x:F3}, {uvCoordinate.y:F3})", EditorStyles.boldLabel);

            // ルーペ画像を表示
            var rect = GUILayoutUtility.GetRect(magnifyingTexture.width, magnifyingTexture.height);
            EditorGUI.DrawTextureTransparent(rect, magnifyingTexture);

            EditorGUILayout.EndVertical();

            // ESCキーまたはクリックで閉じる
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
            }
            else if (Event.current.type == EventType.MouseDown)
            {
                Close();
            }
        }

        private void OnDestroy()
        {
            if (magnifyingTexture != null)
            {
                DestroyImmediate(magnifyingTexture);
            }
            instance = null;
        }
    }
}