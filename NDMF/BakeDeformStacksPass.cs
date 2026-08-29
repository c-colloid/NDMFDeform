using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;

namespace MeshModifier.NDMFDeform.NDMF
{
	/// <summary>
	/// Transforming フェーズ: 各 DeformStack をベイクし、
	/// 直後に自前コンポーネントを component 単位で破棄する。
	/// GameObject は一切削除しない(旧実装の誤削除バグの構造的な再発防止)。
	/// アバター外のオブジェクトには決して触れない。
	/// </summary>
	internal static class BakeDeformStacksPass
	{
		public static void Run(BuildContext ctx)
		{
			var root = ctx.AvatarRootTransform;
			var stacks = root.GetComponentsInChildren<DeformStack>(true);

			foreach (var stack in stacks)
			{
				if (IsEditorOnly(stack.transform, root))
					continue;

				var source = GetSourceMesh(stack, out var smr, out var meshFilter);
				if (source == null)
					continue;

				var baked = DeformBakeCore.Bake(stack, source, stack.transform);
				if (baked == null)
					continue;

				AssetDatabase.AddObjectToAsset(baked, ctx.AssetContainer);

				if (smr != null)
				{
					smr.sharedMesh = baked;
					ExpandBoundsForDeform(smr, source, baked);
				}
				else if (meshFilter != null)
				{
					meshFilter.sharedMesh = baked;
				}
			}

			// クリーンアップ: アバター配下の自前コンポーネントのみを破棄する。
			// スタックから参照されていてもアバター外の Deformer には触れない。
			foreach (var stack in root.GetComponentsInChildren<DeformStack>(true))
				Object.DestroyImmediate(stack);
			foreach (var deformer in root.GetComponentsInChildren<DeformerBase>(true))
				Object.DestroyImmediate(deformer);
		}

		/// <summary>
		/// 変形でメッシュが元のバウンズを超えた分だけ SMR の localBounds を保守的に広げる。
		/// localBounds はルートボーン空間のため厳密な変換はせず、成長量でパディングする
		/// (縮む方向には触れない。updateWhenOffscreen も変更しない)。
		/// </summary>
		internal static void ExpandBoundsForDeform(SkinnedMeshRenderer smr, Mesh source, Mesh baked)
		{
			var s = source.bounds;
			var b = baked.bounds;
			var growth = Mathf.Max(0f, Mathf.Max(
				Mathf.Max(s.min.x - b.min.x, Mathf.Max(s.min.y - b.min.y, s.min.z - b.min.z)),
				Mathf.Max(b.max.x - s.max.x, Mathf.Max(b.max.y - s.max.y, b.max.z - s.max.z))));
			if (growth <= 0f)
				return;

			var localBounds = smr.localBounds;
			localBounds.Expand(growth * 2f);
			smr.localBounds = localBounds;
		}

		internal static Mesh GetSourceMesh(DeformStack stack, out SkinnedMeshRenderer smr, out MeshFilter meshFilter)
		{
			smr = stack.GetComponent<SkinnedMeshRenderer>();
			meshFilter = null;
			if (smr != null)
				return smr.sharedMesh;

			meshFilter = stack.GetComponent<MeshFilter>();
			return meshFilter != null ? meshFilter.sharedMesh : null;
		}

		private static bool IsEditorOnly(Transform t, Transform root)
		{
			for (var current = t; current != null && current != root; current = current.parent)
			{
				if (current.CompareTag("EditorOnly"))
					return true;
			}
			return false;
		}
	}
}
