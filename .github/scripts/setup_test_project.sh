#!/bin/bash
# NDMFDeform v2 のテスト用 Unity プロジェクトを組み立てる。
# v2 アセンブリのみを埋め込みパッケージとして配置する
# (レガシー NDMFPlugin と Deform フォークは VRChat SDK 必須のため除外)。
# usage: setup_test_project.sh <project_dir> [ndmf_ref]
set -euo pipefail

PROJ="${1:?usage: setup_test_project.sh <project_dir> [ndmf_ref]}"
NDMF_REF="${2:-1.14.8}"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

mkdir -p "$PROJ/Assets" "$PROJ/ProjectSettings" "$PROJ/Packages"
echo "m_EditorVersion: 2022.3.22f1" > "$PROJ/ProjectSettings/ProjectVersion.txt"

# 本パッケージ(フィルタ済み)を埋め込み — 埋め込みパッケージのテストは自動で Test Runner 対象になる
PKG="$PROJ/Packages/jp.colloid.nemfdeform"
rm -rf "$PKG" && mkdir -p "$PKG"
(cd "$REPO_ROOT" && tar -cf - \
    --exclude=./.git --exclude=./.github --exclude='./Documentation~' \
    --exclude=./Deform --exclude=./Deform.meta \
    --exclude=./NDMFPlugin --exclude=./NDMFPlugin.meta \
    --exclude=./NDMFDeform.asmdef --exclude='./NDMFDeform.asmdef.meta' \
    --exclude=./TestProject \
    .) | tar -xf - -C "$PKG"

# NDMF: リリース zip は Dependencies~ を実フォルダへ変換して配布しているが、
# git clone ではそのままのため手動でリネームして DLL(System.Collections.Immutable 等)を取り込む
NDMF="$PROJ/Packages/nadena.dev.ndmf"
if [ ! -d "$NDMF" ]; then
    git clone --depth 1 --branch "$NDMF_REF" https://github.com/bdunderscore/ndmf.git "$NDMF"
    rm -rf "$NDMF/.git"
fi
if [ -d "$NDMF/Dependencies~" ]; then
    mv "$NDMF/Dependencies~" "$NDMF/Dependencies"
fi

cat > "$PROJ/Packages/manifest.json" <<'MANIFEST'
{
  "dependencies": {
    "com.unity.burst": "1.8.13",
    "com.unity.mathematics": "1.2.6",
    "com.unity.collections": "2.1.4",
    "com.unity.test-framework": "1.1.33",
    "com.unity.modules.animation": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.modules.unityanalytics": "1.0.0"
  }
}
MANIFEST

echo "test project ready: $PROJ"
