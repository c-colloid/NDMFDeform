using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Deform;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Preview
{
	/// <summary>
	/// DeformライブラリのためのNDMFプレビュー実装
	/// </summary>
	public class DeformPreview : IRenderFilter
	{
		public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
		{
			// Deformableコンポーネントを持つレンダラーを検索
			var deformables = context.GetComponentsByType<Deformable>();
			var groups = new List<RenderGroup>();
            
			foreach (var deformable in deformables)
			{
				if (deformable.GetComponent<Renderer>() is Renderer renderer)
				{
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
			// レンダラーとプロキシのペアをリストに変換
			var pairs = new List<(Renderer, Renderer)>(proxyPairs);
			if (pairs.Count == 0)
				return null;

			var (originalRenderer, proxyRenderer) = pairs[0];
			var deformable = originalRenderer.GetComponent<Deformable>();
            
			if (deformable == null)
				return null;

			// Deformableコンポーネントの現在の設定を監視
			context.Observe(deformable);
            
			// Deformerの設定も監視
			foreach (var deformerElement in deformable.DeformerElements)
			{
				if (deformerElement.Component != null)
				{
					context.Observe(deformerElement.Component);
				}
			}

			// メッシュコンテナを監視
			if (deformable.GetMesh() != null)
			{
				context.Observe(deformable.GetMesh());
			}
            
			// SkinnedMeshRendererの場合
			if (originalRenderer is SkinnedMeshRenderer originalSmr && proxyRenderer is SkinnedMeshRenderer proxySmr)
			{
				// 新しいメッシュインスタンスを作成
				Mesh originalMesh = null;
                
				if (deformable.GetMesh() != null && deformable.GetOriginalMesh() != null)
				{
					originalMesh = deformable.GetOriginalMesh();
				}
				else if (originalSmr.sharedMesh != null)
				{
					originalMesh = originalSmr.sharedMesh;
				}
                
				if (originalMesh == null)
					return null;
                
				// ノードを作成して返す
				return Task.FromResult<IRenderFilterNode>(new DeformPreviewNode(deformable, originalRenderer, proxyRenderer));
			}
            
			// MeshRenderer + MeshFilterの場合
			if (originalRenderer is MeshRenderer originalMr && proxyRenderer is MeshRenderer proxyMr)
			{
				var originalFilter = originalRenderer.GetComponent<MeshFilter>();
				var proxyFilter = proxyRenderer.GetComponent<MeshFilter>();
                
				if (originalFilter == null || proxyFilter == null || originalFilter.sharedMesh == null)
					return null;

				// ノードを作成して返す
				return Task.FromResult<IRenderFilterNode>(new DeformPreviewNode(deformable, originalRenderer, proxyRenderer));
			}

			return null;
		}

		/// <summary>
		/// Deformプレビュー用のレンダーフィルターノード
		/// </summary>
		private class DeformPreviewNode : IRenderFilterNode
		{
			private Deformable _deformable;
			private Renderer _originalRenderer;
			private Renderer _proxyRenderer;
			private Mesh _lastPreviewMesh;
            
			public RenderAspects WhatChanged => RenderAspects.Mesh;

			public DeformPreviewNode(Deformable deformable, Renderer originalRenderer, Renderer proxyRenderer)
			{
				_deformable = deformable;
				_originalRenderer = originalRenderer;
				_proxyRenderer = proxyRenderer;
			}

			public void OnFrame(Renderer original, Renderer proxy)
			{
				if (_deformable == null || !_deformable.isActiveAndEnabled)
					return;

				// Deformの更新を確実に実行 - ここではJobシステムの処理結果を使用
				// Deformの通常の更新サイクルに任せるためにForceUpdateを使用
				_deformable.ForceImmediateUpdate();
                
				// 変形済みメッシュの取得
				Mesh deformedMesh = _deformable.GetCurrentMesh();
                
				if (deformedMesh == null)
					return;
                
				// プロキシレンダラーにメッシュを設定
				if (proxy is SkinnedMeshRenderer proxySmr)
				{
					proxySmr.sharedMesh = deformedMesh;
                    
					// 元のSkinnedMeshRendererの設定を引き継ぐ
					if (_originalRenderer is SkinnedMeshRenderer originalSmr)
					{
						proxySmr.sharedMaterials = originalSmr.sharedMaterials;
						proxySmr.localBounds = originalSmr.localBounds;
						proxySmr.rootBone = originalSmr.rootBone;
						proxySmr.quality = originalSmr.quality;
                        
						// ブレンドシェイプの重みをコピー
						if (deformedMesh.blendShapeCount > 0)
						{
							for (int i = 0; i < deformedMesh.blendShapeCount; i++)
							{
								proxySmr.SetBlendShapeWeight(i, originalSmr.GetBlendShapeWeight(i));
							}
						}
					}
				}
				else if (proxy is MeshRenderer proxyMr)
				{
					var proxyFilter = proxy.GetComponent<MeshFilter>();
					if (proxyFilter != null)
					{
						proxyFilter.sharedMesh = deformedMesh;
					}
                    
					// 元のMeshRendererの設定を引き継ぐ
					if (_originalRenderer is MeshRenderer originalMr)
					{
						proxyMr.sharedMaterials = originalMr.sharedMaterials;
					}
				}
                
				// レンダリング設定のコピー
				proxy.shadowCastingMode = original.shadowCastingMode;
				proxy.receiveShadows = original.receiveShadows;
				proxy.lightProbeUsage = original.lightProbeUsage;
				proxy.reflectionProbeUsage = original.reflectionProbeUsage;
                
				// 最後に処理したメッシュを記録
				_lastPreviewMesh = deformedMesh;
			}

			public void Dispose()
			{
				// 必要なクリーンアップを行う
				// Deformライブラリ自体がメッシュの破棄を管理するのでここでは何もしない
			}
		}
	}
}