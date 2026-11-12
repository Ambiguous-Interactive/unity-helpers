"""Replay the original mirror generator and verify write and launcher behavior."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from readme_mirror import (
    BANNER,
    ROOT,
    build_mirror,
    fragment_map,
    github_slug,
    headings_of,
    main,
    mkdocs_slug,
    prettier_binary,
    publish_mirror,
)

CORPUS = cast(
    "dict[str, object]",
    json.loads(
        (Path(__file__).parent / "fixtures/readme-mirror-v1.json").read_text(encoding="utf-8")
    ),
)
CASES = cast("list[dict[str, object]]", CORPUS["cases"])
SLUGS = cast("list[dict[str, str]]", CORPUS["slugs"])


def populate(root: Path) -> dict[str, str]:
    documents = cast("dict[str, str]", CORPUS["documents"])
    for name, content in documents.items():
        file = root / name
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(content, encoding="utf-8")
    # The frozen HTML-image case assumes this asset exists. Keep its historical
    # output unchanged while supplying that precondition explicitly.
    image = root / "docs/images/banner.svg"
    image.parent.mkdir(parents=True, exist_ok=True)
    image.write_text("<svg/>", encoding="utf-8")
    return documents


@pytest.mark.parametrize("case", CASES)
def test_original_executable_corpus(tmp_path: Path, case: dict[str, object]) -> None:
    documents = populate(tmp_path)
    source = str(case["source"])
    documents = {**documents, "README.md": source}
    content, problems = build_mirror(source, documents.__getitem__, tmp_path)
    assert content == case["content"]
    assert problems == case["problems"]


@pytest.mark.parametrize("case", SLUGS, ids=[case["heading"] for case in SLUGS])
def test_original_slug_corpus(case: dict[str, str]) -> None:
    assert github_slug(case["heading"]) == case["github"]
    assert mkdocs_slug(case["heading"]) == case["mkdocs"]


@pytest.mark.parametrize("fence", ["```", "~~~", "````", "~~~~~"])
def test_fences_keep_code_and_require_full_closing_length(tmp_path: Path, fence: str) -> None:
    source = f"# Visible\n{fence}markdown\n# Hidden\n[x](./docs/hidden.md)\n{fence[:2]}\n## Still hidden\n{fence}\n# Visible again\n"
    assert headings_of(source) == ["Visible", "Visible again"]
    content, problems = build_mirror(source, lambda _: source, tmp_path)
    assert content == BANNER + "\n\n" + source
    assert problems == []


def test_fragment_map_retains_first_heading() -> None:
    assert fragment_map("# Alpha & Beta\n# Alpha  Beta\n") == {"alpha--beta": "alpha-beta"}


def test_repository_escape_is_refused(tmp_path: Path) -> None:
    source = "# T\n[x](../README.md)\n"
    _, problems = build_mirror(source, lambda _: source, tmp_path)
    assert len(problems) == 1
    assert "leaves the repository" in problems[0]


def test_generator_roundtrip_drift_and_missing_mirror(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    populate(tmp_path)
    source = "# Fixture\n\n## Top Time Savers\n\n[Self](#top-time-savers) [Guide](./docs/overview/index.md#alpha--beta) [Log](./CHANGELOG.md)\n"
    (tmp_path / "README.md").write_text(source, encoding="utf-8")
    arguments = ["--repo-root", str(tmp_path)]
    assert main([*arguments, "--check"]) == 1
    assert not (tmp_path / "docs/readme.md").exists()
    assert main(arguments) == 0
    mirror = tmp_path / "docs/readme.md"
    expected = mirror.read_text(encoding="utf-8")
    assert "[Guide](./overview/index.md#alpha-beta)" in expected
    assert "[Log](https://github.com/wallstop/unity-helpers/blob/main/CHANGELOG.md)" in expected
    assert main([*arguments, "--check", "--verbose"]) == 0
    assert "matches" in capsys.readouterr().out
    mirror.write_text(expected.replace("# Fixture", "# Edited by hand"), encoding="utf-8")
    assert main([*arguments, "--check"]) == 1
    assert "Edited by hand" in capsys.readouterr().err
    assert main(arguments) == 0
    assert mirror.read_text(encoding="utf-8") == expected
    assert main([*arguments, "--verbose"]) == 0
    assert "already current" in capsys.readouterr().out


@pytest.mark.parametrize("name", ["scripts/sync-readme-mirror.js", "scripts/lint-readme-mirror.js"])
def test_compatibility_launchers_from_foreign_directory(tmp_path: Path, name: str) -> None:
    node = shutil.which("node")
    assert node is not None
    environment = {**os.environ, "README_MIRROR_ROOT": str(ROOT)}
    result = subprocess.run(
        [node, str(ROOT / name), "--check", "--verbose"],
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=60,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert "matches" in result.stdout


def test_missing_generator_reports_failure(capsys: pytest.CaptureFixture[str]) -> None:
    assert main(["--sync-script", "scripts/no-such-generator.js"]) == 1
    assert "generator not found" in capsys.readouterr().err


def test_override_generator_preserves_status_and_arguments(tmp_path: Path) -> None:
    custom = tmp_path / "override generator.js"
    custom.write_text(
        "process.exit(process.argv.includes('--check') && process.argv.includes('--verbose') ? 7 : 9);\n",
        encoding="utf-8",
    )
    assert main(["--sync-script", str(custom), "--verbose"]) == 7


def test_missing_source_is_failure(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    assert main(["--repo-root", str(tmp_path)]) == 1
    assert "README.md" in capsys.readouterr().err


def test_invalid_source_does_not_overwrite_mirror(tmp_path: Path) -> None:
    populate(tmp_path)
    (tmp_path / "README.md").write_text("# T\n[x](#missing)\n", encoding="utf-8")
    mirror = tmp_path / "docs/readme.md"
    mirror.write_bytes(b"retained bytes\r\n")
    assert main(["--repo-root", str(tmp_path)]) == 1
    assert mirror.read_bytes() == b"retained bytes\r\n"


def test_formatter_failure_does_not_overwrite(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    populate(tmp_path)
    (tmp_path / "README.md").write_text("# T\n", encoding="utf-8")
    mirror = tmp_path / "docs/readme.md"
    mirror.write_bytes(b"retained\r\n")
    monkeypatch.setenv("PATH", "")
    assert main(["--repo-root", str(tmp_path)]) == 1
    assert mirror.read_bytes() == b"retained\r\n"


def test_output_symlink_is_refused(tmp_path: Path) -> None:
    populate(tmp_path)
    (tmp_path / "README.md").write_text("# T\n", encoding="utf-8")
    target = tmp_path / "target.md"
    target.write_bytes(b"retained")
    try:
        (tmp_path / "docs/readme.md").symlink_to(target)
    except OSError as error:
        pytest.skip(f"Symlink creation unavailable: {error}")
    assert main(["--repo-root", str(tmp_path)]) == 1
    assert target.read_bytes() == b"retained"


def test_overview_anchors_resolve_in_both_renderers() -> None:
    index = (ROOT / "docs/overview/index.md").read_text(encoding="utf-8")
    referenced = set(re.findall(r"\.\./readme\.md#([^)\s|]+)", index))
    headings = headings_of((ROOT / "docs/readme.md").read_text(encoding="utf-8"))
    assert referenced
    assert referenced <= {github_slug(heading) for heading in headings}
    assert referenced <= {mkdocs_slug(heading) for heading in headings}


def test_publication_failure_preserves_previous_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    mirror = tmp_path / "mirror.md"
    mirror.write_bytes(b"previous\r\n")

    def refuse(_source: object, _destination: object) -> None:
        raise OSError("publication refused")

    monkeypatch.setattr(os, "replace", refuse)
    with pytest.raises(OSError, match="publication refused"):
        publish_mirror(mirror, "new\n")
    assert mirror.read_bytes() == b"previous\r\n"
    assert sorted(path.name for path in tmp_path.iterdir()) == ["mirror.md"]


def test_empty_source_fails_without_overwrite(tmp_path: Path) -> None:
    populate(tmp_path)
    (tmp_path / "README.md").write_text(" \n", encoding="utf-8")
    mirror = tmp_path / "docs/readme.md"
    mirror.write_bytes(b"retained")
    assert main(["--repo-root", str(tmp_path)]) == 1
    assert mirror.read_bytes() == b"retained"


def test_retained_test_launcher_forwards_selection(tmp_path: Path) -> None:
    node = shutil.which("node")
    assert node is not None
    result = subprocess.run(
        [
            node,
            str(ROOT / "scripts/tests/test-sync-readme-mirror.js"),
            "-k",
            "test_missing_generator_reports_failure",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=60,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert "1 passed" in result.stdout
    assert "deselected" in result.stdout


@pytest.mark.parametrize(
    ("metadata", "valid"),
    [
        ('{"bin":"./cli.cjs"}', True),
        ('{"bin":{"prettier":"./cli.cjs"}}', True),
        ("[]", False),
        ('{"bin":null}', False),
        ('{"bin":{"other":"./cli.cjs"}}', False),
        ('{"bin":"../outside.cjs"}', False),
        ('{"bin":"./missing.cjs"}', False),
    ],
)
def test_formatter_metadata_is_checked(tmp_path: Path, metadata: str, valid: bool) -> None:
    package = tmp_path / "node_modules/prettier"
    package.mkdir(parents=True)
    (package / "package.json").write_text(metadata, encoding="utf-8")
    (package / "cli.cjs").write_text("", encoding="utf-8")
    (package.parent / "outside.cjs").write_text("", encoding="utf-8")
    if valid:
        assert prettier_binary(tmp_path) == package / "cli.cjs"
    else:
        with pytest.raises(ValueError, match="Prettier"):
            prettier_binary(tmp_path)


def test_compatibility_generator_receives_extra_literal_arguments(
    tmp_path: Path,
) -> None:
    custom = tmp_path / "generator.js"
    custom.write_text(
        "process.exit(process.argv.includes('--literal') && process.argv.includes('a b') ? 0 : 8);",
        encoding="utf-8",
    )
    assert main(["--sync-script", str(custom), "--literal", "a b"]) == 0


def test_hardlinked_mirror_does_not_rewrite_the_source(tmp_path: Path) -> None:
    (tmp_path / "docs").mkdir()
    source = tmp_path / "README.md"
    original = b"# Retained source\r\n"
    source.write_bytes(original)
    mirror = tmp_path / "docs/readme.md"
    try:
        os.link(source, mirror)
    except OSError as error:
        pytest.skip(f"Hard links unavailable: {error}")
    assert main(["--repo-root", str(tmp_path)]) == 0
    assert source.read_bytes() == original
    assert not source.samefile(mirror)
    assert mirror.read_text(encoding="utf-8").startswith(BANNER)


# cspell:ignore Bvalue caf
@pytest.mark.parametrize(
    ("filename", "encoded"),
    [
        ("guide one.md", "guide%20one.md"),
        ("price+value.md", "price%2Bvalue.md"),
        ("hash#name.md", "hash%23name.md"),
        ("round(name).md", "round%28name%29.md"),
        ("café.md", "caf%C3%A9.md"),
    ],
)
def test_docs_targets_preserve_encoded_filename_semantics(
    tmp_path: Path, filename: str, encoded: str
) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / filename).write_text("# Alpha & Beta\n", encoding="utf-8")
    source = f"# T\n[x](./docs/{encoded}#alpha--beta)\n"
    content, problems = build_mirror(
        source, lambda file: (tmp_path / file).read_text(encoding="utf-8"), tmp_path
    )
    assert problems == []
    assert f"[x](./{encoded}#alpha-beta)" in content


def test_encoded_sample_path_and_external_fragment_are_retained(tmp_path: Path) -> None:
    target = tmp_path / "Samples~/DI - VContainer/README.md"
    target.parent.mkdir(parents=True)
    target.write_text("# A & B\n", encoding="utf-8")
    source = "# T\n[x](./Samples~/DI%20-%20VContainer/README.md#a--b)\n"
    content, problems = build_mirror(source, lambda _: "", tmp_path)
    assert problems == []
    assert (
        "https://github.com/wallstop/unity-helpers/blob/main/Samples~/DI%20-%20VContainer/README.md#a--b"
        in content
    )


def test_missing_generator_argument_returns_legacy_failure(
    capsys: pytest.CaptureFixture[str],
) -> None:
    assert main(["--sync-script"]) == 1
    assert "expected one argument" in capsys.readouterr().err


def test_terminated_generator_returns_legacy_failure(tmp_path: Path) -> None:
    custom = tmp_path / "terminated.js"
    custom.write_text("process.kill(process.pid, 'SIGTERM');", encoding="utf-8")
    assert main(["--sync-script", str(custom)]) == 1


@pytest.mark.parametrize(
    "target",
    ["./docs/missing.md", "docs/missing.md#section", "./docs/images/missing.svg"],
)
def test_missing_docs_targets_fail_without_publishing(tmp_path: Path, target: str) -> None:
    populate(tmp_path)
    source = tmp_path / "README.md"
    source.write_text(f"# Title\n\n[Missing]({target})\n", encoding="utf-8")
    mirror = tmp_path / "docs/readme.md"
    mirror.write_text("previous mirror", encoding="utf-8")
    for arguments in [[], ["--check"]]:
        assert main(["--repo-root", str(tmp_path), *arguments]) == 1
        assert mirror.read_text(encoding="utf-8") == "previous mirror"


@pytest.mark.parametrize(
    "example",
    [
        "`[example](./missing.md)`",
        '``[example](./missing.md) ` <img src="./docs/missing.svg">``',
        "`[example](./missing.md)\nsecond line`",
        "``[example](./missing.md)\n`shorter delimiter`\nend``",
        "`first` and `[example](./missing.md)`",
        "\\\\`[example](./missing.md)`",
        "`[example](./missing.md)\\`",
    ],
)
def test_inline_code_preserves_examples_and_real_links(tmp_path: Path, example: str) -> None:
    documents = populate(tmp_path)
    source = f"# Title\n\n{example} [Log](./CHANGELOG.md)\n"
    content, problems = build_mirror(source, documents.__getitem__, tmp_path)
    assert problems == []
    assert example in content
    assert "[Log](https://github.com/wallstop/unity-helpers/blob/main/CHANGELOG.md)" in content


@pytest.mark.parametrize("prefix", ["`unclosed ", "\\`escaped "])
def test_unmatched_or_escaped_backticks_keep_real_links_active(tmp_path: Path, prefix: str) -> None:
    documents = populate(tmp_path)
    source = f"# Title\n\n{prefix}[Log](./CHANGELOG.md)\n"
    content, problems = build_mirror(source, documents.__getitem__, tmp_path)
    assert problems == []
    assert prefix in content
    assert "[Log](https://github.com/wallstop/unity-helpers/blob/main/CHANGELOG.md)" in content


def test_inline_code_cannot_suppress_links_in_another_paragraph(tmp_path: Path) -> None:
    documents = populate(tmp_path)
    source = "# Title\n\n`unclosed\n\n[Missing](./docs/missing.md)\nclosing`\n"
    _, problems = build_mirror(source, documents.__getitem__, tmp_path)
    assert len(problems) == 1
    assert "does not exist" in problems[0]
