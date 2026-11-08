"""Validate worktree line endings, UTF-8 BOMs, and Git text-blob normalization."""

from __future__ import annotations

import argparse
import os
import re
import shutil
import stat
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path

from cli_encoding import configure_output
from csharp_sources import is_link

ROOT = Path(__file__).resolve().parents[1]
MAX_SCOPE_ARGUMENT_BYTES = 12000
EXTENSIONS = frozenset(
    [
        "cs",
        "csproj",
        "sln",
        "json",
        "yaml",
        "yml",
        "md",
        "xml",
        "uxml",
        "uss",
        "shader",
        "hlsl",
        "compute",
        "cginc",
        "asmdef",
        "asmref",
        "meta",
        "ps1",
        "sh",
        "html",
    ]
)
LF_EXTENSIONS = frozenset(("sh", "yaml", "yml", "md"))
LF_PATHS = (".github/", ".githooks/")


def supported(path: str) -> bool:
    """Preserve the repository's explicit text-file policy."""
    return path.casefold() == ".gitignore" or Path(path).suffix[1:].casefold() in EXTENSIONS


def requires_lf(path: str) -> bool:
    """Match the repository attributes for files that require LF in the worktree."""
    path = path.casefold()
    return (
        Path(path).suffix[1:] in LF_EXTENSIONS
        or path.startswith(LF_PATHS)
        or path in ("package.json", "package-lock.json")
        or (path.startswith("_includes/") and path.endswith(".html"))
    )


def git_output(repository: Path, arguments: list[str]) -> bytes:
    """Read Git state without changing the index or invoking a shell."""
    executable = shutil.which("git")
    if executable is None:
        raise ValueError("Git is required to check line endings")
    result = subprocess.run(  # noqa: S603 - read-only Git with separate literal arguments.
        [executable, "-C", str(repository), *arguments],
        capture_output=True,
        timeout=60,
        check=False,
    )
    if result.returncode:
        raise ValueError(result.stderr.decode("utf-8", errors="replace").strip())
    return result.stdout


def relative_path(repository: Path, name: str) -> str:
    """Normalize a literal path without following a linked file or ancestor."""
    candidate = Path(name.replace("\\", "/"))
    if not candidate.is_absolute():
        candidate = repository / candidate
    candidate = Path(os.path.abspath(candidate))  # noqa: PTH100 - preserve links for rejection.
    if not candidate.is_relative_to(repository):
        raise ValueError(f"Path is outside the repository: {name}")
    return candidate.relative_to(repository).as_posix()


def index_issues(records: bytes, scope: set[str] | None) -> list[str]:
    """Find CR bytes in blobs whose Git attributes enable text normalization."""
    issues: list[str] = []
    for record in records.split(b"\0"):
        if not record:
            continue
        fields, separator, raw_path = record.partition(b"\t")
        if not separator:
            raise ValueError("Malformed git ls-files --eol output")
        path = os.fsdecode(raw_path)
        if scope is not None and path not in scope:
            continue
        description = fields.decode("ascii")
        match = re.search(r"(?:^|\s)i/(crlf|mixed)(?:\s|$)", description)
        if match is None:
            continue
        _, marker, attributes = description.partition("attr/")
        if not marker:
            raise ValueError("Missing Git text attributes")
        if re.search(r"(?:^|\s)-text(?:\s|$)", attributes):
            continue
        if re.search(r"(?:^|\s)text(?:=\S+)?(?:\s|$)", attributes):
            issues.append(f"{path} (committed blob is {match[1]}; git stores text blobs as LF)")
    return sorted(set(issues))


@dataclass(frozen=True)
class Findings:
    """Keep independent worktree and index findings visible to callers."""

    subjects: int
    endings: list[str]
    boms: list[str]
    blobs: list[str]

    @property
    def failed(self) -> bool:
        return bool(self.endings or self.boms or self.blobs)


def inspect(repository: Path, paths: list[str] | None = None) -> Findings:
    """Inspect literal selected files, or every tracked text file plus all index blobs."""
    if not repository.is_dir() or is_link(repository):
        raise ValueError(f"Repository is missing or linked: {repository}")
    scope = {relative_path(repository, name) for name in paths} if paths else None
    arguments = ["--literal-pathspecs", "ls-files", "--eol", "-z"]
    if (
        scope is not None
        and sum(len(os.fsencode(name)) + 1 for name in scope) <= MAX_SCOPE_ARGUMENT_BYTES
    ):
        arguments.extend(["--", *sorted(scope)])
    records = git_output(repository, arguments)
    tracked = {os.fsdecode(record.partition(b"\t")[2]) for record in records.split(b"\0") if record}
    names = sorted(name for name in (scope if scope is not None else tracked) if supported(name))
    endings: list[str] = []
    boms: list[str] = []
    checked_directories = {repository}

    def inspect_file(name: str) -> tuple[int, str | None, str | None]:
        path = repository / name
        try:
            info = path.lstat()
        except FileNotFoundError:
            if scope is None:
                return 0, None, None
            raise ValueError(f"Input is missing or not a regular file: {name}") from None
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise ValueError(f"Cannot check linked input: {name}")
        if not stat.S_ISREG(info.st_mode):
            raise ValueError(f"Input is missing or not a regular file: {name}")
        current = path.parent
        verified: list[Path] = []
        while current not in checked_directories:
            if is_link(current):
                raise ValueError(f"Cannot check linked input: {name}")
            verified.append(current)
            current = current.parent
        checked_directories.update(verified)
        data = path.read_bytes()
        bom = name if data.startswith(b"\xef\xbb\xbf") else None
        ending = None
        if requires_lf(name):
            if b"\r\n" in data:
                ending = f"{name} (expected LF, found CRLF)"
        elif b"\n" in data.replace(b"\r\n", b""):
            ending = f"{name} (expected CRLF, found LF-only or mixed)"
        return 1, ending, bom

    subjects = 0
    with ThreadPoolExecutor(max_workers=min(8, max(1, len(names)))) as workers:
        for count, ending, bom in workers.map(inspect_file, names):
            subjects += count
            if ending is not None:
                endings.append(ending)
            if bom is not None:
                boms.append(bom)
    if scope is None and not subjects:
        raise ValueError("No tracked text files were inspected; a pass would mean nothing")
    return Findings(subjects, endings, boms, index_issues(records, scope))


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--paths", nargs="+")
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    try:
        repository = Path(os.path.abspath(args.repo_root))  # noqa: PTH100 - preserve root links for rejection.
        findings = inspect(repository, args.paths)
        if args.verbose or findings.failed:
            for title, values in (
                ("EOL issues (wrong line endings):", findings.endings),
                ("Contains UTF-8 BOM (disallowed):", findings.boms),
                (
                    "Unnormalized committed blobs (every fresh clone reports these as modified):",
                    findings.blobs,
                ),
            ):
                if values:
                    print(title)
                    for value in values:
                        print(f" - {value}")
        if findings.blobs:
            print("Fix by re-staging through Git's clean filter: git add --renormalize <path>")
            print("Content that must keep CR bytes must use '-text' in .gitattributes.")
        print(f"Inspected text files: {findings.subjects}")
        print(f"EOL issues: {len(findings.endings)}")
        print(f"Files with BOM: {len(findings.boms)}")
        print(f"Unnormalized committed blobs: {len(findings.blobs)}")
        return 3 if findings.failed else 0
    except (OSError, UnicodeError, ValueError, subprocess.SubprocessError) as error:
        print(f"[check-eol] {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
