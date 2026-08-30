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
			if (!Colloid.UitkFontFix.FontFix.ShouldPreferCjkUi(Application.systemLanguage))
				return;

			// プレイモードの出入りで動的フォントのアトラス(Texture2D / Material)だけが
			// 破棄され、キャッシュされた FontAsset が残ることがある。
			// その状態で適用するとテキスト描画のたびに MissingReferenceException が出て
			// インスペクタが崩れるため、アトラスが生きている場合のみ適用する
			// (適用しない場合はエディタ既定フォントのまま = 表示は崩れない)
			var asset = Colloid.UitkFontFix.FontFix.CjkUiFontAsset;
			if (asset == null || asset.atlasTexture == null)
				return;

			Colloid.UitkFontFix.FontFix.ApplyCjkUi(root);
#endif
		}
	}
}
