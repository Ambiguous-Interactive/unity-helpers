"""Extract documentation C# samples and retain their exact source locations."""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path, PurePath
from typing import TypedDict

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
EXCLUDED_DIRECTORIES = frozenset(
    {"node_modules", ".git", "Library", "Temp", "obj", "artifacts", "Logs"}
)
PARTIAL_MARKERS = ("// ...", "/* ... */", "// (existing code)", "// (omitted)", "...")
OPENING_FENCE = re.compile(r" {0,3}(`{3,}|~{3,})([^\n]*)", re.ASCII)
CLOSING_FENCE = re.compile(r" {0,3}(`{3,}|~{3,})[ \t]*", re.ASCII)
COMMON_USINGS = """using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;
using WallstopStudios.UnityHelpers.Core.DataStructure;
using WallstopStudios.UnityHelpers.Core.Extension;
using WallstopStudios.UnityHelpers.Core.Helper;
using WallstopStudios.UnityHelpers.Core.Random;
using WallstopStudios.UnityHelpers.Core.Serialization;
using WallstopStudios.UnityHelpers.Tags;
"""


@dataclass
class CodeSample:
    """One fenced block with its source position and generation decisions."""

    language: str
    code: str
    file_path: Path
    line_number: int
    raw: str
    is_complete: bool = False
    is_snippet: bool = False
    generated_file: Path | None = None


class SampleReport(TypedDict):
    file: str
    line: int
    language: str
    isComplete: bool
    generatedFile: str | None


class ExtractionReport(TypedDict):
    timestamp: str
    totalFiles: int
    totalSamples: int
    completeSamples: int
    partialSamples: int
    samples: list[SampleReport]


def markdown_files(root: Path) -> list[Path]:
    """Walk owned Markdown trees deterministically without following directory links."""
    if not root.is_dir():
        raise ValueError(f"Scan root does not exist or is not a directory: {root}")
    if root == ROOT:
        git = shutil.which("git")
        if git is None:
            raise ValueError("Git is required to inventory repository documentation.")
        result = subprocess.run(  # noqa: S603 - fixed read-only Git inventory.
            [
                git,
                "-C",
                str(root),
                "ls-files",
                "--cached",
                "--others",
                "--exclude-standard",
                "-z",
            ],
            capture_output=True,
            text=True,
            encoding="utf-8",
            check=False,
            timeout=60,
        )
        if result.returncode != 0:
            raise ValueError(f"Cannot inventory repository documentation: {result.stderr.strip()}")
        names = result.stdout.rstrip("\0").split("\0")
        return sorted(
            {
                root / name
                for name in names
                if name.lower().endswith(".md")
                and not any(part in EXCLUDED_DIRECTORIES for part in Path(name).parts[:-1])
                and not (root / name).is_symlink()
                and not any((root / parent).is_symlink() for parent in Path(name).parents)
                and (root / name).is_file()
            }
        )
    files: list[Path] = []
    for directory, children, names in os.walk(root, onerror=raise_walk_error):
        children[:] = sorted(name for name in children if name not in EXCLUDED_DIRECTORIES)
        files.extend(
            Path(directory) / name
            for name in sorted(names)
            if name.lower().endswith(".md") and not (Path(directory) / name).is_symlink()
        )
    return files


def raise_walk_error(error: OSError) -> None:
    raise error


def extract_code_blocks(content: str, path: Path) -> list[CodeSample]:
    """Read line-delimited matching fences, preserving normalized source content and positions."""
    normalized = content.replace("\r\n", "\n").replace("\r", "\n")
    samples: list[CodeSample] = []
    lines = normalized.splitlines(keepends=True)
    index = 0
    while index < len(lines):
        opening = OPENING_FENCE.fullmatch(lines[index].rstrip("\n"))
        if opening is None or (opening[1][0] == "`" and "`" in opening[2]):
            index += 1
            continue
        marker = opening[1]
        info = opening[2].strip().split(maxsplit=1)
        language = info[0].lower() if info else ""
        end = index + 1
        while end < len(lines):
            closing = CLOSING_FENCE.fullmatch(lines[end].rstrip("\n"))
            if (
                closing is not None
                and closing[1][0] == marker[0]
                and len(closing[1]) >= len(marker)
            ):
                break
            end += 1
        code = "".join(lines[index + 1 : end])
        raw = "".join(lines[index : min(end + 1, len(lines))])
        if end < len(lines):
            raw = raw.removesuffix("\n")
        samples.append(CodeSample(language, code, path, index + 1, raw))
        index = end + 1
    return samples


def is_complete_sample(code: str, *, include_partial: bool) -> bool:
    if not include_partial and any(marker in code for marker in PARTIAL_MARKERS):
        return False
    has_type = re.search(r"\b(?:class|struct|interface)\s+\w+", code, re.ASCII) is not None
    has_method = (
        re.search(r"\b(?:void|int|float|bool|string|async|Task)\s+\w+\s*\(", code, re.ASCII)
        is not None
    )
    return has_type or (has_method and re.search(r"\busing\s+[\w.]+;", code, re.ASCII) is not None)


def is_standalone_snippet(code: str) -> bool:
    has_statements = re.search(r"\w+\.\w+\(|\w+\s*=\s*", code, re.ASCII) is not None
    return has_statements and re.search(r"\bclass\s+\w+", code, re.ASCII) is None


def relative_or_absolute(path: PurePath, root: PurePath) -> str:
    """Keep relative source identities where possible, including cross-volume inputs."""
    try:
        return os.path.relpath(path, root)
    except ValueError:
        return str(path)


def sample_name(path: Path, line: int) -> str:
    relative = relative_or_absolute(path, ROOT).replace("\\", "/")
    base = re.sub(r"\.md$", "", relative.replace("/", "_"), flags=re.IGNORECASE)
    return f"{re.sub(r'[^a-zA-Z0-9_-]', '_', base)}_L{line}"


def wrap_in_compilable_class(code: str, class_name: str) -> str:
    if re.search(r"\b(?:class|struct|interface)\s+\w+", code, re.ASCII):
        return (
            code if re.search(r"\busing\s+[\w.]+;", code, re.ASCII) else f"{COMMON_USINGS}\n{code}"
        )
    safe_name = re.sub(r"[^a-zA-Z0-9_]", "_", class_name)
    indented = "\n".join(f"        {line}" for line in code.split("\n"))
    return (
        f"{COMMON_USINGS}\nnamespace WallstopStudios.UnityHelpers.CodeSamples\n"
        "{\n"
        f"    public class {safe_name} : MonoBehaviour\n"
        "    {\n        private void DocumentationExample()\n        {\n"
        f"{indented}\n        }}\n    }}\n}}\n"
    )


def generate_compilable_files(samples: list[CodeSample], output: Path) -> tuple[int, int]:
    """Validate output identities before writing any generated compilation unit."""
    pending: dict[str, tuple[CodeSample, str]] = {}
    identities: set[str] = set()
    for sample in samples:
        if not sample.is_complete and not sample.is_snippet:
            continue
        name = sample_name(sample.file_path, sample.line_number)
        filename = f"{name}.cs"
        if filename.casefold() in identities:
            raise ValueError(f"Generated filename collision: {filename}")
        identities.add(filename.casefold())
        pending[filename] = (sample, wrap_in_compilable_class(sample.code, name))
    output.mkdir(parents=True, exist_ok=True)
    for filename, (sample, code) in pending.items():
        destination = output / filename
        destination.write_text(code, encoding="utf-8", newline="")
        sample.generated_file = destination
    assembly = {
        "name": "WallstopStudios.UnityHelpers.CodeSamples",
        "rootNamespace": "WallstopStudios.UnityHelpers.CodeSamples",
        "references": [
            "WallstopStudios.UnityHelpers",
            "WallstopStudios.UnityHelpers.Editor",
        ],
        "includePlatforms": ["Editor"],
        "excludePlatforms": [],
        "allowUnsafeCode": False,
        "overrideReferences": False,
        "precompiledReferences": [],
        "autoReferenced": False,
        "defineConstraints": [],
        "versionDefines": [],
        "noEngineReferences": False,
    }
    (output / "WallstopStudios.UnityHelpers.CodeSamples.asmdef").write_text(
        json.dumps(assembly, indent=2), encoding="utf-8", newline=""
    )
    return len(pending), len(samples) - len(pending)


def generate_report(samples: list[CodeSample], output: Path) -> ExtractionReport:
    complete = sum(sample.is_complete for sample in samples)
    return {
        "timestamp": datetime.now(UTC).isoformat(timespec="milliseconds").replace("+00:00", "Z"),
        "totalFiles": len({sample.file_path for sample in samples}),
        "totalSamples": len(samples),
        "completeSamples": complete,
        "partialSamples": len(samples) - complete,
        "samples": [
            {
                "file": relative_or_absolute(sample.file_path, ROOT),
                "line": sample.line_number,
                "language": sample.language,
                "isComplete": sample.is_complete,
                "generatedFile": (
                    relative_or_absolute(sample.generated_file, output)
                    if sample.generated_file
                    else None
                ),
            }
            for sample in samples
        ],
    }


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--extract-only", action="store_true")
    parser.add_argument("--include-partial", action="store_true")
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument("--output-dir", type=Path, default=ROOT / "artifacts/code-samples")
    parser.add_argument(
        "--scan-root",
        type=Path,
        default=Path(os.environ.get("CODE_SAMPLES_ROOT", ROOT)),
    )
    args = parser.parse_args(argv)
    scan_root: Path = args.scan_root.resolve()
    output: Path = args.output_dir.resolve()
    try:
        files = markdown_files(scan_root)
        samples: list[CodeSample] = []
        total_blocks = 0
        for path in files:
            blocks = extract_code_blocks(path.read_text(encoding="utf-8-sig"), path)
            total_blocks += len(blocks)
            for block in blocks:
                if block.language not in {"csharp", "cs"}:
                    continue
                block.is_complete = is_complete_sample(
                    block.code, include_partial=args.include_partial
                )
                block.is_snippet = is_standalone_snippet(block.code)
                samples.append(block)
        print(f"Found {len(files)} Markdown files")
        print(f"Total code blocks: {total_blocks}")
        print(f"C# code blocks: {len(samples)}")
        print(f"Complete samples: {sum(sample.is_complete for sample in samples)}")
        if not args.extract_only:
            generated, skipped = generate_compilable_files(samples, output)
            print(f"Generated: {generated} files; skipped: {skipped} incomplete samples")
        report = generate_report(samples, output)
        output.mkdir(parents=True, exist_ok=True)
        report_path = output / "extraction-report.json"
        report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"Report saved to: {report_path}")
        if args.verbose:
            for path in dict.fromkeys(sample.file_path for sample in samples):
                selected = [sample for sample in samples if sample.file_path == path]
                complete = sum(sample.is_complete for sample in selected)
                print(
                    f"{relative_or_absolute(path, scan_root)}: {complete} complete, {len(selected) - complete} partial"
                )
        if not samples:
            print("No C# code samples found in documentation!", file=sys.stderr)
            return 1
        return 0
    except (OSError, UnicodeError, ValueError, subprocess.TimeoutExpired) as error:
        print(f"Code sample extraction failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
