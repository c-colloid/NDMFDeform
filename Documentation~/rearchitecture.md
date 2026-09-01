# NDMFDeform v2 再設計ドキュメント

- ステータス: Draft
- 日付: 2026-08-29
- ブランチ: `claude/ndmfdeform-rearchitecture-48ot3e`

## 0. 要約

NDMFDeform を、Deform フォーク + 薄い NDMF ラッパーという現行構成から、
**NDMF ネイティブの非破壊メッシュ変形ツール**として作り直す。

- Deform への依存(サブモジュール/フォーク)を段階的に解消する
- Deform の Burst ジョブ数式は MIT ライセンスに基づき必要な分だけ流用する
- 対象デフォーマは実需ベースで絞る: **Lattice(ミラー付き)/ Cylindrical 系(自作)/ UVIslandMask(自作)**
- エディタ UI は **UITK 主体**、シーンハンドルは**宣言的 API(HandleBuilder)**として実装し、
  今後の自作デフォーマ開発を「ジョブ + フィールド + 宣言数行」まで軽量化する
- ベイクはライフサイクル副作用に依存しない**ヘッドレスなベイクコア**で行い、
  現行の致命バグ(ブレンドシェイプ破損・GameObject 誤削除など)を構造的に解消する

## 1. 背景

### 1.1 現行構成

```
NDMFDeform (jp.colloid.nemfdeform v0.0.8)
├── NDMFPlugin/
│   ├── NDMFDeform.cs      … NDMF Plugin(ベイク + クリーンアップ)163行
│   └── DeformPreview.cs   … NDMF IRenderFilter プレビュー 193行
└── Deform/                … keenanwoodall/Deform のフォーク(git submodule、約17k行)
    └── フォーク独自変更: UITK インスペクタ、Lattice ミラー編集、
        VRC.SDKBase.IEditorOnly 付与、Category.VRChat 追加(20コミット、実質約950行)
```

未マージのブランチに自作デフォーマ資産がある:

- `origin/dev` … CylindricalScaleDeformer / CylindricalVertexTransformDefomer(+ IMGUI ハンドルエディタ)、
  UVIslandMask コンパクト版(UITK)、統合修正コミット群(約2.7k行)
- `origin/UVIslandMask` … ExDeform 拡張フレームワーク(`IExDeformer` 基底)+ UV 島解析・
  多層キャッシュ・UITK 選択 UI の大幅拡張(約18.5k行、リファクタ途中の重複を多く含む)

2026-08-29 の精査(全ファイル + 約90コミット)で `origin/UVIslandMask` は**流用不可**と最終判断した。
マスクを実際に変形へ適用するコードパスがブランチ内に存在せず(`UVIslandMask.ProcessMesh` は計算した
マスク値を捨てて素通し)、同一コンポーネントへの `[CustomEditor]` 3 重登録による競合、参照ゼロの
ドメイン層・セレクタサービス層、キャッシュ 9 実装中 7 が死にコード、といった状態で「どれが本物か」の
判別コストが書き直しを上回る。ズームパンの座標変換(描画と判定で同一行列を共有)・スロットル付き
テクスチャ再描画・「選択の永続化とキャッシュの分離」などの良案のみ v2 に再実装して取り込んだ。

### 1.2 調査で確認された問題(2026-08 実施)

ラッパー / アーキテクチャ起因(いずれも本セッションでコード上確認済み):

| 重大度 | 問題 |
|---|---|
| critical | **ブレンドシェイプ破損**: ベイクは変形後の基底頂点をコピーするが、ブレンドシェイプのデルタは元メッシュのまま再計算されない(Deform 全体に blendshape 対応コードが存在しない)。顔にかかる変形で口パク・表情がサイレントに壊れる |
| critical | **GameObject 誤削除**: クリーンアップパスが Deformer の GameObject ごと `DestroyImmediate`(`NDMFPlugin/NDMFDeform.cs:87,94`)。アバター外のオブジェクトを参照している場合、クローンで参照が差し替わらず**元シーンのオブジェクトを Undo 不能に削除**しうる |
| critical | ベイクが SkinnedMeshRenderer 前提のハードキャスト。MeshFilter 構成は NRE でビルド失敗 |
| major | ベイクが `GetCurrentMesh()`(= ExecuteAlways の副作用でレンダラーに載っているメッシュ)頼みで順序依存。他プラグインが GameObject を無効化すると OnDisable がベイク済みメッシュを元に戻してしまう |
| major | プレビューがシーン側 Deformable を毎フレーム `ForceImmediateUpdate` し、シーンとプロキシで同一メッシュを共有。トグルは実質 no-op、変形は毎フレーム二重計算(NDMF の IRenderFilter 規約にも反する) |
| major | 法線再計算が単純面積加重のみ(シーム分断・作り込み法線の破壊)、タンジェント再計算は存在しない |

フォーク保守起因:

- 拡張の大半(約610行中536行)が partial class で同一アセンブリに溶接されており、外部アセンブリに出せない
- `VRC.SDKBase.IEditorOnly` がフォークのランタイムに無条件で混入(汎用ライブラリとしては死んでいる)
- 全 .meta の GUID がフォークで振り直されており、upstream 配布物と差し替えるとシーンが Missing Script になる(検証済み)
- `.gitmodules` が SSH URL + どのブランチからも到達不能なコミットをピン(匿名 clone 不可、GitHub の ZIP は空フォルダ)
- issue 履歴 4 件は全件がフォーク/ランタイムライブラリ構造起因の摩擦

環境要因:

- upstream Deform は 2024-10 以降休眠(2025年コミット 0)。Unity 6 対応表明なし
- VRChat の Unity 6 移行が接近。17k行の他人のコードを移行させる立場になるリスク
- NDMF ネイティブの競合(32ba: Mesh Deformation Tool)が Lattice 領域で活発に出荷中

### 1.3 検討した選択肢と決定

| 案 | 評価 |
|---|---|
| A. 現状維持(フォーク+ラッパー修正) | プレビュー・ブレンドシェイプ・保守性に構造的な天井。不採用 |
| B. ハイブリッド(未改変 upstream 依存 + ラッパー再構築) | 「45種のデフォーマ幅と IMGUI エディタ資産を温存する」前提でのみ最適。実際は Lattice + 自作しか使われず、UI は UITK 化したい → 前提が成立しない |
| **C. スコープ限定リライト(本設計)** | 実需 3 系統に絞れば、フル書き直しの最大コスト(45種 × ハンドルエディタ再移植)が消える。UITK 主体・Unity 6 対応面積の最小化・拡張 SDK 化がすべて自然に実現。**採用** |

## 2. 目標 / 非目標

### 目標

1. **NDMF ネイティブ**: パッシブな設定コンポーネント + 関数的なベイク。ExecuteAlways やランタイム更新ループを持たない
2. **UITK 主体**: インスペクタ・ツール設定 UI は UITK(UXML/USS)。IMGUI は SceneView の Handles 描画のみ(API の背後に隠蔽)
3. **拡張 SDK**: 自作デフォーマ 1 個の追加コスト = ジョブ + フィールド + ハンドル宣言数行 + (必要なら)UITK 部品
4. **正しいベイク**: ブレンドシェイプ再ベイク、タンジェント再計算、作り込み法線の保持オプション、bounds 整合
5. **Unity 6 移行容易性**: 自前コードの面積を最小化。ジョブ(Burst/Collections/Mathematics)と UITK は最も安定した層
6. **フォーク解消**: Deform サブモジュールを最終的に削除。流用コードは MIT 表記の上で取り込み
7. **配布整備**: vpmDependencies 宣言、タグ + CI + VPM リスティング

### 非目標(v2 初期)

- ランタイム(プレイモード/実機)での変形 — VRChat アバターでは成立しない(コンポーネントはアップロード時に剥がれる)。ワールド利用の扱いは下記 2.1 で別系統として整理
- Deform 全 45 種デフォーマの再現 — 必要になったものからジョブ流用で追加
- ボーン/バインドポーズ補正 — 体型変更系で骨格・IK・PhysBone が視覚とずれる問題は**既知の制約として文書化**し、警告を出す(将来課題)
- Elastic 等の時間駆動デフォーマ — 静的ベイクと両立しない

### 2.1 ワールド利用(リアルタイム / Udon)の扱い

「VRChat ワールドでのリアルタイム変形」は v2 コアとは**別系統として分離**する
(本体の目標/非目標は変更しない)。

理由:

- ワールドで実行できるのは Udon(UdonSharp)であり、任意の MonoBehaviour は動かない。
  Udon は Jobs / Burst / NativeArray を使えず、頂点単位の CPU 処理には性能的にも不適で、
  本設計のジョブ層をそのままワールドへ流用する道は技術的に存在しない
- 成立する実現手段は別技術になる:
  - **(a) ブレンドシェイプとしてベイク** — デフォーマの効果(パラメータ 0 → 指定値)を
    ブレンドシェイプとして焼き込み、再生側は標準機能で駆動する。
    アバターなら Animator、ワールドなら Udon の `SetBlendShapeWeight`(API 露出は実装時確認)。
    ランタイムコンポーネント不要で両環境に効く、最も費用対効果の高い橋渡し
  - **(b) 頂点シェーダ化** — デフォーマ数式をシェーダへ移植しマテリアルパラメータで駆動。
    別モジュールとして将来検討
- コアのジョブ層が純関数である限り (a)(b) どちらも後から追加できる。
  v2 で担保するのはこの分離(数式がコンポーネント/エディタに依存しないこと)のみ

(a) Bake as BlendShape はロードマップ候補として §10 に記載する。

## 3. 全体アーキテクチャ

### 3.1 アセンブリ構成

```
jp.colloid.ndmfdeform (v2)
├── NDMFDeform.Runtime.asmdef     … 設定コンポーネント / ジョブ構造体 / データ型
│                                    (IEditorOnly 実装。更新ループなし。VRCSDK は versionDefines でガード)
├── NDMFDeform.Editor.asmdef      … ベイクコア / HandleBuilder / UITK 基盤 / 移行ツール
└── NDMFDeform.NDMF.asmdef        … NDMF Plugin(パス定義)+ IRenderFilter プレビュー
                                     (defineConstraints: NDMF)
```

- Runtime アセンブリは「シーンに載る器」であり、ロジックを持たない(検証・ベイク・描画はすべて Editor 側)
- 現行 `NDMFDeform.asmdef` の `!DefromAsset` 制約のような全停止スイッチは廃止。
  旧 Deform / Asset Store 版 Deform との共存は、名前空間・GUID・コンポーネント型が完全に別物になるため問題にならない

### 3.2 コンポーネントモデル

旧 Deformable/Deformer のシーン構造(レンダラー側に本体、デフォーマは軸 Transform を兼ねた別 GameObject)を踏襲する。移行の 1:1 対応と、Transform をコントローラとして使う既存の操作感を保つため。

```csharp
// レンダラーの GameObject に付ける(旧 Deformable 相当)
public class DeformStack : MonoBehaviour, VRC.SDKBase.IEditorOnly   // versionDefines ガード付き
{
    [SerializeField] List<DeformerEntry> deformers;   // 順序付き。Entry = { DeformerBase, bool enabled }
    // 更新ループなし。ExecuteAlways なし。メッシュには一切触らない
}

// 各デフォーマ(旧 Deformer 相当)。軸として使う Transform の GameObject に付ける
public abstract class DeformerBase : MonoBehaviour, VRC.SDKBase.IEditorOnly
{
    public abstract DeformDataFlags DataFlags { get; }
    public abstract JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dep);
    public virtual void DescribeHandles(HandleBuilder h) { }   // 6.1 参照
}
```

メタデータは属性で宣言し、「デフォーマ追加」メニュー・分類・ツールチップに使う
(ExDeform `IExDeformer` の DeformerName/Category/Description の後継。`CompatibleDeformVersion` はコア自前化により不要):

```csharp
[DeformerMeta(Name = "Cylindrical Scale", Category = DeformerCategory.Shape,
              Description = "円柱コントローラで範囲スケール")]
public class CylindricalScaleDeformer : DeformerBase { ... }
```

#### カテゴリ設計

旧構成の `VRChat` カテゴリは「本家 Deform と自作を区別する」ための**出自ベース**の分類だった。
v2 では全デフォーマが第一級(自作、または明示的に取り込んだもの)になり出自の区別が不要になるため、
カテゴリは**機能ベース**に再整理する:

| カテゴリ | 意味 | 例 |
|---|---|---|
| `Shape` | 頂点位置を変形する | Lattice、CylindricalScale、CylindricalVertexTransform、(将来: Bend / Twist) |
| `Mask` | 適用範囲・重みを制御する | UVIslandMask、(将来: RegionMask) |
| `Utility` | 補助機能 | 必要になるまで空 |
| `Experimental` | 実験的機能 | — |

- カテゴリは「デフォーマ追加」メニューの階層と、スタック UI 上のバッジ表示に使う
- 流用元(Deform 由来か自作か)の区別は THIRD-PARTY-NOTICES とコードヘッダで管理し、
  ユーザー向け UI には出さない

#### 命名について(Deformable → DeformStack)

型名を旧 `Deformable` から変える理由:

1. **型名衝突の回避(必須要件)**: 移行期間中は旧フォークの `Deform.Deformable` と
   新コンポーネントがプロジェクト内に併存する。同名型が Add Component 検索に 2 つ並ぶと
   誤追加・混乱の原因になる
2. **責務の違いの明示**: 新コンポーネントは「自身では何もしないデフォーマの順序付きリスト」であり、
   ExecuteAlways で自己更新していた旧 Deformable とは挙動が異なる。名前で区別が付くようにする

既存利用者への導線(改名の負担をここで吸収する):

- `AddComponentMenu` を「NDMF Deform/Deform Stack (旧 Deformable)」とし、検索語に旧名を併記
- 旧 `Deformable` 選択時のインスペクタに「新コンポーネントへ変換」ボタンを表示(移行ツールへの入口)
- README に新旧コンポーネント対応表を掲載
- 表示名は型名と独立に決められるため、ユーザー向け表示を「Deformable」に寄せる選択も可能。
  最終的な表示名は M1 実装時に確定する

### 3.3 データフロー

```
[authoring]                    [NDMF Transforming]              [NDMF Preview]
DeformStack + DeformerBase --> BakeCore.Bake(originalMesh,  --> IRenderFilter.Instantiate 内で
(パッシブ、シリアライズのみ)     stack) → 新メッシュを           同じ BakeCore をプロキシ専有
                               AssetContainer へ                メッシュに対して実行
```

シーンのレンダラー・sharedMesh はオーサリング中一切書き換えない(旧 Deform の
「sharedMesh を非アセットのクローンに差し替える」動作を全廃)。変形の可視化は NDMF プレビューが担う。

## 4. 変形コア

### 4.1 MeshBuffers / DeformSpace

```csharp
public struct MeshBuffers : System.IDisposable
{
    public NativeArray<float3> Vertices;
    public NativeArray<float3> Normals;
    public NativeArray<float4> Tangents;
    public int Length;
    // 必要になったチャンネル(UV/Color 等)は流用元 Deform の NativeMeshData を参考に追加
}

public readonly struct DeformSpace
{
    public readonly float4x4 MeshToAxis;   // 旧 DeformerUtils.GetMeshToAxisSpace 相当をコアで一元計算
    public readonly float4x4 AxisToMesh;
}
```

- ベイクはエディタ実行のため、`Mesh.AcquireReadOnlyMeshData` を用い **Read/Write 有効化を要求しない**方針
  (実装時に検証。不可なら一時 readable コピーへフォールバック)。旧 Deform の `isReadable` 必須制約を撤廃する
- 頂点数・順序は常に保存する(SetVertices インプレース)。サブメッシュ・ボーンウェイト・バインドポーズは素通し

### 4.2 デフォーマの実装単位

ジョブ構造体は流用可能な純関数として維持する(Deform 上流の設計で最も価値のある層をそのまま踏襲):

```csharp
public override JobHandle Schedule(in MeshBuffers b, in DeformSpace s, JobHandle dep)
{
    if (Mathf.Approximately(factor, 0f)) return dep;
    return new CylindricalScaleJob {   // dev ブランチのジョブをほぼそのまま移植
        factor = factor, radius = radius, scope = scope, top = top, bottom = bottom,
        meshToAxis = s.MeshToAxis, axisToMesh = s.AxisToMesh, vertices = b.Vertices,
    }.Schedule(b.Length, 64, dep);
}
```

Deform 上流からのジョブ流用(Bend / Twist 等)は「必要になったら 1 個ずつ」。
ジョブ本体はコピーでき、コストはハンドル宣言と UITK インスペクタのみ。

### 4.3 マスク段

- マスクは「スタック中のその位置までの変形結果を、頂点ごとの重み w で開始時スナップショットとブレンドする」
  という Deform 互換のセマンティクスを採用(`v = lerp(v_snapshot, v_deformed, w)`)
- `UVIslandMask` は UV 島選択から重みバッファを生成するマスクとして移植する
- 将来: Cylindrical 系の円柱領域指定(radius/scope/top/bottom/axis + falloff)を
  「領域(Region)プリミティブ」として共通化し、3D 空間マスクにも流用する

### 4.4 ベイクパイプライン

1 レンダラーあたり:

```
originalMesh
 → MeshBuffers 構築
 → スタック順に Schedule() をチェーン → Complete
 → ブレンドシェイプ再ベイク:
      各フレーム f について deformedDelta_f = Deform(base + delta_f) − Deform(base)
      (法線・タンジェントデルタも同様。フレームは baked メッシュ上に再構築)
 → 法線: 「作り込み法線を保持(デフォルト)」/「再計算」の選択式
 → タンジェント: 法線「再計算」選択時のみ再構築(UV 無しは skip)。
      保持(既定)では作り込みタンジェント(髪ハイライト等)も保持する
 → bounds 再計算 → SMR.localBounds へ反映、updateWhenOffscreen は触らない
 → AssetContainer へ保存、レンダラーへ割当て
```

- ブレンドシェイプ再ベイクのコストは (シェイプ数 + 1) 回のチェーン実行。
  ベイク時のみ実行し(プレビューでは行わない)、フレームごとの差分頂点のみ処理する最適化を検討する
- MeshFilter/MeshRenderer 構成もサポート(型で分岐。ハードキャスト禁止)
- ベイク不能な構成(頂点数を変える将来のデフォーマ等)は NDMF の ErrorReport で警告する

## 5. NDMF 統合

- **Transforming フェーズ**: 各 `DeformStack` をベイクし、直後に自前コンポーネント
  (`DeformStack` / `DeformerBase`)を **component 単位で** `DestroyImmediate`
  (GameObject は削除しない — 現行の誤削除バグの再発防止を設計で保証)。
  デフォーマ専用だった空 GameObject の掃除はしない(害がなく、誤爆リスクだけがある)
- 旧実装の SetActive 総なめ・EditorOnly タグ判定は、`GetComponentsInChildren(includeInactive: true)` +
  タグ判定(自身と祖先)で置き換える
- **プレビュー(IRenderFilter)**:
  - `Instantiate` 内でプロキシ専有のメッシュコピーに対し BakeCore を実行(ブレンドシェイプ再ベイクは省略)
  - `ComputeContext.Observe` の対象は「スタック構成 + 各デフォーマのシリアライズ値 + 軸 Transform」。
    `DescribeHandles` が触るプロパティ集合から自動導出する(6.1)
  - `OnFrame` はメッシュ割当てとマテリアル等の引き継ぎのみ
  - **ドラッグ高速パス**: ハンドルドラッグ中は NDMF の無効化を経由せず、HandleBuilder が
    ベイクコアを直接叩いてプロキシメッシュの頂点だけを更新する(hot preview)。
    マウスアップで初めてシリアライズ値を確定し、通常の invalidation を 1 回だけ走らせる。
    これにより「毎ドラッグフレームのノード再構築」問題を回避する
- **依存宣言**: package.json に `vpmDependencies { "nadena.dev.ndmf": ">=1.5.0" }`(下限は実装時に確定)。
  asmdef の versionDefines にも下限式を入れる(現行の空式を廃止)

## 6. 編集 UI 基盤

### 6.1 HandleBuilder(宣言的シーンハンドル API)

デフォーマ作者は「何を編集させたいか」を宣言するだけにし、描画・Undo・複数選択・
プレハブ・座標変換・スクリーンスペースサイズはフレームワークが一元処理する。

```csharp
public override void DescribeHandles(HandleBuilder h)
{
    h.InAxisSpace(space => {
        space.RadiusSlider(nameof(radius), along: Axis3.Y);
        space.RadiusSlider(nameof(scope),  along: Axis3.Y, style: LineStyle.Dotted);
        space.RangeSlider(nameof(top), nameof(bottom), along: Axis3.Z);
        space.Circle(atZ: nameof(top),    radius: nameof(radius));
        space.Circle(atZ: nameof(bottom), radius: nameof(radius));
    });
}
```

設計要点:

- **プロパティ名バインド**(`nameof`)→ SerializedProperty 経由で編集。
  `Undo.RecordObject` の手書き(dev ブランチのエディタで 1 デフォーマ約190行 × ボイラープレート)を全廃
- 初期プリミティブは実需から確定したセットに限定する:
  `Position` / `AxisSlider` / `RadiusSlider` / `RangeSlider` / `Circle` / `Line` / `AngleDial` /
  `PointGrid`(要件は下記)
- 逃げ道として `h.Custom(Action<CustomHandleContext>)` を 1 つだけ用意(生 Handles 描画へのエスケープハッチ)

#### PointGrid(格子ハンドル)の要件

Lattice 編集の実用性はここで決まるため、「点の集合の表示と移動」ではなく以下を初期要件とする:

選択:

- クリック / Shift 追加 / 矩形(マーキー)選択
- **ループ選択**: 格子の行・列・シート(面)単位の一括選択。
  修飾キー + クリックで、クリックした制御点を含む軸方向の並びへ伝播させる(Blender の Alt+クリック相当)
- 全選択 / 反転 / 選択の隣接シートへの拡張
- 対称マッピング(ミラー編集): 選択と移動を対称側の制御点へ反映

可視性(重なった内側の点を選びやすくするための表示マスク):

- **奥点マスク**: デプステストにより、メッシュや手前の制御点に遮蔽された点を
  フェード表示または非表示にする切替(既定: フェード)
- **スライス表示**: 指定軸の 1 シートのみを表示し、格子内部の点を直接編集するモード
- 選択中の行/列/シートの強調表示と、非選択点の減光
- 距離に応じたハンドルサイズ補正(`HandleUtility.GetHandleSize`)

これらのモード切替(ループ選択軸・奥点マスク・スライス・ミラー・スナップ)は
EditorTool の UITK Overlay に集約する。
- 描画は共有の `EditorTool` 1 個の中で `Handles` API により行う。UITK Overlay がツール設定
  (ミラー ON/OFF・スナップ等)を出す。SceneView ハンドルが IMGUI ベースであることは API の背後に閉じる
- `DescribeHandles` が参照したプロパティ集合を記録し、NDMF プレビューの Observe 対象を自動導出する
- ドラッグ中は 5 章の hot preview を駆動する

API は先に完成させず、リファレンスクライアント 3 系統(Lattice / Cylindrical / UVIslandMask)が
同じ API で書けた時点でプリミティブを凍結する。

### 6.2 UITK インスペクタ基盤

- `[DeformerMeta]` 駆動の共通インスペクタフレーム(ヘッダ・説明・enable トグル)
- `DeformStack` インスペクタ: フォークで実装済みの UITK ListView + ドラッグ&ドロップ
  (`DeformableEditorExtention` / `ReorderableComponentElementListExtention` / `ComponentDropManipulator` /
  `listview.uxml/uss`)を移植。フォーク側 partial class 前提を外し、自前コンポーネント用に書き直す
- 既定はプロパティ自動生成(PropertyField)とし、カスタム UI が要るデフォーマだけ UXML を持つ

### 6.3 共有 UITK 部品

- `UVIslandSelectorView`: dev ブランチの UVIslandSelector(723行)+ UXML/USS をベースに共有部品化。
  UVIslandMask ブランチ側の解析改良(`UVIslandAnalyzer` 等)は取捨して取り込む
- テクスチャプレビュー / 拡大鏡(`MagnifyingGlassWindow` 由来)も部品化候補
- UVIslandMask ブランチの多層キャッシュ機構(Json/Binary/EditorPrefs/Optimal/Robust 各実装、
  性能計測・重複リファクタ群)は**移植しない**。ベイク時専用設計では
  「メッシュ(GUID + importer hash)単位で UV 島解析結果を 1 つキャッシュ」で足りる

## 7. 拡張 SDK(自作デフォーマの作り方)

新規デフォーマ 1 個に必要なもの:

1. `DeformerBase` 派生クラス + `[DeformerMeta]`(フィールド定義と Schedule 実装)
2. Burst ジョブ構造体(純関数。テスト対象)
3. `DescribeHandles` 宣言(数行)
4. (任意)UXML カスタムインスペクタ / 共有 UITK 部品の利用

EditMode テスト(golden mesh: 入力メッシュ + パラメータ → 期待頂点)をテンプレート化し、
デフォーマ追加時の検証をコピペで書けるようにする。

## 8. 既存資産の移植マップ

| 資産 | 出所 | 扱い |
|---|---|---|
| Lattice ジョブ | Deform 上流(MIT) | ジョブ流用 |
| Lattice ミラー(MirroredLatticeJob 223行 + エディタ改修) | フォーク(自作) | 新 LatticeDeformer に統合。ミラーは PointGrid の対称マッピングとして一般化 |
| Lattice ハンドル UX | Deform 上流エディタ(847行)を参考 | PointGrid プリミティブとして再実装 |
| CylindricalScale / CylindricalVertexTransform | `origin/dev`(自作) | ジョブほぼそのまま移植。エディタは DescribeHandles 宣言に置換 |
| UVIslandMask 本体 + UVIslandSelector | `origin/dev`(自作、コンパクト版) | マスク段 + 共有 UITK 部品として移植 |
| UV 島解析の改良 | `origin/UVIslandMask` | 有用部分のみ取捨(多層キャッシュ・重複エディタは破棄) |
| UITK ListView / D&D インスペクタ | フォーク(自作) | DeformStack インスペクタとして移植 |
| IExDeformer のメタデータ概念 | `origin/UVIslandMask` | `[DeformerMeta]` として吸収(CompatibleDeformVersion は廃止) |
| Bend / Twist 等の追加デフォーマ | Deform 上流(MIT) | 需要が出たらジョブ流用で追加 |

## 9. 移行(既存シーン)

- 旧 `Deformable` + `LatticeDeformer`(ミラー拡張含む)・ExDeform 各コンポーネントから
  新コンポーネントへ変換する**一回きりのエディタツール**を提供する
- 変換ツールは旧アセンブリ(フォーク Deform)がプロジェクトに残っている状態で実行する設計とし、
  **フォーク削除より 1 リリース前に出荷**する(Missing MonoScript 化の防止)
- インストールベースが極小の今が互換性を切れる唯一のタイミングであることを README に明記する

## 10. マイルストーン

各マイルストーンは単独でリリース可能な単位とする(ホビー開発の中断耐性を優先)。

| M | 内容 | 出荷物 |
|---|---|---|
| M0 | リポジトリ再編: 新 asmdef 骨格、`Documentation~`、CI(ビルド検証)、フォークは併存のまま | — |
| M1 | BakeCore + `DeformStack`/`DeformerBase` + CylindricalScale 移植。NDMF Transforming ベイク(E2E 最短経路)+ 簡易プレビュー | 0.1.0(実験版) |
| M2 | HandleBuilder(基本プリミティブ)+ Cylindrical 系ハンドル + hot preview | 0.2.0 |
| M3 | Lattice + PointGrid(選択・矩形・ミラー) | 0.3.0 |
| M4 | UVIslandMask(マスク段 + UVIslandSelectorView) | 0.4.0 |
| M5 | ブレンドシェイプ再ベイク + タンジェント + 法線保持オプション + golden mesh テスト整備 | 0.5.0 |
| M6 | 移行ツール → フォーク削除 → パッケージング整備(vpmDependencies、タグ、VPM リスティング、THIRD-PARTY-NOTICES)。UITK Font Fix(jp.colloid.uitk-font-fix)も同じ VPM リスティングへ登録し、vpmDependencies による自動取得を成立させる。リリースからリスティングへの通知(repository_dispatch)も両パッケージで揃える。将来的には OpenUPM への登録も併用し、UPM 経路でも依存自動解決を可能にする(スコープドレジストリ追加が前提) | 0.1.0 / 0.2.0 |
| M7(候補) | **Bake as BlendShape**: デフォーマ効果をブレンドシェイプとして焼き込み、Animator / Udon から駆動(§2.1 の橋渡し機能) | 0.x |
| M8(候補) | **非アバターメッシュの手動ベイク**: AvatarDescriptor の無いオブジェクトの DeformStack を、エディタ操作(インスペクタのボタン等)で静的メッシュアセットとしてベイク・保存する。0.1.1 でプレビュー / ビルド対象をアバタールート配下に限定したため、アバター外(小物・ワールド制作等)での利用経路はこの機能が担う | 0.x |
| M9(候補) | **NDMF 非依存プロジェクト対応**: 変形コア(Runtime / Editor)は NDMF を参照していないため、NDMF 接続層(NDMFDeform.NDMF asmdef)を Version Defines で任意化し、NDMF の無いプロジェクトでは手動ベイク(M8)中心で動作させる。NDMF プレビュー(IRenderFilter)は使えないため簡易プレビューの代替を検討。`vpmDependencies` の必須依存の扱い(推奨依存化 / 配布チャネル分離)も要検討 | 0.x |

リリース経路(2026-08-29 決定、同日 0.x 管理へ改訂): 旧パッケージと同名(`jp.colloid.nemfdeform`)の
ため、フォーク削除版へ直接更新すると旧コンポーネントが Missing Script になり移行不能になる。そこで

- **0.1.0(移行リリース)** = フォーク同梱 + v2 + 移行ツール。既存ユーザーはここで移行する
- **0.2.0** = フォーク削除後の本体。1.0.0 は安定後まで先送りし、当面 0.x で管理する

配布は VPM で `Packages/` へ展開し、`package.json` の **`legacyFolders`**(`Assets\NDMFDeform`)
により旧 Assets 展開のフォルダを VCC/ALCOM が自動削除する。フォークの .meta GUID は
リポジトリ由来で新旧同一のため、旧フォルダ削除 → パッケージ内フォークへの参照差し替えは
GUID 一致でシーン参照が維持され、その状態で移行ツールを実行できる。
上表 M1〜M5 の出荷物列は当初計画の番号であり、実際には 0.1.0 に一括で含まれる。
VPM リスティングは c-colloid/vpm から配信し(公開 URL は
https://c-colloid.github.io/vpm/index.json)、NDMFDeform と UITK Font Fix の
両方を登録する。リスティング側は各リポジトリの Releases を走査し、
`package.json` と `{name}-{version}.zip` の両アセットを持つリリースだけを取り込む。
収集はスケジュール実行(6 時間ごと)が基本だが、反映を待たせないため
release.yml のリリース確定後に `repository_dispatch`(`event_type: package-released`)
を送って即時再生成させる。通知はリリースの付随処理であり、
失敗してもリリースは成功のまま(スケジュール実行が予備として拾う)。

現行 main(v0.0.x 系)への Phase 0 的なバグ修正(GameObject 誤削除・SMR キャスト等)は、
v2 完成まで既存ユーザーが v0.0.x を使い続ける場合にのみ適用を検討する(別作業)。

## 11. リスクと未解決事項

1. **プレビュー性能**: 70k 頂点級アバターでの Instantiate 内フルベイクと hot preview の実測が未実施。
   M1 で最初に計測し、必要ならチャンク化・チャンネル限定更新を入れる
2. **ボーン/バインドポーズ非補正**: 体型変更系デフォームで骨格・IK・PhysBone が視覚とずれる。
   v2 では文書化 + ビルド時警告(ウェイトの大きい領域への大変形を検知)。補正は将来課題
3. **NDMF API の変化**: IRenderFilter / preview API は活発に開発中。下限バージョン宣言と
   薄い接続層(NDMFDeform.NDMF アセンブリに隔離)で影響範囲を限定する
4. **競合(32ba Mesh Deformation Tool)**: Lattice 単体では正面衝突。差別化は
   ミラー編集・スタック可能な複数デフォーマ・UV 島マスク・拡張 SDK・ブレンドシェイプ正しさ
5. **ブレンドシェイプ再ベイクのビルド時間**: シェイプ数 +1 回のチェーン実行。
   差分頂点限定処理と per-mesh オプトアウトを用意する
6. **Unity 6**: ジョブ・UITK・Mesh API は低リスク層だが、M1 時点から Unity 6 beta での
   コンパイル確認を CI に含める

## 12. ライセンス

- Deform(keenanwoodall、MIT)由来のジョブコード等を流用するファイルには出典ヘッダを付し、
  `THIRD-PARTY-NOTICES.md` に MIT 全文と著作権表示を記載する
- 本パッケージ自体のライセンスは現行を踏襲(LICENSE 参照)
