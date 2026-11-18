"""Check the native diagnostic selectors against their original executable decisions."""

from __future__ import annotations

import hashlib
import itertools
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TYPE_CHECKING

import pytest

if TYPE_CHECKING:
    from collections.abc import Iterator, Mapping

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
import native_build_selection as selection

ROOT = Path(__file__).resolve().parents[2]

FIELDS = (
    ("UH_NATIVE_DIAGNOSTICS", "true"),
    ("UH_NATIVE_MODE", "standalone"),
    ("UH_NATIVE_ACCEPTANCE", "none"),
    ("UH_NATIVE_DIRECT_CONTROL", "false"),
    ("UH_NATIVE_STANDARD_HEAP", "false"),
    ("UH_NATIVE_ATTACH_CONTROL", "true"),
    ("UH_NATIVE_ATTACH_AFTER_STARTUP", "true"),
    ("UH_NATIVE_ATTACH_AFTER_SHUTDOWN", "false"),
)
CHECKS = {
    "direct": selection.direct_error,
    "heap": selection.heap_error,
    "attach": selection.attach_error,
    "startup": selection.startup_error,
    "shutdown": selection.shutdown_error,
}


def environments(length: int) -> Iterator[dict[str, str]]:
    for mask in range(4**length):
        environment = {}
        for key, valid in FIELDS[:length]:
            opposite = "true" if valid == "false" else "false"
            candidate = (valid, None, "invalid", opposite)[mask % 4]
            if candidate is not None:
                environment[key] = candidate
            mask //= 4
        yield environment


# Original JS at c490233d. Each result is a compact JSON string or null followed by LF.
@pytest.mark.parametrize(
    ("name", "length", "accepted", "expected"),
    [
        ("direct", 3, 1, "58ecb1206e483b639a99c0b0670887fc0cc34f268af234cf42fbb7eb9e761d28"),
        ("heap", 4, 1, "4b28f5bc3b4afbc31da62b27fd7158c0e4cd0bd9a55bb69d86b0552d08d4cac1"),
        ("attach", 5, 1, "9f3e520ca94c818fd92a001d7fe23be29d84e5dea1d57331467b215950b3dc3d"),
        ("startup", 8, 2, "737cdfac6168d8b5d6ce3f9ed0f4c364343408feca5da06b0e0795ded1e756b2"),
        ("shutdown", 8, 1, "34a19baf2b81577525683164fc87d3daf7b64a0e2d76614cb5c1a16b51404d62"),
    ],
)
def test_matches_original_decisions_and_diagnostics(
    name: str, length: int, accepted: int, expected: str
) -> None:
    digest = hashlib.sha256()
    successes = 0
    for environment in environments(length):
        error = CHECKS[name](environment)
        successes += error is None
        digest.update((json.dumps(error) + "\n").encode("utf-8"))
    assert digest.hexdigest() == expected
    assert successes == accepted


@pytest.mark.parametrize(
    ("name", "field_policy", "length"),
    [
        ("direct", "DIRECT_FIELDS", 3),
        ("heap", "HEAP_FIELDS", 4),
        ("attach", "ATTACH_FIELDS", 5),
        ("startup", "STARTUP_FIELDS", 8),
        ("shutdown", "SHUTDOWN_FIELDS", 8),
    ],
)
def test_each_policy_field_has_a_failing_witness(
    monkeypatch: pytest.MonkeyPatch, name: str, field_policy: str, length: int
) -> None:
    original: tuple[tuple[str, str], ...] = getattr(selection, field_policy)
    baseline = dict(FIELDS[:length])
    if name == "shutdown":
        baseline.update(
            UH_NATIVE_ATTACH_AFTER_STARTUP="false", UH_NATIVE_ATTACH_AFTER_SHUTDOWN="true"
        )
    check = CHECKS[name]
    assert check(baseline) is None
    for index, (key, _) in enumerate(original):
        for invalid in ("", "bogus", None):
            environment = baseline.copy()
            if invalid is None:
                del environment[key]
            else:
                environment[key] = invalid
            assert check(environment) is not None
            with monkeypatch.context() as patch:
                patch.setattr(selection, field_policy, original[:index] + original[index + 1 :])
                assert check(environment) is None


@pytest.mark.parametrize("name", ["startup", "shutdown"])
def test_delayed_attachment_requires_the_base_arm(
    monkeypatch: pytest.MonkeyPatch, name: str
) -> None:
    environment = dict(FIELDS)
    if name == "shutdown":
        environment.update(
            UH_NATIVE_ATTACH_AFTER_STARTUP="false", UH_NATIVE_ATTACH_AFTER_SHUTDOWN="true"
        )
    environment["UH_NATIVE_MODE"] = "editmode"
    assert CHECKS[name](environment) is not None

    def admit(_environment: Mapping[str, str]) -> None:
        return None

    monkeypatch.setattr(selection, "attach_error", admit)
    assert CHECKS[name](environment) is None


# Frozen from the original JavaScript entry point with synthetic process streams.
def test_attachment_entry_point_precedence_matches_original() -> None:
    digest = hashlib.sha256()
    admitted = 0
    toggle = ("true", "false", "invalid", None)
    for mode, attach, startup, shutdown in itertools.product(
        ("standalone", "editmode"), toggle, toggle, toggle
    ):
        environment = dict(FIELDS[:5])
        environment["UH_NATIVE_MODE"] = mode
        for (key, _), value in zip(FIELDS[5:], (attach, startup, shutdown), strict=True):
            if value is not None:
                environment[key] = value
        error = selection.attachment_selection_error(environment)
        admitted += error is None
        status, stderr = (0, "") if error is None else (1, error + "\n")
        result = json.dumps([status, stderr], separators=(",", ":"))
        digest.update((result + "\n").encode("utf-8"))
    assert admitted == 19
    assert digest.hexdigest() == "15ad437ea7c00b7a9efdeaa4bc0704f3dec4b625b8021cb35ef9ac8562c2aff7"


@pytest.mark.parametrize("legacy", [False, True])
@pytest.mark.parametrize(
    ("mode", "overrides", "expected"),
    [
        ("direct", {}, None),
        ("direct", {"UH_NATIVE_MODE": "editmode"}, selection.direct_error({})),
        ("heap", {}, None),
        ("heap", {"UH_NATIVE_DIRECT_CONTROL": "true"}, selection.heap_error({})),
        ("attach", {}, None),
        (
            "attach",
            {"UH_NATIVE_ATTACH_AFTER_STARTUP": "invalid"},
            "Unknown attachment startup selection.",
        ),
        (
            "attach",
            {"UH_NATIVE_ATTACH_AFTER_SHUTDOWN": "invalid"},
            "Unknown attachment shutdown selection.",
        ),
        ("attach", {"UH_NATIVE_ATTACH_AFTER_SHUTDOWN": "true"}, selection.shutdown_error({})),
        (
            "attach",
            {"UH_NATIVE_ATTACH_AFTER_SHUTDOWN": "true", "UH_NATIVE_ATTACH_AFTER_STARTUP": "false"},
            None,
        ),
        (
            "attach",
            {"UH_NATIVE_ATTACH_AFTER_SHUTDOWN": "false", "UH_NATIVE_ATTACH_CONTROL": "false"},
            selection.startup_error({}),
        ),
    ],
)
def test_cli_retains_exit_status_and_streams_from_a_foreign_directory(
    tmp_path: Path, legacy: bool, mode: str, overrides: dict[str, str], expected: str | None
) -> None:
    if legacy:
        node = shutil.which("node")
        assert node is not None
        command = [node, str(ROOT / f"scripts/unity/validate-native-build-{mode}-selection.js")]
    else:
        command = [sys.executable, str(ROOT / "scripts/unity/native_build_selection.py"), mode]
    environment = {
        key: value for key, value in os.environ.items() if not key.startswith("UH_NATIVE_")
    }
    environment.update(dict(FIELDS), **overrides)
    result = subprocess.run(
        command,
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        timeout=20,
        check=False,
    )
    assert result.stdout == b""
    assert result.stderr == (b"" if expected is None else (expected + "\n").encode("utf-8"))
    assert result.returncode == (0 if expected is None else 1)
