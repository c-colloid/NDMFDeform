using System;
using System.IO;
using UnityEngine;
using ExDeform.Runtime.Core.Interfaces;

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
        
        public void SaveTexture(string key, Texture2D texture)
        {
            if (string.IsNullOrEmpty(key) || texture == null) return;
            
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
            }
            catch (Exception e)
            {
                Debug.LogError($"[{CacheTypeName}] Save failed for key '{key}': {e.Message}");
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