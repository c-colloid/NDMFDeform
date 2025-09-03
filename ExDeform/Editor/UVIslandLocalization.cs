using System.Collections.Generic;
using UnityEngine;

namespace ExDeform.Editor
{
    /// <summary>
    /// Localization system for UV Island Selector
    /// UV Island Selector用の多言語化システム
    /// </summary>
    public static class UVIslandLocalization
    {
        public enum Language
        {
            English,
            Japanese
        }
        
        private static Language currentLanguage = Language.Japanese; // デフォルトは日本語
        
        public static Language CurrentLanguage 
        { 
            get => currentLanguage; 
            set => currentLanguage = value; 
        }
        
        private static readonly Dictionary<string, Dictionary<Language, string>> localizedText = 
            new Dictionary<string, Dictionary<Language, string>>
        {
            // Header titles
            ["header_selection"] = new Dictionary<Language, string>
            {
                [Language.English] = "UV Island Selection",
                [Language.Japanese] = "UVアイランド選択"
            },
            ["header_display"] = new Dictionary<Language, string>
            {
                [Language.English] = "Display Settings", 
                [Language.Japanese] = "表示設定"
            },
            ["header_preview"] = new Dictionary<Language, string>
            {
                [Language.English] = "UV Map Preview",
                [Language.Japanese] = "UVマッププレビュー"
            },
            ["header_selection_tools"] = new Dictionary<Language, string>
            {
                [Language.English] = "Selection Tools",
                [Language.Japanese] = "選択ツール"
            },
            ["header_magnifying"] = new Dictionary<Language, string>
            {
                [Language.English] = "Magnifying Glass",
                [Language.Japanese] = "ルーペ機能"
            },
            
            // Field labels
            ["adaptive_vertex_size"] = new Dictionary<Language, string>
            {
                [Language.English] = "Use Adaptive Vertex Size",
                [Language.Japanese] = "適応的頂点サイズを使用"
            },
            ["manual_vertex_size"] = new Dictionary<Language, string>
            {
                [Language.English] = "Manual Vertex Size",
                [Language.Japanese] = "手動頂点サイズ"
            },
            ["size_multiplier"] = new Dictionary<Language, string>
            {
                [Language.English] = "Size Multiplier",
                [Language.Japanese] = "サイズ倍率"
            },
            ["auto_update"] = new Dictionary<Language, string>
            {
                [Language.English] = "Auto Update Preview",
                [Language.Japanese] = "自動プレビュー更新"
            },
            ["zoom_level"] = new Dictionary<Language, string>
            {
                [Language.English] = "Zoom",
                [Language.Japanese] = "ズーム"
            },
            ["enable_range_selection"] = new Dictionary<Language, string>
            {
                [Language.English] = "Enable Range Selection",
                [Language.Japanese] = "範囲選択を有効化"
            },
            ["enable_magnifying"] = new Dictionary<Language, string>
            {
                [Language.English] = "Enable Magnifying Glass",
                [Language.Japanese] = "ルーペ機能を有効化"
            },
            ["magnifying_zoom"] = new Dictionary<Language, string>
            {
                [Language.English] = "Magnifying Zoom",
                [Language.Japanese] = "ルーペ倍率"
            },
            ["magnifying_size"] = new Dictionary<Language, string>
            {
                [Language.English] = "Magnifying Size",
                [Language.Japanese] = "ルーペサイズ"
            },
            
            // Button labels
            ["refresh"] = new Dictionary<Language, string>
            {
                [Language.English] = "Refresh",
                [Language.Japanese] = "更新"
            },
            ["clear_selection"] = new Dictionary<Language, string>
            {
                [Language.English] = "Clear Selection",
                [Language.Japanese] = "選択解除"
            },
            ["reset"] = new Dictionary<Language, string>
            {
                [Language.English] = "Reset",
                [Language.Japanese] = "リセット"
            },
            
            // Tooltips
            ["tooltip_adaptive_size"] = new Dictionary<Language, string>
            {
                [Language.English] = "Automatically adjust vertex sphere size based on mesh bounds",
                [Language.Japanese] = "メッシュの境界に基づいて頂点球のサイズを自動調整します"
            },
            ["tooltip_manual_size"] = new Dictionary<Language, string>
            {
                [Language.English] = "Manual size for vertex spheres when adaptive sizing is disabled",
                [Language.Japanese] = "適応サイズが無効時の手動頂点球サイズ"
            },
            ["tooltip_size_multiplier"] = new Dictionary<Language, string>
            {
                [Language.English] = "Multiplier for adaptive vertex sphere size calculation",
                [Language.Japanese] = "適応頂点球サイズ計算の倍率"
            },
            ["tooltip_auto_update"] = new Dictionary<Language, string>
            {
                [Language.English] = "Automatically update UV map preview when selection changes",
                [Language.Japanese] = "選択変更時にUVマッププレビューを自動更新"
            },
            ["tooltip_zoom"] = new Dictionary<Language, string>
            {
                [Language.English] = "Zoom level for UV map preview (1x = normal, 8x = maximum)",
                [Language.Japanese] = "UVマッププレビューのズームレベル（1倍 = 通常、8倍 = 最大）"
            },
            ["tooltip_range_selection"] = new Dictionary<Language, string>
            {
                [Language.English] = "Enable dragging to select multiple UV islands in a rectangular area",
                [Language.Japanese] = "ドラッグによる矩形範囲での複数UVアイランド選択を有効化"
            },
            ["magnifying_glass"] = new Dictionary<Language, string>
            {
                [Language.English] = "Magnifying Glass",
                [Language.Japanese] = "ルーペ機能"
            },
            ["tooltip_magnifying"] = new Dictionary<Language, string>
            {
                [Language.English] = "Enable magnifying glass with middle-click for detailed UV island inspection",
                [Language.Japanese] = "中クリックでルーペ機能を有効化し、UVアイランドの詳細表示"
            },
            ["magnifying_zoom"] = new Dictionary<Language, string>
            {
                [Language.English] = "Zoom",
                [Language.Japanese] = "倍率"
            },
            ["tooltip_magnifying_zoom"] = new Dictionary<Language, string>
            {
                [Language.English] = "Zoom level for magnifying glass (x2, x4, x8, x16)",
                [Language.Japanese] = "ルーペの拡大倍率（x2, x4, x8, x16）"
            },
            ["magnifying_size"] = new Dictionary<Language, string>
            {
                [Language.English] = "Size",
                [Language.Japanese] = "サイズ"
            },
            ["tooltip_magnifying_size"] = new Dictionary<Language, string>
            {
                [Language.English] = "Size of magnifying glass display area",
                [Language.Japanese] = "ルーペ表示エリアのサイズ"
            },
            
            // Status messages
            ["status_ready"] = new Dictionary<Language, string>
            {
                [Language.English] = "Ready",
                [Language.Japanese] = "待機中"
            },
            ["status_refreshing"] = new Dictionary<Language, string>
            {
                [Language.English] = "Refreshing...",
                [Language.Japanese] = "更新中..."
            },
            ["status_islands_found"] = new Dictionary<Language, string>
            {
                [Language.English] = "{0} UV islands found",
                [Language.Japanese] = "{0}個のUVアイランドが見つかりました"
            },
            ["status_islands_selected"] = new Dictionary<Language, string>
            {
                [Language.English] = "{0} islands selected, {1} vertices, {2} faces masked",
                [Language.Japanese] = "{0}個のアイランドを選択中、{1}頂点、{2}面をマスク"
            },
            
            // Control instructions
            ["controls_uv_map"] = new Dictionary<Language, string>
            {
                [Language.English] = "Click: select islands, Drag: pan view, Wheel: zoom, Middle-click: magnifying glass",
                [Language.Japanese] = "クリック: アイランド選択、ドラッグ: 視点移動、ホイール: ズーム、中ボタン: ルーペ"
            },
            
            // Island info
            ["island_info"] = new Dictionary<Language, string>
            {
                [Language.English] = "Island {0}",
                [Language.Japanese] = "アイランド {0}"
            },
            ["vertex_count"] = new Dictionary<Language, string>
            {
                [Language.English] = "{0} verts", 
                [Language.Japanese] = "{0}頂点"
            },
            ["face_count"] = new Dictionary<Language, string>
            {
                [Language.English] = "{0} faces",
                [Language.Japanese] = "{0}面"
            }
        };
        
        public static string Get(string key, params object[] args)
        {
            if (localizedText.TryGetValue(key, out var translations))
            {
                if (translations.TryGetValue(currentLanguage, out var text))
                {
                    return args.Length > 0 ? string.Format(text, args) : text;
                }
            }
            
            // Fallback to key if translation not found
            return key;
        }
        
        public static GUIContent GetContent(string key, string tooltipKey = null)
        {
            var text = Get(key);
            var tooltip = !string.IsNullOrEmpty(tooltipKey) ? Get(tooltipKey) : "";
            return new GUIContent(text, tooltip);
        }
    }
}