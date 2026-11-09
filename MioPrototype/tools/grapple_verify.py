#!/usr/bin/env python
"""グラップル演出の自動検証（撮影 + 関節可動域ランキング + 前後比較）を 1 本で回す。

使い方（MioPrototype/ で。Unity Editor を閉じておく）:

    # 1) いまのコードを撮る（ビルドが古ければ先にビルド）
    python tools/grapple_verify.py run --label before

    # 2) コードを直す

    # 3) もう一度撮る
    python tools/grapple_verify.py run --label after

    # 4) 前後を並べる
    python tools/grapple_verify.py compare before after

出力は MioPrototype/Verify/<label>/ に出る。
    montage_<シナリオ>.png   … 連番から等間隔に抜いたコマ
    rom_report.md            … 可動域を超えた関節の一覧（超過が大きい順）
    rom.json                 … compare 用の生データ
    <シナリオ>.log           … その実行の Player.log
compare は Verify/compare_<A>_vs_<B>/ に
    <シナリオ>.png           … 上段 A、下段 B の同じコマ
    rom_diff.md              … 可動域の超過が増えた・減った・新しく出た・消えた関節

可動域は実行版を -jointrom で動かして拾う（キャラ同士が絡む姿勢はこれでしか出ない）。
警告は同じ関節につき 2 秒に 1 回なので、回数は「実際の回数」ではなく目安。
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import time
from collections import defaultdict
from pathlib import Path

PROJECT = Path(__file__).resolve().parent.parent
EXE = PROJECT / "Build" / "MioPrototype.exe"
VERIFY = PROJECT / "Verify"

# 共通の起動引数。解像度を固定して、前後で画の大きさが変わらないようにする。
COMMON = [
    "-screen-width", "960", "-screen-height", "540", "-screen-fullscreen", "0",
    "-jointrom", "-jointlog", "-nofeint",
]

# シナリオ名 -> (追加の起動引数, 連番の接頭辞, 枚数の目安, 終了を待つ秒数)
# 連番は <prefix>_00.png から。Diagnostics.md のフラグ表を参照。
SCENARIOS: dict[str, dict] = {
    "belly": {
        "args": ["-grappletest", "-heldpunchface=0", "-tagcam", "-heldpunchburstframes=40"],
        "burst": "-heldpunchburst=",
        "frames": 40,
        "timeout": 90,
        "quits": True,
    },
    "face": {
        "args": ["-grappletest", "-heldpunchface=1", "-tagcam", "-heldpunchburstframes=40"],
        "burst": "-heldpunchburst=",
        "frames": 40,
        "timeout": 90,
        "quits": True,
    },
    "face_far": {
        "args": ["-grappletest", "-heldpunchface=1", "-tagcam", "-tagcamflip",
                 "-heldpunchburstframes=40"],
        "burst": "-heldpunchburst=",
        "frames": 40,
        "timeout": 90,
        "quits": True,
    },
    "mount": {
        "args": ["-mountwide"],
        "burst": "-mountburst=",
        "frames": 60,
        "timeout": 90,
        "quits": True,
    },
    "mount_prone": {
        "args": ["-mountprone", "-mountwide"],
        "burst": "-mountburst=",
        "frames": 60,
        "timeout": 90,
        "quits": True,
    },
    "pin": {
        "args": ["-grappletest", "-pintest"],
        "burst": "-pinburst=",
        "frames": 80,
        "timeout": 120,
        "quits": False,  # -pinburst は撮り終えても終了しない。ログの完了行で止める
        "done_marker": "pin burst",
    },
}
DEFAULT_SCENARIOS = ["belly", "face", "mount"]

# [JointROM] <対象> <関節> <角度>°（可動域 a〜b°・曲げすぎ|逆方向）<超過>° 超過|反対方向 [状態]
ROM_RE = re.compile(
    r"\[JointROM\]\s+(?P<subject>\S+)\s+(?P<joint>\S+)\s+(?P<deg>-?\d+(?:\.\d+)?)°"
    r"（可動域\s*(?P<range>[^・）]+)・(?P<verdict>曲げすぎ|逆方向)）"
    r"\s*(?P<excess>\d+(?:\.\d+)?)°\s*(?:超過|反対方向)"
    r"(?:\s*\[(?P<state>[^\]]+)\])?"
)


# ---------------------------------------------------------------- 実行

def unity_exe() -> Path | None:
    version = None
    pv = PROJECT / "ProjectSettings" / "ProjectVersion.txt"
    if pv.exists():
        m = re.search(r"m_EditorVersion:\s*(\S+)", pv.read_text(encoding="utf-8"))
        version = m.group(1) if m else None
    if not version:
        return None
    home = Path.home()
    for base in (Path(r"C:\Program Files\Unity\Hub\Editor"), home / "Unity" / "Editor"):
        p = base / version / "Editor" / "Unity.exe"
        if p.exists():
            return p
    return None


def unity_running() -> bool:
    out = subprocess.run(["tasklist", "/fi", "imagename eq Unity.exe"],
                         capture_output=True, text=True).stdout
    return "Unity.exe" in out


def newest_source_mtime() -> float:
    newest = 0.0
    for sub in ("Assets", "ProjectSettings", "Packages"):
        for p in (PROJECT / sub).rglob("*"):
            if p.is_file() and p.suffix in (".cs", ".asmdef", ".unity", ".prefab", ".asset", ".json"):
                newest = max(newest, p.stat().st_mtime)
    return newest


def ensure_build(force: bool) -> None:
    stale = (not EXE.exists()) or newest_source_mtime() > EXE.stat().st_mtime
    if not (force or stale):
        print("[build] ビルドは最新。")
        return

    unity = unity_exe()
    if unity is None:
        sys.exit("[build] Unity.exe が見つからない。ビルドが古い可能性があるが続行できない。")
    if unity_running():
        sys.exit("[build] Unity Editor が開いている。閉じるか、Editor 側で Auto Build Player を"
                 "回してから --no-build で実行して。")

    print("[build] ビルド中…（数分かかる）")
    log = PROJECT / "Logs" / "batchbuild.log"
    log.parent.mkdir(exist_ok=True)
    r = subprocess.run([str(unity), "-batchmode", "-nographics", "-quit",
                        "-projectPath", str(PROJECT),
                        "-executeMethod", "Ryona.EditorTools.PlayerBuild.BuildWindowsFromCli",
                        "-logFile", str(log)])
    if r.returncode != 0 or not EXE.exists():
        sys.exit(f"[build] 失敗。{log} を見て。")


def run_scenario(name: str, outdir: Path) -> list[Path]:
    spec = SCENARIOS[name]
    prefix = outdir / name
    log = outdir / f"{name}.log"
    for old in outdir.glob(f"{name}_*.png"):
        old.unlink()

    cmd = [str(EXE), *COMMON, *spec["args"], f"{spec['burst']}{prefix}", "-logFile", str(log)]
    print(f"[run] {name}: 起動")
    proc = subprocess.Popen(cmd, cwd=str(EXE.parent))
    started = time.time()
    marker = spec.get("done_marker")
    try:
        while True:
            if proc.poll() is not None:
                break
            if time.time() - started > spec["timeout"]:
                print(f"[run] {name}: {spec['timeout']} 秒で終わらないので止める")
                break
            if marker and log.exists():
                text = log.read_text(encoding="utf-8", errors="replace")
                if marker in text:
                    time.sleep(1.5)  # 最後の PNG の書き出しを待つ
                    break
            time.sleep(1.0)
    finally:
        if proc.poll() is None:
            proc.kill()
            proc.wait()

    frames = sorted(outdir.glob(f"{name}_[0-9][0-9].png"))
    print(f"[run] {name}: {len(frames)} 枚 / {time.time() - started:.0f} 秒")
    return frames


# ---------------------------------------------------------------- 可動域

def parse_rom(log: Path) -> list[dict]:
    if not log.exists():
        return []
    rows = []
    for line in log.read_text(encoding="utf-8", errors="replace").splitlines():
        m = ROM_RE.search(line)
        if not m:
            continue
        rows.append({
            "subject": re.sub(r"\d+$", "", m["subject"]) or m["subject"],  # 敵2 -> 敵
            "joint": m["joint"],
            "verdict": m["verdict"],
            "range": m["range"],
            "deg": float(m["deg"]),
            "excess": float(m["excess"]),
            "state": m["state"] or "",
        })
    return rows


def aggregate(rows: list[dict], scenario: str) -> dict[str, dict]:
    """(シナリオ, 対象, 関節, 判定, 状態) ごとに、最大超過・最悪の角度・件数を出す。"""
    out: dict[str, dict] = {}
    for r in rows:
        key = f"{scenario}|{r['subject']}|{r['joint']}|{r['verdict']}|{r['state']}"
        cur = out.get(key)
        if cur is None:
            out[key] = {"scenario": scenario, "subject": r["subject"], "joint": r["joint"],
                        "verdict": r["verdict"], "state": r["state"], "range": r["range"],
                        "max_excess": r["excess"], "worst_deg": r["deg"], "count": 1}
        else:
            cur["count"] += 1
            if r["excess"] > cur["max_excess"]:
                cur["max_excess"] = r["excess"]
                cur["worst_deg"] = r["deg"]
    return out


def rom_table(entries: list[dict]) -> str:
    entries = sorted(entries, key=lambda e: -e["max_excess"])
    lines = ["| 順位 | シナリオ | 対象 | 関節 | 判定 | 最悪角度 | 可動域 | 最大超過 | 状態 | 回数 |",
             "|---:|---|---|---|---|---:|---|---:|---|---:|"]
    for i, e in enumerate(entries, 1):
        lines.append(f"| {i} | {e['scenario']} | {e['subject']} | {e['joint']} | {e['verdict']} | "
                     f"{e['worst_deg']:.0f}° | {e['range']} | {e['max_excess']:.0f}° | "
                     f"{e['state'] or '-'} | {e['count']} |")
    return "\n".join(lines)


def write_rom_report(outdir: Path, label: str, agg: dict[str, dict], ran: list[str]) -> None:
    (outdir / "rom.json").write_text(json.dumps(agg, ensure_ascii=False, indent=2), encoding="utf-8")
    entries = list(agg.values())
    body = [f"# 関節可動域レポート: {label}", "",
            f"実行したシナリオ: {', '.join(ran)}", ""]
    if not entries:
        body.append("可動域を超えた関節はありません。")
    else:
        body.append(f"逸脱 {len(entries)} 種（超過が大きい順。上から直す）。")
        body.append("")
        body.append(rom_table(entries))
        # 関節ごとにまとめた「どこから直すか」
        by_joint: dict[str, float] = defaultdict(float)
        for e in entries:
            k = f"{e['subject']} {e['joint']}"
            by_joint[k] = max(by_joint[k], e["max_excess"])
        body += ["", "## 関節別の最大超過", ""]
        for k, v in sorted(by_joint.items(), key=lambda kv: -kv[1]):
            body.append(f"- {k}: {v:.0f}°")
    (outdir / "rom_report.md").write_text("\n".join(body) + "\n", encoding="utf-8")


# ---------------------------------------------------------------- 画像

def _font(size: int):
    from PIL import ImageFont
    for name in ("meiryo.ttc", "msgothic.ttc", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def pick(frames: list[Path], count: int) -> list[int]:
    """連番から等間隔に count 枚ぶんの添字を選ぶ。"""
    n = len(frames)
    if n <= count:
        return list(range(n))
    return [round(i * (n - 1) / (count - 1)) for i in range(count)]


def make_montage(frames: list[Path], out: Path, count: int, cols: int, tile_w: int) -> None:
    from PIL import Image, ImageDraw
    idx = pick(frames, count)
    if not idx:
        return
    first = Image.open(frames[0])
    tile_h = round(tile_w * first.height / first.width)
    rows = (len(idx) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * tile_w, rows * tile_h), (20, 20, 20))
    draw = ImageDraw.Draw(sheet)
    font = _font(16)
    for n, i in enumerate(idx):
        img = Image.open(frames[i]).convert("RGB").resize((tile_w, tile_h))
        x, y = (n % cols) * tile_w, (n // cols) * tile_h
        sheet.paste(img, (x, y))
        draw.rectangle((x, y, x + 44, y + 22), fill=(0, 0, 0))
        draw.text((x + 4, y + 2), f"#{i:02d}", fill=(255, 255, 255), font=font)
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out)


def make_compare(a_frames: list[Path], b_frames: list[Path], la: str, lb: str,
                 out: Path, count: int, tile_w: int) -> None:
    from PIL import Image, ImageDraw
    n = min(len(a_frames), len(b_frames))
    if n == 0:
        return
    idx = pick(a_frames[:n], count)
    first = Image.open(a_frames[0])
    tile_h = round(tile_w * first.height / first.width)
    strip = 26
    sheet = Image.new("RGB", (len(idx) * tile_w, 2 * (tile_h + strip)), (20, 20, 20))
    draw = ImageDraw.Draw(sheet)
    font = _font(18)
    for row, (label, frames) in enumerate(((la, a_frames), (lb, b_frames))):
        y0 = row * (tile_h + strip)
        draw.text((6, y0 + 3), f"{label}", fill=(255, 220, 120), font=font)
        for col, i in enumerate(idx):
            img = Image.open(frames[i]).convert("RGB").resize((tile_w, tile_h))
            sheet.paste(img, (col * tile_w, y0 + strip))
            if row == 0:
                draw.text((col * tile_w + 6 + 120, y0 + 3), f"#{i:02d}", fill=(200, 200, 200), font=font)
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out)


# ---------------------------------------------------------------- コマンド

def cmd_run(args) -> None:
    label = args.label or time.strftime("run_%Y%m%d_%H%M%S")
    names = [s.strip() for s in args.scenarios.split(",") if s.strip()]
    unknown = [s for s in names if s not in SCENARIOS]
    if unknown:
        sys.exit(f"知らないシナリオ: {unknown}。使えるもの: {', '.join(SCENARIOS)}")

    if not args.no_build:
        ensure_build(args.build)
    elif not EXE.exists():
        sys.exit(f"{EXE} が無い。--no-build を外してビルドして。")

    outdir = VERIFY / label
    outdir.mkdir(parents=True, exist_ok=True)

    agg: dict[str, dict] = {}
    for name in names:
        frames = run_scenario(name, outdir)
        if frames:
            make_montage(frames, outdir / f"montage_{name}.png",
                         args.count, args.cols, args.tile)
        else:
            print(f"[run] {name}: 画像が撮れていない。{outdir / (name + '.log')} を見て。")
        agg.update(aggregate(parse_rom(outdir / f"{name}.log"), name))

    write_rom_report(outdir, label, agg, names)

    print()
    print(f"=== 可動域（{label}）===")
    if agg:
        print(rom_table(list(agg.values())))
    else:
        print("逸脱なし。")
    print()
    print(f"出力: {outdir}")


def load_agg(label: str) -> dict[str, dict]:
    p = VERIFY / label / "rom.json"
    if not p.exists():
        sys.exit(f"{p} が無い。先に run --label {label} を実行して。")
    return json.loads(p.read_text(encoding="utf-8"))


def cmd_compare(args) -> None:
    a, b = args.a, args.b
    for lab in (a, b):
        if not (VERIFY / lab).is_dir():
            sys.exit(f"{VERIFY / lab} が無い。")

    outdir = VERIFY / f"compare_{a}_vs_{b}"
    outdir.mkdir(parents=True, exist_ok=True)

    shared = sorted({p.name.rsplit("_", 1)[0] for p in (VERIFY / a).glob("*_[0-9][0-9].png")}
                    & {p.name.rsplit("_", 1)[0] for p in (VERIFY / b).glob("*_[0-9][0-9].png")})
    for name in shared:
        af = sorted((VERIFY / a).glob(f"{name}_[0-9][0-9].png"))
        bf = sorted((VERIFY / b).glob(f"{name}_[0-9][0-9].png"))
        make_compare(af, bf, a, b, outdir / f"{name}.png", args.count, args.tile)
        print(f"[compare] {name}: {outdir / (name + '.png')}")
    if not shared:
        print("[compare] 両方にあるシナリオの画像が無い。")

    # 可動域の差分
    ra, rb = load_agg(a), load_agg(b)
    lines = [f"# 可動域の差分: {a} → {b}", ""]
    worse, better, new, gone = [], [], [], []
    for k in sorted(set(ra) | set(rb)):
        ea, eb = ra.get(k), rb.get(k)
        if ea and eb:
            d = eb["max_excess"] - ea["max_excess"]
            if d > 1:
                worse.append((k, eb, d))
            elif d < -1:
                better.append((k, eb, d))
        elif eb:
            new.append((k, eb, eb["max_excess"]))
        else:
            gone.append((k, ea, -ea["max_excess"]))

    def section(title, rows):
        lines.append(f"## {title}（{len(rows)}）")
        lines.append("")
        if not rows:
            lines.append("なし")
        for k, e, d in sorted(rows, key=lambda r: -abs(r[2])):
            lines.append(f"- {e['scenario']} / {e['subject']} {e['joint']} {e['verdict']} "
                         f"[{e['state'] or '-'}]: 最大超過 {e['max_excess']:.0f}°（{d:+.0f}°）")
        lines.append("")

    section("悪化", worse)
    section("新しく出た", new)
    section("改善", better)
    section("消えた", gone)
    (outdir / "rom_diff.md").write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    print(f"出力: {outdir}")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    r = sub.add_parser("run", help="撮影して可動域ランキングを出す")
    r.add_argument("--label", help="保存名（既定: 日時）")
    r.add_argument("--scenarios", default=",".join(DEFAULT_SCENARIOS),
                   help=f"カンマ区切り。既定 {','.join(DEFAULT_SCENARIOS)}。全部: {','.join(SCENARIOS)}")
    r.add_argument("--build", action="store_true", help="ソースが古くなくても必ずビルドする")
    r.add_argument("--no-build", action="store_true", help="ビルドを確認せず、いまの exe で撮る")
    r.add_argument("--count", type=int, default=8, help="montage に並べるコマ数（既定 8）")
    r.add_argument("--cols", type=int, default=4, help="montage の列数（既定 4）")
    r.add_argument("--tile", type=int, default=480, help="1 コマの幅 px（既定 480）")
    r.set_defaults(func=cmd_run)

    c = sub.add_parser("compare", help="2 回ぶんの結果を並べる")
    c.add_argument("a", help="前（run --label で付けた名前）")
    c.add_argument("b", help="後")
    c.add_argument("--count", type=int, default=8)
    c.add_argument("--tile", type=int, default=320)
    c.set_defaults(func=cmd_compare)

    args = ap.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
