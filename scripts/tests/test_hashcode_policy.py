"""Keep production hash composition deterministic with Objects.HashCode."""

from __future__ import annotations

import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from csharp_sources import mask_noise, source_files

ROOT = Path(__file__).resolve().parents[2]
FORBIDDEN = re.compile(
    r"\bSystem\s*\.\s*HashCode\b"
    r"|\busing\s+@?[A-Za-z_]\w*\s*=\s*(?:global\s*::\s*)?System\s*;"
    r"|(?<!\.)\bHashCode\b(?!\s*<)"
)


def scan(text: str) -> list[str]:
    masked = mask_noise(text)
    found: list[str] = []
    for match in FORBIDDEN.finditer(masked):
        if masked[: match.start()].rstrip().endswith("."):
            continue
        found.append(match.group(0))
    return found


def findings(repository: Path = ROOT, roots: tuple[str, ...] = ("Runtime", "Editor")) -> list[str]:
    found: list[str] = []
    for path in source_files(repository, roots):
        masked = mask_noise(path.read_text(encoding="utf-8"))
        for match in FORBIDDEN.finditer(masked):
            if masked[: match.start()].rstrip().endswith("."):
                continue
            line = masked.count("\n", 0, match.start()) + 1
            found.append(f"{path.relative_to(repository).as_posix()}:{line}: {match.group(0)}")
    return found


def test_production_sources_use_objects_hashcode() -> None:
    found = findings()
    assert found == [], "Use Objects.HashCode instead:\n" + "\n".join(found)


def test_production_scan_reports_a_forbidden_source(tmp_path: Path) -> None:
    runtime = tmp_path / "Runtime"
    runtime.mkdir()
    (runtime / "Bad.cs").write_text(
        "internal static class Bad { internal static int Value(int a, int b) "
        "{ return System.HashCode.Combine(a, b); } }",
        encoding="utf-8",
    )
    found = findings(tmp_path, ("Runtime",))
    assert any("Bad.cs" in item and "HashCode" in item for item in found), found


def test_detector_rejects_each_forbidden_shape() -> None:
    assert FORBIDDEN.search("return HashCode.Combine(first, second);") is not None
    assert FORBIDDEN.search("return System.HashCode.Combine(first);") is not None
    assert FORBIDDEN.search("HashCode hash = new HashCode();") is not None
    assert FORBIDDEN.search("HashCode hash = new System.HashCode();") is not None
    assert FORBIDDEN.search("HashCode hash = default;") is not None
    assert FORBIDDEN.search("System.HashCode hash = default(System.HashCode);") is not None
    assert FORBIDDEN.search("HashCode hash = new();") is not None
    assert FORBIDDEN.search("private HashCode _hash;") is not None
    assert FORBIDDEN.search("var hash = default(System.HashCode);") is not None
    assert FORBIDDEN.search("var hash = default(HashCode);") is not None
    assert FORBIDDEN.search("var hash = new global::System.HashCode();") is not None
    assert FORBIDDEN.search("global::System.HashCode hash = default;") is not None
    assert FORBIDDEN.search("var hash = new System.HashCode { };") is not None
    assert FORBIDDEN.search("using HC = System.HashCode;") is not None
    assert FORBIDDEN.search("System.HashCode? hash = new System.HashCode();") is not None
    assert FORBIDDEN.search("using static System.HashCode;") is not None
    assert FORBIDDEN.search("private HashCode first, second;") is not None
    assert FORBIDDEN.search("using Sys = System;") is not None
    assert FORBIDDEN.search("using Sys = global::System;") is not None
    assert FORBIDDEN.search("using @Sys = global::System;") is not None
    assert scan("int hash = Objects. HashCode(first, second);") == []
    assert scan("using Sys = System; int hash = Sys.HashCode.Combine(first, second);") != []
    assert FORBIDDEN.search("return Objects.HashCode(first, second);") is None
    assert FORBIDDEN.search("return value.GetHashCode();") is None


def test_detector_ignores_comments_and_literals() -> None:
    comment = "// HashCode.Combine(first, second)\nreturn 0;\n"
    literal = 'string text = "HashCode.Combine(first, second)";\n'
    block = "/* new HashCode() */\nreturn 0;\n"
    assert FORBIDDEN.search(mask_noise(comment)) is None
    assert FORBIDDEN.search(mask_noise(literal)) is None
    assert FORBIDDEN.search(mask_noise(block)) is None
