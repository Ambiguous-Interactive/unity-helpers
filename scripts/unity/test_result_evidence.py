"""Qualify strict NUnit and TRX reports for WProto evidence."""

from __future__ import annotations

import argparse
import codecs
import json
import stat
import sys
import xml.etree.ElementTree as ET
from itertools import chain
from pathlib import Path
from typing import TYPE_CHECKING, TypedDict

if TYPE_CHECKING:
    from collections.abc import Iterator

MAX_BYTES = 32 * 1024 * 1024
TRX = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
PASSED = ("Completed", "Passed")
SIZE_ERROR = "Test evidence XML must be a nonempty file of at most 32 MiB."


class Case(TypedDict):
    Fixture: str
    FullName: str
    Result: str
    AssemblyPath: str
    TestId: str
    ExecutionId: str
    AdapterTypeName: str


class Evidence(TypedDict):
    RunId: str
    StartedUtc: str
    CompletedUtc: str
    Cases: list[Case]
    Total: int
    Passed: int
    Failed: int
    Skipped: int
    Inconclusive: int


class _Tree(ET.TreeBuilder):
    def doctype(self, _name: str, _public_id: str | None, _system_id: str | None) -> None:
        raise ValueError("Test evidence XML must not declare a document type.")


def _root(path: Path) -> ET.Element:
    info = path.stat()
    if not stat.S_ISREG(info.st_mode) or not 1 <= info.st_size <= MAX_BYTES:
        raise ValueError(SIZE_ERROR)
    parser = ET.XMLParser(target=_Tree())  # noqa: S314 - rejects DTDs.
    with path.open("rb") as stream:
        first = stream.read(64 * 1024)
        marker = first[:4]
        encoding = {
            codecs.BOM_UTF32_LE: "utf-32",
            codecs.BOM_UTF32_BE: "utf-32",
            b"<\x00\x00\x00": "utf-32-le",
            b"\x00\x00\x00<": "utf-32-be",
        }.get(marker)
        decoder = codecs.getincrementaldecoder(encoding)() if encoding else None
        size = 0
        for block in chain((first,), iter(lambda: stream.read(64 * 1024), b"")):
            size += len(block)
            if size > MAX_BYTES:
                raise ValueError(SIZE_ERROR)
            parser.feed(decoder.decode(block) if decoder else block)
        if decoder:
            parser.feed(decoder.decode(b"", final=True))
    return parser.close()


def _counts(
    node: ET.Element, label: str, size: int, passed: tuple[str, ...], other: tuple[str, ...]
) -> None:
    for key in passed + other:
        if node.get(key) != (str(size) if key in passed else "0"):
            raise ValueError(f"{label} summary differs from passed cases: {key}.")


def _nunit_leaves(root: ET.Element) -> Iterator[tuple[ET.Element, str]]:
    stack = [(iter((root,)), "")]
    while stack:
        children, assembly = stack[-1]
        node = next(children, None)
        if node is None:
            stack.pop()
            continue
        if node.tag == "failure" or (node.tag == "test-suite" and node.get("result") != "Passed"):
            raise ValueError("NUnit root or fixture lifecycle did not pass.")
        if node.tag == "test-case":
            yield node, assembly
        if node.tag == "test-suite" and node.get("type") == "Assembly":
            assembly = node.get("fullname", "")
        if len(node):
            stack.append((iter(node), assembly))


def _nunit(root: ET.Element) -> Evidence:
    if root.tag != "test-run" or root.get("result") != "Passed":
        raise ValueError("NUnit root or fixture lifecycle did not pass.")
    names: set[str] = set()
    ids: set[str] = set()
    cases: list[Case] = []
    for leaf, assembly in _nunit_leaves(root):
        name = leaf.get("fullname", "")
        test_id = leaf.get("id", "")
        if (
            not name.strip()
            or name in names
            or leaf.get("result") != "Passed"
            or (test_id.strip() and test_id in ids)
        ):
            raise ValueError("NUnit case identity is missing, duplicated, or not passed.")
        names.add(name)
        if test_id.strip():
            ids.add(test_id)
        cases.append(
            Case(
                Fixture=leaf.get("classname", ""),
                FullName=name,
                Result="Passed",
                AssemblyPath=assembly,
                TestId=test_id,
                ExecutionId="",
                AdapterTypeName="",
            )
        )
    _counts(
        root,
        "NUnit",
        len(cases),
        ("total", "testcasecount", "passed"),
        ("failed", "skipped", "inconclusive"),
    )
    return _evidence(root, root.get("start-time", ""), root.get("end-time", ""), cases)


def _trx_index(root: ET.Element, parent: str, tag: str, key: str) -> dict[str, ET.Element]:
    index: dict[str, ET.Element] = {}
    for container in root.findall(TRX + parent):
        for element in container:
            if element.tag != TRX + tag:
                raise ValueError("TRX definitions, entries and results are not one-to-one.")
            identity = element.get(key, "")
            if not identity.strip() or identity in index:
                raise ValueError(f"TRX {tag} identity is missing or duplicated.")
            index[identity] = element
    return index


def _trx_case(definition: ET.Element, execution: str, test_id: str, display: str) -> Case:
    methods = definition.findall(TRX + "TestMethod")
    executions = definition.findall(TRX + "Execution")
    if (
        len(methods) != 1
        or len(executions) != 1
        or executions[0].get("id") != execution
        or definition.get("name") != display
        or any(not methods[0].get(key, "").strip() for key in ("className", "name", "codeBase"))
    ):
        raise ValueError("TRX definition metadata or execution differs from its result.")
    method = methods[0]
    return Case(
        Fixture=method.attrib["className"],
        FullName=display,
        Result="Passed",
        AssemblyPath=method.attrib["codeBase"],
        TestId=test_id,
        ExecutionId=execution,
        AdapterTypeName=method.get("adapterTypeName", ""),
    )


def _trx(root: ET.Element) -> Evidence:
    if root.tag != TRX + "TestRun":
        raise ValueError("TRX TestRun root or namespace is missing.")
    summaries = root.findall(TRX + "ResultSummary")
    if (
        len(summaries) != 1
        or summaries[0].get("outcome") not in PASSED
        or any(row.get("outcome") not in PASSED for row in root.iter(TRX + "RunInfo"))
        or next(root.iter(TRX + "ErrorInfo"), None) is not None
    ):
        raise ValueError("TRX summary or lifecycle did not pass.")
    definitions = _trx_index(root, "TestDefinitions", "UnitTest", "id")
    entries = _trx_index(root, "TestEntries", "TestEntry", "executionId")
    results = _trx_index(root, "Results", "UnitTestResult", "executionId")
    if len(definitions) != len(results) or len(entries) != len(results):
        raise ValueError("TRX definitions, entries and results are not one-to-one.")
    cases: list[Case] = []
    names: set[tuple[str, str]] = set()
    ids: set[str] = set()
    for execution, row in results.items():
        test_id = row.get("testId", "")
        display = row.get("testName", "")
        if (
            not test_id.strip()
            or test_id in ids
            or not display.strip()
            or row.get("outcome") != "Passed"
        ):
            raise ValueError("TRX result identity is missing, duplicated, or not passed.")
        ids.add(test_id)
        definition = definitions.get(test_id)
        entry = entries.get(execution)
        if definition is None or entry is None or entry.get("testId") != test_id:
            raise ValueError("TRX result does not join its definition and execution entry.")
        case = _trx_case(definition, execution, test_id, display)
        identity = case["Fixture"], case["FullName"]
        if identity in names:
            raise ValueError("TRX fixture and case name are duplicated.")
        names.add(identity)
        cases.append(case)
    counters = summaries[0].findall(TRX + "Counters")
    if len(counters) != 1:
        raise ValueError("TRX counters are missing or duplicated.")
    _counts(
        counters[0],
        "TRX",
        len(cases),
        ("total", "executed", "passed"),
        (
            "failed",
            "error",
            "timeout",
            "aborted",
            "inconclusive",
            "passedButRunAborted",
            "notRunnable",
            "notExecuted",
            "disconnected",
            "warning",
            "completed",
            "inProgress",
            "pending",
        ),
    )
    times = root.findall(TRX + "Times")
    if len(times) != 1:
        raise ValueError("TRX run times are missing or duplicated.")
    return _evidence(root, times[0].get("start", ""), times[0].get("finish", ""), cases)


def _evidence(root: ET.Element, started: str, completed: str, cases: list[Case]) -> Evidence:
    if not cases:
        raise ValueError("Test evidence contains no passed cases.")
    return Evidence(
        RunId=root.get("id", ""),
        StartedUtc=started,
        CompletedUtc=completed,
        Cases=cases,
        Total=len(cases),
        Passed=len(cases),
        Failed=0,
        Skipped=0,
        Inconclusive=0,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--format", required=True, type=str.lower, choices=("nunit", "trx"))
    parser.add_argument("path", type=Path)
    arguments = parser.parse_args()
    try:
        root = _root(arguments.path)
        evidence = _nunit(root) if arguments.format == "nunit" else _trx(root)
    except (OSError, ET.ParseError, ValueError, LookupError) as error:
        print(f"Test evidence could not be read: {error}", file=sys.stderr)
        return 1
    print(json.dumps(evidence, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
