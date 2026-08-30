using System;
using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// DeformStack・デフォーマの作成ロジック(GameObject メニューの実体)。
	/// Undo・プレハブインスタンス対応込み。メニュー以外(テスト等)からも呼べる。
	/// </summary>
	public static class NdmfDeformObjectFactory
	{
		/// <summary>
		/// target に DeformStack を追加する(SkinnedMeshRenderer または
		/// MeshFilter+MeshRenderer が必要)。追加済みならそれを返す。
		/// </summary>
		public static DeformStack AddStack(GameObject target)
		{
			if (target == null || PrefabUtility.IsPartOfPrefabAsset(target))
				return null;

			if (target.TryGetComponent<DeformStack>(out var existing))
				return existing;

			if (!HasRenderableMesh(target))
			{
				Debug.LogWarning(
					$"[NDMF Deform] {target.name} に SkinnedMeshRenderer / MeshFilter+MeshRenderer がないため Deform Stack を追加できません",
					target);
				return null;
			}

			return Undo.AddComponent<DeformStack>(target);
		}

		/// <summary>
		/// デフォーマを target の子 GameObject として作成し、target(または祖先)の
		/// DeformStack に登録する。スタックが無い場合、target にレンダラーがあれば
		/// スタックも自動追加する。作成できなければ null。
		/// </summary>
		public static DeformerBase CreateDeformer(GameObject target, Type deformerType, string name)
		{
			if (target == null || PrefabUtility.IsPartOfPrefabAsset(target))
				return null;

			var stack = target.GetComponentInParent<DeformStack>();
			if (stack == null && HasRenderableMesh(target))
				stack = Undo.AddComponent<DeformStack>(target);
			if (stack == null)
			{
				Debug.LogWarning(
					$"[NDMF Deform] {target.name} とその祖先に Deform Stack がありません。レンダラーのある GameObject から追加してください",
					target);
				return null;
			}

			Undo.SetCurrentGroupName($"Create {name}");

			var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(target.transform, name));
			Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
			go.transform.SetParent(target.transform, false);

			// AddComponent 時の Reset(Lattice の FitToParentStack 等)が
			// 親のスタックを参照できるよう、親子付けの後にコンポーネントを追加する
			var deformer = (DeformerBase)Undo.AddComponent(go, deformerType);

			Undo.RecordObject(stack, $"Create {name}");
			stack.AddDeformer(deformer);
			PrefabUtility.RecordPrefabInstancePropertyModifications(stack);

			Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
			return deformer;
		}

		private static bool HasRenderableMesh(GameObject go)
		{
			if (go.TryGetComponent<SkinnedMeshRenderer>(out _))
				return true;
			return go.TryGetComponent<MeshFilter>(out _) && go.TryGetComponent<MeshRenderer>(out _);
		}
	}
}
