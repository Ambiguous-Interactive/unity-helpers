"""Stage and export a Unity package through the local Docker runner."""

from __future__ import annotations

import hashlib
import os
import shutil
import subprocess
import sys
import tarfile
from dataclasses import dataclass
from itertools import combinations
from pathlib import Path
from typing import IO, cast

from stage_unitypackage import package_metadata, release_version, stage_unity_package

DEFAULT_PROJECT_DIRECTORY = ".artifacts/unity/unitypackage-project"
DEFAULT_UNITY_TIMEOUT = "7200"
OUTPUT_DIRECTORY = "unitypackage-output"
UNITY_LOG_NAME = "unity.log"


@dataclass(slots=True)
class ExportOptions:
    project_directory: Path
    output_argument: str | None = None
    stage_only: bool = False


def parse_options(arguments: list[str], repository: Path) -> ExportOptions:
    options = ExportOptions(
        Path(os.environ.get("UNITY_PACKAGE_PROJECT_DIR") or repository / DEFAULT_PROJECT_DIRECTORY)
    )
    operands = iter(arguments)
    for argument in operands:
        if argument == "--stage-only":
            options.stage_only = True
        elif argument in ("--output", "--project-dir"):
            value = next(operands, None)
            if value is None or (argument == "--project-dir" and not value):
                raise ValueError(f"{argument} requires a path.")
            if argument == "--output":
                options.output_argument = value or None
            else:
                options.project_directory = Path(value)
        else:
            raise ValueError(f"Unknown argument: {argument}")
    return options


def shell_status(returncode: int) -> int:
    return 128 - returncode if returncode < 0 else returncode


def run_export(repository: Path, project: Path, output_argument: str, unity_version: str) -> int:
    output_path = Path(output_argument)
    internal_directory = project / OUTPUT_DIRECTORY
    internal_package = internal_directory / output_path.name
    log = internal_directory / UNITY_LOG_NAME
    # Staging creates a fresh project. Only launch tee into a directory we just created.
    internal_directory.mkdir()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    command = [
        str(repository / "scripts/unity/run-unity-docker.sh"),
        "-batchmode",
        "-nographics",
        "-quit",
        "-releaseCodeOptimization",
        "-projectPath",
        "/project",
        "-executeMethod",
        "UnityHelpersPackageExporter.Export",
        "-exportOutput",
        f"/project/{OUTPUT_DIRECTORY}/{output_path.name}",
        "-logFile",
        "-",
    ]
    environment = {
        **os.environ,
        "UNITY_TEST_PROJECT_DIR": str(project),
        "UNITY_VERSION": unity_version,
        "UNITY_TIMEOUT": os.environ.get("UNITY_TIMEOUT") or DEFAULT_UNITY_TIMEOUT,
    }
    sys.stdout.flush()
    # Use the host tee for byte-preserving streams and its existing output failure semantics.
    tee_command = ["tee", str(log)]
    with subprocess.Popen(tee_command, stdin=subprocess.PIPE) as tee:  # noqa: S603 - native tee, no shell.
        log_input = cast("IO[bytes]", tee.stdin)
        try:
            with subprocess.Popen(  # noqa: S603 - fixed repository runner, argument list only.
                command, stdout=log_input, stderr=subprocess.STDOUT, env=environment
            ) as unity:
                log_input.close()
                unity_status = shell_status(unity.wait())
        finally:
            log_input.close()
        tee_status = shell_status(tee.wait())
    if tee_status:
        print(
            f"ERROR: Failed to persist Unity package export log with tee exit code {tee_status}: {log}",
            file=sys.stderr,
        )
        return tee_status
    if unity_status:
        print(
            f"ERROR: Unity package export failed with exit code {unity_status}. Log: {log}",
            file=sys.stderr,
        )
        return unity_status
    if not internal_package.is_file() or internal_package.stat().st_size == 0:
        print(
            f"ERROR: Unity package was not exported: {internal_package}",
            file=sys.stderr,
        )
        return 1

    publish_package(internal_package, log, output_path)
    print(f"==> [export-unitypackage] Exported {output_argument}")
    return 0


def paths_alias(first: Path, second: Path) -> bool:
    return first.resolve() == second.resolve() or (
        first.exists() and second.exists() and first.samefile(second)
    )


def publish_package(package: Path, log: Path, output_path: Path) -> None:
    checksum_path = output_path.with_name(f"{output_path.name}.sha256")
    for first, second in combinations((package, log, output_path, checksum_path), 2):
        if paths_alias(first, second):
            raise ValueError(f"Unity package export must use distinct files: {first} and {second}")
    if output_path.is_symlink() and not output_path.exists():
        raise ValueError(f"Refusing to write through a dangling output symlink: {output_path}")
    # Preserve an existing output file's mode. Apply the current umask for a newly created file.
    mode = package.stat().st_mode & 0o777
    with (
        package.open("rb") as source,
        os.fdopen(
            os.open(output_path, os.O_CREAT | os.O_WRONLY | os.O_TRUNC, mode), "wb"
        ) as output,
    ):
        shutil.copyfileobj(source, output)
    with output_path.open("rb") as package_file:
        digest = hashlib.file_digest(package_file, "sha256").hexdigest()
    filename = os.fsencode(output_path.name)
    escaped = filename.replace(b"\\", b"\\\\").replace(b"\n", b"\\n").replace(b"\r", b"\\r")
    prefix = b"\\" if escaped != filename else b""
    checksum_path.write_bytes(prefix + digest.encode("ascii") + b"  " + escaped + b"\n")


def main(arguments: list[str] | None = None) -> int:
    repository = Path(__file__).resolve().parents[2]
    try:
        options = parse_options(sys.argv[1:] if arguments is None else arguments, repository)
        name, version = package_metadata(repository)
        output_argument = options.output_argument or str(
            repository / f".artifacts/release/{name}-{version}.unitypackage"
        )
        if not options.stage_only and Path(output_argument).name.casefold() == UNITY_LOG_NAME:
            raise ValueError(
                f"The output filename must differ from the Unity log name {UNITY_LOG_NAME}."
            )
    except (OSError, ValueError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    try:
        unity_version = os.environ.get("UNITY_VERSION") or release_version(repository)
        project = stage_unity_package(repository, options.project_directory, unity_version)
    except (
        OSError,
        ValueError,
        tarfile.TarError,
        subprocess.CalledProcessError,
    ) as error:
        print(str(error), file=sys.stderr)
        return 1
    if options.stage_only:
        print("==> [export-unitypackage] Stage-only mode complete.")
        return 0
    try:
        return run_export(repository, project, output_argument, unity_version)
    except (OSError, ValueError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
