using System;
using UnityEditor;
using UnityEngine;
using ExDeform.Runtime.Core.Interfaces;

namespace ExDeform.Editor
{
    /// <summary>
    /// EditorPrefs-based cache implementation
    /// EditorPrefsベースのキャッシュ実装（Base64エンコード使用）
    /// </summary>
    public class EditorPrefsCache : ICacheStorage
    {
        public string CacheTypeName => "EditorPrefs";
        
        public void SaveTexture(string key, Texture2D texture)
        {
            if (string.IsNullOrEmpty(key) || texture == null) return;
            
            try
            {
                var data = texture.EncodeToPNG();
                var base64 = Convert.ToBase64String(data);
                var mainKey = CacheConstants.EDITOR_PREFS_PREFIX + key;
                var metaKey = mainKey + CacheConstants.EDITOR_PREFS_META_SUFFIX;
                
                EditorPrefs.SetString(mainKey, base64);
                EditorPrefs.SetString(metaKey, FormatMetadata(texture));
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
                var mainKey = CacheConstants.EDITOR_PREFS_PREFIX + key;
                var metaKey = mainKey + CacheConstants.EDITOR_PREFS_META_SUFFIX;
                
                var base64 = EditorPrefs.GetString(mainKey, "");
                if (string.IsNullOrEmpty(base64)) return null;
                
                var meta = EditorPrefs.GetString(metaKey, "");
                var (width, height) = ParseMetadata(meta);
                if (width <= 0 || height <= 0) return null;
                
                var data = Convert.FromBase64String(base64);
                var texture = new Texture2D(width, height, CacheConstants.TEST_TEXTURE_FORMAT, false);
                texture.LoadImage(data);
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
            
            var mainKey = CacheConstants.EDITOR_PREFS_PREFIX + key;
            return EditorPrefs.HasKey(mainKey);
        }
        
        public void ClearCache(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            
            try
            {
                var mainKey = CacheConstants.EDITOR_PREFS_PREFIX + key;
                var metaKey = mainKey + CacheConstants.EDITOR_PREFS_META_SUFFIX;
                
                EditorPrefs.DeleteKey(mainKey);
                EditorPrefs.DeleteKey(metaKey);
            }
            catch (Exception e)
            {
                Debug.LogError($"[{CacheTypeName}] Clear failed for key '{key}': {e.Message}");
            }
        }
        
        private string FormatMetadata(Texture2D texture)
        {
            return string.Join(CacheConstants.METADATA_SEPARATOR.ToString(), 
                texture.width, 
                texture.height, 
                DateTime.Now.Ticks);
        }
        
        private (int width, int height) ParseMetadata(string meta)
        {
            if (string.IsNullOrEmpty(meta)) return (0, 0);
            
            var parts = meta.Split(CacheConstants.METADATA_SEPARATOR);
            if (parts.Length < 2) return (0, 0);
            
            if (int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
            {
                return (width, height);
            }
            
            return (0, 0);
        }
    }
}