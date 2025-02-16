//using System;
//using System.Collections;
//using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
//using UnityEngine.UIElements;
//using UnityEditor.UIElements;
//using Deform;
using DeformEditor;

namespace MeshModifier.NDMFDeform.ExDeform
{
	[CustomEditor (typeof(CylindricalScaleDeformer)),CanEditMultipleObjects]
	public class CylinderScakerDeformerEditor : DeformerEditor
	{
		private static class Content
		{
			public static readonly GUIContent Factor = DeformEditorGUIUtility.DefaultContent.Factor;
			public static readonly GUIContent Radius = new GUIContent (text: "Radius", tooltip: "The cylinder radius.");
			public static readonly GUIContent Scope = new GUIContent (text: "Scope", tooltip: "The cylinder deform scope.");
			public static readonly GUIContent Top = DeformEditorGUIUtility.DefaultContent.Top;
			public static readonly GUIContent Bottom = DeformEditorGUIUtility.DefaultContent.Bottom;
			public static readonly GUIContent Axis = DeformEditorGUIUtility.DefaultContent.Axis;
		}
		
		private class Properties
		{
			public SerializedProperty Factor;
			public SerializedProperty Radius;
			public SerializedProperty Scope;
			public SerializedProperty Top;
			public SerializedProperty Bottom;
			public SerializedProperty Axis;
			
			public Properties (SerializedObject obj)
			{
				Factor = obj.FindProperty ("factor");
				Radius = obj.FindProperty ("radius");
				Scope = obj.FindProperty ("scope");
				Top = obj.FindProperty ("top");
				Bottom = obj.FindProperty ("bottom");
				Axis = obj.FindProperty ("axis");
			}
		}
		
		private Properties properties;
			
		protected override void OnEnable()
		{
			base.OnEnable();
			properties = new Properties (serializedObject);
		}
			
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();
				
			serializedObject.UpdateIfRequiredOrScript();
			EditorGUILayout.Slider(properties.Factor, 0f, 1f,Content.Factor);
			EditorGUILayout.PropertyField(properties.Radius,Content.Radius);
			EditorGUILayout.PropertyField(properties.Scope, Content.Scope);
			EditorGUILayout.PropertyField(properties.Top, Content.Top);
			EditorGUILayout.PropertyField(properties.Bottom, Content.Bottom);
			EditorGUILayout.PropertyField(properties.Axis,Content.Axis);
				
			serializedObject.ApplyModifiedProperties();
				
			EditorApplication.QueuePlayerLoopUpdate();
		}
		
		public override void OnSceneGUI() {
			base.OnSceneGUI();
			
			if (target == null) return;
			
			var cylinderscaler = target as CylindricalScaleDeformer;
			
			DrawRadiusHandle(cylinderscaler);
			DrawScopeHandle(cylinderscaler);
			
			
			EditorApplication.QueuePlayerLoopUpdate();
		}
		
		private void DrawRadiusHandle(CylindricalScaleDeformer cylinderscaler)
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
		
		private void DrawScopeHandle(CylindricalScaleDeformer cylinderscaler)
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
				
				//using (var check = new EditorGUI.ChangeCheckScope
				//())
				//{
				//	var newWorldPosition = DeformHandles.Slider(positon + Vector3.forward * cylinderscaler.Top, Vector3.forward);
				//	if (check.changed)
				//	{
				//		Undo.RecordObject(cylinderscaler, "Changed Top");
				//		cylinderscaler.Top = newWorldPosition.z;
				//	}
				//}
				
				//using (var check = new EditorGUI.ChangeCheckScope
				//())
				//{
				//	var newWorldPosition = DeformHandles.Slider(positon + Vector3.forward * cylinderscaler.Bottom, Vector3.forward);
				//	if (check.changed)
				//	{
				//		Undo.RecordObject(cylinderscaler, "Changed Bottom");
				//		cylinderscaler.Bottom = newWorldPosition.z;
				//	}
				//}
			}
		}
	}
}