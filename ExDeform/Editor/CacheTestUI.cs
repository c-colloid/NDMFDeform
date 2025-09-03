using UnityEditor;

namespace ExDeform.Editor
{
    /// <summary>
    /// UI operations for cache performance tests
    /// キャッシュパフォーマンステストのUI操作
    /// </summary>
    public static class CacheTestUI
    {
        /// <summary>
        /// Show dialog to select cache implementation
        /// キャッシュ実装選択ダイアログを表示
        /// </summary>
        /// <returns>Selected implementation index, -1 if cancelled</returns>
        public static int ShowImplementationSelectionDialog()
        {
            return EditorUtility.DisplayDialogComplex(
                "Choose Cache Implementation",
                "Select cache implementation to test:",
                "EditorPrefs", "JsonFile", "BinaryFile");
        }

        /// <summary>
        /// Show confirmation dialog for running all tests
        /// 全テスト実行の確認ダイアログを表示
        /// </summary>
        /// <returns>True if confirmed</returns>
        public static bool ShowRunAllTestsDialog()
        {
            return EditorUtility.DisplayDialog(
                "Run All Performance Tests",
                "This will run performance tests on all cache implementations.\n" +
                "This may take a few moments. Continue?",
                "Run Tests", "Cancel");
        }
    }
}