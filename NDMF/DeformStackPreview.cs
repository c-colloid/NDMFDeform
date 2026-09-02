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
			// NDMF が処理するのはアバタールート(AvatarDescriptor 等)配下のみなので、
			// プレビューも同じ範囲に限定する。アバター外のスタックまで対象にすると
			// 「プレビューでは変形するのにビルド / プレイでは適用されない」不整合や、
			// プレイモード遷移時の NDMF セッション再構築で例外の原因になる。
			// GetAvatarRoots / GetComponentsInChildren は反応的なので、
			// 後から AvatarDescriptor を付けた場合も自動で対象に入る
			var groups = new List<RenderGroup>();
			var seen = new HashSet<Renderer>();
			foreach (var root in context.GetAvatarRoots())
			{
				foreach (var stack in context.GetComponentsInChildren<DeformStack>(root, true))
				{
					if (stack.TryGetComponent<Renderer>(out var renderer) && seen.Add(renderer))
						groups.Add(RenderGroup.For(renderer));
				}
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
			var referenced = new List<Renderer>();
			foreach (var entry in stack.Deformers)
			{
				if (entry.deformer == null)
					continue;
				context.Observe(entry.deformer);
				context.Observe(entry.deformer.Axis);
				if (entry.deformer is IRendererReferences refs)
					refs.CollectReferencedRenderers(referenced);
			}

			// 参照レンダラー(Body Fit の体など)とその Transform、参照先にも Deform Stack が
			// あればその構成も監視する(重ね着: 参照先の変形が変わったら再ベイク)
			foreach (var renderer in referenced)
			{
				if (renderer == null)
					continue;
				context.Observe(renderer);
				context.Observe(renderer.transform);
				if (!renderer.TryGetComponent<DeformStack>(out var referencedStack))
					continue;
				context.Observe(referencedStack);
				foreach (var entry in referencedStack.Deformers)
				{
					if (entry.deformer == null)
						continue;
					context.Observe(entry.deformer);
					context.Observe(entry.deformer.Axis);
				}
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
					smr => DeformPreviewBakeCache.GetActiveShapeNames(smr),
					(a, b) => a.SetEquals(b));
			}

			// キャッシュ経由のホットパス: パラメータ変更だけの再ベイクは
			// メッシュ複製なしで頂点更新のみになる(キャッシュがメッシュを所有する)
			var previewEntry = DeformPreviewBakeCache.Bake(stack, source, original.transform, activeShapes);
			return Task.FromResult<IRenderFilterNode>(new Node(previewEntry));
		}

		private class Node : IRenderFilterNode
		{
			private readonly DeformPreviewBakeCache.Entry _entry;

			public RenderAspects WhatChanged => RenderAspects.Mesh;

			public Node(DeformPreviewBakeCache.Entry entry)
			{
				_entry = entry;
			}

			public void OnFrame(Renderer original, Renderer proxy)
			{
				// メッシュはキャッシュが所有・更新する(追いかけフルベイクで
				// インスタンスが差し替わることがあるため毎フレーム参照する)
				var baked = _entry?.Baked;
				if (baked == null)
					return;

				if (proxy is SkinnedMeshRenderer proxySmr)
				{
					proxySmr.sharedMesh = baked;
				}
				else if (proxy is MeshRenderer && proxy.TryGetComponent<MeshFilter>(out var proxyFilter))
				{
					proxyFilter.sharedMesh = baked;
				}
			}

			public void Dispose()
			{
				// メッシュはキャッシュ所有のため破棄しない
			}
		}
	}
}
