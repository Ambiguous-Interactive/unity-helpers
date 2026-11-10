"""Extract marked documentation examples into the runtime and Editor compile corpora."""

from __future__ import annotations

import argparse
import os
import re
import stat
import sys
from dataclasses import dataclass, field
from pathlib import Path

from cli_encoding import configure_output, escape_command_data
from csharp_sources import is_link, linked_entry, mask_noise, source_tokens
from extract_code_samples import relative_or_absolute
from markdown_it import MarkdownIt

ROOT = Path(__file__).resolve().parents[1]
MARKER = "<!-- doc-sample: compiles -->"
EDITOR_MARKER = "<!-- doc-sample: compiles-editor -->"
MARKERS = frozenset((MARKER, EDITOR_MARKER))
ELISIONS = ("// ...", "/* ... */", "// (existing", "// (omitted", "...")
USING = re.compile(r"^\s*using\s+[A-Za-z0-9_.<>, ]+\s*;\s*$")
TYPE = re.compile(r"(^|[^A-Za-z0-9_])(class|struct|interface|enum|record)\s+[A-Za-z_]")
NAMESPACE = re.compile(r"^\s*namespace\s+[A-Za-z_][A-Za-z0-9_.]*\s*\{")
MEMBER = re.compile(
    r"^(public|private|protected|internal|static|readonly|const|virtual|override|abstract|"
    r"sealed|extern|event|partial|volatile|unsafe|new)\s"
)
METHOD = re.compile(
    r"^[A-Za-z_][A-Za-z0-9_<>,.\[\]?]*\s+[A-Za-z_][A-Za-z0-9_]*\s*(<[^>()]*>)?\s*\("
)
STATEMENT = re.compile(
    r"^(return|throw|yield|await|new|if|else|while|for|foreach|switch|case|lock|using|try|"
    r"catch|finally|do|checked|unchecked|fixed|goto|break|continue|var|ref|out|in|delegate)\s"
)
COMMON_USINGS = (
    "using System;",
    "using System.Collections;",
    "using System.Collections.Generic;",
    "using System.Linq;",
    "using System.Threading;",
    "using System.Threading.Tasks;",
    "using UnityEngine;",
    "using WallstopStudios.UnityHelpers.Core.Attributes;",
    "using WallstopStudios.UnityHelpers.Core.DataStructure;",
    "using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;",
    "using WallstopStudios.UnityHelpers.Core.Extension;",
    "using WallstopStudios.UnityHelpers.Core.Helper;",
    "using WallstopStudios.UnityHelpers.Core.Math;",
    "using WallstopStudios.UnityHelpers.Core.Model;",
    "using WallstopStudios.UnityHelpers.Core.Random;",
    "using WallstopStudios.UnityHelpers.Core.Serialization;",
    "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;",
    "using WallstopStudios.UnityHelpers.Tags;",
    "using WallstopStudios.UnityHelpers.Utils;",
)


@dataclass(frozen=True)
class Sample:
    path: Path
    line: int
    body: tuple[str, ...]
    editor_only: bool = False


@dataclass
class Counts:
    declaration: int = 0
    member: int = 0
    usage: int = 0
    empty: int = 0


@dataclass
class Scopes:
    types: list[str] = field(default_factory=list[str])
    rest: list[str] = field(default_factory=list[str])
    has_statement: bool = False


def hoist(body: tuple[str, ...]) -> tuple[list[str], list[str]]:
    usings: list[str] = []
    rest: list[str] = []
    code = mask_noise("\n".join(body)).split("\n")
    for index, line in enumerate(body):
        if USING.fullmatch(code[index]) and "(" not in code[index]:
            usings.append(line.strip())
        else:
            rest.append(line)
    return usings, rest


def top_level_items(code: list[str]) -> list[tuple[int, int]]:
    items: list[tuple[int, int]] = []
    depth = 0
    opened = False
    start = 0
    saw_code = False
    for index, line in enumerate(code):
        if not saw_code and not line.strip():
            continue
        saw_code = True
        for character in line:
            if character == "{":
                depth += 1
                opened = True
            elif character == "}":
                depth -= 1
        closed = re.search(r"\}[\s;,]*$" if opened else r";\s*$", line.strip())
        if depth <= 0 and closed:
            items.append((start, index + 1))
            start = index + 1
            opened = False
            depth = 0
            saw_code = False
    if start < len(code):
        items.append((start, len(code)))
    return items


def kind_of(item: str) -> str:
    if not item.strip():
        return "trivia"
    without_attributes = re.sub(r"^(\s*\[[^\]]*\]\s*\n?)+", "", item)
    head = without_attributes.split("{", 1)[0]
    if TYPE.search(head) or NAMESPACE.match(item):
        return "type"
    trimmed = without_attributes.strip()
    if item.strip().startswith("[") or MEMBER.match(trimmed):
        return "member"
    if METHOD.match(trimmed) and not STATEMENT.match(trimmed):
        return "member"
    if re.search(r"\{\s*get\b", item) and "=>" not in head:
        return "member"
    return "statement"


def scopes_for(body: list[str]) -> Scopes:
    code = mask_noise("\n".join(body)).split("\n")
    scopes = Scopes()
    previous = scopes.rest
    for start, end in top_level_items(code):
        kind = kind_of("\n".join(code[start:end]))
        lines = body[start:end]
        if kind == "trivia":
            previous.extend(lines)
            continue
        previous = scopes.types if kind == "type" else scopes.rest
        scopes.has_statement |= kind == "statement"
        previous.extend(lines)
    return scopes


def samples_in(path: Path, text: str, counts: Counts) -> tuple[list[Sample], list[str]]:
    tokens = MarkdownIt("commonmark").parse(text)
    samples: list[Sample] = []
    problems: list[str] = []
    claimed: dict[int, str] = {}
    for index, token in enumerate(tokens):
        marker = token.content.strip()
        if token.type != "html_block" or marker not in MARKERS or token.map is None:
            continue
        following = tokens[index + 1] if index + 1 < len(tokens) else None
        if (
            following is None
            or following.type != "fence"
            or following.info.strip() not in ("csharp", "cs")
            or following.level != token.level
        ):
            problems.append(
                f"{path}:{token.map[0] + 1}: the {marker} marker is not followed by a C# fence, so it adds nothing to the checked corpus."
            )
        else:
            claimed[index + 1] = marker
    for index, token in enumerate(tokens):
        if token.type != "fence" or token.info.strip() not in ("csharp", "cs") or token.map is None:
            continue
        body = tuple(token.content.removesuffix("\n").split("\n"))
        joined = "\n".join(body)
        line = token.map[0] + 2
        if not any(part.strip() and not part.strip().startswith("//") for part in body):
            counts.empty += 1
            if index in claimed:
                problems.append(
                    f"{path}:{line}: a marked sample is empty, so this claim checked nothing."
                )
            continue
        if index not in claimed:
            scopes = scopes_for(hoist(body)[1])
            if scopes.types:
                counts.declaration += 1
            elif scopes.has_statement:
                counts.usage += 1
            else:
                counts.member += 1
            continue
        literal_free = list(joined)
        for start, end, kind in source_tokens(joined):
            if kind == "literal":
                literal_free[start:end] = " " * (end - start)
        elision_text = "".join(literal_free)
        if any(elision in elision_text for elision in ELISIONS):
            problems.append(
                f"{path}:{line}: a marked sample contains an elision, so it cannot compile."
            )
        elif re.search(r"^\s*\[assembly\s*:", mask_noise(joined), re.MULTILINE):
            problems.append(
                f"{path}:{line}: a marked sample may not carry an [assembly: ...] attribute."
            )
        else:
            samples.append(Sample(path, line, body, claimed[index] == EDITOR_MARKER))
    return samples, problems


def render(sample: Sample, suffix: str, root: Path) -> str:
    usings, rest = hoist(sample.body)
    unique = list(dict.fromkeys((*COMMON_USINGS, *usings)))
    relative = sample.path.relative_to(root).as_posix()
    parts = [
        "// <auto-generated />",
        f"// Extracted from {escape_command_data(relative)}:{sample.line}. Edit the documentation, not this file.",
        "#pragma warning disable",
        f"namespace WallstopStudios.UnityHelpers.DocSamples.{suffix}",
        "{",
        "\n".join("    " + directive for directive in unique),
        "",
    ]
    scopes = scopes_for(rest)
    if scopes.types:
        parts.append("\n".join(scopes.types))
    if scopes.rest:
        parts.extend(("    internal sealed class DocSample : UnityEngine.MonoBehaviour", "    {"))
        joined = "\n".join(scopes.rest)
        if scopes.has_statement:
            signature = "private void DocSampleUsage()"
            if re.search(r"\byield\s+(return|break)\b", joined):
                signature = "private System.Collections.IEnumerator DocSampleUsage()"
            elif re.search(r"\bawait\s", joined):
                signature = "private async System.Threading.Tasks.Task DocSampleUsage()"
            parts.extend(("        " + signature, "        {", joined, "        }"))
        else:
            parts.append(joined)
        parts.append("    }")
    return "\n".join((*parts, "}", ""))


def markdown_under(root: Path) -> list[Path]:
    if not root.is_dir() or is_link(root):
        raise ValueError(f"Documentation root is missing or linked: {root}")
    pending = [root]
    found: list[Path] = []
    while pending:
        with os.scandir(pending.pop()) as entries:
            for entry in entries:
                if entry.name.startswith(".") or entry.name == "node_modules":
                    continue
                if linked_entry(entry):
                    raise ValueError(f"Linked documentation input: {entry.path}")
                if entry.is_dir(follow_symlinks=False):
                    pending.append(Path(entry.path))
                elif entry.name.lower().endswith(".md"):
                    if not entry.is_file(follow_symlinks=False):
                        raise ValueError(f"Documentation input is not a regular file: {entry.path}")
                    found.append(Path(entry.path))
    return sorted(found)


def validate_output(output: Path, root: Path) -> None:
    for parent in (output, *output.parents):
        if (parent.exists() or parent.is_symlink()) and (is_link(parent) or not parent.is_dir()):
            raise ValueError(f"Output is linked or not a directory: {parent}")
    docs = root / "docs"
    if output in (root, docs) or output in root.parents or docs in output.parents:
        raise ValueError("The output cannot replace the source tree or documentation.")
    if output.exists():
        for directory in (output, output / "editor"):
            if directory.is_symlink():
                raise ValueError(f"Linked output directory: {directory}")
            if directory.exists():
                for path in directory.iterdir():
                    if is_link(path) or (
                        path != output / "editor" and not stat.S_ISREG(path.stat().st_mode)
                    ):
                        raise ValueError(f"Unsafe output entry: {path}")


def extract(root: Path, output: Path, *, require_both: bool) -> int:
    root = Path(os.path.abspath(root))  # noqa: PTH100 -- preserve links for rejection below.
    output = Path(os.path.abspath(output))  # noqa: PTH100 -- preserve links for rejection below.
    if not root.is_dir() or is_link(root):
        raise ValueError("The source root is missing or linked.")
    counts = Counts()
    files = markdown_under(root / "docs")
    samples: list[Sample] = []
    problems: list[str] = []
    rendered: dict[Path, str] = {}
    identities: set[str] = set()
    for path in files:
        found, invalid = samples_in(path, path.read_text(encoding="utf-8-sig"), counts)
        samples.extend(found)
        problems.extend(invalid)
    runtime_count = sum(not sample.editor_only for sample in samples)
    editor_count = len(samples) - runtime_count
    if not samples:
        problems.append("No marked documentation samples; this gate checked nothing.")
    if require_both and not runtime_count:
        problems.append("No runtime sample; the runtime compile leg checked nothing.")
    if require_both and not editor_count:
        problems.append("No Editor-only sample; the Editor-only corpus checked nothing.")
    for sample in samples:
        relative = sample.path.relative_to(root).as_posix()
        suffix = "S_" + re.sub(r"[^A-Za-z0-9]", "_", relative[:-3]) + f"_{sample.line}"
        name = ("editor/" if sample.editor_only else "") + suffix + ".cs"
        if name.casefold() in identities:
            problems.append(f"Generated sample filename collision: {relative}:{sample.line}")
        identities.add(name.casefold())
        rendered[output / name] = render(sample, suffix, root)
    print(f"[doc-samples] Markdown files scanned: {len(files)}")
    print(f"[doc-samples] Samples compiled: {len(samples)}")
    print(f"[doc-samples] Runtime samples: {runtime_count}; Editor-only samples: {editor_count}")
    print(
        f"[doc-samples] Unmarked: {counts.declaration} declaration-shaped, {counts.member} member-shaped, {counts.usage} statement-shaped, {counts.empty} empty"
    )
    print(f"[doc-samples] Output: {relative_or_absolute(output, ROOT)}")
    if problems:
        for problem in problems:
            print("[doc-samples] ERROR " + escape_command_data(problem), file=sys.stderr)
        return 1
    validate_output(output, root)
    for target in rendered:
        if target.exists() and not target.read_text(encoding="utf-8").startswith(
            "// <auto-generated />\n// Extracted from "
        ):
            raise ValueError(f"Refusing to overwrite unrelated output: {target}")
    output.mkdir(parents=True, exist_ok=True)
    (output / "editor").mkdir(exist_ok=True)
    for directory in (output, output / "editor"):
        for stale in directory.glob("*.cs"):
            if stale not in rendered and stale.read_text(encoding="utf-8").startswith(
                "// <auto-generated />\n// Extracted from "
            ):
                stale.unlink()
    for target, source in rendered.items():
        target.write_text(source, encoding="utf-8", newline="\n")
    return 0


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.parse_args(argv)
    try:
        root = Path(os.path.abspath(os.environ.get("DOC_SAMPLES_ROOT", str(ROOT))))  # noqa: PTH100 -- preserve links for extract's validation.
        output = Path(
            os.environ.get("DOC_SAMPLES_OUT", str(ROOT / "artifacts/doc-samples~"))
        ).absolute()
        return extract(
            root,
            output,
            require_both=root == ROOT or os.environ.get("DOC_SAMPLES_REQUIRE_BOTH") == "1",
        )
    except (OSError, ValueError, UnicodeError) as error:
        print("[doc-samples] ERROR " + escape_command_data(str(error)), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
