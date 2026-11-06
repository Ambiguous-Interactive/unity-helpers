"""Preserve the original cache checker fixtures and exercise physical CLI boundaries."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_concurrent_cache_fill as lint

ROOT = Path(__file__).resolve().parents[2]
CASES: tuple[tuple[str, str, int, str], ...] = (
    (
        "GetOrAdd and TryAdd pass",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n        private static readonly ConcurrentDictionary<Type, byte> Unsupported = new();\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n            Unsupported.TryAdd(type, 0);\r\n            return Names.GetOrAdd(type, static t => t.FullName);\r\n        }\r\n    }\r\n}",
        0,
        "",
    ),
    (
        "Indexer fill after a miss fails",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n            if (!Names.TryGetValue(type, out string cached))\r\n            {\r\n                cached = type.FullName;\r\n                Names[type] = cached;\r\n            }\r\n\r\n            return cached;\r\n        }\r\n    }\r\n}",
        1,
        "",
    ),
    (
        "Indexer fill inside SINGLE_THREADED passes",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n    using System.Collections.Generic;\r\n\r\n    internal static class Cache\r\n    {\r\n#if SINGLE_THREADED\r\n        private static readonly Dictionary<Type, string> Names = new();\r\n#else\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n#endif\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n#if SINGLE_THREADED\r\n            if (!Names.TryGetValue(type, out string cached))\r\n            {\r\n                cached = type.FullName;\r\n                Names[type] = cached;\r\n            }\r\n\r\n            return cached;\r\n#else\r\n            return Names.GetOrAdd(type, static t => t.FullName);\r\n#endif\r\n        }\r\n    }\r\n}",
        0,
        "",
    ),
    (
        "Indexer fill in the #else branch still fails",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n    using System.Collections.Generic;\r\n\r\n    internal static class Cache\r\n    {\r\n#if SINGLE_THREADED\r\n        private static readonly Dictionary<Type, string> Names = new();\r\n#else\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n#endif\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n#if SINGLE_THREADED\r\n            return Names.TryGetValue(type, out string cached) ? cached : null;\r\n#else\r\n            Names[type] = type.FullName;\r\n            return Names[type];\r\n#endif\r\n        }\r\n    }\r\n}",
        1,
        "",
    ),
    (
        "Marked deliberate overwrite passes",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static void Register(Type type, string name)\r\n        {\r\n            Names[type] = name; // concurrent-overwrite: an explicit registration must beat inference\r\n        }\r\n    }\r\n}",
        0,
        "",
    ),
    (
        "Marker in the comment block above passes",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static void Register(Type type, string name)\r\n        {\r\n            // concurrent-overwrite: an explicit registration must replace whatever inference\r\n            // cached earlier, and that reason does not fit on the write line.\r\n            Names[type] = name;\r\n        }\r\n    }\r\n}",
        0,
        "",
    ),
    (
        "Marker in a /* */ block above passes",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static void Register(Type type, string name)\r\n        {\r\n            /*\r\n                concurrent-overwrite: an explicit registration must replace whatever inference\r\n                cached earlier. The repository requires this form for a multi-line reason, so a\r\n                walker that reads only `//` cannot see the marker at all.\r\n            */\r\n            Names[type] = name;\r\n        }\r\n    }\r\n}",
        0,
        "",
    ),
    (
        "A /* */ block with no marker still fails",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static void Register(Type type, string name)\r\n        {\r\n            /*\r\n                An explanation of what this method is for, which says nothing about why the\r\n                overwrite is safe.\r\n            */\r\n            Names[type] = name;\r\n        }\r\n    }\r\n}",
        1,
        "",
    ),
    (
        "A trailing /* */ does not inherit another method marker",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static void RegisterExplicit(Type type, string name)\r\n        {\r\n            /*\r\n                concurrent-overwrite: an explicit registration must replace whatever inference\r\n                cached earlier.\r\n            */\r\n            Names[type] = name;\r\n        }\r\n\r\n        internal static void RegisterInferred(Type type, string name)\r\n        {\r\n            Console.WriteLine(name); /* trace */\r\n            Names[type] = name;\r\n        }\r\n    }\r\n}",
        1,
        "",
    ),
    (
        "Marker cut off by a blank line does not exempt",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static void Register(Type type, string name)\r\n        {\r\n            // concurrent-overwrite: this comment belongs to something else entirely.\r\n\r\n            Names[type] = name;\r\n        }\r\n    }\r\n}",
        1,
        "",
    ),
    (
        "Wrapped generic declaration is still found",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<\r\n            Type,\r\n            Func<object>\r\n        > Factories = new();\r\n\r\n        internal static void Fill(Type type, Func<object> factory)\r\n        {\r\n            Factories[type] = factory;\r\n        }\r\n    }\r\n}",
        1,
        "",
    ),
    (
        "Non-static cache factory fails",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n            return Names.GetOrAdd(type, t => t.FullName);\r\n        }\r\n    }\r\n}",
        1,
        "",
    ),
    (
        "Static cache factory passes",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n            return Names.GetOrAdd(type, static t => t.FullName);\r\n        }\r\n    }\r\n}",
        0,
        "",
    ),
    (
        "Nested lambda inside the value is not a factory",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n    using System.Collections.Generic;\r\n\r\n    internal sealed class Holder<T>\r\n    {\r\n        internal Holder(Func<T> make, Action<T> onRelease) { }\r\n    }\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<\r\n            IComparer<int>,\r\n            Holder<SortedSet<int>>\r\n        > Pools = new();\r\n\r\n        internal static Holder<SortedSet<int>> Get(IComparer<int> comparer)\r\n        {\r\n            return Pools.GetOrAdd(\r\n                comparer,\r\n                static inComparer => new Holder<SortedSet<int>>(\r\n                    () => new SortedSet<int>(inComparer),\r\n                    onRelease: set => set.Clear()\r\n                )\r\n            );\r\n        }\r\n    }\r\n}",
        0,
        "",
    ),
    (
        "Comma inside a generic value type is not an argument split",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n    using System.Collections.Generic;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<\r\n            Type,\r\n            Dictionary<string, int>\r\n        > Maps = new();\r\n\r\n        internal static Dictionary<string, int> Get(Type type)\r\n        {\r\n            return Maps.GetOrAdd(type, static _ => new Dictionary<string, int>());\r\n        }\r\n    }\r\n}",
        0,
        "",
    ),
    (
        "CLI unrelated if preserves marked writes",
        "#if WALLSTOP_PROTO_ONLY\nnamespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static void Register(Type type, string name)\r\n        {\r\n            Names[type] = name; // concurrent-overwrite: an explicit registration must beat inference\r\n        }\r\n    }\r\n}\n#endif",
        0,
        "1 deliberate overwrite(s) exempted",
    ),
    (
        "CLI unrelated if still rejects unmarked writes",
        "#if WALLSTOP_PROTO_ONLY\nnamespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n            if (!Names.TryGetValue(type, out string cached))\r\n            {\r\n                cached = type.FullName;\r\n                Names[type] = cached;\r\n            }\r\n\r\n            return cached;\r\n        }\r\n    }\r\n}\n#endif",
        1,
        "'Names' is a ConcurrentDictionary filled through its indexer",
    ),
    (
        "CLI unrelated else preserves marked writes",
        "#if WALLSTOP_PROTO_ONLY\n#else\nnamespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static void Register(Type type, string name)\r\n        {\r\n            Names[type] = name; // concurrent-overwrite: an explicit registration must beat inference\r\n        }\r\n    }\r\n}\n#endif",
        0,
        "1 deliberate overwrite(s) exempted",
    ),
    (
        "CLI unrelated else still rejects unmarked writes",
        "#if WALLSTOP_PROTO_ONLY\n#else\nnamespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n            if (!Names.TryGetValue(type, out string cached))\r\n            {\r\n                cached = type.FullName;\r\n                Names[type] = cached;\r\n            }\r\n\r\n            return cached;\r\n        }\r\n    }\r\n}\n#endif",
        1,
        "'Names' is a ConcurrentDictionary filled through its indexer",
    ),
    (
        "CLI unrelated elif preserves marked writes",
        "#if UNITY_EDITOR\n#elif WALLSTOP_PROTO_ONLY\nnamespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static void Register(Type type, string name)\r\n        {\r\n            Names[type] = name; // concurrent-overwrite: an explicit registration must beat inference\r\n        }\r\n    }\r\n}\n#endif",
        0,
        "1 deliberate overwrite(s) exempted",
    ),
    (
        "CLI unrelated elif still rejects unmarked writes",
        "#if UNITY_EDITOR\n#elif WALLSTOP_PROTO_ONLY\nnamespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n            if (!Names.TryGetValue(type, out string cached))\r\n            {\r\n                cached = type.FullName;\r\n                Names[type] = cached;\r\n            }\r\n\r\n            return cached;\r\n        }\r\n    }\r\n}\n#endif",
        1,
        "'Names' is a ConcurrentDictionary filled through its indexer",
    ),
    (
        "CLI nested unrelated directives preserves marked writes",
        "#if WALLSTOP_PROTO_ONLY\n#if UNITY_EDITOR\n#elif UNITY_STANDALONE\n#else\nnamespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static void Register(Type type, string name)\r\n        {\r\n            Names[type] = name; // concurrent-overwrite: an explicit registration must beat inference\r\n        }\r\n    }\r\n}\n#endif\n#endif",
        0,
        "1 deliberate overwrite(s) exempted",
    ),
    (
        "CLI nested unrelated directives still rejects unmarked writes",
        "#if WALLSTOP_PROTO_ONLY\n#if UNITY_EDITOR\n#elif UNITY_STANDALONE\n#else\nnamespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n\r\n    internal static class Cache\r\n    {\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n            if (!Names.TryGetValue(type, out string cached))\r\n            {\r\n                cached = type.FullName;\r\n                Names[type] = cached;\r\n            }\r\n\r\n            return cached;\r\n        }\r\n    }\r\n}\n#endif\n#endif",
        1,
        "'Names' is a ConcurrentDictionary filled through its indexer",
    ),
    (
        "CLI unrelated outer directive preserves SINGLE_THREADED exemption",
        "#if WALLSTOP_PROTO_ONLY\nnamespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n    using System.Collections.Generic;\r\n\r\n    internal static class Cache\r\n    {\r\n#if SINGLE_THREADED\r\n        private static readonly Dictionary<Type, string> Names = new();\r\n#else\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n#endif\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n#if SINGLE_THREADED\r\n            if (!Names.TryGetValue(type, out string cached))\r\n            {\r\n                cached = type.FullName;\r\n                Names[type] = cached;\r\n            }\r\n\r\n            return cached;\r\n#else\r\n            return Names.GetOrAdd(type, static t => t.FullName);\r\n#endif\r\n        }\r\n    }\r\n}\n#endif",
        0,
        "skipped 1 write(s) inside SINGLE_THREADED branches",
    ),
    (
        "CLI unrelated outer directive preserves concurrent else rejection",
        "#if WALLSTOP_PROTO_ONLY\nnamespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n    using System.Collections.Generic;\r\n\r\n    internal static class Cache\r\n    {\r\n#if SINGLE_THREADED\r\n        private static readonly Dictionary<Type, string> Names = new();\r\n#else\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n#endif\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n#if SINGLE_THREADED\r\n            return Names.TryGetValue(type, out string cached) ? cached : null;\r\n#else\r\n            Names[type] = type.FullName;\r\n            return Names[type];\r\n#endif\r\n        }\r\n    }\r\n}\n#endif",
        1,
        "'Names' is a ConcurrentDictionary filled through its indexer",
    ),
    (
        "CLI unrelated inner if elif else retains SINGLE_THREADED exemption",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n    using System.Collections.Generic;\r\n\r\n    internal static class Cache\r\n    {\r\n#if SINGLE_THREADED\r\n        private static readonly Dictionary<Type, string> Names = new();\r\n#else\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n#endif\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n#if SINGLE_THREADED\r\n            if (!Names.TryGetValue(type, out string cached))\r\n            {\r\n                cached = type.FullName;\r\n                #if WALLSTOP_PROTO_ONLY\nNames[type] = cached;\n#elif UNITY_EDITOR\nNames[type] = cached;\n#else\nNames[type] = cached;\n#endif\r\n            }\r\n\r\n            return cached;\r\n#else\r\n            return Names.GetOrAdd(type, static t => t.FullName);\r\n#endif\r\n        }\r\n    }\r\n}",
        0,
        "skipped 3 write(s) inside SINGLE_THREADED branches",
    ),
    (
        "CLI unrelated inner if elif else cannot exempt concurrent writes",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n    using System.Collections.Generic;\r\n\r\n    internal static class Cache\r\n    {\r\n#if SINGLE_THREADED\r\n        private static readonly Dictionary<Type, string> Names = new();\r\n#else\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n#endif\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n#if SINGLE_THREADED\r\n            return Names.TryGetValue(type, out string cached) ? cached : null;\r\n#else\r\n            #if WALLSTOP_PROTO_ONLY\nNames[type] = type.FullName;\n#elif UNITY_EDITOR\nNames[type] = type.FullName;\n#else\nNames[type] = type.FullName;\n#endif\r\n            return Names[type];\r\n#endif\r\n        }\r\n    }\r\n}",
        1,
        "'Names' is a ConcurrentDictionary filled through its indexer",
    ),
    (
        "CLI SINGLE_THREADED elif after unrelated if still exempts writes",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n    using System.Collections.Generic;\r\n\r\n    internal static class Cache\r\n    {\r\n#if WALLSTOP_PROTO_ONLY\n#elif SINGLE_THREADED\r\n        private static readonly Dictionary<Type, string> Names = new();\r\n#else\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n#endif\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n#if WALLSTOP_PROTO_ONLY\n#elif SINGLE_THREADED\r\n            if (!Names.TryGetValue(type, out string cached))\r\n            {\r\n                cached = type.FullName;\r\n                Names[type] = cached;\r\n            }\r\n\r\n            return cached;\r\n#else\r\n            return Names.GetOrAdd(type, static t => t.FullName);\r\n#endif\r\n        }\r\n    }\r\n}",
        0,
        "skipped 1 write(s) inside SINGLE_THREADED branches",
    ),
    (
        "CLI else after SINGLE_THREADED elif still rejects concurrent writes",
        "namespace Fixture\r\n{\r\n    using System;\r\n    using System.Collections.Concurrent;\r\n    using System.Collections.Generic;\r\n\r\n    internal static class Cache\r\n    {\r\n#if WALLSTOP_PROTO_ONLY\n#elif SINGLE_THREADED\r\n        private static readonly Dictionary<Type, string> Names = new();\r\n#else\r\n        private static readonly ConcurrentDictionary<Type, string> Names = new();\r\n#endif\r\n\r\n        internal static string Get(Type type)\r\n        {\r\n#if WALLSTOP_PROTO_ONLY\n#elif SINGLE_THREADED\r\n            return Names.TryGetValue(type, out string cached) ? cached : null;\r\n#else\r\n            Names[type] = type.FullName;\r\n            return Names[type];\r\n#endif\r\n        }\r\n    }\r\n}",
        1,
        "'Names' is a ConcurrentDictionary filled through its indexer",
    ),
)


@pytest.mark.parametrize(("name", "source", "expected", "diagnostic"), CASES)
def test_original_outcomes(name: str, source: str, expected: int, diagnostic: str) -> None:
    result = lint.scan_source(source)
    assert bool(result.findings) == bool(expected), name
    if "exempted" in diagnostic:
        assert f"{result.exempted} deliberate overwrite(s) exempted" == diagnostic
    elif "skipped" in diagnostic:
        assert f"skipped {result.skipped} write(s) inside SINGLE_THREADED branches" == diagnostic
    elif diagnostic:
        assert any(diagnostic in finding.message for finding in result.findings)


def fixture_root(path: Path) -> Path:
    for name in lint.SOURCE_ROOTS:
        (path / name).mkdir()
    return path


@pytest.mark.parametrize("entrypoint", ["python", "powershell"])
@pytest.mark.parametrize(("source", "expected"), [(CASES[0][1], 0), (CASES[1][1], 1)])
def test_actual_cli(tmp_path: Path, source: str, expected: int, entrypoint: str) -> None:
    repository = fixture_root(tmp_path)
    (repository / "Runtime" / "Fixture.cs").write_text(source, encoding="utf-8")
    command = (
        [
            sys.executable,
            str(ROOT / "scripts/lint_concurrent_cache_fill.py"),
            "--repo-root",
            str(repository),
            "--verbose",
        ]
        if entrypoint == "python"
        else [
            "pwsh",
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-concurrent-cache-fill.ps1"),
            "-RepoRoot",
            str(repository),
            "-VerboseOutput",
        ]
    )
    result = subprocess.run(
        command,
        cwd=tmp_path.parent,
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=30,
        check=False,
    )
    assert result.returncode == expected, result.stdout + result.stderr
    assert "1 C# sources" in result.stdout


@pytest.mark.parametrize("missing", [None, "Runtime", "Editor", "Styles"])
def test_missing_or_empty_corpus_fails(tmp_path: Path, missing: str | None) -> None:
    repository = fixture_root(tmp_path)
    if missing is not None:
        (repository / missing).rmdir()
    assert lint.main(["--repo-root", str(repository)]) == 1


def test_annotations_escape_file_names(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    repository = fixture_root(tmp_path)
    (repository / "Runtime" / "Comma,Percent%.cs").write_text(CASES[1][1], encoding="utf-8")
    assert lint.main(["--repo-root", str(repository)]) == 1
    output = capsys.readouterr().out
    assert "Comma%2CPercent%25.cs" in output


@pytest.mark.parametrize(
    "guard",
    [
        "SINGLE_THREADED || UNITY_EDITOR",
        "!SINGLE_THREADED && UNITY_EDITOR",
        "SINGLE_THREADED_EXTRA",
        "UNITY_EDITOR || SINGLE_THREADED",
    ],
)
def test_compound_guards_do_not_hide_concurrent_writes(guard: str) -> None:
    source = "ConcurrentDictionary<int,int> Cache;\n#if " + guard + "\nCache[1] = 2;\n#endif"
    assert lint.scan_source(source).findings


@pytest.mark.parametrize(
    "source",
    [
        "// ConcurrentDictionary<int,int> Cache; Cache[1] = 2;",
        'string text = "cache.GetOrAdd(key, key => value)";',
        "/* cache.GetOrAdd(key, key => value); */",
        "var value = cache.GetOrAdd(key, static /* retained */ key => key);",
        "var value = cache.GetOrAdd(key, static async key => await Work(key));",
        "var value = cache.GetOrAdd(key, static @event => @event);",
    ],
)
def test_comments_literals_and_legal_static_lambdas_do_not_report(source: str) -> None:
    assert not lint.scan_source(source).findings


def test_directives_inside_comments_cannot_exempt_writes() -> None:
    source = "ConcurrentDictionary<int,int> Cache;\n/*\n#if SINGLE_THREADED\n*/\nCache[1] = 2;"
    assert lint.scan_source(source).findings


@pytest.mark.parametrize(("name", "access"), [("Cache", "this.Cache"), ("@Cache", "@Cache")])
def test_qualified_and_escaped_cache_writes_report(name: str, access: str) -> None:
    result = lint.scan_source(f"ConcurrentDictionary<int,int> {name};\n{access}[1] = 2;")
    assert len(result.findings) == 1
    assert result.findings[0].line == 2


@pytest.mark.parametrize("encoding", ["utf-8-sig", "utf-16", "utf-32"])
@pytest.mark.parametrize("valid", [False, True])
def test_byte_order_marks_match_original_reader(
    tmp_path: Path, encoding: str, valid: bool, capsys: pytest.CaptureFixture[str]
) -> None:
    repository = fixture_root(tmp_path)
    source = (
        "cache.GetOrAdd(key, static key => key);"
        if valid
        else "ConcurrentDictionary<int,int> Cache;\nCache[1] = 2;"
    )
    (repository / "Runtime/Fixture.cs").write_text(source, encoding=encoding)
    assert lint.main(["--repo-root", str(repository)]) == (0 if valid else 1)
    output = capsys.readouterr().out
    if not valid:
        assert "'Cache' is a ConcurrentDictionary filled through its indexer" in output


def test_legacy_suite_launcher_selects_the_original_cases(tmp_path: Path) -> None:
    pwsh = shutil.which("pwsh")
    assert pwsh is not None
    result = subprocess.run(
        [
            pwsh,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-lint-concurrent-cache-fill.ps1"),
            "-TestFilter",
            "test_original_outcomes",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=60,
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "29 passed" in result.stdout


@pytest.mark.parametrize(
    "literal",
    [
        '"concurrent-overwrite: fake"',
        '@"concurrent-overwrite: fake"',
        '"""concurrent-overwrite: fake"""',
    ],
)
def test_literal_markers_cannot_excuse_a_write(literal: str) -> None:
    result = lint.scan_source(
        "ConcurrentDictionary<int,string> Cache;\nCache[1] = " + literal + ";"
    )
    assert len(result.findings) == 1


@pytest.mark.parametrize("marker", ["concurrent-overwrite:", "CONCURRENT-OVERWRITE:"])
def test_actual_comment_markers_remain_accepted(marker: str) -> None:
    source = (
        "ConcurrentDictionary<int,int> Cache;\n/*\n"
        + marker
        + " intentional replacement\n*/\nCache[1] = 2;"
    )
    assert not lint.scan_source(source).findings


def test_returned_lambda_is_not_another_cache_factory_argument() -> None:
    result = lint.scan_source("cache.GetOrAdd(key, static key => value => key + value);")
    assert not result.findings
    assert result.lambdas == 1


def test_a_later_factory_still_requires_static_after_a_returned_lambda() -> None:
    result = lint.scan_source(
        "cache.AddOrUpdate(key, static key => value => key + value, (key, old) => old);"
    )
    assert len(result.findings) == 1
    assert result.lambdas == 2
