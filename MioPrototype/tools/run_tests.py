#!/usr/bin/env python
"""Unity のテストをクラス分割で一括実行する。

普通に Unity Test Runner を 1 回で回すと 30〜60 分かかるので、
EditMode と PlayMode をいくつかのまとまりに分けて順に走らせる。
1 バッチ 1〜5 分、全体で 10〜15 分が目安。

使い方（MioPrototype/ で実行）:

    python tools/run_tests.py                  # コンパイル確認 → 全部
    python tools/run_tests.py --only edit      # EditMode だけ
    python tools/run_tests.py --only grapple   # 拘束系の PlayMode だけ
    python tools/run_tests.py --skip-compile   # コンパイル確認を省略（速い）
    python tools/run_tests.py --list           # バッチ名の一覧

終了コード: 0 = 全部通過 / 1 = 失敗あり / 2 = 実行できなかった（Unity 未検出など）

結果は MioPrototype/Logs/tests-<batch>-<日時>.xml / .log に残る。
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

PROJECT = Path(__file__).resolve().parent.parent          # MioPrototype/
LOGS = PROJECT / "Logs"

# バッチ名 -> (プラットフォーム, テスト名フィルタ, 説明, タイムアウト秒)
# フィルタは Unity の -testFilter（クラス名の正規表現。| で複数指定できる）。
BATCHES: dict[str, tuple[str, str | None, str, int]] = {
    "edit": ("EditMode", None,
             "戦闘ルール・関節可動域（EditMode）", 900),
    "grapple": ("PlayMode", "WallPinRuntime|PinGrapple|HeldBellyPunch|BearHug|OverheadRuntime",
                "拘束・壁・腹パン（PlayMode）", 2400),
    "motion": ("PlayMode", "DownRuntime|FeelRuntime|HurtPose|JointRomRuntime",
               "ダウン・手応え・被弾姿勢（PlayMode）", 1800),
    "misc": ("PlayMode", "EnemyAiRuntime|PlayabilityRegression|PlayerFaceRuntime|TestAudio|UiRuntime|VrmRuntime",
             "AI・UI・表情・VRM・遊びの回帰（PlayMode）", 2400),
}

ALL_ORDER = ["edit", "grapple", "motion", "misc"]
DEFAULT_TIMEOUT = 2400
COMPILE_TIMEOUT = 900


# ------------------------------------------------------------------ 環境

def unity_exe(project: Path) -> Path | None:
    """ProjectVersion.txt と同じバージョンの Unity.exe を探す。"""
    import re
    version = None
    pv = project / "ProjectSettings" / "ProjectVersion.txt"
    if pv.exists():
        m = re.search(r"m_EditorVersion:\s*(\S+)", pv.read_text(encoding="utf-8"))
        version = m.group(1) if m else None
    if not version:
        return None
    for base in (Path(r"C:\Program Files\Unity\Hub\Editor"),
                 Path.home() / "Unity" / "Editor"):
        p = base / version / "Editor" / "Unity.exe"
        if p.exists():
            return p
    return None


def unity_running() -> bool:
    try:
        out = subprocess.run(["tasklist", "/fi", "imagename eq Unity.exe"],
                             capture_output=True, text=True,
                             encoding="utf-8", errors="replace").stdout
    except OSError:
        return False
    return "Unity.exe" in out


# ------------------------------------------------------------------ 実行

def run_unity(args: list[str], log: Path, timeout: int) -> int:
    log.parent.mkdir(parents=True, exist_ok=True)
    print(f"  > {' '.join(args[-6:])}", flush=True)
    try:
        return subprocess.run(args + ["-logFile", str(log)],
                              timeout=timeout).returncode
    except subprocess.TimeoutExpired:
        print(f"  ! タイムアウト（{timeout} 秒）。ログ: {log}")
        return -1


def compile_check(unity: Path, stamp: str) -> bool:
    print("[compile] スクリプトのコンパイル確認")
    log = LOGS / f"tests-compile-{stamp}.log"
    code = run_unity([str(unity), "-batchmode", "-nographics", "-quit",
                      "-projectPath", str(PROJECT)], log, COMPILE_TIMEOUT)
    text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
    bad = "Scripts have compiler errors" in text or "\nerror CS" in text
    if bad:
        print(f"  x コンパイルエラー。ログ: {log}")
        for line in text.splitlines():
            if "error CS" in line:
                print("    " + line.strip())
        return False
    if code != 0:
        print(f"  ! Unity が終了コード {code} で終了。ログ: {log}")
        return False
    print("  ok")
    return True


def parse_results(xml: Path) -> dict:
    """NUnit 形式の結果を数える。失敗テストの名前とメッセージも返す。"""
    root = ET.parse(xml).getroot()
    passed = failed = skipped = 0
    failures: list[tuple[str, str]] = []
    for case in root.iter("test-case"):
        result = case.get("result")
        name = case.get("name") or "?"
        cls = case.get("classname") or ""
        if result == "Passed":
            passed += 1
        elif result == "Failed":
            failed += 1
            msg = ""
            failure = case.find("failure")
            if failure is not None:
                m = failure.find("message")
                if m is not None and m.text:
                    msg = m.text.strip().splitlines()[0]
            failures.append((f"{cls}.{name}" if cls else name, msg))
        elif result in ("Skipped", "Ignored", "Inconclusive"):
            skipped += 1
    total = passed + failed + skipped
    return {"total": total, "passed": passed, "failed": failed,
            "skipped": skipped, "failures": failures}


def run_batch(name: str, unity: Path, stamp: str) -> dict:
    platform, test_filter, description, timeout = BATCHES[name]
    print(f"[{name}] {description}")
    xml = LOGS / f"tests-{name}-{stamp}.xml"
    log = LOGS / f"tests-{name}-{stamp}.log"
    if xml.exists():
        xml.unlink()

    args = [str(unity), "-batchmode", "-nographics", "-runTests",
            "-projectPath", str(PROJECT), "-testPlatform", platform,
            "-testResults", str(xml)]
    if test_filter:
        args += ["-testFilter", test_filter]

    started = time.time()
    code = run_unity(args, log, timeout)
    elapsed = time.time() - started

    if not xml.exists():
        print(f"  x 結果 XML が出なかった（終了コード {code}）。ログ: {log}")
        return {"batch": name, "ok": False, "total": 0, "passed": 0,
                "failed": 0, "skipped": 0, "failures": [], "elapsed": elapsed}

    stats = parse_results(xml)
    stats.update(batch=name, ok=stats["failed"] == 0, elapsed=elapsed)
    if stats["total"] == 0:
        # フィルタの綴り間違いなどで 1 件も走らないのは「成功」ではない。
        stats["ok"] = False
        print(f"  x テストが 1 件も走っていない（フィルタ: {test_filter}）")
        return stats
    if stats["ok"]:
        print(f"  ok  {stats['passed']}/{stats['total']}（{elapsed:.0f} 秒）")
    else:
        print(f"  x  {stats['passed']}/{stats['total']} 失敗 {stats['failed']}（{elapsed:.0f} 秒）")
        for test, msg in stats["failures"]:
            print(f"    - {test}")
            if msg:
                print(f"      {msg}")
        print(f"    結果: {xml}")
    return stats


# ------------------------------------------------------------------ 入口

def main() -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except AttributeError:
        pass

    parser = argparse.ArgumentParser(
        description="Unity のテストをクラス分割で一括実行する。")
    parser.add_argument("--only", help="実行するバッチ（カンマ区切り: edit,grapple,motion,misc）")
    parser.add_argument("--skip-compile", action="store_true", help="コンパイル確認を省略する")
    parser.add_argument("--unity", help="Unity.exe のパスを明示する")
    parser.add_argument("--list", action="store_true", help="バッチ一覧を出して終了する")
    args = parser.parse_args()

    if args.list:
        for name in ALL_ORDER:
            platform, flt, desc, _ = BATCHES[name]
            print(f"  {name:8s} {platform:8s} {desc}")
        return 0

    names = ALL_ORDER
    if args.only:
        names = [n.strip() for n in args.only.split(",") if n.strip()]
        unknown = [n for n in names if n not in BATCHES]
        if unknown:
            print(f"知らないバッチ: {unknown}。使えるもの: {', '.join(ALL_ORDER)}")
            return 2

    unity = Path(args.unity) if args.unity else unity_exe(PROJECT)
    if unity is None or not unity.exists():
        print("Unity.exe が見つからない。--unity で指定するか、Unity Hub のインストールを確認する。")
        return 2
    if unity_running():
        print("Unity Editor が起動中。閉じてから実行する（バッチモードと競合する）。")
        return 2

    stamp = time.strftime("%Y%m%d-%H%M%S")
    LOGS.mkdir(exist_ok=True)

    print(f"project: {PROJECT}")
    print(f"unity:   {unity}")
    print(f"batches: {', '.join(names)}")
    print()

    if not args.skip_compile and not compile_check(unity, stamp):
        print()
        print("コンパイルが通らないため、テストは実行していません。")
        return 1

    started = time.time()
    results = [run_batch(name, unity, stamp) for name in names]
    elapsed = time.time() - started

    print()
    print("===== まとめ =====")
    for r in results:
        mark = "ok" if r["ok"] else "x "
        print(f"  {mark} {r['batch']:8s} {r['passed']:3d}/{r['total']:3d} "
              f"失敗 {r['failed']:2d}  {r['elapsed']:.0f}s")
    total_failed = sum(r["failed"] for r in results)
    total_all = sum(r["total"] for r in results)
    print(f"  合計 {total_all} 件 / 失敗 {total_failed} 件 / {elapsed:.0f} 秒")
    if total_failed:
        print("  結果 XML は Logs/tests-*.xml に残っています。")
    return 0 if total_failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
