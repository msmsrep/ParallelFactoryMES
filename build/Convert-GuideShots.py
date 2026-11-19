# ガイドの画面写真（build/Capture-GuideScreens.js が撮ったPNG）を、ガイドに置く webp にする。
#
#   python build/Convert-GuideShots.py <PNGのフォルダ> <出力先（例 docs/assets/en/walkthrough）> [--keep-size]
#
# 既定（walkthrough）：本文の下の余白（背景色だけの行）を落とし、幅1440に縮小して quality 82 の webp で保存する。
# 幅1280・DPR2で撮ったものを1440に縮めると、既存の写真（サイドバー248px幅に見える）と倍率が揃う。
# --keep-size（intro）：撮影時に紹介ページの寸法で切り出してあるので、余白も寸法もそのままで webp にする。
import sys
from pathlib import Path

from PIL import Image

WIDTH = 1440
MARGIN = 48  # 本文の下に残す余白（縮小前のピクセル）


def trim_bottom(img: Image.Image) -> Image.Image:
    rgb = img.convert("RGB")
    w, h = rgb.size
    # 右端の列（本文の外）の一番下の色を背景色とみなす
    bg = rgb.getpixel((w - 1, h - 1))
    # 左のサイドバーは下まで伸びるので、本文の部分（左から1/4より右）だけを見る
    left = w // 4
    px = rgb.load()
    bottom = h
    for y in range(h - 1, -1, -1):
        if any(px[x, y] != bg for x in range(left, w, 4)):
            bottom = y + 1
            break
    return img.crop((0, 0, w, min(h, bottom + MARGIN)))


def main() -> None:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    keep_size = "--keep-size" in sys.argv[1:]
    src, dest = Path(args[0]), Path(args[1])
    dest.mkdir(parents=True, exist_ok=True)
    for png in sorted(src.glob("*.png")):
        img = Image.open(png).convert("RGB")
        if not keep_size:
            img = trim_bottom(img)
            h = round(img.height * WIDTH / img.width)
            img = img.resize((WIDTH, h), Image.LANCZOS)
        out = dest / (png.stem + ".webp")
        img.save(out, "WEBP", quality=82)
        print(f"  {out}  {img.width}x{img.height}")


if __name__ == "__main__":
    main()
