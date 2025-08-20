//using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using Deform;
using MeshModifier.NDMFDeform.Preview;

[assembly: ExportsPlugin(typeof(MeshModifier.NDMFDeform.NDMFPlugin.NDMFDeform))]
namespace MeshModifier.NDMFDeform.NDMFPlugin
{
	public class NDMFDeform : Plugin<NDMFDeform>
	{
		private static DeformPreview _previewFilter;
		private static SequencePoint _previewSequence;
		private static TogglablePreviewNode _previewToggle;
		
		public override string DisplayName => "NDMFDeform";
		
		protected override void Configure()
		{
			InPhase(BuildPhase.Transforming).Run("Generate DefromMesh",ctx =>{
				var target = ctx?.AvatarDescriptor.GetComponentsInChildren<Deformable>(true);
				if (target is null) return;
				target.ToList().ForEach(d => {
					if (!d.enabled) return;
					d.ApplyData();
					var mesh = d.GetCurrentMesh();
					AssetDatabase.AddObjectToAsset(mesh,ctx.AssetContainer);
					d.GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
				});
			})
				.PreviewingWith(ConfigurePreview());
			
			InPhase(BuildPhase.Optimizing).Run("Destroy Deformable",ctx =>{
				var target = ctx?.AvatarDescriptor.GetComponentsInChildren<Deformable>(true);
				if (target is null) return;
				var defomers = new HashSet<Deformer>();
				target.ToList().ForEach(d => {
					d.assignOriginalMeshOnDisable = false;
					
					d.DeformerElements.ForEach(e => defomers.Add(e.Component));
					Object.DestroyImmediate(d);
				});
				defomers.ToList().ForEach(d =>Object.DestroyImmediate(d?.gameObject));
			});
			
		}
		
		/// <summary>
		/// NDMFプレビューシステムの設定を行います
		/// </summary>
		private IRenderFilter ConfigurePreview()
		{
			if (_previewFilter == null)
			{
				// プレビューフィルターの作成
				_previewFilter = new DeformPreview();
                
				// プレビュートグルノードの作成
				_previewToggle = TogglablePreviewNode.Create(
					() => "Deform Preview",
					"MeshModifier.NDMFDeform.Preview",
					true
				);
                
				// トグルの状態を監視して、プレビューを制御
				_previewToggle.IsEnabled.OnChange += isEnabled => {
					if (isEnabled)
					{
						EnableDeformPreview();
					}
					else
					{
						DisableDeformPreview();
					}
				};
                
				// 初期状態のプレビューを設定
				if (_previewToggle.IsEnabled.Value)
				{
					EnableDeformPreview();
				}
			}
            
			return _previewFilter;
		}

		/// <summary>
		/// Deformプレビューを有効化します
		/// </summary>
		private void EnableDeformPreview()
		{
			// Deformライブラリが自動アップデートするように設定
			foreach (var deformable in Object.FindObjectsOfType<Deformable>())
			{
				/**
				// Deformableコンポーネントの設定を保存
				deformable.CanUpdate() = true;
                
				// メッシュデータの遅延更新を無効化して、即時更新を有効化する
				// これにより、Jobsシステムの結果をすぐに反映できる
				deformable.UpdateMode = UpdateMode.Auto;
				**/
			}
		}

		/// <summary>
		/// Deformプレビューを無効化します
		/// </summary>
		private void DisableDeformPreview()
		{
			// プレビュー無効時にDeformableの状態を元に戻す場合はここに実装
		}
	}
}