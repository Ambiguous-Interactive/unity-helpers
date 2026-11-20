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


def assert_isolated_fixtures(results: str, fixture_names: tuple[str, ...]) -> None:
    root = _read_root(results)
    cases = list(root.iter("test-case"))
    if not cases:
        raise ValueError("The isolated compilation fixture produced no test cases.")

    required = set(fixture_names)
    if len(required) != len(fixture_names) or any(not name.strip() for name in required):
        raise ValueError("Required isolated fixture names must be nonempty and unique.")
    if not required:
        raise ValueError("At least one isolated fixture must be required.")

    observed: set[str] = set()
    for case in cases:
        name = case.get("classname", "")
        if name not in required or case.get("result") != "Passed":
            raise ValueError(
                "Every isolated compilation case must belong to a required fixture and pass."
            )
        observed.add(name)
    if observed != required:
        raise ValueError("A required isolated fixture did not execute.")

    try:
        failed = float(root.get("failed", "0"))
    except ValueError:
        # Match the existing XPath comparison, where an absent numeric value is not positive.
        failed = 0
    if (
        root.get("result") == "Failed"
        or failed > 0
        or any(suite.get("result") == "Failed" for suite in root.iter("test-suite"))
    ):
        raise ValueError("Isolated fixture roots and suites must pass.")


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
        outcome_count = int(passed) + failed_count + int(skipped) + int(inconclusive)
        if total_count < outcome_count:
            raise ValueError("NUnit outcome counts exceed the total")
        if total_count > outcome_count:
            raise ValueError("NUnit outcome counts fall short of the total")
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
    if len(sys.argv) >= 3 and sys.argv[1] == "--isolated-fixtures":
        try:
            assert_isolated_fixtures(sys.argv[2], tuple(sys.argv[3:]))
        except (ET.ParseError, OSError, ValueError) as error:
            print(error)
            return 1
        return 0
    if len(sys.argv) not in (3, 4):
        print(
            "Usage: nunit_results.py <results.xml> <platform> [published.xml]",
            file=sys.stderr,
        )
        return 2
    results, platform = sys.argv[1:3]
    status = summarize(results, platform)
    if len(sys.argv) == 4:
        published = sys.argv[3]
        source = Path(results)
        try:
            source.replace(published)
        except OSError as error:
            print(
                f"ERROR: Could not publish {published}. The new report is at {results}. {error}",
                file=sys.stderr,
            )
            return 1
    return status


if __name__ == "__main__":
    raise SystemExit(main())
