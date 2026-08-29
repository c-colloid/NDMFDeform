using MeshModifier.NDMFDeform.Core;
using UnityEditor;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// UV 島選択の共有状態と編集操作。
	/// インスペクタの UVIslandSelectorView とシーンビュー(UVIslandMaskDeformerEditor)の
	/// 両方から同じ Undo 付きトグルを使い、ホバー中の島を相互にハイライトする。
	/// </summary>
	internal static class UVIslandSelection
	{
		/// <summary>ホバー中の島(どちらの UI か問わず)。シーンビューが輪郭を描画する</summary>
		public static UVIslandMaskDeformer HoverDeformer { get; private set; }
		public static UVIslandAnalysis.Island HoverIsland { get; private set; }

		/// <summary>選択が変更された(ビューはこれで再描画する)</summary>
		public static event System.Action Changed;

		public static void SetHover(UVIslandMaskDeformer deformer, UVIslandAnalysis.Island island)
		{
			if (HoverDeformer == deformer && HoverIsland == island)
				return;
			HoverDeformer = deformer;
			HoverIsland = island;
			SceneView.RepaintAll();
		}

		public static void ClearHover(UVIslandMaskDeformer deformer)
		{
			if (HoverDeformer != deformer)
				return;
			SetHover(null, null);
		}

		/// <summary>島の選択をトグルする(Undo 対応)</summary>
		public static void Toggle(UVIslandMaskDeformer deformer, UVIslandAnalysis analysis,
			UVIslandAnalysis.Island island)
		{
			if (deformer == null || analysis == null || island == null)
				return;

			Undo.RecordObject(deformer, "Toggle UV Island");

			// この島に解決される既存シードを取り除く。無ければ追加(= トグル)
			var seeds = deformer.SelectedIslands;
			var removed = seeds.RemoveAll(s => analysis.FindIslandAt(s.uv, s.subMesh) == island) > 0;
			if (!removed)
				seeds.Add(new IslandSeed(island.Seed, island.SubMesh));

			Commit(deformer);
		}

		public static void ClearSelection(UVIslandMaskDeformer deformer)
		{
			if (deformer == null || deformer.SelectedIslands.Count == 0)
				return;

			Undo.RecordObject(deformer, "Clear UV Island Selection");
			deformer.SelectedIslands.Clear();
			Commit(deformer);
		}

		private static void Commit(UVIslandMaskDeformer deformer)
		{
			PrefabUtility.RecordPrefabInstancePropertyModifications(deformer);
			EditorUtility.SetDirty(deformer);
			Changed?.Invoke();
			SceneView.RepaintAll();
		}
	}
}
