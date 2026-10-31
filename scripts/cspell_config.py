"""Read and update spelling buckets while preserving unrelated configuration."""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import cast

ROOT = Path(__file__).resolve().parents[1]


@dataclass(frozen=True)
class Bucket:
    name: str
    owner: dict[str, object]

    def words(self) -> list[str]:
        value = self.owner.get("words", [])
        if not isinstance(value, list) or not all(
            isinstance(word, str) for word in cast("list[object]", value)
        ):
            raise ValueError(f"{self.name} words must be a string array")
        return cast("list[str]", value)

    def replace(self, words: list[str]) -> None:
        self.owner["words"] = words


def read_config(path: Path) -> tuple[str, dict[str, object]]:
    raw = path.read_bytes().decode("utf-8")
    value: object = json.loads(raw)
    if not isinstance(value, dict):
        raise ValueError("cspell.json must be an object")
    return raw, cast("dict[str, object]", value)


def buckets(config: dict[str, object]) -> list[Bucket]:
    values = config.get("dictionaryDefinitions", [])
    if not isinstance(values, list):
        raise ValueError("dictionaryDefinitions must be an array")
    result: list[Bucket] = []
    for value in cast("list[object]", values):
        if not isinstance(value, dict):
            raise ValueError("dictionaryDefinitions entries must be objects")
        dictionary = cast("dict[str, object]", value)
        name = dictionary.get("name")
        if not isinstance(name, str) or not name:
            raise ValueError("Each spelling dictionary must have a name")
        result.append(Bucket(f"dictionary:{name}", dictionary))
    result.append(Bucket("root:words", config))
    for bucket in result:
        bucket.words()
    return result


def cross_duplicates(groups: list[Bucket]) -> list[tuple[str, list[str]]]:
    locations: dict[str, list[str]] = {}
    for group in groups:
        for word in group.words():
            locations.setdefault(word.lower(), []).append(group.name)
    return [(word, names) for word, names in locations.items() if len(names) > 1]


def case_redundant(words: list[str]) -> list[tuple[str, list[str]]]:
    entries: dict[str, tuple[str, list[str]]] = {}
    for word in words:
        lower = word.lower()
        if lower in entries:
            entries[lower][1].append(word)
        else:
            entries[lower] = (word, [])
    return [(first, duplicates) for first, duplicates in entries.values() if duplicates]


def deduplicate(words: list[str]) -> list[str]:
    seen: set[str] = set()
    result: list[str] = []
    for word in words:
        lower = word.lower()
        if lower not in seen:
            seen.add(lower)
            result.append(word)
    return result


def write_config(path: Path, raw: str, config: dict[str, object]) -> None:
    serialized = json.dumps(config, ensure_ascii=False, indent=2) + "\n"
    if json.loads(serialized) != config:
        raise ValueError("JSON round-trip validation failed")
    newline = "\r\n" if "\r\n" in raw else "\n"
    path.write_bytes(serialized.replace("\n", newline).encode("utf-8"))
