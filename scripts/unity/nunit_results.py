"""Summarize and qualify the NUnit result produced by a local Unity run."""

from __future__ import annotations

import argparse
import io
import json
import os
import sys
import xml.etree.ElementTree as ET
from dataclasses import asdict, dataclass, field
from hashlib import file_digest, sha256
from pathlib import Path
from typing import TYPE_CHECKING, Literal, TypedDict, cast

if TYPE_CHECKING:
    from typing import BinaryIO

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


def _read_root(path: str | BinaryIO) -> ET.Element:
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
    _assert_isolated_root(_read_root(results), fixture_names)


def _assert_isolated_root(root: ET.Element, fixture_names: tuple[str, ...]) -> None:
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


_SINGLETON_PREFIX = (
    "WallstopStudios.UnityHelpers.Tests.Utils.ScriptableObjectSingletonTests."
    "MetadataAssetTracksSingletonEntries("
)


@dataclass(frozen=True)
class _Failure:
    name: str
    message: str
    stack_trace: str


class _ResultCounts(TypedDict):
    total: int
    passed: int
    failed: int
    skipped: int


@dataclass(frozen=True)
class _CompositeVerdict:
    status: Literal["missing", "zero", "failed", "rejected", "passed"]
    counts: _ResultCounts | None = None
    failure_count: int = 0
    omitted_failure_details: int = 0
    failure_details: tuple[_Failure, ...] = ()
    diagnostic: str = ""
    report_hashes: dict[str, str] = field(default_factory=dict[str, str])


def _failed_nodes(root: ET.Element) -> list[ET.Element]:
    failed_cases = [case for case in root.iter("test-case") if case.get("result") == "Failed"]
    failed_suites = [
        suite
        for suite in root.iter("test-suite")
        if suite.get("result") == "Failed"
        and suite.get("site") != "Child"
        and suite.find("failure") is not None
    ]
    return failed_cases + failed_suites


def _failure_details(nodes: list[ET.Element]) -> tuple[tuple[_Failure, ...], int]:
    seen: set[str] = set()
    details: list[_Failure] = []
    for node in nodes:
        name = node.get("fullname") or node.get("name") or "(unnamed test)"
        if name in seen:
            continue
        seen.add(name)
        if len(details) < 50:
            message = node.find("failure/message")
            stack = node.find("failure/stack-trace")
            details.append(
                _Failure(
                    name,
                    "" if message is None else "".join(message.itertext()),
                    "" if stack is None else "".join(stack.itertext()),
                )
            )
    return tuple(details), len(seen) - len(details)


def _required_case_passed(root: ET.Element, required_name: str) -> bool:
    identities: set[str] = set()
    required_results: list[str] = []
    for case in root.iter("test-case"):
        name = case.get("fullname", "")
        if required_name and name == required_name:
            required_results.append(case.get("result", ""))
        if name.startswith(_SINGLETON_PREFIX):
            if name in identities:
                raise ValueError(f"Duplicate singleton metadata scenario identity: {name}")
            identities.add(name)
    return not required_name or required_results == ["Passed"]


def _capture_report(path: Path, receipts: dict[Path, bytes]) -> ET.Element:
    payload = path.read_bytes()
    receipts[path] = sha256(payload).digest()
    return _read_root(io.BytesIO(payload))


def _verify_composite(
    directory: Path, required_name: str, isolated: dict[str, tuple[str, ...]]
) -> _CompositeVerdict:
    receipts: dict[Path, bytes] = {}
    try:
        root = _capture_report(directory / "results.xml", receipts)
    except (ET.ParseError, OSError, ValueError):
        return _CompositeVerdict("missing")
    counts = _ResultCounts(
        total=int(_count(root, "total", "0")),
        passed=int(_count(root, "passed", "0")),
        failed=int(_count(root, "failed", "0")),
        skipped=int(_count(root, "skipped", "0")),
    )
    required_passed = _required_case_passed(root, required_name)
    if not counts["total"]:
        return _CompositeVerdict("zero", counts)
    failures = _failed_nodes(root)
    if counts["failed"] or failures:
        details, omitted = _failure_details(failures)
        return _CompositeVerdict(
            "failed",
            counts,
            failure_count=len(failures),
            failure_details=details,
            omitted_failure_details=omitted,
        )
    if not required_passed:
        return _CompositeVerdict(
            "rejected",
            counts,
            diagnostic=f"Required native test '{required_name}' must have exactly one passed result.",
        )
    for relative, fixtures in isolated.items():
        path = directory / relative
        try:
            _assert_isolated_root(_capture_report(path, receipts), fixtures)
        except (ET.ParseError, OSError, ValueError) as error:
            return _CompositeVerdict("rejected", counts, diagnostic=f"{relative}: {error}")
    report_hashes: dict[str, str] = {}
    for path, digest in receipts.items():
        with path.open("rb") as stream:
            if file_digest(stream, "sha256").digest() != digest:
                raise ValueError(f"The NUnit report changed during verification: {path}")
        report_hashes[path.relative_to(directory).as_posix()] = digest.hex()
    return _CompositeVerdict("passed", counts, report_hashes=report_hashes)


def _composite_main(arguments: list[str]) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("--required-name", default="")
    parser.add_argument("--isolated", nargs="+", action="append", default=[])
    args = parser.parse_args(arguments)
    isolated: dict[str, tuple[str, ...]] = {}
    for relative, *fixtures in cast("list[list[str]]", args.isolated):
        if relative in isolated:
            parser.error(f"Duplicate isolated report path: {relative}")
        isolated[relative] = tuple(fixtures)
    try:
        verdict = _verify_composite(args.directory, args.required_name, isolated)
    except (OSError, ValueError) as error:
        verdict = _CompositeVerdict("rejected", diagnostic=str(error))
    print(json.dumps(asdict(verdict), ensure_ascii=True))
    return 0 if verdict.status == "passed" else 1


def main() -> int:
    configure_output()
    if sys.argv[1:2] == ["--verify-composite"]:
        return _composite_main(sys.argv[2:])
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
