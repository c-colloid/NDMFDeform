using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace ExDeform.Editor
{
    /// <summary>
    /// Binary File Cache mechanism explanation with visual examples
    /// Binary File Cacheの仕組みの詳細解説とビジュアル例
    /// </summary>
    public static class BinaryCacheExplanation
    {
        /// <summary>
        /// Step-by-step explanation of how Binary Cache works
        /// Binary Cacheの動作を段階的に説明
        /// </summary>
        [MenuItem("Tools/Binary Cache/Show Mechanism Explanation")]
        public static void ExplainMechanism()
        {
            Debug.Log("=== Binary File Cache Mechanism Explanation ===\n");
            
            // Create example texture
            var texture = CreateExampleTexture();
            
            Debug.Log("1. ORIGINAL DATA (Texture2D in Memory):");
            Debug.Log($"   - Size: {texture.width}x{texture.height}");
            Debug.Log($"   - Format: {texture.format}");
            Debug.Log($"   - Memory Size: {texture.width * texture.height * 4} bytes (RGBA32)");
            
            // Step 1: Convert to PNG (binary compression)
            var pngData = texture.EncodeToPNG();
            Debug.Log($"\n2. PNG ENCODING (Binary Compression):");
            Debug.Log($"   - Original: {texture.width * texture.height * 4} bytes");
            Debug.Log($"   - Compressed: {pngData.Length} bytes");
            Debug.Log($"   - Compression Ratio: {((float)(texture.width * texture.height * 4) / pngData.Length):F1}x");
            Debug.Log($"   - Data Format: Pure binary bytes");
            
            // Step 2: Direct file write
            var filePath = "Library/BinaryCacheExample/example.png";
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            File.WriteAllBytes(filePath, pngData);
            stopwatch.Stop();
            
            Debug.Log($"\n3. BINARY FILE WRITE:");
            Debug.Log($"   - Method: File.WriteAllBytes() - DIRECT OS call");
            Debug.Log($"   - File Path: {filePath}");
            Debug.Log($"   - Write Time: {stopwatch.Elapsed.TotalMilliseconds:F3}ms");
            Debug.Log($"   - No intermediate conversion (JSON/Base64/XML)");
            
            // Step 3: Direct file read
            stopwatch.Restart();
            var loadedData = File.ReadAllBytes(filePath);
            stopwatch.Stop();
            
            Debug.Log($"\n4. BINARY FILE READ:");
            Debug.Log($"   - Method: File.ReadAllBytes() - DIRECT OS call");
            Debug.Log($"   - Read Time: {stopwatch.Elapsed.TotalMilliseconds:F3}ms");
            Debug.Log($"   - Data identical: {CompareByteArrays(pngData, loadedData)}");
            
            // Step 4: Reconstruct texture
            stopwatch.Restart();
            var newTexture = new Texture2D(2, 2);
            newTexture.LoadImage(loadedData);
            stopwatch.Stop();
            
            Debug.Log($"\n5. TEXTURE RECONSTRUCTION:");
            Debug.Log($"   - LoadImage Time: {stopwatch.Elapsed.TotalMilliseconds:F3}ms");
            Debug.Log($"   - Final Size: {newTexture.width}x{newTexture.height}");
            Debug.Log($"   - Format: {newTexture.format}");
            
            // Compare with other methods
            CompareWithOtherMethods(texture);
            
            // Cleanup
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(newTexture);
            File.Delete(filePath);
        }
        
        /// <summary>
        /// Show internal binary data structure
        /// 内部バイナリデータ構造を表示
        /// </summary>
        [MenuItem("Tools/Binary Cache/Show Binary Structure")]
        public static void ShowBinaryStructure()
        {
            Debug.Log("=== Binary Data Structure Analysis ===\n");
            
            var texture = CreateSmallTexture(); // 2x2 for easy analysis
            var pngData = texture.EncodeToPNG();
            
            Debug.Log("BINARY DATA BREAKDOWN:");
            Debug.Log($"Total PNG Size: {pngData.Length} bytes\n");
            
            // PNG Header Analysis (first 8 bytes)
            Debug.Log("PNG HEADER (first 8 bytes):");
            for (int i = 0; i < Math.Min(8, pngData.Length); i++)
            {
                Debug.Log($"  Byte {i}: 0x{pngData[i]:X2} ({pngData[i]}) {GetPngHeaderMeaning(i, pngData[i])}");
            }
            
            // Show some data bytes
            Debug.Log("\nDATA SECTION (bytes 8-20):");
            for (int i = 8; i < Math.Min(20, pngData.Length); i++)
            {
                Debug.Log($"  Byte {i}: 0x{pngData[i]:X2} ({pngData[i]})");
            }
            
            Debug.Log($"\n... (remaining {pngData.Length - 20} bytes contain compressed pixel data) ...");
            
            // Show how this differs from text-based storage
            var base64Data = Convert.ToBase64String(pngData);
            Debug.Log($"\nCOMPARISON WITH TEXT ENCODING:");
            Debug.Log($"Binary Storage: {pngData.Length} bytes");
            Debug.Log($"Base64 Text: {base64Data.Length} bytes (+{((float)base64Data.Length / pngData.Length - 1) * 100:F1}% overhead)");
            Debug.Log($"First 50 chars of Base64: {base64Data.Substring(0, Math.Min(50, base64Data.Length))}...");
            
            UnityEngine.Object.DestroyImmediate(texture);
        }
        
        /// <summary>
        /// Demonstrate OS-level file system operations
        /// OS レベルのファイルシステム操作のデモンストレーション
        /// </summary>
        [MenuItem("Tools/Binary Cache/Show OS Operations")]
        public static void ShowOSOperations()
        {
            Debug.Log("=== OS-Level File System Operations ===\n");
            
            var texture = CreateExampleTexture();
            var pngData = texture.EncodeToPNG();
            var filePath = "Library/BinaryCacheExample/os_example.png";
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            
            Debug.Log("BINARY CACHE OPERATION FLOW:");
            Debug.Log("1. Application Memory → PNG Encoder → Byte Array");
            Debug.Log("2. Byte Array → OS File System API → Disk Storage");
            Debug.Log("3. Disk Storage → OS File System API → Byte Array");
            Debug.Log("4. Byte Array → PNG Decoder → Application Memory\n");
            
            // Show actual system calls happening
            Debug.Log("ACTUAL SYSTEM CALLS:");
            
            // Write operation
            Debug.Log("WRITE OPERATION:");
            Debug.Log($"  C# Call: File.WriteAllBytes(\"{filePath}\", byte[{pngData.Length}])");
            Debug.Log($"  OS Call: CreateFile() → WriteFile() → CloseHandle()");
            Debug.Log($"  Disk I/O: {pngData.Length} bytes written directly");
            
            File.WriteAllBytes(filePath, pngData);
            var fileInfo = new FileInfo(filePath);
            Debug.Log($"  Result: File created, {fileInfo.Length} bytes on disk");
            
            // Read operation
            Debug.Log("\nREAD OPERATION:");
            Debug.Log($"  C# Call: File.ReadAllBytes(\"{filePath}\")");
            Debug.Log($"  OS Call: CreateFile() → GetFileSize() → ReadFile() → CloseHandle()");
            
            var loadedData = File.ReadAllBytes(filePath);
            Debug.Log($"  Disk I/O: {loadedData.Length} bytes read directly");
            Debug.Log($"  Result: byte[{loadedData.Length}] in memory");
            
            // Show performance characteristics
            Debug.Log("\nPERFORMANCE CHARACTERISTICS:");
            Debug.Log("✅ Direct memory-to-disk transfer");
            Debug.Log("✅ No CPU-intensive text conversion");
            Debug.Log("✅ OS-level caching benefits");
            Debug.Log("✅ Hardware-optimized disk access");
            Debug.Log("✅ Minimal memory allocation");
            
            // Cleanup
            File.Delete(filePath);
            UnityEngine.Object.DestroyImmediate(texture);
        }
        
        private static void CompareWithOtherMethods(Texture2D texture)
        {
            Debug.Log("\n=== COMPARISON WITH OTHER METHODS ===");
            
            var pngData = texture.EncodeToPNG();
            
            // Binary File Method
            var binaryTime = MeasureOperation(() =>
            {
                File.WriteAllBytes("temp_binary.png", pngData);
                var loaded = File.ReadAllBytes("temp_binary.png");
                File.Delete("temp_binary.png");
            });
            
            // EditorPrefs Method (Base64)
            var base64Data = Convert.ToBase64String(pngData);
            var editorPrefsTime = MeasureOperation(() =>
            {
                EditorPrefs.SetString("temp_key", base64Data);
                var loaded = EditorPrefs.GetString("temp_key");
                var decoded = Convert.FromBase64String(loaded);
                EditorPrefs.DeleteKey("temp_key");
            });
            
            // JSON Method (ScriptableObject approach)
            var jsonData = $"{{\"data\":\"{base64Data}\",\"width\":{texture.width},\"height\":{texture.height}}}";
            var jsonTime = MeasureOperation(() =>
            {
                File.WriteAllText("temp_json.json", jsonData);
                var loaded = File.ReadAllText("temp_json.json");
                File.Delete("temp_json.json");
            });
            
            Debug.Log("\nPERFORMANCE COMPARISON:");
            Debug.Log($"Binary File:  {binaryTime:F3}ms (baseline)");
            Debug.Log($"EditorPrefs:  {editorPrefsTime:F3}ms ({editorPrefsTime / binaryTime:F1}x slower)");
            Debug.Log($"JSON Method:  {jsonTime:F3}ms ({jsonTime / binaryTime:F1}x slower)");
            
            Debug.Log("\nSTORAGE SIZE COMPARISON:");
            Debug.Log($"Binary:     {pngData.Length} bytes");
            Debug.Log($"Base64:     {base64Data.Length} bytes (+{((float)base64Data.Length / pngData.Length - 1) * 100:F1}%)");
            Debug.Log($"JSON:       {jsonData.Length} bytes (+{((float)jsonData.Length / pngData.Length - 1) * 100:F1}%)");
        }
        
        private static float MeasureOperation(System.Action operation)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            operation();
            stopwatch.Stop();
            return (float)stopwatch.Elapsed.TotalMilliseconds;
        }
        
        private static Texture2D CreateExampleTexture()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = new Color[64 * 64];
            
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    var index = y * 64 + x;
                    pixels[index] = new Color(
                        (float)x / 64,
                        (float)y / 64,
                        0.5f,
                        1.0f
                    );
                }
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
        
        private static Texture2D CreateSmallTexture()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var pixels = new Color[] {
                Color.red, Color.green,
                Color.blue, Color.white
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
        
        private static string GetPngHeaderMeaning(int index, byte value)
        {
            switch (index)
            {
                case 0: return value == 0x89 ? "(PNG signature)" : "";
                case 1: return value == 0x50 ? "(P)" : "";
                case 2: return value == 0x4E ? "(N)" : "";
                case 3: return value == 0x47 ? "(G)" : "";
                case 4: return value == 0x0D ? "(CR)" : "";
                case 5: return value == 0x0A ? "(LF)" : "";
                case 6: return value == 0x1A ? "(SUB)" : "";
                case 7: return value == 0x0A ? "(LF)" : "";
                default: return "";
            }
        }
        
        private static bool CompareByteArrays(byte[] array1, byte[] array2)
        {
            if (array1.Length != array2.Length) return false;
            for (int i = 0; i < array1.Length; i++)
            {
                if (array1[i] != array2[i]) return false;
            }
            return true;
        }
    }
}