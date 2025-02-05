//using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using nadena.dev.ndmf;
using Deform;

[assembly: ExportsPlugin(typeof(MeshModifier.NDMFDeform.NDMFPlugin.NDMFDeform))]
namespace MeshModifier.NDMFDeform.NDMFPlugin
{
	public class NDMFDeform : Plugin<NDMFDeform>
	{
		protected override void Configure()
		{
			InPhase(BuildPhase.Transforming).Run("Generate DefromMesh",ctx =>{
				var target = ctx?.AvatarDescriptor.GetComponentsInChildren<Deformable>();
				if (target is null) return;
				target.ToList().ForEach(d => {
					d.ApplyData();
					var mesh = d.GetCurrentMesh();
					AssetDatabase.AddObjectToAsset(mesh,ctx.AssetContainer);
					d.GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
				});
			});
			
			InPhase(BuildPhase.Optimizing).Run("Destroy Deformable",ctx =>{
				var target = ctx?.AvatarDescriptor.GetComponentsInChildren<Deformable>();
				if (target is null) return;
				var defomers = new HashSet<Deformer>();
				target.ToList().ForEach(d => {
					d.assignOriginalMeshOnDisable = false;
					
					d.DeformerElements.ForEach(e => defomers.Add(e.Component));
					Object.DestroyImmediate(d);
				});
				defomers.ToList().ForEach(d => Object.DestroyImmediate(d.gameObject));
			});
		}
	}
}