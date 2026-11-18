"""Verify the native tools available to a developer or dev container."""

from __future__ import annotations

import os
import selectors
import shutil
import signal
import subprocess
import sys
import time
from contextlib import suppress
from dataclasses import dataclass
from enum import Enum
from pathlib import Path
from typing import Literal

from cli_encoding import configure_output
from windows_version_probe import MAXIMUM_VERSION_BYTES, STATUS_BYTES

RED = "\x1b[0;31m"
GREEN = "\x1b[0;32m"
YELLOW = "\x1b[1;33m"
RESET = "\x1b[0m"
DIVIDER = "========================================"
VERSION = "--version"
TIMEOUT_SECONDS = 30


@dataclass(frozen=True)
class Tool:
    label: str
    command: str
    source: Literal["system", "npm", "dotnet"] = "system"
    container_only: bool = False


GROUPS = (
    (
        "CI/CD Linting Tools (container-only)",
        tuple(
            Tool(name, name, container_only=True)
            for name in ("actionlint", "shellcheck", "yamllint", "lychee")
        ),
    ),
    (
        "npm-based Tools (via package.json)",
        tuple(Tool(name, name, "npm") for name in ("markdownlint", "prettier", "cspell")),
    ),
    (".NET Tools", (Tool("csharpier", "csharpier", "dotnet"),)),
    (
        "Core Development Tools",
        tuple(
            Tool(name, name) for name in ("node", "npm", "dotnet", "pwsh", "python3", "git", "gh")
        ),
    ),
    (
        "AI CLI Tools (container-only)",
        tuple(Tool(name, name, container_only=True) for name in ("codex", "opencode", "nanocoder")),
    ),
    (
        "High-Performance CLI Tools (container-only)",
        (
            Tool("ripgrep (rg)", "rg", container_only=True),
            *(
                Tool(name, name, container_only=True)
                for name in ("fd", "bat", "fzf", "delta", "eza", "zoxide", "yq", "jq")
            ),
        ),
    ),
)


@dataclass(frozen=True)
class Version:
    status: int
    first_line: str
    timed_out: bool = False


class Outcome(Enum):
    PASSED = "passed"
    REQUIRED_FAILURE = "required_failure"
    OPTIONAL_MISSING = "optional_missing"


def _stop_probe(process: subprocess.Popen[bytes]) -> None:
    with suppress(ProcessLookupError):
        if sys.platform == "win32":
            process.kill()
        else:
            os.killpg(process.pid, signal.SIGKILL)
    process.wait()


def _version_from_output(status: int, captured: bytes) -> Version:
    first_line, newline, _ = captured.partition(b"\n")
    if newline:
        first_line = first_line.removesuffix(b"\r")
    return Version(status, first_line[:MAXIMUM_VERSION_BYTES].decode("utf-8", "replace"))


if sys.platform == "win32":

    def _probe_windows(executable: str, args: tuple[str, ...]) -> Version | None:
        worker = Path(__file__).with_name("windows_version_probe.py")
        # Skip the venv redirector so the process handle belongs to the job owner.
        python = Path(sys.base_prefix) / "python.exe"
        with subprocess.Popen(  # noqa: S603 - start the sibling worker with a resolved executable.
            [str(python), "-I", str(worker), executable, *args],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            creationflags=subprocess.CREATE_NEW_PROCESS_GROUP,
        ) as process:
            try:
                captured, diagnostic = process.communicate(timeout=TIMEOUT_SECONDS)
            except (KeyboardInterrupt, OSError, subprocess.TimeoutExpired) as error:
                _stop_probe(process)
                if isinstance(error, subprocess.TimeoutExpired):
                    return Version(124, "", timed_out=True)
                raise
            if process.returncode or len(captured) < STATUS_BYTES:
                if diagnostic:
                    print(diagnostic.decode("utf-8", "replace").rstrip(), file=sys.stderr)
                return None
            status = int.from_bytes(captured[:STATUS_BYTES], "little")
            return _version_from_output(status, captured[STATUS_BYTES:])


def _read_probe_pipe(process: subprocess.Popen[bytes], deadline: float) -> bytes:
    pipe = process.stdout
    if pipe is None:
        return b""
    descriptor = pipe.fileno()
    first_line = bytearray()
    line_finished = False
    with selectors.DefaultSelector() as selector:
        selector.register(descriptor, selectors.EVENT_READ)
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0 or not selector.select(remaining):
                raise subprocess.TimeoutExpired(process.args, TIMEOUT_SECONDS)
            chunk = os.read(descriptor, MAXIMUM_VERSION_BYTES)
            if not chunk:
                return bytes(first_line)
            space = MAXIMUM_VERSION_BYTES + 2 - len(first_line)
            if not line_finished and space > 0:
                newline = chunk.find(b"\n")
                line_finished = newline >= 0
                first_line.extend(chunk[: min(space, newline + 1) if line_finished else space])


def _probe_posix(executable: str, args: tuple[str, ...]) -> Version:
    with subprocess.Popen(  # noqa: S603 - resolved executable with explicit version arguments.
        [executable, *args],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        start_new_session=True,
    ) as process:
        deadline = time.monotonic() + TIMEOUT_SECONDS
        try:
            captured = _read_probe_pipe(process, deadline)
            process.wait(timeout=max(0.0, deadline - time.monotonic()))
        except (KeyboardInterrupt, OSError, subprocess.TimeoutExpired) as error:
            _stop_probe(process)
            if isinstance(error, subprocess.TimeoutExpired):
                return Version(124, "", timed_out=True)
            raise
        return _version_from_output(process.returncode, captured)


if sys.platform == "win32":
    _native_probe = _probe_windows
else:
    _native_probe = _probe_posix


def probe(command: str, *args: str) -> Version | None:
    executable = shutil.which(command)
    if executable is None:
        return None
    try:
        return _native_probe(executable, args)
    except OSError:
        return None


def check(tool: Tool, *, inside_container: bool) -> tuple[str, Outcome]:
    if tool.source == "npm":
        result = probe("npx", "--no-install", tool.command, VERSION)
        if result is not None and result.status == 0:
            return f"{GREEN}✓{RESET} {result.first_line} (via npx)", Outcome.PASSED
        if result is not None and result.status == 124:
            return (
                f"{RED}✗ TIMED OUT - npx {tool.command} {VERSION}{RESET}",
                Outcome.REQUIRED_FAILURE,
            )
        return f"{RED}✗ NOT FOUND - run 'npm install'{RESET}", Outcome.REQUIRED_FAILURE
    if tool.source == "dotnet":
        result = probe("dotnet", "tool", "run", tool.command, VERSION)
        if result is not None and result.status == 0:
            return f"{GREEN}✓{RESET} {result.first_line} (via dotnet tool)", Outcome.PASSED
        result = probe(tool.command, VERSION)
        if result is not None and result.timed_out:
            return f"{RED}✗ TIMED OUT - {tool.command} {VERSION}{RESET}", Outcome.REQUIRED_FAILURE
        if result is None:
            return f"{RED}✗ NOT FOUND - run 'dotnet tool restore'{RESET}", Outcome.REQUIRED_FAILURE
        return f"{GREEN}✓{RESET} {result.first_line} (global)", Outcome.PASSED
    result = probe(tool.command, VERSION)
    if result is not None and not result.timed_out:
        return f"{GREEN}✓{RESET} {result.first_line}", Outcome.PASSED
    if result is not None and result.timed_out:
        outcome = (
            Outcome.OPTIONAL_MISSING
            if tool.container_only and not inside_container
            else Outcome.REQUIRED_FAILURE
        )
        return f"{RED}✗ TIMED OUT - {tool.command} {VERSION}{RESET}", outcome
    if tool.container_only and not inside_container:
        return f"{YELLOW}⚠ not installed (optional){RESET}", Outcome.OPTIONAL_MISSING
    return f"{RED}✗ NOT FOUND (REQUIRED){RESET}", Outcome.REQUIRED_FAILURE


def verify(*, inside_container: bool) -> int:
    mode = (
        f"{GREEN}Dev Container{RESET}"
        if inside_container
        else f"{YELLOW}Host Machine{RESET} (some tools optional)"
    )
    print(
        f"{DIVIDER}\nDev Container Tool Verification\nEnvironment: {mode}\n{DIVIDER}\n", flush=True
    )
    warnings = 0
    for title, tools in GROUPS:
        print(f"=== {title} ===", flush=True)
        for tool in tools:
            message, status = check(tool, inside_container=inside_container)
            print(f"{tool.label + ':':<20}{message}", flush=True)
            if status is Outcome.REQUIRED_FAILURE:
                return 1
            warnings += status is Outcome.OPTIONAL_MISSING
        print()
    print(DIVIDER)
    if warnings:
        print(f"{GREEN}All required tools are installed!{RESET}")
        print(f"{YELLOW}{warnings} optional tool(s) not installed.{RESET}")
    else:
        print(f"{GREEN}All tools are installed and ready!{RESET}")
    print(DIVIDER)
    return 0


def main() -> int:
    configure_output()
    original_path = os.environ.pop("UH_VERIFY_NATIVE_PATH", None)
    if original_path is not None:
        os.environ["PATH"] = original_path
    inside_container = bool(
        Path("/.dockerenv").is_file()
        or os.environ.get("REMOTE_CONTAINERS")
        or os.environ.get("CODESPACES")
    )
    return verify(inside_container=inside_container)


if __name__ == "__main__":
    raise SystemExit(main())
