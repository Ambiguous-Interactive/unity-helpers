"""Compile one Unity source tree or a selection of its test fixtures."""

from __future__ import annotations

import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import cast

ROOT = Path(__file__).resolve().parents[1]
NOTICE = (
    "Iteration feedback only: compiler and source generators run; WUH analyzers are omitted. "
    "Run the normal typecheck gates before publication."
)
USAGE = (
    "Usage: npm run typecheck:iterate -- <runtime|editor|tests|editor-tests|integrations> "
    "[default|legacy|odin|singlethreaded|proto-only] "
    "[--server] [--files Tests/{Runtime,Editor}/Fixture.cs ...]"
)
VARIANTS: dict[str, tuple[str, ...]] = {
    "default": (),
    "legacy": ("-p:WallstopProto=false",),
    "odin": ("-p:WallstopOdin=true",),
    "singlethreaded": ("-p:WallstopSingleThreaded=true",),
    "proto-only": ("-p:WallstopProtoOnly=true",),
}
TEST_TREES = {"tests": "Tests/Runtime", "editor-tests": "Tests/Editor"}
COMPILER_INPUTS = (
    "Proto.Generator",
    "Analyzers",
    "OdinMigration.Generator",
    "Enum.Generator",
)
MSBUILD_SPECIAL = re.compile(r'[%;,*?\[\]\r\n\x00"]')


def load_projects(root: Path) -> dict[str, str]:
    entries: object = json.loads(
        (root / "scripts/typecheck-projects.json").read_text(encoding="utf-8")
    )
    if not isinstance(entries, list):
        raise ValueError("Expected a list of typecheck projects")
    projects: dict[str, str] = {}
    for entry in cast("list[object]", entries):
        if not isinstance(entry, dict):
            raise ValueError("Expected a typecheck project record")
        record = cast("dict[str, object]", entry)
        project_id = record.get("id")
        project = record.get("project")
        if not isinstance(project_id, str) or not isinstance(project, str):
            raise ValueError("Expected an ID and project path in the typecheck registry")
        if project_id in projects:
            raise ValueError(f"Duplicate typecheck project ID: {project_id}")
        projects[project_id] = project
    return projects


def compiler_identity(project_id: str, variant: str, payloads: list[bytes]) -> str:
    digest = hashlib.sha256(f"{project_id}\n{variant}\n".encode())
    for payload in payloads:
        digest.update(hashlib.sha256(payload).digest())
    return f"unity-helpers-iterate-{digest.hexdigest()}"


def scoped_source(root: Path, scope: Path, name: str) -> str:
    invalid = ValueError(
        f"Scoped iteration requires existing C# files under {scope.relative_to(root).as_posix()}: "
        f"{name}"
    )
    if MSBUILD_SPECIAL.search(name):
        raise invalid
    source = root / name
    try:
        resolved = source.resolve(strict=True)
    except (OSError, RuntimeError) as error:
        raise invalid from error
    if (
        source.suffix != ".cs"
        or resolved.suffix != ".cs"
        or not resolved.is_relative_to(scope.resolve())
        or not source.is_relative_to(scope)
        or MSBUILD_SPECIAL.search(str(resolved))
        or not resolved.is_file()
    ):
        raise invalid
    return str(resolved)


def build_arguments(args: list[str], root: Path = ROOT) -> list[str]:
    if not args:
        raise ValueError(USAGE)
    project_id, *remaining = args
    shared = "--server" in remaining
    if shared:
        remaining.remove("--server")
    variant = remaining.pop(0) if remaining and remaining[0] != "--files" else "default"
    projects = load_projects(root)
    tree = TEST_TREES.get(project_id)
    if (
        project_id not in projects
        or variant not in VARIANTS
        or (remaining and (tree is None or remaining[0] != "--files" or len(remaining) < 2))
    ):
        raise ValueError(USAGE)

    command = [
        "build",
        projects[project_id],
        "--no-restore",
        "--nologo",
        "-v",
        "quiet",
        "-p:RunAnalyzers=false",
        f"-p:UseSharedCompilation={str(shared).lower()}",
        *VARIANTS[variant],
        f"-p:OutputPath=bin/iteration/{variant}/",
        f"-p:IntermediateOutputPath=obj/iteration/{variant}/",
    ]
    if tree is not None and remaining:
        sources = dict.fromkeys(scoped_source(root, root / tree, name) for name in remaining[1:])
        command.append("-p:WallstopIterationTestSources=" + "%3B".join(sources))
    if shared:
        payloads = [
            (root / f"Runtime/Analyzers/WallstopStudios.UnityHelpers.{name}.dll").read_bytes()
            for name in COMPILER_INPUTS
        ]
        command.append(f"-p:SharedCompilationId={compiler_identity(project_id, variant, payloads)}")
    return command


def run(args: list[str]) -> int:
    try:
        command = build_arguments(args)
    except (OSError, ValueError) as error:
        print(error, file=sys.stderr)
        return 2

    print(NOTICE, file=sys.stderr)
    if "-p:UseSharedCompilation=true" in command:
        print(
            "Compiler server identity includes all shipped analyzer payloads "
            "and the selected project and variant.",
            file=sys.stderr,
        )
    if any(argument.startswith("-p:WallstopIterationTestSources=") for argument in command):
        print(
            "Scoped test compilation: only the named test fixtures are included. "
            "Production and shared test sources remain included.",
            file=sys.stderr,
        )
    try:
        result = subprocess.run(["dotnet", *command], cwd=ROOT, check=False)  # noqa: S603, S607
    except OSError as error:
        print(error, file=sys.stderr)
        return 1
    return result.returncode if result.returncode >= 0 else 1


if __name__ == "__main__":
    raise SystemExit(run(sys.argv[1:]))
