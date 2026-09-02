using UnityEditor;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// エディタ UI アセット(UXML / USS)のロード。
	/// 見た目の構成は .uxml、スタイルは NdmfDeform.uss に分離し、
	/// C# 側は要素の取得と動作のバインドだけを行う。
	/// パッケージが Assets 直下でも Packages 配下でも解決できるよう、
	/// パスではなく GUID(各アセットの .meta と一致)で参照する。
	/// </summary>
	public static class NdmfDeformUI
	{
		public const string CommonStyleGuid = "df9af6019ad249a5840230d67eadec6a";
		public const string DeformerInspectorGuid = "c65edbfceb424edab7ef6b36c2129aab";
		public const string StackInspectorGuid = "9b56a3be88ec44a9912ae5d7b695f9df";
		public const string StackRowGuid = "d5d1d3fac6ec4eb09d66175ec7957088";
		public const string StackInlineGuid = "2f86e4605d4c453296d7e636fba0dec5";
		public const string LatticeInspectorGuid = "7dcc5a357ea84753b8ec2fccf6436381";
		public const string UVIslandMaskInspectorGuid = "886db9a0f70f4276a24590753e7fed47";
		public const string UVIslandSelectorGuid = "5630c7e07510451587ecfefd0f0cb48a";
		public const string BlendShapeOverrideRowGuid = "875cdc4a28af48989f3e3a314fe17368";
		public const string PointGridOverlayGuid = "5ac14dd6496842d3a69729e522890fe8";
		public const string BodyFitInspectorGuid = "577847c3c47747ee94b2a914fef9e1b7";

		private static StyleSheet _commonStyle;

		private static StyleSheet CommonStyle
		{
			get
			{
				if (_commonStyle == null)
					_commonStyle = Load<StyleSheet>(CommonStyleGuid);
				return _commonStyle;
			}
		}

		public static T Load<T>(string guid) where T : UnityEngine.Object
		{
			var path = AssetDatabase.GUIDToAssetPath(guid);
			return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
		}

		/// <summary>
		/// UXML を target 直下へ展開し、共通スタイルとフォントを適用する。
		/// アセットが見つからない場合は警告を出す(target は空のまま)。
		/// </summary>
		public static void CloneTree(string treeGuid, VisualElement target)
		{
			ApplyCommonStyle(target);
			var tree = Load<VisualTreeAsset>(treeGuid);
			if (tree == null)
			{
				UnityEngine.Debug.LogWarning($"[NDMFDeform] UI アセット(UXML)が見つかりません: GUID {treeGuid}");
				return;
			}
			tree.CloneTree(target);
		}

		/// <summary>共通スタイルシートと UI フォントをルートへ適用する</summary>
		public static void ApplyCommonStyle(VisualElement root)
		{
			var style = CommonStyle;
			if (style != null && !root.styleSheets.Contains(style))
				root.styleSheets.Add(style);
			NdmfDeformFonts.ApplyEditorUiFont(root);
		}
	}
}
