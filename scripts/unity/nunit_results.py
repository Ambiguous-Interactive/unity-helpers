"""Summarize and qualify the NUnit result produced by a local Unity run."""

from __future__ import annotations

import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from cli_encoding import configure_output


class _NUnitTree(ET.TreeBuilder):
    def doctype(self, _name: str, _public_id: str | None, _system_id: str | None) -> None:
        raise ValueError("An NUnit result must not declare a document type")


def _count(root: ET.Element, field: str, default: str | None = None) -> str:
    value = root.get(field, default)
    if value is None or not value.isascii() or not value.isdecimal():
        raise ValueError(f"Expected a nonnegative decimal '{field}' count")
    return value


def _read_root(path: str) -> ET.Element:
    parser = ET.XMLParser(target=_NUnitTree())  # noqa: S314 - the target rejects document types.
    root = ET.parse(path, parser=parser).getroot()  # noqa: S314 - the target rejects document types.
    if root.tag != "test-run":
        raise ValueError("Expected an NUnit test-run root")
    return root


def _print_failed_tests(root: ET.Element) -> bool:
    found_failure = False
    for element in root.iter("test-case"):
        if element.get("result", "").lower() in ("failed", "error"):
            if not found_failure:
                print("\n    Failed tests:")
                found_failure = True
            name = element.get("fullname", element.get("name", "unknown"))
            for line in f"FAILED: {name}".split("\n"):
                print(f"      {line}")
    return found_failure


def summarize(results: str, platform: str) -> int:
    prefix = f"==> [run-tests] {platform}"
    print()
    try:
        if not os.path.isfile(results):  # noqa: PTH113 - preserve the caller's trailing separator.
            print(f"{prefix}: no results at {results} -- tests did not run.")
            return 1
        root = _read_root(results)
        total = _count(root, "total")
        passed = _count(root, "passed", "0")
        failed = _count(root, "failed")
        skipped = _count(root, "skipped", "0")
        inconclusive = _count(root, "inconclusive", "0")
        total_count = int(total)
        failed_count = int(failed)
        if total_count < int(passed) + failed_count + int(skipped) + int(inconclusive):
            raise ValueError("NUnit outcome counts exceed the total")
    except (ET.ParseError, OSError, ValueError) as error:
        print(f"{prefix}: invalid NUnit results at {results}: {error}")
        return 1

    print(f"{prefix} Test Results Summary:")
    print("    ----------------------------------------")
    print(f"    Total:   {total}")
    print(f"    Passed:  {passed}")
    print(f"    Failed:  {failed}")
    shown_skipped = skipped if "skipped" in root.attrib else inconclusive
    print(f"    Skipped: {shown_skipped}")
    found_failure = _print_failed_tests(root)
    print("    ----------------------------------------\n")

    status = 0
    if total_count == 0:
        print(
            f"{prefix}: 0 tests ran -- check assembly selection, the --filter "
            "expression, and the host project's testables."
        )
        status = 1
    if failed_count != 0:
        print(f"{prefix}: {failed} test(s) failed.")
        status = 1
    elif found_failure or root.get("result", "").lower() in ("failed", "error"):
        print(f"{prefix}: the NUnit outcome reports a failure with a zero failed count.")
        status = 1
    return status


def main() -> int:
    configure_output()
    if len(sys.argv) != 3:
        print("Usage: nunit_results.py <results.xml> <platform>", file=sys.stderr)
        return 2
    return summarize(sys.argv[1], sys.argv[2])


if __name__ == "__main__":
    raise SystemExit(main())
