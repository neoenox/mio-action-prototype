"""motion_audit の監査結果がサイレントに 0 にならないことを固定する。"""
from __future__ import annotations

import io
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest.mock import patch

from PIL import Image

import motion_audit


def _spike_sheet(path: Path) -> None:
    """1 列目だけ真っ白な 1 行 4 列のシート（隣との差が突出する）。"""
    im = Image.new("L", (128, 32))
    for i, value in enumerate((0, 255, 255, 255)):
        im.paste(value, (i * 32, 0, (i + 1) * 32, 32))
    im.save(path)


class SheetAuditTests(unittest.TestCase):
    def test_worst_ratio_is_measured(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            sheet = Path(td) / "sheet.png"
            _spike_sheet(sheet)
            with redirect_stdout(io.StringIO()):
                ratio = motion_audit.audit_sheet(sheet, 1, 4, 6)
            self.assertGreaterEqual(ratio, 2.5, "突出した列を検知できていない")

    def test_threshold_fails_the_run(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            sheet = Path(td) / "sheet.png"
            _spike_sheet(sheet)
            argv = ["motion_audit.py", "--sheet", str(sheet), "--rows", "1",
                    "--cols", "4", "--threshold", "2"]
            with patch.object(sys, "argv", argv), redirect_stdout(io.StringIO()):
                code = motion_audit.main()
            self.assertEqual(code, 1, "しきい値超えなのに成功扱いになっている")


if __name__ == "__main__":
    unittest.main()
