using System;
using UnityEditor;
using UnityEngine;
using ExDeform.Core.Interfaces;

namespace ExDeform.Editor
{
    /// <summary>
    /// EditorPrefs-based cache implementation
    /// EditorPrefsベースのキャッシュ実装（Base64エンコード使用）
    /// </summary>
    public class EditorPrefsCache : ICacheStorage
    {
        public string CacheTypeName => "EditorPrefs";
        
        public bool SaveTexture(string key, Texture2D texture)
        {
            if (string.IsNullOrEmpty(key) || texture == null) return false;
            
            try
            {
                var data = texture.EncodeToPNG();
                var base64 = Convert.ToBase64String(data);
                var mainKey = CacheConstants.EDITOR_PREFS_PREFIX + key;
                var metaKey = mainKey + CacheConstants.EDITOR_PREFS_META_SUFFIX;
                
                EditorPrefs.SetString(mainKey, base64);
                EditorPrefs.SetString(metaKey, FormatMetadata(texture));
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
        
        public void ClearAllCache()
        {
            try
            {
                // EditorPrefsには全キー削除の直接的な方法がないため、
                // プレフィックスで始まるキーを検索して削除
                // 注意：この実装は効率的ではないが、EditorPrefsの制限による
                for (int i = 0; i < 1000; i++) // 実用的な上限
                {
                    var testKey = CacheConstants.EDITOR_PREFS_PREFIX + i;
                    if (EditorPrefs.HasKey(testKey))
                    {
                        EditorPrefs.DeleteKey(testKey);
                        EditorPrefs.DeleteKey(testKey + CacheConstants.EDITOR_PREFS_META_SUFFIX);
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
                int entryCount = 0;
                long totalSize = 0;
                
                // EditorPrefsの制限により、効率的な統計計算は困難
                // 実用的な範囲でキーをスキャン
                for (int i = 0; i < 1000; i++)
                {
                    var testKey = CacheConstants.EDITOR_PREFS_PREFIX + i;
                    if (EditorPrefs.HasKey(testKey))
                    {
                        entryCount++;
                        var base64 = EditorPrefs.GetString(testKey, "");
                        if (!string.IsNullOrEmpty(base64))
                        {
                            // Base64サイズから元のデータサイズを推定
                            totalSize += (base64.Length * 3) / 4;
                        }
                    }
                }
                
                stats.entryCount = entryCount;
                stats.totalSizeBytes = totalSize;
                
                // EditorPrefsベースでは詳細な統計は取得困難
                stats.hitRate = 0f;
                stats.averageAccessTime = 0f;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{CacheTypeName}] Statistics calculation failed: {e.Message}");
            }
            
            return stats;
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