from __future__ import annotations

import importlib.util
import io
import tempfile
import unittest
import xml.etree.ElementTree as ET
from contextlib import redirect_stdout
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


class ParseResultsEdgeCaseTests(unittest.TestCase):
    """parse_results() は読めない入力を ET.ParseError で知らせる（現行仕様）。"""

    def parse(self, text: str | None) -> dict:
        with tempfile.TemporaryDirectory() as directory:
            xml = Path(directory) / "tests-edit-unit.xml"
            if text is not None:
                xml.write_text(text, encoding="utf-8")
            return RUN_TESTS.parse_results(xml)

    def test_empty_file_raises_parse_error(self) -> None:
        with self.assertRaises(ET.ParseError):
            self.parse("")

    def test_broken_xml_raises_parse_error(self) -> None:
        with self.assertRaises(ET.ParseError):
            self.parse('<test-run><test-case name="a" result="Passed">')

    def test_xml_without_test_case_elements_counts_as_zero(self) -> None:
        stats = self.parse("<test-run><test-suite /></test-run>")

        self.assertEqual(stats["total"], 0)
        self.assertEqual(stats["passed"], 0)
        self.assertEqual(stats["failed"], 0)
        self.assertEqual(stats["skipped"], 0)
        self.assertEqual(stats["failures"], [])

    def test_skipped_ignored_and_inconclusive_are_counted_as_skipped(self) -> None:
        stats = self.parse(
            "<test-run><test-suite>"
            '<test-case name="a" result="Skipped" />'
            '<test-case name="b" result="Ignored" />'
            '<test-case name="c" result="Inconclusive" />'
            "</test-suite></test-run>"
        )

        self.assertEqual(stats["total"], 3)
        self.assertEqual(stats["skipped"], 3)
        self.assertEqual(stats["passed"], 0)
        self.assertEqual(stats["failed"], 0)

    def test_failure_reports_class_name_and_first_message_line(self) -> None:
        stats = self.parse(
            '<test-run><test-suite><test-case classname="A.B" name="C" result="Failed">'
            "<failure><message>\n  1行目の理由\n2行目の詳細\n</message></failure>"
            "</test-case></test-suite></test-run>"
        )

        self.assertEqual(stats["failed"], 1)
        self.assertEqual(stats["failures"], [("A.B.C", "1行目の理由")])


class RunUnityTimeoutTests(unittest.TestCase):
    """Unity がタイムアウトした経路を subprocess のモックで固定する。"""

    def test_timeout_returns_minus_one_instead_of_raising(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "Logs" / "tests-edit-unit.log"
            output = io.StringIO()
            with (
                patch.object(
                    RUN_TESTS.subprocess,
                    "run",
                    side_effect=RUN_TESTS.subprocess.TimeoutExpired(["Unity.exe"], 60),
                ),
                redirect_stdout(output),
            ):
                code = RUN_TESTS.run_unity(["Unity.exe"], log, 60)

            self.assertEqual(code, -1)
            self.assertIn("タイムアウト", output.getvalue())
            self.assertTrue(log.parent.is_dir())

    def test_timed_out_batch_without_result_xml_is_failure(self) -> None:
        with (
            tempfile.TemporaryDirectory() as directory,
            redirect_stdout(io.StringIO()),
        ):
            result = run_batch_with(Path(directory), xml=None, exit_code=-1)

        self.assertEqual(result["total"], 0)
        self.assertFalse(result["ok"])
        self.assertEqual(RUN_TESTS.result_exit_code([result]), 1)


def run_batch_with(root: Path, xml: str | None, exit_code: int) -> dict:
    """run_batch() を 1 バッチ（edit）だけで回す。xml=None なら結果 XML を書かない。"""
    logs = root / "Logs"
    batches = {"edit": ("EditMode", None, "unit test", 60)}

    def fake_run_unity(args: list[str], log: Path, timeout: int) -> int:
        log.parent.mkdir(parents=True, exist_ok=True)
        log.write_text("mock Unity log", encoding="utf-8")
        if xml is not None:
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


class BatchResultEdgeCaseTests(unittest.TestCase):
    """結果 XML が欠落・破損・Skipped のみのときのバッチ判定を固定する。"""

    def test_missing_result_xml_is_failure(self) -> None:
        with (
            tempfile.TemporaryDirectory() as directory,
            redirect_stdout(io.StringIO()),
        ):
            result = run_batch_with(Path(directory), xml=None, exit_code=0)

        self.assertEqual(result["batch"], "edit")
        self.assertEqual(result["total"], 0)
        self.assertFalse(result["ok"])
        self.assertEqual(RUN_TESTS.result_exit_code([result]), 1)

    def test_corrupted_result_xml_is_reported_as_failure(self) -> None:
        """破損 XML でトレースバックに落ちず、バッチ 1 本の失敗として返る。"""
        with (
            tempfile.TemporaryDirectory() as directory,
            redirect_stdout(io.StringIO()),
        ):
            result = run_batch_with(
                Path(directory), xml="<test-run><test-case", exit_code=0
            )

        self.assertEqual(result["batch"], "edit")
        self.assertEqual(result["total"], 0)
        self.assertEqual(result["runner_exit_code"], 0)
        self.assertFalse(result["ok"])
        self.assertEqual(RUN_TESTS.result_exit_code([result]), 1)

    def test_skipped_only_result_is_success_by_current_spec(self) -> None:
        """Skipped は「実行 1 件」として数える現行仕様を固定する。

        0 件実行（total == 0）は失敗だが、全部 Skipped は成功扱いのまま。
        この差が意図的なのかは別途判断が必要なので、ここでは現状だけを留める。
        """
        with (
            tempfile.TemporaryDirectory() as directory,
            redirect_stdout(io.StringIO()),
        ):
            result = run_batch_with(
                Path(directory),
                xml="<test-run><test-suite>"
                '<test-case name="a" result="Skipped" />'
                '<test-case name="b" result="Ignored" />'
                "</test-suite></test-run>",
                exit_code=0,
            )

        self.assertEqual(result["total"], 2)
        self.assertEqual(result["skipped"], 2)
        self.assertEqual(result["failed"], 0)
        self.assertTrue(result["ok"])
        self.assertEqual(RUN_TESTS.result_exit_code([result]), 0)


class MainBatchSelectionTests(unittest.TestCase):
    """main() の --only 解析を sys.argv の差し替えで固定する。"""

    def run_main(self, argv: list[str], unity: Path | None = None) -> tuple[int, str]:
        output = io.StringIO()
        with (
            patch.object(RUN_TESTS.sys, "argv", ["run_tests.py", *argv]),
            patch.object(RUN_TESTS, "unity_exe", return_value=unity),
            patch.object(RUN_TESTS, "unity_running", return_value=False),
            redirect_stdout(output),
        ):
            code = RUN_TESTS.main()
        return code, output.getvalue()

    def test_unknown_batch_name_is_rejected_before_looking_for_unity(self) -> None:
        code, output = self.run_main(["--only", "bogus"])

        self.assertEqual(code, 2)
        self.assertIn("知らないバッチ", output)
        self.assertIn("bogus", output)

    def test_unknown_batch_mixed_with_known_name_is_rejected(self) -> None:
        code, output = self.run_main(["--only", "edit, bogus"])

        self.assertEqual(code, 2)
        self.assertIn("知らないバッチ", output)

    def test_empty_only_falls_back_to_all_batches(self) -> None:
        """`--only ""` はバッチ未指定と同じ扱い（不正値エラーにはしない）。"""
        code, output = self.run_main(["--only", ""])

        self.assertEqual(code, 2)
        self.assertNotIn("知らないバッチ", output)
        self.assertIn("Unity.exe", output)


class MultipleBatchFailureTests(unittest.TestCase):
    """複数バッチの途中で失敗しても、残りを走らせてから終了コード 1 を返す。"""

    def run_main_with_batches(
        self, argv: list[str], oks: list[bool]
    ) -> tuple[int, str, list[str]]:
        calls: list[str] = []
        results = [
            {
                "batch": name,
                "ok": ok,
                "total": 3,
                "passed": 3 if ok else 0,
                "failed": 0 if ok else 1,
                "skipped": 0,
                "failures": [],
                "elapsed": 1.0,
            }
            for name, ok in zip(argv[-1].split(","), oks, strict=True)
        ]

        def fake_run_batch(name: str, unity: Path, stamp: str) -> dict:
            calls.append(name)
            return next(r for r in results if r["batch"] == name)

        output = io.StringIO()
        with (
            tempfile.TemporaryDirectory() as directory,
            patch.object(RUN_TESTS.sys, "argv", ["run_tests.py", *argv]),
            patch.object(RUN_TESTS, "unity_exe", return_value=SCRIPT),
            patch.object(RUN_TESTS, "unity_running", return_value=False),
            patch.object(RUN_TESTS, "LOGS", Path(directory)),
            patch.object(RUN_TESTS, "compile_check", return_value=True),
            patch.object(RUN_TESTS, "run_batch", side_effect=fake_run_batch),
            redirect_stdout(output),
        ):
            code = RUN_TESTS.main()
        return code, output.getvalue(), calls

    def test_failure_in_the_middle_still_runs_later_batches(self) -> None:
        code, output, calls = self.run_main_with_batches(
            ["--only", "edit,grapple,motion"], [True, False, True]
        )

        self.assertEqual(calls, ["edit", "grapple", "motion"])
        self.assertEqual(code, 1)
        self.assertIn("合計", output)

    def test_all_batches_succeed_returns_zero(self) -> None:
        code, _, calls = self.run_main_with_batches(
            ["--only", "edit,grapple"], [True, True]
        )

        self.assertEqual(calls, ["edit", "grapple"])
        self.assertEqual(code, 0)


if __name__ == "__main__":
    unittest.main()
