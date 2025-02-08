using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using System.Collections.Generic;
using System.Linq;

namespace Deform.Masking.Editor
{
    [CustomEditor(typeof(UVIslandMask))]
    public class UVIslandMaskEditor : UnityEditor.Editor
    {
        private UVIslandMask mask;
        private VisualElement root;
        private IMGUIContainer uvCanvas;
        private Vector2 panOffset = Vector2.zero;
        private float zoom = 1f;
        private bool isDragging = false;
        private int selectedPointIndex = -1;

        public override VisualElement CreateInspectorGUI()
        {
            mask = (UVIslandMask)target;
	        // Load UXML
	        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
		        "Assets/NDMFDeform/ExDeform/UVIslandMaskEditor.uxml");
	        VisualElement root = visualTree.CloneTree();

            // Load USS
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/NDMFDeform/ExDeform/UVIslandMaskEditor.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

	        // Bind properties
	        var factorField = root.Q<FloatField>("factor-field");
	        factorField.BindProperty(serializedObject.FindProperty("factor"));

	        var falloffField = root.Q<FloatField>("falloff-field");
	        falloffField.BindProperty(serializedObject.FindProperty("falloff"));

	        var invertToggle = root.Q<Toggle>("invert-toggle");
	        invertToggle.BindProperty(serializedObject.FindProperty("invert"));

	        // Setup buttons
	        var resetViewButton = root.Q<ToolbarButton>("reset-view-button");
	        resetViewButton.clicked += ResetView;

	        var clearPointsButton = root.Q<ToolbarButton>("clear-points-button");
	        clearPointsButton.clicked += ClearPoints;

	        // Setup UV canvas
	        uvCanvas = new IMGUIContainer(OnUVCanvasGUI);
	        uvCanvas.AddToClassList("uv-canvas");
	        root.Q("uv-canvas-container").Add(uvCanvas);

	        // Register events
	        uvCanvas.RegisterCallback<WheelEvent>(OnWheel);
	        uvCanvas.RegisterCallback<MouseDownEvent>(OnMouseDown);
	        uvCanvas.RegisterCallback<MouseMoveEvent>(OnMouseMove);
	        uvCanvas.RegisterCallback<MouseUpEvent>(OnMouseUp);
	        uvCanvas.RegisterCallback<KeyDownEvent>(OnKeyDown);

	        return root;
        }

        private void OnUVCanvasGUI()
        {
	        if (mask == null) return;

	        var rect = uvCanvas.contentRect;
	        float size = Mathf.Min(rect.width, rect.height - 10);
	        var uvRect = new Rect((rect.width - size) * 0.5f, (rect.height - size) * 0.5f, size, size);

	        DrawUVGrid(uvRect);
	        DrawSelectionPoints(uvRect);
        }

        private void DrawUVGrid(Rect uvRect)
        {
            EditorGUI.DrawRect(uvRect, new Color(0.2f, 0.2f, 0.2f, 1));

            Handles.color = new Color(0.3f, 0.3f, 0.3f, 1);
            float gridSize = 0.1f;

            for (float x = 0; x <= 1; x += gridSize)
            {
                Vector2 start = UVToScreenPoint(new Vector2(x, 0), uvRect);
                Vector2 end = UVToScreenPoint(new Vector2(x, 1), uvRect);
                Handles.DrawLine(start, end);
            }
            for (float y = 0; y <= 1; y += gridSize)
            {
                Vector2 start = UVToScreenPoint(new Vector2(0, y), uvRect);
                Vector2 end = UVToScreenPoint(new Vector2(1, y), uvRect);
                Handles.DrawLine(start, end);
            }
        }

        private void DrawSelectionPoints(Rect uvRect)
        {
            var points = mask.SelectionPoints;
            if (points == null || points.Count == 0) return;

            // ポリゴンの描画
            if (points.Count >= 3)
            {
                Handles.color = new Color(0, 1, 0, 0.2f);
                var screenPoints = points.Select(p => UVToScreenPoint(p, uvRect)).Select(p => (Vector3)p).ToArray();
                Handles.DrawAAConvexPolygon(screenPoints);
            }

            // 線の描画
            Handles.color = Color.green;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 current = UVToScreenPoint(points[i], uvRect);
                Vector2 next = UVToScreenPoint(points[(i + 1) % points.Count], uvRect);
                Handles.DrawLine(current, next);
            }

            // ポイントの描画
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 screenPoint = UVToScreenPoint(points[i], uvRect);
                float handleSize = 5f;
                
                Handles.color = (selectedPointIndex == i) ? Color.yellow : Color.white;
                Handles.DrawSolidDisc(screenPoint, Vector3.forward, handleSize);
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            zoom = Mathf.Clamp(zoom - evt.delta.y * 0.1f, 0.1f, 10f);
            uvCanvas.MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button == 0)
            {
                var uvRect = GetUVRect();
                Vector2 uvPoint = ScreenToUVPoint(evt.localMousePosition, uvRect);
                
                selectedPointIndex = GetNearestPointIndex(uvPoint);
                
                if (selectedPointIndex == -1)
                {
                    var points = mask.SelectionPoints;
                    points.Add(uvPoint);
                    mask.SelectionPoints = points;
                    EditorUtility.SetDirty(mask);
                }
                
                isDragging = selectedPointIndex != -1;
                uvCanvas.MarkDirtyRepaint();
            }
            else if (evt.button == 2)
            {
                isDragging = true;
            }
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (isDragging)
            {
                if (selectedPointIndex != -1)
                {
                    var uvRect = GetUVRect();
                    Vector2 uvPoint = ScreenToUVPoint(evt.localMousePosition, uvRect);
                    
                    var points = mask.SelectionPoints;
                    points[selectedPointIndex] = uvPoint;
                    mask.SelectionPoints = points;
                    EditorUtility.SetDirty(mask);
                }
                else if (evt.button == 2)
                {
                    panOffset += evt.mouseDelta;
                }
                
                uvCanvas.MarkDirtyRepaint();
            }
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            isDragging = false;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Delete && selectedPointIndex != -1)
            {
                var points = mask.SelectionPoints;
                points.RemoveAt(selectedPointIndex);
                mask.SelectionPoints = points;
                selectedPointIndex = -1;
                EditorUtility.SetDirty(mask);
                uvCanvas.MarkDirtyRepaint();
            }
        }

        private void ResetView()
        {
            zoom = 1f;
            panOffset = Vector2.zero;
            uvCanvas.MarkDirtyRepaint();
        }

        private void ClearPoints()
        {
            if (mask != null)
            {
                mask.SelectionPoints.Clear();
                EditorUtility.SetDirty(mask);
                uvCanvas.MarkDirtyRepaint();
            }
        }

        private Rect GetUVRect()
        {
            var rect = uvCanvas.contentRect;
            float size = Mathf.Min(rect.width, rect.height) - 20;
            return new Rect((rect.width - size) * 0.5f, (rect.height - size) * 0.5f, size, size);
        }

        private int GetNearestPointIndex(Vector2 uvPoint)
        {
            var points = mask.SelectionPoints;
            float minDistance = float.MaxValue;
            int nearestIndex = -1;

            for (int i = 0; i < points.Count; i++)
            {
                float distance = Vector2.Distance(points[i], uvPoint);
                if (distance < minDistance && distance < 0.05f)
                {
                    minDistance = distance;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }

        private Vector2 UVToScreenPoint(Vector2 uvPoint, Rect uvRect)
        {
            return new Vector2(
                uvRect.x + uvRect.width * uvPoint.x * zoom + panOffset.x,
                uvRect.y + uvRect.height * (1 - uvPoint.y) * zoom + panOffset.y
            );
        }

        private Vector2 ScreenToUVPoint(Vector2 screenPoint, Rect uvRect)
        {
            return new Vector2(
                (screenPoint.x - uvRect.x - panOffset.x) / (uvRect.width * zoom),
                1 - (screenPoint.y - uvRect.y - panOffset.y) / (uvRect.height * zoom)
            );
        }
    }
}