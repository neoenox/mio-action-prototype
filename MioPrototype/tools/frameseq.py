"""撮影した連番（`f_000.png` …）の置き場所と並べ方。

preview.py / preview_reel.py / motion_audit.py で共有する。
ファイル名の規則（`<接頭辞>_<番号>.png`）を変えるときはここだけ直す。
"""
from __future__ import annotations

import os
from pathlib import Path

PROJECT = Path(__file__).resolve().parent.parent      # MioPrototype/
PREVIEW_ROOT = PROJECT / "Verify" / "preview"

# preview.py は `f_*` で撮る。古い -pinburst 系の撮影は場面名の接頭辞で残っている。
LEGACY_PREFIXES = ("f", "pin", "belly", "face", "knee", "front")


def frame_index(path: str | os.PathLike[str]) -> int:
    """`f_012.png` → 12。"""
    return int(os.path.splitext(os.path.basename(path))[0].split("_")[1])


def frame_files(directory: str | os.PathLike[str], prefixes: tuple[str, ...] = ("f",)) -> list[Path]:
    """`directory` の連番を番号順に返す。接頭辞が違っても同じ番号順に並べる。"""
    d = Path(directory)
    files = {p for prefix in prefixes for p in d.glob(f"{prefix}_*.png")}
    return sorted(files, key=frame_index)


def count_frames(directory: str | os.PathLike[str]) -> int:
    """撮影中の進み具合を見るための枚数（`f_*` のみ）。"""
    return sum(1 for _ in Path(directory).glob("f_*.png"))
