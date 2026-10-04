"""アイコン（剣と盾の紋章）をドット絵として描き、すべての大きさの画像を書き出す。

    python tools/make_icon.py

64×64 の升目に、主人公の盾と同じ配色（青地・金の縁・金の太陽）で描く。
小さいアイコン（16・24 px）は剣を省いて盾だけを大きく描いた簡略版を使う。

書き出すもの:
  src/Abbild/Icon.ico                       Windows の実行ファイルのアイコン（16〜256 px）
  src/Abbild/Icon.bmp                       ウィンドウのアイコン（64 px）
  src/Abbild.Web/wwwroot/icons/icon-*.png   ブラウザー版（タブ・ホーム画面・読み込み画面）
"""
from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
G = 64  # 升目の数

OUT = (14, 9, 8)
NAVY = [(7, 9, 24), (11, 15, 38), (16, 22, 54), (22, 30, 72), (30, 40, 92)]
BLUE = [(16, 28, 84), (28, 52, 142), (48, 90, 198), (110, 160, 244)]
GOLD = [(104, 56, 14), (178, 112, 30), (232, 174, 60), (255, 228, 140), (255, 248, 214)]
STEEL = [(58, 68, 96), (122, 138, 170), (190, 204, 226), (244, 248, 255)]
LEATHER = [(58, 28, 14), (104, 56, 26), (140, 82, 40)]
RUBY = [(120, 10, 30), (214, 36, 60), (255, 140, 150)]

# 形は 48 の大きさの座標で決め、64 の升目に写す（zoom で拡大）
SHIELD_OUTER = [(14, 9), (34, 9), (34, 22), (32.8, 27.5), (29.8, 32), (24, 37.5), (18.2, 32), (15.2, 27.5), (14, 22)]
SHIELD_INNER = [(17, 12), (31, 12), (31, 22), (30, 26.6), (27.4, 30.4), (24, 33.8), (20.6, 30.4), (18, 26.6), (17, 22)]


def inside(poly, x, y):
    c = False
    for i in range(len(poly)):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % len(poly)]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            c = not c
    return c


def draw(zoom: float, swords: bool, center_y: float) -> Image.Image:
    img = [[None] * G for _ in range(G)]

    def put(x, y, c):
        if 0 <= x < G and 0 <= y < G:
            img[y][x] = c

    def m(v):
        return 24 + (v - 32) * (48 / 64) / zoom

    # 背景: 紺の同心円と、盾の後ろの淡い光
    for y in range(G):
        for x in range(G):
            d = math.hypot(x - 31.5, y - center_y) / 33
            img[y][x] = NAVY[min(int(max(0, 1 - d) * 4.6), 4)]
            g = math.hypot(x - 31.5, (y - center_y + 1) * 0.9)
            if g < 17 and not (g > 15.5 and (x + y) % 2):
                img[y][x] = (34, 40, 84) if g < 12 else (27, 33, 70)

    def raster(shape):
        cells = {}
        for y in range(G):
            for x in range(G):
                c = shape(m(x + 0.5), m(y + 0.5))
                if c:
                    cells[(x, y)] = c
        return cells

    def stamp(cells):
        for (x, y) in cells:  # 1 マスの縁取り
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if (x + dx, y + dy) not in cells:
                    put(x + dx, y + dy, OUT)
        for (x, y), c in cells.items():
            put(x, y, c)

    def sword(px, py, dx, dy):
        n = math.hypot(dx, dy)
        dx, dy = dx / n, dy / n
        nx, ny = -dy, dx

        def f(x, y):
            u = (x - px) * dx + (y - py) * dy
            v = (x - px) * nx + (y - py) * ny
            if 0 <= u < 2.6 and abs(v) <= 1.6:  # 柄頭
                return GOLD[3] if v < -0.3 and u < 1.6 else GOLD[2]
            if 2.6 <= u < 7.4 and abs(v) <= 0.75:  # 握り
                return LEATHER[2] if int(u * 1.4) % 2 == 0 else LEATHER[1]
            if 7.4 <= u < 9.4 and abs(v) <= 5.2:  # 鍔
                if abs(v) > 4.2:
                    return GOLD[2]
                return GOLD[3] if u < 8.3 else GOLD[1]
            if 9.4 <= u <= 46:  # 刃
                w = 1.55 if u < 42 else 1.55 * (46 - u) / 4
                if abs(v) <= w:
                    if abs(v) < 0.5:
                        return STEEL[3] if u < 14 or int(u) % 7 == 0 else STEEL[2]
                    return STEEL[2] if v < 0 else STEEL[1]
            return None

        return raster(f)

    if swords:  # 盾の後ろで交差する 2 本の剣（刃先は上、柄は下）
        stamp(sword(7.0, 41.6, 1, -1))
        stamp(sword(41.0, 41.6, -1, -1))

    def shield(x, y):
        if not inside(SHIELD_OUTER, x, y):
            return None
        if inside(SHIELD_INNER, x, y):  # 青地（左上から光）
            if not inside(SHIELD_INNER, x - 1.0, y) or not inside(SHIELD_INNER, x, y - 1.0):
                return BLUE[3] if x + y < 46 else BLUE[2]
            k = (x - 17) / 14 * 0.55 + (y - 12) / 22 * 0.75
            return BLUE[2] if k < 0.42 else BLUE[1] if k < 0.95 else BLUE[0]
        if not inside(SHIELD_INNER, x + 1.0, y + 1.0) and inside(SHIELD_INNER, x - 1.2, y - 1.2):
            return GOLD[0]
        k = (x - 14) / 20 * 0.6 + (y - 9) / 28 * 0.65  # 金の縁
        if k < 0.16:
            return GOLD[4]
        return GOLD[3] if k < 0.36 else GOLD[2] if k < 0.68 else GOLD[1]

    stamp(raster(shield))

    cx, cy = 24, 21

    def sun(x, y):  # 金の太陽と、まん中の紅玉
        dx, dy = x - cx, y - cy
        d = math.hypot(dx, dy)
        a = math.atan2(dy, dx)
        if d <= 1.2:
            return RUBY[2] if dx < 0 and dy < 0 else RUBY[1]
        if d <= 2.6:
            return GOLD[3] if dx + dy < -0.8 else GOLD[2]
        if d <= 3.3:
            return GOLD[0]
        sector = round(a / (math.pi / 8))
        off = abs(a - sector * math.pi / 8)
        length = 7.9 if sector % 4 == 0 else 6.6 if sector % 2 == 0 else 5.0
        if 3.3 < d <= length and off * d <= 1.05 * (length - d) / (length - 3.3) + 0.02:
            return GOLD[3] if dx + dy < 0 else GOLD[2]
        return None

    stamp(raster(sun))

    if swords:
        def sparkle(x, y, big=True):
            put(x, y, (255, 255, 255))
            for d in ([1, 2] if big else [1]):
                c = (255, 250, 220) if d == 1 else (200, 190, 150)
                for ex, ey in ((d, 0), (-d, 0), (0, d), (0, -d)):
                    put(x + ex, y + ey, c)

        sparkle(20, 15)
        sparkle(55, 9, False)
        sparkle(44, 24, False)

    out = Image.new('RGB', (G, G))
    for y in range(G):
        for x in range(G):
            out.putpixel((x, y), img[y][x])
    return out


def rounded(square: Image.Image, size: int) -> Image.Image:
    big = square.resize((1024, 1024), Image.NEAREST)
    mask = Image.new('L', big.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, 1023, 1023], radius=205, fill=255)
    r = big.convert('RGBA')
    r.putalpha(mask)
    return r.resize((size, size), Image.LANCZOS)


def main():
    full = draw(zoom=1.06, swords=True, center_y=29)
    small = draw(zoom=1.42, swords=False, center_y=33)

    frames = [rounded(small, s) for s in (16, 24)] + [rounded(full, s) for s in (32, 48, 64, 128, 256)]
    frames[-1].save(ROOT / 'src/Abbild/Icon.ico', sizes=[f.size for f in frames], append_images=frames[:-1])
    full.resize((64, 64), Image.NEAREST).save(ROOT / 'src/Abbild/Icon.bmp')

    web = ROOT / 'src/Abbild.Web/wwwroot/icons'
    rounded(full, 64).save(web / 'icon-64.png')  # タブのアイコン（角を丸める）
    for s in (192, 512):  # ホーム画面用（角は OS が丸める）
        full.resize((1024, 1024), Image.NEAREST).resize((s, s), Image.LANCZOS).save(web / f'icon-{s}.png')
    print('icons written')


if __name__ == '__main__':
    main()
