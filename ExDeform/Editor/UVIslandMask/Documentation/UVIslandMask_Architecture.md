# UVIslandMask Architecture Documentation
# UVIslandMaskアーキテクチャドキュメント

## System Overview / システム概要

```
┌─────────────────────────────────────────────────────────────────┐
│                        Unity Editor UI                          │
│                      Unityエディタ UI                            │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                   UVIslandMaskEditor.cs                         │
│                   (2985 lines / 20 regions)                     │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ • UI Creation & Management                              │   │
│  │ • User Input Handling (Mouse, Keyboard)                 │   │
│  │ • Async Initialization                                  │   │
│  │ • Texture Caching (Low-res & Full-res)                  │   │
│  │ • Scene View Integration                                │   │
│  └─────────────────────────────────────────────────────────┘   │
└───────┬─────────────────────┬───────────────────┬───────────────┘
        │                     │                   │
        ▼                     ▼                   ▼
┌───────────────┐   ┌──────────────────┐   ┌──────────────────┐
│ Localization  │   │  UVIslandSelector│   │  UVIslandMask    │
│               │   │  (1289 lines)    │   │  (366 lines)     │
│ .cs (268)     │   │  15 regions      │   │  6 regions       │
└───────────────┘   └──────────────────┘   └──────────────────┘
                             │                   │
                             │                   │
                             ▼                   ▼
                    ┌──────────────────┐   ┌──────────────────┐
                    │ UVIslandAnalyzer │   │  Deform System   │
                    │ (1233 lines)     │   │  (Unity Jobs)    │
                    │ 13 regions       │   │                  │
                    └──────────────────┘   └──────────────────┘
```

## Detailed Component Architecture / 詳細コンポーネントアーキテクチャ

### 1. UVIslandMaskEditor.cs (Editor Layer / エディタ層)

```
┌─────────────────────────────────────────────────────────────────┐
│                    UVIslandMaskEditor                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  [Fields and Constants]                                         │
│  ├─ UI Elements (root, uvMapContainer, buttons, etc)           │
│  ├─ State (selector, targetMask, asyncInitManager)             │
│  └─ Cache (persistentCache, currentLowResTexture)              │
│                                                                 │
│  [Editor Lifecycle] ────────────────────────────────────────┐   │
│  ├─ RequiresConstantRepaint()                              │   │
│  ├─ CreateInspectorGUI() ◄─── Main Entry Point            │   │
│  ├─ OnEnable() / OnDisable()                               │   │
│  └─ OnDestroy()                                            │   │
│                                                             │   │
│  [UI Creation - Inspector Setup] ◄──────────────────────────┤   │
│  ├─ CreateLanguageSelector()                                   │
│  ├─ CreateHeader()                                             │
│  ├─ CreateMaskSettings()                                       │
│  ├─ CreateSubmeshSelector()                                    │
│  ├─ CreateHighlightSettings()                                  │
│  ├─ CreateUVMapArea() ◄─── 300x300 Interactive Canvas         │
│  ├─ CreateIslandList()                                         │
│  └─ CreateControlButtons()                                     │
│                                                                 │
│  [Mouse Event Handlers] ◄─── User Interaction Layer            │
│  ├─ OnUVMapMouseDown() ──► Island Selection                    │
│  ├─ HandleMouseMove()   ──► Pan / Range Selection              │
│  ├─ OnUVMapWheel()      ──► Zoom                               │
│  └─ OnRootMouseUp()     ──► Finalize Selection                 │
│                                                                 │
│  [Async Initialization] ◄─── Performance Optimization          │
│  ├─ MonitorAsyncInitialization()                               │
│  ├─ OnAsyncInitializationCompleted()                           │
│  └─ ShowPlaceholderMessage()                                   │
│                                                                 │
│  [Texture & Cache Operations]                                  │
│  ├─ LoadLowResTextureFromCache() ◄─── Fast Initial Display    │
│  ├─ SaveLowResTextureToCache()                                 │
│  └─ UpdateTextureWithThrottle()  ◄─── 60fps Throttling        │
│                                                                 │
│  [Range Selection] ◄─── Box Selection Feature                  │
│  ├─ StartRangeSelection()                                      │
│  ├─ UpdateRangeSelection()                                     │
│  └─ FinishRangeSelection()                                     │
│                                                                 │
│  [Magnifying Glass] ◄─── Right-click Zoom Feature              │
│  ├─ StartMagnifyingGlass()                                     │
│  ├─ UpdateMagnifyingGlass()                                    │
│  └─ HandleMagnifyingGlassClick()                               │
│                                                                 │
│  [Scene View Integration]                                      │
│  ├─ OnSceneGUI() ──► 3D Mesh Highlighting                     │
│  └─ GetRendererTransform()                                     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
          │                    │                    │
          │ Uses               │ Manages            │ Updates
          ▼                    ▼                    ▼
  [Localization]      [UVIslandSelector]      [UVIslandMask]
```

### 2. UVIslandSelector.cs (Core Selection Logic / コア選択ロジック)

```
┌─────────────────────────────────────────────────────────────────┐
│                    UVIslandSelector                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  [Initialization & Lifecycle]                                   │
│  ├─ UVIslandSelector() ◄─── Empty constructor (async)          │
│  ├─ UVIslandSelector(Mesh) ◄─── Full constructor               │
│  └─ Dispose()                                                   │
│                                                                 │
│  [Mesh Data Management] ────────────────────────────────────┐   │
│  ├─ SetMesh(mesh)                                          │   │
│  ├─ UpdateMeshData() ──► Calls UVIslandAnalyzer           │   │
│  ├─ SetAnalyzedIslands() ◄─── From async analysis         │   │
│  └─ FilterIslandsBySubmesh()                              │   │
│                                                            │   │
│  [Selection Management] ◄──────────────────────────────────┤   │
│  ├─ ToggleIslandSelection(islandID)                           │
│  ├─ SetSelectedIslands(List<int>)                             │
│  ├─ SetAllSelectedIslands(Dictionary)                         │
│  └─ ClearSelection()                                           │
│                                                                 │
│  [Submesh Operations]                                           │
│  ├─ SetSelectedSubmeshes(List<int>)                            │
│  ├─ SetPreviewSubmesh(index)                                   │
│  ├─ NextPreviewSubmesh()                                       │
│  └─ PreviousPreviewSubmesh()                                   │
│                                                                 │
│  [Range Selection]                                              │
│  ├─ StartRangeSelection(startPoint)                            │
│  ├─ UpdateRangeSelection(currentPoint)                         │
│  ├─ FinishRangeSelection(add/remove)                           │
│  └─ GetIslandsInRect(rect) ◄─── Spatial Query                 │
│                                                                 │
│  [View Transform & Navigation]                                  │
│  ├─ CalculateUVTransformMatrix()                               │
│  ├─ SetZoomLevel(zoom)                                         │
│  ├─ SetPanOffset(offset)                                       │
│  └─ ZoomAtPoint(point, delta)                                  │
│                                                                 │
│  [Hit Testing & Picking]                                        │
│  └─ GetIslandAtUVCoordinate(uvCoord) ──► Returns Island ID     │
│                                                                 │
│  [Mask System] ◄─── Output for Deformation                     │
│  ├─ UpdateMasks() ──► Creates vertex/triangle masks           │
│  ├─ AddIslandToMasks(islandID)                                 │
│  └─ RemoveIslandFromMasks(islandID)                            │
│                                                                 │
│  [Texture Generation]                                           │
│  ├─ GenerateUVMapTexture() ──► 512x512 Main Display           │
│  ├─ GenerateLowResUVMapTexture() ──► 128x128 Cache            │
│  └─ GenerateMagnifyingGlassTexture() ──► Zoom View            │
│                                                                 │
│  [Scene View Rendering]                                         │
│  ├─ DrawSelectedFacesInScene() ◄─── SceneView GUI              │
│  ├─ RebuildTriangleRenderCache()                               │
│  └─ DrawCachedTriangles() ◄─── Multi-pass rendering           │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
          │                               │
          │ Calls                         │ Provides data to
          ▼                               ▼
  [UVIslandAnalyzer]              [UVIslandMask]
```

### 3. UVIslandAnalyzer.cs (Algorithm Layer / アルゴリズム層)

```
┌─────────────────────────────────────────────────────────────────┐
│                   UVIslandAnalyzer                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  [Public API] ◄─── Entry Points                                │
│  ├─ AnalyzeUVIslands(Mesh) ──► Main method                     │
│  └─ AnalyzeUVIslands(Mesh, submeshList)                        │
│        │                                                        │
│        ├─► [Advanced Algorithm] (Default)                      │
│        │   ├─ BuildVertexToTriangleMapping()                   │
│        │   ├─ BuildTriangleAdjacencyGraph()                    │
│        │   │   ├─ Edge-based connections                       │
│        │   │   ├─ UV proximity connections                     │
│        │   │   └─ Aggressive proximity connections             │
│        │   └─ Union-Find clustering                            │
│        │                                                        │
│        └─► [Legacy Algorithm] (Backward compatibility)         │
│            └─ FindAdjacentTrianglesOptimized()                 │
│                                                                 │
│  [Data Structures]                                              │
│  └─ UVIsland class                                              │
│      ├─ islandID: int                                           │
│      ├─ submeshIndex: int                                       │
│      ├─ triangleIndices: List<int>                             │
│      ├─ vertexIndices: HashSet<int>                            │
│      ├─ uvBounds: Bounds                                        │
│      └─ color: Color                                            │
│                                                                 │
│  [Hit Testing] ◄─── Point-in-polygon testing                   │
│  ├─ IsPointInUVIsland(point, island)                           │
│  ├─ IsPointInUVIslandOptimized()                               │
│  └─ IsPointInIslandRayCasting() ◄─── Complex polygons          │
│                                                                 │
│  [Utility Methods]                                              │
│  ├─ CalculateUVBounds()                                        │
│  ├─ GenerateIslandColor()                                      │
│  └─ GetTriangleUVCenter()                                      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
          │
          │ Returns
          ▼
   List<UVIsland> ──► Used by UVIslandSelector
```

### 4. UVIslandMask.cs (Runtime Deformation / ランタイム変形)

```
┌─────────────────────────────────────────────────────────────────┐
│                      UVIslandMask                               │
│                   (Deformer Component)                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  [Serialized Fields] ◄─── Saved in Scene                       │
│  ├─ selectedSubmeshes: List<int>                               │
│  ├─ currentPreviewSubmesh: int                                 │
│  ├─ selectedIslandIDs: List<int> (Legacy)                      │
│  ├─ perSubmeshSelections: List<SubmeshIslandSelection> (New)   │
│  ├─ selectedVertexIndices: List<int>                           │
│  ├─ invertMask: bool                                            │
│  └─ maskStrength: float                                         │
│                                                                 │
│  [Runtime Data]                                                 │
│  ├─ maskValues: NativeArray<float> ◄─── Per-vertex mask        │
│  ├─ cachedMesh: Mesh                                            │
│  └─ cachedRenderer: Renderer                                    │
│                                                                 │
│  [Deformation Processing] ◄─── Unity Job System                │
│  └─ Process(MeshData, JobHandle)                               │
│      ├─ UpdateMaskData() ──► Build mask array                  │
│      └─ Schedule UVIslandMaskJob                               │
│                                                                 │
│  [Public API]                                                   │
│  ├─ SetSelectedIslands()                                        │
│  ├─ SetPerSubmeshSelections()                                  │
│  ├─ GetPerSubmeshSelections()                                  │
│  ├─ SetSelectedVertexIndices() ◄─── Most efficient             │
│  └─ UpdateRendererCache()                                       │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
          │
          │ Executes
          ▼
┌─────────────────────────────────────────────────────────────────┐
│                  UVIslandMaskJob                                │
│                  (Burst Compiled)                               │
├─────────────────────────────────────────────────────────────────┤
│  Execute(int index)                                             │
│  ├─ Read maskValues[index]                                     │
│  ├─ Apply invertMask logic                                     │
│  ├─ Apply maskStrength                                          │
│  └─ Lerp between deformed and original vertex                  │
└─────────────────────────────────────────────────────────────────┘
```

### 5. UVIslandLocalization.cs (Localization / 多言語化)

```
┌─────────────────────────────────────────────────────────────────┐
│                 UVIslandLocalization                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  [Language Definition]                                          │
│  ├─ enum Language { English, Japanese }                        │
│  └─ CurrentLanguage: Language                                  │
│                                                                 │
│  [Localized Strings]                                            │
│  └─ Dictionary<string, Dictionary<Language, string>>           │
│      ├─ Header titles (20+ entries)                            │
│      ├─ Field labels (30+ entries)                             │
│      ├─ Button labels                                           │
│      ├─ Tooltips                                                │
│      ├─ Status messages                                         │
│      └─ Control instructions                                    │
│                                                                 │
│  [Public API]                                                   │
│  ├─ Get(key, args) ──► Returns localized text                  │
│  └─ GetContent(key, tooltipKey) ──► Returns GUIContent         │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
          │
          │ Used by
          ▼
   UVIslandMaskEditor (All UI creation)
```

## Data Flow Diagram / データフロー図

```
┌────────────┐
│    User    │
│ ユーザー    │
└──────┬─────┘
       │ Clicks on UV Island
       │ UVアイランドをクリック
       ▼
┌─────────────────────────────────────┐
│      UVIslandMaskEditor             │
│   OnUVMapMouseDown()                │
└──────┬──────────────────────────────┘
       │ LocalPosToUV()
       │ UV座標に変換
       ▼
┌─────────────────────────────────────┐
│      UVIslandSelector               │
│   GetIslandAtUVCoordinate()         │
└──────┬──────────────────────────────┘
       │ Hit testing
       │ ヒットテスト
       ▼
┌─────────────────────────────────────┐
│      UVIslandAnalyzer               │
│   IsPointInUVIsland()               │
└──────┬──────────────────────────────┘
       │ Returns Island ID
       │ アイランドIDを返す
       ▼
┌─────────────────────────────────────┐
│      UVIslandSelector               │
│   ToggleIslandSelection()           │
│   UpdateMasks()                     │
└──────┬──────────────────────────────┘
       │ Updates vertex mask
       │ 頂点マスクを更新
       ▼
┌─────────────────────────────────────┐
│      UVIslandMask                   │
│   SetSelectedVertexIndices()        │
└──────┬──────────────────────────────┘
       │ Serializes selection
       │ 選択をシリアライズ
       ▼
┌─────────────────────────────────────┐
│      UVIslandMaskEditor             │
│   UpdateMaskComponent()             │
│   RefreshUI()                       │
│   GenerateUVMapTexture()            │
└──────┬──────────────────────────────┘
       │ Display update
       │ 表示更新
       ▼
┌────────────┐
│    User    │
│  ユーザー   │
└────────────┘
```

## Runtime Execution Flow / ランタイム実行フロー

```
Game/Playmode Start
ゲーム/プレイモード開始
       │
       ▼
┌─────────────────────────────────────┐
│  Deformable Component               │
│  Process() called every frame       │
└──────┬──────────────────────────────┘
       │
       ▼
┌─────────────────────────────────────┐
│  UVIslandMask.Process()             │
│  ├─ Check if mask needs update      │
│  ├─ UpdateMaskData() (if needed)    │
│  │   └─ Build NativeArray<float>    │
│  └─ Schedule UVIslandMaskJob        │
└──────┬──────────────────────────────┘
       │
       ▼
┌─────────────────────────────────────┐
│  UVIslandMaskJob (Burst)            │
│  Parallel execution on all vertices │
│  ├─ For each vertex:                │
│  │   ├─ Read mask value (0 or 1)    │
│  │   ├─ Apply invert/strength       │
│  │   └─ Lerp to original position   │
│  └─ Output: Modified vertex buffer  │
└──────┬──────────────────────────────┘
       │
       ▼
┌─────────────────────────────────────┐
│  Deformable renders mesh            │
│  With selective deformation         │
└─────────────────────────────────────┘
```

## Cache System Architecture / キャッシュシステムアーキテクチャ

```
┌─────────────────────────────────────────────────────────────────┐
│                    Cache System                                 │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  [Persistent Cache] (In-Memory)                                 │
│  Dictionary<string, UVIslandSelector>                           │
│  ├─ Key: "meshName_vertexCount_uvHash"                         │
│  ├─ Value: UVIslandSelector with analyzed islands              │
│  └─ Survives: Editor refresh, inspector close/open             │
│                                                                 │
│  [Low-Res Texture Cache] (Disk + Memory)                       │
│  RobustUVCache system                                           │
│  ├─ Key: "meshName_vertexCount_uvHash_sm{index}"               │
│  ├─ Value: 128x128 Texture2D (PNG)                             │
│  ├─ Location: Library/UVIslandCache/                           │
│  └─ Purpose: Fast initial display (< 50ms)                     │
│                                                                 │
│  [Cache Generation Flow]                                        │
│  First Time:                                                    │
│    1. Mesh assigned ──► UVIslandAnalyzer (300-1000ms)          │
│    2. Save UVIslandSelector to persistent cache                │
│    3. Generate 128x128 texture ──► Save to disk                │
│                                                                 │
│  Second Time:                                                   │
│    1. Check persistent cache ──► Found!                        │
│    2. Load 128x128 texture from disk ──► Display (< 50ms)      │
│    3. User interaction ──► Generate full 512x512 texture       │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## Performance Optimization Strategies / パフォーマンス最適化戦略

```
┌─────────────────────────────────────────────────────────────────┐
│               Performance Optimizations                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  [1. Async Initialization]                                      │
│      AsyncInitializationManager                                 │
│      ├─ UV analysis in EditorApplication.delayCall             │
│      ├─ Prevents UI blocking (300-1000ms saved)                │
│      └─ Progress bar feedback                                   │
│                                                                 │
│  [2. Texture Throttling]                                        │
│      UpdateTextureWithThrottle()                                │
│      ├─ Max 60fps texture regeneration                         │
│      ├─ Prevents unnecessary redraws                            │
│      └─ Smooth panning/zooming                                  │
│                                                                 │
│  [3. Low-Res Preview]                                           │
│      128x128 cached texture                                     │
│      ├─ Instant display on inspector open                       │
│      ├─ < 50ms load time                                        │
│      └─ Upgrades to 512x512 on interaction                      │
│                                                                 │
│  [4. Scene View Caching]                                        │
│      TriangleRenderData[] cache                                 │
│      ├─ Rebuild only when selection changes                     │
│      ├─ Hash-based dirty checking                               │
│      └─ Multi-pass rendering (fill + wireframe)                 │
│                                                                 │
│  [5. Burst Compilation]                                         │
│      UVIslandMaskJob                                            │
│      ├─ SIMD optimizations                                      │
│      ├─ Parallel vertex processing                              │
│      └─ 10-100x faster than C#                                  │
│                                                                 │
│  [6. Incremental Mask Updates]                                  │
│      AddIslandToMasks() / RemoveIslandFromMasks()               │
│      ├─ Only update affected vertices                           │
│      ├─ Avoids full mask rebuild                                │
│      └─ O(island_size) vs O(total_vertices)                     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## Feature Map / 機能マップ

```
┌─────────────────────────────────────────────────────────────────┐
│                      Features                                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ✓ UV Island Selection                                          │
│    ├─ Click to select individual islands                       │
│    ├─ Ctrl+Click to multi-select                               │
│    ├─ Shift+Click to range select                              │
│    └─ Ctrl+Shift+Drag to deselect region                       │
│                                                                 │
│  ✓ Multi-Submesh Support                                        │
│    ├─ Filter by submesh                                         │
│    ├─ Per-submesh selection tracking                            │
│    └─ Preview submesh switching                                 │
│                                                                 │
│  ✓ View Navigation                                              │
│    ├─ Mouse wheel zoom (1x - 8x)                               │
│    ├─ Middle-drag pan                                           │
│    ├─ Reset view button                                         │
│    └─ Zoom at cursor point                                      │
│                                                                 │
│  ✓ Magnifying Glass                                             │
│    ├─ Right-click to activate                                   │
│    ├─ Configurable zoom (2x - 16x)                             │
│    ├─ Configurable size                                         │
│    └─ Click within to select                                    │
│                                                                 │
│  ✓ Scene View Highlighting                                      │
│    ├─ Real-time 3D mesh highlighting                            │
│    ├─ Configurable opacity                                      │
│    ├─ Multi-pass rendering (fill + wireframe)                   │
│    └─ Frustum culling                                           │
│                                                                 │
│  ✓ Localization                                                 │
│    ├─ English / Japanese                                        │
│    ├─ All UI elements localized                                 │
│    └─ Switchable at runtime                                     │
│                                                                 │
│  ✓ Performance Optimizations                                    │
│    ├─ Async initialization                                      │
│    ├─ Texture caching                                           │
│    ├─ Burst-compiled jobs                                       │
│    └─ Incremental updates                                       │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## File Size and Region Summary / ファイルサイズとRegion概要

| File | Lines | Regions | Main Responsibility |
|------|-------|---------|---------------------|
| UVIslandMaskEditor.cs | 2985 | 20 | Editor UI & Interaction<br/>エディタUIとインタラクション |
| UVIslandSelector.cs | 1289 | 15 | Selection Logic & Rendering<br/>選択ロジックとレンダリング |
| UVIslandAnalyzer.cs | 1233 | 13 | UV Island Detection<br/>UVアイランド検出 |
| UVIslandMask.cs | 366 | 6 | Runtime Deformation<br/>ランタイム変形 |
| UVIslandLocalization.cs | 268 | 3 | Localization<br/>多言語化 |
| **Total** | **6141** | **57** | **Complete System** |

---

## Quick Navigation Guide / クイックナビゲーションガイド

### Want to modify UI?
**UIを変更したい場合:**
→ `UVIslandMaskEditor.cs` → Region: `UI Creation - Inspector Setup`

### Want to change selection behavior?
**選択動作を変更したい場合:**
→ `UVIslandSelector.cs` → Region: `Selection Management`

### Want to optimize UV island detection?
**UVアイランド検出を最適化したい場合:**
→ `UVIslandAnalyzer.cs` → Region: `Advanced Algorithm`

### Want to modify deformation behavior?
**変形動作を変更したい場合:**
→ `UVIslandMask.cs` → Region: `Deformation Processing`

### Want to add new language?
**新しい言語を追加したい場合:**
→ `UVIslandLocalization.cs` → Region: `Localized Strings`

### Want to improve performance?
**パフォーマンスを改善したい場合:**
→ `UVIslandMaskEditor.cs` → Region: `Async Initialization` or `Texture and Cache Operations`

---

**Generated:** 2025-10-10
**Version:** 1.0
**Total System:** 6,141 lines, 57 regions, 5 files
