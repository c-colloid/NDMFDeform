using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace Deform.Masking
{
    /// <summary>
    /// 自動UVアイランド検出・選択コンポーネント（Renderer対応版・改良版）
    /// </summary>
    //[System.Serializable]
    public class UVIslandSelector : MonoBehaviour
    {
        [Header("UV Island Selection Settings")]
        [SerializeField] private bool showUVMap = true;
        [SerializeField] private Texture2D uvMapTexture;
        [SerializeField] private List<int> selectedIslandIDs = new List<int>();
        
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
        
        public List<UVIsland> UVIslands => uvIslands;
        public int[] VertexMask => vertexMask;
        public int[] TriangleMask => triangleMask;
        public bool HasSelectedIslands => selectedIslandIDs.Count > 0;
        public Mesh TargetMesh => targetMesh;
        public List<int> SelectedIslandIDs => selectedIslandIDs;
        public Texture2D UvMapTexture => uvMapTexture;
        
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
                AnalyzeUVIslands();
            }
        }
        
        /// <summary>
        /// Rendererからメッシュを取得（改良版）
        /// </summary>
        private Mesh GetMeshFromRenderer()
        {
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
        }
        
        /// <summary>
        /// UV座標からアイランドを検索
        /// </summary>
        public int GetIslandAtUVCoordinate(Vector2 uvCoord)
        {
            foreach (var island in uvIslands)
            {
                if (island.uvBounds.Contains(new Vector3(uvCoord.x, uvCoord.y, 0)))
                {
                    // より詳細な判定（三角形ベース）
                    if (IsPointInUVIsland(uvCoord, island))
                    {
                        return island.islandID;
                    }
                }
            }
            return -1;
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
        /// UVマップテクスチャを生成（ワイヤーフレーム付き）
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
            
            // UVグリッドを描画
            DrawUVGrid(pixels, width, height);
            
            // UVアイランドを描画
            foreach (var island in uvIslands)
            {
                var color = selectedIslandIDs.Contains(island.islandID) ? 
                            island.maskColor : new Color(0.5f, 0.5f, 0.5f, 0.6f);
                
                DrawUVIslandToTexture(island, pixels, width, height, color);
            }
            
            // ワイヤーフレームを描画
            foreach (var island in uvIslands)
            {
                var wireframeColor = selectedIslandIDs.Contains(island.islandID) ? 
                                   Color.white : new Color(0.8f, 0.8f, 0.8f, 1.0f);
                
                DrawUVIslandWireframe(island, pixels, width, height, wireframeColor);
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            uvMapTexture = texture;
            
            return texture;
        }
        
        /// <summary>
        /// UVグリッドを描画
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
        /// UVアイランドをテクスチャに描画（三角形ベース）
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
        /// 三角形をテクスチャに描画
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
        /// UVアイランドのワイヤーフレームを描画
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
            int y0 = Mathf.RoundToInt((1f - start.y) * height);
            int x1 = Mathf.RoundToInt(end.x * width);
            int y1 = Mathf.RoundToInt((1f - end.y) * height);
            
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
        /// シーンビューでの面ハイライト用の情報を取得
        /// </summary>
        public List<Vector3> GetSelectedTrianglesWorldPositions()
        {
            if (targetMesh == null || triangleMask == null || triangleMask.Length == 0)
                return new List<Vector3>();
            
            var vertices = targetMesh.vertices;
            var worldPositions = new List<Vector3>();
            
            foreach (int vertexIndex in triangleMask)
            {
                if (vertexIndex < vertices.Length)
                {
                    worldPositions.Add(transform.TransformPoint(vertices[vertexIndex]));
                }
            }
            
            return worldPositions;
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