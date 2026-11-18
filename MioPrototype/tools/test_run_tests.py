from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

SCRIPT = Path(__file__).with_name("run_tests.py")
SPEC = importlib.util.spec_from_file_location("unity_run_tests", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
RUN_TESTS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUN_TESTS)


class RunBatchExitStatusTests(unittest.TestCase):
    def run_batch_with(self, xml: str, exit_code: int) -> dict:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            logs = root / "Logs"
            batches = {"edit": ("EditMode", None, "unit test", 60)}

            def fake_run_unity(args: list[str], log: Path, timeout: int) -> int:
                log.parent.mkdir(parents=True, exist_ok=True)
                log.write_text("mock Unity log", encoding="utf-8")
                result_path = Path(args[args.index("-testResults") + 1])
                result_path.write_text(xml, encoding="utf-8")
                return exit_code

            with (
                patch.object(RUN_TESTS, "PROJECT", root),
                patch.object(RUN_TESTS, "LOGS", logs),
                patch.object(RUN_TESTS, "BATCHES", batches),
                patch.object(RUN_TESTS, "run_unity", side_effect=fake_run_unity),
            ):
                return RUN_TESTS.run_batch("edit", Path("Unity.exe"), "unit")

    def test_nonzero_unity_exit_fails_even_when_xml_has_only_passed_tests(self) -> None:
        result = self.run_batch_with(
            '<test-run><test-suite><test-case name="pass" result="Passed" />'
            "</test-suite></test-run>",
            7,
        )

        self.assertEqual(result["passed"], 1)
        self.assertEqual(result["failed"], 0)
        self.assertEqual(result["runner_exit_code"], 7)
        self.assertFalse(result["ok"])

    def test_zero_executed_tests_is_not_success(self) -> None:
        result = self.run_batch_with("<test-run><test-suite /></test-run>", 0)

        self.assertEqual(result["total"], 0)
        self.assertFalse(result["ok"])
        self.assertEqual(RUN_TESTS.result_exit_code([result]), 1)

    def test_only_all_successful_nonempty_batches_return_zero(self) -> None:
        self.assertEqual(RUN_TESTS.result_exit_code([{"ok": True}]), 0)
        self.assertEqual(RUN_TESTS.result_exit_code([{"ok": True}, {"ok": False}]), 1)
        self.assertEqual(RUN_TESTS.result_exit_code([]), 1)


if __name__ == "__main__":
    unittest.main()
