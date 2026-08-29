using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// IHandleBuilder のシーンビュー実装。
	/// DeformerBaseEditor.OnSceneGUI から、軸空間の Handles.DrawingScope 内で呼ばれる。
	/// プロパティ編集は SerializedProperty 経由で行い、
	/// Undo・複数選択・プレハブオーバーライドは Unity のシリアライズ層に任せる。
	/// </summary>
	internal class SceneHandleBuilder : IHandleBuilder
	{
		private static readonly Color DottedTint = new Color(1f, 1f, 1f, 0.4f);

		private readonly SerializedObject _serializedObject;
		private readonly Dictionary<string, PointGridController> _pointGrids;

		public bool Changed { get; private set; }

		public SceneHandleBuilder(SerializedObject serializedObject,
			Dictionary<string, PointGridController> pointGrids = null)
		{
			_serializedObject = serializedObject;
			_pointGrids = pointGrids;
		}

		public void PointGrid(string pointsProperty, Vector3Int resolution, string mirrorAxisProperty = null)
		{
			if (_pointGrids == null) return;
			if (!_pointGrids.TryGetValue(pointsProperty, out var controller))
			{
				controller = new PointGridController();
				_pointGrids[pointsProperty] = controller;
			}
			if (controller.OnSceneGUI(_serializedObject, pointsProperty, resolution, mirrorAxisProperty))
				Changed = true;
		}

		public void AxisSlider(string property, HandleAxis along, HandleLineStyle style = HandleLineStyle.Solid)
		{
			var p = Find(property);
			if (p == null) return;

			var dir = AxisVector(along);
			var pos = dir * p.floatValue;
			using (ApplyStyle(style))
			{
				EditorGUI.BeginChangeCheck();
				var newPos = Handles.Slider(pos, dir);
				if (EditorGUI.EndChangeCheck())
				{
					p.floatValue = Vector3.Dot(newPos, dir);
					Changed = true;
				}
			}
		}

		public void RadiusSlider(string property, HandleAxis along, HandleLineStyle style = HandleLineStyle.Solid)
		{
			var p = Find(property);
			if (p == null) return;

			var dir = AxisVector(along);
			var pos = -dir * p.floatValue;
			using (ApplyStyle(style))
			{
				EditorGUI.BeginChangeCheck();
				var newPos = Handles.Slider(pos, dir);
				if (EditorGUI.EndChangeCheck())
				{
					p.floatValue = -Vector3.Dot(newPos, dir);
					Changed = true;
				}
			}
		}

		public void Circle(HandleAxis normal, string offsetProperty, string radiusProperty,
			HandleLineStyle style = HandleLineStyle.Solid)
		{
			var op = Find(offsetProperty);
			var rp = Find(radiusProperty);
			if (op == null || rp == null) return;

			var n = AxisVector(normal);
			using (ApplyStyle(style))
			{
				Handles.DrawWireDisc(n * op.floatValue, n, rp.floatValue);
			}
		}

		public void Position(string property)
		{
			var p = Find(property);
			if (p == null) return;

			EditorGUI.BeginChangeCheck();
			var newValue = Handles.PositionHandle(p.vector3Value, Quaternion.identity);
			if (EditorGUI.EndChangeCheck())
			{
				p.vector3Value = newValue;
				Changed = true;
			}
		}

		public void Line(Vector3 from, Vector3 to, HandleLineStyle style = HandleLineStyle.Solid)
		{
			using (ApplyStyle(style))
			{
				if (style == HandleLineStyle.Dotted)
					Handles.DrawDottedLine(from, to, 4f);
				else
					Handles.DrawLine(from, to);
			}
		}

		private SerializedProperty Find(string name)
		{
			var p = _serializedObject.FindProperty(name);
			if (p == null)
				Debug.LogWarning($"[NDMFDeform] DescribeHandles: プロパティ '{name}' が {_serializedObject.targetObject.GetType().Name} に見つかりません");
			return p;
		}

		private static Vector3 AxisVector(HandleAxis axis)
		{
			switch (axis)
			{
				case HandleAxis.X: return Vector3.right;
				case HandleAxis.Y: return Vector3.up;
				default: return Vector3.forward;
			}
		}

		private static Handles.DrawingScope ApplyStyle(HandleLineStyle style)
		{
			var color = Handles.color;
			if (style == HandleLineStyle.Dotted)
				color *= DottedTint;
			return new Handles.DrawingScope(color);
		}
	}
}
