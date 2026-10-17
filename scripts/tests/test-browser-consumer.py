"""Controls for the browser consumer's acceptance boundaries."""

import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

specification = importlib.util.spec_from_file_location(
    "browser_consumer",
    Path(__file__).resolve().parents[1] / "unity/run-browser-consumer.py",
)
consumer = importlib.util.module_from_spec(specification)
specification.loader.exec_module(consumer)


class BrowserConsumerTests(unittest.TestCase):
    def test_exact_case_inventory(self):
        cases = consumer.cases()
        self.assertEqual(23, len(cases))
        self.assertEqual(23, len(set(cases)))
        self.assertIn(("safety", "populated"), cases)
        self.assertIn(("serialize-read", "populated"), cases)
        self.assertIn(("serialize-read", "null"), cases)
        self.assertIn(("merge", "populated"), cases)
        self.assertIn(("presence-foreign", "presence"), cases)
        self.assertEqual(6, sum(mode.startswith("presence-") for mode, _ in cases))

    def test_generated_profile_preserves_legacy_tags_and_removes_suppression(self):
        source = "#pragma warning disable WPROTO030\n[ProtoContract] class Doc { [ProtoMember(7)] public int Value; }\n#pragma warning restore WPROTO030\n"
        generated = consumer.generated_contract(source, [7])
        self.assertIn("[ProtoContract, global::", generated)
        self.assertIn("WProtoContract]", generated)
        self.assertIn("partial class Doc", generated)
        self.assertIn("[ProtoMember(7), global::", generated)
        self.assertIn("WProtoMember(7)]", generated)
        self.assertNotIn("#pragma", generated)

    def test_changed_contract_shape_requires_review(self):
        for source in (
            "",
            "[ProtoContract] class Doc {}",
            "[ProtoContract] class Doc { [ProtoMember(8)] int Value; }",
        ):
            with self.subTest(source=source), self.assertRaises(ValueError):
                consumer.generated_contract(source, [7])

    def test_all_four_real_contracts_receive_generated_annotations(self):
        template = (
            Path(__file__).resolve().parents[2]
            / "Tests/Acceptance~/NullableMapConsumer/Assets/Consumer"
        )
        declarations = {
            "Doc": [1, 7, 11, 17, 23],
            "OwnedMessage": [1, 3, 5],
            "PresenceValue": [1],
            "PresenceDoc": list(range(1, 25)),
        }
        for name, tags in declarations.items():
            with self.subTest(contract=name):
                generated = consumer.generated_contract(
                    (template / f"{name}.cs").read_text(), tags
                )
                self.assertEqual(len(tags), generated.count("WProtoMember("))
                self.assertEqual(1, generated.count("WProtoContract]"))

    def test_browser_startup_failure_gets_current_failed_status(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            first = consumer.new_browser_run(root)
            consumer.record_browser_status(root, first, "passed")
            with self.assertRaises((ModuleNotFoundError, FileNotFoundError)):
                consumer.run_browser(root)
            latest = json.loads((root / "latest-run.json").read_text())
            self.assertEqual("failed", latest["status"])
            self.assertNotEqual(first.relative_to(root).as_posix(), latest["run"])

    def test_result_identity_is_exact(self):
        expected = "Passed|serialize-read|populated|6000.4.6f1|source=correct"
        consumer.validate_result(expected, expected, [])
        for actual in (
            "",
            expected + "\n",
            expected.replace("correct", "stale"),
            "Failed|failure",
        ):
            with self.subTest(actual=actual), self.assertRaises(RuntimeError):
                consumer.validate_result(actual, expected, [])

    def test_browser_errors_cannot_pass_with_success_receipt(self):
        with self.assertRaises(RuntimeError):
            consumer.validate_result("Passed", "Passed", ["abort at runtime"])

    def test_inventory_rejects_empty_subjects(self):
        with (
            tempfile.TemporaryDirectory() as temporary,
            self.assertRaises(RuntimeError),
        ):
            consumer.require_inventory(Path(temporary), {})

    def test_inventory_detects_changed_added_and_missing_files(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "player.wasm"
            source.write_bytes(b"original")
            expected = consumer.inventory(root)
            consumer.require_inventory(root, expected)
            source.write_bytes(b"changed")
            with self.assertRaises(RuntimeError):
                consumer.require_inventory(root, expected)
            source.write_bytes(b"original")
            added = root / "extra.data"
            added.write_bytes(b"extra")
            with self.assertRaises(RuntimeError):
                consumer.require_inventory(root, expected)
            added.unlink()
            source.unlink()
            with self.assertRaises(RuntimeError):
                consumer.require_inventory(root, expected)

    def test_existing_artifacts_are_never_overwritten(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            sentinel = root / "kept.txt"
            sentinel.write_text("prior evidence")
            with self.assertRaises(ValueError):
                consumer.prepare(root, root, root / "editor", "6000.4.6f1")
            self.assertEqual("prior evidence", sentinel.read_text())

    def test_editor_nonzero_exit_is_recorded_and_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            process = MagicMock()
            process.__enter__.return_value = process
            process.wait.return_value = 5
            process.poll.return_value = 5
            process.pid = 42
            with (
                patch.object(consumer.subprocess, "Popen", return_value=process),
                self.assertRaises(RuntimeError),
            ):
                consumer.run_editor(
                    root / "Unity", root, "Build", root / "build.log", root / "player"
                )
            record = json.loads((root / "build.process.json").read_text())
            self.assertEqual(5, record["exitCode"])
            self.assertFalse(record["interrupted"])

    def test_editor_watchdog_retains_interruption_and_reaps_process(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            process = MagicMock()
            process.__enter__.return_value = process
            process.wait.side_effect = [subprocess.TimeoutExpired("Unity", 1), 0]
            process.poll.side_effect = [None, 0]
            process.pid = 42
            with (
                patch.object(consumer.subprocess, "Popen", return_value=process),
                patch.object(consumer.subprocess, "run"),
                patch.object(consumer.os, "killpg", create=True) as kill_group,
                self.assertRaises(subprocess.TimeoutExpired),
            ):
                consumer.run_editor(
                    root / "Unity", root, "Build", root / "build.log", root / "player"
                )
            self.assertEqual(2, process.wait.call_count)
            if consumer.os.name != "nt":
                kill_group.assert_called_once_with(42, consumer.signal.SIGKILL)
            record = json.loads((root / "build.process.json").read_text())
            self.assertTrue(record["interrupted"])
            self.assertEqual(0, record["exitCode"])

    def test_rerun_preserves_earlier_receipts_and_replaces_latest_status(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            first = consumer.new_browser_run(root)
            (first / "result.json").write_text("original failure evidence")
            consumer.record_browser_status(root, first, "passed")
            second = consumer.new_browser_run(root)
            consumer.record_browser_status(root, second, "failed", "browser timeout")
            self.assertNotEqual(first, second)
            self.assertEqual(
                "original failure evidence", (first / "result.json").read_text()
            )
            self.assertEqual(
                "passed", json.loads((first / "status.json").read_text())["status"]
            )
            latest = json.loads((root / "latest-run.json").read_text())
            self.assertEqual("failed", latest["status"])
            self.assertEqual(second.relative_to(root).as_posix(), latest["run"])


if __name__ == "__main__":
    unittest.main()
