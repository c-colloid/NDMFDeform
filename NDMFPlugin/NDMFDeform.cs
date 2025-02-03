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
				if (target == null) return;
				target.ToList().ForEach(d => d.ApplyData());
			});
			
			InPhase(BuildPhase.Optimizing).Run("Destroy Deformable",ctx =>{
				var target = ctx?.AvatarDescriptor.GetComponentsInChildren<Deformable>();
				if (target is null) return;
				target.ToList().ForEach(d => {
					d.assignOriginalMeshOnDisable = false;
					
					d.DeformerElements.ForEach(e => Object.DestroyImmediate(e.Component.gameObject));
					Object.DestroyImmediate(d);
				});
			});
		}
	}
}