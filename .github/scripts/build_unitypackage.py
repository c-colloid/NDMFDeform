#!/usr/bin/env python3
"""リポジトリから「Packages/ に展開される」.unitypackage を生成する。

Unity 本体を使わず、コミット済みの .meta の GUID から
unitypackage 形式(GUID ディレクトリ + pathname / asset / asset.meta の tar.gz)を
組み立てる。pathname は Unity のエクスポートと同じくプレーンパス(改行なし)。

- 各エントリのパスは Packages/<package.json の name>/<リポジトリ相対パス>
- .meta の無いファイル(ドットファイルや Documentation~ 等、Unity が無視するもの)は含めない
- 除外: .git / .github / TestProject、パス要素がドットで始まるもの、*.zip / *.unitypackage

使い方: build_unitypackage.py <リポジトリルート> <出力パス>
"""
import io
import json
import os
import re
import sys
import tarfile

EXCLUDE_TOP = {".git", ".github", "TestProject"}
EXCLUDE_SUFFIXES = (".zip", ".unitypackage")
GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.MULTILINE)


def fail(message):
    print(f"::error::{message}")
    sys.exit(1)


def iter_assets(root):
    """(リポジトリ相対パス, ディレクトリか) を決定的な順序で列挙する"""
    for base, dirs, files in os.walk(root):
        rel_base = os.path.relpath(base, root)
        if rel_base == ".":
            dirs[:] = sorted(d for d in dirs if d not in EXCLUDE_TOP and not d.startswith("."))
        else:
            dirs[:] = sorted(d for d in dirs if not d.startswith("."))

        for d in dirs:
            yield (os.path.join(rel_base, d) if rel_base != "." else d), True
        for f in sorted(files):
            if f.startswith(".") or f.endswith(EXCLUDE_SUFFIXES) or f.endswith(".meta"):
                continue
            yield (os.path.join(rel_base, f) if rel_base != "." else f), False


def add_bytes(tar, name, data):
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mode = 0o644
    tar.addfile(info, io.BytesIO(data))


def main():
    if len(sys.argv) != 3:
        fail("usage: build_unitypackage.py <repo_root> <output>")
    root, output = sys.argv[1], sys.argv[2]

    with open(os.path.join(root, "package.json"), encoding="utf-8") as f:
        package_name = json.load(f)["name"]
    prefix = f"Packages/{package_name}"

    seen_guids = {}
    count_files = 0
    count_dirs = 0
    skipped = []

    with tarfile.open(output, "w:gz") as tar:
        for rel, is_dir in iter_assets(root):
            meta_path = os.path.join(root, rel + ".meta")
            if not os.path.isfile(meta_path):
                skipped.append(rel)
                continue
            with open(meta_path, "rb") as f:
                meta = f.read()
            match = GUID_RE.search(meta.decode("utf-8", errors="replace"))
            if not match:
                fail(f"{rel}.meta に guid がありません")
            guid = match.group(1)
            if guid in seen_guids:
                fail(f"GUID 重複: {rel} と {seen_guids[guid]} が {guid} を共有しています")
            seen_guids[guid] = rel

            unity_path = f"{prefix}/{rel.replace(os.sep, '/')}"
            add_bytes(tar, f"{guid}/pathname", unity_path.encode("utf-8"))
            add_bytes(tar, f"{guid}/asset.meta", meta)
            if is_dir:
                count_dirs += 1
            else:
                with open(os.path.join(root, rel), "rb") as f:
                    add_bytes(tar, f"{guid}/asset", f.read())
                count_files += 1

    print(f"{output}: files={count_files} dirs={count_dirs} -> {prefix}/")
    if skipped:
        print(f".meta なしのため除外 ({len(skipped)}): {', '.join(skipped[:10])}"
              + (" ..." if len(skipped) > 10 else ""))
    if count_files == 0:
        fail("ファイルが 1 つも含まれていません")


if __name__ == "__main__":
    main()
