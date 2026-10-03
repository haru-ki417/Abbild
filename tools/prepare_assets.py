#!/usr/bin/env python3
"""元の WinForms 版の素材から、ゲームで使う形の素材を作る。

使い方:
    python3 tools/prepare_assets.py <元プロジェクトの Abbild フォルダー> [--fonts <フォントのフォルダー>]

元フォルダーには Resources/ と bin/Debug/ゲーム用モンスター素材/ がある前提。
出力先は src/Abbild/Content/。何度実行しても同じ結果になる。

- 背景: 16:9 に中央で切り抜き、1920x1080 の JPEG にする
- 敵・主人公 (JPEG, 白背景): 外側の白をつながりで透過し、余白を詰めて PNG にする
- ローズミルクティー様の素材 (PNG): 待機・攻撃のコマを横一列の帯にまとめる
- BGM: WAV を Ogg Vorbis にする
"""
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "src" / "Abbild" / "Content"

# 背景: 出力名 -> 元ファイル
BACKGROUNDS = {
    "plain": "bg1_plain.png",
    "volcano": "bg1_volcano.png",
    "ice": "bg1_ice.png",
    "desert": "bg1_desert.png",
    "poison": "bg1_poison.png",
    "temple": "bg1_temple.png",
    "cave": "bg1_cave.png",
    "divine": "bg1_divine.png",
    "demon": "bg1_demon.png",
    "final": "bg_final.png",
    "dungeon": "bg1_dungeon.png",
    "corridor": "dungeon_bg.png",
    "gameover": "game over.png",
    "end": "end.png",
}

# 敵の一枚絵: 出力名 -> 元ファイル名の頭（_移動000.jpg を除いた部分）
ENEMY_IMAGES = {
    "ai_core": "AIコア", "ufo": "UFO", "mom": "お母さん", "behemoth": "べヒーモス",
    "iron_golem": "アイアンゴーレム", "ice_crystal": "アイスクリスタル", "earth_golem": "アースゴーレム",
    "wind_spirit": "ウィンドスピリット", "werewolf": "ウェアウルフ", "error404": "エラー404", "orc": "オーク",
    "chameleon": "カメレオン", "ghast": "ガースト", "chimera": "キマイラ", "killer_crab": "キラークラブ",
    "killer_bee": "キラービー", "kraken": "クラーケン", "griffon": "グリフォン", "bat": "コウモリ",
    "goblin": "ゴブリン", "ghost": "ゴースト", "golem": "ゴーレム", "shark": "サメ",
    "thunderbird": "サンダーバード", "thunder_ball": "サンダーボール", "shadow": "シャドウ",
    "sea_serpent": "シーサーペント", "skeleton": "スケルトン", "siren": "セイレーン", "turret": "タレット",
    "dullahan": "デュラハン", "drill_mole": "ドリルモグラ", "harpy": "ハーピー", "bug": "バグ",
    "hydra": "ヒュドラ", "fire_wisp": "ファイアウィスプ", "phoenix": "フェニックス", "hobgoblin": "ホブゴブリン",
    "mad_bear": "マッドベア", "mummy": "マミー", "mimic": "ミミック", "mecha_goblin": "メカゴブリン",
    "light_orb": "ライトオーブ", "lich": "リッチ", "leviathan": "リヴァイアサン", "wyvern": "ワイバーン",
    "vampire": "ヴァンパイア", "hitodama": "人魂", "legendary_bento": "伝説のコンビニ弁当",
    "living_armor": "動く鎧", "fishman": "半魚人", "ancient_weapon": "古代兵器", "vampire_bat": "吸血バット",
    "cursed_sword": "呪いの剣", "giant_squid": "大王イカ", "great_serpent": "大蛇", "giant_turtle": "巨大ガメ",
    "giant_spider": "巨大グモ", "angry_wifi": "怒れるWi-Fi", "wild_boar": "暴れイノシシ",
    "runaway_tank": "暴走戦車", "reaper": "死神", "floating_eye": "浮遊目玉", "deep_sea_fish": "深海魚",
    "berserker": "狂戦士", "bandit": "盗賊", "demon_lord": "真・魔王", "god_of_destruction": "破壊神",
    "empty_can": "空き缶", "flying_spaghetti": "空飛ぶスパゲッティ", "deadline": "締め切り",
    "rotting_corpse": "腐った死体", "kamikaze_drone": "自爆ドローン", "guard_robot": "警備ロボ",
    "lost_cat": "迷子の猫", "wild_dog": "野犬", "dev_grudge": "開発者の怨念",
    "electric_jellyfish": "電気クラゲ", "demon_lord_phantom": "魔王の幻影",
}

HEROES = {"hero_male": "hero_male.jpg", "hero_female": "hero_female.jpg"}

# ローズミルクティー様の素材: (フォルダー, 頭文字の一覧, 出力名の頭)
RMT_GROUPS = [
    ("スライム", "スライム", "slime"), ("コボルト", "コボルト", "kobold"), ("ゾンビ", "ゾンビ", "zombie"),
    ("ドラゴン", "ドラゴン", "dragon"), ("兵士", "兵士", "soldier"), ("妖精", "妖精", "fairy"),
    ("植物", "植物", "plant"), ("魔法使い", "魔法使い", "wizard"), ("鳥", "鳥", "bird"), ("どくろ", "どくろ", "skull"),
]

BGM = {"title": "bgm_title.wav", "dungeon": "bgm_dungeon.wav", "ending": "bgm_ending.wav", "gameover": "bgm_gameover.wav"}

SPRITE_MAX = 420   # 一枚絵の敵の長辺の上限 (px)
HERO_MAX = 520
# 白く光る中心を持つので、囲まれた白を残す素材
KEEP_WHITE_HOLES = {"light_orb", "hitodama", "thunder_ball", "phoenix"}


def crop_16x9(im: Image.Image) -> Image.Image:
    w, h = im.size
    target = 16 / 9
    if w / h > target:
        nw = round(h * target)
        x = (w - nw) // 2
        return im.crop((x, 0, x + nw, h))
    nh = round(w / target)
    return im.crop((0, 0, w, nh))  # 下側を落とす（右下の透かしが入る素材があるため）


def crop_16x9_avoid_corner(im: Image.Image, corner: int) -> Image.Image:
    """右下 corner px 四方を避けて 16:9 に切り抜く（AI 生成ツールの透かしがある素材用）。"""
    w, h = im.size
    nw = w - corner
    nh = round(nw * 9 / 16)
    if nh > h:
        nh = h
        nw = round(nh * 16 / 9)
    return im.crop((0, 0, nw, nh))


def make_background(src: Path, dst: Path, watermark_corner: int = 0) -> None:
    im = Image.open(src).convert("RGB")
    im = crop_16x9_avoid_corner(im, watermark_corner) if watermark_corner else crop_16x9(im)
    im = im.resize((1920, 1080), Image.LANCZOS)
    im.save(dst, "JPEG", quality=86, optimize=True, progressive=True)


def cut_out_white(src: Path, max_side: int, fill_holes: bool = True) -> Image.Image:
    rgb = np.asarray(Image.open(src).convert("RGB")).astype(np.int16)
    lo = rgb.min(axis=2)
    hi = rgb.max(axis=2)
    # 白っぽい（明るく、色みが少ない）画素
    whitish = (lo > 222) & ((hi - lo) < 28)
    # 外周とつながっている白だけを背景とみなす（白い体の内側は残す）
    labels, _ = ndimage.label(whitish)
    border = np.unique(np.concatenate([labels[0], labels[-1], labels[:, 0], labels[:, -1]]))
    border = border[border != 0]
    bg = np.isin(labels, border)
    # 体に囲まれた「純白の大きな穴」（とぐろの内側など）も背景にする
    pure = (lo > 246) & ((hi - lo) < 8)
    holes, n = ndimage.label(pure & ~bg)
    if n and fill_holes:
        sizes = ndimage.sum(np.ones_like(lo), holes, index=np.arange(1, n + 1))
        min_area = lo.size * 0.002
        big = np.flatnonzero(sizes >= min_area) + 1
        bg |= np.isin(holes, big)
    # JPEG のにじみ（背景のすぐ内側の明るい画素）を 2px まで背景に含める
    lightish = (lo > 175) & ((hi - lo) < 45)
    for _ in range(2):
        grow = ndimage.binary_dilation(bg) & lightish
        if not (grow & ~bg).any():
            break
        bg |= grow
    alpha = np.where(bg, 0, 255).astype(np.uint8)
    # 縁を少しだけなめらかに
    soft = ndimage.uniform_filter(alpha.astype(np.float32), size=3)
    alpha = np.where(bg, 0, np.clip(soft * 1.6, 0, 255)).astype(np.uint8)
    rgba = np.dstack([rgb.astype(np.uint8), alpha])
    im = Image.fromarray(rgba, "RGBA")
    bbox = im.getbbox()
    if bbox:
        im = im.crop(bbox)
    scale = min(1.0, max_side / max(im.size))
    if scale < 1.0:
        im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)
    return im


def make_strip(frames: list[Path]) -> Image.Image:
    ims = [Image.open(f).convert("RGBA") for f in frames]
    w = max(i.width for i in ims)
    h = max(i.height for i in ims)
    strip = Image.new("RGBA", (w * len(ims), h), (0, 0, 0, 0))
    for k, im in enumerate(ims):
        strip.alpha_composite(im, (k * w + (w - im.width) // 2, h - im.height))
    return strip


def rmt_frames(folder: Path, stem: str, kind: str) -> list[Path]:
    pats = {"idle": ["待機", "待機・移動"], "attack": ["攻撃"]}[kind]
    for p in pats:
        fs = sorted(folder.glob(f"{stem}_{p}[0-9][0-9][0-9].png"))
        if fs:
            return fs
    return []


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("source", type=Path, help="元プロジェクトの Abbild フォルダー（Resources を含む）")
    ap.add_argument("--fonts", type=Path, help="DotGothic16-Regular.ttf と OFL.txt のあるフォルダー")
    a = ap.parse_args()
    res = a.source / "Resources"
    rmt = a.source / "bin" / "Debug" / "ゲーム用モンスター素材"
    if not res.is_dir():
        print(f"Resources が見つかりません: {res}", file=sys.stderr)
        return 1

    for sub in ["bg", "enemies", "hero", "rmt", "fonts"]:
        (OUT / sub).mkdir(parents=True, exist_ok=True)

    for name, file in BACKGROUNDS.items():
        corner = 175 if file.startswith("bg_") or file == "dungeon_bg.png" else 0
        make_background(res / file, OUT / "bg" / f"{name}.jpg", corner)
        print("bg", name)

    for name, stem in ENEMY_IMAGES.items():
        src = res / f"{stem}_移動000.jpg"
        cut_out_white(src, SPRITE_MAX, name not in KEEP_WHITE_HOLES).save(OUT / "enemies" / f"{name}.png", optimize=True)
    print("enemies", len(ENEMY_IMAGES))

    for name, file in HEROES.items():
        cut_out_white(res / file, HERO_MAX).save(OUT / "hero" / f"{name}.png", optimize=True)
    print("heroes")

    count = 0
    frames: dict[str, int] = {}
    for folder, prefix, slug in RMT_GROUPS:
        d = rmt / folder
        for v in "ABCD":
            stem = f"{prefix}{v}"
            for kind in ("idle", "attack"):
                fs = rmt_frames(d, stem, kind)
                if fs:
                    name = f"{slug}_{v.lower()}_{kind}"
                    make_strip(fs).save(OUT / "rmt" / f"{name}.png", optimize=True)
                    frames[name] = len(fs)
                    count += 1
    d1 = rmt / "スライム" / "スライムD1_移動000.png"
    if d1.exists():
        make_strip([d1]).save(OUT / "rmt" / "slime_d1_idle.png", optimize=True)
        frames["slime_d1_idle"] = 1
        count += 1
    (OUT / "rmt" / "frames.json").write_text(json.dumps(frames, ensure_ascii=False, indent=1, sort_keys=True), encoding="utf-8")
    shutil.copyfile(rmt / "利用規約.txt", OUT / "rmt" / "TERMS_ja_sjis.txt")
    print("rmt strips", count)

    # BGM は Ogg にしてから、取り出しにくい 1 つのファイル（Content/bgm.dat）にまとめる。
    # Ogg そのものは配布物にもリポジトリにも入れない（assets-src/ は .gitignore 済み）
    ogg_dir = ROOT / "assets-src" / "bgm"
    ogg_dir.mkdir(parents=True, exist_ok=True)
    for name, file in BGM.items():
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", str(res / file), "-ac", "2", "-ar", "44100",
                        "-c:a", "libvorbis", "-q:a", "4", str(ogg_dir / f"{name}.ogg")], check=True)
    subprocess.run([sys.executable, str(ROOT / "tools" / "pack_bgm.py"), str(ogg_dir), str(OUT / "bgm.dat")], check=True)
    print("bgm")

    if a.fonts:
        for f in ("DotGothic16-Regular.ttf", "OFL.txt"):
            shutil.copyfile(a.fonts / f, OUT / "fonts" / f)
        print("fonts")
    return 0


if __name__ == "__main__":
    sys.exit(main())
