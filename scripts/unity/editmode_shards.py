"""Partition selected EditMode assemblies and combine independently verified NUnit results."""

from __future__ import annotations

import copy
import math
import re
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path


def partition_assemblies(profile: str, count: int) -> list[str]:
    """Assign every selected assembly once, deterministically, without splitting fixtures."""
    names = profile.split(";")
    if count not in (1, 2):
        raise ValueError("EditMode supports one or two shards")
    if (
        any(re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.-]*", name) is None for name in names)
        or len({name.casefold() for name in names}) != len(names)
        or len(names) < count
    ):
        raise ValueError("EditMode shards require distinct assembly names and nonempty partitions")
    if count == 1:
        return [profile]
    ordered = sorted(names)
    return [";".join(ordered[index::count]) for index in range(count)]


def _outcome(node: ET.Element) -> str:
    """Normalize NUnit outcome suffixes consistently for cases and suites."""
    return node.get("result", "").split(":", 1)[0].split("(", 1)[0]


def merge_results(
    inputs: list[Path], destination: Path, *, selected_assemblies: set[str] | None = None
) -> None:
    """Publish a combined result only when every shard ran passing tests without failed suites."""
    if len(inputs) < 2 or len(set(inputs)) != len(inputs):
        raise ValueError("Combining EditMode results requires distinct shard inputs")
    counts: dict[str, int] = dict.fromkeys(
        ("total", "passed", "failed", "skipped", "inconclusive", "asserts"), 0
    )
    merged = ET.Element("test-run", {"id": "0", "result": "Passed"})
    assemblies: set[str] = set()
    duration = 0.0
    starts: list[str] = []
    ends: list[str] = []
    next_id = 1
    for path in inputs:
        try:
            root = ET.parse(path).getroot()  # noqa: S314 - local NUnit output or generated test fixtures.
        except ET.ParseError as error:
            raise ValueError(f"Malformed shard XML: {path}") from error
        if root.tag != "test-run":
            raise ValueError(f"Missing NUnit test-run root: {path}")
        cases = list(root.iter("test-case"))
        shard_counts = {key: int(root.get(key, "0")) for key in counts}
        leaf_counts = dict.fromkeys(("Passed", "Failed", "Skipped", "Inconclusive"), 0)
        for case in cases:
            state = _outcome(case)
            if state not in leaf_counts:
                raise ValueError(f"Shard has an unknown case outcome: {path}")
            leaf_counts[state] += 1
        if (
            not cases
            or len(cases) != shard_counts["total"]
            or shard_counts["passed"] <= 0
            or shard_counts["failed"] != 0
            or any(value < 0 for value in shard_counts.values())
            or sum(shard_counts[key] for key in ("passed", "failed", "skipped", "inconclusive"))
            != len(cases)
            or any(
                _outcome(node) not in ("Passed", "Skipped", "Inconclusive")
                for node in root.iter()
                if node.tag in ("test-run", "test-suite", "test-case")
            )
            or any(shard_counts[state.lower()] != value for state, value in leaf_counts.items())
        ):
            raise ValueError(f"Shard has missing, failed, or inconsistent NUnit results: {path}")
        observed = {
            node.get("name", "")
            for node in root.iter("test-suite")
            if node.get("type") == "Assembly"
        }
        if not observed or "" in observed or assemblies.intersection(observed):
            raise ValueError(f"Shard has missing or overlapping test assemblies: {path}")
        assemblies.update(observed)
        seconds = float(root.get("duration", "nan"))
        if not math.isfinite(seconds) or seconds < 0:
            raise ValueError(f"Invalid shard duration: {path}")
        duration += seconds
        starts.append(root.get("start-time", ""))
        ends.append(root.get("end-time", ""))
        for key, value in shard_counts.items():
            counts[key] += value
        for child in root:
            copied = copy.deepcopy(child)
            for node in copied.iter():
                if node.tag in ("test-suite", "test-case"):
                    node.set("id", str(next_id))
                    next_id += 1
            merged.append(copied)
    if selected_assemblies is not None and not {
        name.removesuffix(".dll") for name in assemblies
    }.issubset(selected_assemblies):
        raise ValueError("Shard results contain unselected assemblies")
    for key, value in counts.items():
        merged.set(key, str(value))
    merged.set("testcasecount", str(counts["total"]))
    merged.set("duration", str(duration))
    if all(starts):
        merged.set("start-time", min(starts))
    if all(ends):
        merged.set("end-time", max(ends))
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            dir=destination.parent, suffix=".xml", delete=False
        ) as stream:
            temporary = Path(stream.name)
            ET.ElementTree(merged).write(stream, encoding="utf-8", xml_declaration=True)
        temporary.replace(destination)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
