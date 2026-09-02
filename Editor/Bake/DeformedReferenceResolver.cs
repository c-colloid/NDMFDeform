using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// 参照レンダラー(Body Fit の体など)に DeformStack が付いている場合に、
	/// その「変形後」メッシュを参照側へ提供する(重ね着の連鎖)。
	/// Runtime 側のフック(ReferenceSurfaceUtility.DeformedMeshResolver)へ登録する。
	///
	/// - プレビュー: 参照先スタックを DeformPreviewBakeCache でベイクした結果を返す
	///   (参照先自身のプレビューと同じ引数で呼ぶため、キャッシュのホットパスに乗る)
	/// - ビルド: BakeDeformStacksPass が参照先を先にベイクし、ベイク直後にスタックを
	///   破棄するため、ここは呼ばれても sharedMesh(= ベイク済み)へフォールバックする
	/// - 循環参照(A が B を、B が A を参照)は再入ガードで打ち切り、変形前メッシュを使う
	/// </summary>
	[InitializeOnLoad]
	public static class DeformedReferenceResolver
	{
		private static readonly HashSet<int> Resolving = new HashSet<int>();

		static DeformedReferenceResolver()
		{
			ReferenceSurfaceUtility.DeformedMeshResolver = Resolve;
		}

		public static ReferenceMeshInfo Resolve(Renderer renderer)
		{
			if (renderer == null || !renderer.TryGetComponent<DeformStack>(out var stack))
				return default;
			if (DeformBakeCore.CollectEnabledDeformers(stack).Count == 0)
				return default;

			var id = renderer.GetInstanceID();
			if (!Resolving.Add(id))
				return default;
			try
			{
				var source = renderer is SkinnedMeshRenderer smr
					? smr.sharedMesh
					: renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
				if (source == null)
					return default;

				var activeShapes = renderer is SkinnedMeshRenderer activeSmr
					? DeformPreviewBakeCache.GetActiveShapeNames(activeSmr)
					: null;
				var entry = DeformPreviewBakeCache.Bake(stack, source, renderer.transform, activeShapes);
				if (entry == null || entry.Baked == null)
					return default;
				return new ReferenceMeshInfo { Mesh = entry.Baked, Version = entry.BakeSerial };
			}
			finally
			{
				Resolving.Remove(id);
			}
		}
	}
}
