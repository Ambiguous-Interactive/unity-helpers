"""Prove complete assembly partitions and honest combined NUnit results."""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
from editmode_shards import merge_results, partition_assemblies


def result(path: Path, name: str, *, skipped: bool = False) -> Path:
    root = ET.Element(
        "test-run",
        {
            "id": "1",
            "result": "Passed",
            "total": "2" if skipped else "1",
            "passed": "1",
            "failed": "0",
            "skipped": "1" if skipped else "0",
            "inconclusive": "0",
            "duration": "2.5",
        },
    )
    suite = ET.SubElement(
        root, "test-suite", {"id": "2", "type": "Assembly", "name": name, "result": "Passed"}
    )
    ET.SubElement(suite, "test-case", {"id": "3", "fullname": name + ".Case", "result": "Passed"})
    if skipped:
        ET.SubElement(
            suite,
            "test-case",
            {"id": "4", "fullname": name + ".Skipped", "result": "Skipped:Ignored"},
        )
    ET.ElementTree(root).write(path, encoding="utf-8")
    return path


@pytest.mark.parametrize("count", [1, 2])
@pytest.mark.parametrize("size", [2, 3, 7, 28, 51])
def test_every_assembly_is_assigned_once_without_empty_shards(count: int, size: int) -> None:
    names = [f"Subject.Assembly{index}" for index in range(size)]
    profiles = partition_assemblies(";".join(names), count)
    assert len(profiles) == count
    assigned = [name for profile in profiles for name in profile.split(";")]
    assert sorted(assigned) == sorted(names)
    assert len(set(assigned)) == size
    assert all(profiles)
    if count == 2:
        assert profiles == partition_assemblies(";".join(reversed(names)), count)


@pytest.mark.parametrize("profile", ["", "A;A", ";A", "A;", "A;B\nC", "A;../B", " A;B", "A\x00;B"])
def test_invalid_profiles_are_rejected(profile: str) -> None:
    with pytest.raises(ValueError, match=r"EditMode|Shard|shard"):
        partition_assemblies(profile, 2)


@pytest.mark.parametrize("count", [0, 3, -1])
def test_unsupported_partition_counts_are_rejected(count: int) -> None:
    with pytest.raises(ValueError, match=r"EditMode|Shard|shard"):
        partition_assemblies("A;B", count)


def test_two_shards_need_two_assemblies() -> None:
    with pytest.raises(ValueError, match=r"EditMode|Shard|shard"):
        partition_assemblies("A", 2)


def test_merge_preserves_every_case_skip_and_original_file(tmp_path: Path) -> None:
    files = [
        result(tmp_path / "one.xml", "A.dll", skipped=True),
        result(tmp_path / "two.xml", "B.dll"),
    ]
    original = [path.read_bytes() for path in files]
    destination = tmp_path / "merged/results.xml"
    merge_results(files, destination)
    root = ET.parse(destination).getroot()  # noqa: S314 - generated test fixture.
    assert root.get("total") == "3"
    assert root.get("passed") == "2"
    assert root.get("skipped") == "1"
    assert root.get("duration") == "5.0"
    assert [case.get("fullname") for case in root.iter("test-case")] == [
        "A.dll.Case",
        "A.dll.Skipped",
        "B.dll.Case",
    ]
    identifiers = [node.get("id") for node in root.iter() if node.get("id") is not None]
    assert len(identifiers) == len(set(identifiers))
    assert [path.read_bytes() for path in files] == original


@pytest.mark.parametrize(
    "mutation",
    [
        "failed-root",
        "failed-suite",
        "failed-leaf",
        "empty",
        "all-skipped",
        "count-drift",
        "negative-count",
        "no-assembly",
        "nan-duration",
        "negative-duration",
        "unknown-root",
        "unknown-suite",
        "unknown-leaf",
        "leaf-count-drift",
    ],
)
def test_merge_refuses_false_passes_and_preserves_previous_output(
    tmp_path: Path, mutation: str
) -> None:
    first = result(tmp_path / "one.xml", "A.dll")
    second = result(tmp_path / "two.xml", "B.dll")
    root = ET.parse(second).getroot()  # noqa: S314 - generated test fixture.
    suite = next(root.iter("test-suite"))
    case = next(root.iter("test-case"))
    if mutation == "failed-root":
        root.set("result", "Failed")
    elif mutation == "failed-suite":
        suite.set("result", "Failed(Child)")
    elif mutation == "failed-leaf":
        case.set("result", "Failed")
    elif mutation == "empty":
        suite.remove(case)
    elif mutation == "all-skipped":
        root.set("passed", "0")
        root.set("skipped", "1")
        case.set("result", "Skipped")
    elif mutation == "count-drift":
        root.set("total", "2")
    elif mutation == "negative-count":
        root.set("inconclusive", "-1")
    elif mutation == "no-assembly":
        suite.set("type", "TestFixture")
    elif mutation == "nan-duration":
        root.set("duration", "nan")
    elif mutation == "negative-duration":
        root.set("duration", "-1")
    elif mutation == "unknown-root":
        root.set("result", "Aborted")
    elif mutation == "unknown-suite":
        suite.set("result", "Aborted")
    elif mutation == "unknown-leaf":
        case.set("result", "Aborted")
    elif mutation == "leaf-count-drift":
        case.set("result", "Skipped")
    ET.ElementTree(root).write(second, encoding="utf-8")
    destination = tmp_path / "results.xml"
    destination.write_text("previous evidence", encoding="utf-8")
    with pytest.raises(ValueError, match=r"EditMode|Shard|shard"):
        merge_results([first, second], destination)
    assert destination.read_text(encoding="utf-8") == "previous evidence"


def test_duplicate_assembly_cannot_cover_a_missing_partition(tmp_path: Path) -> None:
    files = [result(tmp_path / "one.xml", "A.dll"), result(tmp_path / "two.xml", "A.dll")]
    with pytest.raises(ValueError, match="overlapping"):
        merge_results(files, tmp_path / "results.xml")


@pytest.mark.parametrize("count", [0, 1, 2])
def test_missing_or_repeated_input_is_refused(tmp_path: Path, count: int) -> None:
    source = result(tmp_path / "one.xml", "A.dll")
    with pytest.raises(ValueError, match=r"EditMode|Shard|shard"):
        merge_results([source] * count, tmp_path / "results.xml")


def test_merge_refuses_unselected_assemblies_and_allows_inactive_selections(tmp_path: Path) -> None:
    files = [result(tmp_path / "one.xml", "A.dll"), result(tmp_path / "two.xml", "B.dll")]
    destination = tmp_path / "results.xml"
    merge_results(files, destination, selected_assemblies={"A", "B", "Inactive"})
    previous = destination.read_bytes()
    with pytest.raises(ValueError, match="unselected assemblies"):
        merge_results(files, destination, selected_assemblies={"A", "Missing"})
    assert destination.read_bytes() == previous
