# NDMFDeform

VRChat アバターのメッシュを**非破壊で変形**する [NDMF](https://github.com/bdunderscore/ndmf) プラグインです。
シーンのメッシュには一切触れず、ビルド時(と NDMF プレビュー)にだけ変形を適用します。

Non-destructive mesh deformation plugin for VRChat avatars, built on NDMF.

## 機能

- **Lattice** — 格子の制御点でメッシュを自由変形
  - ミラー編集、クリック / 矩形 / 行・リング・シート選択、Ctrl+ドラッグの軸スワイプ選択
  - 解像度変更時は既存の変形をリサンプリングして引き継ぎ
- **UV Island Mask** — 選択した UV 島の変形を打ち消すマスク(反転で「島だけに変形を残す」)
  - UV マップ上のクリック選択(ズーム / パン / サブメッシュフィルタ付き)
  - シーンビューのメッシュ面クリックでも選択可能(変形後の形状に追従してハイライト)
- **Cylindrical Scale / Cylindrical Vertex Transform** — 円柱コントローラによる範囲変形
- **Body Fit** — 参照した体のメッシュに沿って衣装を寄せる / 離す(非対応衣装の体合わせ)
  - 二重球で適用範囲を指定し、体との隙間の帯(最小 / 最大)で「ぴったり」「ブカッと」を調整
  - ヒューマノイド骨格から体と衣装のパーツ(胴・腕・脚…)を把握し、パーツごとのボーン軸を基準に
    放射状に動かすため、装飾が潰れず、腕の紐飾りが胴に吸われない(骨格が無ければ最近接表面にフォールバック)
  - 衣装のパーツ所属は UV 島(または連結成分)を単位に、ボーンウェイト(ボーン名 / ヒューマノイド対応で
    位置に頼らず対応付け)と体の形状を投票して決定。食い違うグループは「要確認」として一覧し、手動で指定できる
  - めり込んだ頂点は体の外側へ押し出し、凹んだ部位で布が折れないよう変位を平滑化
  - 衣装のブレンドシェイプは形状を維持したまま再ベイク。体側のシェイプ重みも反映
  - 重ね着(下着 → 服 → コート)は参照先を先にベイクする順序を自動解決
  - 設計の詳細は [Documentation~/body-fit-deformer.md](Documentation~/body-fit-deformer.md)
- **Transform / Scale** — Transform への補間・軸スケール(旧 Deform 互換)
- **Sphere / Box / Vertical Gradient / Vertex Color Mask** — 領域・グラデーション・頂点カラーによるマスク(旧 Deform 互換)
- **正しいベイク**
  - ブレンドシェイプは各フレームを `Deform(base + delta) − Deform(base)` で再ベイク
    (変形後も表情・リップシンクが壊れません)
  - 非線形な変形を横切るシェイプには中間フレームを自動挿入(瞬きの軌道補正)
  - シェイプ別の「作った形を維持」モード(「太さ0」のような絶対ターゲット向け)
  - 法線・タンジェントは「作り込みを保持(既定)/ 再計算」を選択式
- エディタ UI は UITK 製。[UITK Font Fix](https://github.com/c-colloid/UITKFontFix) 導入時は日本語表示も最適化

## インストール

1. VCC / ALCOM に VPM リポジトリを追加: **https://c-colloid.github.io/vpm/**
2. プロジェクトに `NDMFDeform` を追加(NDMF / UITK Font Fix は依存として自動導入されます)

## 旧バージョン(0.0.x)からの移行

**必ず 0.1.0 を経由してください。** 0.1.0 は移行専用リリースで、旧 Deform フォークと
新実装(v2)、移行ツールを同梱しています。フォーク削除後のバージョン(0.2.0 以降)へ
直接更新すると、旧コンポーネントが Missing Script になり移行できなくなります。

1. パッケージを **0.1.0** に更新
   (VPM 導入時、旧 `Assets\NDMFDeform` フォルダは自動削除されます。
   別の場所に展開していた場合は手動で削除してください)
2. メニュー **Tools > NDMF Deform > 旧 Deformable から移行...** を実行
   - `Deformable` + `LatticeDeformer` が `DeformStack` + 新 `LatticeDeformer` に変換されます
   - Lattice 以外の旧デフォーマ(Bend / Twist など)は移行されず一覧に報告されます
3. 動作確認後、**0.2.0 以降**(フォーク削除版)へ更新

> 0.x 系のあいだは互換性が変わることがあります。インストールベースが小さい今の時期に
> 旧アーキテクチャとの互換を清算しています。

## 使い方(最小)

1. 変形したいレンダラー(SkinnedMeshRenderer など)の GameObject をヒエラルキーで右クリック →
   **NDMF Deform > Deformers > Lattice** などデフォーマを選択
   (Deform Stack が無ければ自動で追加され、デフォーマは子 GameObject として作成・登録されます)
2. NDMF プレビューで確認しながら編集 → アバタービルド時に自動でベイクされます

コンポーネントメニュー(Add Component > NDMF Deform)から手動で組む場合は、
レンダラーの GameObject に **Deform Stack** を追加し、子のデフォーマを一覧に登録してください。

各デフォーマの操作方法はインスペクタの「操作ガイド」を参照してください。

## 既知の制限

- ボーン・バインドポーズの補正は行いません(大きく骨格を動かす用途は対象外)
- Body Fit はボーンウェイトを変更しません(体のウェイトを衣装へ転写する機能は別途検討中)。
  三角形単位の交差や衣装の自己交差は保証しません(隙間と平滑化で緩和します)
- シーンビューの島選択などは、バインドポーズから大きく外れたポーズでは判定がずれることがあります

## ライセンス

MIT License。ベイクジョブの一部数式は [keenanwoodall/Deform](https://github.com/keenanwoodall/Deform)
(MIT)由来です。詳細は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。
