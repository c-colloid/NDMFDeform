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

		// スライダー・位置ハンドルはワールド空間で描く(位置の変換のみ軸行列を通す)。
		// 軸 Transform が非一様スケールでも矢印キャップが潰れないようにするため。
		public void AxisSlider(string property, HandleAxis along, HandleLineStyle style = HandleLineStyle.Solid)
		{
			SliderInternal(property, along, sign: 1f, style);
		}

		public void RadiusSlider(string property, HandleAxis along, HandleLineStyle style = HandleLineStyle.Solid,
			float scale = 1f)
		{
			SliderInternal(property, along, sign: -1f, style, scale);
		}

		private void SliderInternal(string property, HandleAxis along, float sign, HandleLineStyle style,
			float scale = 1f)
		{
			var p = Find(property);
			if (p == null) return;
			if (Mathf.Abs(scale) < 1e-6f) return;

			var m = Handles.matrix;
			var dir = AxisVector(along);
			var world = m.MultiplyPoint3x4(sign * dir * (p.floatValue * scale));
			var worldDir = m.MultiplyVector(dir);
			if (worldDir.sqrMagnitude < 1e-12f) return;
			worldDir.Normalize();

			using (new Handles.DrawingScope(StyleColor(style), Matrix4x4.identity))
			{
				EditorGUI.BeginChangeCheck();
				var newWorld = Handles.Slider(world, worldDir);
				if (EditorGUI.EndChangeCheck())
				{
					var newLocal = m.inverse.MultiplyPoint3x4(newWorld);
					p.floatValue = sign * Vector3.Dot(newLocal, dir) / scale;
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

		public void Circle(HandleAxis normal, float offset, string radiusProperty,
			HandleLineStyle style = HandleLineStyle.Solid, float scale = 1f)
		{
			var rp = Find(radiusProperty);
			if (rp == null) return;

			var n = AxisVector(normal);
			using (ApplyStyle(style))
			{
				Handles.DrawWireDisc(n * offset, n, rp.floatValue * scale);
			}
		}

		public void Circle(HandleAxis normal, float offset, float radius, HandleLineStyle style = HandleLineStyle.Solid)
		{
			var n = AxisVector(normal);
			using (ApplyStyle(style))
			{
				Handles.DrawWireDisc(n * offset, n, radius);
			}
		}

		public void Box(string boundsProperty, HandleLineStyle style = HandleLineStyle.Solid)
		{
			var p = Find(boundsProperty);
			if (p == null) return;

			var b = p.boundsValue;
			using (ApplyStyle(style))
			{
				Handles.DrawWireCube(b.center, b.size);
			}

			// 面ハンドルはワールド空間で描く(非一様スケールでキャップが潰れないようにするため)
			var m = Handles.matrix;
			var min = b.min;
			var max = b.max;
			var boundsChanged = false;
			for (var axis = 0; axis < 3; axis++)
			{
				for (var sign = -1; sign <= 1; sign += 2)
				{
					var dir = Vector3.zero;
					dir[axis] = sign;
					var faceLocal = b.center + Vector3.Scale(dir, b.extents);
					var world = m.MultiplyPoint3x4(faceLocal);
					var worldDir = m.MultiplyVector(dir);
					if (worldDir.sqrMagnitude < 1e-12f) continue;
					worldDir.Normalize();

					using (new Handles.DrawingScope(StyleColor(style), Matrix4x4.identity))
					{
						EditorGUI.BeginChangeCheck();
						var size = HandleUtility.GetHandleSize(world) * 0.03f;
						var newWorld = Handles.Slider(world, worldDir, size, Handles.DotHandleCap, -1f);
						if (EditorGUI.EndChangeCheck())
						{
							var newFace = m.inverse.MultiplyPoint3x4(newWorld)[axis];
							if (sign > 0)
								max[axis] = Mathf.Max(newFace, min[axis]);
							else
								min[axis] = Mathf.Min(newFace, max[axis]);
							boundsChanged = true;
						}
					}
				}
			}

			if (boundsChanged)
			{
				var updated = new Bounds();
				updated.SetMinMax(min, max);
				p.boundsValue = updated;
				Changed = true;
			}
		}

		public void Position(string property)
		{
			var p = Find(property);
			if (p == null) return;

			var m = Handles.matrix;
			var world = m.MultiplyPoint3x4(p.vector3Value);
			using (new Handles.DrawingScope(Handles.color, Matrix4x4.identity))
			{
				EditorGUI.BeginChangeCheck();
				var newWorld = Handles.PositionHandle(world, Quaternion.identity);
				if (EditorGUI.EndChangeCheck())
				{
					p.vector3Value = m.inverse.MultiplyPoint3x4(newWorld);
					Changed = true;
				}
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
			return new Handles.DrawingScope(StyleColor(style));
		}

		private static Color StyleColor(HandleLineStyle style)
		{
			var color = Handles.color;
			if (style == HandleLineStyle.Dotted)
				color *= DottedTint;
			return color;
		}
	}
}
