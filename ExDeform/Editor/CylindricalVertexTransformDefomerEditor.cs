using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
//using UnityEditor.UIElements;
//using Deform;
using DeformEditor;

namespace MeshModifier.NDMFDeform.ExDeform
{
	[CustomEditor (typeof(CylindricalVertexTransformDefomer)),CanEditMultipleObjects]
	public class CylindricalVertexTransformDefomerEditor : DeformerEditor
	{
		[SerializeField]
		VisualTreeAsset UXML;
		
		public override void OnInspectorGUI() {
			base.OnInspectorGUI();
		}
		
		public override VisualElement CreateInspectorGUI() {
			var root = base.CreateInspectorGUI() ?? new VisualElement();
			root.Add(new IMGUIContainer(OnInspectorGUI));
			
			UXML.CloneTree(root);
			
			//InspectorElement.FillDefaultInspector(root,serializedObject,this);
			return root;
		}
		
		public override void OnSceneGUI() {
			base.OnSceneGUI();
			
			if (target == null) return;
			
			var cylinderscaler = target as CylindricalVertexTransformDefomer;
			
			DrawRadiusHandle(cylinderscaler);
			DrawScopeHandle(cylinderscaler);
			
			
			EditorApplication.QueuePlayerLoopUpdate();
		}
		
		private void DrawRadiusHandle(CylindricalVertexTransformDefomer cylinderscaler)
		{
			var positon = Vector3.down * cylinderscaler.Radius;
			
			var axis = cylinderscaler.Axis;
			using (new Handles.DrawingScope(Matrix4x4.TRS(axis.position, axis.rotation, axis.lossyScale)))
			{
				var size = HandleUtility.GetHandleSize(positon) * DeformEditorSettings.ScreenspaceSliderHandleCapSize;
				
				DeformHandles.Circle(Vector3.forward * cylinderscaler.Top, Vector3.forward, Vector3.up, cylinderscaler.Radius);
				DeformHandles.Circle(Vector3.forward * cylinderscaler.Bottom, Vector3.forward, Vector3.up, cylinderscaler.Radius);
				
				DeformHandles.Line(positon + Vector3.forward * size, positon + Vector3.forward * cylinderscaler.Top, DeformHandles.LineMode.Light);
				DeformHandles.Line(positon - Vector3.forward * size, positon + Vector3.forward * cylinderscaler.Bottom, DeformHandles.LineMode.Light);
				
				using (var check = new EditorGUI.ChangeCheckScope
				())
				{
					var newWorldPosition = DeformHandles.Slider(positon, Vector3.up);
					if (check.changed)
					{
						Undo.RecordObject(cylinderscaler, "Changed Radius");
						cylinderscaler.Radius = -newWorldPosition.y;
					}
				}
				
				using (var check = new EditorGUI.ChangeCheckScope
				())
				{
					var newWorldPosition = DeformHandles.Slider(Vector3.zero + Vector3.forward * cylinderscaler.Top, Vector3.forward);
					if (check.changed)
					{
						Undo.RecordObject(cylinderscaler, "Changed Top");
						cylinderscaler.Top = newWorldPosition.z;
					}
				}
				
				using (var check = new EditorGUI.ChangeCheckScope
				())
				{
					var newWorldPosition = DeformHandles.Slider(Vector3.zero + Vector3.forward * cylinderscaler.Bottom, Vector3.forward);
					if (check.changed)
					{
						Undo.RecordObject(cylinderscaler, "Changed Bottom");
						cylinderscaler.Bottom = newWorldPosition.z;
					}
				}
			}
		}
		
		private void DrawScopeHandle(CylindricalVertexTransformDefomer cylinderscaler)
		{
			var positon = Vector3.down * cylinderscaler.Scope;
			
			var axis = cylinderscaler.Axis;
			using (new Handles.DrawingScope(Matrix4x4.TRS(axis.position, axis.rotation, axis.lossyScale)))
			{
				var size = HandleUtility.GetHandleSize(positon) * DeformEditorSettings.ScreenspaceSliderHandleCapSize;
				
				DeformHandles.Circle(Vector3.forward * cylinderscaler.Top, Vector3.forward, Vector3.up, cylinderscaler.Scope);
				DeformHandles.Circle(Vector3.forward * cylinderscaler.Bottom, Vector3.forward, Vector3.up, cylinderscaler.Scope);
				
				DeformHandles.Line(positon + Vector3.forward * size, positon + Vector3.forward * cylinderscaler.Top, DeformHandles.LineMode.LightDotted);
				DeformHandles.Line(positon - Vector3.forward * size, positon + Vector3.forward * cylinderscaler.Bottom, DeformHandles.LineMode.LightDotted);
				
				using (var check = new EditorGUI.ChangeCheckScope
				())
				{
					var newWorldPosition = DeformHandles.Slider(positon, Vector3.up);
					if (check.changed)
					{
						Undo.RecordObject(cylinderscaler, "Changed Scope");
						cylinderscaler.Scope = -newWorldPosition.y;
					}
				}
			}
		}
	}
}