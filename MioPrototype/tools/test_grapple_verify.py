"""連番キャプチャの 100 枚目以降が比較から落ちないことを固定する。"""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

import grapple_verify


class NumberedFramesTests(unittest.TestCase):
    def test_three_digit_frames_are_found_in_numeric_order(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            d = Path(td)
            for n in (100, 2, 101, 10):
                (d / f"belly_{n:03d}.png").write_bytes(b"")
            (d / "belly_x.png").write_bytes(b"")

            names = [p.name for p in grapple_verify.numbered_frames(d, "belly")]

            self.assertEqual(names, ["belly_002.png", "belly_010.png",
                                     "belly_100.png", "belly_101.png"])


if __name__ == "__main__":
    unittest.main()
