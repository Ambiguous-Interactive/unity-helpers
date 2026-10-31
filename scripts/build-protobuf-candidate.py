"""Build the pinned upstream protobuf candidate without changing shipped assemblies."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import signal
import subprocess
import sys
from pathlib import Path
from typing import TypedDict

SOURCE_COMMIT = "3aa672eaaf7763b86d62bec9ed7d7896b0511a8b"
SDK_VERSION = "9.0.319"
SOURCE_URL = "https://github.com/protobuf-net/protobuf-net.git"
ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "Generator~/ProtobufNetCurrentCandidate/il2cpp-aot.patch"
GIT_LOCAL_VARIABLES = frozenset(
    [
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_CONFIG",
        "GIT_CONFIG_PARAMETERS",
        "GIT_CONFIG_COUNT",
        "GIT_OBJECT_DIRECTORY",
        "GIT_DIR",
        "GIT_WORK_TREE",
        "GIT_IMPLICIT_WORK_TREE",
        "GIT_GRAFT_FILE",
        "GIT_INDEX_FILE",
        "GIT_REPLACE_REF_BASE",
        "GIT_PREFIX",
        "GIT_SHALLOW_FILE",
        "GIT_COMMON_DIR",
    ]
)


class CandidateReceipt(TypedDict):
    sourceCommit: str
    sourceUrl: str
    patchSha256: str
    buildScriptSha256: str
    sourceInventorySha256: str
    sourceInventoryScope: str
    sdk: str
    dotnetInfo: str
    targetFramework: str
    files: dict[str, str]
    assetsSha256: str
    nativeQualificationComplete: bool


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(arguments: list[str], directory: Path, log: Path, timeout_seconds: float = 900) -> str:
    process = subprocess.Popen(  # noqa: S603 - explicit argv, no shell; tools selected by caller.
        arguments,
        cwd=directory,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        start_new_session=os.name != "nt",
        env={
            **{
                name: value
                for name, value in os.environ.items()
                if name not in GIT_LOCAL_VARIABLES
                and not name.startswith(("GIT_CONFIG_KEY_", "GIT_CONFIG_VALUE_"))
            },
            "GIT_NO_REPLACE_OBJECTS": "1",
            "GIT_TERMINAL_PROMPT": "0",
            "DOTNET_CLI_UI_LANGUAGE": "en",
        },
    )
    timed_out = False
    try:
        output, _ = process.communicate(timeout=timeout_seconds)
    except subprocess.TimeoutExpired:
        timed_out = True
        if sys.platform == "win32":
            subprocess.run(  # noqa: S603 - Windows process-tree termination.
                ["taskkill", "/PID", str(process.pid), "/T", "/F"],  # noqa: S607 - Windows system utility.
                check=True,
                timeout=30,
            )
        else:
            os.killpg(process.pid, signal.SIGKILL)
        output, _ = process.communicate(timeout=30)
    log.write_text(output, encoding="utf-8")
    log.with_suffix(".process.json").write_text(
        json.dumps(
            {
                "arguments": arguments,
                "exitCode": process.returncode,
                "timedOut": timed_out,
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    if timed_out or process.returncode:
        raise RuntimeError(
            f"Command failed ({process.returncode}, timedOut={timed_out}); see {log}"
        )
    return output.strip()


def select_target(project: Path) -> None:
    original = "<TargetFrameworks>net462;netstandard2.0;netstandard2.1;net8.0</TargetFrameworks>"
    text = project.read_text(encoding="utf-8")
    if text.count(original) != 1:
        raise ValueError(f"Unexpected upstream target declaration: {project}")
    project.write_text(
        text.replace(original, "<TargetFrameworks>netstandard2.1</TargetFrameworks>"),
        encoding="utf-8",
    )


def canonicalize_origin(source: Path, output: Path) -> str:
    run(
        ["git", "remote", "set-url", "origin", SOURCE_URL],
        source,
        output / "canonical-origin.log",
    )
    actual = run(
        ["git", "config", "--local", "--get", "remote.origin.url"],
        source,
        output / "source-url.log",
    )
    if actual != SOURCE_URL:
        raise ValueError("Candidate source origin mismatch")
    return actual


def inventory(source: Path, output: Path, phase: str) -> list[dict[str, str]]:
    log = output / f"inventory-{phase}.log"
    run(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        source,
        log,
    )
    names = log.read_text(encoding="utf-8")
    records = [
        {"path": name, "sha256": digest(source / name)}
        for name in sorted(set(names.split("\0")))
        if name
    ]
    if not records:
        raise ValueError("Candidate source inventory is empty")
    (output / f"source-{phase}.json").write_text(
        json.dumps(records, indent=2) + "\n", encoding="utf-8"
    )
    return records


def build(output: Path, dotnet: str, upstream: str) -> CandidateReceipt:
    output = output.resolve()
    if output.exists():
        raise ValueError("Output must be a new directory; prior evidence is never overwritten")
    if output == ROOT or (
        ROOT in output.parents
        and not any(
            part.startswith(".") or part.endswith("~") for part in output.relative_to(ROOT).parts
        )
    ):
        raise ValueError("Build output inside the package must be hidden from Unity")
    executable = shutil.which(dotnet)
    if not executable:
        raise ValueError(f"Cannot find .NET executable: {dotnet}")
    patch_hash = digest(PATCH)
    script_hash = digest(Path(__file__))
    output.mkdir(parents=True)
    source = output / "source"
    run(
        ["git", "clone", "--no-hardlinks", upstream, str(source)],
        output,
        output / "clone.log",
    )
    run(["git", "checkout", "--detach", SOURCE_COMMIT], source, output / "checkout.log")
    actual = run(["git", "rev-parse", "HEAD"], source, output / "source-identity.log")
    if actual != SOURCE_COMMIT:
        raise ValueError("Upstream source identity mismatch")
    source_url = canonicalize_origin(source, output)
    run(["git", "apply", "--check", str(PATCH)], source, output / "patch-check.log")
    run(["git", "apply", str(PATCH)], source, output / "patch-apply.log")
    (source / "global.json").write_text(
        json.dumps(
            {
                "sdk": {
                    "version": SDK_VERSION,
                    "rollForward": "disable",
                    "allowPrerelease": False,
                }
            }
        ),
        encoding="utf-8",
    )
    for name in ("protobuf-net", "protobuf-net.Core"):
        select_target(source / "src" / name / f"{name}.csproj")
    selected_sdk = run([executable, "--version"], source, output / "sdk.log")
    if selected_sdk != SDK_VERSION:
        raise ValueError(f"Expected SDK {SDK_VERSION}; selected {selected_sdk}")
    before = inventory(source, output, "before")
    run(
        [
            executable,
            "build",
            "src/protobuf-net/protobuf-net.csproj",
            "-c",
            "Release",
            "-f",
            "netstandard2.1",
            "-p:GeneratePackageOnBuild=false",
            "-p:UseSharedCompilation=false",
            "--nologo",
        ],
        source,
        output / "build.log",
    )
    after = inventory(source, output, "after")
    if before != after or patch_hash != digest(PATCH) or script_hash != digest(Path(__file__)):
        raise ValueError("Candidate build inputs changed during the invocation")
    assemblies = output / "assemblies"
    assemblies.mkdir()
    files: dict[str, str] = {}
    for name in ("protobuf-net", "protobuf-net.Core"):
        for extension in ("dll", "xml"):
            path = source / "src" / name / "bin/Release/netstandard2.1" / f"{name}.{extension}"
            destination = assemblies / path.name
            shutil.copyfile(path, destination)
            files[path.name] = digest(destination)
    assets = source / "src/protobuf-net/obj/project.assets.json"
    shutil.copyfile(assets, output / "project.assets.json")
    runtime = run([executable, "--info"], source, output / "dotnet-info.log")
    receipt: CandidateReceipt = {
        "sourceCommit": SOURCE_COMMIT,
        "sourceUrl": source_url,
        "patchSha256": patch_hash,
        "buildScriptSha256": script_hash,
        "sourceInventorySha256": digest(output / "source-before.json"),
        "sourceInventoryScope": "tracked and nonignored working-tree files; compiler-generated intermediates excluded",
        "sdk": selected_sdk,
        "dotnetInfo": runtime,
        "targetFramework": "netstandard2.1",
        "files": files,
        "assetsSha256": digest(assets),
        "nativeQualificationComplete": False,
    }
    (assemblies / "candidate-receipt.json").write_text(
        json.dumps(receipt, indent=2) + "\n", encoding="utf-8"
    )
    return receipt


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--upstream", default=SOURCE_URL)
    arguments = parser.parse_args()
    try:
        receipt = build(arguments.output, arguments.dotnet, arguments.upstream)
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
        print(str(error), file=sys.stderr)
        return 1
    print(json.dumps(receipt, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
