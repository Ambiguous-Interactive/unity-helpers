"""Validate the pinned download URLs in the devcontainer Dockerfile."""

from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass
from io import TextIOWrapper
from pathlib import Path

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_DOCKERFILE = ".devcontainer/Dockerfile"
RED = "\033[0;31m"
GREEN = "\033[0;32m"
YELLOW = "\033[1;33m"
RESET = "\033[0m"
RULE = "=" * 40
VERSION = re.compile(r'([A-Z_]+)_VERSION="([^"]+)"')
ASSET = re.compile(
    r'curl\s+-[a-zA-Z]*\s+"(https://(?:github\.com|builds\.dotnet\.microsoft\.com)/[^"]+)"'
)
APT_POWERSHELL = re.compile(r"apt(?:-get)?\s+install[^&;|]*(?<!\w)powershell(?!\w)")


@dataclass(frozen=True)
class Tool:
    name: str
    version: str
    amd64: str
    arm64: str
    template: str

    def url(self, architecture: str) -> str:
        return self.template.replace("{VERSION}", self.version).replace(
            "{ARCH}", architecture if architecture != "NONE" else ""
        )


def apt_installs(lines: list[str]) -> list[str]:
    rejected: list[str] = []
    command: list[str] = []
    start = 0
    for number, line in enumerate(lines, 1):
        if line.lstrip().startswith("#"):
            continue
        if not command:
            if re.match(r"^\s*RUN\s+", line) is None:
                continue
            start = number
        command.append(line)
        if not line.rstrip().endswith("\\"):
            check_apt_command(command, start, rejected)
            command.clear()
    if command:
        check_apt_command(command, start, rejected)
    return rejected


def check_apt_command(command: list[str], start: int, rejected: list[str]) -> None:
    text = " ".join(command).strip()
    if APT_POWERSHELL.search(text.lower()) is not None:
        rejected.append(f"{start}:{text}")


def extract_tools(lines: list[str]) -> list[Tool]:
    tools: list[Tool] = []
    name = version = amd64 = arm64 = ""
    for line in lines:
        assignment = VERSION.search(line)
        if assignment:
            name, version = assignment.groups()
            amd64 = arm64 = "NONE"
        if not name:
            continue
        amd64_match = re.search(r'amd64\) +[A-Z_]+="([^"]+)"', line)
        arm64_match = re.search(r'arm64\) +[A-Z_]+="([^"]+)"', line)
        if amd64_match:
            amd64 = amd64_match[1]
        if arm64_match:
            arm64 = arm64_match[1]
        asset_match = ASSET.search(line)
        if asset_match:
            template = re.sub(r"\$\{[A-Z_]+_VERSION\}", "{VERSION}", asset_match[1])
            template = re.sub(r"\$\{[A-Z_]+_ARCH\}", "{ARCH}", template)
            tools.append(Tool(name.lower(), version, amd64, arm64, template))
            name = ""
    return tools


def run_curl(arguments: list[str], *, suppress_errors: bool = False) -> tuple[int, str]:
    executable = shutil.which("curl")
    if executable is None:
        if not suppress_errors:
            print("curl is required to probe download URLs.", file=sys.stderr)
        return 127, ""
    try:
        result = subprocess.run(  # noqa: S603 - resolved curl executable and explicit arguments.
            [executable, *arguments],
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL if suppress_errors else None,
            encoding="utf-8",
            errors="replace",
            timeout=35,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        if not suppress_errors:
            print(f"curl: {error}", file=sys.stderr)
        return 1, ""
    return result.returncode, result.stdout.rstrip("\n")


def supports_bounded_get() -> bool:
    exit_code, output = run_curl(["--version"])
    version = re.match(r"^curl\s+([0-9]+)\.([0-9]+)\.", output)
    return exit_code == 0 and version is not None and (int(version[1]), int(version[2])) >= (8, 4)


def check_url(label: str, url: str) -> bool:
    arguments = ["-o", os.devnull, "-w", "%{http_code}", "-L"]
    for attempt in (1, 2, 3):
        exit_code, status = run_curl(
            ["-sI", *arguments, "--max-time", "30", url], suppress_errors=True
        )
        if exit_code != 0:
            status = "000"
        if status == "200":
            break
        if attempt < 3:
            time.sleep(attempt * 2)

    if status != "200":
        if supports_bounded_get():
            get_exit, get_status = run_curl(
                ["-s", *arguments, "--range", "0-0", "--max-filesize", "1", "--max-time", "30", url]
            )
            print(f"  HEAD HTTP {status}; bounded GET HTTP {get_status} (curl exit {get_exit})")
            if get_exit in (0, 63) and get_status in ("200", "206"):
                status = "200"
        else:
            print(
                "  Bounded GET fallback requires curl 8.4.0 or newer; retaining the failed HEAD result."
            )

    padded_label = f"[{label}]".ljust(8)
    if status == "200":
        print(f"  {GREEN}✓{RESET} {padded_label} {url}")
        return True
    print(f"  {RED}✗{RESET} {padded_label} HTTP {status}  {url}")
    return False


def validate_downloads(tools: list[Tool]) -> int:
    passed = 0
    print(f"Found {len(tools)} tools to validate\n")
    for tool in tools:
        print(f"{YELLOW}{tool.name}{RESET} v{tool.version}")
        passed += check_url("amd64", tool.url(tool.amd64))
        passed += check_url("arm64", tool.url(tool.arm64))
        print()
    checked = len(tools) * 2
    failed = checked - passed
    print(f"{RULE}\nResults: {passed} passed, {failed} failed ({checked} total)\n{RULE}")
    if failed:
        print(f"{RED}{failed} URL(s) returned non-200 status!{RESET}")
        print(
            "\nCommon causes:\n"
            "  - Wrong libc suffix (gnu vs musl) — check release assets\n"
            "  - Version yanked or doesn't exist\n"
            "  - Archive naming convention changed in new release\n"
            "  - arm64 builds not published for this version"
        )
        return 1
    print(f"{GREEN}All download URLs are valid!{RESET}")
    return 0


def parse_arguments(arguments: list[str]) -> tuple[str, bool, bool] | None:
    dockerfile = DEFAULT_DOCKERFILE
    contracts_only = list_tools = False
    remaining = iter(arguments)
    for flag in remaining:
        if flag == "--dockerfile":
            selected = next(remaining, None)
            if selected is None:
                return None
            dockerfile = selected
        elif flag == "--contracts-only":
            contracts_only = True
        elif flag == "--list-tools":
            list_tools = True
        else:
            return None
    return dockerfile, contracts_only, list_tools


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    stdout = sys.stdout
    if isinstance(stdout, TextIOWrapper):
        stdout.reconfigure(line_buffering=True)
    settings = parse_arguments(sys.argv[1:] if arguments is None else arguments)
    if settings is None:
        program = os.environ.get("UH_DEVCONTAINER_URL_PROGRAM", sys.argv[0])
        print(
            f"Usage: {program} [--dockerfile <path>] [--contracts-only] [--list-tools]",
            file=sys.stderr,
        )
        return 1
    dockerfile, contracts_only, list_tools = settings
    path = ROOT / dockerfile
    if not path.is_file():
        print(f"Error: Dockerfile not found at {path}", file=sys.stderr)
        return 1
    print(f"{RULE}\nDevcontainer Download URL Validation\nDockerfile: {dockerfile}\n{RULE}\n")
    try:
        with path.open(encoding="utf-8", newline="") as stream:
            contents = stream.read()
    except (OSError, UnicodeError) as error:
        print(f"Error: Cannot read Dockerfile at {path}: {error}", file=sys.stderr)
        return 1
    lines = contents.split("\n")
    rejected = apt_installs(lines)
    if rejected:
        print(f"{RED}Detected apt-based PowerShell install in Dockerfile:{RESET}", file=sys.stderr)
        print("\n".join(rejected), file=sys.stderr)
        print(
            "Use PowerShell release tarballs with TARGETARCH mapping (amd64=x64, arm64=arm64).",
            file=sys.stderr,
        )
        return 1
    if contracts_only:
        print("Contract checks passed (no URL probing requested).")
        return 0
    # The original read loop processes only complete physical lines.
    tools = extract_tools(lines[:-1])
    if not tools:
        print("Warning: No tool definitions found in Dockerfile", file=sys.stderr)
        return 1
    if list_tools:
        print(f"Found {len(tools)} tools")
        for tool in tools:
            print(f"{tool.name}|{tool.version}|{tool.amd64}|{tool.arm64}|{tool.template}")
        return 0
    return validate_downloads(tools)


if __name__ == "__main__":
    raise SystemExit(main())
