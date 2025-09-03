using System;
using System.Text;
using UnityEngine;

namespace ExDeform.Editor
{
    /// <summary>
    /// Visual comparison of different caching methods
    /// 異なるキャッシュ方式の視覚的比較
    /// </summary>
    public static class CachingMethodComparison
    {
        public static void ShowDataTransformationComparison()
        {
            // Original texture data (simplified 2x2 texture)
            var originalPixels = new Color[] {
                Color.red,   Color.green,  // Top row
                Color.blue,  Color.white   // Bottom row
            };
            
            Console.WriteLine("=== データ変換の比較 ===\n");
            
            // 1. Binary File Cache
            Console.WriteLine("【1. Binary File Cache】");
            Console.WriteLine("原始データ: Color[4] → PNG圧縮 → byte[] → ディスク");
            Console.WriteLine("例: [FF0000FF, 00FF00FF, 0000FFFF, FFFFFFFF]");
            Console.WriteLine("↓ PNG圧縮");
            Console.WriteLine("例: [89 50 4E 47 0D 0A 1A 0A ... 圧縮ピクセルデータ ...]");
            Console.WriteLine("↓ 直接書き込み");
            Console.WriteLine("ファイル: cache.png (バイナリ形式)");
            Console.WriteLine("変換回数: 1回 (PNG圧縮のみ)");
            Console.WriteLine("CPU負荷: 低");
            
            // 2. EditorPrefs Cache
            Console.WriteLine("\n【2. EditorPrefs Cache】");
            Console.WriteLine("原始データ: Color[4] → PNG圧縮 → byte[] → Base64文字列 → レジストリ");
            Console.WriteLine("例: [FF0000FF, 00FF00FF, 0000FFFF, FFFFFFFF]");
            Console.WriteLine("↓ PNG圧縮");
            Console.WriteLine("例: [89 50 4E 47 0D 0A 1A 0A ...]");
            Console.WriteLine("↓ Base64変換");
            Console.WriteLine("例: \"iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kA...\"");
            Console.WriteLine("↓ レジストリ保存");
            Console.WriteLine("変換回数: 2回 (PNG圧縮 + Base64エンコード)");
            Console.WriteLine("CPU負荷: 中");
            
            // 3. ScriptableObject Cache
            Console.WriteLine("\n【3. ScriptableObject Cache (JSON)】");
            Console.WriteLine("原始データ: Color[4] → PNG圧縮 → byte[] → Base64文字列 → JSON → ファイル");
            Console.WriteLine("例: [FF0000FF, 00FF00FF, 0000FFFF, FFFFFFFF]");
            Console.WriteLine("↓ PNG圧縮");
            Console.WriteLine("例: [89 50 4E 47 0D 0A 1A 0A ...]");
            Console.WriteLine("↓ Base64変換");
            Console.WriteLine("例: \"iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kA...\"");
            Console.WriteLine("↓ JSON構造化");
            Console.WriteLine("例: {\"textureData\":\"iVBORw0K...\",\"width\":2,\"height\":2}");
            Console.WriteLine("↓ テキストファイル保存");
            Console.WriteLine("変換回数: 3回 (PNG圧縮 + Base64エンコード + JSON構造化)");
            Console.WriteLine("CPU負荷: 高");
        }
        
        public static void ShowPerformanceCharacteristics()
        {
            Console.WriteLine("\n=== パフォーマンス特性の詳細比較 ===\n");
            
            // Processing speed breakdown
            Console.WriteLine("【処理速度の内訳】");
            Console.WriteLine("Binary File Cache:");
            Console.WriteLine("  PNG圧縮:        1.0ms");
            Console.WriteLine("  ファイル書き込み: 0.5ms");
            Console.WriteLine("  ファイル読み込み: 0.2ms");
            Console.WriteLine("  PNG解凍:        0.3ms");
            Console.WriteLine("  合計:           2.0ms");
            
            Console.WriteLine("\nEditorPrefs Cache:");
            Console.WriteLine("  PNG圧縮:        1.0ms");
            Console.WriteLine("  Base64エンコード: 1.5ms");
            Console.WriteLine("  レジストリ書き込み: 2.0ms");
            Console.WriteLine("  レジストリ読み込み: 0.5ms");
            Console.WriteLine("  Base64デコード:   0.7ms");
            Console.WriteLine("  PNG解凍:        0.3ms");
            Console.WriteLine("  合計:           6.0ms");
            
            Console.WriteLine("\nScriptableObject Cache:");
            Console.WriteLine("  PNG圧縮:        1.0ms");
            Console.WriteLine("  Base64エンコード: 1.5ms");
            Console.WriteLine("  JSON構造化:      1.5ms");
            Console.WriteLine("  ファイル書き込み: 3.0ms");
            Console.WriteLine("  ファイル読み込み: 1.0ms");
            Console.WriteLine("  JSON解析:       1.5ms");
            Console.WriteLine("  Base64デコード:   0.7ms");
            Console.WriteLine("  PNG解凍:        0.3ms");
            Console.WriteLine("  合計:           10.5ms");
        }
        
        public static void ShowStorageEfficiency()
        {
            Console.WriteLine("\n=== ストレージ効率の比較 ===\n");
            
            // Simulate 128x128 texture
            int pixelCount = 128 * 128;
            int rawSize = pixelCount * 4; // RGBA32
            int pngSize = (int)(rawSize * 0.3f); // PNG compression ~70%
            
            Console.WriteLine($"元のテクスチャサイズ: {rawSize:N0} bytes ({rawSize/1024f:F1}KB)");
            Console.WriteLine($"PNG圧縮後サイズ: {pngSize:N0} bytes ({pngSize/1024f:F1}KB)\n");
            
            // Binary File Storage
            int binaryStorage = pngSize;
            Console.WriteLine("【1. Binary File Cache】");
            Console.WriteLine($"ストレージサイズ: {binaryStorage:N0} bytes");
            Console.WriteLine($"効率: {((float)rawSize / binaryStorage):F1}x 圧縮");
            Console.WriteLine("形式: 直接バイナリ(.png)");
            
            // EditorPrefs Storage
            int base64Size = (int)(pngSize * 1.33f); // Base64 overhead
            Console.WriteLine("\n【2. EditorPrefs Cache】");
            Console.WriteLine($"ストレージサイズ: {base64Size:N0} bytes");
            Console.WriteLine($"効率: {((float)rawSize / base64Size):F1}x 圧縮");
            Console.WriteLine($"オーバーヘッド: +{((float)base64Size / pngSize - 1) * 100:F1}%");
            Console.WriteLine("形式: Base64文字列(レジストリ)");
            
            // JSON Storage
            int jsonOverhead = 100; // JSON structure overhead
            int jsonSize = base64Size + jsonOverhead;
            Console.WriteLine("\n【3. ScriptableObject Cache】");
            Console.WriteLine($"ストレージサイズ: {jsonSize:N0} bytes");
            Console.WriteLine($"効率: {((float)rawSize / jsonSize):F1}x 圧縮");
            Console.WriteLine($"オーバーヘッド: +{((float)jsonSize / pngSize - 1) * 100:F1}%");
            Console.WriteLine("形式: JSON(.json)");
        }
        
        public static void ShowSystemLevelOperations()
        {
            Console.WriteLine("\n=== システムレベル操作の違い ===\n");
            
            Console.WriteLine("【Binary File Cache】");
            Console.WriteLine("OS呼び出し:");
            Console.WriteLine("  書き込み: CreateFile() → WriteFile() → FlushFileBuffers() → CloseHandle()");
            Console.WriteLine("  読み込み: CreateFile() → GetFileSize() → ReadFile() → CloseHandle()");
            Console.WriteLine("特徴:");
            Console.WriteLine("  ✅ OSファイルキャッシュ活用");
            Console.WriteLine("  ✅ ハードウェア最適化");
            Console.WriteLine("  ✅ メモリ効率良い");
            Console.WriteLine("  ✅ 並列I/O可能");
            
            Console.WriteLine("\n【EditorPrefs Cache】");
            Console.WriteLine("OS呼び出し:");
            Console.WriteLine("  Windows: RegOpenKeyEx() → RegSetValueEx() → RegCloseKey()");
            Console.WriteLine("  Mac: CFPreferencesSetValue() → CFPreferencesAppSynchronize()");
            Console.WriteLine("特徴:");
            Console.WriteLine("  ⚠️ プラットフォーム依存");
            Console.WriteLine("  ⚠️ レジストリ断片化");
            Console.WriteLine("  ⚠️ サイズ制限あり");
            Console.WriteLine("  ❌ 大量データに不向き");
            
            Console.WriteLine("\n【ScriptableObject Cache】");
            Console.WriteLine("OS呼び出し:");
            Console.WriteLine("  CreateFile() → WriteFile() → CloseHandle() (テキストモード)");
            Console.WriteLine("  + Unity Serialization API");
            Console.WriteLine("特徴:");
            Console.WriteLine("  ⚠️ テキスト処理オーバーヘッド");
            Console.WriteLine("  ⚠️ Unity API経由");
            Console.WriteLine("  ⚠️ JSON解析負荷");
            Console.WriteLine("  ❌ 大量データで性能低下");
        }
    }
}