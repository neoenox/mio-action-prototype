#!/usr/bin/env python
"""グラップル演出の自動検証（撮影 + 関節可動域ランキング + 前後比較）を 1 本で回す。

使い方（MioPrototype/ で。Unity Editor を閉じておく）:

    # いまのコードを、直前のコミット（HEAD）と比べる。基準は自動でビルドして撮る
    python tools/grapple_verify.py run --label after --baseline HEAD

    # 基準を手動で撮る場合
    python tools/grapple_verify.py run --label before
    python tools/grapple_verify.py run --label after
    python tools/grapple_verify.py compare before after

出力は MioPrototype/Verify/<label>/ に出る。
    montage_<シナリオ>.png   … 連番から等間隔に抜いたコマ
    rom_report.md            … 可動域を超えた関節の一覧（超過が大きい順）
    rom.json                 … compare 用の生データ
    <シナリオ>.log           … その実行の Player.log
compare は Verify/compare_<A>_vs_<B>/ に
    <シナリオ>.png           … 上段 A、下段 B の同じコマ
    rom_diff.md              … 可動域の超過が増えた・減った・新しく出た・消えた関節

--baseline REF
    REF（HEAD, HEAD~3, コミットのハッシュなど）を専用のワークツリー
    （.claude/worktrees/verify-baseline）へ出してビルドし、同じシナリオで撮る。
    作業ツリーの未コミットの変更には触れない。基準は Verify/base-<ハッシュ>/ に保存して
    使い回す（--refresh-baseline で撮り直す）。撮り終えたら自動で compare まで行う。
    基準が「コミット済みのコード」なので、未コミットの変更を試すときの「直す前」になる。
    初回は Library のコピーとビルドで数分かかる。

可動域は実行版を -jointrom -jointlog で動かして拾う（キャラ同士が絡む姿勢はこれでしか出ない）。
  - [JointROMWorst] 行（関節ごとの最悪値と延べ回数。毎サンプルの集計）があればそれを使う。
    警告は同じ関節につき 2 秒に 1 回なので、警告だけでは最悪値を取り逃がす。
  - その行が無い古いビルド（基準が古いコミットのとき）は、警告行から集計する。
    この場合は最大超過・回数とも目安になる（表に「目安」と出る）。
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import time
from collections import defaultdict
from pathlib import Path

PROJECT = Path(__file__).resolve().parent.parent          # MioPrototype/
REPO = PROJECT.parent
VERIFY = PROJECT / "Verify"
BASE_WORKTREE = REPO / ".claude" / "worktrees" / "verify-baseline"

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

# 警告:  [JointROM] <対象> <関節> <角度>°（可動域 a〜b°・曲げすぎ|逆方向）<超過>° 超過|反対方向 [状態]
# 集計:  [JointROMWorst] <同じ書式> ×<延べ回数>   … 最悪値の行（累積。最後の行が実行全体の値）
ROM_RE = re.compile(
    r"\[JointROM(?P<worst>Worst)?\]\s+(?P<subject>\S+)\s+(?P<joint>\S+)\s+(?P<deg>-?\d+(?:\.\d+)?)°"
    r"（可動域\s*(?P<range>[^・）]+)・(?P<verdict>曲げすぎ|逆方向)）"
    r"\s*(?P<excess>\d+(?:\.\d+)?)°\s*(?:超過|反対方向)"
    r"(?:\s*\[(?P<state>[^\]]+)\])?"
    r"(?:\s*×(?P<count>\d+))?"
)


# ---------------------------------------------------------------- git / ビルド

def git(*args: str, cwd: Path = REPO, check: bool = True) -> str:
    r = subprocess.run(["git", *args], cwd=str(cwd), capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    if check and r.returncode != 0:
        sys.exit(f"git {' '.join(args)} が失敗: {r.stderr.strip()}")
    return r.stdout.strip()


def unity_exe(project: Path) -> Path | None:
    version = None
    pv = project / "ProjectSettings" / "ProjectVersion.txt"
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


def newest_source_mtime(project: Path) -> float:
    newest = 0.0
    for sub in ("Assets", "ProjectSettings", "Packages"):
        for p in (project / sub).rglob("*"):
            if p.is_file() and p.suffix in (".cs", ".asmdef", ".unity", ".prefab", ".asset", ".json"):
                newest = max(newest, p.stat().st_mtime)
    return newest


def exe_of(project: Path) -> Path:
    return project / "Build" / "MioPrototype.exe"


def ensure_build(project: Path, force: bool) -> None:
    exe = exe_of(project)
    stale = (not exe.exists()) or newest_source_mtime(project) > exe.stat().st_mtime
    if not (force or stale):
        print(f"[build] ビルドは最新（{project.name}）。")
        return

    unity = unity_exe(project)
    if unity is None:
        sys.exit("[build] Unity.exe が見つからない。ビルドが古い可能性があるが続行できない。")
    if unity_running():
        sys.exit("[build] Unity Editor が開いている。閉じるか、Editor 側で Auto Build Player を"
                 "回してから --no-build で実行して。")

    print(f"[build] ビルド中…（数分かかる）{project}")
    log = project / "Logs" / "batchbuild.log"
    log.parent.mkdir(exist_ok=True)
    r = subprocess.run([str(unity), "-batchmode", "-nographics", "-quit",
                        "-projectPath", str(project),
                        "-executeMethod", "Ryona.EditorTools.PlayerBuild.BuildWindowsFromCli",
                        "-logFile", str(log)])
    if r.returncode != 0 or not exe.exists():
        sys.exit(f"[build] 失敗。{log} を見て。")


def prepare_baseline(ref: str) -> tuple[Path, str]:
    """REF をワークツリーへ出す。作業ツリー（MioPrototype/）には触れない。"""
    sha = git("rev-parse", "--short", ref)
    if not sha:
        sys.exit(f"{ref} が解決できない。")

    project = BASE_WORKTREE / "MioPrototype"
    if not BASE_WORKTREE.exists():
        print(f"[baseline] ワークツリーを作る: {BASE_WORKTREE}")
        BASE_WORKTREE.parent.mkdir(parents=True, exist_ok=True)
        git("worktree", "add", "--detach", str(BASE_WORKTREE), ref)
    else:
        print(f"[baseline] {sha} へ切り替える")
        git("checkout", "--force", "--detach", ref, cwd=BASE_WORKTREE)

    # Library を丸ごとコピーすると、基準のビルドは差分のインポートだけで済む。
    lib = project / "Library"
    if not lib.exists() and (PROJECT / "Library").exists():
        print("[baseline] Library をコピー中…（初回のみ）")
        r = subprocess.run(["robocopy", str(PROJECT / "Library"), str(lib),
                            "/E", "/MT:16", "/NFL", "/NDL", "/NJH", "/NJS", "/NP"],
                           capture_output=True)
        if r.returncode >= 8:   # robocopy は 8 未満が成功
            sys.exit("[baseline] Library のコピーに失敗。")
    return project, sha


# ---------------------------------------------------------------- 実行

def run_scenario(name: str, outdir: Path, exe: Path) -> list[Path]:
    spec = SCENARIOS[name]
    prefix = outdir / name
    log = outdir / f"{name}.log"
    for old in outdir.glob(f"{name}_*.png"):
        old.unlink()

    cmd = [str(exe), *COMMON, *spec["args"], f"{spec['burst']}{prefix}", "-logFile", str(log)]
    print(f"[run] {name}: 起動")
    proc = subprocess.Popen(cmd, cwd=str(exe.parent))
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
            "worst_line": bool(m["worst"]),
            "raw_subject": m["subject"],                               # 敵1, 敵3 …
            "subject": re.sub(r"\d+$", "", m["subject"]) or m["subject"],  # 敵
            "joint": m["joint"],
            "verdict": m["verdict"],
            "range": m["range"],
            "deg": float(m["deg"]),
            "excess": float(m["excess"]),
            "state": m["state"] or "",
            "count": int(m["count"]) if m["count"] else 1,
        })
    return rows


def entry_key(scenario: str, subject: str, joint: str, verdict: str) -> str:
    return f"{scenario}|{subject}|{joint}|{verdict}"


def aggregate(rows: list[dict], scenario: str) -> dict[str, dict]:
    """(シナリオ, 対象, 関節, 判定) ごとに、最大超過・最悪の角度・件数・最悪のときの状態を出す。

    [JointROMWorst] 行があれば、それが毎サンプルの集計なのでそれを使う（正確）。
    無いときは警告行から集計する（2 秒に 1 回しか出ないので目安）。
    """
    out: dict[str, dict] = {}
    worst_rows = [r for r in rows if r["worst_line"]]
    approx = not worst_rows

    if worst_rows:
        # 累積の行なので、同じ（対象, 関節）は最後の行だけを取る。
        last: dict[tuple, dict] = {}
        for r in worst_rows:
            last[(r["raw_subject"], r["joint"], r["verdict"])] = r
        source = list(last.values())
    else:
        source = rows

    for r in source:
        key = entry_key(scenario, r["subject"], r["joint"], r["verdict"])
        cur = out.get(key)
        if cur is None:
            out[key] = {"scenario": scenario, "subject": r["subject"], "joint": r["joint"],
                        "verdict": r["verdict"], "state": r["state"], "range": r["range"],
                        "max_excess": r["excess"], "worst_deg": r["deg"],
                        "count": r["count"], "approx": approx}
        else:
            cur["count"] += r["count"]
            if r["excess"] > cur["max_excess"]:
                cur["max_excess"] = r["excess"]
                cur["worst_deg"] = r["deg"]
                cur["state"] = r["state"]
    return out


def rom_table(entries: list[dict]) -> str:
    entries = sorted(entries, key=lambda e: -e["max_excess"])
    lines = ["| 順位 | シナリオ | 対象 | 関節 | 判定 | 最悪角度 | 可動域 | 最大超過 | 状態 | 回数 | 精度 |",
             "|---:|---|---|---|---|---:|---|---:|---|---:|---|"]
    for i, e in enumerate(entries, 1):
        lines.append(f"| {i} | {e['scenario']} | {e['subject']} | {e['joint']} | {e['verdict']} | "
                     f"{e['worst_deg']:.0f}° | {e['range']} | {e['max_excess']:.0f}° | "
                     f"{e['state'] or '-'} | {e['count']} | "
                     f"{'目安' if e.get('approx') else '集計'} |")
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
        body.append("")
        body.append("精度「集計」= 毎サンプルの集計（[JointROMWorst]）。「目安」= 警告行だけから作った値で、"
                    "警告は同じ関節につき 2 秒に 1 回なので最悪値を取り逃がしうる。")
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
                draw.text((col * tile_w + 6 + 200, y0 + 3), f"#{i:02d}", fill=(200, 200, 200), font=font)
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out)


# ---------------------------------------------------------------- コマンド

def run_set(exe: Path, label: str, names: list[str], args) -> dict[str, dict]:
    """exe で names を撮って、Verify/<label>/ へ保存する。可動域の集計を返す。"""
    outdir = VERIFY / label
    outdir.mkdir(parents=True, exist_ok=True)

    agg: dict[str, dict] = {}
    for name in names:
        frames = run_scenario(name, outdir, exe)
        if frames:
            make_montage(frames, outdir / f"montage_{name}.png",
                         args.count, args.cols, args.tile)
        else:
            print(f"[run] {name}: 画像が撮れていない。{outdir / (name + '.log')} を見て。")
        agg.update(aggregate(parse_rom(outdir / f"{name}.log"), name))

    write_rom_report(outdir, label, agg, names)
    return agg


def baseline_cached(label: str, names: list[str]) -> bool:
    meta = VERIFY / label / "meta.json"
    if not meta.exists():
        return False
    try:
        done = set(json.loads(meta.read_text(encoding="utf-8")).get("scenarios", []))
    except (OSError, ValueError):
        return False
    return set(names) <= done


def cmd_run(args) -> None:
    label = args.label or time.strftime("run_%Y%m%d_%H%M%S")
    names = [s.strip() for s in args.scenarios.split(",") if s.strip()]
    unknown = [s for s in names if s not in SCENARIOS]
    if unknown:
        sys.exit(f"知らないシナリオ: {unknown}。使えるもの: {', '.join(SCENARIOS)}")

    # 基準（--baseline）。先に撮って、作業ツリーのビルドの前後で exe が混ざらないようにする。
    base_label = None
    if args.baseline:
        sha = git("rev-parse", "--short", args.baseline)
        if not sha:
            sys.exit(f"{args.baseline} が解決できない。")
        base_label = f"base-{sha}"
        if baseline_cached(base_label, names) and not args.refresh_baseline:
            print(f"[baseline] {base_label} は撮影済み。使い回す（撮り直しは --refresh-baseline）。")
        else:
            base_project, sha = prepare_baseline(args.baseline)
            ensure_build(base_project, force=args.refresh_baseline)
            print(f"[baseline] {base_label} を撮る")
            run_set(exe_of(base_project), base_label, names, args)
            (VERIFY / base_label / "meta.json").write_text(json.dumps(
                {"ref": args.baseline, "sha": sha, "scenarios": names}, ensure_ascii=False),
                encoding="utf-8")

    if not args.no_build:
        ensure_build(PROJECT, args.build)
    elif not exe_of(PROJECT).exists():
        sys.exit(f"{exe_of(PROJECT)} が無い。--no-build を外してビルドして。")

    agg = run_set(exe_of(PROJECT), label, names, args)

    print()
    print(f"=== 可動域（{label}）===")
    print(rom_table(list(agg.values())) if agg else "逸脱なし。")
    print()
    print(f"出力: {VERIFY / label}")

    if base_label:
        print()
        do_compare(base_label, label, args.count, 320)


def load_agg(label: str) -> dict[str, dict]:
    p = VERIFY / label / "rom.json"
    if not p.exists():
        sys.exit(f"{p} が無い。先に run --label {label} を実行して。")
    return json.loads(p.read_text(encoding="utf-8"))


def do_compare(a: str, b: str, count: int, tile: int) -> None:
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
        make_compare(af, bf, a, b, outdir / f"{name}.png", count, tile)
        print(f"[compare] {name}: {outdir / (name + '.png')}")
    if not shared:
        print("[compare] 両方にあるシナリオの画像が無い。")

    # 可動域の差分。両方に載っているシナリオだけを比べる（片方が撮っていないものは差に数えない）。
    ra, rb = load_agg(a), load_agg(b)
    scen_a = {e["scenario"] for e in ra.values()} | set(shared)
    scen_b = {e["scenario"] for e in rb.values()} | set(shared)
    common = scen_a & scen_b
    lines = [f"# 可動域の差分: {a} → {b}", ""]
    if common:
        lines += [f"比べたシナリオ: {', '.join(sorted(common))}", ""]
    worse, better, new, gone = [], [], [], []
    for k in sorted(set(ra) | set(rb)):
        ea, eb = ra.get(k), rb.get(k)
        scenario = (ea or eb)["scenario"]
        if scenario not in common:
            continue
        if ea and eb:
            d = eb["max_excess"] - ea["max_excess"]
            # 片方が「目安」（警告だけから作った値）でもう片方が「集計」だと、差は精度の違いも含む。
            mixed = bool(ea.get("approx")) != bool(eb.get("approx"))
            if d > 1:
                worse.append((k, eb, d, mixed))
            elif d < -1:
                better.append((k, eb, d, mixed))
        elif eb:
            new.append((k, eb, eb["max_excess"], False))
        else:
            gone.append((k, ea, -ea["max_excess"], False))

    def section(title, rows):
        lines.append(f"## {title}（{len(rows)}）")
        lines.append("")
        if not rows:
            lines.append("なし")
        for k, e, d, mixed in sorted(rows, key=lambda r: -abs(r[2])):
            note = "（目安）" if e.get("approx") else ""
            if mixed:
                note += "※基準と精度が違う（目安 ↔ 集計）。差は精度の違いを含む"
            lines.append(f"- {e['scenario']} / {e['subject']} {e['joint']} {e['verdict']} "
                         f"[{e['state'] or '-'}]: 最大超過 {e['max_excess']:.0f}°（{d:+.0f}°）{note}")
        lines.append("")

    section("悪化", worse)
    section("新しく出た", new)
    section("改善", better)
    section("消えた", gone)
    (outdir / "rom_diff.md").write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    print(f"出力: {outdir}")


def cmd_compare(args) -> None:
    do_compare(args.a, args.b, args.count, args.tile)


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    r = sub.add_parser("run", help="撮影して可動域ランキングを出す")
    r.add_argument("--label", help="保存名（既定: 日時）")
    r.add_argument("--scenarios", default=",".join(DEFAULT_SCENARIOS),
                   help=f"カンマ区切り。既定 {','.join(DEFAULT_SCENARIOS)}。全部: {','.join(SCENARIOS)}")
    r.add_argument("--baseline", metavar="REF",
                   help="REF（HEAD, HEAD~3, ハッシュなど）を基準として自動でビルド・撮影し、最後に比べる")
    r.add_argument("--refresh-baseline", action="store_true", help="撮影済みの基準を撮り直す")
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
