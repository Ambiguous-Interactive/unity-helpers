"""Hold migrated compiler tooling to frozen entry points and real filesystem boundaries."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_csharp_format
import typecheck_clean

ROOT = Path(__file__).resolve().parents[2]
ORIGINAL = cast(
    "dict[str, str]",
    json.loads(
        (Path(__file__).parent / "fixtures/compiler-tooling-original.json").read_text(
            encoding="utf-8"
        )
    ),
)


def fixture_root(root: Path) -> None:
    (root / "scripts").mkdir(parents=True)
    for name in ("obj", "bin"):
        target = root / "Generator~" / "Project" / name
        target.mkdir(parents=True)
        (target / "owned.txt").write_text("owned")
    (root / "Generator~" / "keep.txt").write_text("keep")
    (root / "package.json").write_text(
        json.dumps(
            {"scripts": {"typecheck:unity": "node receive.cjs", "fixture": "node receive.cjs"}}
        )
    )
    (root / "receive.cjs").write_text(
        "require('node:fs').writeFileSync('received.json', JSON.stringify({"
        "args:process.argv.slice(2),shared:process.env.UseSharedCompilation,cwd:process.cwd()}));"
        "process.exit(Number(process.env.TOOLING_FIXTURE_EXIT || 0));"
    )


@pytest.mark.parametrize("arguments", [[], ["fixture", "--", "two words", "caf\u00e9"]])
@pytest.mark.parametrize("status", [0, 7])
def test_clean_runs_npm_without_shared_compilation(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, arguments: list[str], status: int
) -> None:
    root = tmp_path / "candidate"
    fixture_root(root)
    monkeypatch.setenv("TOOLING_FIXTURE_EXIT", str(status))
    assert typecheck_clean.run(arguments, root) == status
    received = json.loads((root / "received.json").read_text(encoding="utf-8"))
    assert received == {
        "args": arguments[2:] if arguments else [],
        "shared": "false",
        "cwd": str(root),
    }
    assert not (root / "Generator~/Project/obj").exists()
    assert not (root / "Generator~/Project/bin").exists()
    assert (root / "Generator~/keep.txt").read_text(encoding="utf-8") == "keep"
    if os.name != "nt":
        baseline = tmp_path / "baseline"
        fixture_root(baseline)
        source = baseline / "scripts/typecheck-clean.js"
        source.write_text(ORIGINAL["typecheck-clean.js"])
        node = shutil.which("node")
        assert node is not None
        old = subprocess.run([node, str(source), *arguments], cwd=tmp_path, check=False, timeout=30)
        assert old.returncode == status
        observed = json.loads((baseline / "received.json").read_text(encoding="utf-8"))
        observed["cwd"] = str(root)
        assert observed == received
        assert not (baseline / "Generator~/Project/obj").exists()
        assert not (baseline / "Generator~/Project/bin").exists()


def test_missing_generator_fails_without_launch(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    def fail() -> list[str]:
        pytest.fail("Missing generator launched npm")

    monkeypatch.setattr(typecheck_clean, "npm_command", fail)
    assert typecheck_clean.run([], tmp_path) == 2


@pytest.mark.parametrize("kind", ["project", "artifact", "generator"])
def test_clean_does_not_follow_directory_links(tmp_path: Path, kind: str) -> None:
    root = tmp_path / "root"
    outside = tmp_path / "outside"
    (outside / "Project/bin").mkdir(parents=True)
    retained = outside / "Project/bin/value.txt"
    retained.write_text("retained")
    root.mkdir()
    try:
        if kind == "generator":
            (root / "Generator~").symlink_to(outside, target_is_directory=True)
        else:
            (root / "Generator~/Project").mkdir(parents=True)
            link = root / "Generator~" / ("Linked" if kind == "project" else "Project/bin")
            target = outside / ("Project" if kind == "project" else "Project/bin")
            link.symlink_to(target, target_is_directory=True)
    except OSError as error:
        pytest.skip(f"Directory-link creation is unavailable: {error}")
    if kind == "generator":
        with pytest.raises(ValueError, match="symbolic link"):
            typecheck_clean.clean_artifacts(root)
    else:
        assert typecheck_clean.clean_artifacts(root) == (1 if kind == "artifact" else 0)
    assert retained.read_text(encoding="utf-8") == "retained"


@pytest.mark.skipif(os.name != "nt", reason="Windows junction control")
@pytest.mark.parametrize("kind", ["generator", "project", "artifact"])
def test_clean_preserves_windows_junction_targets(tmp_path: Path, kind: str) -> None:
    root = tmp_path / "root"
    outside = tmp_path / "outside"
    (outside / "Project/bin").mkdir(parents=True)
    retained = outside / "Project/bin/value.txt"
    retained.write_text("retained")
    root.mkdir()
    if kind == "generator":
        link = root / "Generator~"
        target = outside
    else:
        (root / "Generator~/Project").mkdir(parents=True)
        link = root / "Generator~" / ("Linked" if kind == "project" else "Project/bin")
        target = outside / ("Project" if kind == "project" else "Project/bin")
    command = shutil.which("cmd.exe")
    assert command is not None
    result = subprocess.run(
        [command, "/d", "/c", "mklink", "/J", str(link), str(target)],
        check=False,
        capture_output=True,
        timeout=15,
    )
    assert result.returncode == 0, result.stderr
    if kind == "generator":
        with pytest.raises(ValueError, match="symbolic link"):
            typecheck_clean.clean_artifacts(root)
    else:
        assert typecheck_clean.clean_artifacts(root) == 0
    assert retained.read_text(encoding="utf-8") == "retained"


def test_unavailable_npm_preserves_build_outputs(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    fixture_root(tmp_path)

    def unavailable() -> list[str]:
        raise FileNotFoundError("npm unavailable")

    monkeypatch.setattr(typecheck_clean, "npm_command", unavailable)
    assert typecheck_clean.run([], tmp_path) == 1
    assert (tmp_path / "Generator~/Project/bin/owned.txt").read_text(encoding="utf-8") == "owned"


@pytest.mark.parametrize("skip", [False, True])
def test_format_gate_announces_missing_manifest(
    tmp_path: Path, capsys: pytest.CaptureFixture[str], skip: bool
) -> None:
    assert lint_csharp_format.run(["--skip-when-unavailable"] if skip else [], tmp_path) == (
        0 if skip else 1
    )
    message = capsys.readouterr().out
    assert "no .NET tool manifest" in message
    assert ("Skipped:" in message) == skip


@pytest.mark.parametrize("fix", [False, True])
@pytest.mark.parametrize("existing", [False, True])
def test_format_gate_matches_the_frozen_powershell(
    tmp_path: Path, fix: bool, existing: bool
) -> None:
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required to execute the frozen formatter control")
    outcomes: list[tuple[int, bytes | None]] = []
    for name in ("baseline", "candidate"):
        root = tmp_path / name
        (root / "scripts").mkdir(parents=True)
        (root / ".config").mkdir()
        shutil.copy2(ROOT / ".config/dotnet-tools.json", root / ".config/dotnet-tools.json")
        target = root / "File.cs"
        if existing:
            target.write_text("namespace Example { class Thing{public int Value;} }\n")
        if name == "baseline":
            source = root / "scripts/lint-csharp-format.ps1"
            source.write_text(ORIGINAL["lint-csharp-format.ps1"])
            result = subprocess.run(
                [
                    pwsh,
                    "-NoProfile",
                    "-File",
                    str(source),
                    *(["-Fix"] if fix else []),
                    "-Paths",
                    "File.cs",
                ],
                cwd=tmp_path,
                check=False,
                capture_output=True,
                text=True,
                timeout=90,
            )
            status = result.returncode
        else:
            status = lint_csharp_format.run(
                [*(["--fix"] if fix else []), "--paths", "File.cs"], root
            )
        outcomes.append((status, target.read_bytes() if target.exists() else None))
    assert outcomes[0] == outcomes[1]
    assert outcomes[1][0] == (0 if fix or not existing else 1)


@pytest.mark.parametrize("restore", [True, False])
@pytest.mark.parametrize("status", [1, 42, -15])
def test_format_gate_propagates_failures(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, restore: bool, status: int
) -> None:
    (tmp_path / ".config").mkdir()
    (tmp_path / ".config/dotnet-tools.json").write_text("{}")

    def dotnet_path(_command: str) -> str:
        return "dotnet"

    monkeypatch.setattr(shutil, "which", dotnet_path)
    calls: list[list[str]] = []

    def execute(command: list[str], **kwargs: object) -> subprocess.CompletedProcess[str]:
        calls.append(command)
        assert kwargs["cwd"] == tmp_path
        assert kwargs["timeout"] in (300, 600)
        return subprocess.CompletedProcess(
            command, status if restore or len(calls) == 2 else 0, "failure"
        )

    monkeypatch.setattr(subprocess, "run", execute)
    assert lint_csharp_format.run([], tmp_path) == (1 if restore or status < 0 else status)
    assert calls[0] == ["dotnet", "tool", "restore"]
    if not restore:
        assert calls[1] == ["dotnet", "tool", "run", "csharpier", "check", "."]
