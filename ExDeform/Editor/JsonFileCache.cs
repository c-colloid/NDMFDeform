using System;
using System.IO;
using UnityEngine;
using ExDeform.Core.Interfaces;

namespace ExDeform.Editor
{
    /// <summary>
    /// JSON file-based cache implementation  
    /// JSONファイルベースのキャッシュ実装
    /// </summary>
    public class JsonFileCache : ICacheStorage
    {
        [Serializable]
        public class CachedTextureData
        {
            public byte[] textureData;
            public int width;
            public int height;
            public string meshHash;
            public long timestamp;
        }
        
        public string CacheTypeName => "JsonFile";
        
        public JsonFileCache()
        {
            EnsureDirectoryExists();
        }
        
        public bool SaveTexture(string key, Texture2D texture)
        {
            if (string.IsNullOrEmpty(key) || texture == null) return false;
            
            try
            {
                var data = new CachedTextureData
                {
                    textureData = texture.EncodeToPNG(),
                    width = texture.width,
                    height = texture.height,
                    meshHash = key,
                    timestamp = DateTime.Now.Ticks
                };
                
                var json = JsonUtility.ToJson(data, false);
                var filePath = GetCacheFilePath(key);
                
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[{CacheTypeName}] Save failed for key '{key}': {e.Message}");
                return false;
            }
        }
        
        public Texture2D LoadTexture(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            
            try
            {
                var filePath = GetCacheFilePath(key);
                if (!File.Exists(filePath)) return null;
                
                var json = File.ReadAllText(filePath);
                var data = JsonUtility.FromJson<CachedTextureData>(json);
                
                if (data?.textureData == null) return null;
                
                var texture = new Texture2D(data.width, data.height, CacheConstants.TEST_TEXTURE_FORMAT, false);
                texture.LoadImage(data.textureData);
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
            
            var filePath = GetCacheFilePath(key);
            return File.Exists(filePath);
        }
        
        public void ClearCache(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            
            try
            {
                var filePath = GetCacheFilePath(key);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
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
                if (Directory.Exists(CacheConstants.JSON_CACHE_FOLDER))
                {
                    var files = Directory.GetFiles(CacheConstants.JSON_CACHE_FOLDER, "*" + CacheConstants.CACHE_JSON_EXTENSION);
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
                if (Directory.Exists(CacheConstants.JSON_CACHE_FOLDER))
                {
                    var jsonFiles = Directory.GetFiles(CacheConstants.JSON_CACHE_FOLDER, "*" + CacheConstants.CACHE_JSON_EXTENSION);
                    stats.entryCount = jsonFiles.Length;
                    
                    long totalSize = 0;
                    foreach (var file in jsonFiles)
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
                if (!Directory.Exists(CacheConstants.JSON_CACHE_FOLDER))
                {
                    Directory.CreateDirectory(CacheConstants.JSON_CACHE_FOLDER);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[{CacheTypeName}] Directory creation failed: {e.Message}");
            }
        }
        
        private string GetCacheFilePath(string key)
        {
            return Path.Combine(CacheConstants.JSON_CACHE_FOLDER, key + CacheConstants.CACHE_JSON_EXTENSION);
        }
    }
}