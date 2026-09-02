using System;
using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// ヒエラルキー右クリック(GameObject メニュー)からの作成導線。
	/// 旧 Deform ではこの経路(Deform/Deformable・Deform/Deformers/...)が
	/// 主要な追加手段だったため、v2 でも同等の導線を提供する。
	///
	/// 優先度はすべて 50 未満にする(50 以上の GameObject メニュー項目は
	/// ヒエラルキーのコンテキストメニューに表示されない)。
	/// ヒエラルキーからの実行は選択オブジェクト毎に context 付きで呼ばれるため、
	/// 各呼び出しは自分の context のみ処理する(メニューバーからは context 無しで
	/// 一度だけ呼ばれ、選択全体を処理する)。
	/// </summary>
	public static class GameObjectMenu
	{
		private const string Root = "GameObject/NDMF Deform/";

		[MenuItem(Root + "Deform Stack (旧 Deformable)", false, 10)]
		private static void AddStack(MenuCommand command)
		{
			foreach (var target in TargetsOf(command))
				NdmfDeformObjectFactory.AddStack(target);
		}

		// ---- デフォーマ(旧メニューに倣い、Deformable とは別階層の Deformers/ 配下) ----

		[MenuItem(Root + "Deformers/Lattice", false, 21)]
		private static void CreateLattice(MenuCommand c) => Create<LatticeDeformer>(c);

		[MenuItem(Root + "Deformers/Cylindrical Scale", false, 22)]
		private static void CreateCylindricalScale(MenuCommand c) => Create<CylindricalScaleDeformer>(c);

		[MenuItem(Root + "Deformers/Cylindrical Vertex Transform", false, 23)]
		private static void CreateCylindricalVertexTransform(MenuCommand c) =>
			Create<CylindricalVertexTransformDeformer>(c);

		[MenuItem(Root + "Deformers/Transform", false, 24)]
		private static void CreateTransform(MenuCommand c) => Create<TransformDeformer>(c);

		[MenuItem(Root + "Deformers/Scale", false, 25)]
		private static void CreateScale(MenuCommand c) => Create<ScaleDeformer>(c);

		[MenuItem(Root + "Deformers/Body Fit", false, 26)]
		private static void CreateBodyFit(MenuCommand c) => Create<BodyFitDeformer>(c);

		[MenuItem(Root + "Deformers/Mask/UV Island Mask", false, 37)]
		private static void CreateUvIslandMask(MenuCommand c) => Create<UVIslandMaskDeformer>(c);

		[MenuItem(Root + "Deformers/Mask/Sphere Mask", false, 38)]
		private static void CreateSphereMask(MenuCommand c) => Create<SphereMaskDeformer>(c);

		[MenuItem(Root + "Deformers/Mask/Box Mask", false, 39)]
		private static void CreateBoxMask(MenuCommand c) => Create<BoxMaskDeformer>(c);

		[MenuItem(Root + "Deformers/Mask/Vertical Gradient Mask", false, 40)]
		private static void CreateVerticalGradientMask(MenuCommand c) => Create<VerticalGradientMaskDeformer>(c);

		[MenuItem(Root + "Deformers/Mask/Vertex Color Mask", false, 41)]
		private static void CreateVertexColorMask(MenuCommand c) => Create<VertexColorMaskDeformer>(c);

		// ---- validate(全項目共通: シーン上の GameObject 選択時のみ有効) ----

		[MenuItem(Root + "Deform Stack (旧 Deformable)", true)]
		[MenuItem(Root + "Deformers/Lattice", true)]
		[MenuItem(Root + "Deformers/Cylindrical Scale", true)]
		[MenuItem(Root + "Deformers/Cylindrical Vertex Transform", true)]
		[MenuItem(Root + "Deformers/Transform", true)]
		[MenuItem(Root + "Deformers/Scale", true)]
		[MenuItem(Root + "Deformers/Body Fit", true)]
		[MenuItem(Root + "Deformers/Mask/UV Island Mask", true)]
		[MenuItem(Root + "Deformers/Mask/Sphere Mask", true)]
		[MenuItem(Root + "Deformers/Mask/Box Mask", true)]
		[MenuItem(Root + "Deformers/Mask/Vertical Gradient Mask", true)]
		[MenuItem(Root + "Deformers/Mask/Vertex Color Mask", true)]
		private static bool ValidateSelection()
		{
			var go = Selection.activeGameObject;
			return go != null && !EditorUtility.IsPersistent(go);
		}

		private static void Create<T>(MenuCommand command) where T : DeformerBase
		{
			var meta = (DeformerMetaAttribute)Attribute.GetCustomAttribute(
				typeof(T), typeof(DeformerMetaAttribute));
			var name = meta?.Name ?? typeof(T).Name;

			GameObject created = null;
			foreach (var target in TargetsOf(command))
			{
				var deformer = NdmfDeformObjectFactory.CreateDeformer(target, typeof(T), name);
				if (deformer != null)
					created = deformer.gameObject;
			}

			// 作成したデフォーマを選択して、そのままギズモで編集を始められるようにする
			if (created != null)
				Selection.activeGameObject = created;
		}

		private static IEnumerable<GameObject> TargetsOf(MenuCommand command)
		{
			if (command != null && command.context is GameObject go)
				return new[] { go };
			return Selection.gameObjects ?? Array.Empty<GameObject>();
		}
	}
}
