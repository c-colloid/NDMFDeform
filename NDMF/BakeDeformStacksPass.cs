using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using nadena.dev.ndmf;
using UnityEngine;

namespace MeshModifier.NDMFDeform.NDMF
{
	/// <summary>
	/// Transforming フェーズ: 各 DeformStack をベイクし、
	/// 直後に自前コンポーネントを component 単位で破棄する。
	/// GameObject は一切削除しない(旧実装の誤削除バグの構造的な再発防止)。
	/// アバター外のオブジェクトには決して触れない。
	///
	/// 他のレンダラーを参照するデフォーマ(Body Fit の体など)があり、参照先にも
	/// DeformStack が付いている場合は参照先を先にベイクする(重ね着)。
	/// 各スタックはベイク直後に破棄するため、後続のスタックから見た参照先は
	/// 「ベイク済みの sharedMesh を持ち、DeformStack の無いレンダラー」になる
	/// (参照先の変形後メッシュを解決するフックが二重に変形しない)。
	/// EditorOnly 配下のスタックもこの不変条件を保つため同じ経路でベイクする
	/// (EditorOnly の参照用ダミー素体に Deform Stack を付けて衣装を合わせる用途を含む。
	/// そのオブジェクト自体はビルド後に取り除かれる)。
	/// </summary>
	internal static class BakeDeformStacksPass
	{
		public static void Run(BuildContext ctx)
		{
			var root = ctx.AvatarRootTransform;
			var stacks = DeformStackOrdering.Sort(root.GetComponentsInChildren<DeformStack>(true));

			foreach (var stack in stacks)
			{
				if (stack == null)
					continue;

				var source = GetSourceMesh(stack, out var smr, out var meshFilter);
				if (source == null)
					continue;

				var baked = DeformBakeCore.Bake(stack, source, stack.transform);
				if (baked == null)
				{
					Object.DestroyImmediate(stack);
					continue;
				}

				// プレイモード(Apply on Play)ではアセット保存が無効
				// (NullAssetSaver / AssetContainer = null)のため、
				// AssetContainer への直接追加ではなく AssetSaver 経由で保存する
				// (プレイ中は no-op になり、ビルド時のみ永続化される)
				ctx.AssetSaver.SaveAsset(baked);

				if (smr != null)
				{
					smr.sharedMesh = baked;
					ExpandBoundsForDeform(smr, source, baked);
				}
				else if (meshFilter != null)
				{
					meshFilter.sharedMesh = baked;
				}

				Object.DestroyImmediate(stack);
			}

			// クリーンアップ: アバター配下の自前コンポーネントのみを破棄する。
			// スタックから参照されていてもアバター外の Deformer には触れない。
			foreach (var stack in root.GetComponentsInChildren<DeformStack>(true))
				Object.DestroyImmediate(stack);
			foreach (var deformer in root.GetComponentsInChildren<DeformerBase>(true))
				Object.DestroyImmediate(deformer);

			// ビルド用クローンのレンダラーを参照した表面データ(Body Fit の BVH 等)は
			// クローン破棄後に不要になるため、ここで回収する(シーン側のエントリは残す)
			foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
				ReferenceSurfaceCache.Evict(renderer);
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

	}
}
