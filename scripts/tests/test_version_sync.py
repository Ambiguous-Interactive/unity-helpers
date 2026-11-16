"""Replay original version synchronization files, output and index effects."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import version_sync as sync
from check_runner import mapping

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = mapping(
    cast(
        "object", json.loads((Path(__file__).parent / "fixtures/version-sync-v1.json").read_bytes())
    )
)
CASES = cast("list[dict[str, object]]", FIXTURE["cases"])
assert len(CASES) == 40
assert len(cast("list[object]", FIXTURE["checks"])) == 10
REJECTED_LEGACY = {
    ("banner", "numeric-version"),
    ("banner", "lf-version"),
    ("banner", "dollar-version"),
    ("templates", "manifest-scalar"),
    ("templates", "manifest-leading-zero"),
    ("templates", "manifest-overflow"),
    ("templates", "manifest-unicode"),
    ("templates", "same-line"),
}


@pytest.fixture(autouse=True)
def isolated_git_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    for key in list(os.environ):
        if key.startswith("GIT_"):
            monkeypatch.delenv(key, raising=False)


def case_id(case: dict[str, object]) -> str:
    return f"{case['kind']}:{case['name']}"


def environment() -> dict[str, str]:
    return {key: value for key, value in os.environ.items() if not key.startswith("GIT_")}


def prepare(root: Path, files: dict[str, str | None], git: bool) -> dict[str, str]:
    env = environment()
    for name, content in files.items():
        if content is not None:
            target = root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(content.encode("utf-8"))
    if git:
        executable = shutil.which("git")
        assert executable is not None
        subprocess.run(
            [executable, "init", "--quiet", str(root)], env=env, check=True, capture_output=True
        )
    return env


def snapshot(root: Path, files: dict[str, str | None]) -> dict[str, str | None]:
    return {
        name: (root / name).read_bytes().decode("utf-8") if (root / name).is_file() else None
        for name in files
    }


def normalized(data: str, root: Path) -> str:
    return (
        re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", data)
        .replace(str(root), "<ROOT>")
        .replace("\\", "/")
        .replace("\r\n", "\n")
    )


@pytest.mark.parametrize("case", CASES, ids=case_id)
def test_original_cli_files_and_index_effects(case: dict[str, object], tmp_path: Path) -> None:
    files = cast("dict[str, str | None]", case["files"])
    env = prepare(tmp_path, files, case["git"] is True)
    command = [
        sys.executable,
        str(ROOT / "scripts/version_sync.py"),
        str(case["kind"]),
        "--repo-root",
        str(tmp_path),
    ]
    if case["add_version"] is True:
        command.append("--add-package-version")
    result = subprocess.run(
        command, cwd=tmp_path, env=env, check=False, capture_output=True, timeout=60
    )
    rejected = (str(case["kind"]), str(case["name"])) in REJECTED_LEGACY
    assert result.returncode == (1 if rejected else case["exit"]), result.stdout + result.stderr
    assert snapshot(tmp_path, files) == (files if rejected else case["after"])
    if case["git"] is True:
        git = shutil.which("git")
        assert git is not None
        staged = subprocess.check_output(
            [git, "diff", "--cached", "--name-only", "-z"], cwd=tmp_path, env=env
        ).decode("utf-8")
        assert [path for path in staged.split("\0") if path] == ([] if rejected else case["staged"])
    expected = cast("str", case["stdout"])
    if (
        result.returncode == 0
        and case["git"] is True
        and "WARNING:" not in normalized(expected, tmp_path)
    ):
        assert normalized(result.stdout.decode("utf-8"), tmp_path) == normalized(expected, tmp_path)


@pytest.mark.parametrize(
    "name",
    ["comment-only", "unrelated-context", "empty-version", "numeric-version", "prefix-match"],
)
def test_banner_validator_rejects_original_false_positives(name: str, tmp_path: Path) -> None:
    cases = cast("list[dict[str, object]]", FIXTURE["checks"])
    case = next(case for case in cases if case["name"] == name)
    assert case["exit"] == 0
    files = cast("dict[str, str | None]", case["files"])
    env = prepare(tmp_path, files, False)
    result = subprocess.run(
        [
            sys.executable,
            str(ROOT / "scripts/version_sync.py"),
            "check-banner",
            "--repo-root",
            str(tmp_path),
        ],
        cwd=tmp_path,
        env=env,
        check=False,
        capture_output=True,
        timeout=30,
    )
    assert result.returncode == 1
    assert snapshot(tmp_path, files) == files


@pytest.mark.parametrize(
    "name", ["valid", "missing-banner", "missing-context", "wrong-banner", "wrong-context"]
)
def test_other_original_banner_checks_remain_read_only(name: str, tmp_path: Path) -> None:
    cases = cast("list[dict[str, object]]", FIXTURE["checks"])
    case = next(case for case in cases if case["name"] == name)
    files = cast("dict[str, str | None]", case["files"])
    env = prepare(tmp_path, files, False)
    result = subprocess.run(
        [
            sys.executable,
            str(ROOT / "scripts/version_sync.py"),
            "check-banner",
            "--repo-root",
            str(tmp_path),
        ],
        cwd=tmp_path,
        env=env,
        check=False,
        capture_output=True,
        timeout=30,
    )
    assert result.returncode == case["exit"]
    assert snapshot(tmp_path, files) == files
    if result.returncode == 0:
        assert result.stdout.decode("utf-8") == case["stdout"]


def selected_case(kind: str, name: str = "rewrite") -> dict[str, object]:
    return next(case for case in CASES if case["kind"] == kind and case["name"] == name)


@pytest.mark.parametrize("target", ["docs/images/unity-helpers-banner.svg", ".llm/context.md"])
def test_banner_final_lf(target: str, tmp_path: Path) -> None:
    case = selected_case("banner", "trailing-whitespace")
    prepare(tmp_path, cast("dict[str, str | None]", case["files"]), True)
    assert sync.synchronize(tmp_path, "banner") == 0
    content = (tmp_path / target).read_bytes()
    assert content.endswith(b"\n")
    assert not content.endswith((b"\n\n", b" \n", b"\t\n"))
    assert not content.startswith(b"\xef\xbb\xbf")


def test_current_manifest_and_both_template_dropdowns() -> None:
    version = mapping(cast("object", json.loads((ROOT / "package.json").read_bytes())))["version"]
    raw = mapping(
        cast("object", json.loads((ROOT / ".github/issue-template-versions.json").read_bytes()))
    )["versions"]
    assert isinstance(raw, list)
    versions = cast("list[str]", raw)
    assert versions
    assert len(versions) == len(set(versions))
    assert versions == sorted(versions, key=sync.version_key, reverse=True)
    assert version in versions
    for name in ("bug_report.yml", "feature_request.yml"):
        content = (ROOT / ".github/ISSUE_TEMPLATE" / name).read_text(encoding="utf-8")
        block = re.search(
            re.escape(sync.START) + r"(.*?)" + re.escape(sync.END), content, re.DOTALL
        )
        assert block is not None
        assert (
            re.findall(r'^\s*- "([0-9]+\.[0-9]+\.[0-9]+)"\s*$', block[1], re.MULTILINE) == versions
        )
        assert block[1].count('- "Other"') == 1


def test_second_sync_is_noop_without_staging_or_publication(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    case = selected_case("templates")
    files = cast("dict[str, str | None]", case["files"])
    prepare(tmp_path, files, True)
    assert sync.synchronize(tmp_path, "templates") == 0
    before = snapshot(tmp_path, files)

    def refuse_publish(path: Path, content: str) -> None:
        raise AssertionError((path, content))

    def refuse_stage(changes: list[sync.Change], info: sync.GitInfo, pwsh: str) -> None:
        raise AssertionError((changes, info, pwsh))

    monkeypatch.setattr(sync, "publish", refuse_publish)
    monkeypatch.setattr(sync, "stage_files", refuse_stage)
    assert sync.synchronize(tmp_path, "templates") == 0
    assert snapshot(tmp_path, files) == before


def test_generated_changes_stage_once_as_one_owned_batch(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    case = selected_case("banner")
    prepare(tmp_path, cast("dict[str, str | None]", case["files"]), True)
    calls: list[list[Path]] = []

    def stage(changes: list[sync.Change], info: sync.GitInfo, pwsh: str) -> None:
        assert info.cwd == tmp_path
        assert Path(pwsh).is_file()
        calls.append([change.path for change in changes])

    monkeypatch.setattr(sync, "stage_files", stage)
    assert sync.synchronize(tmp_path, "banner") == 0
    assert calls == [
        [tmp_path / "docs/images/unity-helpers-banner.svg", tmp_path / ".llm/context.md"]
    ]
    assert (tmp_path / "authored.txt").read_bytes() == b"Unrelated authored data.\n"


def test_hardlinked_authored_alias_remains_unchanged(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    case = selected_case("banner")
    prepare(root, cast("dict[str, str | None]", case["files"]), True)
    selected = root / ".llm/context.md"
    alias = tmp_path / "authored.md"
    alias.hardlink_to(selected)
    before = alias.read_bytes()
    assert sync.synchronize(root, "banner") == 0
    assert alias.read_bytes() == before
    assert selected.read_bytes() != before


def test_readonly_later_destination_refused_before_any_publication(tmp_path: Path) -> None:
    case = selected_case("banner")
    files = cast("dict[str, str | None]", case["files"])
    prepare(tmp_path, files, True)
    path = tmp_path / ".llm/context.md"
    mode = path.stat().st_mode
    path.chmod(0o444)
    try:
        with pytest.raises((ValueError, OSError), match=r"read-only|non-writable|Permission"):
            sync.synchronize(tmp_path, "banner")
        assert snapshot(tmp_path, files) == files
    finally:
        path.chmod(mode)


def test_symlink_to_authored_file_refused_before_any_publication(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    case = selected_case("banner")
    files = cast("dict[str, str | None]", case["files"])
    prepare(root, files, True)
    selected = root / ".llm/context.md"
    outside = tmp_path / "authored.md"
    selected.replace(outside)
    try:
        selected.symlink_to(outside)
    except OSError as error:
        pytest.skip(f"This host cannot create the controlled file symlink: {error}")
    before = outside.read_bytes()
    with pytest.raises(ValueError, match=r"redirect|symbolic|symlink"):
        sync.synchronize(root, "banner")
    assert outside.read_bytes() == before
    assert snapshot(root, files) == files


def test_missing_index_coordinator_refused_before_publication(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    case = selected_case("banner")
    files = cast("dict[str, str | None]", case["files"])
    prepare(tmp_path, files, True)
    original = shutil.which

    def which(command: str) -> str | None:
        return None if command == "pwsh" else original(command)

    monkeypatch.setattr(shutil, "which", which)
    with pytest.raises(ValueError, match="PowerShell is required"):
        sync.synchronize(tmp_path, "banner")
    assert snapshot(tmp_path, files) == files


def test_alternate_index_preserves_default_index_lock_and_unrelated_staging(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    case = selected_case("banner")
    prepare(tmp_path, cast("dict[str, str | None]", case["files"]), True)
    alternate = tmp_path / ".git/partial-index"
    main_lock = tmp_path / ".git/index.lock"
    main_lock.write_bytes(b"owned by another operation")
    monkeypatch.setenv("GIT_INDEX_FILE", str(alternate))
    info = sync.git_info(tmp_path, tmp_path, required=True)
    pwsh = shutil.which("pwsh")
    assert info is not None
    assert info.lock == Path(str(alternate) + ".lock")
    assert pwsh is not None
    sync.stage_files([sync.Change(tmp_path / "authored.txt", "")], info, pwsh)
    assert sync.synchronize(tmp_path, "banner") == 0
    git = shutil.which("git")
    assert git is not None
    staged = (
        subprocess.check_output([git, "diff", "--cached", "--name-only", "-z"], cwd=tmp_path)
        .decode()
        .rstrip("\0")
        .split("\0")
    )
    assert staged == [".llm/context.md", "authored.txt", "docs/images/unity-helpers-banner.svg"]
    assert main_lock.read_bytes() == b"owned by another operation"
    assert not (tmp_path / ".git/index").exists()


def test_held_active_lock_is_not_deleted_and_staging_failure_propagates(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    case = selected_case("banner")
    prepare(tmp_path, cast("dict[str, str | None]", case["files"]), True)
    lock = tmp_path / ".git/index.lock"
    lock.write_bytes(b"owned lock")
    for name in (
        "GIT_LOCK_MAX_ATTEMPTS",
        "GIT_LOCK_INITIAL_WAIT_MS",
        "GIT_LOCK_WAIT_TIMEOUT_MS",
        "GIT_LOCK_POLL_INTERVAL_MS",
    ):
        monkeypatch.setenv(name, "1")
    with pytest.raises(sync.StageFailure, match="exit code 128") as caught:
        sync.synchronize(tmp_path, "banner")
    assert caught.value.status == 128
    assert lock.read_bytes() == b"owned lock"
    assert not (tmp_path / ".git/index").exists()


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        ("", 50),
        ("0", 50),
        ("-1", 50),
        ("bad", 50),
        ("2147483648", 50),
        ("1_0", 50),
        ("+25", 25),
        (" 12 ", 12),
    ],
)
def test_positive_environment(value: str, expected: int, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("GIT_LOCK_POLL_INTERVAL_MS", value)
    assert sync.positive_environment("GIT_LOCK_POLL_INTERVAL_MS", 50) == expected


@pytest.mark.parametrize("kind", ["banner", "templates"])
def test_legacy_cli_repo_root_and_literal_paths(kind: str, tmp_path: Path) -> None:
    root = tmp_path / "project [literal] $x"
    case = selected_case(kind)
    files = cast("dict[str, str | None]", case["files"])
    env = prepare(root, files, True)
    executable = shutil.which("pwsh")
    assert executable is not None
    launcher = "sync-banner-version.ps1" if kind == "banner" else "sync-issue-template-versions.ps1"
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts" / launcher),
            "-RepoRoot",
            str(root),
        ],
        cwd=root,
        env=env,
        check=False,
        capture_output=True,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert snapshot(root, files) == case["after"]


def test_legacy_test_launcher_reaches_selected_control(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    assert executable is not None
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-sync-issue-template-versions.ps1"),
            "-TestFilter",
            "test_positive_environment",
        ],
        cwd=tmp_path,
        check=False,
        capture_output=True,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert b"8 passed" in result.stdout


@pytest.mark.parametrize("target", ["badge", "context"])
def test_every_repeated_version_is_validated_and_repaired(tmp_path: Path, target: str) -> None:
    files: dict[str, str | None] = {
        "package.json": '{"version":"1.2.3"}',
        "docs/images/unity-helpers-banner.svg": "<svg><text>v1.2.3</text></svg>",
        ".llm/context.md": "**Version**: 1.2.3",
    }
    if target == "badge":
        files["docs/images/unity-helpers-banner.svg"] = (
            "<svg><text>v1.2.3</text><text>v1.2.2</text></svg>"
        )
    else:
        files[".llm/context.md"] = "**Version**: 1.2.3\n**Version**: 1.2.2"
    prepare(tmp_path, files, False)
    with pytest.raises(ValueError, match="does not match"):
        sync.check_banner(tmp_path)
    plan = sync.banner_plan(tmp_path)
    assert len(plan.changes) == 1
    change = plan.changes[0]
    assert "1.2.2" not in change.content
    change.path.write_text(change.content, encoding="utf-8")
    assert sync.check_banner(tmp_path) == 0
    assert sync.banner_plan(tmp_path).changes == []


@pytest.mark.parametrize(
    ("svg", "valid", "repaired"),
    [
        (
            "<svg><!--<text>v1.2.3</text>--><text>v1.2.2</text></svg>",
            False,
            "<svg><!--<text>v1.2.3</text>--><text>v1.2.3</text></svg>\n",
        ),
        ("<svg><!--<text>v1.2.2</text>--><text>v1.2.3</text></svg>", True, None),
        ("<svg><!--<text>v1.2.3</text>--></svg>", False, None),
        (
            "<svg><![CDATA[<text>v1.2.3</text>]]><text>v1.2.2</text></svg>",
            False,
            "<svg><![CDATA[<text>v1.2.3</text>]]><text>v1.2.3</text></svg>\n",
        ),
        ("<svg><other>v1.2.3</text></svg>", False, None),
        ('<svg><text title=">">v1.2.3</text></svg>', True, None),
        ("<svg><text>v1.2.3</text><text>v1.2.3</text></svg>", True, None),
    ],
)
def test_banner_uses_visible_text_elements(
    tmp_path: Path, svg: str, valid: bool, repaired: str | None
) -> None:
    files: dict[str, str | None] = {
        "package.json": '{"version":"1.2.3"}',
        "docs/images/unity-helpers-banner.svg": svg,
        ".llm/context.md": "**Version**: 1.2.3",
    }
    prepare(tmp_path, files, False)
    if valid:
        assert sync.check_banner(tmp_path) == 0
    else:
        with pytest.raises(ValueError, match="badge does not match"):
            sync.check_banner(tmp_path)
    plan = sync.banner_plan(tmp_path)
    if repaired is None:
        assert plan.changes == []
    else:
        assert len(plan.changes) == 1
        assert plan.changes[0].content == repaired
        plan.changes[0].path.write_text(repaired, encoding="utf-8")
        assert sync.check_banner(tmp_path) == 0
