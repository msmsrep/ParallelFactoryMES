# -*- coding: utf-8 -*-
"""Microsoft Store 掲載用のアート画像（ポスター・ボックス・タイル・スーパーヒーロー）を作る。

図柄は build/icon.svg を Inkscape でラスタライズして使う（New-MsixAssets.ps1 と同じ定義元）。
実画面は build/store/raw/*.png（build/Capture-AppScreens.js の撮影結果）を使う。
配色と部品はスクリーンショット（New-StoreScreenshots.py）に揃えている。

  ポスターアート       720x1080   製品名入り（ストアの主表示）
  ボックスアート       1080x1080  製品名入り
  アプリタイルアイコン 300x300    図柄だけ
  スーパーヒーロー     1920x1080  文字を入れない。下1/3はストアが文字を重ねるので主役を置かない
"""
import os
import shutil
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFont

sys.dont_write_bytecode = True  # 隣のスクリプトを読み込むだけなので __pycache__ を残さない
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from importlib import import_module

shots = import_module('New-StoreScreenshots')

INK, SUB, MINT, BG = shots.INK, shots.SUB, shots.MINT, shots.BG
MINT_DEEP = (205, 235, 223)  # アイコンの一番淡いバー（#CDEBDF）
FONT_LATIN = r'C:\Windows\Fonts\seguisb.ttf'
FONT_REG = shots.FONT_REG
NAME = 'Parallel Factory MES'
KIND = '製造実行システム'
TAGLINE = 'PC1台で、工場の記録を始める。'

ROOT = shots.ROOT
RAW = shots.RAW
OUT = shots.OUT
SVG = os.path.join(ROOT, 'icon.svg')
INKSCAPE_CANDIDATES = [
    shutil.which('inkscape'),
    r'C:\Program Files\Inkscape\bin\inkscape.exe',
    r'C:\Program Files (x86)\Inkscape\bin\inkscape.exe',
]


def render_icon(size, workdir):
    """icon.svg を指定ピクセルの透過PNGにする"""
    inkscape = next((p for p in INKSCAPE_CANDIDATES if p and os.path.exists(p)), None)
    if not inkscape:
        sys.exit('Inkscape が見つかりません（icon.svg のラスタライズに使います）。')
    path = os.path.join(workdir, 'icon-%d.png' % size)
    subprocess.run([inkscape, '--export-type=png', '-w', str(size), '-h', str(size),
                    '--export-filename=' + path, SVG], check=True, capture_output=True)
    return Image.open(path).convert('RGBA')


def canvas(w, h, circles):
    """白地にミントの円を敷いた下地"""
    base = Image.new('RGBA', (w, h), BG + (255,))
    d = ImageDraw.Draw(base)
    for box, color in circles:
        d.ellipse(box, fill=color + (255,))
    return base


def center_text(d, cx, y, text, font, fill):
    left, _, right, _ = d.textbbox((0, 0), text, font=font)
    d.text((cx - (right - left) / 2 - left, y), text, font=font, fill=fill)


def shot_card(name, width):
    img = shots.trim_bottom(Image.open(os.path.join(RAW, name + '.png')).convert('RGB'))
    img = img.resize((width, round(img.height * width / img.width)), Image.LANCZOS)
    return shots.rounded_shadow(img, radius=max(10, width // 44))


def poster(workdir):
    w, h = 720, 1080
    base = canvas(w, h, [((-420, 640, 900, 1960), MINT)])
    base.alpha_composite(render_icon(280, workdir), ((w - 280) // 2, 150))
    d = ImageDraw.Draw(base)
    center_text(d, w / 2, 480, NAME, ImageFont.truetype(FONT_LATIN, 56), INK)
    center_text(d, w / 2, 566, KIND, ImageFont.truetype(FONT_REG, 30), SUB)
    center_text(d, w / 2, 616, TAGLINE, ImageFont.truetype(FONT_REG, 26), SUB)
    # 下端は実画面をはみ出させて「アプリの中身」を見せる
    card, pad = shot_card('dashboard', 620)
    base.alpha_composite(card, ((w - 620) // 2 - pad, 730 - pad))
    return base


def box(workdir):
    w = h = 1080
    base = canvas(w, h, [((-520, 620, 820, 1960), MINT), ((760, -260, 1340, 320), MINT_DEEP)])
    base.alpha_composite(render_icon(400, workdir), ((w - 400) // 2, 190))
    d = ImageDraw.Draw(base)
    center_text(d, w / 2, 650, NAME, ImageFont.truetype(FONT_LATIN, 80), INK)
    center_text(d, w / 2, 780, KIND, ImageFont.truetype(FONT_REG, 40), SUB)
    return base


def tile(workdir):
    # タイルの下地はマニフェストの BackgroundColor（白）と揃える
    base = Image.new('RGBA', (300, 300), BG + (255,))
    base.alpha_composite(render_icon(232, workdir), (34, 34))
    return base


def super_hero(workdir):
    w, h = 1920, 1080
    base = canvas(w, h, [((-360, -420, 900, 840), MINT), ((1500, 560, 2260, 1320), MINT_DEEP)])
    base.alpha_composite(render_icon(380, workdir), (200, 150))
    # 主役は上2/3（y < 720）に収める
    back, pad_b = shot_card('work-orders', 820)
    base.alpha_composite(back, (1000 - pad_b, 70 - pad_b))
    front, pad_f = shot_card('dashboard', 880)
    base.alpha_composite(front, (760 - pad_f, 250 - pad_f))
    return base


ARTS = [
    ('poster-art-720x1080.png', poster),
    ('box-art-1080x1080.png', box),
    ('app-tile-icon-300x300.png', tile),
    ('super-hero-art-1920x1080.png', super_hero),
]

if __name__ == '__main__':
    missing = [n for n in ('dashboard', 'work-orders') if not os.path.exists(os.path.join(RAW, n + '.png'))]
    if missing:
        sys.exit('実画面のPNGがありません: %s（build/Capture-AppScreens.js で撮影してください）'
                 % '、'.join(missing))
    os.makedirs(OUT, exist_ok=True)
    print('生成先: ' + OUT)
    with tempfile.TemporaryDirectory() as workdir:
        for filename, build in ARTS:
            build(workdir).convert('RGB').save(os.path.join(OUT, filename))
            print('  ' + filename)
    print('完了しました。')
