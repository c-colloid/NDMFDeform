using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// エディタ UI のフォント適用ヘルパー。
	/// UITK Font Fix(jp.colloid.uitk-font-fix)が導入されている場合、
	/// CJK 環境では日本語表記が崩れないフォントをルートへ適用する
	/// (子孫要素はスタイル継承で追従する)。未導入時は何もしない。
	///
	/// UITK Font Fix が生成する FontAsset は一時オブジェクトで、
	/// Play Mode の出入り・新規シーン作成・他パッケージからの
	/// <c>FontFix.ResetCaches()</c> などで破棄・差し替えされることがある。
	/// 破棄済みの FontAsset をインラインスタイルに持ったままの要素は
	/// 描画のたびに TextCore 内部で NullReferenceException /
	/// MissingReferenceException を投げてインスペクタが崩れるため、
	/// 適用したルートを追跡し、無効化通知とシーンイベントで再適用する。
	/// </summary>
	internal static class NdmfDeformFonts
	{
		/// <summary>コンテナルート(インスペクタ・オーバーレイ等)に UI フォントを適用する</summary>
		public static void ApplyEditorUiFont(VisualElement root)
		{
#if UITK_FONT_FIX
			ApplyEditorUiFont(root,
				Colloid.UitkFontFix.FontFix.ShouldPreferCjkUi(Application.systemLanguage));
#endif
		}

#if UITK_FONT_FIX
		/// <summary>
		/// 言語判定を外から与える版(テスト用)。preferCjk が false なら何もしない。
		/// </summary>
		internal static void ApplyEditorUiFont(VisualElement root, bool preferCjk)
		{
			if (root == null || !preferCjk)
				return;

			EnsureHooked();
			Track(root);
			Apply(root);
		}

		// 適用済みルート。インスペクタ破棄後まで生かさないよう弱参照で保持する
		private static readonly List<System.WeakReference<VisualElement>> _roots =
			new List<System.WeakReference<VisualElement>>();

		private static bool _hooked;
		private static bool _reapplying;

		[InitializeOnLoadMethod]
		private static void EnsureHooked()
		{
			if (_hooked)
				return;
			_hooked = true;

#if UITK_FONT_FIX_CACHES_INVALIDATED
			// 解決済み FontAsset が破棄・差し替えされた(ResetCaches / 設定変更 /
			// 修復不能な破損からの再構築)ときの通知。同期的に発火するので、
			// ここで再適用すれば破棄済み参照のまま再描画されることはない
			Colloid.UitkFontFix.FontFix.CachesInvalidated += ReapplyAll;
#endif
			// 新規シーン作成・シーン読み込みは、TextCore が遅延生成した
			// 未保護のアトラスページを道連れにすることがある。
			// FontFix 側にはこのタイミングのフックが無いので、こちらから
			// 取得を通して検出・修復(または差し替え)を走らせる
			EditorSceneManager.newSceneCreated += OnNewSceneCreated;
			EditorSceneManager.sceneOpened += OnSceneOpened;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
		}

		private static void OnNewSceneCreated(Scene scene, NewSceneSetup setup, NewSceneMode mode)
		{
			ReapplyAll();
		}

		private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
		{
			ReapplyAll();
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange change)
		{
			if (change == PlayModeStateChange.EnteredEditMode
				|| change == PlayModeStateChange.EnteredPlayMode)
				ReapplyAll();
		}

		private static void Track(VisualElement root)
		{
			for (int i = _roots.Count - 1; i >= 0; i--)
			{
				if (!_roots[i].TryGetTarget(out var existing))
				{
					_roots.RemoveAt(i);
					continue;
				}
				if (ReferenceEquals(existing, root))
					return;
			}
			_roots.Add(new System.WeakReference<VisualElement>(root));
		}

		/// <summary>
		/// ルートへ現在の解決結果を反映する。
		/// FontFix の getter は破棄済みアトラスの検出と修復を兼ねるため、
		/// キャッシュせず毎回取得する。何も解決できない場合は
		/// インライン指定を外してエディタ既定フォントへ戻す
		/// (破棄済み参照を残すよりは表示が崩れない)。
		/// </summary>
		private static void Apply(VisualElement root)
		{
			var asset = Colloid.UitkFontFix.FontFix.CjkUiFontAsset;
			if (asset == null)
			{
				root.style.unityFontDefinition = StyleKeyword.Null;
				return;
			}

			// 同じインスタンスの再設定はスタイル変更にならず再生成されない。
			// 差し替え後に古いインスタンスを保持している場合のみ書き換える
			var current = root.style.unityFontDefinition;
			if (current.keyword == StyleKeyword.Undefined
				&& ReferenceEquals(current.value.fontAsset, asset))
				return;

			Colloid.UitkFontFix.FontFix.ApplyCjkUi(root);
		}

		/// <summary>追跡中の生きているルートすべてへ再適用し、破棄済みルートを掃除する</summary>
		private static void ReapplyAll()
		{
			if (_reapplying)
				return;
			_reapplying = true;
			try
			{
				for (int i = _roots.Count - 1; i >= 0; i--)
				{
					if (!_roots[i].TryGetTarget(out var root))
					{
						_roots.RemoveAt(i);
						continue;
					}
					// パネル未接続(未表示・一時的に外れている)のルートも更新しておく:
					// 後で再接続されたとき破棄済み参照を持ち込ませないため
					try
					{
						Apply(root);
					}
					catch (System.Exception e)
					{
						Debug.LogException(e);
					}
				}
			}
			finally
			{
				_reapplying = false;
			}
		}

		/// <summary>テスト用: 追跡を全て解除する</summary>
		internal static void ClearTrackedRootsForTest()
		{
			_roots.Clear();
		}

		/// <summary>テスト用: 追跡中(生存)のルート数</summary>
		internal static int TrackedRootCount
		{
			get
			{
				int count = 0;
				foreach (var reference in _roots)
				{
					if (reference.TryGetTarget(out _))
						count++;
				}
				return count;
			}
		}

		/// <summary>テスト用: 無効化通知と同じ再適用処理を明示的に走らせる</summary>
		internal static void ReapplyAllForTest()
		{
			ReapplyAll();
		}
#endif
	}
}
