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
	///
	/// スタイル方針(「常態はシンプル、豪華さは操作の瞬間だけ」):
	/// - 常態: 白の主従2階調(Solid=実線 / Dotted=点線・減光)+小ドットキャップ
	/// - ホバー: 対象のキャップと同プロパティのリングが意味色に点灯し、
	///   影響範囲が広がる方向の小矢印が出る(Solid=シアン / Dotted=オレンジ)
	/// - ドラッグ中: さらに実効範囲の半透明フィルと数値表示、離せば常態に戻る
	/// </summary>
	internal class SceneHandleBuilder : IHandleBuilder
	{
		private enum Interaction
		{
			None,
			Hover,
			Drag,
		}

		private static readonly Color DottedTint = new Color(1f, 1f, 1f, 0.35f);
		private static readonly Color PrimaryAccent = new Color(0.34f, 0.84f, 0.91f);   // シアン(主)
		private static readonly Color SecondaryAccent = new Color(0.94f, 0.64f, 0.29f); // オレンジ(従)

		private const float SolidCapScale = 0.045f;
		private const float LightCapScale = 0.036f;
		private const float EngagedCapBoost = 1.25f;

		// リング点灯をスライダーのホバー状態と結びつけるための状態表
		// (ドメインリロードで消えてよい表示状態。キー = 対象オブジェクト + プロパティパス)
		private static readonly Dictionary<(int target, string property), Interaction> InteractionStates =
			new Dictionary<(int, string), Interaction>();

		private static GUIStyle _valueLabelStyle;

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
			float scale = 1f, string offsetProperty = null, HandleAxis offsetAxis = HandleAxis.Z)
		{
			var origin = Vector3.zero;
			var fillNormal = Vector3.zero;
			if (offsetProperty != null)
			{
				var op = Find(offsetProperty);
				if (op != null)
					origin = AxisVector(offsetAxis) * op.floatValue;
				// オフセット付き(円柱など)はキャップの載っているリング平面でフィルする
				fillNormal = AxisVector(offsetAxis);
			}
			SliderInternal(property, along, sign: -1f, style, scale, origin, fillNormal);
		}

		private void SliderInternal(string property, HandleAxis along, float sign, HandleLineStyle style,
			float scale = 1f, Vector3 origin = default, Vector3 fillNormal = default)
		{
			var p = Find(property);
			if (p == null) return;
			if (Mathf.Abs(scale) < 1e-6f) return;

			var m = Handles.matrix;
			var dir = AxisVector(along);
			var world = m.MultiplyPoint3x4(origin + sign * dir * (p.floatValue * scale));
			var worldDir = m.MultiplyVector(dir);
			if (worldDir.sqrMagnitude < 1e-12f) return;
			worldDir.Normalize();

			var id = GUIUtility.GetControlID(FocusType.Passive);
			var interaction = GetInteraction(id);
			SetInteraction(p, interaction);

			var engaged = interaction != Interaction.None;
			var color = engaged ? AccentColor(style) : StyleColor(style);
			var handleSize = HandleUtility.GetHandleSize(world);
			var capSize = handleSize * (style == HandleLineStyle.Solid ? SolidCapScale : LightCapScale);
			if (engaged)
				capSize *= EngagedCapBoost;

			using (new Handles.DrawingScope(color, Matrix4x4.identity))
			{
				// 影響範囲が広がる方向の小矢印(ホバー/ドラッグ中のみ)
				var growDir = worldDir * Mathf.Sign(sign);
				if (engaged)
					DrawArrowGlyph(world + growDir * (capSize * 2.5f), growDir, handleSize * 0.28f);

				EditorGUI.BeginChangeCheck();
				var newWorld = Handles.Slider(id, world, worldDir, capSize, Handles.DotHandleCap, 0f);
				if (EditorGUI.EndChangeCheck())
				{
					var newLocal = m.inverse.MultiplyPoint3x4(newWorld) - origin;
					p.floatValue = sign * Vector3.Dot(newLocal, dir) / scale;
					Changed = true;
				}

				if (interaction == Interaction.Drag)
					DrawValueLabel(world, p.floatValue * scale, color);
			}

			// 半径スライダーのドラッグ中は実効範囲を面で提示
			// (リング平面が指定されていればその平面、なければカメラ正対ディスク)
			if (sign < 0f && interaction == Interaction.Drag)
				DrawRadiusFill(m, origin, p.floatValue * scale, AccentColor(style), fillNormal);
		}

		public void Circle(HandleAxis normal, string offsetProperty, string radiusProperty,
			HandleLineStyle style = HandleLineStyle.Solid)
		{
			var op = Find(offsetProperty);
			var rp = Find(radiusProperty);
			if (op == null || rp == null) return;

			var engaged = IsEngaged(offsetProperty) || IsEngaged(radiusProperty);
			var n = AxisVector(normal);
			using (new Handles.DrawingScope(engaged ? AccentColor(style) : StyleColor(style)))
			{
				Handles.DrawWireDisc(n * op.floatValue, n, rp.floatValue, engaged ? 2f : 0f);
			}
		}

		public void Circle(HandleAxis normal, float offset, string radiusProperty,
			HandleLineStyle style = HandleLineStyle.Solid, float scale = 1f)
		{
			var rp = Find(radiusProperty);
			if (rp == null) return;

			var engaged = IsEngaged(radiusProperty);
			var n = AxisVector(normal);
			using (new Handles.DrawingScope(engaged ? AccentColor(style) : StyleColor(style)))
			{
				Handles.DrawWireDisc(n * offset, n, rp.floatValue * scale, engaged ? 2f : 0f);
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

					var id = GUIUtility.GetControlID(FocusType.Passive);
					var interaction = GetInteraction(id);
					var engaged = interaction != Interaction.None;
					var color = engaged ? AccentColor(style) : StyleColor(style);
					var handleSize = HandleUtility.GetHandleSize(world);
					var capSize = handleSize * (style == HandleLineStyle.Solid ? SolidCapScale : LightCapScale);
					if (engaged)
						capSize *= EngagedCapBoost;

					using (new Handles.DrawingScope(color, Matrix4x4.identity))
					{
						// ホバー面だけ外向き法線矢印を出す
						if (engaged)
							DrawArrowGlyph(world + worldDir * (capSize * 2.5f), worldDir, handleSize * 0.28f);

						EditorGUI.BeginChangeCheck();
						var newWorld = Handles.Slider(id, world, worldDir, capSize, Handles.DotHandleCap, 0f);
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

					// ドラッグ中の面は半透明フィルで提示する
					if (interaction == Interaction.Drag)
						DrawFaceFill(b, axis, sign, AccentColor(style));
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

		public void Arrow(Vector3 from, Vector3 to, HandleLineStyle style = HandleLineStyle.Solid)
		{
			if (Event.current.type != EventType.Repaint)
				return;

			// 矢頭はワールド空間で描く(軸の非一様スケールで潰れないようにするため)
			var m = Handles.matrix;
			var worldFrom = m.MultiplyPoint3x4(from);
			var worldTo = m.MultiplyPoint3x4(to);
			var dir = worldTo - worldFrom;
			if (dir.sqrMagnitude < 1e-12f) return;
			dir.Normalize();

			using (new Handles.DrawingScope(StyleColor(style), Matrix4x4.identity))
			{
				Handles.DrawLine(worldFrom, worldTo);
				Handles.ConeHandleCap(0, worldTo, Quaternion.LookRotation(dir),
					HandleUtility.GetHandleSize(worldTo) * 0.1f, EventType.Repaint);
			}
		}

		public void DecaySlider(string property, HandleAxis along, float k, float ringRadius,
			HandleLineStyle style = HandleLineStyle.Solid)
		{
			var p = Find(property);
			if (p == null) return;
			var value = p.floatValue;
			if (value <= 1e-4f) return;

			var m = Handles.matrix;
			var dir = AxisVector(along);
			var distance = k / value;
			// キャップは軸線上ではなくリングの縁に置く(軸線・矢印に埋もれないように)
			var rim = AxisVector(along == HandleAxis.Y ? HandleAxis.Z : HandleAxis.Y) * ringRadius;
			var world = m.MultiplyPoint3x4(dir * distance + rim);
			var worldDir = m.MultiplyVector(dir);
			if (worldDir.sqrMagnitude < 1e-12f) return;
			worldDir.Normalize();

			var id = GUIUtility.GetControlID(FocusType.Passive);
			var interaction = GetInteraction(id);
			SetInteraction(p, interaction);

			var engaged = interaction != Interaction.None;
			var accent = AccentColor(style);
			var color = engaged ? accent : StyleColor(style);
			var handleSize = HandleUtility.GetHandleSize(world);
			var capSize = handleSize * LightCapScale;
			if (engaged)
				capSize *= EngagedCapBoost;

			// キャップ位置(減衰 50% の距離)のリング
			using (new Handles.DrawingScope(engaged ? accent : StyleColor(HandleLineStyle.Dotted)))
			{
				Handles.DrawWireDisc(dir * distance, dir, ringRadius, engaged ? 2f : 0f);
			}

			using (new Handles.DrawingScope(color, Matrix4x4.identity))
			{
				if (engaged)
					DrawArrowGlyph(world + worldDir * (capSize * 2.5f), worldDir, handleSize * 0.28f);

				EditorGUI.BeginChangeCheck();
				var newWorld = Handles.Slider(id, world, worldDir, capSize, Handles.DotHandleCap, 0f);
				if (EditorGUI.EndChangeCheck())
				{
					// rim は dir と直交するため Dot で距離成分だけが残る
					var newDistance = Vector3.Dot(m.inverse.MultiplyPoint3x4(newWorld), dir);
					p.floatValue = k / Mathf.Max(newDistance, 1e-3f);
					Changed = true;
				}

				if (interaction == Interaction.Drag)
					DrawValueLabel(world, p.floatValue, color);
			}

			// ドラッグ中は減衰カーブを「リングの櫛」で提示する
			if (interaction == Interaction.Drag && Event.current.type == EventType.Repaint)
			{
				var falloff = p.floatValue;
				var maxDistance = Mathf.Log(10f) / falloff * 1.1f;
				const int combCount = 8;
				for (var i = 0; i < combCount; i++)
				{
					var z = maxDistance * i / (combCount - 1);
					var alpha = 0.12f + 0.65f * Mathf.Exp(-falloff * z);
					using (new Handles.DrawingScope(new Color(accent.r, accent.g, accent.b, alpha)))
					{
						Handles.DrawWireDisc(dir * z, dir, ringRadius);
					}
				}
			}
		}

		// ---- 内部ヘルパー ----

		private static Interaction GetInteraction(int controlId)
		{
			if (GUIUtility.hotControl == controlId)
				return Interaction.Drag;
			if (GUIUtility.hotControl == 0 && HandleUtility.nearestControl == controlId)
				return Interaction.Hover;
			return Interaction.None;
		}

		/// <summary>リング点灯用に、プロパティ毎のホバー/ドラッグ状態を記録する</summary>
		private void SetInteraction(SerializedProperty property, Interaction interaction)
		{
			var key = (_serializedObject.targetObject.GetInstanceID(), property.propertyPath);
			InteractionStates.TryGetValue(key, out var previous);
			if (previous == interaction)
				return;
			InteractionStates[key] = interaction;
			SceneView.RepaintAll();
		}

		private bool IsEngaged(string property)
		{
			return InteractionStates.TryGetValue(
				       (_serializedObject.targetObject.GetInstanceID(), property), out var state) &&
			       state != Interaction.None;
		}

		/// <summary>影響方向を示す小さな矢印(ワールド空間・表示専用)</summary>
		private static void DrawArrowGlyph(Vector3 from, Vector3 worldDir, float length)
		{
			if (Event.current.type != EventType.Repaint)
				return;
			var to = from + worldDir * length;
			Handles.DrawLine(from, to, 2f);
			Handles.ConeHandleCap(0, to, Quaternion.LookRotation(worldDir), length * 0.45f, EventType.Repaint);
		}

		/// <summary>ドラッグ中の数値表示(キャップの脇)</summary>
		private static void DrawValueLabel(Vector3 world, float value, Color color)
		{
			if (Event.current.type != EventType.Repaint)
				return;
			if (_valueLabelStyle == null)
				_valueLabelStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
			_valueLabelStyle.normal.textColor = color;

			var offset = HandleUtility.GetHandleSize(world) * 0.22f;
			var view = SceneView.currentDrawingSceneView;
			var right = view != null && view.camera != null ? view.camera.transform.right : Vector3.right;
			Handles.Label(world + right * offset, value.ToString("0.###"), _valueLabelStyle);
		}

		/// <summary>半径ドラッグ中の実効範囲フィル(軸空間ディスク)</summary>
		private static void DrawRadiusFill(Matrix4x4 axisMatrix, Vector3 center, float radius, Color accent,
			Vector3 fillNormal)
		{
			if (Event.current.type != EventType.Repaint)
				return;
			var normalLocal = fillNormal;
			if (normalLocal == Vector3.zero)
			{
				// 平面指定なし(球など)はカメラ正対ディスク = 球のシルエット
				var view = SceneView.currentDrawingSceneView;
				if (view == null || view.camera == null)
					return;
				normalLocal = axisMatrix.inverse.MultiplyVector(view.camera.transform.forward);
			}
			if (normalLocal.sqrMagnitude < 1e-10f)
				return;
			using (new Handles.DrawingScope(new Color(accent.r, accent.g, accent.b, 0.08f), axisMatrix))
			{
				Handles.DrawSolidDisc(center, normalLocal.normalized, radius);
			}
		}

		/// <summary>Box の面フィル(ドラッグ中の面を軸空間で塗る)</summary>
		private static void DrawFaceFill(Bounds b, int axis, int sign, Color accent)
		{
			if (Event.current.type != EventType.Repaint)
				return;
			var u = (axis + 1) % 3;
			var v = (axis + 2) % 3;
			var corners = new Vector3[4];
			for (var i = 0; i < 4; i++)
			{
				var c = b.center;
				c[axis] += sign * b.extents[axis];
				c[u] += (i == 0 || i == 3 ? -1f : 1f) * b.extents[u];
				c[v] += (i < 2 ? -1f : 1f) * b.extents[v];
				corners[i] = c;
			}
			Handles.DrawSolidRectangleWithOutline(corners,
				new Color(accent.r, accent.g, accent.b, 0.10f), Color.clear);
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

		private static Color AccentColor(HandleLineStyle style)
		{
			return style == HandleLineStyle.Dotted ? SecondaryAccent : PrimaryAccent;
		}
	}
}
