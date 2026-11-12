"""Validate local documentation targets and the repository's Markdown link style."""

from __future__ import annotations

import argparse
import ast
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path
from urllib.parse import unquote

from cli_encoding import configure_output, escape_command_data
from csharp_sources import source_tokens

SCHEME = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.\-]*:")
MARKDOWN_PATH = re.compile(r"[A-Za-z0-9._/\-]+\.md(?:#[A-Za-z0-9_\-]+)?(?![A-Za-z0-9_-])")
CODE_PATH = re.compile(
    r"docs[\\/][A-Za-z0-9._/\\-]+\.md(?:#[A-Za-z0-9_\-]+)?(?![A-Za-z0-9_-])", re.I
)
CODE_SPAN = re.compile(r"(?:``(.+?)``|`([^`\n]+)`)")
INLINE_PATH = re.compile(
    r"(^|[^A-Za-z0-9_.-])(?P<path>[A-Za-z0-9._/\\-]+\.md(?:#[A-Za-z0-9_\-]+)?)(?![A-Za-z0-9_-])",
    re.I,
)
LINK = re.compile(r"(?P<image>!?)\[(?P<label>[^\]]*)\]\((?P<target>[^)]+)\)")
REFERENCE = re.compile(r"!\[[^\]]*\]\[(?P<label>[^\]]+)\]")
DEFINITION = re.compile(r"^\s*\[(?P<label>[^\]]+)\]:\s*(?P<rest>.+)$")
FENCE = re.compile(r"^ {0,3}(?P<marker>`{3,}|~{3,})(?P<info>.*)$")
CODE_EXTENSIONS = frozenset(
    [
        ".cs",
        ".csproj",
        ".props",
        ".targets",
        ".ps1",
        ".psm1",
        ".psd1",
        ".py",
        ".ts",
        ".tsx",
        ".js",
        ".jsx",
        ".json",
        ".yml",
        ".yaml",
        ".sh",
        ".cmd",
    ]
)
REGEX_KEYWORDS = frozenset(
    [
        "return",
        "typeof",
        "in",
        "instanceof",
        "void",
        "delete",
        "new",
        "throw",
        "await",
        "yield",
        "of",
        "do",
        "else",
        "case",
    ]
)
HEREDOC = re.compile(
    r"""<<\s*(-?)\s*(?:'([A-Za-z_]\w*)'|"([A-Za-z_]\w*)"|\\([A-Za-z_]\w*)|([A-Za-z_]\w*))\s*(?:#[^\r\n]*)?\s*$"""
)
JS_IDENTIFIER = re.compile(r"[A-Za-z_$][A-Za-z0-9_$]*")
POWERSHELL_CLOSERS = {
    '"': re.compile(r'(?m)^"@(?=\r?$)'),
    "'": re.compile(r"(?m)^'@(?=\r?$)"),
}


@dataclass(frozen=True)
class Finding:
    path: str
    line: int
    message: str
    source: str = ""


@dataclass(frozen=True)
class ScanResult:
    findings: list[Finding]
    sources_checked: int


def target_text(raw: str) -> str:
    trimmed = raw.strip()
    if trimmed.startswith("<") and ">" in trimmed:
        return trimmed[1 : trimmed.index(">")]
    return re.split(r"[ \t]", trimmed, maxsplit=1)[0]


def is_local(target: str) -> bool:
    return bool(target.strip()) and not SCHEME.match(target) and not target.startswith(("#", "//"))


@dataclass
class Repository:
    root: Path
    files: list[str]
    entries: set[str] = field(init=False)
    targets: dict[tuple[str, str, bool], bool] = field(
        default_factory=dict[tuple[str, str, bool], bool]
    )

    def __post_init__(self) -> None:
        self.root = self.root.resolve()
        self.entries = {"."}
        for name in self.files:
            self.entries.add(name)
            self.entries.update(parent.as_posix() for parent in Path(name).parents)

    def exists(self, candidate: Path) -> bool:
        normalized = Path(os.path.abspath(candidate))  # noqa: PTH100 - preserve lexical case and symlink identity before containment checks.
        try:
            relative = normalized.relative_to(self.root).as_posix()
            resolved = normalized.resolve()
            resolved.relative_to(self.root)
        except (OSError, ValueError, RuntimeError):
            return False
        return relative in self.entries and normalized.exists()

    def resolve(self, source: str, target: str, *, root_fallback: bool = True) -> bool:
        key = (source, target, root_fallback)
        if key in self.targets:
            return self.targets[key]
        normalized = target.strip().replace("\\", "/")
        normalized = re.split(r"[?#]", normalized, maxsplit=1)[0]
        if normalized.startswith("/unity-helpers/"):
            normalized = normalized[len("/unity-helpers") :]
        decoded = unquote(normalized).replace("\\", "/")
        found = False
        if decoded.strip() and "\0" not in decoded:
            found = self.exists(self.root / Path(source).parent / decoded)
            if (
                not found
                and (root_fallback or decoded.startswith("/"))
                and not decoded.startswith(".")
            ):
                found = self.exists(self.root / decoded.lstrip("/"))
        self.targets[key] = found
        return found


def inventory(root: Path) -> list[str]:
    git = shutil.which("git")
    if git is None:
        raise ValueError("Git is required to inventory documentation sources.")
    result = subprocess.run(  # noqa: S603 - read-only NUL-delimited repository inventory.
        [
            git,
            "-C",
            str(root),
            "ls-files",
            "--cached",
            "--others",
            "--exclude-standard",
            "-z",
        ],
        capture_output=True,
        check=True,
    )
    names = sorted({os.fsdecode(name) for name in result.stdout.split(b"\0") if name})
    if not names:
        raise ValueError("The documentation source inventory is empty.")
    return names


def python_docstring_ranges(text: str) -> list[tuple[int, int]]:
    try:
        tree = ast.parse(text)
    except (SyntaxError, ValueError, RecursionError):
        return []
    lines = text.split("\n")
    starts = [0]
    for line in lines[:-1]:
        starts.append(starts[-1] + len(line) + 1)

    def offset(line: int, byte_column: int) -> int:
        prefix = lines[line - 1].encode("utf-8")[:byte_column].decode("utf-8")
        return starts[line - 1] + len(prefix)

    ranges: list[tuple[int, int]] = []
    for node in ast.walk(tree):
        if (
            not isinstance(node, (ast.Module, ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef))
            or not node.body
        ):
            continue
        first = node.body[0]
        if (
            not isinstance(first, ast.Expr)
            or not isinstance(first.value, ast.Constant)
            or not isinstance(first.value.value, str)
        ):
            continue
        literal = first.value
        if literal.end_lineno is None or literal.end_col_offset is None:
            continue
        ranges.append(
            (
                offset(literal.lineno, literal.col_offset),
                offset(literal.end_lineno, literal.end_col_offset),
            )
        )
    return ranges


def mask_comments(text: str, suffix: str) -> str:
    """Preserve executable string references while masking language comments and Python docstrings."""
    masked = list(text)

    def mask(start: int, end: int) -> None:
        masked[start:end] = [
            character if character in "\r\n" else " " for character in text[start:end]
        ]

    if suffix == ".cs":
        for start, end, kind in source_tokens(text):
            if kind != "literal" and kind != "directive":
                mask(start, end)
        return "".join(masked)
    if suffix in {".csproj", ".props", ".targets"}:
        for match in re.finditer(r"<!--[\s\S]*?(?:-->|$)", text):
            mask(*match.span())
        return "".join(masked)
    if suffix == ".cmd":
        for match in re.finditer(r"(?im)^[ \t]*@?(?:rem(?:[ \t]|$)|::)[^\r\n]*", text):
            mask(*match.span())
        return "".join(masked)
    if suffix == ".json":
        return text
    powershell = suffix in {".ps1", ".psm1", ".psd1"}
    javascript = suffix in {".js", ".jsx", ".ts", ".tsx"}
    python = suffix == ".py"
    if python:
        for start, end in python_docstring_ranges(text):
            mask(start, end)
    shell = suffix == ".sh"
    boundary_hash = shell or suffix in {".yaml", ".yml"}
    regex_allowed = True
    heredoc_tag = ""
    heredoc_tabs = False
    index = 0
    while index < len(text):
        start = index
        line_start = index == 0 or text[index - 1] == "\n"
        if shell and line_start and heredoc_tag:
            end = text.find("\n", index)
            end = len(text) if end < 0 else end
            line = text[index:end].rstrip("\r")
            if (line.lstrip("\t") if heredoc_tabs else line) == heredoc_tag:
                heredoc_tag = ""
            index = min(end + 1, len(text))
        elif shell and text.startswith("<<", index):
            end = text.find("\n", index)
            end = len(text) if end < 0 else end
            opener = HEREDOC.match(text, index, end)
            if opener:
                heredoc_tabs = bool(opener[1])
                heredoc_tag = next(group for group in opener.groups()[1:] if group)
            index += 2
        elif powershell and text.startswith("<#", index):
            depth = 1
            index += 2
            while index < len(text) and depth:
                if text.startswith("<#", index):
                    depth += 1
                    index += 2
                elif text.startswith("#>", index):
                    depth -= 1
                    index += 2
                else:
                    index += 1
            mask(start, index)
        elif javascript and text.startswith("/*", index):
            end = text.find("*/", index + 2)
            index = len(text) if end < 0 else end + 2
            mask(start, index)
        elif (javascript and text.startswith("//", index)) or (
            not javascript
            and text[index] == "#"
            and (not boundary_hash or line_start or text[index - 1] in " \t\r")
        ):
            end = text.find("\n", index)
            index = len(text) if end < 0 else end
            mask(start, index)
        elif javascript and text[index] == "/":
            index += 1
            if regex_allowed:
                in_class = False
                while index < len(text) and text[index] not in "\r\n":
                    if text[index] == "\\":
                        index += 2
                        continue
                    if text[index] == "[":
                        in_class = True
                    elif text[index] == "]":
                        in_class = False
                    elif text[index] == "/" and not in_class:
                        index += 1
                        break
                    index += 1
                regex_allowed = False
            else:
                regex_allowed = True
        elif powershell and text[index : index + 2] in {'@"', "@'"}:
            quote = text[index + 1]
            closer = POWERSHELL_CLOSERS[quote].search(text, index + 2)
            index = len(text) if closer is None else closer.end()
        elif python and text[index : index + 3] in {'"""', "'''"}:
            quote = text[index : index + 3]
            index += 3
            while index < len(text):
                if text.startswith(quote, index):
                    index += 3
                    break
                index += 2 if text[index] == "\\" else 1
        elif text[index] in ("'", '"', "`"):
            quote = text[index]
            index += 1
            while index < len(text):
                if text[index] == quote:
                    index += 1
                    if (
                        (powershell or suffix in {".yml", ".yaml"})
                        and quote == "'"
                        and text[index : index + 1] == "'"
                    ):
                        index += 1
                        continue
                    break
                escape = "`" if powershell else "\\"
                index += 2 if text[index] == escape and not (shell and quote == "'") else 1
            regex_allowed = False
        elif javascript and (word := JS_IDENTIFIER.match(text, index)):
            index = word.end()
            regex_allowed = word[0] in REGEX_KEYWORDS
        elif javascript and text[index : index + 2] in {"++", "--"}:
            index += 2
            regex_allowed = False
        else:
            if javascript and not text[index].isspace():
                regex_allowed = text[index] not in ")]}.0123456789"
            index += 1
    return "".join(masked)


def visible_lines(text: str) -> list[tuple[int, str]]:
    fence_character = ""
    fence_length = 0
    result: list[tuple[int, str]] = []
    for number, line in enumerate(text.splitlines(), 1):
        fence = FENCE.match(line)
        if fence_character:
            if (
                fence
                and fence["marker"][0] == fence_character
                and len(fence["marker"]) >= fence_length
                and not fence["info"].strip()
            ):
                fence_character = ""
                fence_length = 0
            continue
        if fence and (fence["marker"][0] == "~" or "`" not in fence["info"]):
            fence_character = fence["marker"][0]
            fence_length = len(fence["marker"])
            continue
        result.append((number, line))
    return result


def markdown_findings(repository: Repository, path: str, text: str, mode: str) -> list[Finding]:
    lines = visible_lines(text)
    findings: list[Finding] = []
    definitions: dict[str, tuple[str, int]] = {}

    def report(number: int, message: str, line: str = "") -> None:
        findings.append(Finding(path, number, message, line))

    def check_target(target: str, number: int, kind: str, line: str = "") -> None:
        if is_local(target) and not repository.resolve(path, target, root_fallback=False):
            if kind == "Image":
                report(number, f"Image target '{target}' does not resolve to a file", line)
                return
            target_kind = (
                "markdown file"
                if re.search(r"\.md($|[?#])", target, re.I)
                else "local file or directory"
            )
            report(
                number,
                f"{kind} target '{target}' does not resolve to an existing {target_kind}",
                line,
            )

    def check_target_format(target: str, number: int, line: str) -> None:
        if target == "/unity-helpers" or target.startswith("/unity-helpers/"):
            suggestion = re.sub(r"^/unity-helpers/?", "./", target)
            if suggestion == "./":
                suggestion = "./README.md"
            report(
                number,
                f"Absolute GitHub Pages path '{target}' will break in CI; use relative path instead (e.g., '{suggestion}')",
                line,
            )
        if re.match(r"[a-zA-Z]", target) and not SCHEME.match(target):
            report(
                number,
                f"Internal link '{target}' missing relative prefix (./ or ../); jekyll-relative-links requires explicit relative paths",
                line,
            )

    for number, line in lines:
        definition = DEFINITION.match(line)
        if definition:
            target = target_text(definition["rest"])
            if target:
                definitions[definition["label"].strip().casefold()] = (target, number)
    if mode != "Format":
        for target, number in definitions.values():
            check_target(target, number, "Reference link")
    for number, line in lines:
        without_code = CODE_SPAN.sub("", line)
        if mode != "Targets":
            for match in CODE_SPAN.finditer(line):
                content = match[1] or match[2]
                if re.fullmatch(r"(\*\*/|\./)?\*?\.\w+", content):
                    continue
                mention = INLINE_PATH.search(content)
                if mention:
                    report(
                        number,
                        f"Inline code mentions markdown file '{mention['path']}'; use a human-readable link instead",
                        line,
                    )
            stripped = LINK.sub("", line)
            stripped = re.sub(r"<[^>]+>", "", stripped)
            stripped = CODE_SPAN.sub("", stripped)
            if not DEFINITION.match(line) and MARKDOWN_PATH.search(stripped):
                report(
                    number,
                    "Bare .md mention; convert to [Readable Text](file.md)",
                    line,
                )
            for match in LINK.finditer(line):
                if re.search(r"\.md(?:#[^\]]+)?$", match["label"]) and re.search(
                    r"\.md(?:#[^)]+)?$", match["target"]
                ):
                    report(
                        number,
                        f"Link text is a filename; use human-readable text for {match['target']}",
                        line,
                    )
            for match in LINK.finditer(without_code):
                check_target_format(target_text(match["target"]), number, line)
            definition = DEFINITION.match(line)
            if definition:
                check_target_format(target_text(definition["rest"]), number, line)
        if mode != "Format":
            for match in LINK.finditer(without_code):
                target = target_text(match["target"])
                check_target(target, number, "Image" if match["image"] else "Markdown link", line)
            for match in REFERENCE.finditer(line):
                definition_target = definitions.get(match["label"].strip().casefold())
                if definition_target:
                    target, _ = definition_target
                    if is_local(target) and not repository.resolve(
                        path, target, root_fallback=False
                    ):
                        report(
                            number,
                            f"Image reference '{match['label']}' points to '{target}' which does not resolve",
                            line,
                        )
    return findings


def scan(repository: Repository, paths: list[str], mode: str = "All") -> ScanResult:
    findings: list[Finding] = []
    sources_checked = 0
    for path in paths:
        file = repository.root / path
        if not file.is_file():
            continue
        suffix = file.suffix.lower()
        if suffix != ".md" and (mode == "Format" or suffix not in CODE_EXTENSIONS):
            continue
        if suffix != ".md" and (
            re.search(r"(^|/)tests?/", path, re.I)
            or re.search(r"(^|/)test_[^/]+\.py$|(^|/)[^/]+_test\.py$", path, re.I)
        ):
            continue
        if not repository.exists(file):
            raise ValueError(f"Documentation source must stay within the repository: {path}")
        text = file.read_text(encoding="utf-8-sig")
        sources_checked += 1
        if suffix == ".md":
            findings.extend(markdown_findings(repository, path, text, mode))
        elif CODE_PATH.search(text):
            masked = mask_comments(text, suffix)
            for number, line in enumerate(masked.splitlines(), 1):
                for match in CODE_PATH.finditer(line):
                    target = match[0].replace("\\", "/")
                    if not repository.resolve(path, target):
                        findings.append(
                            Finding(
                                path,
                                number,
                                f"Source reference '{target}' does not resolve to an existing markdown file",
                            )
                        )
    return ScanResult(findings, sources_checked)


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--paths", "-Paths", nargs="+", default=[])
    parser.add_argument(
        "--mode",
        "-Mode",
        type=str.title,
        choices=("All", "Targets", "Format"),
        default="All",
    )
    parser.add_argument("--verbose", "-VerboseOutput", action="store_true")
    args = parser.parse_args(argv)
    try:
        root = args.repo_root.resolve()
        repository = Repository(root, inventory(root))
        paths: list[str] = repository.files
        if args.paths:
            paths = []
            for raw in args.paths:
                file = Path(os.path.abspath(root / raw))  # noqa: PTH100 - validate the selected link, not only its resolved target.
                if not file.is_file() or not repository.exists(file):
                    raise ValueError(
                        f"Selected documentation source does not exist in this repository: {raw}"
                    )
                relative = file.relative_to(root).as_posix()
                if relative not in paths:
                    paths.append(relative)
        result = scan(repository, paths, args.mode)
        if result.sources_checked == 0:
            raise ValueError("No supported documentation source files were checked.")
        findings = result.findings
        for finding in findings:
            print(escape_command_data(f"{finding.path}:{finding.line}: {finding.message}"))
            if args.verbose and finding.source:
                print(escape_command_data("  " + finding.source))
        if findings:
            print(f"Markdown link lint failed: {len(findings)} issue(s) found.")
            return 1
        print("Markdown link lint passed: no issues found.")
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print(escape_command_data(str(error)), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
