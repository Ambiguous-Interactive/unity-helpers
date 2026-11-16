"""Read the repository's Git push defaults, hook artifacts and container helper state."""

from __future__ import annotations

import argparse
import shutil
import subprocess
from pathlib import Path

from cli_encoding import configure_output

PREFIX = "[validate-git-push-config]"
EXTENSIONS = ("txt", "log", "out", "err", "tmp")
EXPECTED = (("push.autoSetupRemote", "true"), ("push.default", "simple"))


def run_git(executable: str, root: Path, *arguments: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(  # noqa: S603 - resolved Git and literal arguments, no shell.
        [executable, "-C", str(root), *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
        timeout=30,
    )


def scan_artifacts(root: Path) -> list[Path]:
    directory = root / ".githooks"
    if not directory.is_dir():
        return []
    names: set[str] = set()
    for path in directory.iterdir():
        if (
            path.name.startswith(".")
            or not path.is_file()
            or path.name.lower().endswith(".sample")
            or path.suffix.lower().lstrip(".") in EXTENSIONS
        ):
            continue
        names.add(path.stem if "." in path.name else path.name)
    return sorted(
        {
            path
            for name in sorted(names, key=lambda name: (name.lower(), name))
            for extension in EXTENSIONS
            for path in (root / f"{name}.{extension}", directory / f"{name}.{extension}")
            if path.is_file()
        },
        key=lambda path: (str(path).lower(), str(path)),
    )


def validate(root: Path) -> tuple[int, list[str]]:
    executable = shutil.which("git")
    if executable is None:
        return 1, [f"{PREFIX} ERROR: git is required."]
    lines: list[str] = []
    failures = 0
    try:
        mismatches: list[str] = []
        for key, expected in EXPECTED:
            result = run_git(executable, root, "config", "--local", "--get", key)
            actual = result.stdout.strip() if result.returncode == 0 else ""
            if actual.lower() != expected:
                mismatches.append(f"  {key} is '{actual or 'unset'}' (expected '{expected}')")
        if mismatches:
            lines.extend(
                [
                    f"{PREFIX} ERROR: Git push defaults are not configured for this repository:",
                    *mismatches,
                    "Run: npm run agent:preflight:fix",
                ]
            )
            failures += 1
        artifacts = scan_artifacts(root)
        if artifacts:
            ignored: list[Path] = []
            unignored: list[Path] = []
            errors: list[str] = []
            for path in artifacts:
                relative = path.relative_to(root).as_posix()
                result = run_git(executable, root, "check-ignore", "-q", "--", relative)
                if result.returncode == 0:
                    ignored.append(path)
                else:
                    unignored.append(path)
                    if result.returncode != 1:
                        errors.append(f"{path}: git check-ignore exit {result.returncode}")
            lines.append(
                f"{PREFIX} ERROR: Stray git-hook artifact file(s) detected (likely redirected hook output):"
            )
            if ignored:
                lines.extend(
                    [
                        "  gitignored (safe to auto-delete via agent:preflight:fix):",
                        *(f"    {path}" for path in ignored),
                    ]
                )
            if unignored:
                lines.extend(
                    [
                        "  NOT gitignored (manual review required):",
                        *(f"    {path}" for path in unignored),
                    ]
                )
            if errors:
                lines.extend(["  git check-ignore errors:", *(f"    {value}" for value in errors)])
            if ignored:
                lines.append("For gitignored files: run npm run agent:preflight:fix.")
            if unignored:
                lines.append(
                    "For files not gitignored: delete manually if stale, or add a .gitignore entry and re-run (auto-delete is refused for safety)."
                )
            failures += 1
        checker = root / "scripts/check-container-git-credentials.sh"
        if checker.is_file():
            result = run_git(executable, root, "config", "--get-all", "credential.helper")
            if result.returncode not in (0, 1):
                lines.append(
                    f"{PREFIX} ERROR: git config credential.helper failed with exit {result.returncode}."
                )
                failures += 1
            helpers = result.stdout.splitlines() if result.returncode == 0 else []
            if any(
                "vscode-remote-containers" in helper.lower()
                or "git-credential-helper" in helper.lower()
                for helper in helpers
            ):
                bash = shutil.which("bash")
                if bash is None:
                    lines.extend(
                        [
                            f"{PREFIX} ERROR: The Dev Containers credential helper is registered but bash is unavailable to verify the override.",
                            f"Run manually: bash {checker}",
                        ]
                    )
                    failures += 1
                else:
                    check = subprocess.run(  # noqa: S603 - resolved Bash and owned read-only checker.
                        [bash, str(checker), "--quiet"],
                        cwd=root,
                        stdout=subprocess.PIPE,
                        stderr=subprocess.STDOUT,
                        text=True,
                        encoding="utf-8",
                        errors="replace",
                        check=False,
                        timeout=30,
                    )
                    if check.returncode != 0:
                        lines.extend(
                            [
                                f"{PREFIX} ERROR: github.com does not resolve through the cached-token credential helper:",
                                *(f"  {line}" for line in check.stdout.splitlines()),
                            ]
                        )
                        failures += 1
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        lines.append(f"{PREFIX} ERROR: Cannot complete read-only Git push checks: {error}")
        failures += 1
    if failures:
        lines.append(f"{PREFIX} Failed with {failures} check(s) reporting issues.")
    else:
        lines.append(f"{PREFIX} Git push config and hook artifact checks passed.")
    return int(bool(failures)), lines


def main() -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    status, lines = validate(args.repo_root.absolute())
    for line in lines:
        print(line)
    return status


if __name__ == "__main__":
    raise SystemExit(main())
