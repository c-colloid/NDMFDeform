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
	/// 対象メッシュは親の DeformStack が付いたレンダラーから解決する。
	/// 選択は UVIslandMaskDeformer.IslandSeeds(代表 UV)として保存される。
	/// </summary>
	public class UVIslandSelectorView : VisualElement
	{
		private const int TextureSize = 512;
		private const int DisplaySize = 300;

		private readonly UVIslandMaskDeformer _deformer;
		private readonly VisualElement _map;
		private readonly Label _status;
		private readonly HelpBox _noMeshHelp;

		private Mesh _mesh;
		private UVIslandAnalysis _analysis;
		private Texture2D _texture;

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
			_map.RegisterCallback<ClickEvent>(OnMapClick);
			Add(_map);

			_status = new Label();
			_status.style.opacity = 0.7f;
			_status.style.whiteSpace = WhiteSpace.Normal;
			Add(_status);

			var buttons = new VisualElement();
			buttons.style.flexDirection = FlexDirection.Row;
			buttons.style.marginTop = 2;
			buttons.Add(new Button(ClearSelection) { text = "選択解除" });
			buttons.Add(new Button(ReanalyzeMesh) { text = "再解析" });
			Add(buttons);

			RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += Refresh);
			RegisterCallback<DetachFromPanelEvent>(_ =>
			{
				Undo.undoRedoPerformed -= Refresh;
				DestroyTexture();
			});

			Refresh();
		}

		/// <summary>メッシュ解決・解析・テクスチャを更新する</summary>
		public void Refresh()
		{
			if (_deformer == null)
				return;

			_mesh = FindSourceMesh();
			_analysis = _deformer.GetOrCreateAnalysis(_mesh);

			var hasMesh = _mesh != null && _analysis != null && _analysis.Islands.Count > 0;
			_noMeshHelp.style.display = _mesh == null ? DisplayStyle.Flex : DisplayStyle.None;
			_map.style.display = hasMesh ? DisplayStyle.Flex : DisplayStyle.None;

			if (!hasMesh)
			{
				_status.text = _mesh != null
					? $"メッシュ: {_mesh.name} — UV 島が見つかりません(UV0 が必要です)"
					: string.Empty;
				return;
			}

			var selected = ResolveSelectedIslands();
			RegenerateTexture(selected);
			_status.text = $"メッシュ: {_mesh.name} / 島: {_analysis.Islands.Count} / 選択: {selected.Count}";
		}

		private Mesh FindSourceMesh()
		{
			var stack = _deformer.GetComponentInParent<DeformStack>();
			if (stack == null)
				return null;

			if (stack.TryGetComponent<SkinnedMeshRenderer>(out var smr))
				return smr.sharedMesh;
			if (stack.TryGetComponent<MeshFilter>(out var mf))
				return mf.sharedMesh;
			return null;
		}

		private HashSet<UVIslandAnalysis.Island> ResolveSelectedIslands()
		{
			var selected = new HashSet<UVIslandAnalysis.Island>();
			foreach (var seed in _deformer.IslandSeeds)
			{
				var island = _analysis.FindIslandAt(seed);
				if (island != null)
					selected.Add(island);
			}
			return selected;
		}

		private void OnMapClick(ClickEvent evt)
		{
			if (_analysis == null || _analysis.Islands.Count == 0)
				return;

			var rect = _map.contentRect;
			if (rect.width <= 0f || rect.height <= 0f)
				return;

			var uv = new Vector2(
				evt.localPosition.x / rect.width,
				1f - evt.localPosition.y / rect.height);

			var island = _analysis.FindIslandAt(uv);
			if (island == null)
				return;

			Undo.RecordObject(_deformer, "Toggle UV Island");

			// この島に解決される既存シードを取り除く。無ければ追加(= トグル)
			var seeds = _deformer.IslandSeeds;
			var removed = seeds.RemoveAll(s => _analysis.FindIslandAt(s) == island) > 0;
			if (!removed)
				seeds.Add(island.Seed);

			PrefabUtility.RecordPrefabInstancePropertyModifications(_deformer);
			EditorUtility.SetDirty(_deformer);
			Refresh();
		}

		private void ClearSelection()
		{
			if (_deformer.IslandSeeds.Count == 0)
				return;

			Undo.RecordObject(_deformer, "Clear UV Island Selection");
			_deformer.IslandSeeds.Clear();
			PrefabUtility.RecordPrefabInstancePropertyModifications(_deformer);
			EditorUtility.SetDirty(_deformer);
			Refresh();
		}

		private void ReanalyzeMesh()
		{
			_deformer.InvalidateAnalysis();
			Refresh();
		}

		// ---- UV マップテクスチャ描画 ----

		private void RegenerateTexture(HashSet<UVIslandAnalysis.Island> selected)
		{
			if (_texture == null)
			{
				_texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
				{
					hideFlags = HideFlags.HideAndDontSave,
				};
			}

			var pixels = new Color[TextureSize * TextureSize];
			var background = new Color(0.15f, 0.15f, 0.15f, 1f);
			for (var i = 0; i < pixels.Length; i++)
				pixels[i] = background;

			DrawGrid(pixels);

			foreach (var island in _analysis.Islands)
			{
				var fill = selected.Contains(island)
					? IslandColor(island.Id)
					: new Color(0.45f, 0.45f, 0.45f, 0.6f);
				DrawIslandFill(island, pixels, fill);
			}

			foreach (var island in _analysis.Islands)
			{
				var wire = selected.Contains(island)
					? Color.white
					: new Color(0.7f, 0.7f, 0.7f, 1f);
				DrawIslandWireframe(island, pixels, wire);
			}

			_texture.SetPixels(pixels);
			_texture.Apply(false);
			_map.style.backgroundImage = _texture;
		}

		private static Color IslandColor(int islandId)
		{
			// 黄金比で色相を分散し、隣接 ID でも見分けやすくする
			var hue = (islandId * 0.618034f) % 1f;
			return Color.HSVToRGB(hue, 0.7f, 0.9f);
		}

		private static void DrawGrid(Color[] pixels)
		{
			var gridColor = new Color(0.25f, 0.25f, 0.25f, 1f);
			var step = TextureSize / 10;
			for (var x = 0; x < TextureSize; x += step)
				for (var y = 0; y < TextureSize; y++)
					pixels[y * TextureSize + x] = gridColor;
			for (var y = 0; y < TextureSize; y += step)
				for (var x = 0; x < TextureSize; x++)
					pixels[y * TextureSize + x] = gridColor;
		}

		private void DrawIslandFill(UVIslandAnalysis.Island island, Color[] pixels, Color color)
		{
			var uvs = _analysis.Uvs;
			var triangles = island.Triangles;
			for (var i = 0; i + 2 < triangles.Count; i += 3)
				FillTriangle(uvs[triangles[i]], uvs[triangles[i + 1]], uvs[triangles[i + 2]], pixels, color);
		}

		private static void FillTriangle(Vector2 uv0, Vector2 uv1, Vector2 uv2, Color[] pixels, Color color)
		{
			var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(uv0.x, Mathf.Min(uv1.x, uv2.x)) * TextureSize));
			var maxX = Mathf.Min(TextureSize - 1, Mathf.CeilToInt(Mathf.Max(uv0.x, Mathf.Max(uv1.x, uv2.x)) * TextureSize));
			var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(uv0.y, Mathf.Min(uv1.y, uv2.y)) * TextureSize));
			var maxY = Mathf.Min(TextureSize - 1, Mathf.CeilToInt(Mathf.Max(uv0.y, Mathf.Max(uv1.y, uv2.y)) * TextureSize));

			for (var y = minY; y <= maxY; y++)
			{
				for (var x = minX; x <= maxX; x++)
				{
					var point = new Vector2((x + 0.5f) / TextureSize, (y + 0.5f) / TextureSize);
					if (UVIslandAnalysis.IsPointInTriangle(point, uv0, uv1, uv2))
					{
						var index = y * TextureSize + x;
						pixels[index] = Color.Lerp(pixels[index], color, 0.6f);
					}
				}
			}
		}

		private void DrawIslandWireframe(UVIslandAnalysis.Island island, Color[] pixels, Color color)
		{
			var uvs = _analysis.Uvs;
			var triangles = island.Triangles;
			for (var i = 0; i + 2 < triangles.Count; i += 3)
			{
				DrawLine(uvs[triangles[i]], uvs[triangles[i + 1]], pixels, color);
				DrawLine(uvs[triangles[i + 1]], uvs[triangles[i + 2]], pixels, color);
				DrawLine(uvs[triangles[i + 2]], uvs[triangles[i]], pixels, color);
			}
		}

		/// <summary>UV 空間の線分を Bresenham で描画(テクスチャは下原点なので Y 反転しない)</summary>
		private static void DrawLine(Vector2 start, Vector2 end, Color[] pixels, Color color)
		{
			var x0 = Mathf.RoundToInt(start.x * TextureSize);
			var y0 = Mathf.RoundToInt(start.y * TextureSize);
			var x1 = Mathf.RoundToInt(end.x * TextureSize);
			var y1 = Mathf.RoundToInt(end.y * TextureSize);

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

		private void DestroyTexture()
		{
			if (_texture != null)
			{
				Object.DestroyImmediate(_texture);
				_texture = null;
			}
		}
	}
}
