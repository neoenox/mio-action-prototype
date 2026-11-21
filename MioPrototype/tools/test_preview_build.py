from __future__ import annotations

import contextlib
import importlib.util
import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

TOOLS = Path(__file__).resolve().parent
# preview.py は同じディレクトリの frameseq を直接 import する。
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

SCRIPT = TOOLS / "preview.py"
SPEC = importlib.util.spec_from_file_location("unity_preview", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
PREVIEW = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = PREVIEW
SPEC.loader.exec_module(PREVIEW)


class BuildOutcomeTests(unittest.TestCase):
    """終了コードを主、ログを従にした成否判定（BuildAndPlay.cmd と同じ基準）。"""

    def test_zero_exit_with_succeeded_log_is_ok(self) -> None:
        self.assertEqual(
            PREVIEW.build_outcome(0, "build result=Succeeded"), PREVIEW.BUILD_OK)

    def test_zero_exit_with_up_to_date_is_ok(self) -> None:
        self.assertEqual(PREVIEW.build_outcome(0, "[build] up to date"), PREVIEW.BUILD_OK)

    def test_nonzero_exit_fails_even_when_log_reports_success(self) -> None:
        outcome = PREVIEW.build_outcome(1, "build result=Succeeded\nup to date")

        self.assertEqual(outcome, PREVIEW.BUILD_EXIT_CODE)
        self.assertNotEqual(outcome, PREVIEW.BUILD_OK)

    def test_zero_exit_with_compile_error_fails(self) -> None:
        outcome = PREVIEW.build_outcome(
            0, "Assets/X.cs(1,2): error CS0103: name not found\nbuild result=Succeeded")

        self.assertEqual(outcome, PREVIEW.BUILD_COMPILE_ERROR)

    def test_zero_exit_without_success_record_is_not_ok(self) -> None:
        self.assertEqual(PREVIEW.build_outcome(0, "nothing"), PREVIEW.BUILD_UNCONFIRMED)
        self.assertEqual(PREVIEW.build_outcome(0, ""), PREVIEW.BUILD_UNCONFIRMED)


class BuildPlayerTests(unittest.TestCase):
    """build_player() が判定結果どおりに True / False を返すこと（Unity は起動しない）。"""

    def build_with(self, exit_code: int, log_text: str) -> bool:
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            (project / "Logs").mkdir()
            (project / "Logs" / "preview-build.log").write_text(log_text, encoding="utf-8")
            completed = subprocess.CompletedProcess(args=["Unity.exe"], returncode=exit_code)
            with (
                patch.object(PREVIEW, "PROJECT", project),
                patch.object(PREVIEW, "unity_exe", return_value=Path("Unity.exe")),
                patch.object(PREVIEW, "unity_running", return_value=False),
                patch.object(PREVIEW.subprocess, "run", return_value=completed),
                contextlib.redirect_stdout(io.StringIO()),
            ):
                return PREVIEW.build_player()

    def test_nonzero_exit_returns_false(self) -> None:
        self.assertFalse(self.build_with(3, "build result=Succeeded"))

    def test_up_to_date_returns_true(self) -> None:
        self.assertTrue(self.build_with(0, "[build] up to date"))

    def test_compile_error_returns_false(self) -> None:
        self.assertFalse(self.build_with(0, "Assets/X.cs(1,2): error CS0103: name not found"))


if __name__ == "__main__":
    unittest.main()
