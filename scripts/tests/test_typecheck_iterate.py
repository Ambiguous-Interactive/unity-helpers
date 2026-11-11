"""Exercise the iteration compiler's argument, source and process boundaries."""

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
from typecheck_iterate import (
    NOTICE,
    ROOT,
    TEST_TREES,
    VARIANTS,
    build_arguments,
    compiler_identity,
    load_projects,
    run,
)

EDITOR_FIXTURES = [
    "Tests/Editor/Tools/OdinMigration/OdinMigrationLiveDictionaryTests.cs",
    "Tests/Editor/Tools/OdinMigration/OdinMigrationLiveAddressTests.cs",
]
RUNTIME_FIXTURES = [
    "Tests/Runtime/Serialization/WProtoEnumFormatterTests.cs",
    "Tests/Runtime/Extensions/EnumExtensionTests.cs",
]
PROJECTS = load_projects(ROOT)


@pytest.mark.parametrize("variant", VARIANTS)
@pytest.mark.parametrize("project", PROJECTS)
def test_build_arguments(project: str, variant: str) -> None:
    assert (ROOT / PROJECTS[project]).is_file()
    assert build_arguments([project, variant]) == [
        "build",
        PROJECTS[project],
        "--no-restore",
        "--nologo",
        "-v",
        "quiet",
        "-p:RunAnalyzers=false",
        "-p:UseSharedCompilation=false",
        *VARIANTS[variant],
        f"-p:OutputPath=bin/iteration/{variant}/",
        f"-p:IntermediateOutputPath=obj/iteration/{variant}/",
    ]
    assert build_arguments([project]) == build_arguments([project, "default"])


@pytest.mark.parametrize(
    "args",
    [
        [],
        ["unknown"],
        ["../outside.csproj"],
        ["tests", "-p:Anything=true"],
        ["tests", "default", "--no-warnings"],
        ["tests", "__proto__"],
        ["tests", "constructor"],
        ["editor-tests", "--files"],
        ["editor-tests", "--server", "--server"],
        ["runtime", "--files", EDITOR_FIXTURES[0]],
        ["editor-tests", "default", "unexpected", EDITOR_FIXTURES[0]],
        ["editor-tests", "--files", RUNTIME_FIXTURES[0]],
        ["editor-tests", "--files", "Tests/Editor/AbsentFixture.cs"],
        ["editor-tests", "--files", "Tests/Editor/Tools/OdinMigration"],
        ["tests", "--files", EDITOR_FIXTURES[0]],
    ],
)
def test_invalid_input_does_not_launch(args: list[str], monkeypatch: pytest.MonkeyPatch) -> None:
    def fail(*_args: object, **_kwargs: object) -> subprocess.CompletedProcess[bytes]:
        pytest.fail("Invalid input launched dotnet")

    monkeypatch.setattr(subprocess, "run", fail)
    assert run(args) == 2


@pytest.mark.parametrize("status", [0, 1, 42, -15, None])
def test_process_status(
    status: int | None, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    def launch(command: list[str], *, cwd: Path, check: bool) -> subprocess.CompletedProcess[bytes]:
        assert command == ["dotnet", *build_arguments(["tests", "odin"])]
        assert cwd == ROOT
        assert check is False
        if status is None:
            raise FileNotFoundError("missing SDK")
        return subprocess.CompletedProcess(command, status)

    monkeypatch.setattr(subprocess, "run", launch)
    assert run(["tests", "odin"]) == (status if status is not None and status >= 0 else 1)
    error = capsys.readouterr().err
    assert NOTICE in error
    if status is None:
        assert "missing SDK" in error


def test_server_identity_includes_every_payload() -> None:
    payloads = [b"proto", b"analyzer", b"migration"]
    identity = compiler_identity("editor-tests", "default", payloads)
    assert identity == compiler_identity("editor-tests", "default", payloads)
    assert identity != compiler_identity("editor-tests", "odin", payloads)
    assert identity != compiler_identity("tests", "default", payloads)
    for index in range(len(payloads)):
        changed = payloads.copy()
        changed[index] += b"changed"
        assert identity != compiler_identity("editor-tests", "default", changed)
    assert compiler_identity("tests", "default", [b"ab", b"c"]) != compiler_identity(
        "tests", "default", [b"a", b"bc"]
    )
    server = build_arguments(["editor-tests", "--server"])
    assert "-p:UseSharedCompilation=true" in server
    assert re.fullmatch(r"-p:SharedCompilationId=unity-helpers-iterate-[0-9a-f]{64}", server[-1])
    assert server[-1] != build_arguments(["editor-tests", "odin", "--server"])[-1]
    assert server[-1] != build_arguments(["tests", "--server"])[-1]
    assert not any(
        argument.startswith("-p:SharedCompilationId=")
        for argument in build_arguments(["editor-tests"])
    )


@pytest.mark.parametrize("variant", VARIANTS)
@pytest.mark.parametrize(
    ("project", "fixtures"), [("editor-tests", EDITOR_FIXTURES), ("tests", RUNTIME_FIXTURES)]
)
def test_scoped_arguments(project: str, variant: str, fixtures: list[str]) -> None:
    args = build_arguments([project, variant, "--server", "--files", *fixtures, fixtures[0]])
    assert args[-2] == "-p:WallstopIterationTestSources=" + "%3B".join(
        str(ROOT / fixture) for fixture in fixtures
    )
    assert args[8:-4] == list(VARIANTS[variant])
    assert "-p:UseSharedCompilation=true" in args


@pytest.mark.parametrize(
    "suffix", [";extra.cs", "%3Bextra.cs", "\n", "[1].cs", "*.cs", "?.cs", '"']
)
def test_rejects_msbuild_path_syntax(suffix: str) -> None:
    with pytest.raises(ValueError, match="requires existing"):
        build_arguments(["editor-tests", "--files", EDITOR_FIXTURES[0] + suffix])


def test_rejects_links_outside_scope(tmp_path: Path) -> None:
    root = tmp_path / "checkout"
    (root / "scripts").mkdir(parents=True)
    (root / "scripts/typecheck-projects.json").write_text(
        json.dumps([{"id": "editor-tests", "project": PROJECTS["editor-tests"]}]),
        encoding="utf-8",
    )
    scope = root / "Tests/Editor"
    scope.mkdir(parents=True)
    outside = tmp_path / "Outside.cs"
    outside.touch()
    link = scope / "Link.cs"
    try:
        link.symlink_to(outside)
    except OSError:
        if os.name != "nt":
            raise
        pytest.skip("Windows fixture cannot create a symlink")
    with pytest.raises(ValueError, match="requires existing"):
        build_arguments(["editor-tests", "--files", str(link)], root)


@pytest.mark.parametrize(
    ("project", "fixtures"), [("editor-tests", EDITOR_FIXTURES), ("tests", RUNTIME_FIXTURES)]
)
def test_msbuild_uses_only_selected_fixtures(project: str, fixtures: list[str]) -> None:
    query = build_arguments([project, "--files", *fixtures])
    dotnet = shutil.which("dotnet")
    assert dotnet is not None
    result = subprocess.run(
        [
            dotnet,
            "msbuild",
            query[1],
            "-nologo",
            "-verbosity:quiet",
            *(argument for argument in query if argument.startswith("-p:")),
            "-getItem:Compile",
        ],
        cwd=ROOT,
        check=False,
        capture_output=True,
        encoding="utf-8",
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    payload = cast("dict[str, dict[str, list[dict[str, str]]]]", json.loads(result.stdout))
    items = [(ROOT / item["Identity"]).resolve() for item in payload["Items"]["Compile"]]
    tree = ROOT / TEST_TREES[project]
    actual = {
        item
        for item in items
        if item.is_relative_to(tree)
        and not item.is_relative_to(tree / "TestTypes")
        and not item.is_relative_to(tree / "TestDoubles")
    }
    assert actual == {ROOT / fixture for fixture in fixtures}
    assert all(";" not in item.name for item in items)
