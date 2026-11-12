"""Generate and check the documentation front page from the root README."""

from __future__ import annotations

import argparse
import json
import os
import posixpath
import re
import shutil
import subprocess
import sys
import tempfile
import unicodedata
from bisect import bisect_right
from pathlib import Path
from typing import TYPE_CHECKING, cast
from urllib.parse import quote, unquote

from cli_encoding import configure_output
from markdown_it import MarkdownIt
from markdown_it.common.html_re import HTML_TAG_RE

if TYPE_CHECKING:
    from collections.abc import Callable, Sequence

ROOT = Path(__file__).resolve().parents[1]
SOURCE = "README.md"
MIRROR = "docs/readme.md"
BLOB_BASE = "https://github.com/wallstop/unity-helpers/blob/main/"
FIX_COMMAND = "npm run sync:readme-mirror"
BANNER = (
    "<!-- Generated from ../README.md by scripts/sync-readme-mirror.js -- "
    f"edit that file and run `{FIX_COMMAND}`. -->"
)


def github_slug(heading: str) -> str:
    lowered = heading.lower().strip()
    return "".join(
        character
        for character in lowered
        if unicodedata.category(character)[0] in {"L", "N"} or character in "_- "
    ).replace(" ", "-")


def mkdocs_slug(heading: str) -> str:
    ascii_heading = unicodedata.normalize("NFKD", heading).encode("ascii", "ignore").decode()
    clean = re.sub(r"[^\w\s-]", "", ascii_heading, flags=re.ASCII).strip().lower()
    return re.sub(r"[-\s]+", "-", clean)


def fenced_lines(text: str) -> list[tuple[str, bool]]:
    result: list[tuple[str, bool]] = []
    fence = ""
    for line in text.split("\n"):
        match = re.match(r"^\s*(`{3,}|~{3,})", line)
        if not fence and match:
            fence = match[1]
            result.append((line, True))
        elif fence:
            result.append((line, True))
            if re.fullmatch(r"\s*" + re.escape(fence[0]) + r"{" + str(len(fence)) + r",}\s*", line):
                fence = ""
        else:
            result.append((line, False))
    return result


def headings_of(text: str) -> list[str]:
    headings: list[str] = []
    for line, fenced in fenced_lines(text):
        if not fenced:
            match = re.match(r"^#{1,6}\s+(.*?)\s*#*\s*$", line)
            if match:
                headings.append(match[1])
    return headings


def fragment_map(text: str) -> dict[str, str]:
    result: dict[str, str] = {}
    for heading in headings_of(text):
        source = github_slug(heading)
        if source and source not in result:
            result[source] = mkdocs_slug(heading)
    return result


def rewrite_outside_inline_code(text: str, transform: Callable[[str], str]) -> str:
    """Transform prose while preserving matched backtick spans byte for byte."""

    def is_escaped(position: int) -> bool:
        previous = position - 1
        while previous >= 0 and text[previous] == "\\":
            previous -= 1
        return (position - 1 - previous) % 2 != 0

    runs = list(re.finditer(r"`+", text))
    html_tags = list(HTML_TAG_RE.finditer(text))
    html_index = 0
    by_length: dict[int, list[int]] = {}
    for index, run in enumerate(runs):
        by_length.setdefault(len(run[0]), []).append(index)
    pieces: list[str] = []
    start = 0
    index = 0
    while index < len(runs):
        opening = runs[index]
        while html_index < len(html_tags) and html_tags[html_index].end() <= opening.start():
            html_index += 1
        inside_html = (
            html_index < len(html_tags)
            and html_tags[html_index].start() <= opening.start()
            and not is_escaped(html_tags[html_index].start())
        )
        matching = by_length[len(opening[0])]
        next_position = bisect_right(matching, index)
        if inside_html or is_escaped(opening.start()) or next_position == len(matching):
            index += 1
            continue
        closing_index = matching[next_position]
        closing = runs[closing_index]
        # A tag beginning inside code is literal, including any tail after the
        # closing code delimiter. It cannot own a later backtick opener.
        while html_index < len(html_tags) and html_tags[html_index].start() < closing.end():
            html_index += 1
        pieces.append(transform(text[start : opening.start()]))
        pieces.append(text[opening.start() : closing.end()])
        start = closing.end()
        index = closing_index + 1
    pieces.append(transform(text[start:]))
    return "".join(pieces)


def build_mirror(source: str, read_doc: Callable[[str], str], root: Path) -> tuple[str, list[str]]:
    problems: list[str] = []
    cache: dict[str, dict[str, str]] = {}

    def fragments_for(file: str) -> dict[str, str]:
        if file not in cache:
            cache[file] = fragment_map(read_doc(file))
        return cache[file]

    def rewrite(target: str) -> str:
        location, separator, fragment = target.partition("#")
        if not location:
            mapped = fragments_for(SOURCE).get(fragment)
            if mapped is None:
                problems.append(
                    f"{SOURCE} links to '#{fragment}', which is not the GitHub slug of any heading in it"
                )
            elif mapped != fragment:
                problems.append(
                    f"{SOURCE} links to '#{fragment}', but that heading publishes as '#{mapped}'. "
                    "Rename the heading to letters, digits and spaces so both renderers agree: the mirror "
                    "can carry only one of the two, and markdownlint's MD051 checks it with GitHub's slugger"
                )
            return target
        if re.match(r"^(?:[a-z][a-z0-9+.-]*:|/)", location, re.IGNORECASE):
            return target
        try:
            decoded = unquote(location, errors="strict")
        except UnicodeError:
            decoded = location
        relative = posixpath.normpath(decoded.removeprefix("./"))
        path = (root / relative).resolve()
        if not path.is_relative_to(root.resolve()):
            problems.append(f"{SOURCE} links to '{target}', which leaves the repository")
            return target
        if not path.exists():
            problems.append(f"{SOURCE} links to '{target}', which does not exist in the repository")
            return target
        if not relative.startswith("docs/"):
            return BLOB_BASE + location.removeprefix("./") + ("#" + fragment if separator else "")
        inner = quote(relative[len("docs/") :], safe="/-._~")
        prefixed = ("./" if location.startswith("./") else "") + inner
        if not separator:
            return prefixed
        mapped = (
            fragments_for(relative).get(fragment, fragment)
            if relative.endswith(".md") and path.is_file()
            else fragment
        )
        return prefixed + "#" + mapped

    def rewrite_links(prose: str) -> str:
        rewritten = re.sub(
            r"(\]\()([^)\s]+)(\))",
            lambda match: match[1] + rewrite(match[2]) + match[3],
            prose,
        )
        return rewrite_html(rewritten)

    def rewrite_html(prose: str) -> str:
        return re.sub(
            r"""\b(src|href)=(["'])([^"'\n]+)\2""",
            lambda match: match[1] + "=" + match[2] + rewrite(match[3]) + match[2],
            prose,
        )

    offsets = [0]
    offsets.extend(ending.end() for ending in re.finditer(r"\r\n?|\n", source))
    offsets.append(len(source))
    pieces: list[str] = []
    cursor = 0
    for token in MarkdownIt("commonmark").parse(source):
        if token.type not in {"inline", "html_block"} or token.map is None:
            continue
        start, end = offsets[token.map[0]], offsets[token.map[1]]
        pieces.append(source[cursor:start])
        original = source[start:end]
        pieces.append(
            rewrite_outside_inline_code(original, rewrite_links)
            if token.type == "inline"
            else rewrite_html(original)
        )
        cursor = end
    pieces.append(source[cursor:])
    return BANNER + "\n\n" + "".join(pieces), problems


def prettier_binary(repository: Path) -> Path:
    metadata = repository / "node_modules/prettier/package.json"
    settings: object = json.loads(metadata.read_text(encoding="utf-8"))
    if not isinstance(settings, dict):
        raise ValueError("Prettier package metadata must be an object")
    entry = cast("dict[str, object]", settings).get("bin")
    if isinstance(entry, dict):
        entry = cast("dict[str, object]", entry).get("prettier")
    if not isinstance(entry, str) or not entry.strip():
        raise ValueError("Prettier does not declare its executable")
    binary = (metadata.parent / entry).resolve()
    if not binary.is_relative_to(metadata.parent.resolve()) or not binary.is_file():
        raise ValueError("Prettier executable is missing or outside its installed package")
    return binary


def format_mirror(content: str, mirror: Path) -> str:
    node = shutil.which("node")
    if node is None:
        raise ValueError("Node is required to run the repository's Prettier formatter")
    result = subprocess.run(  # noqa: S603 - fixed formatter, stdin content and literal path.
        [
            node,
            str(prettier_binary(ROOT)),
            "--stdin-filepath",
            str(mirror),
            "--parser",
            "markdown",
        ],
        input=content,
        text=True,
        encoding="utf-8",
        capture_output=True,
        cwd=ROOT,
        timeout=60,
        check=False,
    )
    if result.returncode:
        raise ValueError("Prettier failed: " + result.stderr.strip())
    return result.stdout


def publish_mirror(mirror: Path, content: str) -> None:
    descriptor, name = tempfile.mkstemp(prefix=".readme-mirror-", suffix=".tmp", dir=mirror.parent)
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as stream:
            stream.write(content)
        if mirror.is_file():
            shutil.copymode(mirror, temporary)
        else:
            temporary.chmod(0o644)
        temporary.replace(mirror)
    finally:
        temporary.unlink(missing_ok=True)


def main(argv: Sequence[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__, exit_on_error=False)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=Path(os.environ.get("README_MIRROR_ROOT", ROOT)),
    )
    parser.add_argument("--sync-script", type=Path)
    try:
        args, unknown = parser.parse_known_args(argv)
        if args.sync_script is not None:
            custom = (ROOT / args.sync_script).resolve()
            if not custom.is_file():
                raise ValueError(f"generator not found at: {custom}")
            node = shutil.which("node")
            if node is None:
                raise ValueError("Node is required to run the requested compatibility generator")
            command = [node, str(custom), "--check"]
            if args.verbose:
                command.append("--verbose")
            command.extend(unknown)
            code = subprocess.run(command, cwd=ROOT, check=False, timeout=60).returncode  # noqa: S603 - explicit compatibility generator override.
            return code if code >= 0 else 1
        if unknown:
            raise ValueError("unrecognized arguments: " + " ".join(unknown))
        root: Path = args.repo_root.resolve()
        source = (root / SOURCE).read_text(encoding="utf-8")
        if not source.strip():
            raise ValueError("README source must not be empty")
        raw, problems = build_mirror(
            source, lambda file: (root / file).read_text(encoding="utf-8"), root
        )
        if problems:
            print(
                f"[readme-mirror] {len(problems)} link(s) could not be rewritten:",
                file=sys.stderr,
            )
            for problem in problems:
                print(f"  {problem}", file=sys.stderr)
            return 1
        mirror = root / MIRROR
        if mirror.is_symlink() or not mirror.resolve().is_relative_to(root):
            raise ValueError("mirror must be an ordinary file inside the repository")
        content = format_mirror(raw, mirror)
        existing: str | None = mirror.read_text(encoding="utf-8") if mirror.is_file() else None
        if existing == content:
            if args.verbose:
                print(
                    f"[readme-mirror] {MIRROR} "
                    + (f"matches {SOURCE}." if args.check else "already current.")
                )
            return 0
        if args.check:
            print(
                f"[readme-mirror] {MIRROR} is not what {SOURCE} generates.",
                file=sys.stderr,
            )
            expected = content.split("\n")
            actual = existing.split("\n") if existing is not None else []
            drifted = [
                index
                for index in range(max(len(expected), len(actual)))
                if (expected[index] if index < len(expected) else None)
                != (actual[index] if index < len(actual) else None)
            ]
            for index in drifted[:10]:
                print(
                    f"  {MIRROR}:{index + 1}\n    expected: {expected[index] if index < len(expected) else '(no line)'}\n    actual:   {actual[index] if index < len(actual) else '(no line)'}",
                    file=sys.stderr,
                )
            print(
                f"[readme-mirror] {MIRROR} is generated. Put the change in {SOURCE}, then run: {FIX_COMMAND}",
                file=sys.stderr,
            )
            return 1
        publish_mirror(mirror, content)
        print(f"[readme-mirror] wrote {MIRROR} from {SOURCE}.")
        return 0
    except (
        OSError,
        UnicodeError,
        ValueError,
        argparse.ArgumentError,
        subprocess.SubprocessError,
    ) as error:
        print(f"[readme-mirror] {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
