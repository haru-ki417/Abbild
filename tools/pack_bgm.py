#!/usr/bin/env python3
"""BGM（Ogg）を 1 つのファイル bgm.dat にまとめる。

配布元（OpenTracks）の規約で、遊ぶ人が音声ファイルとしてかんたんに取り出せる状態での
利用が禁止されているため、Ogg をそのまま配布物に入れず、名前ごとの鍵でかき混ぜてまとめる。
形式とかき混ぜ方は src/Abbild.Core/ContentPack.cs と同じ。

使い方:
    python3 tools/pack_bgm.py <Ogg のフォルダー> src/Abbild/Content/bgm.dat
    （フォルダーの中の *.ogg を、拡張子を除いた名前で入れる）
"""
import struct
import sys
from pathlib import Path

MAGIC = b"ABPK"
VERSION = 1


def seed(name: str) -> int:
    h = 2166136261
    for b in name.encode("utf-8"):
        h ^= b
        h = (h * 16777619) & 0xFFFFFFFF
    h ^= 0x5A17C0DE
    return h if h != 0 else 0x1234567


def scramble(data: bytes, name: str) -> bytes:
    x = seed(name)
    out = bytearray(data)
    for i in range(len(out)):
        x ^= (x << 13) & 0xFFFFFFFF
        x ^= x >> 17
        x ^= (x << 5) & 0xFFFFFFFF
        out[i] ^= x & 0xFF
    return bytes(out)


def main() -> None:
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(1)
    src, dst = Path(sys.argv[1]), Path(sys.argv[2])
    files = sorted(src.glob("*.ogg"))
    if not files:
        sys.exit(f"{src} に .ogg がありません")
    entries = [(f.stem, f.read_bytes()) for f in files]
    header = 12 + sum(2 + len(n.encode("utf-8")) + 8 for n, _ in entries)
    out = bytearray(MAGIC + struct.pack("<II", VERSION, len(entries)))
    offset = header
    for name, data in entries:
        nb = name.encode("utf-8")
        out += struct.pack("<H", len(nb)) + nb + struct.pack("<II", offset, len(data))
        offset += len(data)
    for name, data in entries:
        out += scramble(data, name)
    dst.write_bytes(bytes(out))
    print(f"{dst}: {len(entries)} 曲（{', '.join(n for n, _ in entries)}）")


if __name__ == "__main__":
    main()
