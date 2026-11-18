"""Preserve the older-editor workflow, CLI, and publication contract."""

from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
import generate_compatibility_workflow as renderer

ROOT = Path(__file__).resolve().parents[2]

SAMPLE = """\
name: Unity Tests
jobs:
  matrix-config:
    name: Resolve test matrix
    if: ${{ !(github.event_name == 'workflow_dispatch' && inputs.native-readback-run-id != '') && always() }}
    outputs:
      combine-variants: >-
        ${{ inputs.latest-mode-scheduling == 'combined' }}
      test-modes: "all"
    steps:
      - name: Explicit scheduling
        if: ${{ inputs.latest-mode-scheduling == 'combined' }}
        run: echo skip
      - name: Resolve matrix
        env:
          INPUT_UNITY_VERSION: ${{ inputs.unity-version }}
          INPUT_TEST_MODE: ${{ inputs.test-mode }}
          INPUT_ACCEPTANCE: ${{ inputs.acceptance }}
        run: echo run
  runner-preflight:
    name: Verify runner
    concurrency:
      group: >-
        ${{ github.workflow }}-${{ github.event_name }}
    steps:
      - name: Validate runner requirements
        with:
          required-label-sets: >-
            ${{ inputs.native-diagnostics && '[]' || '[["self-hosted"]]' }}
  unity-tests:
    name: Selected native modes
    if: >-
      ${{ always() }}
    runs-on: [self-hosted, Windows, RAM-64GB]
    strategy:
      # No max-parallel in foreground validation.
      # Keep the enrollment explanation.
      # The enrollment audit confirms the runner is visible.
      matrix:
        mode: [playmode]
    steps:
      - name: Dispatch mode
        if: ${{ needs.matrix-config.outputs.combine-variants == 'true' }}
        run: echo skip
      - name: Run tests
        env:
          NATIVE: ${{ inputs.native-loader-snaps-comparison }}
        run: echo run
  unity-tests-aggregate:
    name: Unity CI Success
    if: ${{ (always()) }}
    steps:
      - run: echo done
  excluded:
    name: Independent regular job
    steps:
      - run: echo omitted
"""
RAW_HASH = "a5f2394d44fbe43d0dbfa6107cad66252deb63f6e25ee3410db7fbfdc7bc87d0"
FORMATTED_HASH = "c2fef9aa752eb1c524363d9e90d55508c7241fc5d5d70e69ce3313d5e8dc9263"
VALIDATOR = ROOT / "scripts/unity/generate_compatibility_workflow.py"


@pytest.fixture
def checkout(tmp_path: Path) -> Path:
    root = tmp_path / "compatibility workflow [fixture]"
    source = root / renderer.SOURCE
    source.parent.mkdir(parents=True)
    source.write_bytes(SAMPLE.encode("utf-8"))
    return root


def cli(root: Path, *options: str) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        [sys.executable, str(VALIDATOR), "--repo-root", str(root), *options],
        cwd=root.parent,
        check=False,
        capture_output=True,
        timeout=25,
    )


def test_frozen_node_renderer_bytes(checkout: Path) -> None:
    raw = renderer.render_compatibility_workflow(SAMPLE).encode("utf-8")
    assert hashlib.sha256(raw).hexdigest() == RAW_HASH
    assert hashlib.sha256(renderer.generated_workflow(checkout)).hexdigest() == FORMATTED_HASH


def test_current_canonical_workflow_matches_committed_bytes() -> None:
    assert renderer.generated_workflow(ROOT) == (ROOT / renderer.TARGET).read_bytes()


def test_drift_check_and_output_do_not_change_the_worktree(checkout: Path) -> None:
    target = checkout / renderer.TARGET
    target.write_bytes(b"retained bytes\n")
    check = cli(checkout, "--check")
    assert check.returncode == 1
    assert check.stdout == b""
    assert check.stderr.decode("utf-8").strip() == (
        f"Run node {renderer.LAUNCHER} to update {renderer.TARGET}."
    )
    result = cli(checkout, "--stdout")
    assert result.returncode == 0
    assert result.stderr == b""
    assert hashlib.sha256(result.stdout).hexdigest() == FORMATTED_HASH
    assert target.read_bytes() == b"retained bytes\n"


def test_write_then_check_reuses_identical_bytes(checkout: Path) -> None:
    target = checkout / renderer.TARGET
    assert not target.exists()
    missing = cli(checkout, "--check")
    assert missing.returncode == 1
    assert not target.exists()
    write = cli(checkout)
    assert (write.returncode, write.stdout, write.stderr) == (0, b"", b"")
    assert hashlib.sha256(target.read_bytes()).hexdigest() == FORMATTED_HASH
    assert cli(checkout, "--check").returncode == 0
    assert sorted(path.name for path in target.parent.iterdir()) == [target.name, "unity-tests.yml"]


@pytest.mark.parametrize(
    ("original", "mutation", "message"),
    [
        (
            "inputs.test-mode",
            "inputs.new-dispatch-switch",
            "Unclassified Unity dispatch input: new-dispatch-switch",
        ),
        (
            "  unity-tests-aggregate:",
            "  renamed-aggregate:",
            "Canonical Unity workflow is missing unity-tests-aggregate.",
        ),
        ("mode: [playmode]", "mode: [playmode", "SyntaxError"),
    ],
)
def test_render_and_formatter_failure_preserve_the_target(
    checkout: Path, original: str, mutation: str, message: str
) -> None:
    source = checkout / renderer.SOURCE
    source.write_bytes(SAMPLE.replace(original, mutation).encode("utf-8"))
    target = checkout / renderer.TARGET
    target.write_bytes(b"retained bytes\n")
    result = cli(checkout)
    assert result.returncode == 1
    assert result.stdout == b""
    assert message in result.stderr.decode("utf-8")
    assert target.read_bytes() == b"retained bytes\n"
    assert sorted(path.name for path in target.parent.iterdir()) == [target.name, source.name]


def test_node_module_forwards_both_existing_apis(checkout: Path) -> None:
    node = shutil.which("node")
    assert node is not None
    result = subprocess.run(
        [
            node,
            "-e",
            """
const fs = require("node:fs");
const api = require(process.argv[1]);
const source = fs.readFileSync(process.argv[2], "utf8");
api.generatedWorkflow(process.argv[3]).then(formatted => {
  process.stdout.write(JSON.stringify([
    api.target, api.renderCompatibilityWorkflow(source), formatted
  ]));
}).catch(error => { console.error(error.message); process.exitCode = 1; });
""",
            str(ROOT / renderer.LAUNCHER),
            str(checkout / renderer.SOURCE),
            str(checkout),
        ],
        cwd=checkout.parent,
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr.decode("utf-8")
    assert result.stderr == b""
    result_values: list[str] = json.loads(result.stdout)
    target, raw, formatted = result_values
    assert target == renderer.TARGET
    assert hashlib.sha256(raw.encode("utf-8")).hexdigest() == RAW_HASH
    assert hashlib.sha256(formatted.encode("utf-8")).hexdigest() == FORMATTED_HASH
