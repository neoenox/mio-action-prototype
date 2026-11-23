"""このプロジェクトを開いている Unity が動いているかを調べる。

以前は「Unity.exe がどこかで動いているか」（tasklist）で判定していた。worktree を分けて
二人で並行開発したり、CI の self-hosted ランナーが同じ PC で Unity を回したりすると、
別のフォルダの Unity でも「起動中」と判定してビルドや撮影が止まった（2026-11-22）。

Unity は 1 つのプロジェクトフォルダを 1 プロセスでしか開けないが、別のフォルダなら
同時に動かせる。そこで次のどちらかに当たるときだけ「このプロジェクトで起動中」とする。

1. Unity.exe のコマンドラインの ``-projectPath`` がこのプロジェクトを指している
2. ``Temp/UnityLockfile`` が開けない（Unity が掴んでいる）

コマンドラインが読めないとき（権限・PowerShell が無い等）は、取りこぼさないよう
従来どおり「Unity.exe が動いていれば起動中」に倒す。
"""

from __future__ import annotations

import os
import re
import subprocess
from pathlib import Path

_PROJECT_PATH = re.compile(r'-projectPath\s+(?:"([^"]+)"|(\S+))', re.IGNORECASE)


def _same_path(a: str, b: Path) -> bool:
    try:
        left = Path(a).resolve()
    except OSError:
        left = Path(a)
    return os.path.normcase(str(left)) == os.path.normcase(str(b.resolve()))


def _unity_command_lines() -> list[str] | None:
    """動いている Unity.exe のコマンドライン。読めなければ None。"""
    script = ("Get-CimInstance Win32_Process -Filter \"name='Unity.exe'\" | "
              "ForEach-Object { $_.CommandLine }")
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", script],
                             capture_output=True, text=True, encoding="utf-8",
                             errors="replace", timeout=20)
    except (OSError, subprocess.SubprocessError):
        return None
    if out.returncode != 0:
        return None
    return [line for line in out.stdout.splitlines() if line.strip()]


def _any_unity_running() -> bool:
    try:
        out = subprocess.run(["tasklist", "/fi", "imagename eq Unity.exe"],
                             capture_output=True, text=True, encoding="utf-8",
                             errors="replace").stdout
    except OSError:
        return False
    return "Unity.exe" in out


def _lockfile_held(project: Path) -> bool:
    lock = project / "Temp" / "UnityLockfile"
    if not lock.exists():
        return False
    try:
        with open(lock, "a"):
            return False
    except OSError:
        return True


def project_open(project: Path) -> bool:
    """``project``（MioPrototype/）を開いている Unity があれば True。"""
    if _lockfile_held(project):
        return True
    lines = _unity_command_lines()
    if lines is None:
        return _any_unity_running()
    for line in lines:
        m = _PROJECT_PATH.search(line)
        if m is None:
            # -projectPath の無い起動（Hub を通さない Editor 等）は、どのプロジェクトか
            # 分からないので安全側に倒す。
            return True
        if _same_path(m.group(1) or m.group(2), project):
            return True
    return False
