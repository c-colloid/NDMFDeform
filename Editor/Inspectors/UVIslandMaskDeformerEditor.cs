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
	/// シーン: 選択島を緑、ホバー島を黄の輪郭で表示し、
	/// メッシュ面のクリックで島をトグル選択できる(Alt はカメラ操作に譲る)。
	/// </summary>
	[CustomEditor(typeof(UVIslandMaskDeformer))]
	[CanEditMultipleObjects]
	public class UVIslandMaskDeformerEditor : DeformerBaseEditor
	{
		private static readonly int SceneControlHash = "NDMFDeformUVIslandScene".GetHashCode();
		private static readonly Color SelectedOutlineColor = new Color(0.25f, 1f, 0.45f, 0.9f);
		private static readonly Color HoverOutlineColor = new Color(1f, 0.9f, 0.2f, 0.95f);

		private static MethodInfo _intersectRayMesh;
		private static bool _intersectRayMeshSearched;

		private UVIslandAnalysis.Island _sceneHover;

		// 輪郭線分のキャッシュ(メッシュローカル空間。Handles.matrix で変換する)
		private Mesh _outlineMesh;
		private Vector3[] _outlineVertices;
		private int _outlineSelectionHash = -1;
		private Vector3[] _selectedSegments = System.Array.Empty<Vector3>();
		private UVIslandAnalysis.Island _hoverSegmentsIsland;
		private Vector3[] _hoverSegments = System.Array.Empty<Vector3>();

		// OnSceneGUI 内では Editor.targets を参照できない(Unity が警告する)ため、
		// 複数選択かどうかは OnEnable で確定させておく
		private bool _isMultiEdit;

		private void OnEnable()
		{
			_isMultiEdit = targets.Length > 1;
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			if (target is UVIslandMaskDeformer mask)
				UVIslandSelection.ClearHover(mask);
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
				"(選択島は緑、ホバー中の島は黄色の輪郭で表示)。\n" +
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
			var matrix = meshTransform.localToWorldMatrix;

			if (e.type == EventType.MouseMove)
			{
				var island = PickIsland(e.mousePosition, mesh, matrix, analysis);
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
				var island = PickIsland(e.mousePosition, mesh, matrix, analysis);
				if (island != null)
				{
					UVIslandSelection.Toggle(mask, analysis, island);
					e.Use();
				}
			}

			if (e.type == EventType.Repaint)
				DrawIslandOutlines(mask, mesh, matrix, analysis);
		}

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
		/// SkinnedMeshRenderer はベイク前の sharedMesh に対して判定する
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

		private void DrawIslandOutlines(
			UVIslandMaskDeformer mask, Mesh mesh, Matrix4x4 matrix, UVIslandAnalysis analysis)
		{
			if (_outlineMesh != mesh)
			{
				_outlineMesh = mesh;
				_outlineVertices = mesh.vertices;
				_outlineSelectionHash = -1;
				_hoverSegmentsIsland = null;
			}

			var selectionHash = mask.SelectionHash();
			if (_outlineSelectionHash != selectionHash)
			{
				_outlineSelectionHash = selectionHash;
				_selectedSegments = BuildSegments(mask.ResolveSelectedIslands(analysis));
			}

			var hover = _sceneHover;
			if (hover == null && UVIslandSelection.HoverDeformer == mask)
				hover = UVIslandSelection.HoverIsland;
			if (_hoverSegmentsIsland != hover)
			{
				_hoverSegmentsIsland = hover;
				_hoverSegments = hover != null
					? BuildSegments(new List<UVIslandAnalysis.Island> { hover })
					: System.Array.Empty<Vector3>();
			}

			if (_selectedSegments.Length == 0 && _hoverSegments.Length == 0)
				return;

			var previousZTest = Handles.zTest;
			using (new Handles.DrawingScope(matrix))
			{
				Handles.zTest = CompareFunction.LessEqual;
				if (_selectedSegments.Length > 0)
				{
					Handles.color = SelectedOutlineColor;
					Handles.DrawLines(_selectedSegments);
				}
				if (_hoverSegments.Length > 0)
				{
					Handles.color = HoverOutlineColor;
					Handles.DrawLines(_hoverSegments);
				}
			}
			Handles.zTest = previousZTest;
		}

		/// <summary>島の境界エッジをメッシュローカル空間の線分ペア配列にする</summary>
		private Vector3[] BuildSegments(List<UVIslandAnalysis.Island> islands)
		{
			var count = 0;
			foreach (var island in islands)
				count += island.BorderEdgeVerts.Count;
			if (count == 0 || _outlineVertices == null)
				return System.Array.Empty<Vector3>();

			var segments = new Vector3[count];
			var index = 0;
			foreach (var island in islands)
			{
				var verts = island.BorderEdgeVerts;
				for (var i = 0; i < verts.Count; i++)
				{
					var v = verts[i];
					if (v < 0 || v >= _outlineVertices.Length)
						continue;
					segments[index++] = _outlineVertices[v];
				}
			}

			if (index != segments.Length)
				System.Array.Resize(ref segments, index - index % 2);
			return segments;
		}
	}
}
