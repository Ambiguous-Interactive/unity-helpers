"""Reject C# region directives and remove only real directive lines when requested."""

from __future__ import annotations

import argparse
import os
import re
import subprocess
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from check_eol import git_output
from cli_encoding import configure_output, escape_command_property
from csharp_sources import is_link, source_tokens
from lint_comment_block_form import checked_path

ROOT = Path(__file__).resolve().parents[1]
ROOTS = ("Runtime", "Editor", "Tests", "Generator~")
NEWLINES = ("\n", "\r", "\x85", "\u2028", "\u2029")
NEWLINE_TRANSLATION = str.maketrans("\r\x85\u2028\u2029", "\n\n\n\n")
REGION = re.compile(r"#[^\S\r\n]*(?:region|endregion)\b")
REGION_LINE = re.compile(r"^[^\S\r\n]*#[^\S\r\n]*(?:region|endregion)\b", re.MULTILINE)
CI_VARIABLES = (
    "CI",
    "GITHUB_ACTIONS",
    "GITLAB_CI",
    "JENKINS_URL",
    "TRAVIS",
    "CIRCLECI",
    "AZURE_PIPELINES",
    "TF_BUILD",
    "BUILDKITE",
    "CODEBUILD_BUILD_ID",
)


def escape_diagnostic(value: str) -> str:
    return escape_command_property(value).replace("[", "%5B").replace("]", "%5D")


def git(repository: Path, *arguments: str) -> bytes:
    return git_output(repository, list(arguments))


def directives(text: str) -> list[tuple[int, int, int]]:
    """Return real directive line spans and one-based line numbers."""
    lexical = text.translate(NEWLINE_TRANSLATION)
    if REGION_LINE.search(lexical) is None:
        return []
    found: list[tuple[int, int, int]] = []
    for start, end, kind in source_tokens(lexical):
        if kind != "directive" or not REGION.match(lexical, start):
            continue
        line_start = max(text.rfind(newline, 0, start) for newline in NEWLINES) + 1
        line_end = end
        if end < len(text):
            line_end += 2 if text.startswith("\r\n", end) else 1
        prefix = text[:start]
        line = sum(prefix.count(newline) for newline in NEWLINES) - prefix.count("\r\n") + 1
        found.append((line_start, line_end, line))
    return found


def rewrite(text: str, spans: list[tuple[int, int, int]]) -> str:
    """Preserve every byte outside selected complete directive lines."""
    for start, end, _ in reversed(spans):
        text = text[:start] + text[end:]
    return text


def sources(repository: Path, paths: list[str] | None, *, staged: bool) -> list[tuple[str, bytes]]:
    """Read the complete selected corpus before allowing any writes."""
    if not staged and paths is None:
        for name in ROOTS:
            root = repository / name
            if not root.is_dir() or is_link(root):
                raise ValueError(f"Source root not found or linked: {name}")
    top = Path(git(repository, "rev-parse", "--show-toplevel").decode().rstrip("\r\n")).resolve()
    if top != repository:
        raise ValueError("Git repository root does not match the selected root")
    if staged:
        names = (
            git(
                repository,
                "diff",
                "--cached",
                "--name-only",
                "--diff-filter=ACMR",
                "-z",
                "--",
                "*.cs",
            )
            .decode("utf-8")
            .split("\0")
        )
    elif paths is not None:
        names = paths
    else:
        names = (
            git(
                repository,
                "ls-files",
                "--cached",
                "--others",
                "--exclude-standard",
                "-z",
                "--",
                *(f"{name}/*.cs" for name in ROOTS),
            )
            .decode("utf-8")
            .split("\0")
        )

    def read_source(name: str) -> tuple[str, bytes] | None:
        if not name.lower().endswith(".cs"):
            raise ValueError(f"Expected a C# source: {name}")
        if staged:
            mode = git(
                repository,
                "--literal-pathspecs",
                "ls-files",
                "--stage",
                "-z",
                "--",
                name,
            )
            if not mode.startswith((b"100644 ", b"100755 ")):
                raise ValueError(f"Staged C# source is not a regular file: {name}")
            data = git(repository, "show", f":{name}")
        else:
            try:
                path = checked_path(repository, name, literal=paths is None)
            except FileNotFoundError:
                if paths is None:
                    return None
                raise
            name = path.relative_to(repository).as_posix()
            data = path.read_bytes()
        return name, data

    with ThreadPoolExecutor(max_workers=8) as workers:
        loaded = list(workers.map(read_source, sorted(set(names) - {""})))
    found = [source for source in loaded if source is not None]
    if not found and not staged:
        raise ValueError("The scan found no C# files; a pass would mean nothing")
    return found


def main(argv: list[str] | None = None) -> int:
    """Check source or staged blobs with safe, explicit worktree-only repair."""
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--paths", nargs="+")
    parser.add_argument("--staged-only", action="store_true")
    parser.add_argument("--fix", action="store_true")
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    repository = args.repo_root.resolve()
    try:
        if args.staged_only and (args.fix or args.paths is not None):
            raise ValueError(
                "Staged mode checks index blobs only; fix explicit worktree paths and stage them separately"
            )
        selected = sources(repository, args.paths, staged=args.staged_only)
        decoded = [(name, data, data.decode("utf-8-sig")) for name, data in selected]
        fixing = args.fix and not any(os.environ.get(name) for name in CI_VARIABLES)
        violations = 0
        for name, original, text in decoded:
            spans = directives(text)
            violations += len(spans)
            if fixing and spans:
                encoding = "utf-8-sig" if original.startswith(b"\xef\xbb\xbf") else "utf-8"
                replacement = rewrite(text, spans).encode(encoding)
                path = checked_path(repository, name, literal=True)
                if path.read_bytes() != original:
                    raise ValueError(f"Source changed before repair: {name}")
                path.write_bytes(replacement)
            else:
                for _, _, line in spans:
                    print(
                        f"{escape_diagnostic(name)}:{line}: UNH005: #region/#endregion directives are not allowed"
                    )
        if args.verbose or violations:
            print(
                f"[lint-no-regions] Checked {len(selected)} C# files; {violations} directive(s){' removed' if fixing else ''}."
            )
        return int(bool(violations) and not fixing)
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"[lint-no-regions] ERROR: {escape_diagnostic(str(error))}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
