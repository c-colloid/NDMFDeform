#!/usr/bin/env python3
"""Unity パッケージの静的検証。

- package.json / *.asmdef が有効な JSON であること
- Unity が取り込む全ファイル・フォルダに .meta が存在すること
- 孤立した .meta(対象実体のないもの)が存在しないこと

Deform/(サブモジュール、独自に .meta を持つ)、ドットファイル、~ 終端フォルダは対象外。
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP_DIRS = {".git", ".github", "Deform"}

errors = []


def rel(path):
    return os.path.relpath(path, ROOT)


def unity_visible(name):
    return not name.startswith(".") and not name.endswith("~")


for dirpath, dirnames, filenames in os.walk(ROOT):
    if dirpath == ROOT:
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
    dirnames[:] = sorted(d for d in dirnames if unity_visible(d))
    filenames = sorted(f for f in filenames if unity_visible(f))

    entries = [d for d in dirnames] + [f for f in filenames if not f.endswith(".meta")]
    # サブモジュール Deform はフォルダ実体のみ対象(中身は独自リポジトリで管理)
    if dirpath == ROOT and os.path.isdir(os.path.join(ROOT, "Deform")):
        entries.append("Deform")

    metas = {f for f in filenames if f.endswith(".meta")}

    # JSON 検証
    for f in filenames:
        if f.endswith(".asmdef") or f == "package.json":
            path = os.path.join(dirpath, f)
            try:
                with open(path, encoding="utf-8-sig") as fp:
                    json.load(fp)
            except Exception as exc:
                errors.append(f"invalid JSON: {rel(path)}: {exc}")

    # .meta 整合性
    for entry in entries:
        if entry + ".meta" not in metas:
            errors.append(f"missing .meta: {rel(os.path.join(dirpath, entry))}")
    for meta in metas:
        target = meta[: -len(".meta")]
        if target not in entries:
            errors.append(f"orphan .meta: {rel(os.path.join(dirpath, meta))}")

if errors:
    print(f"{len(errors)} problem(s) found:")
    for e in errors:
        print(f"  {e}")
    sys.exit(1)

print("package validation OK")
