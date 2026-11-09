"""Keep cspell authoritative and qualify both lint-code validator entry points."""

from __future__ import annotations

import json
import re
import shutil
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from validate_lint_error_codes import harvest, main, roundtrip, selected_files

ROOT = Path(__file__).resolve().parents[2]
UNKNOWN = "NOVELPREFIXFAMILYQ"


@pytest.fixture
def repository(tmp_path: Path) -> Path:
    root = tmp_path / "repository with spaces café"
    root.mkdir()
    scripts = root / "scripts"
    scripts.mkdir()
    (scripts / "tests").mkdir()
    (root / ".githooks").mkdir()
    (scripts / "lint-example.ps1").write_text("UNH001\n", encoding="utf-8")
    (root / "cspell.json").write_text("{}", encoding="utf-8")
    return root


def runner(repository: Path, output: str = "", status: int = 0) -> None:
    (repository / "scripts/run-node-bin.js").write_text(
        f"process.stdin.resume(); process.stdin.on('end', () => {{ process.stdout.write({json.dumps(output)}); process.exitCode = {status}; }});",
        encoding="utf-8",
    )


@pytest.mark.parametrize(
    "relative",
    [
        "scripts/lint-example.ps1",
        "scripts/Lint-Upper.PS1",
        "scripts/lint-example.js",
        "scripts/lint-example.mjs",
        "scripts/lint_example.py",
        "scripts/tests/test-lint-example.ps1",
        "scripts/tests/test-lint-example.js",
        "scripts/tests/test-lint-example.sh",
        "scripts/tests/test_lint_example.py",
        ".githooks/pre-commit",
        ".githooks/pre-commit.ps1",
    ],
)
def test_every_supported_surface_is_harvested(repository: Path, relative: str) -> None:
    path = repository / relative
    path.write_text(f"{UNKNOWN}001\n", encoding="utf-8")
    sources = harvest(repository, selected_files(repository))
    assert sources[UNKNOWN] == [relative + ":1"]


@pytest.mark.parametrize(
    "relative",
    [
        "scripts/other.py",
        "scripts/lint-example.ps1.meta",
        ".githooks/README.md",
        ".githooks/.gitattributes",
    ],
)
def test_non_subjects_are_excluded(repository: Path, relative: str) -> None:
    (repository / relative).write_text(f"{UNKNOWN}001", encoding="utf-8")
    assert UNKNOWN not in harvest(repository, selected_files(repository))


def test_upstream_codes_boundaries_literal_contents_and_location_cap(repository: Path) -> None:
    (repository / "scripts/lint-example.ps1").write_text(
        "SC2016 MD025 CS001 SA123\n" + "UNH001\n" * 7 + "THING1234\n// WPROTO001\n",
        encoding="utf-8",
    )
    sources = harvest(repository, selected_files(repository))
    assert set(sources) == {"UNH", "WPROTO"}
    assert len(sources["UNH"]) == 5
    assert sources["UNH"][0] == "scripts/lint-example.ps1:2"


@pytest.mark.parametrize("encoding", ["utf-8-sig", "utf-16", "utf-32"])
def test_bom_encodings_preserve_source_lines(repository: Path, encoding: str) -> None:
    (repository / "scripts/lint-example.ps1").write_bytes(f"\n{UNKNOWN}001\n".encode(encoding))
    assert harvest(repository, selected_files(repository))[UNKNOWN] == [
        "scripts/lint-example.ps1:2"
    ]


def test_cspell_success_is_a_real_subprocess(repository: Path) -> None:
    runner(repository)
    assert not roundtrip(repository, ["UNH"], "node")
    assert main(["--repo-root", str(repository), "--verbose"]) == 0


@pytest.mark.parametrize("verdict", ["Unknown", "Forbidden"])
def test_rejected_family_is_reported_with_sources_and_patch(
    repository: Path, verdict: str, capsys: pytest.CaptureFixture[str]
) -> None:
    (repository / "scripts/lint-example.ps1").write_text(f"{UNKNOWN}001", encoding="utf-8")
    runner(repository, f"<stdin>:1:1 - {verdict} word ({UNKNOWN})\n", 1)
    assert roundtrip(repository, [UNKNOWN], "node") == {UNKNOWN}
    assert main(["--repo-root", str(repository)]) == 1
    output = capsys.readouterr().out
    assert "scripts/lint-example.ps1:1" in output
    assert '"add_to_root_words"' in output
    assert UNKNOWN in output


@pytest.mark.parametrize("status", [1, 2, 127])
def test_tool_failures_cannot_pass(repository: Path, status: int) -> None:
    runner(repository, "tool failed", status)
    with pytest.raises(ValueError, match=r"not installed|without matching"):
        roundtrip(repository, ["UNH"], "node")
    assert main(["--repo-root", str(repository)]) == 1


def test_unrelated_parseable_verdict_cannot_mask_tool_failure(repository: Path) -> None:
    runner(repository, f"<stdin>:1:1 - Unknown word ({UNKNOWN})", 1)
    with pytest.raises(ValueError, match="without matching"):
        roundtrip(repository, ["UNH"], "node")


def test_missing_node_and_missing_cspell_inputs_fail(repository: Path) -> None:
    runner(repository)
    with pytest.raises(ValueError, match="Node command"):
        roundtrip(repository, ["UNH"], "missing-node-command-9137")
    (repository / "cspell.json").unlink()
    with pytest.raises(ValueError, match="missing"):
        roundtrip(repository, ["UNH"], "node")


def test_missing_roots_empty_corpus_and_no_tokens_are_distinct(
    repository: Path, tmp_path: Path
) -> None:
    with pytest.raises(ValueError, match="missing"):
        selected_files(tmp_path / "absent")
    (repository / "scripts/lint-example.ps1").unlink()
    with pytest.raises(ValueError, match="No lint"):
        selected_files(repository)
    (repository / "scripts/lint-example.ps1").write_text("no codes here", encoding="utf-8")
    assert main(["--repo-root", str(repository)]) == 0


@pytest.mark.parametrize("kind", ["file", "directory", "fifo"])
def test_linked_and_special_inputs_fail(repository: Path, kind: str) -> None:
    path = repository / "scripts/lint-linked.py"
    if kind == "fifo":
        maker = shutil.which("mkfifo")
        if maker is None:
            pytest.skip("A POSIX FIFO creator is unavailable")
        created = subprocess.run([maker, str(path)], capture_output=True, check=False, timeout=10)
        if created.returncode != 0 or not path.is_fifo():
            pytest.skip("The creator did not produce a FIFO visible to native Python")
    else:
        if kind == "directory":
            path = repository / "scripts/tests"
            path.rmdir()
        try:
            path.symlink_to(
                repository / "scripts" if kind == "directory" else repository / "cspell.json",
                target_is_directory=kind == "directory",
            )
        except OSError:
            pytest.skip("Host cannot create symlinks")
    with pytest.raises(ValueError, match=r"linked|regular"):
        selected_files(repository)


def test_actual_repository_cspell_accepts_registered_and_rejects_unknown() -> None:
    assert not roundtrip(ROOT, ["UNH", "PWS"], "node")
    assert roundtrip(ROOT, [UNKNOWN], "node") == {UNKNOWN}


def test_real_cspell_uses_the_selected_repositories_words_and_forbidden_words(
    repository: Path,
) -> None:
    metadata = json.loads((ROOT / "node_modules/cspell/package.json").read_text(encoding="utf-8"))
    relative = metadata["bin"] if isinstance(metadata["bin"], str) else metadata["bin"]["cspell"]
    binary = str(ROOT / "node_modules/cspell" / relative)
    (repository / "scripts/run-node-bin.js").write_text(
        "const {spawnSync} = require('node:child_process'); "
        "const result = spawnSync(process.execPath, ["
        + json.dumps(binary)
        + ", ...process.argv.slice(3)], {stdio: 'inherit'}); process.exit(result.status ?? 1);",
        encoding="utf-8",
    )
    configuration = {"version": "0.2", "language": "en", "words": [UNKNOWN], "flagWords": []}
    (repository / "cspell.json").write_text(json.dumps(configuration), encoding="utf-8")
    assert not roundtrip(repository, [UNKNOWN], "node")
    configuration["flagWords"] = [UNKNOWN + "001"]
    (repository / "cspell.json").write_text(json.dumps(configuration), encoding="utf-8")
    assert roundtrip(repository, [UNKNOWN], "node") == {UNKNOWN}


def test_concurrent_repositories_do_not_change_process_working_directory(
    repository: Path, tmp_path: Path
) -> None:
    other = tmp_path / "other"
    (other / "scripts").mkdir(parents=True)
    (other / "scripts/lint-example.py").write_text("WPROTO001", encoding="utf-8")
    before = Path.cwd()

    def collect(root: Path) -> dict[str, list[str]]:
        return harvest(root, selected_files(root))

    with ThreadPoolExecutor(max_workers=2) as workers:
        results = list(workers.map(collect, (repository, other)))
    assert set(results[0]) == {"UNH"}
    assert set(results[1]) == {"WPROTO"}
    assert Path.cwd() == before


def test_actual_cli_from_foreign_directory(repository: Path) -> None:
    runner(repository)
    result = subprocess.run(
        [
            sys.executable,
            str(ROOT / "scripts/validate_lint_error_codes.py"),
            "--repo-root",
            str(repository),
        ],
        cwd=repository.parent,
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert b"Scanned 1" in result.stdout


def test_compatibility_launcher(repository: Path) -> None:
    runner(repository)
    shell = shutil.which("pwsh")
    assert shell is not None
    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/validate-lint-error-codes.ps1"),
            "-RepoRoot",
            str(repository),
            "-VerboseOutput",
        ],
        cwd=repository.parent,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert b"Scanned 1" in result.stdout


def test_test_launcher_runs_one_control(tmp_path: Path) -> None:
    shell = shutil.which("pwsh")
    assert shell is not None
    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-validate-lint-error-codes.ps1"),
            "-TestFilter",
            "test_actual_repository_cspell_accepts_registered_and_rejects_unknown",
        ],
        cwd=tmp_path,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert b"1 passed" in result.stdout


def test_retained_powershell_runspace_controls(tmp_path: Path) -> None:
    shell = shutil.which("pwsh")
    assert shell is not None
    result = subprocess.run(
        [shell, "-NoProfile", "-File", str(ROOT / "scripts/tests/test-fixture-runspace.ps1")],
        cwd=tmp_path,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    summary = re.search(rb"Passed: ([1-9]\d*); Failed: 0\b", result.stdout)
    assert summary is not None, result.stdout
    assert int(summary[1]) >= 35, result.stdout


def test_hook_subdirectories_are_not_text_subjects(repository: Path) -> None:
    (repository / ".githooks/helpers").mkdir()
    assert set(harvest(repository, selected_files(repository))) == {"UNH"}
