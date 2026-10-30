"""Compare every shipped compiler-input DLL with an isolated Release build."""

from __future__ import annotations

import argparse
import hashlib
import shutil
import subprocess
import sys
import tempfile
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHIPPED = "Runtime/Analyzers"
ANALYZERS = (
    "WallstopStudios.UnityHelpers.Analyzers",
    "WallstopStudios.UnityHelpers.Proto.Generator",
    "WallstopStudios.UnityHelpers.OdinMigration.Generator",
)


@dataclass(frozen=True)
class BuildResult:
    ok: bool
    output: str = ""


Build = Callable[[str, Path], BuildResult]
Log = Callable[[str], None]


def project_path(assembly: str) -> str:
    return f"Generator~/{assembly}/{assembly}.csproj"


def dll_hashes(directory: Path) -> dict[str, str]:
    hashes: dict[str, str] = {}
    for entry in directory.iterdir():
        if entry.suffix == ".dll" and entry.is_file():
            with entry.open("rb") as stream:
                hashes[entry.name] = hashlib.file_digest(stream, "sha256").hexdigest()
    return hashes


def dotnet_build(project: str, output_directory: Path) -> BuildResult:
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        return BuildResult(False, "dotnet is required to build compiler inputs.")
    try:
        result = subprocess.run(  # noqa: S603 - fixed build tool and owned project paths.
            [
                dotnet,
                "build",
                str(ROOT / project),
                "-c",
                "Release",
                "--nologo",
                "-v",
                "quiet",
                f"-p:AnalyzerPayloadOutputDir={output_directory}",
            ],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
            timeout=600,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        return BuildResult(False, str(error))
    return BuildResult(result.returncode == 0, result.stdout + result.stderr)


def error_log(message: str) -> None:
    print(message, file=sys.stderr)


def verify(
    shipped_directory: Path,
    scratch_directory: Path,
    *,
    build: Build = dotnet_build,
    fix: bool = False,
    log: Log = print,
    log_error: Log = error_log,
) -> int:
    if not shipped_directory.is_dir():
        log_error(
            f"[shipped-analyzers] The shipped analyzer directory {shipped_directory} does not exist."
        )
        log_error(
            "  This check compares the committed analyzer DLLs against a fresh build; with the directory"
        )
        log_error("  gone there is nothing to compare and the comparison is not silently skipped.")
        return 1
    try:
        scratch_directory.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="build-", dir=scratch_directory) as isolated:
            return verify_build(
                shipped_directory,
                Path(isolated),
                build=build,
                fix=fix,
                log=log,
                log_error=log_error,
            )
    except OSError as error:
        log_error(f"[shipped-analyzers] Could not verify compiler inputs: {error}")
        return 1


def verify_build(
    shipped_directory: Path,
    output_directory: Path,
    *,
    build: Build,
    fix: bool,
    log: Log,
    log_error: Log,
) -> int:
    for assembly in ANALYZERS:
        project = project_path(assembly)
        result = build(project, output_directory)
        if not result.ok:
            log_error(f"[shipped-analyzers] {project} failed to build:")
            log_error(result.output)
            return 1

    fresh = dll_hashes(output_directory)
    shipped = dll_hashes(shipped_directory)
    not_built = [assembly for assembly in ANALYZERS if f"{assembly}.dll" not in fresh]
    if not_built:
        log_error(
            "[shipped-analyzers] The Release build did not produce every analyzer this package ships."
        )
        for assembly in not_built:
            log_error(f"  {assembly}.dll (expected from {project_path(assembly)})")
        log_error(
            "  Nothing was compared. Check that the project still sets AnalyzerPayloadOutputDir and"
        )
        log_error("  copies its output there.")
        return 1

    missing = sorted(fresh.keys() - shipped.keys())
    unexpected = sorted(shipped.keys() - fresh.keys())
    stale = sorted(name for name in fresh.keys() & shipped.keys() if fresh[name] != shipped[name])
    if unexpected:
        log_error(
            f"[shipped-analyzers] {SHIPPED} holds {len(unexpected)} DLL(s) no analyzer project produces."
        )
        for name in unexpected:
            log_error(f"  {SHIPPED}/{name}")
        log_error(
            "  Unity loads every DLL here as a compiler input, so an unlisted one ships ungoverned."
        )
        log_error("  Add its project to ANALYZERS in scripts/verify_shipped_analyzers.py, or")
        log_error("  delete the DLL.")
        return 1
    if not missing and not stale:
        return 0
    if fix:
        for name in missing + stale:
            try:
                with tempfile.TemporaryDirectory(
                    prefix=".refresh-", dir=shipped_directory
                ) as staging:
                    staged = Path(staging) / name
                    shutil.copyfile(output_directory / name, staged)
                    staged.replace(shipped_directory / name)
            except OSError as error:
                log_error(f"[shipped-analyzers] Could not refresh {name}: {error}")
                return 1
        log(
            f"[shipped-analyzers] Refreshed {len(missing) + len(stale)} shipped analyzer file(s); stage them with the source change:"
        )
        for name in sorted(missing + stale):
            log(f"  {SHIPPED}/{name}")
        return 0

    log_error(
        "[shipped-analyzers] The shipped analyzer assemblies do not match the sources beside them."
    )
    for name in missing:
        log_error(f"  {SHIPPED}/{name} is missing; the Release build produces it.")
    for name in stale:
        log_error(f"  {SHIPPED}/{name} differs from a fresh Release build of its sources.")
    log_error("  Fix: npm run verify:shipped-analyzers:fix, then stage the rebuilt DLL(s).")
    log_error("  CI compares these byte-for-byte, so a stale one fails 'WallstopProto Generator'.")
    return 1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fix", action="store_true", help="Refresh missing or stale shipped DLLs.")
    arguments = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="shipped-analyzers-") as scratch:
        return verify(ROOT / SHIPPED, Path(scratch), fix=arguments.fix)


if __name__ == "__main__":
    sys.exit(main())
