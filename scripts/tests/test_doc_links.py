"""Replay the original documentation checker and exercise its command and path boundaries."""

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
from cli_encoding import escape_command_data
from lint_doc_links import Repository, main, markdown_findings, mask_comments, scan

ROOT = Path(__file__).resolve().parents[2]
CORPUS = cast(
    "list[dict[str, dict[str, object]]]",
    json.loads((Path(__file__).parent / "fixtures/doc-links-v1.json").read_text(encoding="utf-8")),
)


def populate(root: Path, files: dict[str, str]) -> Repository:
    for name, text in files.items():
        file = root / name
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(text, encoding="utf-8")
    return Repository(root, sorted(files))


@pytest.mark.parametrize("capture", CORPUS, ids=[str(item["Case"]["Name"]) for item in CORPUS])
def test_original_executable_corpus(tmp_path: Path, capture: dict[str, dict[str, object]]) -> None:
    case = capture["Case"]
    files = cast("list[dict[str, object]]", case["Files"])
    repository = populate(
        tmp_path,
        {
            str(item["Path"]): str(item["Content"]) + ("" if item.get("NoNewline") else "\n")
            for item in files
        },
    )
    paths = cast("list[str]", case.get("Paths", repository.files))
    findings = scan(repository, paths, str(case.get("Mode", "All"))).findings
    expected = capture["Result"]
    assert int(bool(findings)) == expected["ExitCode"]
    observed = sorted((finding.line, finding.message) for finding in findings)
    original = sorted(
        (int(line), message)
        for line, message in re.findall(r"(?m)^.*?:(\d+): (.*)$", str(expected["Output"]))
    )
    assert observed == original
    messages = "\n".join(finding.message for finding in findings)
    for text in cast("list[str]", case.get("ExpectedOutputContains", [])):
        assert text in messages or text == "Markdown link lint passed"
    for text in cast("list[str]", case.get("ExpectedOutputNotContains", [])):
        assert text not in messages


@pytest.mark.parametrize("suffix", [".cs", ".ps1", ".py", ".sh", ".js", ".yaml"])
def test_hash_and_comment_markers_inside_strings_stay_live(suffix: str) -> None:
    source = 'value = "docs/real.md # // /* <#";\n'
    assert "docs/real.md" in mask_comments(source, suffix)


@pytest.mark.parametrize(
    ("suffix", "comment"),
    [
        (".cs", "/* docs/fake.md */"),
        (".ps1", "<# outer <# docs/fake.md #> outer #>"),
        (".py", '"""docs/fake.md"""'),
        (".js", "/* docs/fake.md */"),
        (".sh", "# docs/fake.md"),
        (".yaml", "# docs/fake.md"),
        (".csproj", "<!-- docs/fake.md -->"),
        (".cmd", "@REM docs/fake.md"),
    ],
)
def test_comments_preserve_lines_and_offsets(suffix: str, comment: str) -> None:
    text = comment + '\r\nvalue = "docs/real.md"\n'
    masked = mask_comments(text, suffix)
    assert len(masked) == len(text)
    assert "docs/fake.md" not in masked
    assert "docs/real.md" in masked
    assert [index for index, char in enumerate(masked) if char in "\r\n"] == [
        index for index, char in enumerate(text) if char in "\r\n"
    ]


def test_powershell_here_strings_are_live_even_with_comment_markers() -> None:
    source = '$x = @"\n# docs/real.md\n<# docs/also-real.md #>\n"@\n# docs/fake.md\n'
    masked = mask_comments(source, ".ps1")
    assert "docs/real.md" in masked
    assert "docs/also-real.md" in masked
    assert "docs/fake.md" not in masked


def test_repeated_here_strings_keep_absolute_scanner_offsets() -> None:
    source = '$value = @"\n# docs/live.md\n"@\n' * 32 + "# docs/comment.md\n"
    masked = mask_comments(source, ".ps1")
    assert masked.count("docs/live.md") == 32
    assert "docs/comment.md" not in masked


def test_repeated_javascript_identifiers_and_regex_literals_keep_following_paths_live() -> None:
    source = "const value = /[/*]/;\n" * 128 + 'const path = "docs/live.md"; // docs/comment.md\n'
    masked = mask_comments(source, ".js")
    assert "docs/live.md" in masked
    assert "docs/comment.md" not in masked


def test_images_are_validated_while_inline_image_examples_are_ignored(
    tmp_path: Path,
) -> None:
    repository = populate(tmp_path, {"README.md": "", "images/actual.png": ""})
    assert not markdown_findings(
        repository, "README.md", "![image](./images/actual.png)", "Targets"
    )
    missing = markdown_findings(
        repository, "README.md", "![image](./images/missing.png)", "Targets"
    )
    assert [finding.message for finding in missing] == [
        "Image target './images/missing.png' does not resolve to a file"
    ]
    assert not markdown_findings(
        repository, "README.md", "`![image](./images/missing.png)`", "Targets"
    )


def test_case_matching_uses_ordinal_inventory_on_every_platform(tmp_path: Path) -> None:
    repository = populate(tmp_path, {"docs/Actual.md": "text"})
    assert repository.resolve("README.md", "./docs/Actual.md")
    assert repository.resolve("README.md", "./docs%5cActual.md")
    assert not repository.resolve("README.md", "./Docs/Actual.md")
    assert not repository.resolve("README.md", "./docs/actual.md")
    assert repository.resolve("README.md", "./docs/../docs/Actual.md")


def test_deleted_index_entry_is_not_an_existing_target(tmp_path: Path) -> None:
    repository = populate(tmp_path, {"README.md": "[missing](./gone.md)"})
    repository.entries.add("gone.md")
    assert not repository.resolve("README.md", "./gone.md")


def test_angle_target_preserves_spaces_and_encoded_names(tmp_path: Path) -> None:
    repository = populate(tmp_path, {"docs/a b.md": "", "README.md": ""})
    for link in ("[guide](<./docs/a b.md>)", "[guide](./docs/a%20b.md)"):
        assert not markdown_findings(repository, "README.md", link, "Targets")


def test_escaping_repository_is_refused_even_when_file_exists(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    root.mkdir()
    (tmp_path / "outside.md").write_text("", encoding="utf-8")
    repository = populate(root, {"README.md": ""})
    for target in ("../outside.md", "..%2foutside.md", "./%00bad.md"):
        assert not repository.resolve("README.md", target)


def test_symlink_cannot_expose_a_foreign_document(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    root.mkdir()
    outside = tmp_path / "outside.md"
    outside.write_text("", encoding="utf-8")
    link = root / "link.md"
    try:
        link.symlink_to(outside)
    except OSError as error:
        pytest.skip(f"Symlink creation unavailable: {error}")
    repository = Repository(root, ["link.md"])
    assert not repository.resolve("README.md", "./link.md")
    with pytest.raises(ValueError, match="within the repository"):
        scan(repository, ["link.md"])


def test_git_inventory_and_cli_anchor_paths_at_the_selected_repository(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    git = shutil.which("git")
    assert git
    subprocess.run([git, "init", "-q", str(tmp_path)], check=True)
    populate(
        tmp_path,
        {"README.md": "[guide](./docs/guide.md)\n", "docs/guide.md": "# Guide\n"},
    )
    monkeypatch.chdir(tmp_path / "docs")
    assert main(["--repo-root", str(tmp_path), "--paths", "README.md", "docs/guide.md"]) == 0
    assert "Markdown link lint passed" in capsys.readouterr().out
    assert main(["--repo-root", str(tmp_path), "--paths", "absent.md"]) == 1
    assert "does not exist" in capsys.readouterr().err


def test_non_repository_and_empty_inventory_fail_closed(tmp_path: Path) -> None:
    assert main(["--repo-root", str(tmp_path)]) == 1
    git = shutil.which("git")
    assert git
    subprocess.run([git, "init", "-q", str(tmp_path)], check=True)
    assert main(["--repo-root", str(tmp_path)]) == 1


@pytest.mark.parametrize(
    ("name", "mode"),
    [("unrelated.txt", "All"), ("program.cs", "Format"), ("Tests/test.cs", "All")],
)
def test_inventory_without_supported_sources_fails_closed(
    tmp_path: Path, name: str, mode: str, capsys: pytest.CaptureFixture[str]
) -> None:
    git = shutil.which("git")
    assert git
    subprocess.run([git, "init", "-q", str(tmp_path)], check=True)
    populate(tmp_path, {name: "content"})
    assert main(["--repo-root", str(tmp_path), "--mode", mode]) == 1
    assert "No supported documentation source" in capsys.readouterr().err


@pytest.mark.parametrize("opener", ["EOF", "'EOF'", '"EOF"', "\\EOF", "-EOF"])
def test_shell_heredoc_bodies_remain_live(opener: str) -> None:
    source = f"cat <<{opener} # docs/comment.md\n# docs/body.md\nEOF\n# docs/after.md\n"
    masked = mask_comments(source, ".sh")
    assert "docs/body.md" in masked
    assert "docs/comment.md" not in masked
    assert "docs/after.md" not in masked


@pytest.mark.parametrize("suffix", [".sh", ".yaml"])
def test_hash_inside_an_unquoted_word_is_not_a_comment(suffix: str) -> None:
    assert "docs/live.md" in mask_comments("word#docs/live.md\n", suffix)


@pytest.mark.parametrize("prefix", ["value = ", "return ", "typeof ", "x++ / y; value = "])
def test_javascript_regex_character_classes_cannot_start_comments(prefix: str) -> None:
    source = prefix + '/[/*]/; let path = "docs/live.md"; // docs/comment.md\n'
    masked = mask_comments(source, ".js")
    assert "docs/live.md" in masked
    assert "docs/comment.md" not in masked


@pytest.mark.parametrize(
    ("mode", "selected", "expected"),
    [
        ("Format", "missing.md", 0),
        ("Targets", "missing.md", 1),
        ("Targets", "style.md", 0),
        ("All", "style.md", 1),
    ],
)
@pytest.mark.parametrize("launcher", ["powershell", "node"])
def test_compatibility_launcher_anchors_at_its_repository(
    tmp_path: Path, mode: str, selected: str, expected: int, launcher: str
) -> None:
    executable = shutil.which("pwsh" if launcher == "powershell" else "node")
    if not executable:
        pytest.skip(f"{launcher} launcher unavailable")
    git = shutil.which("git")
    assert git
    fixture = tmp_path / "repository"
    populate(
        fixture,
        {
            "clean.md": "# Guide\n",
            "missing.md": "[guide](./absent.md)\n",
            "style.md": "[guide](docs/guide.md)\n",
            "docs/guide.md": "# Guide\n",
        },
    )
    for relative in (
        "scripts/lint-doc-links.ps1",
        "scripts/run-doc-link-lint.js",
        "scripts/lint_doc_links.py",
        "scripts/csharp_sources.py",
        "scripts/cli_encoding.py",
        "scripts/python~/pyproject.toml",
        "scripts/python~/uv.lock",
    ):
        destination = fixture / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, destination)
    subprocess.run([git, "init", "-q", str(fixture)], check=True)
    environment = dict(os.environ, UV_PROJECT_ENVIRONMENT=str(fixture / "environment~"))
    command = (
        [executable, "-NoProfile", "-File", str(fixture / "scripts/lint-doc-links.ps1")]
        if launcher == "powershell"
        else [executable, str(fixture / "scripts/run-doc-link-lint.js"), "-v"]
    ) + [
        "-Paths",
        "clean.md",
        selected,
        "-Mode",
        mode,
    ]
    result = subprocess.run(
        command,
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == expected, result.stdout + result.stderr
    assert (
        "Markdown link lint passed" if expected == 0 else "Markdown link lint failed"
    ) in result.stdout


@pytest.mark.parametrize("name", ["docs/space name.md", "docs/line\nbreak.md"])
def test_cli_preserves_newlines_and_spaces_in_git_names(tmp_path: Path, name: str) -> None:
    git = shutil.which("git")
    assert git
    subprocess.run([git, "init", "-q", str(tmp_path)], check=True)
    try:
        populate(tmp_path, {name: "[guide](./missing.md)\n"})
    except OSError as error:
        if "\n" in name:
            pytest.skip(f"Newline filename unavailable: {error}")
        raise
    result = subprocess.run(
        [
            sys.executable,
            str(ROOT / "scripts/lint_doc_links.py"),
            "--repo-root",
            str(tmp_path),
            "--paths",
            name,
        ],
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == 1
    assert escape_command_data(name) in result.stdout
    assert "missing.md" in result.stdout


@pytest.mark.parametrize("extension", ["mdx", "md_backup", "md5"])
def test_review_source_extensions_are_not_truncated(tmp_path: Path, extension: str) -> None:
    repository = populate(tmp_path, {"program.py": f'path = "docs/guide.{extension}"\n'})
    assert not scan(repository, ["program.py"]).findings


def test_review_markdown_cannot_borrow_a_root_relative_target(tmp_path: Path) -> None:
    repository = populate(tmp_path, {"docs/page.md": "", "LICENSE": "", "program.py": ""})
    assert repository.resolve("docs/page.md", "LICENSE")
    findings = markdown_findings(repository, "docs/page.md", "[license](LICENSE)", "Targets")
    assert len(findings) == 1
    assert repository.resolve("program.py", "LICENSE")


@pytest.mark.parametrize("mode", ["All", "Format", "Targets"])
def test_review_reference_definitions_keep_validation_without_bare_prose_errors(
    tmp_path: Path, mode: str
) -> None:
    repository = populate(tmp_path, {"README.md": "", "docs/guide.md": ""})
    source = "[Guide][guide]\n[guide]: ./docs/guide.md\n"
    assert not markdown_findings(repository, "README.md", source, mode)
    if mode != "Format":
        missing = markdown_findings(
            repository, "README.md", source.replace("guide.md", "gone.md"), mode
        )
        assert len(missing) == 1
    if mode != "Targets":
        prose = markdown_findings(repository, "README.md", source + "See docs/guide.md\n", mode)
        assert len(prose) == 1
        assert prose[0].line == 3


@pytest.mark.parametrize(
    ("opening", "inside", "closing"),
    [
        ("````", "```", "````"),
        ("~~~", "```", "~~~~"),
        ("````", "~~~", "`````"),
        ("~~~", "~~~not-closing", "~~~"),
    ],
)
def test_review_fences_preserve_marker_and_minimum_length(
    tmp_path: Path, opening: str, inside: str, closing: str
) -> None:
    repository = populate(tmp_path, {"README.md": ""})
    source = f"{opening}\n{inside}\n[example](./missing.md)\n{closing}\n[real](./also-missing.md)\n"
    findings = markdown_findings(repository, "README.md", source, "Targets")
    assert len(findings) == 1
    assert findings[0].line == 5


@pytest.mark.parametrize(
    "source", ['path = """docs/missing.md"""\n', 'def value():\n    return """docs/missing.md"""\n']
)
def test_review_executable_triple_strings_remain_visible(tmp_path: Path, source: str) -> None:
    repository = populate(tmp_path, {"program.py": source})
    assert len(scan(repository, ["program.py"]).findings) == 1


@pytest.mark.parametrize(
    "source",
    [
        '"""docs/documentation.md"""\npath = """docs/live.md"""\n',
        'class É:\n    "docs/documentation.md"\n    path = """docs/live.md"""\n',
        'async def value():\n    """docs/documentation.md"""\n    return """docs/live.md"""\n',
    ],
)
def test_review_docstrings_are_masked_by_syntactic_position(source: str) -> None:
    masked = mask_comments(source, ".py")
    assert "docs/documentation.md" not in masked
    assert "docs/live.md" in masked


@pytest.mark.parametrize("mode", ["All", "Format"])
@pytest.mark.parametrize("target", ["docs/guide.md", "/unity-helpers/docs/guide.md"])
def test_review_reference_destinations_require_relative_format(
    tmp_path: Path, mode: str, target: str
) -> None:
    repository = populate(tmp_path, {"README.md": "", "docs/guide.md": ""})
    findings = markdown_findings(repository, "README.md", f"[guide]: {target}\n", mode)
    assert any("relative" in finding.message for finding in findings)
