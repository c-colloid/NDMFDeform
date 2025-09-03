using UnityEngine;

namespace ExDeform.Editor
{
    /// <summary>
    /// Test resource management for cache performance tests
    /// キャッシュパフォーマンステスト用テストリソース管理
    /// </summary>
    public static class TestResourceManager
    {
        /// <summary>
        /// Create test texture with gradient and noise pattern
        /// グラデーションとノイズパターンのテストテクスチャを作成
        /// </summary>
        /// <returns>Test texture</returns>
        public static Texture2D CreateTestTexture()
        {
            var texture = new Texture2D(CacheConstants.TEST_TEXTURE_SIZE, CacheConstants.TEST_TEXTURE_SIZE, 
                CacheConstants.TEST_TEXTURE_FORMAT, false);
            var pixels = new Color[CacheConstants.TEST_TEXTURE_SIZE * CacheConstants.TEST_TEXTURE_SIZE];
            
            // Create test pattern (gradient + noise for realistic data)
            for (int y = 0; y < CacheConstants.TEST_TEXTURE_SIZE; y++)
            {
                for (int x = 0; x < CacheConstants.TEST_TEXTURE_SIZE; x++)
                {
                    var index = y * CacheConstants.TEST_TEXTURE_SIZE + x;
                    var noise = Mathf.PerlinNoise(x * 0.1f, y * 0.1f) * 0.2f;
                    pixels[index] = new Color(
                        (float)x / CacheConstants.TEST_TEXTURE_SIZE + noise,
                        (float)y / CacheConstants.TEST_TEXTURE_SIZE + noise,
                        0.5f + noise,
                        1.0f
                    );
                }
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Create simple solid color texture for basic tests
        /// 基本テスト用の単色テクスチャを作成
        /// </summary>
        /// <param name="color">Texture color</param>
        /// <param name="size">Texture size</param>
        /// <returns>Solid color texture</returns>
        public static Texture2D CreateSolidColorTexture(Color color, int size = 64)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Safely dispose of test texture
        /// テストテクスチャの安全な破棄
        /// </summary>
        /// <param name="texture">Texture to dispose</param>
        public static void DisposeTestTexture(Texture2D texture)
        {
            if (texture != null)
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// Generate unique test key based on current time
        /// 現在時刻に基づくユニークなテストキー生成
        /// </summary>
        /// <param name="prefix">Key prefix</param>
        /// <returns>Unique test key</returns>
        public static string GenerateTestKey(string prefix = "test")
        {
            return $"{prefix}_{System.DateTime.Now.Ticks}";
        }

        /// <summary>
        /// Create multiple test textures with different patterns
        /// 異なるパターンの複数テストテクスチャを作成
        /// </summary>
        /// <param name="count">Number of textures to create</param>
        /// <returns>Array of test textures</returns>
        public static Texture2D[] CreateMultipleTestTextures(int count)
        {
            var textures = new Texture2D[count];
            
            for (int i = 0; i < count; i++)
            {
                var hue = (float)i / count;
                var baseColor = Color.HSVToRGB(hue, 0.7f, 0.9f);
                textures[i] = CreateSolidColorTexture(baseColor);
            }
            
            return textures;
        }

        /// <summary>
        /// Dispose multiple test textures
        /// 複数テストテクスチャの破棄
        /// </summary>
        /// <param name="textures">Textures to dispose</param>
        public static void DisposeMultipleTestTextures(Texture2D[] textures)
        {
            if (textures != null)
            {
                foreach (var texture in textures)
                {
                    DisposeTestTexture(texture);
                }
            }
        }
    }
}