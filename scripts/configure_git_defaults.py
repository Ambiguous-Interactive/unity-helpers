"""Apply local Git push defaults while preserving both legacy caller profiles."""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
from dataclasses import dataclass, field
from pathlib import Path

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
MARKERS = {"success": "✓", "warning": "⚠", "error": "✗", "info": "\N{INFORMATION SOURCE}"}
EXPECTED = (("push.autoSetupRemote", "true"), ("push.default", "simple"))


@dataclass
class Result:
    success: bool = False
    errors: list[str] = field(default_factory=lambda: list[str]())
    values: dict[str, str] = field(
        default_factory=lambda: dict.fromkeys((key for key, _ in EXPECTED), "")
    )
    messages: list[tuple[str, str]] = field(default_factory=lambda: list[tuple[str, str]]())
    exit_code: int = 1
    last_git_exit_code: int | None = None

    def legacy_result(self) -> dict[str, object]:
        return {"Success": self.success, "Errors": self.errors, "Values": self.values}


def run_git(arguments: list[str]) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(  # noqa: S603 - resolved Git, fixed commands and argument arrays.
        arguments, capture_output=True, check=False, timeout=30
    )


def render(messages: list[tuple[str, str]], profile: str) -> list[str]:
    lines: list[str] = []
    for level, text in messages:
        if level == "value":
            lines.append(text)
        elif profile == "bash":
            lines.append(f"{MARKERS[level]} {text}")
        else:
            prefix = {"error": "ERROR: ", "warning": "WARNING: "}.get(level, "")
            lines.append(f"[git-push-defaults] {prefix}{text}")
    return lines


def old_git_version(output: bytes, profile: str) -> str | None:
    text = output.decode("utf-8", errors="replace").strip()
    if profile == "bash":
        words = text.split()
        raw = words[2] if len(words) > 2 else ""
        parts = raw.split(".")
        numbers: list[int] = []
        for index in (0, 1):
            match = re.match(r"^[+-]?[0-9]+", parts[index] if len(parts) > index else "")
            numbers.append(int(match[0]) if match else 0)
    else:
        raw = re.sub(r"^git version ", "", text)
        parts = raw.split(".")
        if len(parts) < 2:
            return None
        numbers = [int(re.sub(r"[^0-9]", "", part) or "0") for part in parts[:2]]
        if any(number > 2147483647 for number in numbers):
            return None
    return raw if numbers[0] < 2 or (numbers[0] == 2 and numbers[1] < 37) else None


def configure(root: str, profile: str) -> Result:
    result = Result()

    def fail(message: str, *, display: bool = True, status: int = 1) -> Result:
        result.errors.append(message)
        if display:
            result.messages.append(("error", message))
        result.exit_code = status
        return result

    executable = shutil.which("git")
    if executable is None:
        return fail("git is not installed or not on PATH.")
    if profile not in ("bash", "powershell"):
        return fail("Unknown Git push-default profile.")
    if profile == "powershell":
        if not root.strip():
            return fail("RepoRoot is required and must be non-empty.")
        if not Path(root).is_dir():
            return fail(f"RepoRoot does not exist or is not a directory: {root}")

    def execute(*arguments: str, scoped: bool = True) -> subprocess.CompletedProcess[bytes]:
        command = [executable, *(["-C", root] if scoped else []), *arguments]
        observation = run_git(command)
        result.last_git_exit_code = observation.returncode
        return observation

    try:
        if execute("rev-parse", "--git-dir").returncode:
            return fail(f"Not a git repository: {root}")
        version = execute("--version", scoped=False)
        if version.returncode and profile == "bash":
            return fail("git --version failed.", display=False, status=version.returncode)
        old = old_git_version(version.stdout, profile)
        if old is not None:
            result.messages.append(
                (
                    "warning",
                    f"git {old} detected. push.autoSetupRemote requires git >= 2.37; older clients will silently ignore it.",
                )
            )
        result.messages.append(("info", f"Configuring git push defaults (local only) in {root}"))
        for key, expected in EXPECTED:
            written = execute("config", "--local", key, expected)
            if written.returncode:
                return fail(
                    f"Failed to set {key}",
                    display=profile == "powershell",
                    status=1 if profile == "powershell" else written.returncode,
                )
            result.messages.append(("success", f"{key} = {expected}"))
        for key, _ in EXPECTED:
            observed = execute("config", "--local", "--get", key)
            text = (
                observed.stdout.decode("utf-8", errors="replace")
                if observed.returncode == 0
                else ""
            )
            normalized = (
                " ".join(text.splitlines()) if profile == "powershell" else text.replace("\r", "")
            )
            result.values[key] = normalized.strip()
        result.messages.extend(("value", f"{key}={result.values[key]}") for key, _ in EXPECTED)
        values = (
            result.values
            if profile == "bash"
            else {key: value.lower() for key, value in result.values.items()}
        )
        if any(values[key] != expected for key, expected in EXPECTED):
            return fail(
                f"Post-configure verification failed (push.autoSetupRemote={result.values['push.autoSetupRemote']} push.default={result.values['push.default']})"
            )
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        return fail(f"Could not configure local Git push defaults: {error}")
    result.success = True
    result.exit_code = 0
    return result


def main() -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", default=str(ROOT))
    parser.add_argument("--profile", choices=("bash", "powershell"), default="bash")
    parser.add_argument("--result-json", action="store_true")
    args = parser.parse_args()
    result = configure(args.repo_root, args.profile)
    if args.result_json:
        print(
            json.dumps(
                {
                    "result": result.legacy_result(),
                    "messages": result.messages,
                    "last_git_exit_code": result.last_git_exit_code,
                },
                ensure_ascii=True,
            )
        )
    else:
        for message in render(result.messages, args.profile):
            print(message)
    return result.exit_code


if __name__ == "__main__":
    raise SystemExit(main())
