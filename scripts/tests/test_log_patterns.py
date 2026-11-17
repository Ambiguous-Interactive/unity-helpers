"""Replay native log observations and preserve warning, protocol and status boundaries."""

from __future__ import annotations

import base64
import hashlib
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TYPE_CHECKING, cast

import pytest

if TYPE_CHECKING:
    from collections.abc import Iterator

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
import native_report_summary as scanner

REPOSITORY = Path(__file__).resolve().parents[2]
FIXTURE = cast(
    "dict[str, object]",
    json.loads((Path(__file__).parent / "fixtures/log-patterns-v1.json").read_bytes()),
)
CASES = cast("list[dict[str, str]]", FIXTURE["cases"])


def pwsh(
    script: Path, *arguments: str, cwd: Path | None = None
) -> subprocess.CompletedProcess[str]:
    executable = shutil.which("pwsh")
    assert executable is not None
    return subprocess.run(
        [executable, "-NoProfile", "-File", str(script), *arguments],
        cwd=cwd,
        capture_output=True,
        text=True,
        timeout=90,
    )


@pytest.fixture(scope="module")
def observed(
    tmp_path_factory: pytest.TempPathFactory,
) -> tuple[dict[str, object], dict[str, object]]:
    root = tmp_path_factory.mktemp("native-log-oracle")
    original = root / "original.ps1"
    original.write_bytes(str(FIXTURE["original_source"]).encode())
    assert hashlib.sha256(original.read_bytes()).hexdigest() == FIXTURE["original_source_sha256"]
    driver = root / "capture.ps1"
    driver.write_text(str(FIXTURE["capture_driver"]), encoding="utf-8")
    executable = shutil.which("pwsh")
    assert executable is not None
    results: list[dict[str, object]] = []
    for index, helper in enumerate(
        (original, REPOSITORY / "scripts/unity/lib/catastrophic-patterns.ps1")
    ):
        folder = root / str(index)
        folder.mkdir()
        result = subprocess.run(
            [
                executable,
                "-NoProfile",
                "-File",
                str(driver),
                "-Helper",
                str(helper),
                "-Root",
                str(folder),
            ],
            input=json.dumps(CASES),
            capture_output=True,
            text=True,
            timeout=90,
        )
        assert result.returncode == 0, result.stderr
        results.append(cast("dict[str, object]", json.loads(result.stdout)))
    return results[0], results[1]


@pytest.mark.parametrize("group", ["catastrophic", "environment", "observations"])
def test_actual_original_host_profile(
    group: str, observed: tuple[dict[str, object], dict[str, object]]
) -> None:
    before, after = observed
    assert after[group] == before[group]


@pytest.mark.parametrize("index", range(len(CASES)), ids=[case["name"] for case in CASES])
def test_python_scanner_matches_original_observation(index: int, tmp_path: Path) -> None:
    case = CASES[index]
    path = tmp_path / (case["name"] + ".log")
    if case["kind"] == "file":
        path.write_bytes(base64.b64decode(case["bytes"]))
    elif case["kind"] == "directory":
        path.mkdir()
    observations = cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", FIXTURE["linux_observations"])["observations"],
    )
    assert (
        scanner.summarize("  " if case["name"] == "blank" else str(path))
        == observations[index]["value"]
    )


def test_partial_read_failure_retains_delivered_observations(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    path = tmp_path / "partial.log"
    path.touch()

    def interrupted(_path: Path) -> Iterator[str]:
        yield "Native Crash Reporting\n"
        yield "Got a UNKNOWN while executing native code\n"
        raise OSError("Owned interrupted-read fixture")

    monkeypatch.setattr(scanner, "read_lines", interrupted)
    value = scanner.summarize(str(path))
    assert value["State"] == "unreadable-log"
    assert value["NativeReportHeaderCount"] == value["UnknownNativeConditionCount"] == 1
    assert value["NativeReportHeaderLines"] == [1]
    assert value["UnknownNativeConditionLines"] == [2]


@pytest.mark.skipif(os.name == "nt", reason="POSIX file permission fixture")
def test_unreadable_log_is_reported(tmp_path: Path) -> None:
    path = tmp_path / "denied.log"
    path.write_text("Native Crash Reporting", encoding="utf-8")
    path.chmod(0)
    try:
        assert scanner.summarize(str(path))["State"] == "unreadable-log"
    finally:
        path.chmod(0o600)


@pytest.mark.parametrize(
    "response",
    [
        '{"State":"observed"}',
        '{"State":"observed","NativeReportHeaderCount":-1}',
        "[]",
        "invalid-json",
    ],
)
def test_native_protocol_failures_cannot_report_observation(tmp_path: Path, response: str) -> None:
    path = tmp_path / "owned.log"
    path.write_text("Native Crash Reporting", encoding="utf-8")
    script = tmp_path / "protocol.ps1"
    script.write_text(
        "param([string]$Helper,[string]$Log,[string]$Response)\n$ErrorActionPreference='Stop'\n. $Helper\nfunction uv { $global:LASTEXITCODE=0; $Response }\n$global:LASTEXITCODE=41\n$value=Get-NativeReportSummary -LogPath $Log\n@{value=$value; exitCode=$global:LASTEXITCODE} | ConvertTo-Json -Depth 5 -Compress\n",
        encoding="utf-8",
    )
    result = pwsh(
        script,
        "-Helper",
        str(REPOSITORY / "scripts/unity/lib/catastrophic-patterns.ps1"),
        "-Log",
        str(path),
        "-Response",
        response,
    )
    assert result.returncode == 0, result.stderr
    # Warning stream belongs to diagnostics, separately from the final JSON receipt.
    receipt = cast("dict[str, object]", json.loads(result.stdout.splitlines()[-1]))
    value = cast("dict[str, object]", receipt["value"])
    assert value["State"] == "unreadable-log"
    assert value["NativeReportHeaderCount"] == 0
    assert receipt["exitCode"] == 41


@pytest.mark.parametrize(
    ("driver", "receipt"),
    [
        (
            "scripts/tests/test-catastrophic-pattern-sync.ps1",
            "Catastrophic-pattern single source OK: 11 patterns, 3 consumers",
        ),
        (
            "scripts/tests/test-unity-environment-warnings.ps1",
            "Unity environment warnings: 45 passed, 0 failed.",
        ),
    ],
)
def test_existing_native_consumers(driver: str, receipt: str) -> None:
    result = pwsh(REPOSITORY / driver)
    assert result.returncode == 0, result.stdout + result.stderr
    assert receipt in result.stdout


@pytest.mark.parametrize("present", [False, True])
def test_native_success_preserves_exit_status(tmp_path: Path, present: bool) -> None:
    path = tmp_path / "owned.log"
    path.write_text("Native Crash Reporting", encoding="utf-8")
    setup = (
        "$global:LASTEXITCODE=41"
        if present
        else "Remove-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue"
    )
    script = tmp_path / "status.ps1"
    script.write_text(
        "param([string]$Helper,[string]$Log)\n$ErrorActionPreference='Stop'\n. $Helper\n"
        + setup
        + "\n$value=Get-NativeReportSummary -LogPath $Log\n$variable=Get-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue\n@{value=$value; present=($null -ne $variable); exitCode=$(if ($null -ne $variable) {$variable.Value})} | ConvertTo-Json -Depth 5 -Compress\n",
        encoding="utf-8",
    )
    result = pwsh(
        script,
        "-Helper",
        str(REPOSITORY / "scripts/unity/lib/catastrophic-patterns.ps1"),
        "-Log",
        str(path),
    )
    assert result.returncode == 0, result.stderr
    receipt = cast("dict[str, object]", json.loads(result.stdout))
    assert receipt["present"] == present
    assert receipt["exitCode"] == (41 if present else None)
    assert cast("dict[str, object]", receipt["value"])["NativeReportHeaderCount"] == 1


def test_catalogues_remain_available_without_python_and_isolate_mutation(tmp_path: Path) -> None:
    script = tmp_path / "catalogue.ps1"
    script.write_text(
        "param([string]$Helper)\n$ErrorActionPreference='Stop'\n. $Helper\nfunction uv { throw 'Python bootstrap intentionally unavailable.' }\n$first=@(Get-CatastrophicPatterns); $first[0].Label='changed'\n$second=@(Get-CatastrophicPatterns)\n@{ label=$second[0].Label; catastrophic=$second.Count; environment=@(Get-EnvironmentWarningPatterns).Count } | ConvertTo-Json -Compress\n",
        encoding="utf-8",
    )
    result = pwsh(
        script, "-Helper", str(REPOSITORY / "scripts/unity/lib/catastrophic-patterns.ps1")
    )
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) == {
        "label": "PrecompiledAssemblyException",
        "catastrophic": 11,
        "environment": 3,
    }


def test_relative_option_filename_survives_native_cli_boundary(tmp_path: Path) -> None:
    path = tmp_path / "--owned.log"
    path.write_text("Native Crash Reporting", encoding="utf-8")
    script = tmp_path / "relative.ps1"
    script.write_text(
        "param([string]$Helper,[string]$Folder)\n$ErrorActionPreference='Stop'\n. $Helper\nSet-Location -LiteralPath $Folder\nGet-NativeReportSummary -LogPath '--owned.log' | ConvertTo-Json -Compress\n",
        encoding="utf-8",
    )
    result = pwsh(
        script,
        "-Helper",
        str(REPOSITORY / "scripts/unity/lib/catastrophic-patterns.ps1"),
        "-Folder",
        str(tmp_path),
        cwd=tmp_path,
    )
    assert result.returncode == 0, result.stderr
    assert cast("dict[str, object]", json.loads(result.stdout))["NativeReportHeaderCount"] == 1


@pytest.mark.skipif(os.name != "nt", reason="Windows native file-sharing contract")
def test_owned_active_writer_matches_original_file_sharing(tmp_path: Path) -> None:
    original = tmp_path / "original.ps1"
    original.write_bytes(str(FIXTURE["original_source"]).encode())
    script = tmp_path / "sharing.ps1"
    script.write_text(
        "param([string]$Original,[string]$Current,[string]$Log)\n$ErrorActionPreference='Stop'\n[IO.File]::WriteAllText($Log, 'Native Crash Reporting')\n$writer=[IO.File]::Open($Log, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite)\ntry {\n. $Original\n$before=Get-NativeReportSummary -LogPath $Log\n. $Current\n$after=Get-NativeReportSummary -LogPath $Log\n@{before=$before; after=$after} | ConvertTo-Json -Depth 6 -Compress\n} finally { $writer.Dispose() }\n",
        encoding="utf-8",
    )
    result = pwsh(
        script,
        "-Original",
        str(original),
        "-Current",
        str(REPOSITORY / "scripts/unity/lib/catastrophic-patterns.ps1"),
        "-Log",
        str(tmp_path / "owned.log"),
    )
    assert result.returncode == 0, result.stderr
    receipt = cast("dict[str, object]", json.loads(result.stdout.splitlines()[-1]))
    assert cast("dict[str, object]", receipt["before"])["State"] == "unreadable-log"
    assert receipt["after"] == receipt["before"]


@pytest.mark.parametrize("failure", [False, True])
def test_native_read_handle_closes_after_child_completion(tmp_path: Path, failure: bool) -> None:
    path = tmp_path / "owned.log"
    path.write_text("Native Crash Reporting", encoding="utf-8")
    script = tmp_path / "released.ps1"
    fixture = "function uv { throw 'Owned scanner launch failure.' }" if failure else ""
    script.write_text(
        "param([string]$Helper,[string]$Log)\n$ErrorActionPreference='Stop'\n. $Helper\n"
        + fixture
        + "\n$value=Get-NativeReportSummary -LogPath $Log\n$exclusive=[IO.File]::Open($Log, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)\ntry { @{state=$value.State; readable=$exclusive.CanRead} | ConvertTo-Json -Compress } finally { $exclusive.Dispose() }\n",
        encoding="utf-8",
    )
    result = pwsh(
        script,
        "-Helper",
        str(REPOSITORY / "scripts/unity/lib/catastrophic-patterns.ps1"),
        "-Log",
        str(path),
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert json.loads(result.stdout.splitlines()[-1]) == {
        "state": "unreadable-log" if failure else "observed",
        "readable": True,
    }
