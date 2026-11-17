"""Preserve original devcontainer policy decisions and read-only CLI contracts."""

from __future__ import annotations

import codecs
import json
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TypedDict, cast

import pytest
import yaml

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import validate_devcontainer_config as checker


class Case(TypedDict):
    name: str
    edits: dict[str, str | None]
    expected: str
    verbose: bool
    exit: int
    stdout: str
    stderr: str


class Corpus(TypedDict):
    provenance: dict[str, str]
    base: dict[str, str]
    cases: list[Case]


CORPUS = cast(
    "Corpus",
    json.loads(
        (Path(__file__).parent / "fixtures/devcontainer-config-v1.json").read_text(encoding="utf-8")
    ),
)


def populate(root: Path, edits: dict[str, str | None] | None = None) -> None:
    for relative, content in {**CORPUS["base"], **(edits or {})}.items():
        if content is not None:
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content.encode())


def snapshot(root: Path) -> dict[str, bytes]:
    return {
        path.relative_to(root).as_posix(): path.read_bytes()
        for path in root.rglob("*")
        if path.is_file()
    }


@pytest.mark.parametrize("case", CORPUS["cases"], ids=lambda case: case["name"])
def test_original_executable_decisions(
    case: Case, tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    populate(tmp_path, case["edits"])
    before = snapshot(tmp_path)
    arguments = ["--repo-root", str(tmp_path)] + (["--verbose"] if case["verbose"] else [])
    assert checker.main(arguments) == case["exit"]
    output = capsys.readouterr()
    assert case["expected"] in output.out + output.err
    assert output.out == case["stdout"]
    if case["exit"] != 0:
        expected_error = case["stderr"].strip()
        # Frozen Linux paths identify the same input using native separators on Windows.
        for relative in CORPUS["base"]:
            expected_error = expected_error.replace(
                f"<ROOT>/{relative}", f"<ROOT>{Path('/') / relative}"
            )
        assert output.err.replace(str(tmp_path), "<ROOT>") == f"ERROR: {expected_error}\n"
    else:
        assert output.err == case["stderr"]
    assert snapshot(tmp_path) == before


@pytest.mark.parametrize(
    ("language", "alias"),
    [
        ("shellscript", "\u017fhellscript"),
        ("markdown", "mar\u212adown"),
        ("javascript", "javascr\u0131pt"),
    ],
)
def test_original_ordinal_language_rejections(
    tmp_path: Path, capsys: pytest.CaptureFixture[str], language: str, alias: str
) -> None:
    configuration = CORPUS["base"][".devcontainer/devcontainer.json"]
    populate(
        tmp_path,
        {".devcontainer/devcontainer.json": configuration.replace(f"[{language}]", f"[{alias}]")},
    )
    before = snapshot(tmp_path)
    assert checker.main(["--repo-root", str(tmp_path)]) == 1
    output = capsys.readouterr()
    assert f"[{language}]" in output.out
    assert (
        "devcontainer.json is missing 1 explicit formatter assignment(s). See warnings above."
        in output.err
    )
    assert snapshot(tmp_path) == before


def test_repository_passes() -> None:
    assert checker.main([]) == 0


@pytest.mark.parametrize("encoding", ["utf-8-sig", "utf-16", "utf-16-be", "utf-32", "utf-32-be"])
def test_bom_inputs_keep_original_text_contract(tmp_path: Path, encoding: str) -> None:
    populate(tmp_path)
    for path in tmp_path.rglob("*"):
        if path.is_file():
            payload = path.read_text(encoding="utf-8").encode(encoding)
            if encoding == "utf-16-be":
                payload = codecs.BOM_UTF16_BE + payload
            elif encoding == "utf-32-be":
                payload = codecs.BOM_UTF32_BE + payload
            path.write_bytes(payload)
    before = snapshot(tmp_path)
    assert checker.main(["--repo-root", str(tmp_path)]) == 0
    assert snapshot(tmp_path) == before


@pytest.mark.parametrize("failure", ["read", "encoding", "directory"])
def test_input_failures_are_visible_and_nonmutating(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    failure: str,
) -> None:
    populate(tmp_path)
    read_bytes = Path.read_bytes
    path = tmp_path / ".devcontainer/devcontainer.json"
    if failure == "read":

        def refuse_read(_path: Path) -> bytes:
            raise PermissionError("controlled read refusal")

        monkeypatch.setattr(Path, "read_bytes", refuse_read)
    elif failure == "encoding":
        path.write_bytes(b"\xff")
    else:
        path.unlink()
        path.mkdir()
    before = {
        file.relative_to(tmp_path).as_posix(): read_bytes(file)
        for file in tmp_path.rglob("*")
        if file.is_file()
    }
    assert checker.main(["--repo-root", str(tmp_path)]) == 1
    assert "ERROR:" in capsys.readouterr().err
    after = {
        file.relative_to(tmp_path).as_posix(): read_bytes(file)
        for file in tmp_path.rglob("*")
        if file.is_file()
    }
    assert after == before


@pytest.mark.parametrize(
    "script",
    [
        "scripts/validate-devcontainer-config.ps1",
        "scripts/tests/test-validate-devcontainer-config.ps1",
    ],
)
def test_powershell_compatibility_launchers(script: str, tmp_path: Path) -> None:
    shell = shutil.which("pwsh")
    assert shell is not None
    root = tmp_path / "space λ"
    populate(root)
    arguments = (
        ["-TestFilter", "test_repository_passes"]
        if "/tests/" in script
        else ["-RepoRoot", str(root), "-VerboseOutput"]
    )
    before = snapshot(root)
    result = subprocess.run(
        [shell, "-NoProfile", "-File", str(checker.ROOT / script), *arguments],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=60,
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert (
        "1 passed" if "/tests/" in script else "All expected formatter assignments"
    ) in result.stdout
    assert snapshot(root) == before


def test_real_python_cli_keeps_utf8_and_input_bytes(tmp_path: Path) -> None:
    root = tmp_path / "space λ"
    populate(root)
    before = snapshot(root)
    result = subprocess.run(
        [
            sys.executable,
            str(checker.ROOT / "scripts/validate_devcontainer_config.py"),
            "--repo-root",
            str(root),
            "--verbose",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=30,
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "All expected formatter assignments" in result.stdout
    assert snapshot(root) == before


@pytest.mark.parametrize("mutation", ["none", "missing", "late"])
def test_publish_workflow_provisions_uv_before_validation(mutation: str) -> None:
    content = (checker.ROOT / checker.PUBLISH).read_text(encoding="utf-8")
    setup = (
        "      - name: Setup Python tooling\n        uses: ./.github/actions/setup-python-tools\n\n"
    )
    assert setup in content
    if mutation == "missing":
        content = content.replace(setup, "")
    elif mutation == "late":
        content = content.replace(setup, "").replace(
            "      - name: Set up QEMU", setup + "      - name: Set up QEMU"
        )
    data = cast("dict[str, object]", yaml.safe_load(content))
    jobs = cast("dict[str, object]", data["jobs"])
    job = cast("dict[str, object]", jobs["publish-devcontainer"])
    steps = cast("list[dict[str, object]]", job["steps"])
    provision = next(
        (
            index
            for index, step in enumerate(steps)
            if step.get("uses") == "./.github/actions/setup-python-tools"
        ),
        None,
    )
    validation = next(
        (index for index, step in enumerate(steps) if step.get("run") == checker.SELF_VALIDATION),
        None,
    )
    assert validation is not None
    assert (provision is not None and provision < validation) is (mutation == "none")


@pytest.mark.parametrize("extra", [[], ["-Unexpected"]])
def test_powershell_validator_rejects_the_selected_bad_root_or_arguments(
    tmp_path: Path, extra: list[str]
) -> None:
    shell = shutil.which("pwsh")
    assert shell is not None
    root = tmp_path / "bad λ"
    populate(
        root,
        {
            ".devcontainer/devcontainer.json": CORPUS["base"][
                ".devcontainer/devcontainer.json"
            ].replace("[csharp]", "[missing]")
        },
    )
    before = snapshot(root)
    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-File",
            str(checker.ROOT / "scripts/validate-devcontainer-config.ps1"),
            "-RepoRoot",
            str(root),
            *extra,
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=30,
        check=False,
    )
    assert result.returncode == 1
    assert (
        "Unexpected arguments" if extra else "is missing 1 explicit formatter assignment"
    ) in result.stderr
    if not extra:
        expected = next(
            case for case in CORPUS["cases"] if case["name"] == "missing-language-csharp"
        )
        assert result.stdout == expected["stdout"]
    assert snapshot(root) == before
