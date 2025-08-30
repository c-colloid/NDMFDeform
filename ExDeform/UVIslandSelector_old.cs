#if UNITY_EDITOR && FALSE // Disabled old class - use UVIslandMask + UVIslandAnalyzer instead
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Deform.Masking
{
    /// <summary>
    /// 自動UVアイランド検出・選択コンポーネント（Renderer対応版・改良版）
    /// </summary>
	[System.Serializable]
	[Deformer(Name = "UV Island Mask", Description = "Masks deformation based on UV island selection", Type = typeof(UVIslandSelector), Category = Category.Mask)]
	public class UVIslandSelector : Deformer
    {
        [Header("UV Island Selection Settings")]
        [SerializeField] private bool showUVMap = true;
        [SerializeField] private Texture2D uvMapTexture;
        [SerializeField] private List<int> selectedIslandIDs = new List<int>();
        
        [Header("Display Settings")]
        [SerializeField] private bool useAdaptiveVertexSize = true;
        [SerializeField, Range(0.001f, 0.1f)] private float manualVertexSphereSize = 0.01f;
        [SerializeField, Range(0.001f, 0.02f)] private float adaptiveSizeMultiplier = 0.007f;
        
        [Header("UV Map Preview Settings")]
        [SerializeField] private bool autoUpdatePreview = true;
        [SerializeField, Range(1f, 8f)] private float uvMapZoom = 1f;
        [SerializeField] private Vector2 uvMapPanOffset = Vector2.zero;
        [SerializeField] private bool enableMagnifyingGlass = true;
        [SerializeField, Range(2f, 10f)] private float magnifyingGlassZoom = 4f;
        [SerializeField, Range(50f, 200f)] private float magnifyingGlassSize = 100f;
        
        [Header("Selection Settings")]
        [SerializeField] private bool enableRangeSelection = true;
        private bool isRangeSelecting = false;
        private Vector2 rangeSelectionStart = Vector2.zero;
        private Vector2 rangeSelectionEnd = Vector2.zero;
        
        // UVアイランド情報を格納するクラス
        [System.Serializable]
        public class UVIsland
        {
            public int islandID;
            public List<int> vertexIndices = new List<int>();
            public List<int> triangleIndices = new List<int>(); // 面のインデックス（3つずつ）
            public List<Vector2> uvCoordinates = new List<Vector2>();
            public Bounds uvBounds;
            public Color maskColor = Color.red;
            public int faceCount => triangleIndices.Count / 3; // 面数
        }
        
        [SerializeField] private List<UVIsland> uvIslands = new List<UVIsland>();
        [SerializeField] private int[] vertexMask; // マスクされた頂点のインデックス
        [SerializeField] private int[] triangleMask; // マスクされた三角形のインデックス
        
        private Mesh targetMesh;
        private Renderer targetRenderer;
        
        // パフォーマンス最適化用キャッシュ
        private List<Vector3> cachedWorldPositions = new List<Vector3>();
        private bool worldPositionsCached = false;
        private float lastMeshScale = 1.0f;
        private float adaptiveVertexSphereSize = 0.01f;
        private bool meshBoundsCalculated = false;
        
        // パフォーマンス制御
        [SerializeField] private int maxDisplayVertices = 1000; // 表示する最大頂点数
        [SerializeField] private bool enablePerformanceOptimization = true;
        
        public List<UVIsland> UVIslands => uvIslands;
        public int[] VertexMask => vertexMask;
        public int[] TriangleMask => triangleMask;
        public bool HasSelectedIslands => selectedIslandIDs.Count > 0;
        public Mesh TargetMesh => targetMesh;
        public List<int> SelectedIslandIDs => selectedIslandIDs;
	    public Texture2D UvMapTexture => uvMapTexture;
        public float AdaptiveVertexSphereSize => useAdaptiveVertexSize ? adaptiveVertexSphereSize : manualVertexSphereSize;
        public int MaxDisplayVertices => maxDisplayVertices;
        public bool EnablePerformanceOptimization => enablePerformanceOptimization;
        public bool UseAdaptiveVertexSize => useAdaptiveVertexSize;
        public float ManualVertexSphereSize => manualVertexSphereSize;
        public bool AutoUpdatePreview => autoUpdatePreview;
        public float UvMapZoom => uvMapZoom;
        public Vector2 UvMapPanOffset => uvMapPanOffset;
        public bool EnableMagnifyingGlass => enableMagnifyingGlass;
        public float MagnifyingGlassZoom => magnifyingGlassZoom;
        public float MagnifyingGlassSize => magnifyingGlassSize;
        public bool EnableRangeSelection => enableRangeSelection;
        
	    public override DataFlags DataFlags => DataFlags.Vertices | DataFlags.UVs;
	    
	    public override void PreProcess()
	    {
	    	base.PreProcess();
	    }
	    
	    public override Unity.Jobs.JobHandle Process(MeshData data, JobHandle dependency = default)
	    {
	    	if (targetMesh != data.OriginalMesh)
	    	{
	    		targetMesh = data.OriginalMesh;
		    	UpdateMeshData();
	    	}
	    	return new UVIslandSelectorJob
	    	{
		    	currentVertices = data.DynamicNative.VertexBuffer,
		    	maskVertices = data.DynamicNative.MaskVertexBuffer
	    	}.Schedule (data.Length, 256, dependency);
	    }
	    
	    [BurstCompile (CompileSynchronously = COMPILE_SYNCHRONOUSLY)]
	    public struct UVIslandSelectorJob : IJobParallelFor
	    {
		    [ReadOnly]
		    public NativeArray<float3> currentVertices;
		    [WriteOnly]
		    public NativeArray<float3> maskVertices;

		    public void Execute (int index)
		    {
			    maskVertices[index] = currentVertices[index];
		    }
	    }
        
	    private void OnEnable()
	    {
		    Debug.Log("Initialize");
	        targetRenderer = GetComponent<Renderer>();
            UpdateMeshData();
        }
        
        /// <summary>
        /// メッシュデータを更新（MeshRenderer + SkinnedMeshRenderer対応）
        /// </summary>
        public void UpdateMeshData()
        {
            targetMesh = GetMeshFromRenderer();
            if (targetMesh != null)
            {
                CalculateAdaptiveVertexSphereSize();
                AnalyzeUVIslands();
            }
            // キャッシュをクリア
            InvalidateCache();
        }
        
        /// <summary>
        /// メッシュサイズに基づいて適応的な頂点球サイズを計算
        /// </summary>
        private void CalculateAdaptiveVertexSphereSize()
        {
            if (targetMesh == null) return;
            
            var bounds = targetMesh.bounds;
            var meshScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
            var maxBoundsSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            
            // メッシュサイズとスケールに基づいて球サイズを調整
            adaptiveVertexSphereSize = Mathf.Clamp(maxBoundsSize * meshScale * adaptiveSizeMultiplier, 0.001f, 0.05f);
            lastMeshScale = meshScale;
            meshBoundsCalculated = true;
            
            Debug.Log($"[UVIslandSelector] Adaptive vertex sphere size: {adaptiveVertexSphereSize:F4} (mesh bounds: {maxBoundsSize:F2}, scale: {meshScale:F2})");
        }
        
        /// <summary>
        /// キャッシュを無効化
        /// </summary>
        private void InvalidateCache()
        {
            worldPositionsCached = false;
            cachedWorldPositions.Clear();
        }
        
        /// <summary>
        /// Rendererからメッシュを取得（改良版）
        /// </summary>
        private Mesh GetMeshFromRenderer()
	    {
		    /*
            // SkinnedMeshRendererの場合
            if (targetRenderer is SkinnedMeshRenderer skinnedRenderer)
            {
                // ランタイムでの変形も考慮してsharedMeshを使用
                var mesh = skinnedRenderer.sharedMesh;
                if (mesh != null)
                {
                    // UV情報があることを確認
                    if (mesh.uv != null && mesh.uv.Length > 0)
                    {
                        Debug.Log($"[UVIslandSelector] SkinnedMeshRenderer mesh: {mesh.name}, UV count: {mesh.uv.Length}, Triangle count: {mesh.triangles.Length / 3}");
                        return mesh;
                    }
                    else
                    {
                        Debug.LogWarning($"[UVIslandSelector] SkinnedMeshRenderer mesh '{mesh.name}' has no UV data!");
                    }
                }
            }
            
            // MeshRendererの場合
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                var mesh = meshFilter.sharedMesh;
                if (mesh.uv != null && mesh.uv.Length > 0)
                {
                    Debug.Log($"[UVIslandSelector] MeshRenderer mesh: {mesh.name}, UV count: {mesh.uv.Length}, Triangle count: {mesh.triangles.Length / 3}");
                    return mesh;
                }
                else
                {
                    Debug.LogWarning($"[UVIslandSelector] MeshRenderer mesh '{mesh.name}' has no UV data!");
                }
            }
	        */
            
		    var mesh = targetMesh;
	        if (mesh.uv != null && mesh.uv.Length > 0)
	        {
		        Debug.Log($"[UVIslandSelector] MeshRenderer mesh: {mesh.name}, UV count: {mesh.uv.Length}, Triangle count: {mesh.triangles.Length / 3}");
		        return mesh;
	        }
	        else
	        {
		        Debug.LogWarning($"[UVIslandSelector] MeshRenderer mesh '{mesh.name}' has no UV data!");
	        }
            
            Debug.LogWarning("[UVIslandSelector] No valid mesh found or mesh has no UV data!");
            return null;
        }
        
        /// <summary>
        /// UVアイランドを解析（面情報も含む・改良版）
        /// </summary>
        private void AnalyzeUVIslands()
        {
            if (targetMesh == null) return;
            
            uvIslands.Clear();
            var vertices = targetMesh.vertices;
            var uvs = targetMesh.uv;
            var triangles = targetMesh.triangles;
            
            if (uvs.Length == 0)
            {
                Debug.LogWarning("[UVIslandSelector] Mesh has no UV coordinates!");
                return;
            }
            
            Debug.Log($"[UVIslandSelector] Analyzing mesh: {vertices.Length} vertices, {uvs.Length} UVs, {triangles.Length/3} triangles");
            
            // 三角形ごとの隣接関係を構築
            var triangleAdjacency = BuildTriangleAdjacency(triangles, uvs);
            
            // UVアイランド検出
            var visitedTriangles = new HashSet<int>();
            var islandCounter = 0;
            
            for (int triIndex = 0; triIndex < triangles.Length; triIndex += 3)
            {
                int triangleID = triIndex / 3;
                if (visitedTriangles.Contains(triangleID)) continue;
                
                var island = new UVIsland { islandID = islandCounter++ };
                var triangleQueue = new Queue<int>();
                triangleQueue.Enqueue(triangleID);
                visitedTriangles.Add(triangleID);
                
                // Flood fillアルゴリズムでアイランドを検出
                while (triangleQueue.Count > 0)
                {
                    int currentTriangleID = triangleQueue.Dequeue();
                    int baseIndex = currentTriangleID * 3;
                    
                    // 三角形の頂点を追加
                    for (int i = 0; i < 3; i++)
                    {
                        int vertexIndex = triangles[baseIndex + i];
                        if (!island.vertexIndices.Contains(vertexIndex))
                        {
                            island.vertexIndices.Add(vertexIndex);
                            if (vertexIndex < uvs.Length)
                            {
                                island.uvCoordinates.Add(uvs[vertexIndex]);
                            }
                        }
                        island.triangleIndices.Add(vertexIndex);
                    }
                    
                    // 隣接する三角形を探す
                    if (triangleAdjacency.ContainsKey(currentTriangleID))
                    {
                        foreach (var adjacentTriangle in triangleAdjacency[currentTriangleID])
                        {
                            if (!visitedTriangles.Contains(adjacentTriangle))
                            {
                                visitedTriangles.Add(adjacentTriangle);
                                triangleQueue.Enqueue(adjacentTriangle);
                            }
                        }
                    }
                }
                
                // UVバウンズを計算
                CalculateUVBounds(island);
                
                // アイランドの色を生成
                island.maskColor = GenerateIslandColor(island.islandID);
                
                uvIslands.Add(island);
            }
            
            Debug.Log($"[UVIslandSelector] Found {uvIslands.Count} UV islands");
            UpdateMasks();
        }
        
        /// <summary>
        /// 三角形の隣接関係を構築（改良版）
        /// </summary>
        private Dictionary<int, HashSet<int>> BuildTriangleAdjacency(int[] triangles, Vector2[] uvs)
        {
            var adjacency = new Dictionary<int, HashSet<int>>();
            const float UV_EPSILON = 0.001f; // より厳格な閾値
            
            int triangleCount = triangles.Length / 3;
            
            // 各三角形の隣接リストを初期化
            for (int i = 0; i < triangleCount; i++)
            {
                adjacency[i] = new HashSet<int>();
            }
            
            // エッジベースの隣接判定（より効率的）
            var edgeMap = new Dictionary<string, List<int>>();
            
            for (int i = 0; i < triangleCount; i++)
            {
                int baseIndex = i * 3;
                for (int j = 0; j < 3; j++)
                {
                    int v1 = triangles[baseIndex + j];
                    int v2 = triangles[baseIndex + (j + 1) % 3];
                    
                    if (v1 < uvs.Length && v2 < uvs.Length)
                    {
                        var uv1 = uvs[v1];
                        var uv2 = uvs[v2];
                        
                        // エッジのキーを作成（UV座標ベース）
                        string edgeKey = CreateEdgeKey(uv1, uv2, UV_EPSILON);
                        
                        if (!edgeMap.ContainsKey(edgeKey))
                        {
                            edgeMap[edgeKey] = new List<int>();
                        }
                        edgeMap[edgeKey].Add(i);
                    }
                }
            }
            
            // エッジを共有する三角形同士を隣接として登録
            foreach (var triangleList in edgeMap.Values)
            {
                for (int i = 0; i < triangleList.Count; i++)
                {
                    for (int j = i + 1; j < triangleList.Count; j++)
                    {
                        adjacency[triangleList[i]].Add(triangleList[j]);
                        adjacency[triangleList[j]].Add(triangleList[i]);
                    }
                }
            }
            
            return adjacency;
        }
        
        /// <summary>
        /// UVエッジのキーを作成
        /// </summary>
        private string CreateEdgeKey(Vector2 uv1, Vector2 uv2, float epsilon)
        {
            // 座標を丸めてキーを作成
            int x1 = Mathf.RoundToInt(uv1.x / epsilon);
            int y1 = Mathf.RoundToInt(uv1.y / epsilon);
            int x2 = Mathf.RoundToInt(uv2.x / epsilon);
            int y2 = Mathf.RoundToInt(uv2.y / epsilon);
            
            // 順序を正規化
            if (x1 > x2 || (x1 == x2 && y1 > y2))
            {
                return $"{x2},{y2}-{x1},{y1}";
            }
            else
            {
                return $"{x1},{y1}-{x2},{y2}";
            }
        }
        
        /// <summary>
        /// UVアイランドのバウンズを計算
        /// </summary>
        private void CalculateUVBounds(UVIsland island)
        {
            if (island.uvCoordinates.Count == 0) return;
            
            var min = island.uvCoordinates[0];
            var max = island.uvCoordinates[0];
            
            foreach (var uv in island.uvCoordinates)
            {
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }
            
            island.uvBounds = new Bounds(
                new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0),
                new Vector3(max.x - min.x, max.y - min.y, 0)
            );
        }
        
        /// <summary>
        /// アイランドの色を生成
        /// </summary>
        private Color GenerateIslandColor(int islandID)
        {
            // HSVベースの色生成で見分けやすい色を作る
            float hue = (islandID * 0.618034f) % 1.0f; // 黄金比で分散
            return Color.HSVToRGB(hue, 0.7f, 0.9f);
        }
        
        /// <summary>
        /// アイランドを選択/選択解除
        /// </summary>
        public void ToggleIslandSelection(int islandID)
        {
            if (selectedIslandIDs.Contains(islandID))
            {
                selectedIslandIDs.Remove(islandID);
            }
            else
            {
                selectedIslandIDs.Add(islandID);
            }
            
            UpdateMasks();
            
            // 自動更新が有効な場合はプレビューを更新
            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }
        
        /// <summary>
        /// 範囲選択を開始
        /// </summary>
        public void StartRangeSelection(Vector2 startPoint)
        {
            if (!enableRangeSelection) return;
            
            isRangeSelecting = true;
            rangeSelectionStart = startPoint;
            rangeSelectionEnd = startPoint;
        }
        
        /// <summary>
        /// 範囲選択を更新
        /// </summary>
        public void UpdateRangeSelection(Vector2 currentPoint)
        {
            if (!isRangeSelecting) return;
            
            rangeSelectionEnd = currentPoint;
        }
        
        /// <summary>
        /// 範囲選択を完了
        /// </summary>
        public void FinishRangeSelection(bool addToSelection = false, bool removeFromSelection = false)
        {
            if (!isRangeSelecting) return;
            
            var selectionRect = new Rect(
                Mathf.Min(rangeSelectionStart.x, rangeSelectionEnd.x),
                Mathf.Min(rangeSelectionStart.y, rangeSelectionEnd.y),
                Mathf.Abs(rangeSelectionEnd.x - rangeSelectionStart.x),
                Mathf.Abs(rangeSelectionEnd.y - rangeSelectionStart.y)
            );
            
            var islandsInRange = GetIslandsInRect(selectionRect);
            
            if (removeFromSelection)
            {
                // Ctrl+Shift: 範囲内のアイランドを選択から除外
                foreach (var islandID in islandsInRange)
                {
                    selectedIslandIDs.Remove(islandID);
                }
                Debug.Log($"[UVIslandSelector] Removed {islandsInRange.Count} islands from selection");
            }
            else if (addToSelection)
            {
                // Shift: 範囲内のアイランドを追加選択
                foreach (var islandID in islandsInRange)
                {
                    if (!selectedIslandIDs.Contains(islandID))
                    {
                        selectedIslandIDs.Add(islandID);
                    }
                }
                Debug.Log($"[UVIslandSelector] Added {islandsInRange.Count} islands to selection");
            }
            else
            {
                // 通常選択: 範囲内のアイランドのみを選択
                selectedIslandIDs.Clear();
                selectedIslandIDs.AddRange(islandsInRange);
                Debug.Log($"[UVIslandSelector] Selected {islandsInRange.Count} islands");
            }
            
            isRangeSelecting = false;
            UpdateMasks();
            
            // 自動更新が有効な場合はプレビューを更新
            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }
        
        /// <summary>
        /// 指定された矩形内にあるUVアイランドを取得
        /// </summary>
        public List<int> GetIslandsInRect(Rect rect)
        {
            var islandsInRect = new List<int>();
            
            foreach (var island in uvIslands)
            {
                var bounds = island.uvBounds;
                var islandRect = new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
                
                // 矩形が重複しているかチェック
                if (rect.Overlaps(islandRect))
                {
                    islandsInRect.Add(island.islandID);
                }
            }
            
            return islandsInRect;
        }
        
        /// <summary>
        /// 範囲選択の状態を取得
        /// </summary>
        public bool IsRangeSelecting => isRangeSelecting;
        
        /// <summary>
        /// 現在の範囲選択矩形を取得
        /// </summary>
        public Rect GetCurrentSelectionRect()
        {
            if (!isRangeSelecting) return new Rect();
            
            return new Rect(
                Mathf.Min(rangeSelectionStart.x, rangeSelectionEnd.x),
                Mathf.Min(rangeSelectionStart.y, rangeSelectionEnd.y),
                Mathf.Abs(rangeSelectionEnd.x - rangeSelectionStart.x),
                Mathf.Abs(rangeSelectionEnd.y - rangeSelectionStart.y)
            );
        }
        
        /// <summary>
        /// UV座標からアイランドを検索（変換行列対応）
        /// </summary>
        public int GetIslandAtUVCoordinate(Vector2 uvCoord)
        {
            // 変換行列を適用して実際のUV座標を取得
            var transform = CalculateUVTransformMatrix();
            var inverseTransform = transform.inverse;
            var actualUV = inverseTransform.MultiplyPoint3x4(new Vector3(uvCoord.x, uvCoord.y, 0f));
            var actualUVCoord = new Vector2(actualUV.x, actualUV.y);
            
            // デバッグ情報
            Debug.Log($"[UVIslandSelector] Input UV: {uvCoord}, Transformed UV: {actualUVCoord}, Zoom: {uvMapZoom}, Pan: {uvMapPanOffset}");
            
            foreach (var island in uvIslands)
            {
                if (island.uvBounds.Contains(new Vector3(actualUVCoord.x, actualUVCoord.y, 0)))
                {
                    // より詳細な判定（三角形ベース）
                    if (IsPointInUVIsland(actualUVCoord, island))
                    {
                        Debug.Log($"[UVIslandSelector] Found island {island.islandID} at {actualUVCoord}");
                        return island.islandID;
                    }
                }
            }
            Debug.Log($"[UVIslandSelector] No island found at {actualUVCoord}");
            return -1;
        }
        
        /// <summary>
        /// スクリーン座標からUV座標に変換（変換行列考慮）
        /// </summary>
        public Vector2 ScreenToUVCoordinate(Vector2 screenCoord, int screenWidth, int screenHeight)
        {
            // スクリーン座標を0-1の範囲に正規化
            var normalizedScreenCoord = new Vector2(
                screenCoord.x / screenWidth,
                screenCoord.y / screenHeight
            );
            
            // 変換行列を適用して実際のUV座標を取得
            var transform = CalculateUVTransformMatrix();
            var inverseTransform = transform.inverse;
            var actualUV = inverseTransform.MultiplyPoint3x4(new Vector3(normalizedScreenCoord.x, normalizedScreenCoord.y, 0f));
            
            return new Vector2(actualUV.x, actualUV.y);
        }
        
        /// <summary>
        /// 点がUVアイランド内にあるかチェック（三角形ベース）
        /// </summary>
        private bool IsPointInUVIsland(Vector2 point, UVIsland island)
        {
            var uvs = targetMesh.uv;
            
            // アイランドの各三角形をチェック
            for (int i = 0; i < island.triangleIndices.Count; i += 3)
            {
                if (i + 2 >= island.triangleIndices.Count) break;
                
                var v0Index = island.triangleIndices[i];
                var v1Index = island.triangleIndices[i + 1];
                var v2Index = island.triangleIndices[i + 2];
                
                if (v0Index < uvs.Length && v1Index < uvs.Length && v2Index < uvs.Length)
                {
                    var uv0 = uvs[v0Index];
                    var uv1 = uvs[v1Index];
                    var uv2 = uvs[v2Index];
                    
                    if (IsPointInTriangle(point, uv0, uv1, uv2))
                    {
                        return true;
                    }
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// 点が三角形内にあるかチェック（重心座標使用）
        /// </summary>
        private bool IsPointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = c - a;
            Vector2 v1 = b - a;
            Vector2 v2 = point - a;
            
            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);
            
            float invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
            
            return (u >= 0) && (v >= 0) && (u + v <= 1);
        }
        
        /// <summary>
        /// 頂点マスクと三角形マスクを更新
        /// </summary>
        private void UpdateMasks()
        {
            var maskedVertices = new List<int>();
            var maskedTriangles = new List<int>();
            
            foreach (var islandID in selectedIslandIDs)
            {
                var island = uvIslands.FirstOrDefault(i => i.islandID == islandID);
                if (island != null)
                {
                    maskedVertices.AddRange(island.vertexIndices);
                    maskedTriangles.AddRange(island.triangleIndices);
                }
            }
            
            vertexMask = maskedVertices.Distinct().ToArray();
            triangleMask = maskedTriangles.ToArray();
            
            // 選択が変わったらキャッシュを無効化
            InvalidateCache();
        }
        
        /// <summary>
        /// 選択されたアイランドをクリア
        /// </summary>
        public void ClearSelection()
        {
            selectedIslandIDs.Clear();
            UpdateMasks();
        }
        
        /// <summary>
        /// UVマップテクスチャを生成（ワイヤーフレーム付き・ズーム対応）
        /// </summary>
        public Texture2D GenerateUVMapTexture(int width = 512, int height = 512)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            
            // 背景を暗いグレーで埋める
            var backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1.0f);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = backgroundColor;
            }
            
            // ズームとパンオフセットを適用
            var transformMatrix = CalculateUVTransformMatrix();
            
            // UVグリッドを描画
            DrawUVGridWithTransform(pixels, width, height, transformMatrix);
            
            // UVアイランドを描画
            foreach (var island in uvIslands)
            {
                var color = selectedIslandIDs.Contains(island.islandID) ? 
                            island.maskColor : new Color(0.5f, 0.5f, 0.5f, 0.6f);
                
                DrawUVIslandToTextureWithTransform(island, pixels, width, height, color, transformMatrix);
            }
            
            // ワイヤーフレームを描画
            foreach (var island in uvIslands)
            {
                var wireframeColor = selectedIslandIDs.Contains(island.islandID) ? 
                                   Color.white : new Color(0.8f, 0.8f, 0.8f, 1.0f);
                
                DrawUVIslandWireframeWithTransform(island, pixels, width, height, wireframeColor, transformMatrix);
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            uvMapTexture = texture;
            
            return texture;
        }
        
        /// <summary>
        /// UV座標変換行列を計算（正規化された座標系）
        /// </summary>
        public Matrix4x4 CalculateUVTransformMatrix()
        {
            // 中央を原点とした正規化座標系でズーム/パンを処理
            var centerOffset = Matrix4x4.Translate(new Vector3(-0.5f, -0.5f, 0f));
            var scaleMatrix = Matrix4x4.Scale(new Vector3(uvMapZoom, uvMapZoom, 1f));
            var panMatrix = Matrix4x4.Translate(new Vector3(uvMapPanOffset.x, uvMapPanOffset.y, 0f));
            var recenterMatrix = Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0f));
            
            return recenterMatrix * panMatrix * scaleMatrix * centerOffset;
        }
        
        /// <summary>
        /// UV座標を画面座標に変換（変換行列適用）
        /// </summary>
        private Vector2 TransformUVToScreen(Vector2 uv, int width, int height, Matrix4x4 transform)
        {
            var transformedUV = transform.MultiplyPoint3x4(new Vector3(uv.x, uv.y, 0f));
            return new Vector2(transformedUV.x * width, transformedUV.y * height);
        }
        
        /// <summary>
        /// UVグリッドを描画（変換行列対応・改良版）
        /// </summary>
        private void DrawUVGridWithTransform(Color[] pixels, int width, int height, Matrix4x4 transform)
        {
            var gridColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            var borderColor = new Color(0.4f, 0.4f, 0.4f, 1.0f);
            var subGridColor = new Color(0.2f, 0.2f, 0.2f, 1.0f);
            
            // ズームレベルに応じてグリッド密度を調整
            float gridStep = 0.1f; // 基本グリッド間隔
            if (uvMapZoom > 4f)
            {
                gridStep = 0.05f; // より細かいグリッド
            }
            else if (uvMapZoom > 2f)
            {
                gridStep = 0.1f; // 標準グリッド
            }
            
            // グリッド線を描画
            for (float gridPos = 0f; gridPos <= 1f; gridPos += gridStep)
            {
                bool isMajorGrid = Mathf.Abs(gridPos % 0.1f) < 0.001f;
                bool isBorder = gridPos == 0f || gridPos == 1f;
                
                Color lineColor = isBorder ? borderColor : (isMajorGrid ? gridColor : subGridColor);
                
                // 縦線
                var startV = TransformUVToScreen(new Vector2(gridPos, 0f), width, height, transform);
                var endV = TransformUVToScreen(new Vector2(gridPos, 1f), width, height, transform);
                
                if (IsLineVisible(startV, endV, width, height))
                {
                    DrawLineScreenSpace(startV, endV, pixels, width, height, lineColor);
                }
                
                // 横線
                var startH = TransformUVToScreen(new Vector2(0f, gridPos), width, height, transform);
                var endH = TransformUVToScreen(new Vector2(1f, gridPos), width, height, transform);
                
                if (IsLineVisible(startH, endH, width, height))
                {
                    DrawLineScreenSpace(startH, endH, pixels, width, height, lineColor);
                }
            }
        }
        
        /// <summary>
        /// 線が画面内に表示されるかチェック
        /// </summary>
        private bool IsLineVisible(Vector2 start, Vector2 end, int width, int height)
        {
            // 両端点が画面外にある場合でも、線が画面を通過する可能性があるため
            // 簡易的な境界チェック
            return !(start.x < -10 && end.x < -10) &&
                   !(start.x > width + 10 && end.x > width + 10) &&
                   !(start.y < -10 && end.y < -10) &&
                   !(start.y > height + 10 && end.y > height + 10);
        }
        
        /// <summary>
        /// UVグリッドを描画（従来版）
        /// </summary>
        private void DrawUVGrid(Color[] pixels, int width, int height)
        {
            var gridColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            
            // 縦線
            for (int x = 0; x < width; x += width / 10)
            {
                for (int y = 0; y < height; y++)
                {
                    if (x < width)
                    {
                        int index = y * width + x;
                        pixels[index] = gridColor;
                    }
                }
            }
            
            // 横線
            for (int y = 0; y < height; y += height / 10)
            {
                for (int x = 0; x < width; x++)
                {
                    if (y < height)
                    {
                        int index = y * width + x;
                        pixels[index] = gridColor;
                    }
                }
            }
            
            // 境界線（太い線）
            var borderColor = new Color(0.4f, 0.4f, 0.4f, 1.0f);
            
            // 上下の境界
            for (int x = 0; x < width; x++)
            {
                pixels[x] = borderColor; // 上端
                pixels[(height - 1) * width + x] = borderColor; // 下端
            }
            
            // 左右の境界
            for (int y = 0; y < height; y++)
            {
                pixels[y * width] = borderColor; // 左端
                pixels[y * width + (width - 1)] = borderColor; // 右端
            }
        }
        
        /// <summary>
        /// UVアイランドをテクスチャに描画（三角形ベース・変換行列対応）
        /// </summary>
        private void DrawUVIslandToTextureWithTransform(UVIsland island, Color[] pixels, 
                                         int width, int height, Color color, Matrix4x4 transform)
        {
            var uvs = targetMesh.uv;
            
            // 各三角形を描画
            for (int i = 0; i < island.triangleIndices.Count; i += 3)
            {
                if (i + 2 >= island.triangleIndices.Count) break;
                
                var v0Index = island.triangleIndices[i];
                var v1Index = island.triangleIndices[i + 1];
                var v2Index = island.triangleIndices[i + 2];
                
                if (v0Index < uvs.Length && v1Index < uvs.Length && v2Index < uvs.Length)
                {
                    var uv0 = uvs[v0Index];
                    var uv1 = uvs[v1Index];
                    var uv2 = uvs[v2Index];
                    
                    // 変換行列を適用して描画
                    var screen0 = TransformUVToScreen(uv0, width, height, transform);
                    var screen1 = TransformUVToScreen(uv1, width, height, transform);
                    var screen2 = TransformUVToScreen(uv2, width, height, transform);
                    
                    DrawTriangleScreenSpace(screen0, screen1, screen2, pixels, width, height, color);
                }
            }
        }
        
        /// <summary>
        /// UVアイランドをテクスチャに描画（三角形ベース・従来版）
        /// </summary>
        private void DrawUVIslandToTexture(UVIsland island, Color[] pixels, 
                                         int width, int height, Color color)
        {
            var uvs = targetMesh.uv;
            
            // 各三角形を描画
            for (int i = 0; i < island.triangleIndices.Count; i += 3)
            {
                if (i + 2 >= island.triangleIndices.Count) break;
                
                var v0Index = island.triangleIndices[i];
                var v1Index = island.triangleIndices[i + 1];
                var v2Index = island.triangleIndices[i + 2];
                
                if (v0Index < uvs.Length && v1Index < uvs.Length && v2Index < uvs.Length)
                {
                    var uv0 = uvs[v0Index];
                    var uv1 = uvs[v1Index];
                    var uv2 = uvs[v2Index];
                    
                    DrawTriangleToTexture(uv0, uv1, uv2, pixels, width, height, color);
                }
            }
        }
        
        /// <summary>
        /// 三角形をスクリーンスペースで描画
        /// </summary>
        private void DrawTriangleScreenSpace(Vector2 screen0, Vector2 screen1, Vector2 screen2,
                                         Color[] pixels, int width, int height, Color color)
        {
            // スクリーン座標での三角形の境界ボックスを計算
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(screen0.x, Mathf.Min(screen1.x, screen2.x))));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(screen0.x, Mathf.Max(screen1.x, screen2.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(screen0.y, Mathf.Min(screen1.y, screen2.y))));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(screen0.y, Mathf.Max(screen1.y, screen2.y))));
            
            // 境界ボックス内の各ピクセルをチェック
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 point = new Vector2(x, y);
                    
                    if (IsPointInTriangleScreenSpace(point, screen0, screen1, screen2))
                    {
                        int index = y * width + x;
                        if (index >= 0 && index < pixels.Length)
                        {
                            pixels[index] = Color.Lerp(pixels[index], color, 0.6f);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// スクリーンスペースでの点と三角形の内外判定
        /// </summary>
        private bool IsPointInTriangleScreenSpace(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = c - a;
            Vector2 v1 = b - a;
            Vector2 v2 = point - a;
            
            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);
            
            float denom = dot00 * dot11 - dot01 * dot01;
            if (Mathf.Abs(denom) < 1e-6f) return false;
            
            float invDenom = 1 / denom;
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
            
            return (u >= 0) && (v >= 0) && (u + v <= 1);
        }
        
        /// <summary>
        /// スクリーンスペースで線を描画
        /// </summary>
        private void DrawLineScreenSpace(Vector2 start, Vector2 end, Color[] pixels, int width, int height, Color color)
        {
            int x0 = Mathf.RoundToInt(start.x);
            int y0 = Mathf.RoundToInt(start.y);
            int x1 = Mathf.RoundToInt(end.x);
            int y1 = Mathf.RoundToInt(end.y);
            
            // 範囲チェック
            if ((x0 < 0 || x0 >= width || y0 < 0 || y0 >= height) &&
                (x1 < 0 || x1 >= width || y1 < 0 || y1 >= height))
                return;
                
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            
            while (true)
            {
                // ピクセルを描画（範囲内のみ）
                if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                {
                    int index = y0 * width + x0;
                    if (index >= 0 && index < pixels.Length)
                    {
                        pixels[index] = Color.Lerp(pixels[index], color, 0.9f);
                    }
                }
                
                if (x0 == x1 && y0 == y1) break;
                
                int e2 = 2 * err;
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
        
        /// <summary>
        /// 三角形をテクスチャに描画（従来版）
        /// </summary>
        private void DrawTriangleToTexture(Vector2 uv0, Vector2 uv1, Vector2 uv2,
                                         Color[] pixels, int width, int height, Color color)
        {
            // 三角形の境界ボックスを計算
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(uv0.x, Mathf.Min(uv1.x, uv2.x)) * width));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(uv0.x, Mathf.Max(uv1.x, uv2.x)) * width));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(uv0.y, Mathf.Min(uv1.y, uv2.y)) * height));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(uv0.y, Mathf.Max(uv1.y, uv2.y)) * height));
            
            // 境界ボックス内の各ピクセルをチェック
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 point = new Vector2(x / (float)width, y / (float)height);
                    
                    if (IsPointInTriangle(point, uv0, uv1, uv2))
                    {
                        int index = y * width + x;
                        pixels[index] = Color.Lerp(pixels[index], color, 0.6f);
                    }
                }
            }
        }
        
        /// <summary>
        /// UVアイランドのワイヤーフレームを描画（変換行列対応）
        /// </summary>
        private void DrawUVIslandWireframeWithTransform(UVIsland island, Color[] pixels, int width, int height, Color color, Matrix4x4 transform)
        {
            var uvs = targetMesh.uv;
            
            // 各三角形のエッジを描画
            for (int i = 0; i < island.triangleIndices.Count; i += 3)
            {
                if (i + 2 >= island.triangleIndices.Count) break;
                
                var v0Index = island.triangleIndices[i];
                var v1Index = island.triangleIndices[i + 1];
                var v2Index = island.triangleIndices[i + 2];
                
                if (v0Index < uvs.Length && v1Index < uvs.Length && v2Index < uvs.Length)
                {
                    var uv0 = uvs[v0Index];
                    var uv1 = uvs[v1Index];
                    var uv2 = uvs[v2Index];
                    
                    // 変換行列を適用してスクリーン座標に変換
                    var screen0 = TransformUVToScreen(uv0, width, height, transform);
                    var screen1 = TransformUVToScreen(uv1, width, height, transform);
                    var screen2 = TransformUVToScreen(uv2, width, height, transform);
                    
                    // 3本のエッジを描画
                    DrawLineScreenSpace(screen0, screen1, pixels, width, height, color);
                    DrawLineScreenSpace(screen1, screen2, pixels, width, height, color);
                    DrawLineScreenSpace(screen2, screen0, pixels, width, height, color);
                }
            }
        }
        
        /// <summary>
        /// UVアイランドのワイヤーフレームを描画（従来版）
        /// </summary>
        private void DrawUVIslandWireframe(UVIsland island, Color[] pixels, int width, int height, Color color)
        {
            var uvs = targetMesh.uv;
            
            // 各三角形のエッジを描画
            for (int i = 0; i < island.triangleIndices.Count; i += 3)
            {
                if (i + 2 >= island.triangleIndices.Count) break;
                
                var v0Index = island.triangleIndices[i];
                var v1Index = island.triangleIndices[i + 1];
                var v2Index = island.triangleIndices[i + 2];
                
                if (v0Index < uvs.Length && v1Index < uvs.Length && v2Index < uvs.Length)
                {
                    var uv0 = uvs[v0Index];
                    var uv1 = uvs[v1Index];
                    var uv2 = uvs[v2Index];
                    
                    // 3本のエッジを描画
                    DrawLine(uv0, uv1, pixels, width, height, color);
                    DrawLine(uv1, uv2, pixels, width, height, color);
                    DrawLine(uv2, uv0, pixels, width, height, color);
                }
            }
        }
        
        /// <summary>
        /// UV座標上に線を描画（Bresenhamアルゴリズム）
        /// </summary>
        private void DrawLine(Vector2 start, Vector2 end, Color[] pixels, int width, int height, Color color)
        {
            int x0 = Mathf.RoundToInt(start.x * width);
            int y0 = Mathf.RoundToInt(start.y * height);
            int x1 = Mathf.RoundToInt(end.x * width);
            int y1 = Mathf.RoundToInt(end.y * height);
            
            // 範囲チェック
            if ((x0 < 0 || x0 >= width || y0 < 0 || y0 >= height) &&
                (x1 < 0 || x1 >= width || y1 < 0 || y1 >= height))
                return;
                
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            
            while (true)
            {
                // ピクセルを描画（範囲内のみ）
                if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                {
                    int index = y0 * width + x0;
                    pixels[index] = Color.Lerp(pixels[index], color, 0.9f);
                }
                
                if (x0 == x1 && y0 == y1) break;
                
                int e2 = 2 * err;
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
        
        /// <summary>
        /// シーンビューでの面ハイライト用の情報を取得（キャッシュ対応・パフォーマンス最適化版）
        /// </summary>
        public List<Vector3> GetSelectedTrianglesWorldPositions()
        {
            if (targetMesh == null || triangleMask == null || triangleMask.Length == 0)
                return new List<Vector3>();
            
            // スケールが変わっていたらキャッシュを無効化
            var currentMeshScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
            if (Mathf.Abs(currentMeshScale - lastMeshScale) > 0.001f)
            {
                InvalidateCache();
                lastMeshScale = currentMeshScale;
            }
            
            // キャッシュが有効ならそれを返す
            if (worldPositionsCached && cachedWorldPositions.Count > 0)
            {
                return cachedWorldPositions;
            }
            
            // キャッシュが無効なので再計算
            var vertices = targetMesh.vertices;
            cachedWorldPositions.Clear();
            
            int addedCount = 0;
            foreach (int vertexIndex in triangleMask)
            {
                if (vertexIndex < vertices.Length)
                {
                    cachedWorldPositions.Add(transform.TransformPoint(vertices[vertexIndex]));
                    addedCount++;
                    
                    // パフォーマンス最適化：最大表示数に達したら停止
                    if (enablePerformanceOptimization && addedCount >= maxDisplayVertices)
                    {
                        Debug.Log($"[UVIslandSelector] Performance optimization: Limited vertex display to {maxDisplayVertices} vertices");
                        break;
                    }
                }
            }
            
            worldPositionsCached = true;
            return cachedWorldPositions;
        }
        
        /// <summary>
        /// 既存のUVIslandMaskとの連携用：選択ポイントのリストを生成
        /// </summary>
        public List<Vector2> GenerateSelectionPointsForDeformer()
        {
            var points = new List<Vector2>();
            
            foreach (var islandID in selectedIslandIDs)
            {
                var island = uvIslands.FirstOrDefault(i => i.islandID == islandID);
                if (island != null)
                {
                    // アイランドの境界を近似ポイントとして追加
                    points.AddRange(ApproximateIslandBoundary(island));
                }
            }
            
            return points;
        }
        
        /// <summary>
        /// アイランドの境界を近似ポイントで表現
        /// </summary>
        private List<Vector2> ApproximateIslandBoundary(UVIsland island)
        {
            // 簡易実装：バウンディングボックスの角
            var bounds = island.uvBounds;
            return new List<Vector2>
            {
                new Vector2(bounds.min.x, bounds.min.y),
                new Vector2(bounds.max.x, bounds.min.y),
                new Vector2(bounds.max.x, bounds.max.y),
                new Vector2(bounds.min.x, bounds.max.y)
            };
        }
        
        /// <summary>
        /// ズームレベルを設定
        /// </summary>
        public void SetZoomLevel(float zoom)
        {
            uvMapZoom = Mathf.Clamp(zoom, 1f, 8f);
            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }
        
        /// <summary>
        /// パンオフセットを設定（範囲制限付き）
        /// </summary>
        public void SetPanOffset(Vector2 offset)
        {
            // パン範囲を制限（ズームレベルに応じて調整）
            float maxOffset = (uvMapZoom - 1f) * 0.5f;
            uvMapPanOffset = new Vector2(
                Mathf.Clamp(offset.x, -maxOffset, maxOffset),
                Mathf.Clamp(offset.y, -maxOffset, maxOffset)
            );
            
            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }
        
        /// <summary>
        /// ズームとパンをリセット（中央表示保証）
        /// </summary>
        public void ResetViewTransform()
        {
            uvMapZoom = 1f;
            uvMapPanOffset = Vector2.zero;
            
            // デバッグログで確認
            Debug.Log($"[UVIslandSelector] View reset - Zoom: {uvMapZoom}, Pan: {uvMapPanOffset}");
            
            if (autoUpdatePreview)
            {
                GenerateUVMapTexture();
            }
        }
        
        /// <summary>
        /// 現在のビュー変換状態をログ出力（デバッグ用）
        /// </summary>
        public void LogViewTransform()
        {
            var transform = CalculateUVTransformMatrix();
            Debug.Log($"[UVIslandSelector] Current transform matrix: {transform}");
            Debug.Log($"[UVIslandSelector] Zoom: {uvMapZoom}, Pan: {uvMapPanOffset}");
        }
        
        /// <summary>
        /// 指定された点を中心にズーム（改善版）
        /// </summary>
        public void ZoomAtPoint(Vector2 point, float zoomDelta)
        {
            float oldZoom = uvMapZoom;
            float newZoom = Mathf.Clamp(uvMapZoom + zoomDelta, 1f, 8f);
            
            if (Mathf.Abs(newZoom - oldZoom) > 0.01f)
            {
                // 中央基準の座標系でズーム処理
                Vector2 centerPoint = point - Vector2.one * 0.5f; // 中央を原点とする
                
                // ズーム比を計算
                float zoomRatio = newZoom / oldZoom;
                
                // パンオフセットをズーム中心に合わせて調整
                Vector2 oldOffset = uvMapPanOffset;
                Vector2 newOffset = (oldOffset - centerPoint) * zoomRatio + centerPoint;
                
                uvMapZoom = newZoom;
                SetPanOffset(newOffset); // 範囲制限付きで設定
                
                if (autoUpdatePreview)
                {
                    GenerateUVMapTexture();
                }
            }
        }
        
        /// <summary>
        /// UV座標でのルーペテクスチャを生成（高詳細版）
        /// </summary>
        public Texture2D GenerateMagnifyingGlassTexture(Vector2 centerUV, int size = 100)
        {
            if (!enableMagnifyingGlass) return null;
            
            // ルーペ専用の高解像度テクスチャを生成
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            
            // centerUVはLocalPosToUV変換済みの座標として受け取る
            Debug.Log($"[Magnifying Glass] Center UV (LocalPosToUV): {centerUV}, Zoom: {uvMapZoom}, Pan: {uvMapPanOffset}");
            
            // ルーペ範囲を計算（現在のズームレベルを考慮）
            float baseRadius = 0.5f / magnifyingGlassZoom;
            float zoomAdjustment = Mathf.Max(1f, uvMapZoom * 0.5f); // メインズームに応じて調整
            float lupeRadius = baseRadius / zoomAdjustment;
            
            // ルーペ用の変換行列を作成（高詳細用）
            var lupeTransform = CreateMagnifyingTransformMatrix(centerUV, lupeRadius);
            
            // 背景を暗いグレーで埋める
            var backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1.0f);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = backgroundColor;
            }
            
            // 高詳細グリッドを描画
            DrawMagnifyingGridWithTransform(pixels, size, size, lupeTransform, lupeRadius);
            
            // UVアイランドを高詳細で描画
            foreach (var island in uvIslands)
            {
                var color = selectedIslandIDs.Contains(island.islandID) ? 
                            island.maskColor : new Color(0.5f, 0.5f, 0.5f, 0.6f);
                
                DrawUVIslandToMagnifyingGlass(island, pixels, size, size, color, lupeTransform, lupeRadius);
            }
            
            // ワイヤーフレームを描画
            foreach (var island in uvIslands)
            {
                var wireframeColor = selectedIslandIDs.Contains(island.islandID) ? 
                                   Color.white : new Color(0.8f, 0.8f, 0.8f, 1.0f);
                
                DrawUVIslandWireframeToMagnifyingGlass(island, pixels, size, size, wireframeColor, lupeTransform, lupeRadius);
            }
            
            // 円形の境界を描画
            DrawMagnifyingGlassBorder(pixels, size);
            
            texture.SetPixels(pixels);
            texture.Apply();
            
            return texture;
        }
        
        /// <summary>
        /// ルーペ用変換行列を作成
        /// </summary>
        private Matrix4x4 CreateMagnifyingTransformMatrix(Vector2 centerUV, float radius)
        {
            var centerOffset = Matrix4x4.Translate(new Vector3(-centerUV.x, -centerUV.y, 0f));
            var scaleMatrix = Matrix4x4.Scale(new Vector3(1f / (radius * 2f), 1f / (radius * 2f), 1f));
            var recenterMatrix = Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0f));
            
            return recenterMatrix * scaleMatrix * centerOffset;
        }
        
        /// <summary>
        /// ルーペ用高詳細グリッドを描画
        /// </summary>
        private void DrawMagnifyingGridWithTransform(Color[] pixels, int width, int height, Matrix4x4 transform, float radius)
        {
            var gridColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            var borderColor = new Color(0.4f, 0.4f, 0.4f, 1.0f);
            var fineGridColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            
            // 細かいグリッド間隔
            float gridStep = Mathf.Min(0.01f, radius * 0.1f);
            
            for (float gridPos = 0f; gridPos <= 1f; gridPos += gridStep)
            {
                bool isMajorGrid = Mathf.Abs(gridPos % 0.1f) < 0.001f;
                bool isMinorGrid = Mathf.Abs(gridPos % 0.05f) < 0.001f;
                bool isBorder = gridPos == 0f || gridPos == 1f;
                
                Color lineColor = isBorder ? borderColor : 
                                 (isMajorGrid ? gridColor : 
                                  (isMinorGrid ? new Color(0.22f, 0.22f, 0.22f, 1.0f) : fineGridColor));
                
                // 縦線
                var startV = TransformUVToScreen(new Vector2(gridPos, 0f), width, height, transform);
                var endV = TransformUVToScreen(new Vector2(gridPos, 1f), width, height, transform);
                DrawLineScreenSpaceClipped(startV, endV, pixels, width, height, lineColor);
                
                // 横線
                var startH = TransformUVToScreen(new Vector2(0f, gridPos), width, height, transform);
                var endH = TransformUVToScreen(new Vector2(1f, gridPos), width, height, transform);
                DrawLineScreenSpaceClipped(startH, endH, pixels, width, height, lineColor);
            }
        }
        
        /// <summary>
        /// ルーペ用UVアイランドを描画
        /// </summary>
        private void DrawUVIslandToMagnifyingGlass(UVIsland island, Color[] pixels, int width, int height, Color color, Matrix4x4 transform, float radius)
        {
            var uvs = targetMesh.uv;
            
            for (int i = 0; i < island.triangleIndices.Count; i += 3)
            {
                if (i + 2 >= island.triangleIndices.Count) break;
                
                var v0Index = island.triangleIndices[i];
                var v1Index = island.triangleIndices[i + 1];
                var v2Index = island.triangleIndices[i + 2];
                
                if (v0Index < uvs.Length && v1Index < uvs.Length && v2Index < uvs.Length)
                {
                    var uv0 = uvs[v0Index];
                    var uv1 = uvs[v1Index];
                    var uv2 = uvs[v2Index];
                    
                    var screen0 = TransformUVToScreen(uv0, width, height, transform);
                    var screen1 = TransformUVToScreen(uv1, width, height, transform);
                    var screen2 = TransformUVToScreen(uv2, width, height, transform);
                    
                    DrawTriangleScreenSpaceClipped(screen0, screen1, screen2, pixels, width, height, color, radius);
                }
            }
        }
        
        /// <summary>
        /// ルーペ用ワイヤーフレームを描画
        /// </summary>
        private void DrawUVIslandWireframeToMagnifyingGlass(UVIsland island, Color[] pixels, int width, int height, Color color, Matrix4x4 transform, float radius)
        {
            var uvs = targetMesh.uv;
            
            for (int i = 0; i < island.triangleIndices.Count; i += 3)
            {
                if (i + 2 >= island.triangleIndices.Count) break;
                
                var v0Index = island.triangleIndices[i];
                var v1Index = island.triangleIndices[i + 1];
                var v2Index = island.triangleIndices[i + 2];
                
                if (v0Index < uvs.Length && v1Index < uvs.Length && v2Index < uvs.Length)
                {
                    var uv0 = uvs[v0Index];
                    var uv1 = uvs[v1Index];
                    var uv2 = uvs[v2Index];
                    
                    var screen0 = TransformUVToScreen(uv0, width, height, transform);
                    var screen1 = TransformUVToScreen(uv1, width, height, transform);
                    var screen2 = TransformUVToScreen(uv2, width, height, transform);
                    
                    DrawLineScreenSpaceClipped(screen0, screen1, pixels, width, height, color);
                    DrawLineScreenSpaceClipped(screen1, screen2, pixels, width, height, color);
                    DrawLineScreenSpaceClipped(screen2, screen0, pixels, width, height, color);
                }
            }
        }
        
        /// <summary>
        /// 円形クリッピング付きの線描画
        /// </summary>
        private void DrawLineScreenSpaceClipped(Vector2 start, Vector2 end, Color[] pixels, int width, int height, Color color)
        {
            // 円形境界内のみ描画
            var center = new Vector2(width * 0.5f, height * 0.5f);
            var radius = width * 0.5f - 2;
            
            int x0 = Mathf.RoundToInt(start.x);
            int y0 = Mathf.RoundToInt(start.y);
            int x1 = Mathf.RoundToInt(end.x);
            int y1 = Mathf.RoundToInt(end.y);
            
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            
            while (true)
            {
                // 円形境界チェック
                if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                {
                    float dist = Vector2.Distance(new Vector2(x0, y0), center);
                    if (dist <= radius)
                    {
                        int index = y0 * width + x0;
                        if (index >= 0 && index < pixels.Length)
                        {
                            pixels[index] = Color.Lerp(pixels[index], color, 0.9f);
                        }
                    }
                }
                
                if (x0 == x1 && y0 == y1) break;
                
                int e2 = 2 * err;
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
        
        /// <summary>
        /// 円形クリッピング付きの三角形描画
        /// </summary>
        private void DrawTriangleScreenSpaceClipped(Vector2 screen0, Vector2 screen1, Vector2 screen2, Color[] pixels, int width, int height, Color color, float uvRadius)
        {
            var center = new Vector2(width * 0.5f, height * 0.5f);
            var pixelRadius = width * 0.5f - 2;
            
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(screen0.x, Mathf.Min(screen1.x, screen2.x))));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(screen0.x, Mathf.Max(screen1.x, screen2.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(screen0.y, Mathf.Min(screen1.y, screen2.y))));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(screen0.y, Mathf.Max(screen1.y, screen2.y))));
            
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 point = new Vector2(x, y);
                    
                    // 円形境界チェック
                    float dist = Vector2.Distance(point, center);
                    if (dist > pixelRadius) continue;
                    
                    if (IsPointInTriangleScreenSpace(point, screen0, screen1, screen2))
                    {
                        int index = y * width + x;
                        if (index >= 0 && index < pixels.Length)
                        {
                            pixels[index] = Color.Lerp(pixels[index], color, 0.6f);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// 指定UV座標の色を高速取得（軽量版）
        /// </summary>
        private Color GetColorAtUVFast(Vector2 uv)
        {
            // 範囲外の場合
            if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
                return new Color(0.1f, 0.1f, 0.1f, 1f);
            
            // グリッド線の色（簡略化）
            float gridX = uv.x % 0.1f;
            float gridY = uv.y % 0.1f;
            if (gridX < 0.01f || gridY < 0.01f)
                return new Color(0.25f, 0.25f, 0.25f, 1.0f);
            
            // UVアイランドの色を確認（高速化：境界ボックスのみチェック）
            foreach (var island in uvIslands)
            {
                var bounds = island.uvBounds;
                if (uv.x >= bounds.min.x && uv.x <= bounds.max.x &&
                    uv.y >= bounds.min.y && uv.y <= bounds.max.y)
                {
                    return selectedIslandIDs.Contains(island.islandID) ? 
                           island.maskColor : new Color(0.5f, 0.5f, 0.5f, 0.6f);
                }
            }
            
            return new Color(0.15f, 0.15f, 0.15f, 1.0f);
        }
        
        /// <summary>
        /// 指定UV座標の色を取得
        /// </summary>
        private Color GetColorAtUV(Vector2 uv)
        {
            // 範囲外の場合
            if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
            {
                return new Color(0.1f, 0.1f, 0.1f, 1f);
            }
            
            // グリッド線の色
            if (Mathf.Abs(uv.x % 0.1f) < 0.005f || Mathf.Abs(uv.y % 0.1f) < 0.005f)
            {
                return new Color(0.25f, 0.25f, 0.25f, 1.0f);
            }
            
            // UVアイランドの色を確認
            foreach (var island in uvIslands)
            {
                if (IsPointInUVIsland(uv, island))
                {
                    return selectedIslandIDs.Contains(island.islandID) ? 
                           island.maskColor : new Color(0.5f, 0.5f, 0.5f, 0.6f);
                }
            }
            
            return new Color(0.15f, 0.15f, 0.15f, 1.0f);
        }
        
        /// <summary>
        /// ルーペの境界を描画
        /// </summary>
        private void DrawMagnifyingGlassBorder(Color[] pixels, int size)
        {
            var borderColor = Color.white;
            int center = size / 2;
            int radius = center - 2;
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int dx = x - center;
                    int dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    
                    if (distance >= radius - 1 && distance <= radius + 1)
                    {
                        pixels[y * size + x] = borderColor;
                    }
                }
            }
        }
        
        // エディタ専用メソッド
        #if UNITY_EDITOR
        public void RefreshInEditor()
        {
            UpdateMeshData();
            GenerateUVMapTexture();
        }
        #endif
    }
}
#endif
