#!/usr/bin/env python
"""撮った連番フレームから、1 本のプレビュー GIF と比較シートを作る。

`tools/preview.py --reel` が主要 3 場面（腹パン・正面拘束・髪）を撮ったあとに
自動で呼ぶ。単体でも、既存のキャプチャディレクトリを指定して使える。

使い方（MioPrototype/ で実行）:

    python tools/preview_reel.py
    python tools/preview_reel.py --belly Verify/preview/bellybody-... \
        --pin Verify/preview/frontpin-... --hair Verify/preview/haul-...

    # before/after シートも作る場合
    python tools/preview_reel.py --belly-before Verify/preview/belly-body \
        --pin-before Verify/preview/frontpin-20261115-233207
"""
from __future__ import annotations

import argparse
from pathlib import Path

from frameseq import PREVIEW_ROOT, frame_files

DEFAULT_OUT = PREVIEW_ROOT / "reel"
FONT_PATH = Path(r"C:/Windows/Fonts/meiryo.ttc")

WIDTH, HEIGHT = 720, 405


def latest(pattern: str) -> Path | None:
    """`Verify/preview/<pattern>-*` のうち、名前順で最後（= 最新）を返す。"""
    dirs = sorted(PREVIEW_ROOT.glob(f"{pattern}-*"))
    return dirs[-1] if dirs else None


def frames(directory: Path) -> list[Path]:
    return frame_files(directory)


def font(size: int):
    from PIL import ImageFont

    try:
        return ImageFont.truetype(str(FONT_PATH), size)
    except OSError:
        return ImageFont.load_default()


def title_card(text: str, sub: str):
    from PIL import Image, ImageDraw

    im = Image.new("RGB", (WIDTH, HEIGHT), (18, 18, 22))
    draw = ImageDraw.Draw(im)
    f1, f2 = font(38), font(20)
    box = draw.textbbox((0, 0), text, font=f1)
    draw.text(((WIDTH - (box[2] - box[0])) // 2, HEIGHT // 2 - 50),
              text, font=f1, fill=(255, 255, 255))
    box = draw.textbbox((0, 0), sub, font=f2)
    draw.text(((WIDTH - (box[2] - box[0])) // 2, HEIGHT // 2 + 10),
              sub, font=f2, fill=(210, 210, 220))
    return im


def clip(files: list[Path], label: str, start: int, end: int, step: int = 2):
    from PIL import Image, ImageDraw

    out = []
    for i in range(start, min(end, len(files)), step):
        im = Image.open(files[i]).convert("RGB").resize((WIDTH, HEIGHT), Image.Resampling.LANCZOS)
        draw = ImageDraw.Draw(im)
        draw.rectangle([0, HEIGHT - 34, WIDTH, HEIGHT], fill=(0, 0, 0))
        draw.text((10, HEIGHT - 29), label, font=font(16), fill=(255, 235, 120))
        out.append(im)
    return out


def build_reel(belly: Path | None, pin: Path | None, hair: Path | None, out: Path) -> Path | None:

    sections: list[tuple[object, int]] = []
    if belly is not None:
        sections.append((title_card("腹パン（後ろ拘束）", "殴るたびに敵が後ろへ下がらない"), 1600))
        sections.append((clip(frames(belly), "after: 5連撃のあいだ立ち位置を固定", 8, 120), 100))
    if pin is not None:
        sections.append((title_card("正面拘束の膝蹴り", "横 → 斜め正面（28°）"), 1600))
        sections.append((clip(frames(pin), "after: 彼女の顔が見える斜め正面", 50, 100), 100))
    if hair is not None:
        sections.append((title_card("髪", "体・袖を貫通しない"), 1600))
        sections.append((clip(frames(hair), "after: 毛束を体の外へ", 4, 80), 100))

    if not sections:
        print("! リールにするフレームが無い")
        return None

    images, durations = [], []
    for item, duration in sections:
        if isinstance(item, list):
            images.extend(item)
            durations.extend([duration] * len(item))
        else:
            images.append(item)
            durations.append(duration)

    out.mkdir(parents=True, exist_ok=True)
    path = out / "preview-reel.gif"
    images[0].save(path, save_all=True, append_images=images[1:],
                   duration=durations, loop=0, optimize=True, disposal=2)
    print(f"  reel:  {path} ({path.stat().st_size // 1024} KB, {len(images)} コマ)")
    return path


def _cell(path: Path, label: str, size=(480, 270)):
    from PIL import Image, ImageDraw

    im = Image.open(path).convert("RGB").resize(size, Image.Resampling.LANCZOS)
    draw = ImageDraw.Draw(im)
    draw.rectangle([0, 0, size[0], 26], fill=(0, 0, 0))
    draw.text((6, 4), label, font=font(15), fill=(255, 255, 255))
    return im


def build_compare(belly_before: Path | None, belly_after: Path | None,
                  pin_before: Path | None, pin_after: Path | None,
                  out: Path) -> Path | None:
    from PIL import Image, ImageDraw

    if belly_before is None and pin_before is None:
        return None

    rows = 0
    if belly_before is not None and belly_after is not None:
        rows += 1
    if pin_before is not None and pin_after is not None:
        rows += 1
    if rows == 0:
        return None

    height = 60 + rows * 320
    sheet = Image.new("RGB", (1920, height), (12, 12, 12))
    draw = ImageDraw.Draw(sheet)
    y = 10

    if belly_before is not None and belly_after is not None:
        draw.text((20, y), "腹パン: before / after（同じフレーム番号）", font=font(24), fill=(255, 255, 0))
        y += 40
        before_files, after_files = frames(belly_before), frames(belly_after)
        for i, idx in enumerate((15, 40)):
            if idx >= len(before_files) or idx >= len(after_files):
                continue
            x = 20 + i * 950
            sheet.paste(_cell(before_files[idx], f"before #{idx}"), (x, y))
            sheet.paste(_cell(after_files[idx], f"after #{idx}"), (x + 490, y))
        y += 300

    if pin_before is not None and pin_after is not None:
        draw.text((20, y), "正面拘束の膝: before（横）/ after（斜め正面）",
                  font=font(24), fill=(255, 255, 0))
        y += 40
        before_files, after_files = frames(pin_before), frames(pin_after)
        idx = min(65, len(before_files) - 1, len(after_files) - 1)
        if idx >= 0:
            sheet.paste(_cell(before_files[idx], "before: 横"), (20, y))
            sheet.paste(_cell(after_files[idx], "after: 斜め正面"), (970, y))
        y += 300

    out.mkdir(parents=True, exist_ok=True)
    path = out / "before-after.png"
    sheet.save(path)
    print(f"  compare: {path} ({sheet.width}x{sheet.height})")
    return path


def main() -> int:
    parser = argparse.ArgumentParser(description="キャプチャからプレビュー GIF / 比較シートを作る")
    parser.add_argument("--belly", help="腹パン（体）のキャプチャディレクトリ")
    parser.add_argument("--pin", help="正面拘束のキャプチャディレクトリ")
    parser.add_argument("--hair", help="髪のキャプチャディレクトリ")
    parser.add_argument("--belly-before", help="腹パンの比較元ディレクトリ")
    parser.add_argument("--pin-before", help="正面拘束の比較元ディレクトリ")
    parser.add_argument("--out", default=str(DEFAULT_OUT), help="出力先（既定 Verify/preview/reel）")
    args = parser.parse_args()

    try:
        from PIL import Image  # noqa: F401
    except ImportError:
        print("! Pillow が無い。プレビューの組み立てに必要。")
        return 1

    belly = Path(args.belly) if args.belly else latest("bellybody")
    pin = Path(args.pin) if args.pin else latest("frontpin")
    hair = Path(args.hair) if args.hair else latest("haul")
    out = Path(args.out)

    print("[reel]")
    if belly is None and pin is None and hair is None:
        print("! キャプチャが無い。先に tools/preview.py で撮る。")
        return 1
    print(f"  belly={belly.name if belly else '-'} pin={pin.name if pin else '-'} hair={hair.name if hair else '-'}")

    build_reel(belly, pin, hair, out)
    build_compare(
        Path(args.belly_before) if args.belly_before else None, belly,
        Path(args.pin_before) if args.pin_before else None, pin,
        out,
    )
    print(f"出力: {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
