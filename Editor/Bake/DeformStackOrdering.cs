using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// DeformStack のベイク順序。
	/// 他のレンダラーを参照するデフォーマ(IRendererReferences。Body Fit の体など)があり、
	/// その参照先にも DeformStack が付いている場合、参照先を先にベイクする
	/// (重ね着: 体 → 下着 → 服 の順に確定させ、変形後の形状へフィットさせるため)。
	/// 依存の無いスタック同士は入力順を保つ。循環参照は警告して入力順のまま並べる。
	/// </summary>
	public static class DeformStackOrdering
	{
		public static List<DeformStack> Sort(IReadOnlyList<DeformStack> stacks)
		{
			var result = new List<DeformStack>(stacks.Count);
			if (stacks.Count == 0)
				return result;

			var byRenderer = new Dictionary<Renderer, DeformStack>();
			foreach (var stack in stacks)
			{
				if (stack != null && stack.TryGetComponent<Renderer>(out var renderer) && renderer != null)
					byRenderer[renderer] = stack;
			}

			// 各スタックが依存する(先にベイクされるべき)スタックの集合
			var dependencies = new Dictionary<DeformStack, HashSet<DeformStack>>();
			var referenced = new List<Renderer>();
			foreach (var stack in stacks)
			{
				if (stack == null)
					continue;
				var deps = new HashSet<DeformStack>();
				foreach (var entry in stack.Deformers)
				{
					if (!entry.enabled || entry.deformer == null)
						continue;
					if (entry.deformer is not IRendererReferences refs)
						continue;
					referenced.Clear();
					refs.CollectReferencedRenderers(referenced);
					foreach (var renderer in referenced)
					{
						if (renderer != null && byRenderer.TryGetValue(renderer, out var other) && other != stack)
							deps.Add(other);
					}
				}
				dependencies[stack] = deps;
			}

			// Kahn 法(安定): 依存がすべて出力済みの最初のスタックを順に取り出す
			var remaining = new List<DeformStack>();
			foreach (var stack in stacks)
			{
				if (stack != null)
					remaining.Add(stack);
			}
			var emitted = new HashSet<DeformStack>();
			while (remaining.Count > 0)
			{
				var pickedIndex = -1;
				for (var i = 0; i < remaining.Count; i++)
				{
					var ready = true;
					foreach (var dep in dependencies[remaining[i]])
					{
						if (!emitted.Contains(dep))
						{
							ready = false;
							break;
						}
					}
					if (ready)
					{
						pickedIndex = i;
						break;
					}
				}

				if (pickedIndex < 0)
				{
					// 循環参照: 先頭を出して続行(結果は入力順に近づく)
					Debug.LogWarning(
						$"[NDMF Deform] Deform Stack の参照が循環しています({remaining[0].name} など)。入力順でベイクします",
						remaining[0]);
					pickedIndex = 0;
				}

				var picked = remaining[pickedIndex];
				remaining.RemoveAt(pickedIndex);
				emitted.Add(picked);
				result.Add(picked);
			}
			return result;
		}
	}
}
