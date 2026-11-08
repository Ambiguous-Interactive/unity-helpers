"""Exercise actual Git blobs, worktree bytes, literal scopes and compatibility launchers."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import check_eol
from check_eol import index_issues, inspect, main, relative_path, requires_lf, supported

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/check_eol.py"


def git(repository: Path, *arguments: str, payload: bytes | None = None) -> bytes:
    result = subprocess.run(
        [str(shutil.which("git")), "-C", str(repository), *arguments],
        input=payload,
        capture_output=True,
        check=True,
        timeout=20,
    )
    return result.stdout


@pytest.fixture
def repository(tmp_path: Path) -> Path:
    root = tmp_path / "repository with spaces café"
    root.mkdir()
    git(root, "init", "-q")
    git(root, "config", "core.autocrlf", "false")
    git(root, "config", "core.safecrlf", "false")
    (root / ".gitattributes").write_bytes(b"* text=auto eol=crlf\n")
    return root


def add(repository: Path, name: str, data: bytes, *, unfiltered: bool = False) -> None:
    target = repository / name
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    if unfiltered:
        digest = git(repository, "hash-object", "-w", "--stdin", payload=data).decode().strip()
        git(repository, "update-index", "--add", "--cacheinfo", "100644", digest, name)
    else:
        git(repository, "add", "--", name)


@pytest.mark.parametrize(
    ("name", "lf"),
    [
        ("code.cs", False),
        ("script.ps1", False),
        ("file.meta", False),
        ("README.md", True),
        ("file.yml", True),
        ("file.yaml", True),
        ("script.sh", True),
        (".github/actions/example.ps1", True),
        (".githooks/pre-commit", True),
        ("package.json", True),
        ("package-lock.json", True),
        ("_includes/test.html", True),
        ("nested/package.json", False),
        ("_includes/test.json", False),
        (".gitignore", False),
        (".GITIGNORE", False),
        ("UPPER.CS", False),
    ],
)
def test_policy(name: str, lf: bool) -> None:
    assert requires_lf(name) is lf
    assert supported(name) is (name != ".githooks/pre-commit")


@pytest.mark.parametrize("data", [b"", b"a", b"a\r\nb\r\n"])
def test_normalized_repository(repository: Path, data: bytes) -> None:
    add(repository, "scripts/normal.ps1", data)
    add(repository, "docs/normal.md", b"# Title\n\nBody\n")
    findings = inspect(repository)
    assert findings.subjects == 2
    assert not findings.failed


@pytest.mark.parametrize(
    ("name", "data", "kind"),
    [
        ("scripts/polluted.ps1", b"a\r\nb\r\n", "crlf"),
        ("workflow.yml", b"name: a\r\non: push\r\n", "crlf"),
        ("scripts/mixed.ps1", b"a\r\nb\nc\n", "mixed"),
    ],
)
def test_unnormalized_blobs(
    repository: Path,
    name: str,
    data: bytes,
    kind: str,
    capsys: pytest.CaptureFixture[str],
) -> None:
    add(repository, name, data, unfiltered=True)
    before = (repository / ".git/index").read_bytes()
    findings = inspect(repository)
    assert findings.blobs == [f"{name} (committed blob is {kind}; git stores text blobs as LF)"]
    assert main(["--repo-root", str(repository)]) == 3
    assert "git add --renormalize" in capsys.readouterr().out
    assert (repository / ".git/index").read_bytes() == before


@pytest.mark.parametrize("attribute", ["-text", "binary", ""])
def test_exempt_blobs(repository: Path, attribute: str) -> None:
    (repository / ".gitattributes").write_text(f"*.fixture {attribute}\n", encoding="utf-8")
    add(repository, "fixtures/crlf.fixture", b"a\r\nb\r\n", unfiltered=True)
    assert not inspect(repository, ["fixtures/crlf.fixture"]).failed


def test_all_index_blobs_are_checked_even_for_unscanned_extensions(
    repository: Path,
) -> None:
    add(repository, "normal.cs", b"a\r\n")
    add(repository, "sample.fixture", b"a\r\n", unfiltered=True)
    assert len(inspect(repository).blobs) == 1


def test_binary_payload_under_auto_text_is_exempt(repository: Path) -> None:
    add(repository, "normal.cs", b"a\r\n")
    add(repository, "binary.png", b"\x89PNG\0\r\n", unfiltered=True)
    assert not inspect(repository).failed


def test_literal_scopes_unicode_and_absolute_paths(repository: Path) -> None:
    name = "scripts/polluted [1] café.ps1"
    add(repository, name, b"a\r\n", unfiltered=True)
    add(repository, "clean.ps1", b"clean\r\n")
    assert len(inspect(repository, [name]).blobs) == 1
    assert len(inspect(repository, [str(repository / name)]).blobs) == 1
    assert not inspect(repository, ["./clean.ps1"]).failed
    assert relative_path(repository, "scripts\\polluted [1] café.ps1") == name


@pytest.mark.parametrize("argument_budget", [12000, 0])
def test_more_than_64_scoped_paths(
    repository: Path, monkeypatch: pytest.MonkeyPatch, argument_budget: int
) -> None:
    monkeypatch.setattr(check_eol, "MAX_SCOPE_ARGUMENT_BYTES", argument_budget)
    names = [f"scripts/chunk-{index:03}.ps1" for index in range(65)]
    for name in names:
        add(repository, name, b"clean\r\n")
    for name in (names[0], names[-1], "scripts/outside.ps1"):
        add(repository, name, b"dirty\r\n", unfiltered=True)
    findings = inspect(repository, names)
    assert len(findings.blobs) == 2
    assert findings.blobs[0].startswith(names[0])
    assert findings.blobs[1].startswith(names[-1])


@pytest.mark.parametrize(
    ("name", "data", "endings", "boms"),
    [
        ("scripts/example.ps1", b"a\nb\n", 1, 0),
        ("scripts/example.ps1", b"a\r\nb\n", 1, 0),
        ("docs/example.md", b"a\r\nb\r\n", 1, 0),
        ("docs/example.md", b"\xef\xbb\xbfa\n", 0, 1),
        ("scripts/example.ps1", b"\xef\xbb\xbfa\r\n", 0, 1),
    ],
)
def test_worktree_findings(
    repository: Path, name: str, data: bytes, endings: int, boms: int
) -> None:
    add(repository, name, data)
    findings = inspect(repository)
    assert len(findings.endings) == endings
    assert len(findings.boms) == boms
    assert not findings.blobs


def test_repo_github_attributes_preserve_png_and_lf_scripts(repository: Path) -> None:
    (repository / ".gitattributes").write_bytes((ROOT / ".gitattributes").read_bytes())
    name = ".github/review-artifacts/preview.png"
    image = (ROOT / "Tests/Editor/TestAssets/Textures/solid_1x1_black.png").read_bytes()
    add(repository, name, image)
    add(repository, ".github/actions/example.ps1", b"Write-Host 'example'\r\n")
    assert git(repository, "show", f":{name}") == image
    for selected in (name, ".github/actions/example.ps1"):
        (repository / selected).unlink()
        git(repository, "checkout-index", "--force", "--", selected)
    assert (repository / name).read_bytes() == image
    assert (repository / ".github/actions/example.ps1").read_bytes() == b"Write-Host 'example'\n"


@pytest.mark.parametrize("kind", ["missing", "directory", "link", "ancestor", "fifo"])
def test_unsafe_inputs_fail(repository: Path, kind: str) -> None:
    path = repository / "bad.cs"
    if kind == "directory":
        path.mkdir()
    elif kind in ("link", "ancestor"):
        destination = repository / "real"
        destination.mkdir()
        (destination / "source.cs").write_bytes(b"a\r\n")
        try:
            path.symlink_to(
                destination if kind == "ancestor" else destination / "source.cs",
                target_is_directory=kind == "ancestor",
            )
        except OSError:
            pytest.skip("Host cannot create symlinks")
        if kind == "ancestor":
            path /= "source.cs"
    elif kind == "fifo":
        maker = shutil.which("mkfifo")
        if maker is None:
            pytest.skip("A POSIX FIFO creator is unavailable")
        created = subprocess.run([maker, str(path)], capture_output=True, check=False, timeout=10)
        if created.returncode != 0 or not path.is_fifo():
            pytest.skip("The creator did not produce a FIFO visible to native Python")
    with pytest.raises(ValueError, match=r"linked|missing|regular"):
        inspect(repository, [str(path)])


def test_empty_corpus_and_outside_scope_fail(repository: Path) -> None:
    with pytest.raises(ValueError, match="No tracked text files"):
        inspect(repository)
    with pytest.raises(ValueError, match="outside"):
        inspect(repository, ["../outside.cs"])
    assert inspect(repository, ["ignored.png"]).subjects == 0


def test_deleted_tracked_file_retains_index_validation(repository: Path) -> None:
    add(repository, "deleted.cs", b"a\r\n", unfiltered=True)
    add(repository, "present.cs", b"a\r\n")
    (repository / "deleted.cs").unlink()
    assert len(inspect(repository).blobs) == 1
    with pytest.raises(ValueError, match="missing"):
        inspect(repository, ["deleted.cs"])


def test_git_errors_and_malformed_records_fail(tmp_path: Path) -> None:
    assert main(["--repo-root", str(tmp_path)]) == 1
    with pytest.raises(ValueError, match="Malformed"):
        index_issues(b"broken\0", None)
    with pytest.raises(ValueError, match="Missing Git"):
        index_issues(b"i/crlf w/crlf\tfile.cs\0", None)


def test_actual_cli_runs_from_foreign_directory(repository: Path, tmp_path: Path) -> None:
    add(repository, "script.ps1", b"a\n")
    result = subprocess.run(
        [sys.executable, str(SCRIPT), "--repo-root", str(repository)],
        cwd=tmp_path.parent,
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 3
    assert b"EOL issues: 1" in result.stdout


def test_compatibility_launcher_binds_multiple_paths(repository: Path) -> None:
    shell = shutil.which("pwsh")
    assert shell is not None, "PowerShell is required to qualify the retained launcher"
    add(repository, "first.cs", b"a\r\n")
    add(repository, "last.cs", b"b\n")
    scripts = repository / "scripts"
    scripts.mkdir()
    shutil.copy2(SCRIPT, scripts / SCRIPT.name)
    for name in ("cli_encoding.py", "csharp_sources.py", "check-eol.ps1"):
        shutil.copy2(ROOT / "scripts" / name, scripts / name)
    project = scripts / "python~"
    project.mkdir()
    for name in ("pyproject.toml", "uv.lock"):
        shutil.copy2(ROOT / "scripts/python~" / name, project / name)
    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-File",
            str(scripts / "check-eol.ps1"),
            "-Paths",
            "first.cs",
            "last.cs",
            "-VerboseOutput",
        ],
        cwd=repository.parent,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 3, result.stdout + result.stderr
    assert b"Inspected text files: 2" in result.stdout
    assert b"last.cs" in result.stdout


def test_legacy_test_launcher_runs_selected_control(tmp_path: Path) -> None:
    shell = shutil.which("pwsh")
    assert shell is not None
    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-check-eol.ps1"),
            "-TestFilter",
            "test_empty_corpus_and_outside_scope_fail",
        ],
        cwd=tmp_path,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert b"1 passed" in result.stdout


def test_concurrent_scan_preserves_all_findings_in_path_order(repository: Path) -> None:
    for index in range(32):
        add(repository, f"shared/deep/{index:02}.cs", b"\xef\xbb\xbfline\n")
    findings = inspect(repository)
    assert findings.subjects == 32
    assert len(findings.endings) == 32
    assert len(findings.boms) == 32
    assert findings.endings == sorted(findings.endings)
    assert findings.boms == sorted(findings.boms)


def test_concurrent_scan_does_not_skip_linked_inputs(repository: Path) -> None:
    for index in range(16):
        add(repository, f"shared/{index:02}.cs", b"line\r\n")
    target = repository / "shared/last.cs"
    try:
        target.symlink_to(repository / "shared/00.cs")
    except OSError:
        pytest.skip("Host cannot create symlinks")
    with pytest.raises(ValueError, match="linked"):
        inspect(repository, [*(f"shared/{index:02}.cs" for index in range(16)), "shared/last.cs"])
