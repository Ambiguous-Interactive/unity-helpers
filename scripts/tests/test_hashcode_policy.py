"""Keep production hash composition deterministic with Objects.HashCode."""

from __future__ import annotations

import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from csharp_sources import mask_noise, source_files

ROOT = Path(__file__).resolve().parents[2]
FORBIDDEN = re.compile(
    r"\bHashCode\s*\.\s*(?:Combine|Add)\b|\bnew\s+(?:System\s*\.\s*)?HashCode\s*\("
)


def findings() -> list[str]:
    found: list[str] = []
    for path in source_files(ROOT, ("Runtime", "Editor")):
        masked = mask_noise(path.read_text(encoding="utf-8"))
        for match in FORBIDDEN.finditer(masked):
            line = masked.count("\n", 0, match.start()) + 1
            found.append(f"{path.relative_to(ROOT).as_posix()}:{line}: {match.group(0)}")
    return found


def test_production_sources_use_objects_hashcode() -> None:
    found = findings()
    assert found == [], "Use Objects.HashCode instead:\n" + "\n".join(found)


def test_detector_rejects_each_forbidden_shape() -> None:
    assert FORBIDDEN.search("return HashCode.Combine(first, second);") is not None
    assert FORBIDDEN.search("return System.HashCode.Combine(first);") is not None
    assert FORBIDDEN.search("HashCode hash = new HashCode();") is not None
    assert FORBIDDEN.search("HashCode hash = new System.HashCode();") is not None
    assert FORBIDDEN.search("return Objects.HashCode(first, second);") is None
    assert FORBIDDEN.search("return value.GetHashCode();") is None


def test_detector_ignores_comments_and_literals() -> None:
    comment = "// HashCode.Combine(first, second)\nreturn 0;\n"
    literal = 'string text = "HashCode.Combine(first, second)";\n'
    block = "/* new HashCode() */\nreturn 0;\n"
    assert FORBIDDEN.search(mask_noise(comment)) is None
    assert FORBIDDEN.search(mask_noise(literal)) is None
    assert FORBIDDEN.search(mask_noise(block)) is None
