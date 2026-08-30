// 移植元: dev ブランチ ExDeform/Editor/UVIslandSelectorEditor.cs(自作コード)を
// UVIslandMaskDeformer + UVIslandAnalysis 前提の UITK ビューとして再構成
using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// UV マッププレビュー上で UV 島をクリック選択するビュー。
	/// ホイールでズーム、中ボタン(または Alt+左)ドラッグでパンできる。
	/// サブメッシュのドロップダウンで表示・クリック対象を絞り込める。
	/// ホバー中の島は黄色の輪郭で表示され、シーンビュー側にも同じ島の輪郭が出る。
	/// 選択は UVIslandMaskDeformer.SelectedIslands(代表 UV + サブメッシュ)として保存される。
	/// </summary>
	public class UVIslandSelectorView : VisualElement
	{
		private const int TextureSize = 512;
		private const int DisplaySize = 300;

		/// <summary>ズーム・パン中の再レンダリング間隔(秒)。約 30Hz</summary>
		private const double RenderInterval = 0.033;

		private readonly UVIslandMaskDeformer _deformer;
		private readonly VisualElement _map;
		private readonly VisualElement _hoverOverlay;
		private readonly DropdownField _subMeshDropdown;
		private readonly Label _status;
		private readonly HelpBox _noMeshHelp;

		private Mesh _mesh;
		private UVIslandAnalysis _analysis;
		private Texture2D _texture;
		private Color[] _pixels;

		// 表示ウィンドウ(UV 空間): _viewCenter を中心に一辺 _viewSize の正方形
		private Vector2 _viewCenter = new Vector2(0.5f, 0.5f);
		private float _viewSize = 1.1f;
		private bool _viewInitialized;

		private int _subMeshFilter = -1;
		private UVIslandAnalysis.Island _hoverIsland;

		private bool _panning;
		private int _panPointerId = -1;
		private Vector2 _pressPosition;
		private bool _pressed;

		// 矩形範囲選択(左ドラッグ)。閾値を超えたらクリックではなくマーキーとして扱う
		private bool _marqueeActive;
		private int _marqueePointerId = -1;
		private Vector2 _marqueeEnd;

		private double _lastRenderTime;
		private bool _renderQueued;

		public UVIslandSelectorView(UVIslandMaskDeformer deformer)
		{
			_deformer = deformer;

			var title = new Label("UV 島の選択");
			title.style.unityFontStyleAndWeight = FontStyle.Bold;
			title.style.marginTop = 6;
			Add(title);

			_noMeshHelp = new HelpBox(
				"対象メッシュが見つかりません。DeformStack の付いたレンダラーの配下に置いてください。",
				HelpBoxMessageType.Warning);
			Add(_noMeshHelp);

			_subMeshDropdown = new DropdownField("サブメッシュ");
			_subMeshDropdown.RegisterValueChangedCallback(_ =>
			{
				_subMeshFilter = _subMeshDropdown.index - 1;
				SetHover(null);
				RenderNow();
			});
			Add(_subMeshDropdown);

			_map = new VisualElement();
			_map.style.width = DisplaySize;
			_map.style.height = DisplaySize;
			_map.style.alignSelf = Align.Center;
			_map.style.marginTop = 4;
			_map.style.marginBottom = 4;
			_map.style.borderTopWidth = 1;
			_map.style.borderBottomWidth = 1;
			_map.style.borderLeftWidth = 1;
			_map.style.borderRightWidth = 1;
			var borderColor = (Color)new Color32(90, 90, 90, 255);
			_map.style.borderTopColor = borderColor;
			_map.style.borderBottomColor = borderColor;
			_map.style.borderLeftColor = borderColor;
			_map.style.borderRightColor = borderColor;
			Add(_map);

			// ホバー島の輪郭はテクスチャを再生成せずベクタ描画で重ねる
			_hoverOverlay = new VisualElement();
			_hoverOverlay.style.position = Position.Absolute;
			_hoverOverlay.style.left = 0;
			_hoverOverlay.style.top = 0;
			_hoverOverlay.style.right = 0;
			_hoverOverlay.style.bottom = 0;
			_hoverOverlay.pickingMode = PickingMode.Ignore;
			_hoverOverlay.generateVisualContent += DrawHoverOverlay;
			_map.Add(_hoverOverlay);

			_map.RegisterCallback<WheelEvent>(OnWheel);
			_map.RegisterCallback<PointerDownEvent>(OnPointerDown);
			_map.RegisterCallback<PointerMoveEvent>(OnPointerMove);
			_map.RegisterCallback<PointerUpEvent>(OnPointerUp);
			_map.RegisterCallback<PointerLeaveEvent>(_ => SetHover(null));

			_status = new Label();
			_status.style.opacity = 0.7f;
			_status.style.whiteSpace = WhiteSpace.Normal;
			Add(_status);

			var hint = new Label(
				"ホイール: ズーム / 中ボタン or Alt+ドラッグ: パン / 左ドラッグ: 範囲選択(Ctrl で解除)\n" +
				"UV が重なった場所のクリックはメニューから島を選べます。シーンビューの面クリックでも選択できます");
			hint.style.opacity = 0.5f;
			hint.style.fontSize = 10;
			hint.style.whiteSpace = WhiteSpace.Normal;
			Add(hint);

			var buttons = new VisualElement();
			buttons.style.flexDirection = FlexDirection.Row;
			buttons.style.marginTop = 2;
			buttons.Add(new Button(() => UVIslandSelection.ClearSelection(_deformer)) { text = "選択解除" });
			buttons.Add(new Button(ResetView) { text = "全体表示" });
			buttons.Add(new Button(ReanalyzeMesh) { text = "再解析" });
			Add(buttons);

			RegisterCallback<AttachToPanelEvent>(_ =>
			{
				Undo.undoRedoPerformed += Refresh;
				UVIslandSelection.Changed += Refresh;
			});
			RegisterCallback<DetachFromPanelEvent>(_ =>
			{
				Undo.undoRedoPerformed -= Refresh;
				UVIslandSelection.Changed -= Refresh;
				UVIslandSelection.ClearHover(_deformer);
				DestroyTexture();
			});

			Refresh();
		}

		/// <summary>メッシュ解決・解析・テクスチャを更新する</summary>
		public void Refresh()
		{
			if (_deformer == null)
				return;

			Renderer renderer = null;
			_mesh = _deformer.TryGetSourceMesh(out var mesh, out _, out renderer) ? mesh : null;
			_analysis = _deformer.GetOrCreateAnalysis(_mesh);

			var hasIslands = _mesh != null && _analysis != null && _analysis.Islands.Count > 0;
			_noMeshHelp.style.display = _mesh == null ? DisplayStyle.Flex : DisplayStyle.None;
			_map.style.display = hasIslands ? DisplayStyle.Flex : DisplayStyle.None;
			_subMeshDropdown.style.display =
				hasIslands && _analysis.SubMeshCount > 1 ? DisplayStyle.Flex : DisplayStyle.None;

			if (!hasIslands)
			{
				_subMeshFilter = -1;
				_status.text = _mesh != null
					? $"メッシュ: {_mesh.name} — UV 島が見つかりません(UV0 が必要です)"
					: string.Empty;
				return;
			}

			if (!_viewInitialized)
			{
				ResetViewWindow();
				_viewInitialized = true;
			}

			UpdateSubMeshDropdown(renderer);
			RenderNow();
		}

		// ---- 表示ウィンドウ(ズーム・パン) ----

		private Vector2 WindowMin => _viewCenter - Vector2.one * (_viewSize * 0.5f);

		private void ResetViewWindow()
		{
			// [0,1] と実際の UV 範囲の合併に 5% の余白を付けて全体表示
			var min = Vector2.Min(Vector2.zero, _analysis.UvBoundsMin);
			var max = Vector2.Max(Vector2.one, _analysis.UvBoundsMax);
			_viewCenter = (min + max) * 0.5f;
			_viewSize = Mathf.Max(max.x - min.x, max.y - min.y) * 1.05f;
		}

		private void ResetView()
		{
			if (_analysis == null)
				return;
			ResetViewWindow();
			RenderNow();
		}

		private Vector2 LocalToUv(Vector2 local)
		{
			var rect = _map.contentRect;
			if (rect.width <= 0f || rect.height <= 0f)
				return Vector2.zero;
			var min = WindowMin;
			return new Vector2(
				min.x + local.x / rect.width * _viewSize,
				min.y + (1f - local.y / rect.height) * _viewSize);
		}

		private Vector2 UvToLocal(Vector2 uv)
		{
			var rect = _map.contentRect;
			var min = WindowMin;
			return new Vector2(
				(uv.x - min.x) / _viewSize * rect.width,
				(1f - (uv.y - min.y) / _viewSize) * rect.height);
		}

		private void OnWheel(WheelEvent evt)
		{
			if (_analysis == null)
				return;

			var uvAtCursor = LocalToUv(evt.localMousePosition);
			var factor = Mathf.Pow(1.15f, evt.delta.y > 0f ? 1f : -1f);
			var newSize = Mathf.Clamp(_viewSize * factor, 0.02f, 8f);

			// カーソル位置の UV を固定したままスケールする
			_viewCenter = uvAtCursor + (_viewCenter - uvAtCursor) * (newSize / _viewSize);
			_viewSize = newSize;
			ClampWindow();

			RequestRender();
			evt.StopPropagation();
		}

		private void ClampWindow()
		{
			var min = Vector2.Min(Vector2.zero, _analysis.UvBoundsMin);
			var max = Vector2.Max(Vector2.one, _analysis.UvBoundsMax);
			_viewCenter.x = Mathf.Clamp(_viewCenter.x, min.x - _viewSize, max.x + _viewSize);
			_viewCenter.y = Mathf.Clamp(_viewCenter.y, min.y - _viewSize, max.y + _viewSize);
		}

		private void OnPointerDown(PointerDownEvent evt)
		{
			if (_analysis == null)
				return;

			// 中ボタン or Alt+左 でパン開始
			if (evt.button == 2 || (evt.button == 0 && evt.altKey))
			{
				_panning = true;
				_panPointerId = evt.pointerId;
				_map.CapturePointer(evt.pointerId);
				evt.StopPropagation();
				return;
			}

			if (evt.button == 0)
			{
				_pressed = true;
				_pressPosition = evt.localPosition;
			}
		}

		private void OnPointerMove(PointerMoveEvent evt)
		{
			if (_panning && evt.pointerId == _panPointerId)
			{
				var rect = _map.contentRect;
				if (rect.width > 0f)
				{
					var delta = (Vector2)evt.deltaPosition;
					_viewCenter.x -= delta.x / rect.width * _viewSize;
					_viewCenter.y += delta.y / rect.height * _viewSize;
					ClampWindow();
					RequestRender();
				}
				return;
			}

			if (_marqueeActive && evt.pointerId == _marqueePointerId)
			{
				_marqueeEnd = evt.localPosition;
				_hoverOverlay.MarkDirtyRepaint();
				return;
			}

			// 押したまま閾値を超えて動いたら矩形範囲選択を開始する
			if (_pressed && _analysis != null &&
			    ((Vector2)evt.localPosition - _pressPosition).sqrMagnitude >= 16f)
			{
				_pressed = false;
				_marqueeActive = true;
				_marqueePointerId = evt.pointerId;
				_marqueeEnd = evt.localPosition;
				_map.CapturePointer(evt.pointerId);
				SetHover(null);
				_hoverOverlay.MarkDirtyRepaint();
				return;
			}

			SetHover(_analysis?.FindIslandAt(LocalToUv(evt.localPosition), _subMeshFilter));
		}

		private void OnPointerUp(PointerUpEvent evt)
		{
			if (_panning && evt.pointerId == _panPointerId)
			{
				_panning = false;
				_panPointerId = -1;
				_map.ReleasePointer(evt.pointerId);
				evt.StopPropagation();
				return;
			}

			if (_marqueeActive && evt.pointerId == _marqueePointerId)
			{
				FinishMarquee(evt);
				evt.StopPropagation();
				return;
			}

			// パンでもマーキーでもないクリックのみ選択トグルとして扱う
			if (_pressed && evt.button == 0 && !evt.altKey && _analysis != null)
				ToggleAt(evt.localPosition, evt.position);
			_pressed = false;
		}

		/// <summary>
		/// クリック位置の島をトグルする。UV が重なって複数の島が該当する場合は
		/// コンテキストメニューを出してどの島かを選ばせる。
		/// </summary>
		private void ToggleAt(Vector2 localPosition, Vector2 panelPosition)
		{
			var uv = LocalToUv(localPosition);
			var hits = new List<UVIslandAnalysis.Island>();
			_analysis.FindIslandsAt(uv, _subMeshFilter, hits);

			if (hits.Count == 0)
			{
				// 島の外側ギリギリのクリックは従来どおり近傍フォールバック
				var near = _analysis.FindIslandAt(uv, _subMeshFilter);
				if (near != null)
					UVIslandSelection.Toggle(_deformer, _analysis, near);
				return;
			}
			if (hits.Count == 1)
			{
				UVIslandSelection.Toggle(_deformer, _analysis, hits[0]);
				return;
			}
			ShowOverlapMenu(hits, panelPosition);
		}

		/// <summary>重なった島の候補メニュー(チェックは現在の選択状態)</summary>
		private void ShowOverlapMenu(List<UVIslandAnalysis.Island> islands, Vector2 panelPosition)
		{
			var selected = new HashSet<UVIslandAnalysis.Island>(_deformer.ResolveSelectedIslands(_analysis));
			var menu = new GenericMenu();
			foreach (var island in islands)
			{
				var captured = island;
				menu.AddItem(new GUIContent(IslandLabel(island)), selected.Contains(island),
					() => UVIslandSelection.Toggle(_deformer, _analysis, captured));
			}
			menu.DropDown(new Rect(panelPosition.x, panelPosition.y, 0f, 0f));
		}

		private string IslandLabel(UVIslandAnalysis.Island island)
		{
			var label = $"島 {island.Id}(三角形 {island.Triangles.Count / 3})";
			if (_subMeshFilter < 0 && _analysis.SubMeshCount > 1)
				label = $"サブメッシュ {island.SubMesh} ─ {label}";
			return label;
		}

		/// <summary>矩形範囲選択の確定。ドラッグ = 追加、Ctrl(Cmd)+ドラッグ = 解除</summary>
		private void FinishMarquee(PointerUpEvent evt)
		{
			_marqueeActive = false;
			_marqueePointerId = -1;
			_map.ReleasePointer(evt.pointerId);

			var a = LocalToUv(_pressPosition);
			var b = LocalToUv(evt.localPosition);
			var min = Vector2.Min(a, b);
			var max = Vector2.Max(a, b);

			var hits = new List<UVIslandAnalysis.Island>();
			_analysis.CollectIslandsInRect(min, max, _subMeshFilter, hits);
			if (hits.Count > 0)
			{
				var remove = evt.ctrlKey || evt.commandKey;
				UVIslandSelection.SetSelected(_deformer, _analysis, hits, !remove);
			}
			_hoverOverlay.MarkDirtyRepaint();
		}

		private void SetHover(UVIslandAnalysis.Island island)
		{
			if (_hoverIsland == island)
				return;
			_hoverIsland = island;
			UVIslandSelection.SetHover(_deformer, island);
			_hoverOverlay.MarkDirtyRepaint();
		}

		// ---- サブメッシュフィルタ ----

		private void UpdateSubMeshDropdown(Renderer renderer)
		{
			var materials = renderer != null ? renderer.sharedMaterials : null;
			var choices = new List<string> { "全て" };
			for (var s = 0; s < _analysis.SubMeshCount; s++)
			{
				var materialName = materials != null && s < materials.Length && materials[s] != null
					? materials[s].name
					: null;
				choices.Add(materialName != null ? $"{s}: {materialName}" : s.ToString());
			}
			_subMeshDropdown.choices = choices;

			if (_subMeshFilter >= _analysis.SubMeshCount)
				_subMeshFilter = -1;
			_subMeshDropdown.SetValueWithoutNotify(choices[_subMeshFilter + 1]);
		}

		private void ReanalyzeMesh()
		{
			_deformer.InvalidateAnalysis();
			Refresh();
		}

		// ---- UV マップテクスチャ描画 ----

		private void RequestRender()
		{
			var now = EditorApplication.timeSinceStartup;
			if (now - _lastRenderTime >= RenderInterval)
			{
				RenderNow();
				return;
			}
			if (_renderQueued)
				return;
			_renderQueued = true;
			var delay = Mathf.Max(1, (int)((RenderInterval - (now - _lastRenderTime)) * 1000));
			schedule.Execute(() =>
			{
				_renderQueued = false;
				RenderNow();
			}).ExecuteLater(delay);
		}

		private void RenderNow()
		{
			if (_analysis == null || _analysis.Islands.Count == 0)
				return;

			_lastRenderTime = EditorApplication.timeSinceStartup;

			var selected = new HashSet<UVIslandAnalysis.Island>(_deformer.ResolveSelectedIslands(_analysis));
			RegenerateTexture(selected);
			_hoverOverlay.MarkDirtyRepaint();

			var visibleIslands = 0;
			foreach (var island in _analysis.Islands)
			{
				if (_subMeshFilter < 0 || island.SubMesh == _subMeshFilter)
					visibleIslands++;
			}
			_status.text = _subMeshFilter < 0
				? $"メッシュ: {_mesh.name} / 島: {_analysis.Islands.Count} / 選択: {selected.Count}"
				: $"メッシュ: {_mesh.name} / 島: {visibleIslands} (全 {_analysis.Islands.Count}) / 選択: {selected.Count}";
		}

		private void RegenerateTexture(HashSet<UVIslandAnalysis.Island> selected)
		{
			if (_texture == null)
			{
				_texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
				{
					hideFlags = HideFlags.HideAndDontSave,
				};
			}
			if (_pixels == null)
				_pixels = new Color[TextureSize * TextureSize];

			var background = new Color(0.15f, 0.15f, 0.15f, 1f);
			for (var i = 0; i < _pixels.Length; i++)
				_pixels[i] = background;

			DrawGrid(_pixels);

			foreach (var island in _analysis.Islands)
			{
				if (_subMeshFilter >= 0 && island.SubMesh != _subMeshFilter)
					continue;
				var fill = selected.Contains(island)
					? IslandColor(island.Id)
					: new Color(0.45f, 0.45f, 0.45f, 0.6f);
				DrawIslandFill(island, _pixels, fill);
			}

			foreach (var island in _analysis.Islands)
			{
				if (_subMeshFilter >= 0 && island.SubMesh != _subMeshFilter)
					continue;
				var wire = selected.Contains(island)
					? Color.white
					: new Color(0.7f, 0.7f, 0.7f, 1f);
				DrawIslandWireframe(island, _pixels, wire);
			}

			_texture.SetPixels(_pixels);
			_texture.Apply(false);
			_map.style.backgroundImage = _texture;
		}

		private static Color IslandColor(int islandId)
		{
			// 黄金比で色相を分散し、隣接 ID でも見分けやすくする
			var hue = (islandId * 0.618034f) % 1f;
			return Color.HSVToRGB(hue, 0.7f, 0.9f);
		}

		private Vector2 UvToPixel(Vector2 uv)
		{
			var min = WindowMin;
			return new Vector2(
				(uv.x - min.x) / _viewSize * TextureSize,
				(uv.y - min.y) / _viewSize * TextureSize);
		}

		private void DrawGrid(Color[] pixels)
		{
			// ズームに応じてグリッド刻みを切替える
			var step = _viewSize > 2f ? 0.5f : _viewSize > 0.6f ? 0.1f : _viewSize > 0.12f ? 0.05f : 0.01f;
			var gridColor = new Color(0.25f, 0.25f, 0.25f, 1f);
			var boundsColor = new Color(0.42f, 0.42f, 0.42f, 1f);

			var min = WindowMin;
			var max = min + Vector2.one * _viewSize;

			for (var u = Mathf.Ceil(min.x / step) * step; u <= max.x; u += step)
			{
				var x = Mathf.RoundToInt((u - min.x) / _viewSize * TextureSize);
				if (x < 0 || x >= TextureSize)
					continue;
				var color = Mathf.Abs(u) < step * 0.5f || Mathf.Abs(u - 1f) < step * 0.5f
					? boundsColor : gridColor;
				for (var y = 0; y < TextureSize; y++)
					pixels[y * TextureSize + x] = color;
			}

			for (var v = Mathf.Ceil(min.y / step) * step; v <= max.y; v += step)
			{
				var y = Mathf.RoundToInt((v - min.y) / _viewSize * TextureSize);
				if (y < 0 || y >= TextureSize)
					continue;
				var color = Mathf.Abs(v) < step * 0.5f || Mathf.Abs(v - 1f) < step * 0.5f
					? boundsColor : gridColor;
				for (var x = 0; x < TextureSize; x++)
					pixels[y * TextureSize + x] = color;
			}
		}

		private void DrawIslandFill(UVIslandAnalysis.Island island, Color[] pixels, Color color)
		{
			// ウィンドウ外の島はスキップ
			var min = WindowMin;
			var max = min + Vector2.one * _viewSize;
			if (island.UvMax.x < min.x || island.UvMin.x > max.x ||
			    island.UvMax.y < min.y || island.UvMin.y > max.y)
				return;

			var uvs = _analysis.Uvs;
			var triangles = island.Triangles;
			for (var i = 0; i + 2 < triangles.Count; i += 3)
			{
				FillTriangle(
					UvToPixel(uvs[triangles[i]]),
					UvToPixel(uvs[triangles[i + 1]]),
					UvToPixel(uvs[triangles[i + 2]]),
					pixels, color);
			}
		}

		private static void FillTriangle(Vector2 p0, Vector2 p1, Vector2 p2, Color[] pixels, Color color)
		{
			var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))));
			var maxX = Mathf.Min(TextureSize - 1, Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))));
			var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))));
			var maxY = Mathf.Min(TextureSize - 1, Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))));

			for (var y = minY; y <= maxY; y++)
			{
				for (var x = minX; x <= maxX; x++)
				{
					var point = new Vector2(x + 0.5f, y + 0.5f);
					if (UVIslandAnalysis.IsPointInTriangle(point, p0, p1, p2))
					{
						var index = y * TextureSize + x;
						pixels[index] = Color.Lerp(pixels[index], color, 0.6f);
					}
				}
			}
		}

		private void DrawIslandWireframe(UVIslandAnalysis.Island island, Color[] pixels, Color color)
		{
			var min = WindowMin;
			var max = min + Vector2.one * _viewSize;
			if (island.UvMax.x < min.x || island.UvMin.x > max.x ||
			    island.UvMax.y < min.y || island.UvMin.y > max.y)
				return;

			var uvs = _analysis.Uvs;
			var triangles = island.Triangles;
			for (var i = 0; i + 2 < triangles.Count; i += 3)
			{
				var p0 = UvToPixel(uvs[triangles[i]]);
				var p1 = UvToPixel(uvs[triangles[i + 1]]);
				var p2 = UvToPixel(uvs[triangles[i + 2]]);
				DrawLine(p0, p1, pixels, color);
				DrawLine(p1, p2, pixels, color);
				DrawLine(p2, p0, pixels, color);
			}
		}

		/// <summary>ピクセル空間の線分をテクスチャ範囲へクリップしてから Bresenham で描画</summary>
		private static void DrawLine(Vector2 start, Vector2 end, Color[] pixels, Color color)
		{
			if (!ClipLine(ref start, ref end))
				return;

			var x0 = Mathf.RoundToInt(start.x);
			var y0 = Mathf.RoundToInt(start.y);
			var x1 = Mathf.RoundToInt(end.x);
			var y1 = Mathf.RoundToInt(end.y);

			var dx = Mathf.Abs(x1 - x0);
			var dy = Mathf.Abs(y1 - y0);
			var sx = x0 < x1 ? 1 : -1;
			var sy = y0 < y1 ? 1 : -1;
			var err = dx - dy;

			while (true)
			{
				if (x0 >= 0 && x0 < TextureSize && y0 >= 0 && y0 < TextureSize)
				{
					var index = y0 * TextureSize + x0;
					pixels[index] = Color.Lerp(pixels[index], color, 0.9f);
				}

				if (x0 == x1 && y0 == y1)
					break;

				var e2 = 2 * err;
				if (e2 > -dy)
				{
					err -= dy;
					x0 += sx;
				}
				if (e2 < dx)
				{
					err += dx;
					y0 += sy;
				}
			}
		}

		/// <summary>Liang–Barsky によるテクスチャ範囲への線分クリップ(ズーム時の遠距離座標対策)</summary>
		private static bool ClipLine(ref Vector2 a, ref Vector2 b)
		{
			var d = b - a;
			float t0 = 0f, t1 = 1f;
			if (!ClipT(-d.x, a.x, ref t0, ref t1)) return false;
			if (!ClipT(d.x, TextureSize - 1 - a.x, ref t0, ref t1)) return false;
			if (!ClipT(-d.y, a.y, ref t0, ref t1)) return false;
			if (!ClipT(d.y, TextureSize - 1 - a.y, ref t0, ref t1)) return false;

			var start = a;
			b = start + d * t1;
			a = start + d * t0;
			return true;
		}

		private static bool ClipT(float p, float q, ref float t0, ref float t1)
		{
			if (Mathf.Approximately(p, 0f))
				return q >= 0f;
			var r = q / p;
			if (p < 0f)
			{
				if (r > t1) return false;
				if (r > t0) t0 = r;
			}
			else
			{
				if (r < t0) return false;
				if (r < t1) t1 = r;
			}
			return true;
		}

		// ---- ホバー輪郭(ベクタ描画) ----

		private void DrawHoverOverlay(MeshGenerationContext ctx)
		{
			if (_analysis == null)
				return;
			var painter = ctx.painter2D;

			if (_hoverIsland != null)
			{
				painter.strokeColor = new Color(1f, 0.9f, 0.2f, 0.95f);
				painter.lineWidth = 2f;
				painter.BeginPath();
				foreach (var edge in _hoverIsland.BorderEdges)
				{
					painter.MoveTo(UvToLocal(new Vector2(edge.x, edge.y)));
					painter.LineTo(UvToLocal(new Vector2(edge.z, edge.w)));
				}
				painter.Stroke();
			}

			// 矩形範囲選択のマーキー(Unity 標準のシーン矩形選択と同系色)
			if (_marqueeActive)
			{
				var min = Vector2.Min(_pressPosition, _marqueeEnd);
				var max = Vector2.Max(_pressPosition, _marqueeEnd);
				painter.fillColor = new Color32(148, 184, 237, 84);
				painter.strokeColor = new Color(1f, 1f, 1f, 0.9f);
				painter.lineWidth = 1f;
				painter.BeginPath();
				painter.MoveTo(new Vector2(min.x, min.y));
				painter.LineTo(new Vector2(max.x, min.y));
				painter.LineTo(new Vector2(max.x, max.y));
				painter.LineTo(new Vector2(min.x, max.y));
				painter.ClosePath();
				painter.Fill();
				painter.Stroke();
			}
		}

		private void DestroyTexture()
		{
			if (_texture != null)
			{
				Object.DestroyImmediate(_texture);
				_texture = null;
			}
			_pixels = null;
		}
	}
}
