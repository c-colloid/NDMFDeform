using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace MeshModifier.NDMFDeform.NDMF
{
	/// <summary>
	/// DeformStack の NDMF プレビュー。
	/// 変形は Instantiate 内でプロキシ専有のメッシュに対して一度だけ計算し、
	/// OnFrame ではメッシュ割当てのみを行う(シーン側のレンダラー・メッシュには一切触れない)。
	/// パラメータ変更は ComputeContext の監視により無効化 → 再計算される。
	/// M2 でハンドルドラッグ中の高速パス(hot preview)を追加予定。
	/// </summary>
	public class DeformStackPreview : IRenderFilter
	{
		public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
		{
			var groups = new List<RenderGroup>();
			foreach (var stack in context.GetComponentsByType<DeformStack>())
			{
				if (stack.TryGetComponent<Renderer>(out var renderer))
					groups.Add(RenderGroup.For(renderer));
			}
			return groups.ToImmutableList();
		}

		public Task<IRenderFilterNode> Instantiate(
			RenderGroup group,
			IEnumerable<(Renderer, Renderer)> proxyPairs,
			ComputeContext context)
		{
			var pairs = new List<(Renderer, Renderer)>(proxyPairs);
			if (pairs.Count == 0)
				return null;

			var (original, _) = pairs[0];
			var stack = original.GetComponent<DeformStack>();
			if (stack == null)
				return null;

			// スタック構成・各デフォーマのパラメータ・軸 Transform を監視する
			context.Observe(stack);
			foreach (var entry in stack.Deformers)
			{
				if (entry.deformer == null)
					continue;
				context.Observe(entry.deformer);
				context.Observe(entry.deformer.Axis);
			}

			var source = BakeDeformStacksPass.GetSourceMesh(stack, out _, out _);
			if (source == null)
				return null;

			// プレビューでは現在重みが非 0 のシェイプだけ再ベイクして編集中の再計算を軽くする。
			// アクティブ集合(0 ↔ 非 0)の変化を監視するので、新たに動かしたシェイプも
			// その時点で正しいデルタに再計算される。ビルドは常に全シェイプを再ベイクする
			HashSet<string> activeShapes = null;
			if (original is SkinnedMeshRenderer originalSmr)
			{
				activeShapes = context.Observe(originalSmr,
					smr => GetActiveShapeNames(smr),
					(a, b) => a.SetEquals(b));
			}

			var options = new DeformBakeOptions
			{
				RebakeBlendShapes = true,
				ShapesToRebake = activeShapes,
			};
			var baked = DeformBakeCore.Bake(stack, source, original.transform, options);
			return Task.FromResult<IRenderFilterNode>(new Node(baked));
		}

		private static HashSet<string> GetActiveShapeNames(SkinnedMeshRenderer smr)
		{
			var names = new HashSet<string>();
			var mesh = smr.sharedMesh;
			if (mesh == null)
				return names;

			for (var i = 0; i < mesh.blendShapeCount; i++)
			{
				if (!Mathf.Approximately(smr.GetBlendShapeWeight(i), 0f))
					names.Add(mesh.GetBlendShapeName(i));
			}
			return names;
		}

		private class Node : IRenderFilterNode
		{
			private Mesh _baked;

			public RenderAspects WhatChanged => RenderAspects.Mesh;

			public Node(Mesh baked)
			{
				_baked = baked;
			}

			public void OnFrame(Renderer original, Renderer proxy)
			{
				if (_baked == null)
					return;

				if (proxy is SkinnedMeshRenderer proxySmr)
				{
					proxySmr.sharedMesh = _baked;
				}
				else if (proxy is MeshRenderer && proxy.TryGetComponent<MeshFilter>(out var proxyFilter))
				{
					proxyFilter.sharedMesh = _baked;
				}
			}

			public void Dispose()
			{
				if (_baked != null)
				{
					Object.DestroyImmediate(_baked);
					_baked = null;
				}
			}
		}
	}
}
