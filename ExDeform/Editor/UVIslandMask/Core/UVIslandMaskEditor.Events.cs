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
    /// Mouse Event Handlers partial class for UVIslandMaskEditor
    /// Contains all mouse and keyboard event handling logic
    /// </summary>
    public partial class UVIslandMaskEditor
    {
        #region Mouse Event Handlers
        // マウスイベントハンドラ
        // Mouse event handling for UV map interaction

        private void OnUVMapMouseDown(MouseDownEvent evt)
        {
            if (selector == null) return;
            
            // Any mouse interaction should trigger full resolution mode
            OnUserInteraction();
            
            var localPosition = evt.localMousePosition;
            
            if (evt.button == 0) // Left click
            {
                if (isMagnifyingGlassActive)
                {
                    evt.StopPropagation();
                    return;
                }
                
                if (selector.EnableRangeSelection && evt.shiftKey)
                {
                    // Check for deselection mode (Ctrl+Shift) at the start of range selection
                    isRangeDeselecting = evt.ctrlKey && evt.shiftKey;
                    StartRangeSelection(localPosition);
                }
                else
                {
                    HandleIslandSelection(localPosition);
                }
                evt.StopPropagation();
            }
            else if (evt.button == 2 && !isMagnifyingGlassActive) // Middle click for pan
            {
                isDraggingUVMap = true;
                lastMousePos = localPosition;
                evt.StopPropagation();
            }
            else if (evt.button == 1 && selector.EnableMagnifyingGlass) // Right click for magnifying glass
            {
                StartMagnifyingGlass(localPosition);
                evt.StopPropagation();
                return;
            }
        }
        
        private void HandleIslandSelection(Vector2 localPosition)
        {
            // Use proper coordinate transformation that accounts for zoom and pan
            var uvCoordinate = LocalPosToUV(localPosition);
            
            int islandID = selector.GetIslandAtUVCoordinate(uvCoordinate);
            
            if (islandID >= 0)
            {
                // User interaction detected - switch to full resolution mode
                OnUserInteraction();

                Undo.RecordObject(targetMask, "Toggle UV Island Selection");
                selector.ToggleIslandSelection(islandID);
                UpdateMaskComponent();
                EditorUtility.SetDirty(targetMask);

                // Generate full texture and update display
                selector.GenerateUVMapTexture();
                RefreshUVMapImage();

                RefreshUI(false);
            }
            // Removed automatic pan start on left click - pan is now middle button only
        }
        
        private void OnUVMapMouseMove(MouseMoveEvent evt) => HandleMouseMove(evt);
        private void OnUVMapContainerMouseMove(MouseMoveEvent evt) => HandleMouseMove(evt);
        
        private void HandleMouseMove(MouseMoveEvent evt)
        {
            if (selector == null) return;

            var localPosition = evt.localMousePosition;

            // Update current mouse position for hover detection
            currentUVMapMousePos = localPosition;
            UpdateHoverIsland(localPosition);

            if (isMagnifyingGlassActive)
            {
                UpdateMagnifyingGlass(localPosition);
                evt.StopPropagation();
            }
            else if (selector.IsRangeSelecting)
            {
                UpdateRangeSelection(localPosition);
                evt.StopPropagation();
            }
            else if (isDraggingUVMap && !isMagnifyingGlassActive)
            {
                var deltaPos = localPosition - lastMousePos;
                // Fixed pan sensitivity to maintain consistent movement regardless of zoom
                var panSensitivity = 1f / UV_MAP_SIZE;
                var uvDelta = new Vector2(
                    deltaPos.x * panSensitivity,
                    -deltaPos.y * panSensitivity
                );

                var currentOffset = selector.UvMapPanOffset;
                selector.SetPanOffset(currentOffset + uvDelta);

                lastMousePos = localPosition;

                // Always update with throttling for immediate feedback
                UpdateTextureWithThrottle();

                evt.StopPropagation();
            }
        }

        /// <summary>
        /// Update the hovered island based on current mouse position
        /// 現在のマウス位置に基づいてホバー中のアイランドを更新
        /// </summary>
        private void UpdateHoverIsland(Vector2 localPosition)
        {
            if (selector == null || !selector.ShowIslandNames)
            {
                if (hoveredIslandID != -1)
                {
                    hoveredIslandID = -1;
                    UpdateIslandNamesOverlay();
                    UpdateHoverTooltipOverlay();
                }
                return;
            }

            // Convert local position to UV coordinate
            var uvCoordinate = LocalPosToUV(localPosition);
            int newHoveredID = selector.GetIslandAtUVCoordinate(uvCoordinate);

            // Update if changed
            if (newHoveredID != hoveredIslandID)
            {
                hoveredIslandID = newHoveredID;
                // Trigger redraw of island names overlay (to update abbreviations)
                UpdateIslandNamesOverlay();
                // Trigger redraw of hover tooltip
                UpdateHoverTooltipOverlay();
            }
        }
        
        private void OnUVMapMouseUp(MouseUpEvent evt) => HandleMouseUp(evt);
        private void OnUVMapContainerMouseUp(MouseUpEvent evt) => HandleMouseUp(evt);
        
        private void HandleMouseUp(MouseUpEvent evt)
        {
            if (evt.button == 0) // Left button
            {
                if (isMagnifyingGlassActive)
                {
                    HandleMagnifyingGlassClick(evt);
                }
                else if (selector?.IsRangeSelecting == true)
                {
                    bool addToSelection = evt.shiftKey && !evt.ctrlKey;
                    bool removeFromSelection = evt.ctrlKey && evt.shiftKey;
                    isRangeDeselecting = removeFromSelection;
                    FinishRangeSelection(addToSelection, removeFromSelection);
                }
                
                evt.StopPropagation();
            }
            else if (evt.button == 2) // Middle button - stop panning
            {
                isDraggingUVMap = false;

                // Always update texture after mouse interaction ends
                selector?.UpdateTextureIfNeeded();
                RefreshUVMapImage();

                evt.StopPropagation();
            }
            else if (evt.button == 1) // Right button
            {
                StopMagnifyingGlass();
                evt.StopPropagation();
            }
        }
        
        private void OnUVMapWheel(WheelEvent evt)
        {
            if (selector == null) return;
            
            // Zoom interaction should trigger full resolution mode
            OnUserInteraction();
            
            var localPosition = evt.localMousePosition;
            var zoomPoint = LocalPosToUV(localPosition);
            var zoomDelta = -evt.delta.y * 0.1f;
            
            selector.ZoomAtPoint(zoomPoint, zoomDelta);
            zoomSlider.value = selector.UvMapZoom;
            UpdateZoomSliderLabel();

            // Always update with throttling for immediate feedback
            UpdateTextureWithThrottle();

            evt.StopPropagation();
        }
        
        private void OnRootMouseMove(MouseMoveEvent evt)
        {
            if (selector == null) return;

            var containerWorldBound = uvMapContainer.worldBound;
            var relativeX = evt.mousePosition.x - containerWorldBound.x;
            var relativeY = evt.mousePosition.y - containerWorldBound.y;
            var localPos = new Vector2(relativeX, relativeY);

            // Check if mouse is outside UV map bounds
            bool isOutsideUVMap = localPos.x < 0 || localPos.x > UV_MAP_SIZE ||
                                  localPos.y < 0 || localPos.y > UV_MAP_SIZE;

            if (isOutsideUVMap && hoveredIslandID != -1)
            {
                // Reset hover state when mouse leaves UV map
                hoveredIslandID = -1;
                UpdateIslandNamesOverlay();
                UpdateHoverTooltipOverlay();
            }

            if (selector.IsRangeSelecting)
            {
                var clampedPos = new Vector2(
                    Mathf.Clamp(localPos.x, 0, UV_MAP_SIZE),
                    Mathf.Clamp(localPos.y, 0, UV_MAP_SIZE)
                );

                var uvCoord = LocalPosToUV(clampedPos);
                selector.UpdateRangeSelection(uvCoord);

	            bool removeFromSelection = evt.ctrlKey && evt.shiftKey;
                // Update deselection mode state based on current key state during dragging
                // Use Input class for cross-platform key detection
	            isRangeDeselecting = removeFromSelection;

                UpdateRangeSelectionVisual();
                evt.StopPropagation();
            }
            else if (isMagnifyingGlassActive)
            {
                var clampedPos = new Vector2(
                    Mathf.Clamp(localPos.x, 0, UV_MAP_SIZE),
                    Mathf.Clamp(localPos.y, 0, UV_MAP_SIZE)
                );

                UpdateMagnifyingGlass(clampedPos);
                evt.StopPropagation();
            }
            else if (isDraggingUVMap)
            {
                var clampedPos = new Vector2(
                    Mathf.Clamp(localPos.x, 0, UV_MAP_SIZE),
                    Mathf.Clamp(localPos.y, 0, UV_MAP_SIZE)
                );

                var deltaPos = clampedPos - lastMousePos;
                // Fixed pan sensitivity to maintain consistent movement regardless of zoom
                var panSensitivity = 1f / UV_MAP_SIZE;
                var uvDelta = new Vector2(
                    deltaPos.x * panSensitivity,
                    -deltaPos.y * panSensitivity
                );

                var currentOffset = selector.UvMapPanOffset;
                selector.SetPanOffset(currentOffset + uvDelta);

                lastMousePos = clampedPos;

                // Always update with throttling for immediate feedback
                UpdateTextureWithThrottle();

                evt.StopPropagation();
            }
        }
        
        private void OnRootMouseUp(MouseUpEvent evt)
        {
            if (selector == null) return;
            
            if (evt.button == 0)
            {
                if (selector.IsRangeSelecting)
                {
                    bool addToSelection = evt.shiftKey && !evt.ctrlKey;
                    bool removeFromSelection = evt.ctrlKey && evt.shiftKey;
                    isRangeDeselecting = removeFromSelection;
                    FinishRangeSelection(addToSelection, removeFromSelection);
                    evt.StopPropagation();
                }
                else if (isDraggingUVMap)
                {
                    isDraggingUVMap = false;

                    // Always update texture after mouse interaction ends
                    selector?.UpdateTextureIfNeeded();
                    RefreshUVMapImage();

                    evt.StopPropagation();
                }
            }
            else if (evt.button == 2)
            {
                if (isDraggingUVMap)
                {
                    isDraggingUVMap = false;

                    // Always update texture after mouse interaction ends
                    selector?.UpdateTextureIfNeeded();
                    RefreshUVMapImage();
                    
                    evt.StopPropagation();
                }
            }
            else if (evt.button == 1)
            {
                if (isMagnifyingGlassActive)
                {
                    StopMagnifyingGlass();
                    evt.StopPropagation();
                }
            }
        }

        #endregion
    }
}
