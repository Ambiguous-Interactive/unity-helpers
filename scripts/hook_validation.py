"""Validate the required hook text through the locked Python toolchain."""

from __future__ import annotations

import argparse
import codecs
import sys
from dataclasses import dataclass
from io import TextIOWrapper
from pathlib import Path

from cli_encoding import configure_output

RULES = (
    (
        "pre-commit.ps1",
        ("scripts/sync-banner-version.ps1", "scripts/sync-issue-template-versions.ps1"),
        "required sync script call(s). Add them to .githooks/pre-commit.ps1.",
        "Pre-commit hook correctly invokes all required sync scripts.",
    ),
    (
        "pre-push.ps1",
        ("[Console]::In.ReadToEnd", "localSha", "remoteSha", "allChanged"),
        "required pattern(s). The hook must read stdin to detect changed files for performance.",
        "Pre-push hook correctly reads stdin for changed-file detection.",
    ),
    (
        "pre-merge-commit.ps1",
        ("pre-commit.ps1", "& $preCommit @HookArgs", "exit $LASTEXITCODE"),
        "required pattern(s). Without delegation, merge commits bypass pre-commit validation.",
        "Pre-merge-commit hook correctly delegates to pre-commit.",
    ),
)
FORBIDDEN = ("Get-Process -Id $PID", "Get-Command pwsh", "& $pwshPath", "$invokeArgs +=")


@dataclass(frozen=True)
class Result:
    errors: tuple[str, ...]
    passed: tuple[str, ...]


def validate(root: Path) -> Result:
    passed: list[str] = []
    for name, required, diagnostic, success in RULES:
        path = root / ".githooks" / name
        try:
            data = path.read_bytes()
            if data.startswith((codecs.BOM_UTF32_LE, codecs.BOM_UTF32_BE)):
                encoding = "utf-32"
            elif data.startswith((codecs.BOM_UTF16_LE, codecs.BOM_UTF16_BE)):
                encoding = "utf-16"
            else:
                encoding = "utf-8-sig"
            content = data.decode(encoding).lower()
        except (OSError, UnicodeError) as error:
            return Result((f"Cannot read {name} at {path}: {error}",), tuple(passed))
        missing = [pattern for pattern in required if pattern.lower() not in content]
        if missing:
            prefix = name.removesuffix(".ps1").capitalize()
            return Result(
                (
                    f"{prefix} hook is missing {len(missing)} {diagnostic}",
                    *(f"  - {pattern}" for pattern in missing),
                ),
                tuple(passed),
            )
        if name == "pre-merge-commit.ps1":
            forbidden = [pattern for pattern in FORBIDDEN if pattern.lower() in content]
            if forbidden:
                return Result(
                    (
                        "Pre-merge-commit hook must delegate to pre-commit.ps1 in-process to keep merge hooks fast.",
                        *(f"  - {pattern}" for pattern in forbidden),
                    ),
                    tuple(passed),
                )
        passed.append(success)
    return Result((), tuple(passed))


def main() -> int:
    configure_output()
    for stream in (sys.stdout, sys.stderr):
        if isinstance(stream, TextIOWrapper):
            stream.reconfigure(newline="\n")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args()
    result = validate(args.repo_root)
    if args.verbose:
        for message in result.passed:
            print(message)
    for message in result.errors:
        print(message)
    return int(bool(result.errors))


if __name__ == "__main__":
    raise SystemExit(main())
