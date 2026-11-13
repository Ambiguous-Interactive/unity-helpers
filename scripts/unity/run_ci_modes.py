"""Run each selected Unity mode in its own project under one activated job lease."""

from __future__ import annotations

import argparse
import base64
import contextlib
import json
import math
import os
import re
import shutil
import signal
import stat
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, BinaryIO, cast

from editmode_shards import merge_results, partition_assemblies

if TYPE_CHECKING:
    from collections.abc import Callable, Generator
    from types import FrameType

ROOT = Path(__file__).resolve().parents[2]
MODES = ("editmode", "playmode", "standalone")
MAX_CONCURRENT_WORKERS = 4
WORKER_PRIORITY = (
    "editmode",
    "editmode_isolated",
    "standalone",
    "playmode",
    "editmode_shard",
    "proto_only",
)
WORKER = """
$ErrorActionPreference = 'Stop'
try {
    $options = Get-Content -LiteralPath $env:UH_MODE_CONFIG -Raw | ConvertFrom-Json -AsHashtable
    & $env:UH_MODE_RUNNER @options
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
"""
Parameters = dict[str, str | bool | list[str]]


@dataclass(frozen=True)
class Mode:
    name: str
    parameters: Parameters


@dataclass
class Worker:
    mode: Mode
    process: subprocess.Popen[bytes]
    started: float
    ended: float | None = None
    forced: bool = False


def read_selection(raw: str, profiles_raw: str) -> tuple[list[str], dict[str, str]]:
    """Validate complete mode profiles before creating projects or processes."""
    if len(raw) > 1024 or len(profiles_raw) > 65536:
        raise ValueError("Unity mode selection exceeds its size limit")
    selected: object = json.loads(raw)
    profiles: object = json.loads(profiles_raw)
    if not isinstance(selected, list):
        raise ValueError("Select a nonempty list of Unity modes")
    items = cast("list[object]", selected)
    if not items or any(not isinstance(mode, str) or mode not in MODES for mode in items):
        raise ValueError("Unknown or empty Unity mode selection")
    names = cast("list[str]", items)
    if len(set(names)) != len(names):
        raise ValueError("Duplicate Unity mode selection")
    if not isinstance(profiles, dict):
        raise ValueError("Unity assembly profiles must be an object")
    values = cast("dict[str, object]", profiles)
    if set(values) != set(MODES) or any(
        not isinstance(value, str) or not value.strip() for value in values.values()
    ):
        raise ValueError("Every Unity mode needs a nonempty assembly profile")
    return names, cast("dict[str, str]", values)


def build_modes(
    version: str,
    selected: list[str],
    profiles: dict[str, str],
    *,
    repository: Path,
    workspace: Path,
    artifacts: Path,
    session: Path,
    single_threaded: bool,
    editmode_shards: int = 1,
) -> list[Mode]:
    """Keep each existing mode's assemblies, defines and build settings."""
    if re.fullmatch(r"[1-9][0-9]{3}\.[0-9]{1,2}\.[0-9]{1,3}f[1-9][0-9]{0,2}", version) is None:
        raise ValueError("Invalid Unity version")
    jobs: list[Mode] = []
    suffix = "-single-threaded" if single_threaded else ""
    for name in MODES:
        if name not in selected:
            continue
        options: Parameters = {
            "UnityVersion": version,
            "TestMode": name,
            "AssemblyNames": profiles[name],
            "RepoRoot": str(repository),
            "ProjectRoot": str(workspace),
            "ProjectRootMinimumFreeGb": "0",
            "ArtifactsPath": str(artifacts / f"{version}-{name}{suffix}"),
            "LicenseSessionPath": str(session),
            "ReleaseCodeOptimization": True,
            "ReleasePlayerBuild": True,
            "Il2CppCompilerConfiguration": "Release",
        }
        if single_threaded:
            options["ProjectScope"] = "single-threaded"
            options["AdditionalScriptingDefines"] = [
                "SINGLE_THREADED",
                "WALLSTOP_CONCAVE_HULL_STATS",
            ]
        else:
            options["IncludeIntegrations"] = True
        if name == "editmode":
            options["ShareEditorProject"] = True
            options["IsolateCompilationFixtures"] = True
            options["EditorPhaseGroup"] = "Main"
        if name == "standalone":
            options["ManagedStrippingLevel"] = "High"
        jobs.append(Mode(name, options))
    if "standalone" in selected and not single_threaded:
        options = dict(jobs[-1].parameters)
        options.pop("IncludeIntegrations", None)
        options["AssemblyNames"] = "WallstopStudios.UnityHelpers.Tests.ProtoOnly"
        options["TestCategory"] = ""
        options["ProjectScope"] = "proto-only"
        options["AdditionalScriptingDefines"] = ["WALLSTOP_PROTO_ONLY"]
        options["ArtifactsPath"] = str(artifacts / f"{version}-standalone-proto-only")
        jobs.append(Mode("proto_only", options))
    if "editmode" in selected:
        options = dict(jobs[0].parameters)
        options["EditorPhaseGroup"] = "Isolated"
        options["ProjectScope"] = "single-threaded-isolated" if single_threaded else "isolated"
        options["ArtifactsPath"] = str(Path(str(options["ArtifactsPath"])) / "isolated")
        jobs.append(Mode("editmode_isolated", options))
        partitions = partition_assemblies(profiles["editmode"], editmode_shards)
        if len(partitions) == 2:
            primary = jobs[0]
            options = dict(primary.parameters)
            jobs[0] = Mode(primary.name, {**primary.parameters, "AssemblyNames": partitions[0]})
            options["AssemblyNames"] = partitions[1]
            options["ProjectScope"] = "single-threaded-shard-1" if single_threaded else "shard-1"
            options["ArtifactsPath"] = str(artifacts / f"{version}-e1{suffix}")
            jobs.append(Mode("editmode_shard", options))
    return jobs


def git(repository: Path, *arguments: str) -> str:
    executable = shutil.which("git")
    if executable is None:
        raise RuntimeError("Git is required for isolated Unity source worktrees")
    for attempt in range(4):
        result = subprocess.run(  # noqa: S603 - fixed Git operations and validated owned cache paths.
            [executable, "-C", str(repository), *arguments],
            capture_output=True,
            text=True,
            encoding="utf-8",
            check=False,
            timeout=120,
        )
        if result.returncode == 0:
            return result.stdout.strip()
        if attempt == 3 or "index.lock" not in result.stderr:
            raise RuntimeError(f"Unity source worktree operation failed: {result.stderr.strip()}")
        time.sleep(0.25 * (2**attempt))
    raise RuntimeError("Unity source worktree retry budget expired")


def linked_path(path: Path) -> bool:
    try:
        metadata = path.lstat()
    except FileNotFoundError:
        return False
    attributes = getattr(metadata, "st_file_attributes", 0)
    return path.is_symlink() or bool(attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT)


def isolate_sources(modes: list[Mode], repository: Path, cache: Path) -> list[Mode]:
    """Refresh only owned worktrees; fixtures may safely rewrite their own package files."""
    cache = cache.absolute()
    if cache.resolve().is_relative_to(repository.resolve()):
        raise ValueError("Unity source worktrees must be outside the input checkout")
    commit = git(repository, "rev-parse", "HEAD")
    common = Path(
        git(repository, "rev-parse", "--path-format=absolute", "--git-common-dir")
    ).resolve()
    isolated: list[Mode] = []
    cache.mkdir(parents=True, exist_ok=True)
    for mode in modes:
        if mode.name not in WORKER_PRIORITY:
            raise ValueError("Unknown Unity source worktree mode")
        destination = cache / mode.name
        marker = cache / f".{mode.name}.owner.json"
        identity = {
            "SchemaVersion": 1,
            "Repository": str(repository.resolve()),
            "Worktree": str(destination),
        }
        if linked_path(destination) or linked_path(cache) or linked_path(marker):
            raise ValueError("Unity source worktree paths must not be links")
        if marker.exists():
            if (
                marker.stat().st_size > 4096
                or json.loads(marker.read_text(encoding="utf-8")) != identity
            ):
                raise ValueError("Unity source worktree ownership differs; preserving it")
        elif destination.exists():
            raise ValueError("Unity source worktree has no ownership receipt; preserving it")
        else:
            marker.write_text(json.dumps(identity), encoding="utf-8")
        if not destination.exists():
            git(
                repository,
                "worktree",
                "add",
                "--force",
                "--detach",
                str(destination),
                commit,
            )
        destination_common = Path(
            git(destination, "rev-parse", "--path-format=absolute", "--git-common-dir")
        ).resolve()
        top = Path(git(destination, "rev-parse", "--show-toplevel")).resolve()
        if (
            destination_common != common
            or destination.resolve() == repository.resolve()
            or top != destination.resolve()
            or not (destination / ".git").is_file()
        ):
            raise ValueError("Unity source worktree is not an owned checkout of this repository")
        git(destination, "checkout", "--detach", "--force", commit)
        git(destination, "clean", "-ffdx")
        isolated.append(Mode(mode.name, {**mode.parameters, "RepoRoot": str(destination)}))
    return isolated


def launch(
    mode: Mode,
    directory: Path,
    shell: str,
    environment: dict[str, str],
    stream: BinaryIO,
) -> Worker:
    config = directory / f"{mode.name}.json"
    config.write_text(json.dumps(mode.parameters), encoding="utf-8")
    child_environment = dict(environment, UH_MODE_CONFIG=str(config))
    if sys.platform == "linux":
        # Each concurrent PowerShell needs its own startup JIT profile (PowerShell#26528).
        if not child_environment.get("UV_CACHE_DIR"):
            cache_home = child_environment.get("XDG_CACHE_HOME") or str(Path.home() / ".cache")
            child_environment["UV_CACHE_DIR"] = str(Path(cache_home) / "uv")
        child_environment["XDG_CACHE_HOME"] = tempfile.mkdtemp(
            prefix=f"{mode.name}-startup-cache-", dir=directory
        )
    if mode.name != "standalone":
        child_environment.pop("UH_NATIVE_ALLOCATOR_DIAGNOSTICS", None)
    command = [
        shell,
        "-NoProfile",
        "-NonInteractive",
        "-OutputFormat",
        "Text",
        "-EncodedCommand",
        base64.b64encode(WORKER.encode("utf-16-le")).decode("ascii"),
    ]
    if sys.platform == "win32":
        command = [
            sys.executable,
            str(Path(__file__).with_name("desktop_worker.py")),
            "--shell",
            shell,
            "--encoded-worker",
            base64.b64encode(WORKER.encode("utf-16-le")).decode("ascii"),
        ]
    started = time.monotonic()
    process = subprocess.Popen(  # noqa: S603 - fixed PowerShell program; options travel as JSON data.
        command,
        env=child_environment,
        stdout=stream,
        stderr=subprocess.STDOUT,
        start_new_session=os.name != "nt",
    )
    return Worker(mode, process, started)


def stop(worker: Worker) -> None:
    """Reap a timed-out worker's process tree before central cleanup can run."""
    if worker.process.poll() is not None:
        return
    worker.forced = True
    if sys.platform == "win32":
        taskkill = Path(os.environ["SYSTEMROOT"]) / "System32/taskkill.exe"
        result = subprocess.run(  # noqa: S603 - OS tool and the PID of our still-owned child.
            [str(taskkill), "/PID", str(worker.process.pid), "/T", "/F"],
            capture_output=True,
            check=False,
            timeout=30,
        )
        if result.returncode and worker.process.poll() is None:
            raise RuntimeError("Could not stop a Unity mode worker tree")
    else:
        with contextlib.suppress(ProcessLookupError):
            os.killpg(worker.process.pid, signal.SIGKILL)
    worker.process.wait(timeout=30)


def wait(workers: list[Worker], deadline: float, refill: Callable[[], None] | None = None) -> None:
    last_report = 0.0
    while True:
        for worker in workers:
            if worker.process.poll() is not None and worker.ended is None:
                worker.ended = time.monotonic()
        if refill is not None:
            refill()
        running: list[str] = []
        for worker in workers:
            if worker.process.poll() is None:
                running.append(worker.mode.name)
            elif worker.ended is None:
                worker.ended = time.monotonic()
        if not running:
            return
        now = time.monotonic()
        if deadline <= now:
            raise TimeoutError("Unity mode deadline expired")
        if now - last_report >= 15:
            print(f"Unity modes running: {', '.join(running)}", flush=True)
            last_report = now
        time.sleep(min(0.1, max(0.0, deadline - now)))


def run(
    modes: list[Mode],
    preparation: Mode,
    *,
    private: Path,
    shell: str,
    environment: dict[str, str],
    timeout: float,
    receipt: Path,
    output: Path | None,
    started_at: float | None = None,
    max_workers: int = MAX_CONCURRENT_WORKERS,
) -> bool:
    """Activate once, run independent projects together, and retain every outcome."""
    if not modes or not math.isfinite(timeout) or timeout <= 0:
        raise ValueError("Select at least one mode and a positive timeout")
    if max_workers not in range(1, 6):
        raise ValueError("Select between one and five concurrent Unity workers")
    started = time.monotonic() if started_at is None else started_at
    workers: list[Worker] = []
    preparation_worker: Worker | None = None
    initializers: list[Worker] = []
    worker_logs: dict[str, BinaryIO] = {}
    completed = False
    try:
        with contextlib.ExitStack() as streams:
            preparation_log = streams.enter_context((private / "activation.log").open("wb"))
            preparation_worker = launch(preparation, private, shell, environment, preparation_log)
            wait([preparation_worker], started + min(timeout, 600))
            if preparation_worker.process.returncode != 0:
                raise RuntimeError(
                    "Unity license preparation failed; inspect the private activation log"
                )
            session_path = Path(str(preparation.parameters["LicenseSessionPath"]))
            session = cast(
                "dict[str, object]",
                json.loads(session_path.read_text(encoding="utf-8")),
            )
            if session.get("State") != "activated" or session.get("OwnerProcessId") != os.getpid():
                raise RuntimeError("Unity license preparation did not publish its owned session")
            for mode in modes:
                initialization = Mode(
                    "initialize-" + mode.name, {**mode.parameters, "GenerateOnly": True}
                )
                initialization_log = streams.enter_context(
                    (private / f"{initialization.name}.log").open("wb")
                )
                initializer = launch(
                    initialization, private, shell, environment, initialization_log
                )
                initializers.append(initializer)
                wait([initializer], started + timeout)
                if initializer.process.returncode != 0:
                    initialization_log.flush()
                    folder = Path(str(mode.parameters["ArtifactsPath"]))
                    folder.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(
                        private / f"{initialization.name}.log",
                        folder / "project-initialization.log",
                    )
                    raise RuntimeError(f"Unity project initialization failed for {mode.name}")
            pending = sorted(modes, key=lambda mode: WORKER_PRIORITY.index(mode.name))

            def fill_available_slots() -> None:
                while pending:
                    active = 0
                    for worker in workers:
                        if worker.process.poll() is None:
                            active += 1
                        elif worker.ended is None:
                            worker.ended = time.monotonic()
                    if active >= max_workers:
                        return
                    if started + timeout <= time.monotonic():
                        raise TimeoutError("Unity mode deadline expired before worker launch")
                    mode = pending.pop(0)
                    folder = Path(str(mode.parameters["ArtifactsPath"]))
                    folder.mkdir(parents=True, exist_ok=True)
                    stream = streams.enter_context((folder / "mode-controller.log").open("wb"))
                    worker_logs[mode.name] = stream
                    workers.append(launch(mode, private, shell, environment, stream))

            wait(workers, started + timeout, fill_available_slots)
            for worker in workers:
                if worker.mode.name == "editmode_isolated" and worker.process.returncode == 0:
                    folder = Path(str(worker.mode.parameters["ArtifactsPath"]))
                    for name in ("compilation", "migration"):
                        destination = folder.parent / name
                        if destination.exists() or not (folder / name).is_dir():
                            raise RuntimeError("Isolated EditMode evidence is missing or conflicts")
                        shutil.move(str(folder / name), destination)
            shard = next(
                (worker for worker in workers if worker.mode.name == "editmode_shard"),
                None,
            )
            if shard is not None:
                primary = next(worker for worker in workers if worker.mode.name == "editmode")
                folder = Path(str(primary.mode.parameters["ArtifactsPath"]))
                secondary_folder = Path(str(shard.mode.parameters["ArtifactsPath"]))
                retained_folder = folder / "shards/1"
                if (
                    retained_folder.exists()
                    or linked_path(retained_folder)
                    or linked_path(retained_folder.parent)
                    or linked_path(secondary_folder)
                    or not secondary_folder.is_dir()
                ):
                    raise RuntimeError("EditMode shard evidence is missing or conflicts")
                retained_folder.parent.mkdir(parents=True, exist_ok=True)
                worker_logs[shard.mode.name].close()
                shutil.move(str(secondary_folder), retained_folder)
                if all(
                    worker.process.returncode == 0 and not worker.forced
                    for worker in (primary, shard)
                ):
                    retained = folder / "shards/main-results.xml"
                    retained.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(folder / "results.xml", retained)
                    secondary = retained_folder / "results.xml"
                    selected_assemblies = {
                        name
                        for worker in (primary, shard)
                        for name in str(worker.mode.parameters["AssemblyNames"]).split(";")
                    }
                    merge_results(
                        [retained, secondary],
                        folder / "results.xml",
                        selected_assemblies=selected_assemblies,
                    )
            completed = True
    finally:
        cleanup_failed = False
        for worker in (
            initializers
            + ([preparation_worker] if preparation_worker is not None else [])
            + workers
        ):
            try:
                stop(worker)
            except (OSError, RuntimeError, subprocess.TimeoutExpired):
                cleanup_failed = True
            if worker.ended is None:
                worker.ended = time.monotonic()
        outcomes = {
            "editmode"
            if mode.name in ("editmode_isolated", "editmode_shard")
            else mode.name: "failure"
            for mode in modes
        }
        if completed and not cleanup_failed:
            for name in outcomes:
                required = [
                    worker
                    for worker in workers
                    if (
                        "editmode"
                        if worker.mode.name in ("editmode_isolated", "editmode_shard")
                        else worker.mode.name
                    )
                    == name
                ]
                if required and all(
                    worker.process.returncode == 0 and not worker.forced for worker in required
                ):
                    outcomes[name] = "success"
        receipt.parent.mkdir(parents=True, exist_ok=True)
        receipt.write_text(
            json.dumps(
                {
                    "SchemaVersion": 1,
                    "ElapsedSeconds": time.monotonic() - started,
                    "Outcomes": outcomes,
                    "CleanupFailed": cleanup_failed,
                    "ControllerCompleted": completed,
                    "Workers": [
                        {
                            "Mode": worker.mode.name,
                            "ProcessId": worker.process.pid,
                            "ExitCode": worker.process.returncode,
                            "Forced": worker.forced,
                            "StartedAfterSeconds": worker.started - started,
                            "EndedAfterSeconds": cast("float", worker.ended) - started,
                            "ElapsedSeconds": cast("float", worker.ended) - worker.started,
                        }
                        for worker in workers
                    ],
                },
                indent=2,
            )
            + "\n",
            encoding="utf-8",
        )
        if output is not None:
            with output.open("a", encoding="utf-8", newline="\n") as output_stream:
                for name, outcome in outcomes.items():
                    output_stream.write(f"{name}={outcome}\n")
        if not completed:
            activation_log = private / "activation.log"
            if activation_log.exists():
                retained_log = private.parent / f"{private.name}-activation.log"
                shutil.copyfile(activation_log, retained_log)
                print(
                    f"Private activation diagnostics retained at {retained_log}",
                    flush=True,
                )
        print(f"Unity mode outcomes: {json.dumps(outcomes, sort_keys=True)}", flush=True)
    return all(value == "success" for value in outcomes.values())


def cancel_signal(_number: int, _frame: FrameType | None) -> None:
    raise KeyboardInterrupt("Unity mode coordinator cancelled")


@contextlib.contextmanager
def cancellation_signals() -> Generator[None, None, None]:
    signals = [signal.SIGTERM]
    if sys.platform == "win32":
        signals.append(signal.SIGBREAK)
    previous = {number: signal.getsignal(number) for number in signals}
    try:
        for number in signals:
            signal.signal(number, cancel_signal)
        yield
    finally:
        for number, handler in previous.items():
            signal.signal(number, handler)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--single-threaded", action="store_true")
    parser.add_argument("--editmode-shards", type=int, choices=(1, 2), default=1)
    parser.add_argument(
        "--max-workers", type=int, choices=range(1, 6), default=MAX_CONCURRENT_WORKERS
    )
    parser.add_argument("--timeout-seconds", type=float, default=1800)
    parser.add_argument("--artifacts-root", type=Path, default=ROOT / ".artifacts/unity")
    args = parser.parse_args(argv)
    controller_started = time.monotonic()
    try:
        selected, profiles = read_selection(
            os.environ["UH_PARALLEL_SELECTED_MODES"],
            os.environ["UH_PARALLEL_ASSEMBLY_PROFILES"],
        )
        shell = shutil.which("pwsh")
        if shell is None:
            raise RuntimeError("PowerShell 7 is required")
        environment = dict(os.environ, UH_UNITY_SESSION_OWNER_PID=str(os.getpid()))
        environment["UH_MODE_RUNNER"] = str(ROOT / "scripts/unity/run-ci-tests.ps1")
        temporary_root = Path(environment["RUNNER_TEMP"])
        with (
            cancellation_signals(),
            tempfile.TemporaryDirectory(prefix="unity-mode-session-", dir=temporary_root) as name,
        ):
            private = Path(name)
            session = private / "session.json"
            session.write_text(
                json.dumps(
                    {
                        "SchemaVersion": 1,
                        "State": "requested",
                        "OwnerProcessId": os.getpid(),
                        "UnityVersion": args.version,
                        "EditorPath": environment["UNITY_EDITOR_PATH"],
                        "Identity": {
                            key: environment.get(key)
                            for key in (
                                "GITHUB_RUN_ID",
                                "GITHUB_RUN_ATTEMPT",
                                "GITHUB_JOB",
                            )
                        },
                    }
                ),
                encoding="utf-8",
            )
            artifacts: Path = args.artifacts_root.absolute()
            modes = build_modes(
                args.version,
                selected,
                profiles,
                repository=ROOT,
                workspace=Path(environment["RUNNER_WORKSPACE"]) / "unity-workspace",
                artifacts=artifacts,
                session=session,
                single_threaded=args.single_threaded,
                editmode_shards=args.editmode_shards,
            )
            source_scope = args.version + ("-st" if args.single_threaded else "")
            modes = isolate_sources(
                modes,
                ROOT,
                Path(environment["RUNNER_WORKSPACE"]) / ".uh-src" / source_scope,
            )
            preparation_parameters = dict(modes[0].parameters)
            preparation_parameters["PrepareLicenseOnly"] = True
            preparation_parameters.pop("ProjectRootMinimumFreeGb", None)
            suffix = "-single-threaded" if args.single_threaded else ""
            success = run(
                modes,
                Mode("prepare", preparation_parameters),
                private=private,
                shell=shell,
                environment=environment,
                timeout=args.timeout_seconds,
                receipt=artifacts / f"{args.version}-editmode{suffix}" / "mode-timings.json",
                output=Path(environment["GITHUB_OUTPUT"])
                if environment.get("GITHUB_OUTPUT")
                else None,
                started_at=controller_started,
                max_workers=args.max_workers,
            )
    except KeyboardInterrupt:
        print(
            "Unity mode coordinator cancelled after stopping its workers.",
            file=sys.stderr,
        )
        return 130
    except (
        KeyError,
        OSError,
        RuntimeError,
        ValueError,
        subprocess.TimeoutExpired,
    ) as error:
        print(f"Cannot run Unity modes: {error}", file=sys.stderr)
        return 1
    return int(not success)


if __name__ == "__main__":
    sys.exit(main())
