#!/usr/bin/env python3
"""画面に出る文字がフォント（DotGothic16）にあるかを確かめる。無い文字は□になって表示されない。

使い方: python3 tools/check_glyphs.py   （fontTools が必要: pip install fonttools）
"""
import glob
import re
import sys

from fontTools.ttLib import TTFont

cmap = TTFont("src/Abbild/Content/fonts/DotGothic16-Regular.ttf").getBestCmap()
missing: dict[str, set[str]] = {}
for f in glob.glob("src/**/*.cs", recursive=True) + glob.glob("src/Abbild/Content/*.txt"):
    s = open(f, encoding="utf-8").read()
    text = "".join(re.findall(r'"((?:[^"\\]|\\.)*)"', s)) if f.endswith(".cs") else s
    for ch in text:
        if ord(ch) >= 32 and ord(ch) not in cmap:
            missing.setdefault(ch, set()).add(f)
for ch, fs in sorted(missing.items()):
    print(f"{ch!r} U+{ord(ch):04X}: {', '.join(sorted(fs))}")
sys.exit(1 if missing else 0)
