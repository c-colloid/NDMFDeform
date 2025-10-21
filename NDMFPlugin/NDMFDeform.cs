using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using nadena.dev.ndmf;
using nadena.dev.ndmf.vrchat;
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
		
		/// <summary>
		/// NDMFDeformの動作を設定します
		/// </summary>
		protected override void Configure()
		{
			InPhase(BuildPhase.Transforming).Run("Generate DefromMesh",ctx =>{
				
				var target = VRChatContextExtensions.VRChatAvatarDescriptor(ctx).GetComponentsInChildren<Deformable>(true);
				if (target is null) return;
				var GOActiveDic = new Dictionary<GameObject,bool>();
				var MeshDic = new Dictionary<Deformable,Mesh>();
				
				target.ToList().ForEach(d => {
					if (d?.gameObject == null) return;
					
					GOActiveDic.TryAdd(d.gameObject,d.gameObject.activeSelf);
					d.gameObject.SetActive(true);
					var parent = d.transform.parent;
					while (parent != ctx.AvatarRootTransform && parent != null)
					{
						var tryAdd = GOActiveDic.TryAdd(parent.gameObject,parent.gameObject.activeSelf);
						if (!tryAdd) return;
						parent.gameObject.SetActive(true);
						parent = parent.parent;
					}
				});
				
				target.ToList().ForEach(d => {
					if (d == null || !d.enabled || d.CompareTag("EditorOnly")) return;
					
					var currentMesh = d.GetCurrentMesh();
					if (currentMesh == null) return;
					
					var mesh = Object.Instantiate(currentMesh);
					mesh.name = System.Text.RegularExpressions.Regex.Replace(mesh.name,@"(\(Clone\)){1,}","(Generated)");
					AssetDatabase.AddObjectToAsset(mesh,ctx.AssetContainer);
					
					MeshDic.TryAdd(d,mesh);
				});
				
				GOActiveDic.ToList().ForEach(d => {
					d.Key.SetActive(d.Value);
				});
				
				MeshDic.ToList().ForEach(d => {
					var skinnedRenderer = d.Key.GetComponent<SkinnedMeshRenderer>();
					if (skinnedRenderer != null)
					{
						skinnedRenderer.sharedMesh = d.Value;
					}
				});
			})
				.PreviewingWith(ConfigurePreview());
			
			//Deformableとそこに登録されているDeformerを削除
			InPhase(BuildPhase.Optimizing).BeforePlugin("com.anatawa12.avatar-optimizer").Run("Destroy Deformable",ctx =>{
				var target = VRChatContextExtensions.VRChatAvatarDescriptor(ctx).GetComponentsInChildren<Deformable>(true);
				if (target is null) return;
				var defomers = new HashSet<Deformer>();
				target.ToList().ForEach(d => {
					if (d == null) return;
					
					d.assignOriginalMeshOnDisable = false;
					
					if (d.DeformerElements != null)
					{
						d.DeformerElements.ForEach(e => {
							if (e?.Component != null)
								defomers.Add(e.Component);
						});
					}
					Object.DestroyImmediate(d);
				});
				defomers.ToList().ForEach(d =>Object.DestroyImmediate(d?.gameObject));
			});
			
			//残ったDeformerを削除
			InPhase(BuildPhase.Optimizing).BeforePlugin("com.anatawa12.avatar-optimizer").Run("Destroy Deformer",ctx =>{
				var target = VRChatContextExtensions.VRChatAvatarDescriptor(ctx).GetComponentsInChildren<Deformer>(true);
				if (target is null) return;
				target.ToList().ForEach(d => Object.DestroyImmediate(d?.gameObject));
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