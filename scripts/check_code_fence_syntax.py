"""Validate comma-free Markdown fence attributes for MkDocs and GitHub Pages."""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass
from pathlib import Path

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
FENCE = re.compile(r"^[ \t\r\f\v]*(?:`{3,}|~{3,})([a-zA-Z][a-zA-Z0-9_+-]*),([a-zA-Z0-9_,+-]+)")
RED = "\033[0;31m"
GREEN = "\033[0;32m"
YELLOW = "\033[1;33m"
BLUE = "\033[0;34m"
RESET = "\033[0m"


@dataclass(frozen=True)
class Finding:
    path: Path
    line: int
    language: str
    attributes: str


def scan(directory: Path) -> tuple[int, list[Finding]]:
    if not directory.is_dir():
        raise ValueError(f"Docs directory not found: {directory}")
    files = sorted(
        path for path in directory.rglob("*.md") if path.is_file() and not path.is_symlink()
    )
    if not files:
        raise ValueError(f"No markdown files found under {directory}")
    findings: list[Finding] = []
    for path in files:
        with path.open(encoding="utf-8", newline="") as stream:
            for number, line in enumerate(stream, start=1):
                match = FENCE.match(line)
                if match is not None:
                    findings.append(Finding(path, number, match[1], match[2]))
    return len(files), findings


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    arguments = sys.argv[1:] if arguments is None else arguments
    directory = Path(arguments[0]) if arguments and arguments[0] else ROOT / "docs"
    print(
        "========================================\nCode Fence Syntax Validator\n========================================\n"
    )
    print(f"Docs Directory: {BLUE}{directory}{RESET}\n")
    if not directory.is_dir():
        print(f"{RED}ERROR: Docs directory not found: {directory}{RESET}")
        return 1
    print(
        "----------------------------------------\nChecking for invalid code fence syntax\n----------------------------------------\n"
    )
    print(
        "Pattern: code fences with comma-separated attributes\n  Invalid: ```csharp,ignore or ```rust,no_run\n  Valid:   ```csharp or ```rust\n"
    )
    try:
        count, findings = scan(directory)
    except (OSError, ValueError) as error:
        print(f"{RED}ERROR: {error}{RESET}\n")
        print(f"{RED}VALIDATION FAILED{RESET}")
        return 1
    for finding in findings:
        path = finding.path.relative_to(ROOT) if finding.path.is_relative_to(ROOT) else finding.path
        print(f"{RED}ISSUE{RESET} {path.as_posix()}:{finding.line}")
        print(f"  Found: ```{finding.language},{finding.attributes}")
        print(f"  Fix:   ```{finding.language}  {YELLOW}(remove ',{finding.attributes}'){RESET}\n")
    print(
        "----------------------------------------\nSummary\n----------------------------------------"
    )
    print(f"Markdown files scanned: {BLUE}{count}{RESET}\n")
    if findings:
        print(f"{RED}Found {len(findings)} code fence syntax issue(s){RESET}\n")
        print(
            "MkDocs and GitHub Pages do not support comma-separated\nattributes in code fences. Remove the comma and everything\nafter it from the language identifier.\n"
        )
        print(f"{RED}VALIDATION FAILED{RESET}")
        return 1
    print(f"{GREEN}No code fence syntax issues found.{RESET}\n\n{GREEN}VALIDATION PASSED{RESET}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
