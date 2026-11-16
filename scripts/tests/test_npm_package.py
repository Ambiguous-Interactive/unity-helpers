"""Replay the original npm package decisions without changing shared source files."""

from __future__ import annotations

import io
import json
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import validate_npm_package as validator
from check_runner import mapping, strings
from typecheck_clean import npm_command

ROOT = Path(__file__).resolve().parents[2]
CASES = [
    mapping(row)
    for row in cast(
        "list[object]",
        json.loads(
            (ROOT / "scripts/tests/fixtures/npm-package-v1.json").read_text(encoding="utf-8")
        ),
    )
]


def fixture(case: dict[str, object], root: Path) -> None:
    for name, content in mapping(case["files"]).items():
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(str(content).encode())
    git = shutil.which("git")
    assert git is not None
    subprocess.run([git, "init", "--quiet"], cwd=root, capture_output=True, check=True, timeout=30)
    untracked = strings(case["untracked"])
    subprocess.run(
        [git, "add", "--", *[name for name in mapping(case["files"]) if name not in untracked]],
        cwd=root,
        capture_output=True,
        check=True,
        timeout=30,
    )
    for name in strings(case["removed_after_tracking"]):
        (root / name).unlink()


@pytest.mark.parametrize("case", CASES, ids=[str(case["name"]) for case in CASES])
def test_original_package_decisions(
    case: dict[str, object], tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    fixture(case, tmp_path)
    before = {
        path.relative_to(tmp_path).as_posix(): path.read_bytes()
        for path in tmp_path.rglob("*")
        if path.is_file() and ".git" not in path.parts
    }
    assert validator.validate(tmp_path) == case["exit"]
    output = capsys.readouterr().out
    errors = [line.split("✗ ", 1)[1] for line in output.splitlines() if "✗ " in line]
    assert errors == case["errors"]
    after = {
        path.relative_to(tmp_path).as_posix(): path.read_bytes()
        for path in tmp_path.rglob("*")
        if path.is_file() and ".git" not in path.parts
    }
    assert before == after


def archive(path: Path, entries: list[tuple[str, bytes]]) -> None:
    with tarfile.open(path, "w:gz") as stream:
        for name, kind in entries:
            member = tarfile.TarInfo(name)
            member.type = kind
            if kind == tarfile.REGTYPE:
                member.size = 1
                stream.addfile(member, io.BytesIO(b"x"))
            else:
                member.linkname = "../../outside"
                stream.addfile(member)


@pytest.mark.parametrize(
    "name",
    [
        "/outside",
        "package/../outside",
        "package/./file",
        "package/C:/outside",
        "package/a\\outside",
        "other/file",
    ],
)
def test_archive_refuses_nonlocal_paths(tmp_path: Path, name: str) -> None:
    path = tmp_path / "bad.tgz"
    archive(path, [(name, tarfile.REGTYPE)])
    with pytest.raises(ValueError, match="Unsafe npm archive entry"):
        validator.archive_inventory(path)
    assert list(tmp_path.iterdir()) == [path]


@pytest.mark.parametrize(
    "kind", [tarfile.SYMTYPE, tarfile.LNKTYPE, tarfile.FIFOTYPE, tarfile.CHRTYPE]
)
def test_archive_refuses_links_and_special_entries(tmp_path: Path, kind: bytes) -> None:
    path = tmp_path / "bad.tgz"
    archive(path, [("package/file", kind)])
    with pytest.raises(ValueError, match="Unsafe npm archive entry"):
        validator.archive_inventory(path)


@pytest.mark.parametrize(
    "names",
    [
        ["package/file", "package/file"],
        ["package/file", "package/file/child"],
        ["package/file/child", "package/file"],
    ],
)
def test_archive_refuses_duplicate_and_conflicting_entries(
    tmp_path: Path, names: list[str]
) -> None:
    path = tmp_path / "bad.tgz"
    archive(path, [(name, tarfile.REGTYPE) for name in names])
    with pytest.raises(ValueError, match="Duplicate npm archive entry"):
        validator.archive_inventory(path)


@pytest.mark.parametrize("directory_first", [True, False])
def test_archive_derives_directories_in_both_orders(tmp_path: Path, directory_first: bool) -> None:
    path = tmp_path / "valid.tgz"
    entries = [("package/Runtime/", tarfile.DIRTYPE), ("package/Runtime/file", tarfile.REGTYPE)]
    archive(path, entries if directory_first else entries[::-1])
    assert validator.archive_inventory(path) == ({"Runtime/file"}, {"Runtime", "Runtime/file"})


def test_zero_archive_subjects_fail(tmp_path: Path) -> None:
    path = tmp_path / "empty.tgz"
    archive(path, [])
    with pytest.raises(ValueError, match="Package directory not found"):
        validator.archive_inventory(path)


@pytest.mark.parametrize("suffix", [".DLL", ".pdb", ".tmp", ".log", ".rsp"])
def test_original_expected_exclusions_remain_case_insensitive(suffix: str) -> None:
    assert validator.expected_exclusion("Runtime/missing" + suffix)
    assert not validator.expected_exclusion("Runtime/missing" + suffix + ".cs")


def test_real_package_validation() -> None:
    assert validator.validate(ROOT) == 0


def assert_signature(raw: str) -> None:
    package = mapping(cast("object", json.loads(raw)))
    assert isinstance(package.get("signature"), str), "signature must be a string"
    assert str(package["signature"]).casefold() == "unsigned", "signature must be unsigned"
    assert len(re.findall(r'"signature"\s*:', raw)) == 1, "signature must occur once"


def test_signature_current_package() -> None:
    assert_signature((ROOT / "package.json").read_text(encoding="utf-8"))


@pytest.mark.parametrize(
    "raw",
    [
        "{}",
        '{"signature":null}',
        '{"signature":1}',
        '{"signature":"signed"}',
        '{"signature":"unsigned","signature":"unsigned"}',
        "{broken",
    ],
)
def test_signature_rejects_each_original_violation(raw: str) -> None:
    with pytest.raises((AssertionError, ValueError)):
        assert_signature(raw)


def assert_surface(raw: str, changelog: str) -> None:
    package = mapping(cast("object", json.loads(raw)))
    url, version = package.get("changelogUrl"), package.get("version")
    assert isinstance(url, str), "changelogUrl must be a string"
    assert url.strip(), "changelogUrl must be non-empty"
    assert "raw.githubusercontent.com" not in url.casefold(), "changelogUrl must render markup"
    assert isinstance(version, str), "version must be a string"
    assert version.strip(), "version must be non-empty"
    assert len(re.findall(r'"changelogUrl"\s*:', raw)) == 1, "changelogUrl must occur once"
    upm = mapping(package.get("_upm"))
    assert isinstance(upm.get("changelog"), str), "UPM notes must be a string"
    assert str(upm["changelog"]).strip(), "UPM notes must be non-empty"
    assert re.search(r"^#\s+", changelog), "changelog must start with a heading"
    assert re.search(
        rf"(?mi)^##\s+\[(?:{re.escape(version)}|Unreleased)\](?:\s+-.*)?\s*$", changelog
    ), "changelog must include a version heading"


def test_surface_current_package() -> None:
    from release_tools import sync_upm

    assert (ROOT / "CHANGELOG.md.meta").is_file()
    assert_surface(
        (ROOT / "package.json").read_text(encoding="utf-8"),
        (ROOT / "CHANGELOG.md").read_text(encoding="utf-8"),
    )
    assert sync_upm(ROOT, check=True) == 0
    result = subprocess.run(
        [*npm_command(), "pack", "--dry-run", "--json"],
        cwd=ROOT,
        capture_output=True,
        check=True,
        timeout=60,
    )
    packed = cast("list[dict[str, object]]", json.loads(result.stdout))
    paths = {str(mapping(item)["path"]) for item in cast("list[object]", packed[0]["files"])}
    assert {"CHANGELOG.md", "CHANGELOG.md.meta"} <= paths


@pytest.mark.parametrize(
    "fault",
    [
        "missing-url",
        "empty-url",
        "raw-url",
        "empty-version",
        "missing-upm",
        "empty-upm",
        "duplicate-url",
        "missing-heading",
        "missing-version-heading",
    ],
)
def test_surface_rejects_each_original_violation(fault: str) -> None:
    package: dict[str, object] = {
        "version": "1.2.3",
        "changelogUrl": "https://example.com/notes",
        "_upm": {"changelog": "- Notes."},
    }
    changelog = "# Changelog\n\n## [1.2.3]\n\n- Notes.\n"
    if fault == "missing-url":
        del package["changelogUrl"]
    elif fault == "empty-url":
        package["changelogUrl"] = " "
    elif fault == "raw-url":
        package["changelogUrl"] = "https://raw.githubusercontent.com/org/repo"
    elif fault == "empty-version":
        package["version"] = " "
    elif fault == "missing-upm":
        del package["_upm"]
    elif fault == "empty-upm":
        package["_upm"] = {"changelog": " "}
    elif fault == "missing-heading":
        changelog = "text\n" + changelog
    elif fault == "missing-version-heading":
        changelog = "# Changelog\n"
    raw = json.dumps(package)
    if fault == "duplicate-url":
        raw = raw[:-1] + ',"changelogUrl":"https://example.com/notes"}'
    with pytest.raises((AssertionError, ValueError)):
        assert_surface(raw, changelog)


def test_package_contract_registry_parity() -> None:
    policy = mapping(
        cast(
            "object",
            json.loads(
                (ROOT / "scripts/tests/fixtures/npm-package-policy-v1.json").read_text(
                    encoding="utf-8"
                )
            ),
        )
    )
    actual = {
        "FORBIDDEN": validator.FORBIDDEN,
        "ALLOWED_TOP_LEVEL": validator.ALLOWED_TOP_LEVEL,
        "REQUIRED": validator.REQUIRED,
        "CONTENT_ROOTS": validator.CONTENT_ROOTS,
        "UNITY_FOLDERS": validator.UNITY_FOLDERS,
        "SCRIPT_FILES": validator.SCRIPT_FILES,
    }
    assert actual == {key: tuple(strings(value)) for key, value in policy.items()}
    package = mapping(cast("object", json.loads((ROOT / "package.json").read_bytes())))
    shipped = strings(package["files"])
    top = {path.split("/", 1)[0] for path in shipped}
    assert set(validator.ALLOWED_TOP_LEVEL) == top | {"package.json"}
    assert not (set(shipped) & set(validator.FORBIDDEN))
    assert not any(validator.forbidden_artifact(path) for path in shipped)
    assert all("/" not in path and "\\" not in path for path in validator.ALLOWED_TOP_LEVEL)
    assert set(validator.REQUIRED) - {"package.json"} <= set(shipped)
    assert set(validator.REQUIRED) <= set(validator.CONTENT_ROOTS)
    assert set(validator.UNITY_FOLDERS) <= set(validator.REQUIRED)


@pytest.mark.parametrize(
    ("name", "fragment"),
    [
        ("Runtime/.hidden", "Missing .meta file for file"),
        ("scripts/.hidden", "Unexpected script"),
        ("docs/.Hidden.cs", "C# source outside Unity package roots"),
        ("Runtime/ENTRY.txt", "File in npm package but not tracked"),
        ("Pr-Description.MD.bak", "Forbidden release artifact"),
    ],
)
def test_package_contract_hidden_case_and_artifact_guards(name: str, fragment: str) -> None:
    files = set(mapping(CASES[0]["files"]))
    tracked = set(files)
    files.add(name)
    paths = set(files)
    for path in files:
        paths.update(parent.as_posix() for parent in Path(path).parents if parent.parts)
    errors = validator.validation_errors(files, paths, tracked)
    assert any(fragment in error and name in error for error in errors)
    if validator.forbidden_artifact(name):
        assert len([error for error in errors if name in error]) == 1


@pytest.mark.parametrize("folder", validator.UNITY_FOLDERS)
def test_package_contract_unity_roots_must_be_directories(folder: str) -> None:
    files = {name for name in mapping(CASES[0]["files"]) if not name.startswith(folder + "/")}
    files.add(folder)
    paths = set(files)
    for path in files:
        paths.update(parent.as_posix() for parent in Path(path).parents if parent.parts)
    errors = validator.validation_errors(files, paths, set(files))
    assert f"Required Unity root is not a directory: {folder}" in errors


@pytest.mark.parametrize(
    "launcher",
    [
        "scripts/tests/test-npm-package-signature.ps1",
        "scripts/tests/test-npm-package-changelog.ps1",
    ],
)
def test_legacy_package_test_launchers(launcher: str, tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    if executable is None:
        pytest.skip("PowerShell is not installed on this host")
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / launcher),
            "-TestFilter",
            "test_package_contract_registry_parity",
        ],
        cwd=tmp_path,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stderr
    assert b"1 passed" in result.stdout


def test_legacy_package_validator(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    if executable is None:
        pytest.skip("PowerShell is not installed on this host")
    fixture(CASES[0], tmp_path)
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/validate-npm-package.ps1"),
            "-VerboseOutput",
        ],
        cwd=tmp_path,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stderr
    assert b"NPM package validation passed!" in result.stdout
    assert b"npm pack produced" in result.stdout
    assert b'"filename"' not in result.stdout


@pytest.mark.parametrize("index", [0, 1])
def test_actual_python_cli_passes_and_rejects_payload(tmp_path: Path, index: int) -> None:
    case = CASES[index]
    fixture(case, tmp_path)
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/validate_npm_package.py"), "--verbose"],
        cwd=tmp_path,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == case["exit"], result.stderr
    assert b"npm pack produced" in result.stdout
    assert b'"filename"' not in result.stdout
    if case["exit"]:
        assert b"File in npm package but not tracked in git repo: docs/new.md" in result.stdout
    else:
        assert b"NPM package validation passed!" in result.stdout


@pytest.mark.parametrize("outcome", ["failure", "missing", "multiple", "malformed"])
def test_pack_failure_and_owned_cleanup(
    tmp_path: Path,
    outcome: str,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    root = tmp_path / "repository"
    root.mkdir()
    fixture(CASES[0], root)
    scratch = tmp_path / "scratch"
    scratch.mkdir()
    monkeypatch.setattr(tempfile, "tempdir", str(scratch))
    child = tmp_path / "pack.py"
    child.write_text(
        "import pathlib,sys\n"
        "destination=pathlib.Path(sys.argv[sys.argv.index('--pack-destination')+1])\n"
        + {
            "failure": "print('pack stdout',flush=True)\nprint('pack stderr',file=sys.stderr,flush=True)\nsys.exit(7)\n",
            "missing": "sys.exit(0)\n",
            "multiple": "(destination/'a.tgz').write_bytes(b'')\n(destination/'b.tgz').write_bytes(b'')\n",
            "malformed": "(destination/'a.tgz').write_bytes(b'bad archive')\n",
        }[outcome],
        encoding="utf-8",
    )
    monkeypatch.setattr(validator, "npm_command", lambda: [sys.executable, str(child)])
    if outcome == "failure":
        assert validator.validate(root) == 7
        output = capsys.readouterr().out
        assert "pack stdout" in output
        assert "pack stderr" in output
    elif outcome == "malformed":
        assert validator.main(["--repo-root", str(root)]) == 1
        assert "not a gzip file" in capsys.readouterr().err
    else:
        with pytest.raises(ValueError, match="Expected one npm tarball"):
            validator.validate(root)
    assert list(scratch.iterdir()) == []


def test_owned_temporary_root_accepts_a_canonical_parent_alias(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / "repository"
    root.mkdir()
    fixture(CASES[0], root)
    physical = tmp_path / "physical-temp"
    physical.mkdir()
    alias = tmp_path / "temp-alias"
    try:
        alias.symlink_to(physical, target_is_directory=True)
    except OSError:
        pytest.skip("Creating a directory alias requires host permission")
    monkeypatch.setattr(tempfile, "tempdir", str(alias))
    assert validator.validate(root) == 0
    assert list(physical.iterdir()) == []
