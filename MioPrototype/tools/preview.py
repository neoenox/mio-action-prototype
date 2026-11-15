#!/usr/bin/env python
"""演出の変更を「撮って確かめる」ための一括ツール。

これまで手で叩いていた

    1. Unity でプレイヤーをビルド
    2. 実行ファイルを起動して連番キャプチャ
    3. コマ送り画像と GIF を作る
    4. 変更前と横に並べて比較画像を作る

を 1 コマンドにまとめる。AGENTS.md の「変更したら撮って確かめる」を回すための道具。

使い方（MioPrototype/ で実行）:

    python tools/preview.py frontpin
    python tools/preview.py belly --no-build
    python tools/preview.py frontpin --compare ../../preview-front5
    python tools/preview.py --list

出力は `Verify/preview/<scenario>-<時刻>/` に置く:

    f_000.png ...   生の連番
    sheet.png       コマ送り（既定 0.25 秒ごと）
    preview.gif     10fps の確認用
    compare.png     --compare を付けたときだけ

終了コード: 0 = 成功 / 1 = 失敗（ビルド失敗・フレーム不足など）。
"""
from __future__ import annotations

import argparse
import glob
import os
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

PROJECT = Path(__file__).resolve().parent.parent            # MioPrototype/
REPO = PROJECT.parent
EXE = PROJECT / "Build" / "MioPrototype.exe"
OUT_ROOT = PROJECT / "Verify" / "preview"

# 撮りたい場面ごとの起動引数。フレーム数と間隔もここに持つ。
#   burst: 連番キャプチャの接頭辞フラグ（Debug の各ドライバが読む）
SCENARIOS = {
    # 壁押さえつけ（壁へ押し出す版）。膝〜二人がかり〜3 人がかりまで含む。
    "wallpin": dict(args=["-grappletest", "-wallpintest"], burst="-pinburst",
                    frames=200, interval=0.05),
    # レーンの正面拘束（-nowallpin で壁版を避ける）。膝蹴りが入る。
    "frontpin": dict(args=["-grappletest", "-pintest", "-nowallpin"], burst="-pinburst",
                     frames=140, interval=0.05),
    # 羽交い締めからの腹パン。
    "belly": dict(args=["-grappletest"], burst="-heldpunchburst",
                  frames=140, interval=0.05),
    # 羽交い締めからの顔面パン。
    "face": dict(args=["-grappletest", "-heldpunchface=1"], burst="-heldpunchburst",
                 frames=140, interval=0.05),
    # 足取り（背後の足首掴み）。掴まれて 1.6 秒でもがけなければ前へ転ぶ。
    "ankle": dict(args=["-grappletest", "-anklegrabtest"], burst="-pinburst",
                  frames=90, interval=0.05),
    # ダウンした彼女を引きずり起こし、羽交い締めへつなぐ（-haultest で必ず選ぶ）。
    "haul": dict(args=["-haultest"], burst="-mountburst",
                 frames=100, interval=0.05),
    # 頭上拘束（両手首を上へ引き上げる）。
    "overhead": dict(args=["-grappletest", "-overheadtest"], burst="-pinburst",
                     frames=120, interval=0.05),
}


# --- Unity まわり ------------------------------------------------------

def unity_exe() -> Path | None:
    """ProjectVersion.txt と同じバージョンの Unity.exe を探す。"""
    version_file = PROJECT / "ProjectSettings" / "ProjectVersion.txt"
    if not version_file.exists():
        return None
    version = ""
    for line in version_file.read_text(encoding="utf-8").splitlines():
        if line.startswith("m_EditorVersion:"):
            version = line.split(":", 1)[1].strip()
            break
    if not version:
        return None

    for base in (Path(r"C:\Program Files\Unity\Hub\Editor"), Path.home() / "Unity" / "Editor"):
        p = base / version / "Editor" / "Unity.exe"
        if p.exists():
            return p
    return None


def unity_running() -> bool:
    try:
        out = subprocess.run(["tasklist", "/fi", "imagename eq Unity.exe"],
                             capture_output=True, text=True, errors="replace")
        return "Unity.exe" in out.stdout
    except OSError:
        return False


def build_player() -> bool:
    unity = unity_exe()
    if unity is None:
        print("! Unity が見つからない（--no-build でスキップできる）")
        return False
    if unity_running():
        print("! Unity Editor が起動中。閉じてから実行する（バッチモードと競合する）")
        return False

    log = PROJECT / "Logs" / "preview-build.log"
    log.parent.mkdir(parents=True, exist_ok=True)
    print("[build] プレイヤーをビルド（最新なら即終了）")
    code = subprocess.run([
        str(unity), "-batchmode", "-nographics", "-quit",
        "-projectPath", str(PROJECT),
        "-executeMethod", "Ryona.EditorTools.PlayerBuild.BuildWindowsFromCli",
        "-logFile", str(log),
    ]).returncode
    text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
    if "error CS" in text:
        print("  x コンパイルエラー:")
        for line in text.splitlines():
            if "error CS" in line:
                print("    " + line.strip())
        return False
    if "build result=Succeeded" not in text and "up to date" not in text:
        print(f"  ! ビルドに失敗した可能性。ログ: {log}")
        return False
    print("  ok")
    return True


# --- 撮影 --------------------------------------------------------------

def capture(scenario: str, outdir: Path, frames: int, interval: float, extra: list[str]) -> int:
    spec = SCENARIOS[scenario]
    if not EXE.exists():
        print(f"! 実行ファイルが無い: {EXE}")
        return 0

    outdir.mkdir(parents=True, exist_ok=True)
    prefix = outdir / "f.png"
    frames_flag = spec["burst"].replace("burst", "burstframes")
    args = [str(EXE)] + spec["args"] + [
        f"{spec['burst']}={prefix}",
        f"{frames_flag}={frames}",
        f"-{spec['burst'][1:]}interval={interval}",
        "-log",
    ] + extra

    print(f"[run] {scenario}: {frames} 枚 / {interval} 秒間隔")
    proc = subprocess.Popen(args, cwd=str(PROJECT / "Build"))
    try:
        deadline = time.time() + frames * interval + 60
        prev, stable = 0, 0
        while time.time() < deadline:
            time.sleep(1)
            n = len(glob.glob(str(outdir / "f_*.png")))
            if n == prev:
                stable += 1
                if stable >= 8 and n > 0:
                    break
            else:
                prev, stable = n, 0
            if n >= frames:
                break
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()

    got = len(glob.glob(str(outdir / "f_*.png")))
    print(f"  {got} 枚")
    return got


# --- 画像の組み立て ----------------------------------------------------

def frame_paths(d: Path) -> list[str]:
    files = glob.glob(str(d / "f_*.png"))
    return sorted(files, key=lambda p: int(os.path.splitext(os.path.basename(p))[0].split("_")[1]))


def make_sheet(files: list[str], out: Path, step: int, cols: int = 6, width: int = 320) -> None:
    from PIL import Image, ImageDraw

    picked = files[::max(1, step)]
    im0 = Image.open(picked[0])
    h = int(width * im0.height / im0.width)
    rows = (len(picked) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * width, rows * (h + 14)), (12, 12, 12))
    draw = ImageDraw.Draw(sheet)
    for i, f in enumerate(picked):
        idx = int(os.path.splitext(os.path.basename(f))[0].split("_")[1])
        im = Image.open(f).convert("RGB").resize((width, h), Image.LANCZOS)
        x, y = (i % cols) * width, (i // cols) * (h + 14)
        sheet.paste(im, (x, y + 14))
        draw.text((x + 3, y + 1), f"#{idx} {idx * 0.05:.2f}s", fill=(255, 255, 0))
    sheet.save(out)
    print(f"  sheet: {out.name} ({sheet.width}x{sheet.height})")


def make_gif(files: list[str], out: Path, width: int = 480, every: int = 2) -> None:
    from PIL import Image

    frames = []
    for i, f in enumerate(files):
        if i % max(1, every):
            continue
        im = Image.open(f).convert("RGB")
        h = int(width * im.height / im.width)
        frames.append(im.resize((width, h), Image.LANCZOS).quantize(colors=64, method=Image.MEDIANCUT))
    if not frames:
        return
    frames[0].save(out, save_all=True, append_images=frames[1:],
                   duration=50 * max(1, every), loop=0, optimize=True, disposal=2)
    print(f"  gif:   {out.name} ({os.path.getsize(out) // 1024} KB, {len(frames)} コマ)")


def make_compare(old_dir: Path, new_dir: Path, out: Path, picks: int = 4, width: int = 520) -> None:
    from PIL import Image, ImageDraw

    old, new = frame_paths(old_dir), frame_paths(new_dir)
    if not old or not new:
        print("  ! 比較できない（フレームが無い）")
        return
    n = min(len(old), len(new))
    idxs = [int(n * (i + 1) / (picks + 1)) for i in range(picks)]
    h = int(width * Image.open(old[0]).height / Image.open(old[0]).width)
    sheet = Image.new("RGB", (width * picks, h * 2 + 40), (12, 12, 12))
    draw = ImageDraw.Draw(sheet)
    draw.text((6, 4), f"BEFORE  {old_dir.name}", fill=(255, 130, 130))
    draw.text((6, h + 24), f"AFTER   {new_dir.name}", fill=(130, 255, 130))
    for i, idx in enumerate(idxs):
        for row, src in enumerate((old, new)):
            im = Image.open(src[idx]).convert("RGB").resize((width, h), Image.LANCZOS)
            y = 18 if row == 0 else h + 38
            sheet.paste(im, (i * width, y))
            draw.text((i * width + 4, y + h - 14), f"#{idx}", fill=(255, 255, 0))
    sheet.save(out)
    print(f"  compare: {out.name} ({sheet.width}x{sheet.height})")


# --- 入口 --------------------------------------------------------------

def main() -> int:
    parser = argparse.ArgumentParser(description="演出を撮って確かめる一括ツール")
    parser.add_argument("scenario", nargs="?", help="撮る場面（--list で一覧）")
    parser.add_argument("--list", action="store_true", help="場面の一覧を出して終了")
    parser.add_argument("--no-build", action="store_true", help="プレイヤーのビルドを省く")
    parser.add_argument("--frames", type=int, help="撮る枚数（既定は場面ごと）")
    parser.add_argument("--interval", type=float, help="撮影間隔（秒）")
    parser.add_argument("--compare", help="比較する前のキャプチャディレクトリ")
    parser.add_argument("--sheet-step", type=int, default=5, help="コマ送りの間引き（既定 5 = 0.25 秒ごと）")
    parser.add_argument("--audit", action="store_true", help="撮ったあとにフレーム間の不連続を検査する")
    parser.add_argument("-a", "--arg", action="append", default=[], help="実行ファイルへ渡す追加引数")
    args = parser.parse_args()

    if args.list or not args.scenario:
        print("場面:")
        for name, spec in SCENARIOS.items():
            print(f"  {name:9s} {spec['frames']} 枚 / {spec['interval']}s  {' '.join(spec['args'])}")
        return 0

    if args.scenario not in SCENARIOS:
        print(f"! 未知の場面: {args.scenario}（--list で一覧）")
        return 1

    try:
        from PIL import Image  # noqa: F401
    except ImportError:
        print("! Pillow が無い。画像の組み立てに必要。")
        return 1

    if not args.no_build and not build_player():
        return 1

    spec = SCENARIOS[args.scenario]
    frames = args.frames or spec["frames"]
    interval = args.interval or spec["interval"]

    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    outdir = OUT_ROOT / f"{args.scenario}-{stamp}"

    got = capture(args.scenario, outdir, frames, interval, args.arg)
    if got < 10:
        print("! フレームが撮れていない（起動オプションかビルドを確認）")
        return 1

    files = frame_paths(outdir)
    print("[image]")
    make_sheet(files, outdir / "sheet.png", args.sheet_step)
    make_gif(files, outdir / "preview.gif")
    if args.compare:
        make_compare(Path(args.compare), outdir, outdir / "compare.png")

    if args.audit:
        try:
            sys.path.insert(0, str(Path(__file__).resolve().parent))
            from motion_audit import report as audit_report
            print("[audit]")
            audit_report(outdir, top=6, threshold=None)
        except Exception as e:      # 監査は補助なので、失敗しても撮影結果は残す
            print(f"  ! 監査できなかった: {e}")

    print(f"\n完了: {outdir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
