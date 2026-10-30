"""Check bundled assembly importers against Unity's version-dependent BCL ownership."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GUIDE = "docs/guides/bundled-assembly-conflicts.md"
UNITY_PROVIDED_CONSTRAINT = "!UNITY_6000_5_OR_NEWER"
EXPECTED_CONSTRAINTS: dict[str, list[str]] = {
    "Microsoft.Bcl.AsyncInterfaces": [UNITY_PROVIDED_CONSTRAINT],
    "System.Text.Encodings.Web": [UNITY_PROVIDED_CONSTRAINT],
    "System.Text.Json": [UNITY_PROVIDED_CONSTRAINT],
    "System.IO.Pipelines": [],
}
EXPECTED_NAMES = {name.casefold(): name for name in EXPECTED_CONSTRAINTS}
DELIBERATELY_NOT_SHIPPED = ("System.Runtime.CompilerServices.Unsafe",)


def define_constraints(meta: Path) -> list[str]:
    constraints: list[str] = []
    in_block = False
    for line in meta.read_text(encoding="utf-8-sig").splitlines():
        flow = re.fullmatch(r"\s*defineConstraints:\s*\[(.*)\]\s*", line)
        if flow:
            return [value for item in flow[1].split(",") if (value := item.strip().strip("'\""))]
        if re.fullmatch(r"\s*defineConstraints:\s*", line):
            in_block = True
            continue
        if in_block:
            item = re.fullmatch(r"\s*-\s*'?([^'\r\n]+?)'?\s*", line)
            if item:
                constraints.append(item[1])
                continue
            break
    return constraints


def check(binaries: Path, *, verbose: bool = False) -> int:
    if not binaries.is_dir():
        print(f"::error::Bundled assembly directory not found: {binaries}")
        return 1
    dlls = sorted(
        path for path in binaries.iterdir() if path.is_file() and path.suffix.lower() == ".dll"
    )
    if not dlls:
        print(
            f"::error file={GUIDE}::No DLLs found under Runtime/Binaries. If the bundled assemblies were removed deliberately, retire this linter in the same commit."
        )
        return 1
    failed = False
    for dll in dlls:
        name = dll.stem
        relative = f"Runtime/Binaries/{dll.name}"
        if name.casefold() in {item.casefold() for item in DELIBERATELY_NOT_SHIPPED}:
            print(
                f"::error file={relative}::{name} is recorded as deliberately not shipped -- every supported editor provides it, and a competing copy is what destabilizes resolution. Remove it, or move it out of the deliberately-not-shipped list with evidence in {GUIDE}."
            )
            failed = True
            continue
        canonical = EXPECTED_NAMES.get(name.casefold())
        if canonical is None:
            print(
                f"::error file={relative}::{name} is not classified by scripts/lint_bundled_assemblies.py. Decide whether Unity ships this name -- if it does, constrain the importer to {UNITY_PROVIDED_CONSTRAINT} and document it in {GUIDE}; if it does not, add it with an empty constraint list."
            )
            failed = True
            continue
        meta = dll.with_name(dll.name + ".meta")
        if not meta.is_file():
            print(f"::error file={relative}::Missing importer metadata: {relative}.meta")
            failed = True
            continue
        expected = EXPECTED_CONSTRAINTS[canonical]
        actual = define_constraints(meta)
        if expected != actual:
            expected_text = ", ".join(expected) or "(none)"
            actual_text = ", ".join(actual) or "(none)"
            print(
                f"::error file={relative}.meta::{name} must carry defineConstraints [{expected_text}] but carries [{actual_text}]. A regenerated importer that drops the constraint lets this copy compete with Unity's own; see {GUIDE}."
            )
            failed = True
            continue
        if verbose:
            print(
                f"[lint-bundled-assemblies] {name} importer constraints match ({', '.join(actual) or 'unconstrained'})."
            )
    present = {dll.stem.casefold() for dll in dlls}
    for name in sorted(EXPECTED_CONSTRAINTS):
        if name.casefold() not in present:
            print(
                f"::error file={GUIDE}::{name} is classified as shipped but no longer exists under Runtime/Binaries. Dropping it breaks consumers on editors that do not provide it; if the removal is deliberate, remove its classification in the same commit."
            )
            failed = True
    if verbose:
        for name in DELIBERATELY_NOT_SHIPPED:
            print(f"[lint-bundled-assemblies] {name} remains deliberately unshipped.")
    if failed:
        return 1
    print(
        f"[lint-bundled-assemblies] OK: {len(dlls)} bundled assemblies classified and constrained."
    )
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--binaries-root", "-BinariesRoot", type=Path, default=ROOT / "Runtime/Binaries"
    )
    parser.add_argument("--verbose", "-VerboseOutput", action="store_true")
    args = parser.parse_args()
    try:
        return check(args.binaries_root, verbose=args.verbose)
    except (OSError, UnicodeError) as error:
        print(f"::error::{error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
