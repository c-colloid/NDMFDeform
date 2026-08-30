using System.Collections.Generic;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// レンダラーの「メッシュ空間 → ワールド」変換の一元計算。
	///
	/// MeshFilter 系はレンダラー Transform そのもの。
	/// SkinnedMeshRenderer は GameObject の Transform がスキン結果に影響しない
	/// (見た目はボーン×バインドポーズで決まる)ため、レンダラー Transform が
	/// バインド時とズレているアバターでは、Transform 基準の写像が見た目のメッシュと
	/// ズレてしまう。そこで代表ボーン(ルートボーン優先)の
	/// localToWorld × バインドポーズを使う。
	/// レストポーズではどのボーンを選んでも同じ行列になり、レンダラー Transform が
	/// バインド時のままのアバターでは Transform 基準と完全に一致する(挙動不変)。
	/// </summary>
	public static class RendererMeshSpace
	{
		private struct CachedBone
		{
			public Transform Bone;
			public Matrix4x4 Bindpose;
			public int MeshId;
			public bool Boneless;
		}

		// プレビューのホットパスから毎フレーム呼ばれるため、
		// smr.bones / mesh.bindposes の配列コピーを避けて代表ボーンをキャッシュする。
		// (bones 配列だけ差し替えられた場合は検知できないが、
		//  ボーン Transform の移動・付け替えには追従する)
		private static readonly Dictionary<int, CachedBone> BoneCache = new Dictionary<int, CachedBone>();

		public static Matrix4x4 GetMeshToWorld(Transform rendererTransform)
		{
			if (rendererTransform == null)
				return Matrix4x4.identity;
			if (rendererTransform.TryGetComponent<SkinnedMeshRenderer>(out var smr))
				return GetMeshToWorld(smr);
			return rendererTransform.localToWorldMatrix;
		}

		public static Matrix4x4 GetMeshToWorld(SkinnedMeshRenderer smr)
		{
			var mesh = smr.sharedMesh;
			if (mesh == null)
				return smr.transform.localToWorldMatrix;

			var id = smr.GetInstanceID();
			if (!BoneCache.TryGetValue(id, out var cached) ||
			    cached.MeshId != mesh.GetInstanceID() ||
			    (!cached.Boneless && cached.Bone == null))
			{
				cached = SelectBone(smr, mesh);
				BoneCache[id] = cached;
			}

			if (cached.Bone == null)
				return smr.transform.localToWorldMatrix;
			return cached.Bone.localToWorldMatrix * cached.Bindpose;
		}

		private static CachedBone SelectBone(SkinnedMeshRenderer smr, Mesh mesh)
		{
			var result = new CachedBone { MeshId = mesh.GetInstanceID() };
			var bones = smr.bones;
			if (bones == null || bones.Length == 0)
			{
				result.Boneless = true;
				return result;
			}

			var bindposes = mesh.bindposes;
			var count = Mathf.Min(bones.Length, bindposes.Length);
			var root = smr.rootBone;
			for (var i = 0; i < count; i++)
			{
				if (bones[i] == null)
					continue;
				if (result.Bone == null)
				{
					result.Bone = bones[i];
					result.Bindpose = bindposes[i];
				}
				if (root != null && bones[i] == root)
				{
					result.Bone = bones[i];
					result.Bindpose = bindposes[i];
					break;
				}
			}
			result.Boneless = result.Bone == null;
			return result;
		}
	}
}
