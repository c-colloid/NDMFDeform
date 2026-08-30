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
		/// <summary>表示するスライス番号の集合(複数選択可)</summary>
		public static readonly HashSet<int> SliceIndices = new HashSet<int> { 0 };
		/// <summary>SliceIndices の変更カウンタ(ワイヤーフレーム再構築の検知用)</summary>
		public static int SliceVersion;

		/// <summary>スライス表示設定のもとで、この格子座標の点を表示するか</summary>
		public static bool IsSliceVisible(Vector3Int coord)
		{
			if (!SliceEnabled) return true;
			int c;
			switch (SliceAxis)
			{
				case HandleAxis.X: c = coord.x; break;
				case HandleAxis.Y: c = coord.y; break;
				default: c = coord.z; break;
			}
			return SliceIndices.Contains(c);
		}
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

		// イベント毎に構築する位置キャッシュ。
		// ドラッグ中はキャッシュを直接編集し、フィールドへの反映(Undo 付きコミット)は
		// 約 20Hz + ドラッグ終了時にまとめる(大量選択時の書き込みコスト削減)
		private float3[] _localCache = System.Array.Empty<float3>();
		private Vector3[] _worldCache = System.Array.Empty<Vector3>();
		private readonly HashSet<int> _dirtyIndices = new HashSet<int>();
		private bool _cacheDirty;
		private double _lastCommitTime;
		private const double CommitInterval = 0.05;

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

			using (new Handles.DrawingScope(Matrix4x4.identity))
			{
				DrawWireframe(resolution);
				HandleEscape();
				HandleSelectAllCommand(count);
				HandleAxisGesture(resolution);
				DrawPointsAndPick(resolution);
				HandleMarquee(resolution);
				TransformSelection(resolution, mirror);
			}

			return CommitIfNeeded(serializedObject, points);
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
				_dirtyIndices.Clear();
				_cacheDirty = false;
			}

			// 未コミットの編集があるあいだはキャッシュが正であり、フィールドから読み戻さない
			if (!_cacheDirty)
			{
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

		private void DrawPointsAndPick(Vector3Int resolution)
		{
			var evt = Event.current;
			var isRepaint = evt.type == EventType.Repaint;
			var fade = PointGridViewState.OcclusionMode == PointGridOcclusionMode.Fade;
			var frontZTest = PointGridViewState.OcclusionMode == PointGridOcclusionMode.ShowAll
				? CompareFunction.Always
				: CompareFunction.LessEqual;

			for (var i = 0; i < _worldCache.Length; i++)
			{
				if (!PointGridViewState.IsSliceVisible(PointGridUtility.GetCoord(resolution, i)))
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
					ApplyClickSelection(i, PointGridUtility.GetCoord(resolution, i), resolution,
						evt.modifiers, evt.mousePosition, evt.clickCount);
				}
			}
			Handles.zTest = CompareFunction.Always;
		}

		// ==== 選択点の変形(Unity のツール W/E/R に追従) ====
		// ドラッグ開始時に選択点の位置とピボットをスナップショットし、
		// 毎フレーム「開始時からの合計変形」を開始位置に適用する
		// (増分適用の誤差蓄積と、コミット間引きとの競合を避ける)。
		// 点選択中は標準の Transform ギズモを隠し、W/E/R を点操作に割り当てる。

		private bool _transformActive;
		private Vector3 _transformPivot;
		private Quaternion _transformStartRotation = Quaternion.identity;
		private Vector3 _gizmoPosition;
		private Quaternion _gizmoRotation = Quaternion.identity;
		private Vector3 _gizmoScale = Vector3.one;
		private readonly Dictionary<int, float3> _transformStartLocal = new Dictionary<int, float3>();

		private static readonly HashSet<PointGridController> ToolHiders = new HashSet<PointGridController>();
		private static bool _toolsHiddenByUs;

		private void TransformSelection(Vector3Int resolution, MirrorAxis mirror)
		{
			UpdateToolsHidden();

			if (_selection.Count == 0)
				return;

			if (_transformActive && GUIUtility.hotControl == 0)
				EndTransform();

			var tool = Tools.current;
			if (tool == Tool.View)
				return;

			Vector3 pivot;
			if (_transformActive)
			{
				pivot = _transformPivot;
			}
			else
			{
				pivot = Vector3.zero;
				foreach (var i in _selection)
					pivot += _worldCache[i];
				pivot /= _selection.Count;
			}

			if (tool == Tool.Rotate)
				RotateSelection(pivot, resolution, mirror);
			else if (tool == Tool.Scale)
				ScaleSelection(pivot, resolution, mirror);
			else
				MoveSelection(pivot, resolution, mirror);
		}

		private void MoveSelection(Vector3 pivot, Vector3Int resolution, MirrorAxis mirror)
		{
			if (!_transformActive)
				_gizmoPosition = pivot;

			EditorGUI.BeginChangeCheck();
			var newPosition = Handles.PositionHandle(_gizmoPosition, HandleOrientation());
			if (!EditorGUI.EndChangeCheck())
				return;

			if (!_transformActive)
				BeginTransform(pivot, HandleOrientation());
			_gizmoPosition = newPosition;

			var delta = newPosition - _transformPivot;
			ApplyToSelection(resolution, mirror, startWorld => startWorld + delta);
		}

		private void RotateSelection(Vector3 pivot, Vector3Int resolution, MirrorAxis mirror)
		{
			if (!_transformActive)
				_gizmoRotation = HandleOrientation();

			EditorGUI.BeginChangeCheck();
			var newRotation = Handles.RotationHandle(_gizmoRotation, pivot);
			if (!EditorGUI.EndChangeCheck())
				return;

			if (!_transformActive)
				BeginTransform(pivot, _gizmoRotation);
			_gizmoRotation = newRotation;

			var delta = newRotation * Quaternion.Inverse(_transformStartRotation);
			ApplyToSelection(resolution, mirror, startWorld => pivot + delta * (startWorld - pivot));
		}

		private void ScaleSelection(Vector3 pivot, Vector3Int resolution, MirrorAxis mirror)
		{
			var orientation = _transformActive ? _transformStartRotation : HandleOrientation();
			if (!_transformActive)
				_gizmoScale = Vector3.one;

			EditorGUI.BeginChangeCheck();
			var newScale = Handles.ScaleHandle(_gizmoScale, pivot, orientation,
				HandleUtility.GetHandleSize(pivot));
			if (!EditorGUI.EndChangeCheck())
				return;

			if (!_transformActive)
				BeginTransform(pivot, orientation);
			_gizmoScale = newScale;

			var inverse = Quaternion.Inverse(_transformStartRotation);
			ApplyToSelection(resolution, mirror, startWorld =>
				pivot + _transformStartRotation * Vector3.Scale(inverse * (startWorld - pivot), newScale));
		}

		/// <summary>Global/Local(Tools.pivotRotation)に応じたハンドルの姿勢</summary>
		private Quaternion HandleOrientation()
		{
			return Tools.pivotRotation == PivotRotation.Local ? _axisMatrix.rotation : Quaternion.identity;
		}

		private void BeginTransform(Vector3 pivot, Quaternion orientation)
		{
			_transformActive = true;
			_transformPivot = pivot;
			_transformStartRotation = orientation;
			_transformStartLocal.Clear();
			foreach (var i in _selection)
				_transformStartLocal[i] = _localCache[i];
		}

		private void EndTransform()
		{
			_transformActive = false;
			_transformStartLocal.Clear();
			_gizmoRotation = Quaternion.identity;
			_gizmoScale = Vector3.one;
		}

		/// <summary>
		/// ドラッグ開始時の各選択点(ワールド)に transformWorld を適用し、
		/// キャッシュにのみ反映する(フィールドへの反映は CommitIfNeeded がまとめて行う)。
		/// </summary>
		private void ApplyToSelection(Vector3Int resolution, MirrorAxis mirror,
			System.Func<Vector3, Vector3> transformWorld)
		{
			foreach (var i in _selection)
			{
				if (!_transformStartLocal.TryGetValue(i, out var startLocal))
					continue;

				var startWorld = _axisMatrix.MultiplyPoint3x4((Vector3)startLocal);
				var newPos = (float3)_axisInverse.MultiplyPoint3x4(transformWorld(startWorld));
				_localCache[i] = newPos;
				_worldCache[i] = _axisMatrix.MultiplyPoint3x4((Vector3)newPos);
				_dirtyIndices.Add(i);

				if (mirror != MirrorAxis.None)
				{
					var partner = PointGridUtility.MirrorIndex(resolution, i, mirror);
					if (partner != i && !_selection.Contains(partner))
					{
						var mirrored = PointGridUtility.MirrorPosition(newPos, mirror);
						_localCache[partner] = mirrored;
						_worldCache[partner] = _axisMatrix.MultiplyPoint3x4((Vector3)mirrored);
						_dirtyIndices.Add(partner);
					}
				}
			}
			_cacheDirty = true;
		}

		/// <summary>Ctrl+A(Select All コマンド)で全制御点を選択する</summary>
		private void HandleSelectAllCommand(int count)
		{
			var evt = Event.current;
			if (evt.commandName != "SelectAll")
				return;

			if (evt.type == EventType.ValidateCommand)
			{
				evt.Use();
			}
			else if (evt.type == EventType.ExecuteCommand)
			{
				for (var i = 0; i < count; i++)
					_selection.Add(i);
				evt.Use();
				SceneView.RepaintAll();
			}
		}

		/// <summary>Esc で選択解除(標準ギズモが戻る)</summary>
		private void HandleEscape()
		{
			var evt = Event.current;
			if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape &&
			    _selection.Count > 0 && GUIUtility.hotControl == 0)
			{
				_selection.Clear();
				evt.Use();
				SceneView.RepaintAll();
			}
		}

		/// <summary>
		/// 点選択中は標準の Transform ギズモを隠す(W/E/R を点操作に割り当てるため)。
		/// Tools.hidden はグローバルなので、自分たちが隠した場合のみ状態遷移で戻す。
		/// </summary>
		private void UpdateToolsHidden()
		{
			if (_selection.Count > 0)
				ToolHiders.Add(this);
			else
				ToolHiders.Remove(this);
			SyncToolsHidden();
		}

		/// <summary>エディタ破棄時に呼ぶ。標準ギズモの非表示を解除する</summary>
		public void ReleaseToolsHidden()
		{
			ToolHiders.Remove(this);
			SyncToolsHidden();
		}

		private static void SyncToolsHidden()
		{
			var want = ToolHiders.Count > 0;
			if (want == _toolsHiddenByUs)
				return;
			_toolsHiddenByUs = want;
			Tools.hidden = want;
		}

		/// <summary>
		/// キャッシュ上の編集をフィールドへ反映する(Undo 付き)。
		/// ドラッグ中は CommitInterval に間引き、ドラッグ終了時は即時。
		/// SerializedProperty 経由の適用が必要な場合のみ true を返す。
		/// </summary>
		private bool CommitIfNeeded(SerializedObject serializedObject, SerializedProperty points)
		{
			if (!_cacheDirty)
				return false;

			var dragging = GUIUtility.hotControl != 0;
			var now = EditorApplication.timeSinceStartup;
			if (dragging && now - _lastCommitTime < CommitInterval)
				return false;

			_lastCommitTime = now;
			_cacheDirty = false;

			var target = serializedObject.targetObject;
			var fast = TryGetFieldArray(target, points.name);
			if (fast != null && fast.Length == _localCache.Length)
			{
				Undo.RecordObject(target, "Edit Lattice Points");
				foreach (var i in _dirtyIndices)
					fast[i] = _localCache[i];
				PrefabUtility.RecordPrefabInstancePropertyModifications(target);
				EditorUtility.SetDirty(target);
				_dirtyIndices.Clear();
				return false;
			}

			// フォールバック: SerializedProperty 経由(呼び出し側が Apply する)
			foreach (var i in _dirtyIndices)
				SetPoint(points, i, _localCache[i]);
			_dirtyIndices.Clear();
			return true;
		}

		// Ctrl 系クリックの循環状態(同じ点への連続クリックで方向・範囲を切替える)
		private int _lastClickIndex = -1;
		private bool _lastClickWasExpand;
		private int _directionStep;
		private int _expandStep;
		private HandleAxis _lastNormalAxis = HandleAxis.Z;

		private void ApplyClickSelection(int index, Vector3Int coord, Vector3Int resolution,
			EventModifiers modifiers, Vector2 mousePosition, int clickCount)
		{
			var ctrl = (modifiers & EventModifiers.Control) != 0 || (modifiers & EventModifiers.Command) != 0;
			var shift = (modifiers & EventModifiers.Shift) != 0;

			if (ctrl && shift)
			{
				if (clickCount >= 2 && index == _lastClickIndex && _lastClickWasExpand)
				{
					// ダブルクリック: 直近のリングをシート全面に拡張
					ReplaceSelection(PointGridUtility.SheetIndices(
						resolution, _lastNormalAxis, AxisCoord(coord, _lastNormalAxis)));
				}
				else
				{
					// リング(ループ)選択。同じ点への連続クリックで法線軸を循環させる
					if (index == _lastClickIndex && _lastClickWasExpand)
						_expandStep++;
					else
						_expandStep = 0;

					var directions = DirectionsByProximity(index, coord, resolution, mousePosition);
					var normal = directions[_expandStep % directions.Count];
					_lastNormalAxis = normal;
					ReplaceSelection(PointGridUtility.RingIndices(resolution, normal, coord));
				}

				_lastClickIndex = index;
				_lastClickWasExpand = true;
			}
			else if (ctrl)
			{
				// 行選択。同じ点への連続クリックで方向を近い順に循環させる
				if (index == _lastClickIndex && !_lastClickWasExpand)
					_directionStep++;
				else
					_directionStep = 0;

				var directions = DirectionsByProximity(index, coord, resolution, mousePosition);
				var axis = directions[_directionStep % directions.Count];
				ReplaceSelection(PointGridUtility.LineIndices(resolution, coord, axis));

				_lastClickIndex = index;
				_lastClickWasExpand = false;
			}
			else if (shift)
			{
				if (!_selection.Add(index))
					_selection.Remove(index);
				_lastClickIndex = -1;
			}
			else
			{
				_selection.Clear();
				_selection.Add(index);
				_lastClickIndex = -1;
			}
			SceneView.RepaintAll();
		}

		// ==== 軸スワイプジェスチャ(Ctrl+ドラッグ) ====
		// 点の上で Ctrl(+Shift) を押しながらドラッグすると、点から X/Y/Z の
		// 軸ガイドが表示され、スワイプ方向に最も近い軸で行(またはリング)を選択する。
		// デッドゾーン内で離した場合は従来のクリック選択(近接辺推定+循環)になる。

		private bool _gestureActive;
		private int _gestureIndex = -1;
		private Vector2 _gestureStart;
		private Vector2 _gestureCurrent;
		private bool _gestureShift;
		private int _gestureClickCount;

		private const float GestureDeadzone = 8f;
		private const float GesturePickRadius = 14f;

		private static readonly Color[] AxisColors =
		{
			new Color(1f, 0.35f, 0.35f),  // X
			new Color(0.55f, 1f, 0.4f),   // Y
			new Color(0.35f, 0.6f, 1f),   // Z
		};

		private void HandleAxisGesture(Vector3Int resolution)
		{
			var evt = Event.current;
			var control = GUIUtility.GetControlID(FocusType.Passive);

			switch (evt.GetTypeForControl(control))
			{
				case EventType.MouseDown:
				{
					var ctrl = evt.control || evt.command;
					if (!ctrl || evt.button != 0 || evt.alt) break;
					var index = PickPointAt(evt.mousePosition, resolution);
					if (index < 0) break;

					_gestureActive = true;
					_gestureIndex = index;
					_gestureStart = _gestureCurrent = evt.mousePosition;
					_gestureShift = evt.shift;
					_gestureClickCount = evt.clickCount;
					GUIUtility.hotControl = control;
					evt.Use();
					break;
				}

				case EventType.MouseDrag:
					if (GUIUtility.hotControl == control)
					{
						_gestureCurrent = evt.mousePosition;
						evt.Use();
					}
					break;

				case EventType.MouseUp:
					if (GUIUtility.hotControl == control)
					{
						GUIUtility.hotControl = 0;
						var coord = PointGridUtility.GetCoord(resolution, _gestureIndex);
						var delta = _gestureCurrent - _gestureStart;

						if (delta.magnitude < GestureDeadzone)
						{
							var modifiers = EventModifiers.Control |
								(_gestureShift ? EventModifiers.Shift : EventModifiers.None);
							ApplyClickSelection(_gestureIndex, coord, resolution,
								modifiers, _gestureStart, _gestureClickCount);
						}
						else
						{
							var axis = BestAxisByScreenDirection(_gestureIndex, delta);
							if (axis.HasValue)
							{
								if (_gestureShift)
								{
									_lastNormalAxis = axis.Value;
									ReplaceSelection(PointGridUtility.RingIndices(resolution, axis.Value, coord));
								}
								else
								{
									ReplaceSelection(PointGridUtility.LineIndices(resolution, coord, axis.Value));
								}
								_lastClickIndex = _gestureIndex;
								_lastClickWasExpand = _gestureShift;
							}
						}

						_gestureActive = false;
						_gestureIndex = -1;
						evt.Use();
						SceneView.RepaintAll();
					}
					break;

				case EventType.Repaint:
					if (_gestureActive && GUIUtility.hotControl == control &&
					    (_gestureCurrent - _gestureStart).magnitude >= GestureDeadzone)
					{
						DrawAxisGuides();
					}
					break;
			}
		}

		/// <summary>GUI 座標に最も近い可視の制御点を返す(半径外なら -1)</summary>
		private int PickPointAt(Vector2 guiPosition, Vector3Int resolution)
		{
			var best = -1;
			var bestDistance = GesturePickRadius * GesturePickRadius;
			for (var i = 0; i < _worldCache.Length; i++)
			{
				if (!PointGridViewState.IsSliceVisible(PointGridUtility.GetCoord(resolution, i)))
					continue;
				var d = (HandleUtility.WorldToGUIPoint(_worldCache[i]) - guiPosition).sqrMagnitude;
				if (d < bestDistance)
				{
					bestDistance = d;
					best = i;
				}
			}
			return best;
		}

		/// <summary>スワイプ方向(GUI 座標)に画面上で最も平行な軸を返す</summary>
		private HandleAxis? BestAxisByScreenDirection(int index, Vector2 delta)
		{
			var world = _worldCache[index];
			var handleSize = HandleUtility.GetHandleSize(world);
			var origin = HandleUtility.WorldToGUIPoint(world);
			var direction = delta.normalized;

			HandleAxis? best = null;
			var bestScore = 0f;
			foreach (var axis in new[] { HandleAxis.X, HandleAxis.Y, HandleAxis.Z })
			{
				var screenDir = AxisScreenDirection(axis, world, handleSize, origin);
				// 画面上でほぼ潰れている軸(奥行き方向)はスワイプでは選ばせない
				if (screenDir.magnitude < 4f)
					continue;
				var score = Mathf.Abs(Vector2.Dot(screenDir.normalized, direction));
				if (score > bestScore)
				{
					bestScore = score;
					best = axis;
				}
			}
			return best;
		}

		private Vector2 AxisScreenDirection(HandleAxis axis, Vector3 world, float handleSize, Vector2 origin)
		{
			Vector3 unit;
			switch (axis)
			{
				case HandleAxis.X: unit = Vector3.right; break;
				case HandleAxis.Y: unit = Vector3.up; break;
				default: unit = Vector3.forward; break;
			}
			var worldDir = _axisMatrix.MultiplyVector(unit).normalized;
			return HandleUtility.WorldToGUIPoint(world + worldDir * handleSize) - origin;
		}

		/// <summary>ドラッグ中の軸ガイド(X=赤 / Y=緑 / Z=青、選択中の軸を強調)を描く</summary>
		private void DrawAxisGuides()
		{
			var world = _worldCache[_gestureIndex];
			var handleSize = HandleUtility.GetHandleSize(world);
			var delta = _gestureCurrent - _gestureStart;
			var active = BestAxisByScreenDirection(_gestureIndex, delta);

			Handles.zTest = CompareFunction.Always;
			foreach (var axis in new[] { HandleAxis.X, HandleAxis.Y, HandleAxis.Z })
			{
				Vector3 unit;
				switch (axis)
				{
					case HandleAxis.X: unit = Vector3.right; break;
					case HandleAxis.Y: unit = Vector3.up; break;
					default: unit = Vector3.forward; break;
				}
				var worldDir = _axisMatrix.MultiplyVector(unit).normalized;
				var isActive = active.HasValue && active.Value == axis;
				var color = AxisColors[(int)axis];
				Handles.color = isActive ? color : color * new Color(1f, 1f, 1f, 0.35f);
				var length = handleSize * 2.2f;
				var thickness = isActive ? 4f : 1.5f;
				Handles.DrawLine(world - worldDir * length, world + worldDir * length, thickness);
			}
		}

		/// <summary>
		/// クリック位置(GUI 座標)から、この点につながる辺の方向を近い順に返す。
		/// 各方向の隣接点との辺の中点を画面座標に投影して距離を比較する。
		/// </summary>
		private List<HandleAxis> DirectionsByProximity(int index, Vector3Int coord, Vector3Int resolution,
			Vector2 mousePosition)
		{
			var candidates = new List<(HandleAxis axis, float distance)>(3);
			foreach (var axis in new[] { HandleAxis.X, HandleAxis.Y, HandleAxis.Z })
			{
				var neighbor = coord;
				var max = AxisRes(resolution, axis) - 1;
				if (max < 1)
					continue;
				switch (axis)
				{
					case HandleAxis.X: neighbor.x += coord.x < max ? 1 : -1; break;
					case HandleAxis.Y: neighbor.y += coord.y < max ? 1 : -1; break;
					default: neighbor.z += coord.z < max ? 1 : -1; break;
				}
				var neighborIndex = PointGridUtility.GetIndex(resolution, neighbor.x, neighbor.y, neighbor.z);
				var midpoint = (_worldCache[index] + _worldCache[neighborIndex]) * 0.5f;
				var gui = HandleUtility.WorldToGUIPoint(midpoint);
				candidates.Add((axis, (gui - mousePosition).sqrMagnitude));
			}
			candidates.Sort((a, b) => a.distance.CompareTo(b.distance));

			var result = new List<HandleAxis>(candidates.Count);
			foreach (var c in candidates)
				result.Add(c.axis);
			if (result.Count == 0)
				result.Add(HandleAxis.X);
			return result;
		}

		private void ReplaceSelection(List<int> indices)
		{
			_selection.Clear();
			foreach (var i in indices)
				_selection.Add(i);
		}

		private void HandleMarquee(Vector3Int resolution)
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
								if (!PointGridViewState.IsSliceVisible(PointGridUtility.GetCoord(resolution, i)))
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
						// Unity 標準の矩形選択と同じ見た目(半透明の青+白枠)
						Handles.BeginGUI();
						var rect = RectFromPoints(_marqueeStart, _marqueeEnd);
						var inner = new Color32(148, 184, 237, (byte)(0.33f * 255));
						var border = new Color(1f, 1f, 1f, 0.67f);
						GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill,
							true, 1f, inner, Vector4.zero, Vector4.zero);
						GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill,
							true, 1f, border, Vector4.one, Vector4.zero);
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

		private void DrawWireframe(Vector3Int res)
		{
			if (Event.current.type != EventType.Repaint) return;

			var config = (res, PointGridViewState.SliceEnabled, PointGridViewState.SliceAxis,
				PointGridViewState.SliceVersion);
			if (_wireConfig != config || _wireIndexPairs.Length == 0)
			{
				RebuildWireIndexPairs(res);
				_wireConfig = config;
			}

			for (var s = 0; s < _wireIndexPairs.Length; s++)
				_wireSegments[s] = _worldCache[_wireIndexPairs[s]];

			Handles.zTest = CompareFunction.Always;
			Handles.color = WireColor;
			Handles.DrawLines(_wireSegments);
		}

		private void RebuildWireIndexPairs(Vector3Int res)
		{
			// 辺は両端の点が可視の場合のみ描く(単一・複数スライスの両方で自然な表示になる)
			var pairs = new List<int>();
			for (var z = 0; z < res.z; z++)
			for (var y = 0; y < res.y; y++)
			for (var x = 0; x < res.x; x++)
			{
				var c = new Vector3Int(x, y, z);
				if (!PointGridViewState.IsSliceVisible(c))
					continue;

				var from = PointGridUtility.GetIndex(res, x, y, z);
				if (x + 1 < res.x && PointGridViewState.IsSliceVisible(new Vector3Int(x + 1, y, z)))
				{
					pairs.Add(from);
					pairs.Add(PointGridUtility.GetIndex(res, x + 1, y, z));
				}
				if (y + 1 < res.y && PointGridViewState.IsSliceVisible(new Vector3Int(x, y + 1, z)))
				{
					pairs.Add(from);
					pairs.Add(PointGridUtility.GetIndex(res, x, y + 1, z));
				}
				if (z + 1 < res.z && PointGridViewState.IsSliceVisible(new Vector3Int(x, y, z + 1)))
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
