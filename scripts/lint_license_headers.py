"""Require an MIT license header or explicit opt-out in every owned C# source."""

from __future__ import annotations

import argparse
import codecs
import io
import re
from concurrent.futures import ThreadPoolExecutor
from itertools import islice
from pathlib import Path

from cli_encoding import configure_output
from csharp_sources import source_files

ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOTS = ("Runtime", "Editor", "Tests")
HEADER_LINES = 20
MAX_HEADER_WORKERS = 8
OPT_OUT = "No license header required"
LICENSE = re.compile(r"\bMIT\b|MIT\s+License", re.IGNORECASE)


def header_is_valid(path: Path) -> bool:
    with path.open("rb") as binary:
        prefix = binary.read(4)
        binary.seek(0)
        encoding = (
            "utf-32"
            if prefix.startswith((codecs.BOM_UTF32_LE, codecs.BOM_UTF32_BE))
            else "utf-16"
            if prefix.startswith((codecs.BOM_UTF16_LE, codecs.BOM_UTF16_BE))
            else "utf-8-sig"
        )
        with io.TextIOWrapper(binary, encoding=encoding) as stream:
            header = "".join(islice(stream, HEADER_LINES))
    return (
        not header or OPT_OUT.casefold() in header.casefold() or LICENSE.search(header) is not None
    )


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    repository: Path = args.repo_root
    try:
        files = source_files(repository, SOURCE_ROOTS)
        missing: list[str] = []
        with ThreadPoolExecutor(max_workers=MAX_HEADER_WORKERS) as workers:
            for path, valid in zip(files, workers.map(header_is_valid, files), strict=True):
                relative = path.relative_to(repository).as_posix()
                if args.verbose:
                    print(f"[lint-license-headers] Checking: {relative}")
                if not valid:
                    missing.append(relative)
        for relative in missing:
            print(f"[lint-license-headers] Missing MIT license header: {relative}")
        if missing:
            print(f"[lint-license-headers] {len(missing)} missing of {len(files)} checked.")
            return 1
        print(f"[lint-license-headers] All {len(files)} files have proper MIT license headers!")
        return 0
    except (OSError, UnicodeError, ValueError) as error:
        print(f"[lint-license-headers] {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
