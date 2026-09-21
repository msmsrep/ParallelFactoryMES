# -*- coding: utf-8 -*-
"""Microsoft Store 掲載用スクリーンショット（1920x1080）を組み立てる。

アプリの実画面（build/store/raw/*.png）を右に置き、左に見出しと説明を入れる。
実画面の撮影は build/Capture-AppScreens.js が行う。ここは合成だけを担当する。
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H = 1920, 1080
INK = (22, 24, 31)          # 見出し（濃い黒）
SUB = (85, 96, 107)         # 説明文
MINT = (219, 238, 231)      # 背景の円（アイコンのパステルに合わせたミント）
BG = (255, 255, 255)

FONT_BOLD = r'C:\Windows\Fonts\YuGothB.ttc'
FONT_REG = r'C:\Windows\Fonts\YuGothR.ttc'

ROOT = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(ROOT, 'store', 'raw')
OUT = os.path.join(ROOT, 'store')
ICON = os.path.join(ROOT, '..', 'src', 'MesApp.Desktop', 'Assets', 'Square44x44Logo.targetsize-256.png')

SHOTS = [
    ('dashboard', '工場の今日が\nひと目で分かる',
     '当日の良品数・不良率・検査の合否を\n1画面にまとめて表示します。'),
    ('orders', '指図の登録から\n工程展開まで',
     '製造指図を登録し、承認・工程展開・\n差立へ。状態の履歴も残ります。'),
    ('work-orders', '作業指示から\nそのまま実績入力',
     '着手・段取り・チェックリスト・部材\n投入・実績報告まで同じ画面で。'),
    ('inventory', 'ロット単位で\n在庫と履歴を追う',
     '入出庫・移動・調整の履歴と期限の\nアラート。部材まで双方向に追跡。'),
    ('inspections', '受入から完成品まで\n検査を一本化',
     '検査指示の発行と、測定値からの自動\n判定。不適合もその場で記録します。'),
]


def rounded_shadow(img, radius=22, blur=26, offset=(0, 14), alpha=58):
    """角丸にして、やわらかい影を敷いたRGBA画像を返す"""
    w, h = img.size
    mask = Image.new('L', (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, w - 1, h - 1), radius=radius, fill=255)
    card = img.convert('RGBA')
    card.putalpha(mask)

    pad = blur * 3
    canvas = Image.new('RGBA', (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    shadow = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(
        (pad + offset[0], pad + offset[1], pad + w - 1 + offset[0], pad + h - 1 + offset[1]),
        radius=radius, fill=(23, 32, 54, alpha))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(blur)))
    canvas.alpha_composite(card, (pad, pad))
    return canvas, pad


def trim_bottom(img, sidebar_px=760, margin=52, min_ratio=0.52):
    """本文の下にできた余白を落とす。サイドバーは全高あるので本文側の列だけを見る"""
    content = img.crop((sidebar_px, 0, img.width, img.height)).convert('L')
    px = content.load()
    bottom = 0
    for y in range(content.height - 1, -1, -1):
        if any(px[x, y] < 244 for x in range(0, content.width, 8)):
            bottom = y
            break
    bottom = min(img.height, bottom + margin)
    return img.crop((0, 0, img.width, max(bottom, int(img.width * min_ratio))))


def draw_lines(d, xy, text, font, fill, line_height):
    x, y = xy
    for line in text.split('\n'):
        d.text((x, y), line, font=font, fill=fill)
        y += line_height
    return y


def compose(name, headline, body):
    base = Image.new('RGB', (W, H), BG)
    layer = Image.new('RGBA', (W, H), (0, 0, 0, 0))

    # 左下の大きな円（参考にした配色と同じく、帯ではなく円で下地を作る）
    ImageDraw.Draw(layer).ellipse((-560, 470, 700, 1730), fill=MINT + (255,))
    base = Image.alpha_composite(base.convert('RGBA'), layer)

    d = ImageDraw.Draw(base)
    head_font = ImageFont.truetype(FONT_BOLD, 66)
    body_font = ImageFont.truetype(FONT_REG, 27)
    end = draw_lines(d, (145, 372), headline, head_font, INK, 92)
    draw_lines(d, (147, end + 44), body, body_font, SUB, 48)

    icon = Image.open(ICON).convert('RGBA').resize((112, 112), Image.LANCZOS)
    base.alpha_composite(icon, (145, 858))

    shot = trim_bottom(Image.open(os.path.join(RAW, name + '.png')).convert('RGB'))
    target_w = 960
    shot = shot.resize((target_w, round(shot.height * target_w / shot.width)), Image.LANCZOS)
    card, pad = rounded_shadow(shot)
    base.alpha_composite(card, (890 - pad, (H - shot.height) // 2 - pad))

    path = os.path.join(OUT, 'screenshot-%d-1920x1080.png' % (index + 1))
    base.convert('RGB').save(path)
    print('  ' + os.path.basename(path))
    return path


if __name__ == '__main__':
    missing = [n for n, _, _ in SHOTS if not os.path.exists(os.path.join(RAW, n + '.png'))]
    if missing:
        sys.exit('実画面のPNGがありません: %s（build/Capture-AppScreens.js で撮影してください）'
                 % '、'.join(missing))
    os.makedirs(OUT, exist_ok=True)
    print('生成先: ' + OUT)
    for index, (name, headline, body) in enumerate(SHOTS):
        compose(name, headline, body)
    print('完了しました。')
