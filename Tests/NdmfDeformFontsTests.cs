#if UITK_FONT_FIX
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using UnityEngine.UIElements;
using Colloid.UitkFontFix;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// UITK Font Fix の FontAsset が破棄・差し替えされた後も、
	/// NDMFDeform の UI ルートが破棄済み FontAsset を参照し続けないことの検証。
	/// (参照し続けると TextCore の描画で NullReferenceException が出て
	/// インスペクタが崩れる: 新規シーン作成・Play Mode 出入り・ResetCaches 時)
	/// </summary>
	public class NdmfDeformFontsTests
	{
		[SetUp]
		public void SetUp()
		{
			NdmfDeformFonts.ClearTrackedRootsForTest();
		}

		[TearDown]
		public void TearDown()
		{
			NdmfDeformFonts.ClearTrackedRootsForTest();
		}

		private static void AssertNotDestroyed(VisualElement root)
		{
			var definition = root.style.unityFontDefinition;
			if (definition.keyword != StyleKeyword.Undefined)
				return; // インライン未指定(既定フォント継承)は安全
			var asset = definition.value.fontAsset;
			// ReferenceEquals(null) はインライン指定なし、"!= null" は Unity の生存判定
			Assert.That(ReferenceEquals(asset, null) || asset != null, Is.True,
				"破棄済みの FontAsset をインラインスタイルに保持しています");
		}

		[Test]
		public void Apply_NotPreferred_DoesNothing()
		{
			var root = new VisualElement();
			NdmfDeformFonts.ApplyEditorUiFont(root, preferCjk: false);
			Assert.That(NdmfDeformFonts.TrackedRootCount, Is.EqualTo(0));
			Assert.That(root.style.unityFontDefinition.value.fontAsset, Is.Null,
				"CJK 非対象言語ではフォントを書き換えないはずです");
		}

		[Test]
		public void Apply_TracksRootOnce()
		{
			var root = new VisualElement();
			NdmfDeformFonts.ApplyEditorUiFont(root, preferCjk: true);
			NdmfDeformFonts.ApplyEditorUiFont(root, preferCjk: true);
			Assert.That(NdmfDeformFonts.TrackedRootCount, Is.EqualTo(1));
			AssertNotDestroyed(root);
		}

		[Test]
		public void Apply_ReflectsResolvedAsset()
		{
			var root = new VisualElement();
			NdmfDeformFonts.ApplyEditorUiFont(root, preferCjk: true);

			var resolved = FontFix.CjkUiFontAsset;
			var applied = root.style.unityFontDefinition.value.fontAsset;
			if (resolved == null)
				Assert.That(ReferenceEquals(applied, null) || applied == null, Is.True,
					"フォント未解決時は既定フォントへ戻すべきです");
			else
				Assert.That(applied, Is.SameAs(resolved));
		}

		[Test]
		public void ResetCaches_ReappliesTrackedRoots()
		{
			var root = new VisualElement();
			NdmfDeformFonts.ApplyEditorUiFont(root, preferCjk: true);
			var before = root.style.unityFontDefinition.value.fontAsset;

			// ResetCaches は解決済み FontAsset を破棄し CachesInvalidated を同期発火する
			FontFix.ResetCaches();

			AssertNotDestroyed(root);
			var resolved = FontFix.CjkUiFontAsset;
			var after = root.style.unityFontDefinition.value.fontAsset;
			if (resolved != null)
			{
				Assert.That(after, Is.SameAs(resolved), "差し替え後の FontAsset へ再適用されていません");
				if (!ReferenceEquals(before, null))
					Assert.That(after, Is.Not.SameAs(before));
			}
		}

		[Test]
		public void Reapply_SurvivesCollectedRoots()
		{
			CreateAndDropRoot();
			System.GC.Collect();
			System.GC.WaitForPendingFinalizers();
			Assert.DoesNotThrow(NdmfDeformFonts.ReapplyAllForTest);
		}

		private static void CreateAndDropRoot()
		{
			var root = new VisualElement();
			NdmfDeformFonts.ApplyEditorUiFont(root, preferCjk: true);
		}
	}
}
#endif
