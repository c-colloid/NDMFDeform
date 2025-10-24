using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Deform.Masking.Editor
{
    /// <summary>
    /// Helper class for rendering UV textures and drawing operations
    /// UVテクスチャレンダリングと描画操作のヘルパークラス
    /// </summary>
    internal static class UVTextureRenderer
    {
        #region Constants
        private const float BLEND_FACTOR = 0.7f;
        private const float GRID_SPACING = 0.1f;
        private static readonly Color GRID_COLOR = new Color(0.3f, 0.3f, 0.3f, 1.0f);
        private static readonly Color UNSELECTED_ISLAND_COLOR = new Color(0.5f, 0.5f, 0.5f, 0.3f);
        #endregion

        /// <summary>
        /// Draw a simple grid on the UV texture
        /// </summary>
        public static void DrawSimpleGrid(Color[] pixels, int width, int height, Matrix4x4 transform)
        {
            for (float gridPos = 0f; gridPos <= 1f; gridPos += GRID_SPACING)
            {
                // Vertical lines
                DrawTransformedLine(new Vector2(gridPos, 0f), new Vector2(gridPos, 1f), pixels, width, height, transform, GRID_COLOR);
                // Horizontal lines  
                DrawTransformedLine(new Vector2(0f, gridPos), new Vector2(1f, gridPos), pixels, width, height, transform, GRID_COLOR);
            }
        }

        /// <summary>
        /// Draw UV islands on the texture
        /// </summary>
        public static void DrawUVIslands(Color[] pixels, int width, int height, Matrix4x4 transform,
            List<UVIslandAnalyzer.UVIsland> uvIslands, List<int> selectedIslandIDs, Mesh targetMesh)
        {
            if (targetMesh?.uv == null) return;

            var uvs = targetMesh.uv;

            foreach (var island in uvIslands)
            {
                // Get submesh-specific triangles
                if (island.submeshIndex < 0 || island.submeshIndex >= targetMesh.subMeshCount)
                    continue;

                var triangles = targetMesh.GetTriangles(island.submeshIndex);
                if (triangles == null || triangles.Length == 0)
                    continue;

                var color = selectedIslandIDs.Contains(island.islandID) ?
                    new Color(island.maskColor.r, island.maskColor.g, island.maskColor.b, BLEND_FACTOR) :
                    UNSELECTED_ISLAND_COLOR;

                DrawIslandOutline(island, uvs, triangles, pixels, width, height, transform, color);
            }
        }

        /// <summary>
        /// Draw magnifying glass content
        /// </summary>
        public static void DrawMagnifyingContent(Color[] pixels, int width, int height, Vector2 centerUV, float radius,
            List<UVIslandAnalyzer.UVIsland> uvIslands, List<int> selectedIslandIDs, Mesh targetMesh)
        {
            if (targetMesh?.uv == null) return;

            var uvs = targetMesh.uv;

            foreach (var island in uvIslands)
            {
                // Get submesh-specific triangles
                if (island.submeshIndex < 0 || island.submeshIndex >= targetMesh.subMeshCount)
                    continue;

                var triangles = targetMesh.GetTriangles(island.submeshIndex);
                if (triangles == null || triangles.Length == 0)
                    continue;

                var color = selectedIslandIDs.Contains(island.islandID) ?
                    island.maskColor : new Color(0.7f, 0.7f, 0.7f, 0.6f);

                foreach (int triangleIndex in island.triangleIndices)
                {
                    if (!IsValidTriangleIndex(triangleIndex, triangles)) continue;

                    var (uv0, uv1, uv2) = GetTriangleUVs(triangleIndex, triangles, uvs);

                    if (IsTriangleInRadius(uv0, uv1, uv2, centerUV, radius))
                    {
                        DrawMagnifyingTriangleOutline(uv0, uv1, uv2, centerUV, radius, pixels, width, height, color);
                    }
                }
            }
        }

        #region Private Helper Methods
        
        private static void DrawIslandOutline(UVIslandAnalyzer.UVIsland island, Vector2[] uvs, int[] triangles,
            Color[] pixels, int width, int height, Matrix4x4 transform, Color color)
        {
            foreach (int triangleIndex in island.triangleIndices)
            {
                if (!IsValidTriangleIndex(triangleIndex, triangles)) continue;

                var (uv0, uv1, uv2) = GetTriangleUVs(triangleIndex, triangles, uvs);

                DrawTransformedLine(uv0, uv1, pixels, width, height, transform, color);
                DrawTransformedLine(uv1, uv2, pixels, width, height, transform, color);
                DrawTransformedLine(uv2, uv0, pixels, width, height, transform, color);
            }
        }

        private static bool IsValidTriangleIndex(int triangleIndex, int[] triangles)
        {
            int baseIndex = triangleIndex * 3;
            return baseIndex + 2 < triangles.Length;
        }

        private static (Vector2 uv0, Vector2 uv1, Vector2 uv2) GetTriangleUVs(int triangleIndex, int[] triangles, Vector2[] uvs)
        {
            int baseIndex = triangleIndex * 3;
            int vertIndex0 = triangles[baseIndex];
            int vertIndex1 = triangles[baseIndex + 1];
            int vertIndex2 = triangles[baseIndex + 2];

            return (uvs[vertIndex0], uvs[vertIndex1], uvs[vertIndex2]);
        }

        private static bool IsTriangleInRadius(Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 center, float radius)
        {
            return (Vector2.Distance(uv0, center) <= radius ||
                    Vector2.Distance(uv1, center) <= radius ||
                    Vector2.Distance(uv2, center) <= radius);
        }

        private static void DrawMagnifyingTriangleOutline(Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 centerUV, float radius,
            Color[] pixels, int width, int height, Color color)
        {
            DrawMagnifyingLine(uv0, uv1, centerUV, radius, pixels, width, height, color);
            DrawMagnifyingLine(uv1, uv2, centerUV, radius, pixels, width, height, color);
            DrawMagnifyingLine(uv2, uv0, centerUV, radius, pixels, width, height, color);
        }

        private static void DrawMagnifyingLine(Vector2 start, Vector2 end, Vector2 centerUV, float radius,
            Color[] pixels, int width, int height, Color color)
        {
            // Transform UV coordinates to magnifying glass coordinates
            var startLocal = (start - centerUV) / radius * 0.5f + Vector2.one * 0.5f;
            var endLocal = (end - centerUV) / radius * 0.5f + Vector2.one * 0.5f;

            DrawSimpleLine(startLocal, endLocal, pixels, width, height, color);
        }

        private static void DrawTransformedLine(Vector2 start, Vector2 end, Color[] pixels, int width, int height, Matrix4x4 transform, Color color)
        {
            var startTransformed = transform.MultiplyPoint3x4(new Vector3(start.x, start.y, 0f));
            var endTransformed = transform.MultiplyPoint3x4(new Vector3(end.x, end.y, 0f));

            DrawSimpleLine(new Vector2(startTransformed.x, startTransformed.y),
                          new Vector2(endTransformed.x, endTransformed.y), pixels, width, height, color);
        }

        /// <summary>
        /// Draw a line using Bresenham's line algorithm
        /// </summary>
        private static void DrawSimpleLine(Vector2 start, Vector2 end, Color[] pixels, int width, int height, Color color)
        {
            int x0 = Mathf.RoundToInt(start.x * width);
            int y0 = Mathf.RoundToInt(start.y * height);
            int x1 = Mathf.RoundToInt(end.x * width);
            int y1 = Mathf.RoundToInt(end.y * height);

            // Simple Bresenham line algorithm
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (IsValidPixelCoordinate(x0, y0, width, height))
                {
                    int index = y0 * width + x0;
                    if (index >= 0 && index < pixels.Length)
                    {
                        pixels[index] = Color.Lerp(pixels[index], color, BLEND_FACTOR);
                    }
                }

                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private static bool IsValidPixelCoordinate(int x, int y, int width, int height)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }

        #endregion
    }
}