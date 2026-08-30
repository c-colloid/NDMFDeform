using System.Collections.Generic;
using System.Reflection;
using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// UVIslandMaskDeformer 専用インスペクタ。
	/// インスペクタ: 共通 UI(factor / falloff / invert)+ UV 島の選択ビュー。
	/// シーン: スタックをベイクした「変形後」の形状に対してレイキャスト・描画し、
	/// 選択島を緑、ホバー島を黄の半透明フィル + 輪郭で表示する
	/// (遮蔽部分も薄く描く二段描画)。メッシュ面のクリックで島をトグル選択できる。
	/// </summary>
	[CustomEditor(typeof(UVIslandMaskDeformer))]
	[CanEditMultipleObjects]
	public class UVIslandMaskDeformerEditor : DeformerBaseEditor
	{
		private static readonly int SceneControlHash = "NDMFDeformUVIslandScene".GetHashCode();

		private static readonly Color SelectedFillColor = new Color(0.25f, 1f, 0.45f, 0.32f);
		private static readonly Color HoverFillColor = new Color(1f, 0.85f, 0.25f, 0.45f);
		private static readonly Color SelectedOutlineColor = new Color(0.25f, 1f, 0.45f, 1f);
		private static readonly Color HoverOutlineColor = new Color(1f, 0.9f, 0.2f, 1f);

		/// <summary>遮蔽されている部分の透明度倍率(それでも薄く見えるように)</summary>
		private const float OccludedAlphaScale = 0.30f;

		/// <summary>変形後メッシュの状態チェック間隔(秒)</summary>
		private const double PreviewCheckInterval = 0.25;

		private static MethodInfo _intersectRayMesh;
		private static bool _intersectRayMeshSearched;
		private static Material _fillMaterial;
		private static bool _fillMaterialSearched;

		// OnSceneGUI 内では Editor.targets を参照できない(Unity が警告する)ため、
		// 複数選択かどうかは OnEnable で確定させておく
		private bool _isMultiEdit;

		private UVIslandAnalysis.Island _sceneHover;

		// ---- 変形後メッシュ(スタックをベイクした結果)。判定と描画の両方に使う ----
		private Mesh _previewMesh;
		private bool _previewOwned;
		private Vector3[] _previewVertices;
		private int _previewStateHash;
		private double _nextPreviewCheck;

		// ---- ハイライトキャッシュ(輪郭線分はメッシュローカル空間。Handles.matrix で変換) ----
		private int _highlightSelectionHash = -1;
		private UVIslandAnalysis.Island _highlightHover;
		private bool _highlightVerticesDirty = true;
		private Vector3[] _selectedSegments = System.Array.Empty<Vector3>();
		private Vector3[] _hoverSegments = System.Array.Empty<Vector3>();
		private Mesh _selectedFillMesh;
		private Mesh _hoverFillMesh;

		private void OnEnable()
		{
			_isMultiEdit = targets.Length > 1;
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			if (target is UVIslandMaskDeformer mask)
				UVIslandSelection.ClearHover(mask);
			DestroyPreviewMesh();
			if (_selectedFillMesh != null) DestroyImmediate(_selectedFillMesh);
			if (_hoverFillMesh != null) DestroyImmediate(_hoverFillMesh);
			_selectedFillMesh = null;
			_hoverFillMesh = null;
		}

		public override VisualElement CreateInspectorGUI()
		{
			var root = base.CreateInspectorGUI();

			if (targets.Length == 1)
			{
				root.Add(new UVIslandSelectorView((UVIslandMaskDeformer)target));
			}
			else
			{
				var note = new Label("UV 島の選択は 1 つずつ編集してください(複数選択中は非表示)。");
				note.style.opacity = 0.7f;
				note.style.whiteSpace = WhiteSpace.Normal;
				root.Add(note);
			}

			var guide = new Foldout { text = "操作ガイド", value = false };
			guide.style.marginTop = 4;
			var hint = new Label(
				"UV マップ上の島をクリックすると選択 / 解除できます。\n" +
				"ホイールでズーム、中ボタン(または Alt+左)ドラッグでパンします。\n" +
				"シーンビューでもメッシュ面のクリックで島を選択 / 解除できます\n" +
				"(選択島は緑、ホバー中の島は黄色でハイライト。隠れた部分も薄く表示)。\n" +
				"シーンのハイライトとクリック判定は変形後(プレビュー)の形状に追従します。\n" +
				"サブメッシュのドロップダウンで表示・クリック対象を絞り込めます。\n" +
				"スタック内でこのマスクより前にあるデフォーマの変形が、\n" +
				"選択した島の頂点で打ち消されます。Invert で島のみに変形を残します。\n" +
				"Falloff は打ち消しを島の外側へ UV 距離でぼかします。");
			hint.style.opacity = 0.7f;
			hint.style.whiteSpace = WhiteSpace.Normal;
			guide.Add(hint);
			root.Add(guide);

			return root;
		}

		protected override void OnSceneGUI()
		{
			base.OnSceneGUI();

			// 複数選択中は同一メッシュへ複数エディタが同時にレイキャスト・トグル
			// してしまうため、シーン側の島選択 UI は単一選択時のみ有効にする
			if (_isMultiEdit)
				return;
			var mask = target as UVIslandMaskDeformer;
			if (mask == null)
				return;
			if (!mask.TryGetSourceMesh(out var mesh, out var meshTransform, out _))
				return;
			var analysis = mask.GetOrCreateAnalysis(mesh);
			if (analysis == null || analysis.Islands.Count == 0)
				return;

			var e = Event.current;
			// SMR はボーン×バインドポーズ基準(ベイクの写像と一致させる)
			var matrix = RendererMeshSpace.GetMeshToWorld(meshTransform);

			UpdatePreviewMesh(mask, mesh, meshTransform);
			var pickMesh = _previewMesh != null ? _previewMesh : mesh;

			if (e.type == EventType.MouseMove)
			{
				var island = PickIsland(e.mousePosition, pickMesh, matrix, analysis);
				if (island != _sceneHover)
				{
					_sceneHover = island;
					UVIslandSelection.SetHover(mask, island);
					HandleUtility.Repaint();
				}
			}

			// ホバー中はクリックを奪って島トグルにする(外したクリックは通常の選択操作のまま)
			var controlId = GUIUtility.GetControlID(SceneControlHash, FocusType.Passive);
			if (e.type == EventType.Layout && _sceneHover != null)
				HandleUtility.AddDefaultControl(controlId);

			if (e.type == EventType.MouseDown && e.button == 0 && !e.alt &&
			    HandleUtility.nearestControl == controlId)
			{
				var island = PickIsland(e.mousePosition, pickMesh, matrix, analysis);
				if (island != null)
				{
					UVIslandSelection.Toggle(mask, analysis, island);
					e.Use();
				}
			}

			if (e.type == EventType.Repaint)
			{
				UpdateHighlightCaches(mask, analysis);
				DrawHighlight(matrix);
			}
		}

		// ---- 変形後メッシュの管理 ----

		/// <summary>
		/// スタックのベイク結果(変形後メッシュ)を維持する。
		/// デフォーマの編集・軸移動を状態ハッシュで検知し、一定間隔でのみ再ベイクする。
		/// スタックに有効なデフォーマが無い場合はソースメッシュをそのまま使う。
		/// </summary>
		private void UpdatePreviewMesh(UVIslandMaskDeformer mask, Mesh source, Transform meshTransform)
		{
			var now = EditorApplication.timeSinceStartup;
			if (now < _nextPreviewCheck && _previewVertices != null)
				return;
			_nextPreviewCheck = now + PreviewCheckInterval;

			var stack = mask.GetComponentInParent<DeformStack>();
			var hash = ComputePreviewStateHash(stack, source, meshTransform);
			if (hash == _previewStateHash && _previewVertices != null)
				return;
			_previewStateHash = hash;

			DestroyPreviewMesh();
			Mesh baked = null;
			if (stack != null)
			{
				// ハイライト用途は頂点位置だけあればよいのでシェイプ再ベイクは省く
				baked = DeformBakeCore.Bake(stack, source, meshTransform,
					new DeformBakeOptions { RebakeBlendShapes = false });
			}
			if (baked != null)
			{
				baked.hideFlags = HideFlags.HideAndDontSave;
				_previewMesh = baked;
				_previewOwned = true;
			}
			else
			{
				_previewMesh = source;
				_previewOwned = false;
			}
			_previewVertices = _previewMesh.vertices;
			_highlightVerticesDirty = true;
		}

		private static int ComputePreviewStateHash(DeformStack stack, Mesh source, Transform meshTransform)
		{
			unchecked
			{
				var h = 17;
				h = h * 31 + source.GetInstanceID();
				h = h * 31 + source.vertexCount;
				h = h * 31 + RendererMeshSpace.GetMeshToWorld(meshTransform).GetHashCode();
				if (stack == null)
					return h;

				foreach (var entry in stack.Deformers)
				{
					if (entry.deformer == null)
						continue;
					h = h * 31 + entry.deformer.GetInstanceID();
					h = h * 31 + (entry.enabled ? 1 : 0);
					// インスペクタ編集・Undo は dirty カウント、軸の移動は行列で検知する
					h = h * 31 + EditorUtility.GetDirtyCount(entry.deformer);
					var axis = entry.deformer.Axis;
					if (axis != null)
						h = h * 31 + axis.localToWorldMatrix.GetHashCode();
				}
				return h;
			}
		}

		private void DestroyPreviewMesh()
		{
			if (_previewOwned && _previewMesh != null)
				DestroyImmediate(_previewMesh);
			_previewMesh = null;
			_previewOwned = false;
			_previewVertices = null;
		}

		// ---- レイキャスト ----

		/// <summary>シーンビューのマウス位置からレイキャストして UV 島を拾う</summary>
		private UVIslandAnalysis.Island PickIsland(
			Vector2 guiPosition, Mesh mesh, Matrix4x4 matrix, UVIslandAnalysis analysis)
		{
			if (analysis.IslandOfTriangle == null)
				return null;

			var ray = HandleUtility.GUIPointToWorldRay(guiPosition);
			if (!IntersectRayMesh(ray, mesh, matrix, out var hit))
				return null;

			var triangle = hit.triangleIndex;
			if (triangle < 0 || triangle >= analysis.IslandOfTriangle.Length)
				return null;
			return analysis.IslandOfTriangle[triangle];
		}

		/// <summary>
		/// HandleUtility.IntersectRayMesh(internal)によるコライダー不要のメッシュレイキャスト。
		/// SkinnedMeshRenderer はスキニング前の形状に対して判定する
		/// (バインドポーズから大きく外れたポーズではずれることがある)。
		/// </summary>
		private static bool IntersectRayMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit)
		{
			hit = default;
			if (!_intersectRayMeshSearched)
			{
				_intersectRayMeshSearched = true;
				_intersectRayMesh = typeof(HandleUtility).GetMethod(
					"IntersectRayMesh", BindingFlags.Static | BindingFlags.NonPublic);
			}
			if (_intersectRayMesh == null)
				return false;

			var args = new object[] { ray, mesh, matrix, null };
			if (!(bool)_intersectRayMesh.Invoke(null, args))
				return false;
			hit = (RaycastHit)args[3];
			return true;
		}

		// ---- ハイライト描画 ----

		private void UpdateHighlightCaches(UVIslandMaskDeformer mask, UVIslandAnalysis analysis)
		{
			var selectionHash = mask.SelectionHash();
			var hover = _sceneHover;
			if (hover == null && UVIslandSelection.HoverDeformer == mask)
				hover = UVIslandSelection.HoverIsland;

			if (_highlightVerticesDirty || selectionHash != _highlightSelectionHash)
			{
				var selected = mask.ResolveSelectedIslands(analysis);
				_selectedSegments = BuildSegments(selected);
				BuildFillMesh(ref _selectedFillMesh, selected);
				_highlightSelectionHash = selectionHash;
			}

			if (_highlightVerticesDirty || hover != _highlightHover)
			{
				if (hover != null)
				{
					var hoverList = new List<UVIslandAnalysis.Island> { hover };
					_hoverSegments = BuildSegments(hoverList);
					BuildFillMesh(ref _hoverFillMesh, hoverList);
				}
				else
				{
					_hoverSegments = System.Array.Empty<Vector3>();
					BuildFillMesh(ref _hoverFillMesh, null);
				}
				_highlightHover = hover;
			}

			_highlightVerticesDirty = false;
		}

		private void DrawHighlight(Matrix4x4 matrix)
		{
			var hasSelected = _selectedFillMesh != null && _selectedFillMesh.vertexCount > 0;
			var hasHover = _hoverFillMesh != null && _hoverFillMesh.vertexCount > 0;
			if (!hasSelected && !hasHover && _selectedSegments.Length == 0 && _hoverSegments.Length == 0)
				return;

			// 面ハイライト: 可視部分をしっかり、遮蔽部分もうっすら描く
			var material = GetFillMaterial();
			if (material != null && (hasSelected || hasHover))
			{
				DrawFillPass(material, matrix, CompareFunction.LessEqual, 1f, hasSelected, hasHover);
				DrawFillPass(material, matrix, CompareFunction.Greater, OccludedAlphaScale, hasSelected, hasHover);
			}

			var previousZTest = Handles.zTest;
			using (new Handles.DrawingScope(matrix))
			{
				// 遮蔽側の輪郭を薄く
				Handles.zTest = CompareFunction.Greater;
				DrawOutlines(OccludedAlphaScale);
				// 可視側の輪郭をはっきり
				Handles.zTest = CompareFunction.LessEqual;
				DrawOutlines(1f);
			}
			Handles.zTest = previousZTest;
		}

		private void DrawOutlines(float alphaScale)
		{
			if (_selectedSegments.Length > 0)
			{
				Handles.color = ScaleAlpha(SelectedOutlineColor, alphaScale);
				Handles.DrawLines(_selectedSegments);
			}
			if (_hoverSegments.Length > 0)
			{
				Handles.color = ScaleAlpha(HoverOutlineColor, alphaScale);
				Handles.DrawLines(_hoverSegments);
			}
		}

		private void DrawFillPass(Material material, Matrix4x4 matrix, CompareFunction zTest,
			float alphaScale, bool hasSelected, bool hasHover)
		{
			material.SetInt("_ZTest", (int)zTest);
			if (hasSelected)
			{
				material.SetColor("_Color", ScaleAlpha(SelectedFillColor, alphaScale));
				if (material.SetPass(0))
					Graphics.DrawMeshNow(_selectedFillMesh, matrix);
			}
			if (hasHover)
			{
				material.SetColor("_Color", ScaleAlpha(HoverFillColor, alphaScale));
				if (material.SetPass(0))
					Graphics.DrawMeshNow(_hoverFillMesh, matrix);
			}
		}

		private static Color ScaleAlpha(Color color, float scale)
		{
			color.a *= scale;
			return color;
		}

		private static Material GetFillMaterial()
		{
			if (!_fillMaterialSearched)
			{
				_fillMaterialSearched = true;
				var shader = Shader.Find("Hidden/Internal-Colored");
				if (shader != null)
				{
					_fillMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
					_fillMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
					_fillMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
					_fillMaterial.SetInt("_ZWrite", 0);
					_fillMaterial.SetInt("_Cull", (int)CullMode.Off);
				}
			}
			return _fillMaterial;
		}

		/// <summary>島の三角形を変形後頂点で持つハイライト用フィルメッシュを組み立てる</summary>
		private void BuildFillMesh(ref Mesh mesh, List<UVIslandAnalysis.Island> islands)
		{
			if (mesh == null)
				mesh = new Mesh { name = "UVIslandHighlight", hideFlags = HideFlags.HideAndDontSave };
			mesh.Clear();

			if (_previewVertices == null || islands == null || islands.Count == 0)
				return;

			var triangles = new List<int>();
			foreach (var island in islands)
				triangles.AddRange(island.Triangles);
			if (triangles.Count == 0)
				return;

			mesh.indexFormat = _previewVertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.vertices = _previewVertices;
			mesh.SetTriangles(triangles, 0);
		}

		/// <summary>島の境界エッジを変形後メッシュローカル空間の線分ペア配列にする</summary>
		private Vector3[] BuildSegments(List<UVIslandAnalysis.Island> islands)
		{
			if (_previewVertices == null || islands == null)
				return System.Array.Empty<Vector3>();

			var count = 0;
			foreach (var island in islands)
				count += island.BorderEdgeVerts.Count;
			if (count == 0)
				return System.Array.Empty<Vector3>();

			var segments = new Vector3[count];
			var index = 0;
			foreach (var island in islands)
			{
				var verts = island.BorderEdgeVerts;
				for (var i = 0; i < verts.Count; i++)
				{
					var v = verts[i];
					if (v < 0 || v >= _previewVertices.Length)
						continue;
					segments[index++] = _previewVertices[v];
				}
			}

			if (index != segments.Length)
				System.Array.Resize(ref segments, index - index % 2);
			return segments;
		}
	}
}
