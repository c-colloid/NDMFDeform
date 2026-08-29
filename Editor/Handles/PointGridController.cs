using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>奥点の表示方法</summary>
	public enum PointGridOcclusionMode
	{
		/// <summary>遮蔽された点をフェード表示(既定)</summary>
		Fade,
		/// <summary>遮蔽された点を非表示</summary>
		Hide,
		/// <summary>常に全点を表示</summary>
		ShowAll,
	}

	/// <summary>PointGrid の表示・操作設定(SceneView オーバーレイから切替え)</summary>
	public static class PointGridViewState
	{
		public static PointGridOcclusionMode OcclusionMode = PointGridOcclusionMode.Fade;
		public static bool SliceEnabled;
		public static HandleAxis SliceAxis = HandleAxis.Z;
		public static int SliceIndex;
		/// <summary>Ctrl+クリックのループ選択が伸びる軸</summary>
		public static HandleAxis LoopAxis = HandleAxis.X;
	}

	/// <summary>オーバーレイ→コントローラへの一回きりの選択コマンド</summary>
	public enum PointGridCommand
	{
		None,
		SelectAll,
		ClearSelection,
		InvertSelection,
	}

	public static class PointGridCommands
	{
		public static PointGridCommand Pending;
	}

	/// <summary>
	/// 格子制御点編集の対話コア。DeformerBaseEditor が保持し、
	/// 軸空間の Handles.DrawingScope 内で毎フレーム呼ばれる。
	/// 選択はエディタセッション状態(非シリアライズ)、
	/// 制御点の編集は SerializedProperty 経由(Undo は Unity 任せ)。
	///
	/// ミラー規則(フォーク版ベイク時ミラーのバグを踏まえた設計):
	/// - ミラー面は格子中心(対象成分 = 0)に固定。中心 Transform の変換は行わない
	/// - 相手側の点には「主点の新しい位置の鏡像」を書き込む(対称性を強制)
	/// - 相手が自分自身(奇数解像度の中心面上)の場合は制約しない
	/// - 主点と相手の両方が選択中の場合は素のデルタのみ適用(二重適用防止)
	/// </summary>
	internal class PointGridController
	{
		private static readonly Color UnselectedColor = new Color(0.4f, 0.75f, 1f, 1f);
		private static readonly Color SelectedColor = new Color(1f, 0.65f, 0.1f, 1f);
		private static readonly Color OccludedTint = new Color(1f, 1f, 1f, 0.25f);
		private static readonly Color WireColor = new Color(1f, 1f, 1f, 0.25f);

		private readonly HashSet<int> _selection = new HashSet<int>();
		private Vector3Int _lastResolution;

		private bool _marqueeActive;
		private Vector2 _marqueeStart;
		private Vector2 _marqueeEnd;

		/// <summary>true を返したらプロパティが変更された(呼び出し側が Apply する)</summary>
		public bool OnSceneGUI(SerializedObject serializedObject, string pointsProperty,
			Vector3Int resolution, string mirrorAxisProperty)
		{
			var points = serializedObject.FindProperty(pointsProperty);
			var count = resolution.x * resolution.y * resolution.z;
			if (points == null || !points.isArray || points.arraySize != count || count == 0)
				return false;

			if (_lastResolution != resolution)
			{
				_selection.Clear();
				_lastResolution = resolution;
			}

			// マーキーの GUI 座標計算用に軸空間行列を控える
			_cachedMatrix = Handles.matrix;

			var mirror = MirrorAxis.None;
			if (mirrorAxisProperty != null)
			{
				var mp = serializedObject.FindProperty(mirrorAxisProperty);
				if (mp != null) mirror = (MirrorAxis)mp.enumValueIndex;
			}

			ConsumePendingCommand(count);

			var sliceOnly = PointGridViewState.SliceEnabled;
			var sliceAxis = PointGridViewState.SliceAxis;
			var sliceIndex = Mathf.Clamp(PointGridViewState.SliceIndex, 0, AxisRes(resolution, sliceAxis) - 1);

			DrawWireframe(points, resolution, sliceOnly, sliceAxis, sliceIndex);

			var changed = false;
			var evt = Event.current;

			// 点の描画とクリック選択
			for (var i = 0; i < count; i++)
			{
				var coord = PointGridUtility.GetCoord(resolution, i);
				if (sliceOnly && AxisCoord(coord, sliceAxis) != sliceIndex)
					continue;

				var pos = (Vector3)GetPoint(points, i);
				var size = HandleSizeInMatrixSpace(pos) * (_selection.Contains(i) ? 1.25f : 1f);
				var color = _selection.Contains(i) ? SelectedColor : UnselectedColor;

				// 奥側(遮蔽)パス: フェード表示
				if (PointGridViewState.OcclusionMode == PointGridOcclusionMode.Fade && evt.type == EventType.Repaint)
				{
					Handles.zTest = CompareFunction.Greater;
					Handles.color = color * OccludedTint;
					Handles.DotHandleCap(0, pos, Quaternion.identity, size, EventType.Repaint);
				}

				// 手前パス + クリック判定
				Handles.zTest = PointGridViewState.OcclusionMode == PointGridOcclusionMode.ShowAll
					? CompareFunction.Always
					: CompareFunction.LessEqual;
				Handles.color = color;
				if (Handles.Button(pos, Quaternion.identity, size, size * 1.4f, Handles.DotHandleCap))
				{
					ApplyClickSelection(i, coord, resolution, evt.modifiers);
				}
				Handles.zTest = CompareFunction.Always;
			}

			// 矩形(マーキー)選択
			HandleMarquee(points, resolution, sliceOnly, sliceAxis, sliceIndex);

			// 選択点の移動
			if (_selection.Count > 0)
			{
				var pivot = Vector3.zero;
				foreach (var i in _selection)
					pivot += (Vector3)GetPoint(points, i);
				pivot /= _selection.Count;

				EditorGUI.BeginChangeCheck();
				var newPivot = Handles.PositionHandle(pivot, Quaternion.identity);
				if (EditorGUI.EndChangeCheck())
				{
					var delta = (float3)(newPivot - pivot);
					foreach (var i in _selection)
					{
						var newPos = GetPoint(points, i) + delta;
						SetPoint(points, i, newPos);

						if (mirror != MirrorAxis.None)
						{
							var partner = PointGridUtility.MirrorIndex(resolution, i, mirror);
							if (partner != i && !_selection.Contains(partner))
								SetPoint(points, partner, PointGridUtility.MirrorPosition(newPos, mirror));
						}
					}
					changed = true;
				}
			}

			return changed;
		}

		private void ApplyClickSelection(int index, Vector3Int coord, Vector3Int resolution, EventModifiers modifiers)
		{
			var ctrl = (modifiers & EventModifiers.Control) != 0 || (modifiers & EventModifiers.Command) != 0;
			var shift = (modifiers & EventModifiers.Shift) != 0;

			if (ctrl && shift)
			{
				// シート選択: LoopAxis に垂直な面
				var axis = PointGridViewState.LoopAxis;
				ReplaceOrAdd(PointGridUtility.SheetIndices(resolution, axis, AxisCoord(coord, axis)), add: false);
			}
			else if (ctrl)
			{
				// ループ(行)選択: LoopAxis 沿い
				ReplaceOrAdd(PointGridUtility.LineIndices(resolution, coord, PointGridViewState.LoopAxis), add: false);
			}
			else if (shift)
			{
				if (!_selection.Add(index))
					_selection.Remove(index);
			}
			else
			{
				_selection.Clear();
				_selection.Add(index);
			}
			SceneView.RepaintAll();
		}

		private void ReplaceOrAdd(List<int> indices, bool add)
		{
			if (!add) _selection.Clear();
			foreach (var i in indices)
				_selection.Add(i);
		}

		private void HandleMarquee(SerializedProperty points, Vector3Int resolution,
			bool sliceOnly, HandleAxis sliceAxis, int sliceIndex)
		{
			var evt = Event.current;
			var control = GUIUtility.GetControlID(FocusType.Passive);
			HandleUtility.AddDefaultControl(control);

			switch (evt.GetTypeForControl(control))
			{
				case EventType.MouseDown:
					if (HandleUtility.nearestControl == control && evt.button == 0 && !evt.alt)
					{
						_marqueeActive = true;
						_marqueeStart = _marqueeEnd = evt.mousePosition;
						GUIUtility.hotControl = control;
						evt.Use();
					}
					break;

				case EventType.MouseDrag:
					if (GUIUtility.hotControl == control)
					{
						_marqueeEnd = evt.mousePosition;
						evt.Use();
					}
					break;

				case EventType.MouseUp:
					if (GUIUtility.hotControl == control)
					{
						GUIUtility.hotControl = 0;
						var rect = RectFromPoints(_marqueeStart, _marqueeEnd);
						var isClick = rect.width < 4f && rect.height < 4f;
						if (!evt.shift)
							_selection.Clear();
						if (!isClick)
						{
							var count = points.arraySize;
							using (new Handles.DrawingScope(Matrix4x4.identity))
							{
								for (var i = 0; i < count; i++)
								{
									var coord = PointGridUtility.GetCoord(resolution, i);
									if (sliceOnly && AxisCoord(coord, sliceAxis) != sliceIndex)
										continue;
									var world = _cachedMatrix.MultiplyPoint3x4((Vector3)GetPoint(points, i));
									var gui = HandleUtility.WorldToGUIPoint(world);
									if (rect.Contains(gui))
										_selection.Add(i);
								}
							}
						}
						_marqueeActive = false;
						evt.Use();
						SceneView.RepaintAll();
					}
					break;

				case EventType.Repaint:
					if (_marqueeActive && GUIUtility.hotControl == control)
					{
						Handles.BeginGUI();
						var rect = RectFromPoints(_marqueeStart, _marqueeEnd);
						GUI.Box(rect, GUIContent.none, "SelectionRect");
						Handles.EndGUI();
					}
					break;
			}
		}

		private Matrix4x4 _cachedMatrix = Matrix4x4.identity;

		private void ConsumePendingCommand(int count)
		{
			var command = PointGridCommands.Pending;
			if (command == PointGridCommand.None) return;
			PointGridCommands.Pending = PointGridCommand.None;

			switch (command)
			{
				case PointGridCommand.SelectAll:
					for (var i = 0; i < count; i++) _selection.Add(i);
					break;
				case PointGridCommand.ClearSelection:
					_selection.Clear();
					break;
				case PointGridCommand.InvertSelection:
					var inverted = new HashSet<int>();
					for (var i = 0; i < count; i++)
						if (!_selection.Contains(i))
							inverted.Add(i);
					_selection.Clear();
					_selection.UnionWith(inverted);
					break;
			}
			SceneView.RepaintAll();
		}

		private void DrawWireframe(SerializedProperty points, Vector3Int res,
			bool sliceOnly, HandleAxis sliceAxis, int sliceIndex)
		{
			if (Event.current.type != EventType.Repaint) return;

			Handles.zTest = CompareFunction.Always;
			Handles.color = WireColor;
			for (var z = 0; z < res.z; z++)
			for (var y = 0; y < res.y; y++)
			for (var x = 0; x < res.x; x++)
			{
				var c = new Vector3Int(x, y, z);
				if (sliceOnly && AxisCoord(c, sliceAxis) != sliceIndex)
					continue;

				var from = (Vector3)GetPoint(points, PointGridUtility.GetIndex(res, x, y, z));
				if (x + 1 < res.x && (!sliceOnly || sliceAxis != HandleAxis.X))
					Handles.DrawLine(from, (Vector3)GetPoint(points, PointGridUtility.GetIndex(res, x + 1, y, z)));
				if (y + 1 < res.y && (!sliceOnly || sliceAxis != HandleAxis.Y))
					Handles.DrawLine(from, (Vector3)GetPoint(points, PointGridUtility.GetIndex(res, x, y + 1, z)));
				if (z + 1 < res.z && (!sliceOnly || sliceAxis != HandleAxis.Z))
					Handles.DrawLine(from, (Vector3)GetPoint(points, PointGridUtility.GetIndex(res, x, y, z + 1)));
			}
		}

		private static float HandleSizeInMatrixSpace(Vector3 positionInMatrixSpace)
		{
			var m = Handles.matrix;
			var world = m.MultiplyPoint3x4(positionInMatrixSpace);
			var worldSize = HandleUtility.GetHandleSize(world) * 0.06f;
			var scale = (((Vector3)m.GetColumn(0)).magnitude
			             + ((Vector3)m.GetColumn(1)).magnitude
			             + ((Vector3)m.GetColumn(2)).magnitude) / 3f;
			return worldSize / Mathf.Max(scale, 1e-6f);
		}

		private static Rect RectFromPoints(Vector2 a, Vector2 b)
		{
			return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
				Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
		}

		private static int AxisRes(Vector3Int res, HandleAxis axis)
		{
			switch (axis)
			{
				case HandleAxis.X: return res.x;
				case HandleAxis.Y: return res.y;
				default: return res.z;
			}
		}

		private static int AxisCoord(Vector3Int coord, HandleAxis axis)
		{
			switch (axis)
			{
				case HandleAxis.X: return coord.x;
				case HandleAxis.Y: return coord.y;
				default: return coord.z;
			}
		}

		internal static float3 GetPoint(SerializedProperty points, int index)
		{
			var p = points.GetArrayElementAtIndex(index);
			return new float3(
				p.FindPropertyRelative("x").floatValue,
				p.FindPropertyRelative("y").floatValue,
				p.FindPropertyRelative("z").floatValue);
		}

		internal static void SetPoint(SerializedProperty points, int index, float3 value)
		{
			var p = points.GetArrayElementAtIndex(index);
			p.FindPropertyRelative("x").floatValue = value.x;
			p.FindPropertyRelative("y").floatValue = value.y;
			p.FindPropertyRelative("z").floatValue = value.z;
		}
	}
}
