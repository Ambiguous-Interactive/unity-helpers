"""Exercise metadata coverage, C# shapes, exclusions and read-only CLI boundaries."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_meta_files as lint

VALID = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
STUB = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n"


def git(root: Path, *arguments: str) -> bytes:
    executable = shutil.which("git")
    assert executable is not None
    return subprocess.run(
        [executable, "-C", str(root), *arguments], capture_output=True, check=True, timeout=20
    ).stdout


@pytest.fixture
def workspace(tmp_path: Path) -> Path:
    git(tmp_path, "init", "-q")
    (tmp_path / "Runtime").mkdir()
    (tmp_path / "Runtime.meta").write_text(STUB, encoding="utf-8")
    return tmp_path


def add_asset(root: Path, name: str = "Runtime/Example.cs", meta: str | None = VALID) -> Path:
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    source_root = root / name.split("/")[0]
    if source_root.name != "Samples~":
        Path(f"{source_root}.meta").write_text(STUB, encoding="utf-8")
    path.write_text("class Example {}\n", encoding="utf-8")
    if meta is not None:
        Path(f"{path}.meta").write_text(meta, encoding="utf-8")
    git(root, "add", "--all")
    return path


@pytest.mark.parametrize(
    "content",
    [
        VALID,
        VALID.replace("executionOrder: 0", "executionOrder: -42"),
        VALID.replace("userData:", "userData: 'one\ttwo'"),
        VALID.replace("userData:", "userData: |\n    \tone"),
        VALID.replace(
            "defaultReferences: []",
            "defaultReferences:\n  - service: {fileID: 123, guid: 11111111111111111111111111111111, type: 3}",
        ),
        "\ufeff" + VALID,
        VALID.replace("\n", "\r\n"),
        VALID.replace("0123456789abcdef0123456789abcdef", "ABCDEF0123456789ABCDEF0123456789"),
    ],
)
def test_valid_customized_importers(tmp_path: Path, content: str) -> None:
    meta = tmp_path / "Example.cs.meta"
    meta.write_bytes(content.encode("utf-8"))
    assert lint.csharp_meta_error(meta) is None


@pytest.mark.parametrize(
    ("content", "field"),
    [
        (STUB, "MonoImporter"),
        ("", "fileFormatVersion"),
        (VALID.replace("fileFormatVersion: 2\n", ""), "fileFormatVersion"),
        (VALID.replace("fileFormatVersion: 2", "fileFormatVersion: 3"), "fileFormatVersion"),
        ("fileFormatVersion: 2\n" + VALID, "fileFormatVersion"),
        ("fileFormatVersion : bad\n" + VALID, "fileFormatVersion"),
        (VALID.replace("guid: 0123456789abcdef0123456789abcdef\n", ""), "guid"),
        (VALID.replace("0123456789abcdef0123456789abcdef", "garbage"), "guid"),
        (VALID + "guid: 0123456789abcdef0123456789abcdef\n", "guid"),
        (VALID.replace("guid:", "guid :"), "guid"),
        (VALID.replace("MonoImporter:", "MonoImporter: {}"), "MonoImporter"),
        (VALID + "MonoImporter:\n  userData:\n", "MonoImporter"),
        (STUB + "MonoImporter:\n", "mapping"),
        (STUB + "MonoImporter:\n  # no settings\n", "mapping"),
        (STUB + "MonoImporter:\n  - invalid\n", "mapping"),
        (STUB + "MonoImporter:\n  userData:value\n", "mapping"),
        (STUB + "MonoImporter:\n\tuserData:\n", "mapping"),
        (VALID + "DefaultImporter:\n  userData:\n", "only MonoImporter"),
    ],
)
def test_rejects_malformed_shapes(tmp_path: Path, content: str, field: str) -> None:
    meta = tmp_path / "Example.cs.meta"
    meta.write_text(content, encoding="utf-8")
    error = lint.csharp_meta_error(meta)
    assert error is not None
    assert field in error


@pytest.mark.parametrize("payload", [b"\xff", b"x" * (lint.MAX_META_BYTES + 1)])
def test_bounded_decoding(tmp_path: Path, payload: bytes) -> None:
    meta = tmp_path / "Example.cs.meta"
    meta.write_bytes(payload)
    assert lint.csharp_meta_error(meta) is not None


def test_missing_and_unreadable_meta(tmp_path: Path) -> None:
    assert lint.csharp_meta_error(tmp_path / "missing") is not None
    assert lint.csharp_meta_error(tmp_path) is not None


@pytest.mark.parametrize("name", sorted(lint.EXCLUDED_DIRECTORIES))
@pytest.mark.parametrize("prefix", ["Runtime/", "Runtime/Deep/"])
def test_directory_exclusions(name: str, prefix: str) -> None:
    assert lint.excluded(f"{prefix}{name}/Example.cs")
    assert lint.excluded(f"{prefix}{name}", directory=True)
    assert not lint.excluded(f"{prefix}{name}Suffix/Example.cs")


@pytest.mark.parametrize(
    "name",
    [
        "package-lock.json",
        "Gemfile.lock",
        "example.tmp",
        ".gitkeep",
        ".DS_Store",
        "Thumbs.db",
        "sample.pyc",
        "sample.pyo",
        "sample.swp",
        "sample.swo",
        "asset.meta",
    ],
)
def test_file_exclusions(name: str) -> None:
    assert lint.excluded(f"Runtime/{name}")


@pytest.mark.parametrize(
    "path",
    ["scripts/tools~/Example.cs", "Samples~/tools~/Example.cs", "Runtime/Generator~/Example.cs"],
)
def test_tilde_tooling_excluded(path: str) -> None:
    assert lint.excluded(path)


def test_samples_children_keep_meta_requirement() -> None:
    assert lint.excluded("Samples~", directory=True)
    assert not lint.excluded("Samples~/Demo/Example.cs")
    assert not lint.excluded("Runtime/Folder", directory=True)


@pytest.mark.parametrize("mode", ["full", "source", "meta"])
@pytest.mark.parametrize(("content", "expected"), [(VALID, 0), (STUB, 1), (None, 1)])
def test_both_gates(
    workspace: Path,
    mode: str,
    content: str | None,
    expected: int,
    capsys: pytest.CaptureFixture[str],
) -> None:
    add_asset(workspace, meta=content)
    result = (
        lint.check(workspace)
        if mode == "full"
        else lint.check_shapes(
            workspace, ["Runtime/Example.cs" + (".meta" if mode == "meta" else "")]
        )
    )
    assert result == expected
    output = capsys.readouterr().out
    if expected:
        assert "Runtime/Example.cs" in output


@pytest.mark.parametrize("kind", ["missing_file", "missing_directory", "orphan"])
def test_coverage_failures(workspace: Path, kind: str, capsys: pytest.CaptureFixture[str]) -> None:
    if kind == "missing_file":
        add_asset(workspace, "Runtime/data.txt", None)
    elif kind == "missing_directory":
        add_asset(workspace, "Runtime/Nested/Example.cs")
    else:
        (workspace / "Runtime/Absent.txt.meta").write_text(STUB, encoding="utf-8")
        git(workspace, "add", "--all")
    assert lint.check(workspace) == 1
    assert ("UNH006" if kind == "orphan" else "UNH005") in capsys.readouterr().out


@pytest.mark.parametrize(
    "name",
    [
        "Runtime/node_modules/Example.cs",
        "Runtime/.pytest_cache/Example.cs",
        "scripts/hidden~/Example.cs",
        "Runtime/example.pyc",
    ],
)
def test_excluded_tracked_files_and_orphans(workspace: Path, name: str) -> None:
    asset = add_asset(workspace, name, STUB)
    asset.unlink()
    git(workspace, "add", "--all")
    assert lint.check(workspace) == 0


def test_samples_import_semantics(workspace: Path) -> None:
    add_asset(workspace, "Samples~/Example.cs", STUB)
    assert lint.check(workspace) == 1
    (workspace / "Samples~/Example.cs.meta").write_text(VALID, encoding="utf-8")
    assert lint.check(workspace) == 0


def test_empty_scan_is_not_a_pass(tmp_path: Path) -> None:
    git(tmp_path, "init", "-q")
    assert lint.check(tmp_path) == 1


def test_untracked_sources_do_not_change_full_inventory(workspace: Path) -> None:
    add_asset(workspace)
    (workspace / "Runtime/Untracked.cs").write_text("bad", encoding="utf-8")
    assert lint.check(workspace) == 0
    assert lint.check_shapes(workspace, ["Runtime/Untracked.cs"]) == 1


def test_check_mode_preserves_bytes_and_index(workspace: Path) -> None:
    add_asset(workspace, meta=STUB)
    index = workspace / ".git/index"
    before = index.read_bytes()
    meta = workspace / "Runtime/Example.cs.meta"
    content = meta.read_bytes()
    assert lint.check(workspace) == 1
    assert lint.check_shapes(workspace, ["Runtime/Example.cs.meta"]) == 1
    assert index.read_bytes() == before
    assert meta.read_bytes() == content


@pytest.mark.parametrize(
    ("input_text", "expected"),
    [
        ('["Runtime/Example.cs.meta"]', 1),
        ('["Runtime/Example.cs"]', 1),
        ("[]", 2),
        ("null", 2),
        ("[12]", 2),
        ('"Runtime/Example.cs"', 2),
        ("invalid", 1),
    ],
)
def test_stdin_cli(workspace: Path, input_text: str, expected: int) -> None:
    add_asset(workspace, meta=STUB)
    result = subprocess.run(
        [sys.executable, str(Path(lint.__file__)), "--root", str(workspace), "--stdin-paths"],
        input=input_text,
        text=True,
        capture_output=True,
        check=False,
        timeout=20,
    )
    assert result.returncode == expected, result.stdout + result.stderr


def test_annotation_escapes_paths(capsys: pytest.CaptureFixture[str]) -> None:
    lint.annotation("a,b:c\n.cs", "UNH007", "bad\r\nvalue%")
    output = capsys.readouterr().out
    assert output == "::error file=a%2Cb%3Ac%0A.cs::UNH007: bad%0D%0Avalue%25\n"


def test_full_cli_from_other_directory(workspace: Path, tmp_path: Path) -> None:
    add_asset(workspace)
    result = subprocess.run(
        [sys.executable, str(Path(lint.__file__)), "--root", str(workspace), "--verbose"],
        cwd=tmp_path.parent,
        text=True,
        capture_output=True,
        check=False,
        timeout=20,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "Checked" in result.stdout


@pytest.mark.parametrize(("content", "expected"), [(VALID, 0), (STUB, 1)])
def test_powershell_launcher(workspace: Path, content: str, expected: int) -> None:
    add_asset(workspace, meta=content)
    scripts = workspace / "scripts"
    scripts.mkdir()
    project = scripts / "python~"
    project.mkdir()
    repository = Path(__file__).resolve().parents[2]
    for name in ("lint-meta-files.ps1", "lint_meta_files.py"):
        shutil.copyfile(repository / "scripts" / name, scripts / name)
    for name in ("pyproject.toml", "uv.lock"):
        shutil.copyfile(repository / "scripts/python~" / name, project / name)
    executable = shutil.which("pwsh")
    assert executable is not None
    result = subprocess.run(
        [executable, "-NoProfile", "-File", str(scripts / "lint-meta-files.ps1"), "-VerboseOutput"],
        cwd=workspace.parent,
        text=True,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == expected, result.stdout + result.stderr
    if expected:
        assert "Example.cs.meta" in result.stdout


def test_accepts_exact_metadata_size_limit(tmp_path: Path) -> None:
    meta = tmp_path / "Example.cs.meta"
    prefix = VALID.encode("utf-8") + b"#"
    meta.write_bytes(prefix + b"a" * (lint.MAX_META_BYTES - len(prefix)))
    assert lint.csharp_meta_error(meta) is None


@pytest.mark.parametrize("root_name", ["runtime", "RUNTIME", "samples~", "SAMPLES~"])
def test_shape_paths_match_preflight_root_casing(workspace: Path, root_name: str) -> None:
    relative = f"{root_name}/Example.cs"
    add_asset(workspace, relative, STUB)
    assert not lint.excluded(relative)
    assert lint.check_shapes(workspace, [relative + ".meta"]) == 1
    Path(f"{workspace / relative}.meta").write_text(VALID, encoding="utf-8")
    assert lint.check_shapes(workspace, [relative + ".meta"]) == 0
