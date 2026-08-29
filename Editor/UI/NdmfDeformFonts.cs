using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// エディタ UI のフォント適用ヘルパー。
	/// UITK Font Fix(jp.colloid.uitk-font-fix)が導入されている場合、
	/// CJK 環境では日本語表記が崩れないフォントをルートへ適用する
	/// (子孫要素はスタイル継承で追従する)。未導入時は何もしない。
	/// </summary>
	internal static class NdmfDeformFonts
	{
		/// <summary>コンテナルート(インスペクタ・オーバーレイ等)に UI フォントを適用する</summary>
		public static void ApplyEditorUiFont(VisualElement root)
		{
#if UITK_FONT_FIX
			if (Colloid.UitkFontFix.FontFix.ShouldPreferCjkUi(Application.systemLanguage))
				Colloid.UitkFontFix.FontFix.ApplyCjkUi(root);
#endif
		}
	}
}
