#!/usr/bin/env python
"""撮った連番から「動きの不自然さ」を数字で拾う。

preview.py が撮った連番に対して、隣り合うフレームの画像差分を測り、
平均の何倍跳ねたかを出す。カメラの飛び・キャラのワープは、ここで大きな値になる。
目視で全部のコマを見る前に、見るべき場所を絞るための道具。

使い方:

    python tools/motion_audit.py Verify/preview/frontpin-20261115-075513
    python tools/motion_audit.py <dir> --threshold 3.5        # 超えたら終了コード 1
    python tools/motion_audit.py --compare <before> <after>   # 良くなったか比べる
    python tools/motion_audit.py <dir> --strip 110 117        # その区間を 1 枚に並べる
    python tools/motion_audit.py --list                       # 直近のキャプチャ一覧

終了コード: 0 = 問題なし / 1 = しきい値超え・読み込み失敗。
"""
from __future__ import annotations

import argparse
import glob
import os
import sys
from pathlib import Path

PROJECT = Path(__file__).resolve().parent.parent
PREVIEW_ROOT = PROJECT / "Verify" / "preview"


def frames(d: Path) -> list[str]:
    files = glob.glob(str(Path(d) / "f_*.png"))
    files += glob.glob(str(Path(d) / "pin_*.png"))
    files += glob.glob(str(Path(d) / "belly_*.png"))
    files += glob.glob(str(Path(d) / "face_*.png"))
    files += glob.glob(str(Path(d) / "knee_*.png"))
    files += glob.glob(str(Path(d) / "front_*.png"))

    def index(p: str) -> int:
        return int(os.path.splitext(os.path.basename(p))[0].split("_")[1])

    return sorted(set(files), key=index)


def diffs(d: Path, size=(160, 90)) -> tuple[list[float], list[int]]:
    from PIL import Image, ImageChops, ImageStat

    files = frames(d)
    if len(files) < 3:
        return [], []
    prev = None
    values: list[float] = []
    indices: list[int] = []
    for f in files:
        im = Image.open(f).convert("L").resize(size, Image.BILINEAR)
        if prev is not None:
            values.append(ImageStat.Stat(ImageChops.difference(im, prev)).mean[0])
            indices.append(int(os.path.splitext(os.path.basename(f))[0].split("_")[1]))
        prev = im
    return values, indices


def report(d: Path, top: int, threshold: float | None) -> float:
    values, indices = diffs(d)
    if not values:
        print(f"  ! フレームが足りない: {d}")
        return 0.0
    avg = sum(values) / len(values)
    peak = max(values)
    ratio = peak / avg if avg > 0 else 0.0
    print(f"{Path(d).name}")
    print(f"  {len(values) + 1} フレーム  平均差 {avg:.2f}  最大 {peak:.2f} ({ratio:.1f}倍)")

    order = sorted(range(len(values)), key=lambda i: -values[i])[:top]
    for i in sorted(order):
        r = values[i] / avg if avg > 0 else 0.0
        mark = "  <- 要確認" if threshold is not None and r >= threshold else ""
        print(f"    #{indices[i]:3d}->#{indices[i] + 1:3d}  差 {values[i]:6.2f}  ({r:4.1f}倍){mark}")

    if threshold is not None and ratio >= threshold:
        print(f"  ! 最大 {ratio:.1f} 倍がしきい値 {threshold} 倍を超えている")
    return ratio


def strip(d: Path, a: int, b: int, out: Path) -> None:
    from PIL import Image, ImageDraw

    files = frames(d)
    sel = [f for f in files
           if a <= int(os.path.splitext(os.path.basename(f))[0].split("_")[1]) <= b]
    if not sel:
        print("  ! その区間にフレームが無い")
        return

    cols = min(4, len(sel))
    width = 480
    h = int(width * 9 / 16)
    rows = (len(sel) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * width, rows * (h + 14)), (10, 10, 10))
    draw = ImageDraw.Draw(sheet)
    for i, f in enumerate(sel):
        idx = int(os.path.splitext(os.path.basename(f))[0].split("_")[1])
        im = Image.open(f).convert("RGB").resize((width, h), Image.LANCZOS)
        x, y = (i % cols) * width, (i // cols) * (h + 14)
        sheet.paste(im, (x, y + 14))
        draw.text((x + 3, y + 1), f"#{idx}", fill=(255, 255, 0))

    target = out if out.is_absolute() else Path(d) / out
    sheet.save(target)
    print(f"  strip: {target}")


def compare(a: Path, b: Path) -> int:
    va, ia = diffs(a)
    vb, ib = diffs(b)
    if not va or not vb:
        print("! 比較できない（フレーム不足）")
        return 1

    def worst(v: list[float]) -> tuple[float, float]:
        avg = sum(v) / len(v)
        return max(v), max(v) / avg if avg > 0 else 0.0

    pa, ra = worst(va)
    pb, rb = worst(vb)
    print(f"{Path(a).name}: 最大 {pa:.2f} ({ra:.1f}倍)")
    print(f"{Path(b).name}: 最大 {pb:.2f} ({rb:.1f}倍)")
    if rb < ra:
        print(f"  改善: {ra - rb:.1f} 倍ぶん下がった")
        return 0
    if rb > ra:
        print(f"  悪化: {rb - ra:.1f} 倍ぶん上がった")
        return 1
    print("  変化なし")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="撮った連番から動きの不自然さを拾う")
    parser.add_argument("dirs", nargs="*", help="キャプチャのディレクトリ")
    parser.add_argument("--top", type=int, default=6, help="上位いくつを出すか（既定 6）")
    parser.add_argument("--threshold", type=float, help="この倍率を超えたら終了コード 1")
    parser.add_argument("--compare", nargs=2, metavar=("BEFORE", "AFTER"), help="2 つを比べる")
    parser.add_argument("--strip", nargs=2, type=int, metavar=("START", "END"),
                        help="その区間を 1 枚に並べる（1 つ目の dir に対して）")
    parser.add_argument("--list", action="store_true", help="直近のキャプチャ一覧")
    parser.add_argument("-o", "--out", default="strip.png", help="--strip の出力名")
    args = parser.parse_args()

    try:
        from PIL import Image  # noqa: F401
    except ImportError:
        print("! Pillow が無い")
        return 1

    if args.list:
        if not PREVIEW_ROOT.exists():
            print("キャプチャがまだ無い（python tools/preview.py <場面> で撮る）")
            return 0
        for d in sorted(PREVIEW_ROOT.iterdir(), key=lambda p: p.stat().st_mtime, reverse=True):
            if d.is_dir():
                print(f"  {d.name}  {len(frames(d))} 枚")
        return 0

    if args.compare:
        return compare(Path(args.compare[0]), Path(args.compare[1]))

    if not args.dirs:
        parser.print_help()
        return 1

    failed = False
    for d in args.dirs:
        ratio = report(Path(d), args.top, args.threshold)
        if args.threshold is not None and ratio >= args.threshold:
            failed = True

    if args.strip:
        strip(Path(args.dirs[0]), args.strip[0], args.strip[1], Path(args.out))

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
