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

        #region Font Rendering
        // Simple 5x7 bitmap font data for basic ASCII characters
        // シンプルな5x7ビットマップフォントデータ
        private static readonly Dictionary<char, byte[]> bitmapFont = new Dictionary<char, byte[]>()
        {
            // Numbers 0-9
            {'0', new byte[] { 0x70, 0x88, 0x98, 0xA8, 0xC8, 0x88, 0x70 }},
            {'1', new byte[] { 0x20, 0x60, 0x20, 0x20, 0x20, 0x20, 0x70 }},
            {'2', new byte[] { 0x70, 0x88, 0x08, 0x10, 0x20, 0x40, 0xF8 }},
            {'3', new byte[] { 0x70, 0x88, 0x08, 0x30, 0x08, 0x88, 0x70 }},
            {'4', new byte[] { 0x10, 0x30, 0x50, 0x90, 0xF8, 0x10, 0x10 }},
            {'5', new byte[] { 0xF8, 0x80, 0xF0, 0x08, 0x08, 0x88, 0x70 }},
            {'6', new byte[] { 0x30, 0x40, 0x80, 0xF0, 0x88, 0x88, 0x70 }},
            {'7', new byte[] { 0xF8, 0x08, 0x10, 0x20, 0x40, 0x40, 0x40 }},
            {'8', new byte[] { 0x70, 0x88, 0x88, 0x70, 0x88, 0x88, 0x70 }},
            {'9', new byte[] { 0x70, 0x88, 0x88, 0x78, 0x08, 0x10, 0x60 }},

            // Uppercase A-Z
            {'A', new byte[] { 0x20, 0x50, 0x88, 0x88, 0xF8, 0x88, 0x88 }},
            {'B', new byte[] { 0xF0, 0x88, 0x88, 0xF0, 0x88, 0x88, 0xF0 }},
            {'C', new byte[] { 0x70, 0x88, 0x80, 0x80, 0x80, 0x88, 0x70 }},
            {'D', new byte[] { 0xF0, 0x88, 0x88, 0x88, 0x88, 0x88, 0xF0 }},
            {'E', new byte[] { 0xF8, 0x80, 0x80, 0xF0, 0x80, 0x80, 0xF8 }},
            {'F', new byte[] { 0xF8, 0x80, 0x80, 0xF0, 0x80, 0x80, 0x80 }},
            {'G', new byte[] { 0x70, 0x88, 0x80, 0x98, 0x88, 0x88, 0x70 }},
            {'H', new byte[] { 0x88, 0x88, 0x88, 0xF8, 0x88, 0x88, 0x88 }},
            {'I', new byte[] { 0x70, 0x20, 0x20, 0x20, 0x20, 0x20, 0x70 }},
            {'J', new byte[] { 0x38, 0x10, 0x10, 0x10, 0x10, 0x90, 0x60 }},
            {'K', new byte[] { 0x88, 0x90, 0xA0, 0xC0, 0xA0, 0x90, 0x88 }},
            {'L', new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0xF8 }},
            {'M', new byte[] { 0x88, 0xD8, 0xA8, 0xA8, 0x88, 0x88, 0x88 }},
            {'N', new byte[] { 0x88, 0xC8, 0xA8, 0x98, 0x88, 0x88, 0x88 }},
            {'O', new byte[] { 0x70, 0x88, 0x88, 0x88, 0x88, 0x88, 0x70 }},
            {'P', new byte[] { 0xF0, 0x88, 0x88, 0xF0, 0x80, 0x80, 0x80 }},
            {'Q', new byte[] { 0x70, 0x88, 0x88, 0x88, 0xA8, 0x90, 0x68 }},
            {'R', new byte[] { 0xF0, 0x88, 0x88, 0xF0, 0xA0, 0x90, 0x88 }},
            {'S', new byte[] { 0x70, 0x88, 0x80, 0x70, 0x08, 0x88, 0x70 }},
            {'T', new byte[] { 0xF8, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20 }},
            {'U', new byte[] { 0x88, 0x88, 0x88, 0x88, 0x88, 0x88, 0x70 }},
            {'V', new byte[] { 0x88, 0x88, 0x88, 0x50, 0x50, 0x20, 0x20 }},
            {'W', new byte[] { 0x88, 0x88, 0x88, 0xA8, 0xA8, 0xD8, 0x88 }},
            {'X', new byte[] { 0x88, 0x88, 0x50, 0x20, 0x50, 0x88, 0x88 }},
            {'Y', new byte[] { 0x88, 0x88, 0x50, 0x20, 0x20, 0x20, 0x20 }},
            {'Z', new byte[] { 0xF8, 0x08, 0x10, 0x20, 0x40, 0x80, 0xF8 }},

            // Lowercase a-z (simplified)
            {'a', new byte[] { 0x00, 0x00, 0x70, 0x08, 0x78, 0x88, 0x78 }},
            {'b', new byte[] { 0x80, 0x80, 0xB0, 0xC8, 0x88, 0x88, 0xF0 }},
            {'c', new byte[] { 0x00, 0x00, 0x70, 0x80, 0x80, 0x88, 0x70 }},
            {'d', new byte[] { 0x08, 0x08, 0x68, 0x98, 0x88, 0x88, 0x78 }},
            {'e', new byte[] { 0x00, 0x00, 0x70, 0x88, 0xF8, 0x80, 0x70 }},
            {'f', new byte[] { 0x30, 0x48, 0x40, 0xE0, 0x40, 0x40, 0x40 }},
            {'g', new byte[] { 0x00, 0x00, 0x78, 0x88, 0x88, 0x78, 0x08, 0x70 }},
            {'h', new byte[] { 0x80, 0x80, 0xB0, 0xC8, 0x88, 0x88, 0x88 }},
            {'i', new byte[] { 0x20, 0x00, 0x60, 0x20, 0x20, 0x20, 0x70 }},
            {'j', new byte[] { 0x10, 0x00, 0x30, 0x10, 0x10, 0x90, 0x60 }},
            {'k', new byte[] { 0x80, 0x80, 0x90, 0xA0, 0xC0, 0xA0, 0x90 }},
            {'l', new byte[] { 0x60, 0x20, 0x20, 0x20, 0x20, 0x20, 0x70 }},
            {'m', new byte[] { 0x00, 0x00, 0xD0, 0xA8, 0xA8, 0x88, 0x88 }},
            {'n', new byte[] { 0x00, 0x00, 0xB0, 0xC8, 0x88, 0x88, 0x88 }},
            {'o', new byte[] { 0x00, 0x00, 0x70, 0x88, 0x88, 0x88, 0x70 }},
            {'p', new byte[] { 0x00, 0x00, 0xF0, 0x88, 0x88, 0xF0, 0x80, 0x80 }},
            {'q', new byte[] { 0x00, 0x00, 0x78, 0x88, 0x88, 0x78, 0x08, 0x08 }},
            {'r', new byte[] { 0x00, 0x00, 0xB0, 0xC8, 0x80, 0x80, 0x80 }},
            {'s', new byte[] { 0x00, 0x00, 0x78, 0x80, 0x70, 0x08, 0xF0 }},
            {'t', new byte[] { 0x40, 0x40, 0xE0, 0x40, 0x40, 0x48, 0x30 }},
            {'u', new byte[] { 0x00, 0x00, 0x88, 0x88, 0x88, 0x98, 0x68 }},
            {'v', new byte[] { 0x00, 0x00, 0x88, 0x88, 0x50, 0x50, 0x20 }},
            {'w', new byte[] { 0x00, 0x00, 0x88, 0x88, 0xA8, 0xA8, 0x50 }},
            {'x', new byte[] { 0x00, 0x00, 0x88, 0x50, 0x20, 0x50, 0x88 }},
            {'y', new byte[] { 0x00, 0x00, 0x88, 0x88, 0x78, 0x08, 0x70 }},
            {'z', new byte[] { 0x00, 0x00, 0xF8, 0x10, 0x20, 0x40, 0xF8 }},

            // Special characters
            {' ', new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }},
            {'-', new byte[] { 0x00, 0x00, 0x00, 0xF8, 0x00, 0x00, 0x00 }},
            {'_', new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF8 }},
            {'.', new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x60, 0x60 }},
            {',', new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x60, 0x20, 0x40 }},
        };

        /// <summary>
        /// Draw text using simple bitmap font
        /// シンプルなビットマップフォントでテキストを描画
        /// </summary>
        public static void DrawText(string text, Vector2 position, Color[] pixels, int width, int height,
            Color textColor, Color shadowColor, int scale = 1)
        {
            if (string.IsNullOrEmpty(text)) return;

            int x = Mathf.RoundToInt(position.x);
            int y = Mathf.RoundToInt(position.y);

            // Calculate text width for centering
            int charWidth = 6 * scale; // 5 pixels + 1 spacing
            int totalWidth = text.Length * charWidth;
            int startX = x - totalWidth / 2;
            int currentX = startX;

            foreach (char c in text)
            {
                char upperChar = char.ToUpper(c);
                if (bitmapFont.ContainsKey(upperChar))
                {
                    // Draw shadow first (offset by 1 pixel)
                    DrawBitmapChar(bitmapFont[upperChar], currentX + 1, y + 1, pixels, width, height, shadowColor, scale);

                    // Draw main character
                    DrawBitmapChar(bitmapFont[upperChar], currentX, y, pixels, width, height, textColor, scale);
                }
                currentX += charWidth;
            }
        }

        /// <summary>
        /// Draw a single bitmap character
        /// ビットマップ文字を1文字描画
        /// </summary>
        private static void DrawBitmapChar(byte[] charData, int x, int y, Color[] pixels, int width, int height,
            Color color, int scale)
        {
            for (int row = 0; row < charData.Length && row < 8; row++)
            {
                byte rowData = charData[row];
                for (int col = 0; col < 8; col++)
                {
                    if ((rowData & (1 << (7 - col))) != 0)
                    {
                        // Draw scaled pixel
                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int pixelX = x + col * scale + sx;
                                int pixelY = y + row * scale + sy;

                                if (pixelX >= 0 && pixelX < width && pixelY >= 0 && pixelY < height)
                                {
                                    int index = pixelY * width + pixelX;
                                    if (index >= 0 && index < pixels.Length)
                                    {
                                        pixels[index] = Color.Lerp(pixels[index], color, 0.9f);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Draw island names on UV islands
        /// UVアイランド上にアイランド名を描画
        /// </summary>
        public static void DrawIslandNames(Color[] pixels, int width, int height, Matrix4x4 transform,
            List<UVIslandAnalyzer.UVIsland> uvIslands)
        {
            if (uvIslands == null || uvIslands.Count == 0) return;

            foreach (var island in uvIslands)
            {
                // Get display name (custom name or default)
                string displayName = !string.IsNullOrEmpty(island.customName)
                    ? island.customName
                    : $"Island {island.islandID}";

                // Calculate island center in UV space
                Vector2 islandCenter = island.uvBounds.center;

                // Transform to texture space
                Vector3 uvPos = new Vector3(islandCenter.x, islandCenter.y, 0f);
                Vector3 transformedPos = transform.MultiplyPoint3x4(uvPos);

                // Convert to pixel coordinates
                int x = Mathf.RoundToInt(transformedPos.x * width);
                int y = Mathf.RoundToInt((1f - transformedPos.y) * height); // Flip Y

                // Skip if outside visible area
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;

                // Draw text with shadow (scale=1 for compact text)
                DrawText(displayName, new Vector2(x, y), pixels, width, height,
                    Color.white, Color.black, 1);
            }
        }
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