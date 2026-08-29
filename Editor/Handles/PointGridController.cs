using System.Collections.Generic;
using System.Reflection;
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
	///
	/// パフォーマンス設計: SerializedProperty の読み取りは要素毎に遅く
	/// アロケーションも発生するため、読み取りはリフレクションで実フィールドの
	/// float3[] を直接参照し、イベント毎に 1 回だけ位置キャッシュを構築する
	/// (ドラッグ中の未適用編集がありうる選択点と鏡像相手のみプロパティから上書き)。
	/// 書き込み(Undo 対象)のみ SerializedProperty 経由で行う。
	/// ワイヤーフレームは Handles.DrawLines による一括描画。
	///
	/// 座標系: 位置変換のみ軸行列を通し、キャップの描画・操作はワールド空間。
	/// ミラー規則: 面は格子中心固定 / 中心面上の点は制約しない / 両側選択時は素のデルタのみ。
	/// </summary>
	internal class PointGridController
	{
		private static readonly Color UnselectedColor = new Color(0.4f, 0.75f, 1f, 1f);
		private static readonly Color SelectedColor = new Color(1f, 0.65f, 0.1f, 1f);
		private static readonly Color OccludedTint = new Color(1f, 1f, 1f, 0.25f);
		private static readonly Color WireColor = new Color(1f, 1f, 1f, 0.25f);

		private static readonly Dictionary<(System.Type, string), FieldInfo> FieldCache =
			new Dictionary<(System.Type, string), FieldInfo>();

		private readonly HashSet<int> _selection = new HashSet<int>();
		private Vector3Int _lastResolution;

		private bool _marqueeActive;
		private Vector2 _marqueeStart;
		private Vector2 _marqueeEnd;

		private Matrix4x4 _axisMatrix = Matrix4x4.identity;
		private Matrix4x4 _axisInverse = Matrix4x4.identity;

		// イベント毎に構築する位置キャッシュ
		private float3[] _localCache = System.Array.Empty<float3>();
		private Vector3[] _worldCache = System.Array.Empty<Vector3>();

		// ワイヤーフレームの線分バッファ(構成が変わった時のみ組み直す)
		private Vector3[] _wireSegments = System.Array.Empty<Vector3>();
		private int[] _wireIndexPairs = System.Array.Empty<int>();
		private (Vector3Int res, bool slice, HandleAxis axis, int index) _wireConfig;

		/// <summary>true を返したらプロパティが変更された(呼び出し側が Apply する)</summary>
		public bool OnSceneGUI(SerializedObject serializedObject, string pointsProperty,
			Vector3Int resolution, string mirrorAxisProperty)
		{
			var points = serializedObject.FindProperty(pointsProperty);
			var count = resolution.x * resolution.y * resolution.z;
			if (points == null || !points.isArray || points.arraySize != count || count == 0)
				return false;

			_axisMatrix = Handles.matrix;
			if (Mathf.Abs(_axisMatrix.determinant) < 1e-12f)
				return false;
			_axisInverse = _axisMatrix.inverse;

			if (_lastResolution != resolution)
			{
				_selection.Clear();
				_lastResolution = resolution;
			}

			var mirror = MirrorAxis.None;
			if (mirrorAxisProperty != null)
			{
				var mp = serializedObject.FindProperty(mirrorAxisProperty);
				if (mp != null) mirror = (MirrorAxis)mp.enumValueIndex;
			}

			ConsumePendingCommand(count);
			RefreshCaches(serializedObject, points, count, resolution, mirror);

			var sliceOnly = PointGridViewState.SliceEnabled;
			var sliceAxis = PointGridViewState.SliceAxis;
			var sliceIndex = Mathf.Clamp(PointGridViewState.SliceIndex, 0, AxisRes(resolution, sliceAxis) - 1);

			var changed = false;
			using (new Handles.DrawingScope(Matrix4x4.identity))
			{
				DrawWireframe(resolution, sliceOnly, sliceAxis, sliceIndex);
				DrawPointsAndPick(resolution, sliceOnly, sliceAxis, sliceIndex);
				HandleMarquee(resolution, sliceOnly, sliceAxis, sliceIndex);
				changed = MoveSelection(points, resolution, mirror);
			}

			return changed;
		}

		/// <summary>
		/// 位置キャッシュを構築する。読み取りは実フィールドの float3[] 直接参照
		/// (未適用の編集がありうる選択点・鏡像相手のみ SerializedProperty から上書き)。
		/// </summary>
		private void RefreshCaches(SerializedObject serializedObject, SerializedProperty points,
			int count, Vector3Int resolution, MirrorAxis mirror)
		{
			if (_localCache.Length != count)
			{
				_localCache = new float3[count];
				_worldCache = new Vector3[count];
			}

			var fast = TryGetFieldArray(serializedObject.targetObject, points.name);
			if (fast != null && fast.Length == count)
			{
				System.Array.Copy(fast, _localCache, count);
			}
			else
			{
				for (var i = 0; i < count; i++)
					_localCache[i] = GetPoint(points, i);
			}

			foreach (var i in _selection)
			{
				_localCache[i] = GetPoint(points, i);
				if (mirror != MirrorAxis.None)
				{
					var partner = PointGridUtility.MirrorIndex(resolution, i, mirror);
					if (partner != i)
						_localCache[partner] = GetPoint(points, partner);
				}
			}

			for (var i = 0; i < count; i++)
				_worldCache[i] = _axisMatrix.MultiplyPoint3x4((Vector3)_localCache[i]);
		}

		private static float3[] TryGetFieldArray(Object target, string fieldName)
		{
			if (target == null) return null;
			var key = (target.GetType(), fieldName);
			if (!FieldCache.TryGetValue(key, out var field))
			{
				for (var type = target.GetType(); type != null && field == null; type = type.BaseType)
				{
					field = type.GetField(fieldName,
						BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
						BindingFlags.DeclaredOnly);
				}
				FieldCache[key] = field;
			}
			return field?.GetValue(target) as float3[];
		}

		private void DrawPointsAndPick(Vector3Int resolution,
			bool sliceOnly, HandleAxis sliceAxis, int sliceIndex)
		{
			var evt = Event.current;
			var isRepaint = evt.type == EventType.Repaint;
			var fade = PointGridViewState.OcclusionMode == PointGridOcclusionMode.Fade;
			var frontZTest = PointGridViewState.OcclusionMode == PointGridOcclusionMode.ShowAll
				? CompareFunction.Always
				: CompareFunction.LessEqual;

			for (var i = 0; i < _worldCache.Length; i++)
			{
				if (sliceOnly && AxisCoord(PointGridUtility.GetCoord(resolution, i), sliceAxis) != sliceIndex)
					continue;

				var world = _worldCache[i];
				var selected = _selection.Contains(i);
				var size = HandleUtility.GetHandleSize(world) * 0.06f * (selected ? 1.25f : 1f);
				var color = selected ? SelectedColor : UnselectedColor;

				// 奥側(遮蔽)パス: フェード表示
				if (fade && isRepaint)
				{
					Handles.zTest = CompareFunction.Greater;
					Handles.color = color * OccludedTint;
					Handles.DotHandleCap(0, world, Quaternion.identity, size, EventType.Repaint);
				}

				// 手前パス + クリック判定
				Handles.zTest = frontZTest;
				Handles.color = color;
				if (Handles.Button(world, Quaternion.identity, size, size * 1.4f, Handles.DotHandleCap))
				{
					ApplyClickSelection(i, PointGridUtility.GetCoord(resolution, i), resolution, evt.modifiers);
				}
			}
			Handles.zTest = CompareFunction.Always;
		}

		private bool MoveSelection(SerializedProperty points, Vector3Int resolution, MirrorAxis mirror)
		{
			if (_selection.Count == 0)
				return false;

			var pivot = Vector3.zero;
			foreach (var i in _selection)
				pivot += _worldCache[i];
			pivot /= _selection.Count;

			EditorGUI.BeginChangeCheck();
			var newPivot = Handles.PositionHandle(pivot, Quaternion.identity);
			if (!EditorGUI.EndChangeCheck())
				return false;

			// ワールドのデルタを軸空間へ変換して各点に適用する
			var localDelta = (float3)_axisInverse.MultiplyVector(newPivot - pivot);
			foreach (var i in _selection)
			{
				var newPos = _localCache[i] + localDelta;
				SetPoint(points, i, newPos);
				_localCache[i] = newPos;
				_worldCache[i] = _axisMatrix.MultiplyPoint3x4((Vector3)newPos);

				if (mirror != MirrorAxis.None)
				{
					var partner = PointGridUtility.MirrorIndex(resolution, i, mirror);
					if (partner != i && !_selection.Contains(partner))
					{
						var mirrored = PointGridUtility.MirrorPosition(newPos, mirror);
						SetPoint(points, partner, mirrored);
						_localCache[partner] = mirrored;
						_worldCache[partner] = _axisMatrix.MultiplyPoint3x4((Vector3)mirrored);
					}
				}
			}
			return true;
		}

		private void ApplyClickSelection(int index, Vector3Int coord, Vector3Int resolution, EventModifiers modifiers)
		{
			var ctrl = (modifiers & EventModifiers.Control) != 0 || (modifiers & EventModifiers.Command) != 0;
			var shift = (modifiers & EventModifiers.Shift) != 0;

			if (ctrl && shift)
			{
				// シート選択: LoopAxis に垂直な面
				var axis = PointGridViewState.LoopAxis;
				ReplaceSelection(PointGridUtility.SheetIndices(resolution, axis, AxisCoord(coord, axis)));
			}
			else if (ctrl)
			{
				// ループ(行)選択: LoopAxis 沿い
				ReplaceSelection(PointGridUtility.LineIndices(resolution, coord, PointGridViewState.LoopAxis));
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

		private void ReplaceSelection(List<int> indices)
		{
			_selection.Clear();
			foreach (var i in indices)
				_selection.Add(i);
		}

		private void HandleMarquee(Vector3Int resolution,
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
							for (var i = 0; i < _worldCache.Length; i++)
							{
								if (sliceOnly &&
								    AxisCoord(PointGridUtility.GetCoord(resolution, i), sliceAxis) != sliceIndex)
									continue;
								var gui = HandleUtility.WorldToGUIPoint(_worldCache[i]);
								if (rect.Contains(gui))
									_selection.Add(i);
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
						GUI.Box(RectFromPoints(_marqueeStart, _marqueeEnd), GUIContent.none, "SelectionRect");
						Handles.EndGUI();
					}
					break;
			}
		}

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

		private void DrawWireframe(Vector3Int res, bool sliceOnly, HandleAxis sliceAxis, int sliceIndex)
		{
			if (Event.current.type != EventType.Repaint) return;

			var config = (res, sliceOnly, sliceAxis, sliceIndex);
			if (_wireConfig != config || _wireIndexPairs.Length == 0)
			{
				RebuildWireIndexPairs(res, sliceOnly, sliceAxis, sliceIndex);
				_wireConfig = config;
			}

			for (var s = 0; s < _wireIndexPairs.Length; s++)
				_wireSegments[s] = _worldCache[_wireIndexPairs[s]];

			Handles.zTest = CompareFunction.Always;
			Handles.color = WireColor;
			Handles.DrawLines(_wireSegments);
		}

		private void RebuildWireIndexPairs(Vector3Int res, bool sliceOnly, HandleAxis sliceAxis, int sliceIndex)
		{
			var pairs = new List<int>();
			for (var z = 0; z < res.z; z++)
			for (var y = 0; y < res.y; y++)
			for (var x = 0; x < res.x; x++)
			{
				var c = new Vector3Int(x, y, z);
				if (sliceOnly && AxisCoord(c, sliceAxis) != sliceIndex)
					continue;

				var from = PointGridUtility.GetIndex(res, x, y, z);
				if (x + 1 < res.x && (!sliceOnly || sliceAxis != HandleAxis.X))
				{
					pairs.Add(from);
					pairs.Add(PointGridUtility.GetIndex(res, x + 1, y, z));
				}
				if (y + 1 < res.y && (!sliceOnly || sliceAxis != HandleAxis.Y))
				{
					pairs.Add(from);
					pairs.Add(PointGridUtility.GetIndex(res, x, y + 1, z));
				}
				if (z + 1 < res.z && (!sliceOnly || sliceAxis != HandleAxis.Z))
				{
					pairs.Add(from);
					pairs.Add(PointGridUtility.GetIndex(res, x, y, z + 1));
				}
			}
			_wireIndexPairs = pairs.ToArray();
			_wireSegments = new Vector3[_wireIndexPairs.Length];
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
