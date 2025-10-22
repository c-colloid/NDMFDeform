# ExDeform Overview

ExDeformは、[Deform](https://github.com/keenanwoodall/Deform)フレームワークの拡張機能コレクションです。
Unityでメッシュ変形処理をより柔軟かつ直感的に制御するための追加コンポーネントを提供します。

## What is ExDeform?

ExDeformは、Deformフレームワークに以下の機能を追加します：

- **高度なマスキング機能** - UV座標やペイントなど、様々な方法でメッシュ変形範囲を制御
- **直感的なエディタ体験** - ビジュアルプレビューとインタラクティブな選択UI
- **非同期処理** - 重い処理をバックグラウンドで実行し、エディタの応答性を維持
- **NDMF統合** - Non-Destructive Modular Frameworkとシームレスに連携

Deformの強力なメッシュ変形エンジンに、より細かい制御とワークフローの改善を提供することで、
VRChat Avatarのセットアップや3Dモデリングワークフローを効率化します。

## Features

### 実装済み機能

#### UVIslandMask
UVアイランド単位でメッシュ変形範囲を制御できるマスクコンポーネント。

**主な機能:**
- インタラクティブなUVマッププレビュー
- 島単位・範囲選択での柔軟な選択
- サブメッシュ対応
- 非同期初期化による軽快な動作
- シーンビューでのリアルタイムハイライト表示

**詳細:** [UVIslandMask Architecture](./UVIslandMask/Architecture.md)

## Links

### ドキュメント
- [UVIslandMask詳細ドキュメント](./UVIslandMask/Architecture.md)

### 関連プロジェクト
- [Deform](https://github.com/keenanwoodall/Deform) - ベースとなるメッシュ変形フレームワーク
- [NDMF](https://github.com/bdunderscore/ndmf) - Non-Destructive Modular Framework

### コミュニティ
- [VRChat Technical Discussion](https://ask.vrchat.com/) - VRChatでの活用事例

---

**Note:** このドキュメントは将来的にDocusaurusやGitHub Pagesを使用した
Webサイトとして公開される予定です。
