using System;
using System.IO;
using UnityEngine;
using ExDeform.Core.Interfaces;
using ExDeform.Core.Constants;

namespace ExDeform.Editor
{
    /// <summary>
    /// Binary file-based cache implementation
    /// バイナリファイルベースのキャッシュ実装（PNG + メタデータファイル分離）
    /// </summary>
    public class BinaryFileCache : ICacheStorage
    {
        public string CacheTypeName => "BinaryFile";
        
        public BinaryFileCache()
        {
            EnsureDirectoryExists();
        }
        
        public bool SaveTexture(string key, Texture2D texture)
        {
            if (string.IsNullOrEmpty(key) || texture == null) return false;
            
            try
            {
                var pngData = texture.EncodeToPNG();
                var pngPath = GetImageFilePath(key);
                var metaPath = GetMetaFilePath(key);
                
                // Save PNG data
                File.WriteAllBytes(pngPath, pngData);
                
                // Save metadata separately
                var metaData = FormatMetadata(texture);
                File.WriteAllText(metaPath, metaData);
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[{CacheTypeName}] Save failed for key '{key}': {e.Message}");
                
                // Cleanup partial files on error
                CleanupPartialFiles(key);
                return false;
            }
        }
        
        public Texture2D LoadTexture(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            
            try
            {
                var pngPath = GetImageFilePath(key);
                if (!File.Exists(pngPath)) return null;
                
                var pngData = File.ReadAllBytes(pngPath);
                if (pngData == null || pngData.Length == 0) return null;
                
                // Try to get dimensions from metadata first (for optimization)
                var (width, height) = LoadMetadata(key);
                
                var texture = (width > 0 && height > 0) 
                    ? new Texture2D(width, height, CacheConstants.TEST_TEXTURE_FORMAT, false)
                    : new Texture2D(2, 2, CacheConstants.TEST_TEXTURE_FORMAT, false);
                
                texture.LoadImage(pngData);
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogError($"[{CacheTypeName}] Load failed for key '{key}': {e.Message}");
                return null;
            }
        }
        
        public bool HasCache(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            
            var pngPath = GetImageFilePath(key);
            return File.Exists(pngPath);
        }
        
        public void ClearCache(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            
            try
            {
                CleanupPartialFiles(key);
            }
            catch (Exception e)
            {
                Debug.LogError($"[{CacheTypeName}] Clear failed for key '{key}': {e.Message}");
            }
        }
        
        public void ClearAllCache()
        {
            try
            {
                if (Directory.Exists(CacheConstants.BINARY_CACHE_DIRECTORY))
                {
                    var files = Directory.GetFiles(CacheConstants.BINARY_CACHE_DIRECTORY);
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[{CacheTypeName}] Clear all cache failed: {e.Message}");
            }
        }
        
        public CacheStatistics GetStatistics()
        {
            var stats = new CacheStatistics();
            
            try
            {
                if (Directory.Exists(CacheConstants.BINARY_CACHE_DIRECTORY))
                {
                    var pngFiles = Directory.GetFiles(CacheConstants.BINARY_CACHE_DIRECTORY, "*" + CacheConstants.CACHE_PNG_EXTENSION);
                    stats.entryCount = pngFiles.Length;
                    
                    long totalSize = 0;
                    foreach (var file in pngFiles)
                    {
                        totalSize += new FileInfo(file).Length;
                    }
                    stats.totalSizeBytes = totalSize;
                    
                    // For file-based cache, we don't track hit rate or access time
                    stats.hitRate = 0f;
                    stats.averageAccessTime = 0f;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{CacheTypeName}] Statistics calculation failed: {e.Message}");
            }
            
            return stats;
        }
        
        private void EnsureDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(CacheConstants.BINARY_CACHE_DIRECTORY))
                {
                    Directory.CreateDirectory(CacheConstants.BINARY_CACHE_DIRECTORY);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[{CacheTypeName}] Directory creation failed: {e.Message}");
            }
        }
        
        private string GetImageFilePath(string key)
        {
            return Path.Combine(CacheConstants.BINARY_CACHE_DIRECTORY, key + CacheConstants.CACHE_PNG_EXTENSION);
        }
        
        private string GetMetaFilePath(string key)
        {
            return Path.Combine(CacheConstants.BINARY_CACHE_DIRECTORY, key + CacheConstants.CACHE_META_EXTENSION);
        }
        
        private string FormatMetadata(Texture2D texture)
        {
            return string.Join(CacheConstants.METADATA_SEPARATOR.ToString(),
                texture.width,
                texture.height,
                DateTime.Now.Ticks);
        }
        
        private (int width, int height) LoadMetadata(string key)
        {
            try
            {
                var metaPath = GetMetaFilePath(key);
                if (!File.Exists(metaPath)) return (0, 0);
                
                var metaData = File.ReadAllText(metaPath);
                var parts = metaData.Split(CacheConstants.METADATA_SEPARATOR);
                
                if (parts.Length >= 2 && 
                    int.TryParse(parts[0], out var width) && 
                    int.TryParse(parts[1], out var height))
                {
                    return (width, height);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{CacheTypeName}] Metadata load failed for key '{key}': {e.Message}");
            }
            
            return (0, 0);
        }
        
        private void CleanupPartialFiles(string key)
        {
            try
            {
                var pngPath = GetImageFilePath(key);
                var metaPath = GetMetaFilePath(key);
                
                if (File.Exists(pngPath)) File.Delete(pngPath);
                if (File.Exists(metaPath)) File.Delete(metaPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{CacheTypeName}] Cleanup failed for key '{key}': {e.Message}");
            }
        }
    }
}