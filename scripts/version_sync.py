"""Synchronize owned version references while retaining the shared index coordinator."""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass
from io import TextIOWrapper
from pathlib import Path
from typing import cast

from check_runner import mapping
from generate_agent_skill_stubs import require_owned_path
from generate_skills_index import publish

ROOT = Path(__file__).resolve().parents[1]
START = "# <!-- AUTO-UPDATED: package-versions -->"
END = "# <!-- END AUTO-UPDATED: package-versions -->"
STRICT_VERSION = re.compile(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)")
BANNER_VERSION = re.compile(
    r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"
    r"(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?"
)
BANNER_PATTERN = re.compile(
    r"""<text\b(?:[^<>"']|"[^"]*"|'[^']*')*(?<!/)>(?P<body>[^<]*)</text>""",
    re.IGNORECASE,
)
BANNER_TEXT_PATTERN = re.compile(r"v\d+\.\d+\.\d+[^<]*", re.IGNORECASE)
XML_NON_TEXT = re.compile(r"<!--.*?(?:-->|$)|<!\[CDATA\[.*?(?:\]\]>|$)", re.DOTALL)
CONTEXT_PATTERN = re.compile(
    r"^\*\*Version\*\*:\s+\d+\.\d+\.\d+[^\r\n]*", re.MULTILINE | re.IGNORECASE
)


@dataclass(frozen=True)
class Badge:
    version: str
    source_spans: tuple[tuple[int, int], ...]


@dataclass(frozen=True)
class Change:
    path: Path
    content: str


@dataclass(frozen=True)
class Plan:
    changes: list[Change]
    messages: list[str]
    inputs: dict[Path, bytes | None]


@dataclass(frozen=True)
class GitInfo:
    cwd: Path
    lock: Path


class StageFailure(RuntimeError):
    def __init__(self, status: int) -> None:
        super().__init__(
            f"git add failed with exit code {status}. Inspect selected version files and the active index before retrying."
        )
        self.status = status


def read(path: Path, inputs: dict[Path, bytes | None], *, optional: bool = False) -> str | None:
    require_owned_path(path)
    if not path.exists():
        inputs[path] = None
        if optional:
            return None
        raise ValueError(f"Required version synchronization input is missing: {path}")
    if not path.is_file():
        raise ValueError(f"Expected a physical regular version synchronization input: {path}")
    data = path.read_bytes()
    inputs[path] = data
    return data.decode("utf-8-sig")


def read_object(path: Path, inputs: dict[Path, bytes | None]) -> dict[str, object]:
    content = read(path, inputs)
    assert content is not None  # noqa: S101 - required read either returns text or raises.
    return mapping(cast("object", json.loads(content)))


def package_version(root: Path, inputs: dict[Path, bytes | None], *, banner: bool) -> str:
    value = read_object(root / "package.json", inputs).get("version")
    pattern = BANNER_VERSION if banner else STRICT_VERSION
    if not isinstance(value, str) or pattern.fullmatch(value) is None:
        raise ValueError("package.json version must be a complete ASCII version string.")
    return value


def banner_badges(content: str) -> list[Badge]:
    masked = XML_NON_TEXT.sub(lambda match: " " * len(match[0]), content)
    badges: list[Badge] = []
    for match in BANNER_PATTERN.finditer(masked):
        start, end = match.span("body")
        previous = start
        spans: list[tuple[int, int]] = []
        for opaque in XML_NON_TEXT.finditer(content, start, end):
            if previous < opaque.start():
                spans.append((previous, opaque.start()))
            previous = opaque.end()
        if previous < end:
            spans.append((previous, end))
        visible = "".join(content[left:right] for left, right in spans)
        if BANNER_TEXT_PATTERN.fullmatch(visible):
            badges.append(Badge(visible, tuple(spans)))
    return badges


def replace_badges(content: str, badges: list[Badge], replacement: str) -> str:
    parts: list[str] = []
    previous = 0
    for badge in badges:
        offset = 0
        for index, (start, end) in enumerate(badge.source_spans):
            text = (
                replacement[offset:]
                if index == len(badge.source_spans) - 1
                else replacement[offset : offset + end - start]
            )
            parts.extend((content[previous:start], text))
            previous = end
            offset += end - start
    parts.append(content[previous:])
    return "".join(parts).rstrip() + "\n"


def banner_plan(root: Path) -> Plan:
    inputs: dict[Path, bytes | None] = {}
    version = package_version(root, inputs, banner=True)
    changes: list[Change] = []
    messages = [f"Found version: v{version}"]
    banner = root / "docs/images/unity-helpers-banner.svg"
    content = read(banner, inputs, optional=True)
    if content is None:
        messages.append(f"Banner SVG not found at: {banner}, skipping.")
    else:
        replacement = f"v{version}"
        badges = banner_badges(content)
        if not badges:
            messages.append("No version pattern found in SVG, skipping.")
        elif all(badge.version.casefold() == replacement.casefold() for badge in badges):
            messages.append(f"Banner already has correct version: v{version}")
        else:
            changes.append(Change(banner, replace_badges(content, badges, replacement)))
            messages.extend((f"Updated banner version to: v{version}", f"Staged: {banner}"))
    context = root / ".llm/context.md"
    content = read(context, inputs, optional=True)
    if content is None:
        messages.append(".llm/context.md not found, skipping.")
    else:
        replacement = f"**Version**: {version}"
        headings = list(CONTEXT_PATTERN.finditer(content))
        if not headings:
            messages.append("No version pattern found in .llm/context.md, skipping.")
        elif all(heading[0].casefold() == replacement.casefold() for heading in headings):
            messages.append(f".llm/context.md already has correct version: {version}")
        else:
            changes.append(
                Change(context, CONTEXT_PATTERN.sub(lambda _: replacement, content).rstrip() + "\n")
            )
            messages.extend(
                (f"Updated .llm/context.md version to: {version}", f"Staged: {context}")
            )
    return Plan(changes, messages, inputs)


def check_banner(root: Path) -> int:
    root = root.resolve(strict=True)
    inputs: dict[Path, bytes | None] = {}
    version = package_version(root, inputs, banner=True)
    banner = read(root / "docs/images/unity-helpers-banner.svg", inputs)
    context = read(root / ".llm/context.md", inputs)
    if banner is None or context is None:
        raise ValueError("Banner validation requires both version references.")
    badges = banner_badges(banner)
    headings = list(CONTEXT_PATTERN.finditer(context))
    if not badges or any(badge.version.casefold() != f"v{version}".casefold() for badge in badges):
        raise ValueError(f"Banner SVG version badge does not match package version {version}.")
    if not headings or any(
        heading[0].casefold() != f"**Version**: {version}".casefold() for heading in headings
    ):
        raise ValueError(f".llm/context.md version field does not match package version {version}.")
    print(f"Package version: {version}")
    print(f"Banner SVG contains version {version}")
    print(f".llm/context.md contains version {version}")
    print("All version references are in sync.")
    return 0


def version_key(version: str) -> tuple[int, int, int]:
    if STRICT_VERSION.fullmatch(version) is None:
        raise ValueError(f"Invalid issue template version: {version!r}.")
    parts = version.split(".")
    if any(len(part) > 10 or int(part) > 2147483647 for part in parts):
        raise ValueError(
            f"Issue template version exceeds the supported component range: {version!r}."
        )
    return int(parts[0]), int(parts[1]), int(parts[2])


def template_plan(root: Path, *, add_package_version: bool, stage: bool) -> Plan:
    inputs: dict[Path, bytes | None] = {}
    manifest_path = root / ".github/issue-template-versions.json"
    raw = read_object(manifest_path, inputs).get("versions")
    if not isinstance(raw, list) or not raw:
        raise ValueError("Issue template version manifest requires a nonempty versions array.")
    versions: list[str] = []
    seen: set[str] = set()
    for value in cast("list[object]", raw):
        if not isinstance(value, str) or value in seen:
            raise ValueError(f"Invalid or duplicate issue template version: {value!r}.")
        version_key(value)
        versions.append(value)
        seen.add(value)
    package = package_version(root, inputs, banner=False)
    version_key(package)
    changed = package not in seen
    if changed and not add_package_version:
        raise ValueError(
            f"package.json version '{package}' is not listed in {manifest_path}. Run release preparation to add it."
        )
    if changed:
        versions.append(package)
    versions.sort(key=version_key, reverse=True)
    messages = [
        f"Total unique versions collected: {len(versions)}",
        f"Versions (sorted): {', '.join(versions)}",
    ]
    changes: list[Change] = []
    if changed:
        changes.append(Change(manifest_path, json.dumps({"versions": versions}, indent=2) + "\n"))
        messages.append(f"Issue template version manifest: added {package}.")
    for name in ("bug_report.yml", "feature_request.yml"):
        path = root / ".github/ISSUE_TEMPLATE" / name
        original = read(path, inputs, optional=True)
        if original is None:
            messages.append(f"WARNING: Template file not found, skipping: {path}")
            continue
        content = original.replace("\r\n", "\n")
        start, end = content.find(START), content.find(END)
        if start < 0 or end < 0:
            messages.append(f"WARNING: Sentinel comments not found in {name}, skipping.")
            continue
        if end <= start:
            messages.append(f"WARNING: Sentinel comments are misordered in {name}, skipping.")
            continue
        line_start = content.rfind("\n", 0, start) + 1
        indent = content[line_start:start]
        if indent.strip():
            raise ValueError(f"Version sentinel must start its own indented line: {path}")
        line_end = content.find("\n", end)
        if line_end < 0:
            line_end = len(content)
        options = "\n".join(
            [
                indent + START,
                *(f'{indent}- "{version}"' for version in versions),
                f'{indent}- "Other"',
                indent + END,
            ]
        )
        updated = content[:line_start] + options + content[line_end:]
        if updated == content:
            messages.append(f"{name}: already up-to-date.")
        else:
            changes.append(Change(path, updated))
            messages.append(f"{name}: updated version dropdown.")
    if not changes:
        messages.append("No template files were modified.")
    elif stage:
        messages.append(f"Staged: {', '.join(str(change.path) for change in changes)}")
    return Plan(changes, messages, inputs)


def positive_environment(name: str, default: int) -> int:
    value = os.environ.get(name, "").strip()
    if re.fullmatch(r"[+-]?[0-9]+", value) is None or len(value) > 11:
        return default
    number = int(value)
    return number if 0 < number <= 2147483647 else default


def git_info(root: Path, cwd: Path, *, required: bool) -> GitInfo | None:
    executable = shutil.which("git")
    if executable is None:
        if required:
            raise ValueError("git not found; cannot inspect or stage files.")
        print("WARNING: Git is not available; continuing without staging.")
        return None
    result = subprocess.run(  # noqa: S603 - fixed read-only Git root query.
        [executable, "rev-parse", "--show-toplevel"], cwd=cwd, capture_output=True, check=False
    )
    if result.returncode:
        if required:
            raise ValueError("Git repository required for banner synchronization.")
        print("WARNING: Not in a Git repository; continuing without staging.")
        return None
    actual = Path(result.stdout.decode("utf-8").rstrip("\r\n")).resolve(strict=True)
    if actual != root:
        raise ValueError("The selected version root differs from the active Git repository.")
    result = subprocess.run(  # noqa: S603 - fixed read-only query preserves the active alternate index.
        [executable, "rev-parse", "--git-path", "index"], cwd=cwd, capture_output=True, check=True
    )
    index = Path(result.stdout.decode("utf-8").rstrip("\r\n"))
    lock = Path(str(index if index.is_absolute() else cwd / index) + ".lock")
    deadline = time.monotonic() + positive_environment("GIT_LOCK_INITIAL_WAIT_MS", 10000) / 1000
    poll = positive_environment("GIT_LOCK_POLL_INTERVAL_MS", 50) / 1000
    while lock.exists() and (remaining := deadline - time.monotonic()) > 0:
        time.sleep(min(poll, remaining))
    if lock.exists():
        print(
            "WARNING: index.lock still held after waiting. Proceeding anyway, but operations may fail."
        )
    return GitInfo(cwd, lock)


def stage_files(changes: list[Change], info: GitInfo, pwsh: str) -> None:
    result = subprocess.run(  # noqa: S603 - fixed adapter delegates index mutation to the existing retry/mutex helper; paths travel through JSON stdin.
        [pwsh, "-NoProfile", "-File", str(ROOT / "scripts/stage-version-sync-files.ps1")],
        input=json.dumps({"files": [str(change.path) for change in changes]}).encode("utf-8"),
        cwd=info.cwd,
        capture_output=True,
        check=False,
    )
    if result.stdout:
        sys.stdout.write(result.stdout.decode("utf-8", errors="replace"))
    if result.stderr:
        sys.stderr.write(result.stderr.decode("utf-8", errors="replace"))
    if result.returncode:
        raise StageFailure(result.returncode if result.returncode > 0 else 1)


def synchronize(
    root: Path, kind: str, *, add_package_version: bool = False, cwd: Path | None = None
) -> int:
    """Replace selected files, then stage; keep other writers idle during this operation.

    This is not a multi-file transaction. A later filesystem or staging failure can
    leave working files updated; callers must inspect the tree and active index.
    """
    root = root.resolve(strict=True)
    require_owned_path(root)
    if kind not in ("banner", "templates") or (kind == "banner" and add_package_version):
        raise ValueError("Expected banner or templates; only templates can add a package version.")
    info = git_info(root, root if cwd is None else cwd, required=kind == "banner")
    plan = (
        banner_plan(root)
        if kind == "banner"
        else template_plan(root, add_package_version=add_package_version, stage=info is not None)
    )
    pwsh = shutil.which("pwsh") if plan.changes and info is not None else None
    if plan.changes and info is not None and pwsh is None:
        raise ValueError("PowerShell is required for the existing Git index coordinator.")
    for path, expected in plan.inputs.items():
        require_owned_path(path)
        actual = path.read_bytes() if path.exists() else None
        if actual != expected:
            raise ValueError(f"Version synchronization input changed while preparing: {path}")
    for change in plan.changes:
        require_owned_path(change.path)
        if not change.path.parent.is_dir() or not change.path.parent.stat().st_mode & 0o222:
            raise ValueError(f"Refusing a non-writable version directory: {change.path.parent}")
        if change.path.exists() and not change.path.stat().st_mode & 0o222:
            raise ValueError(f"Refusing a read-only version destination: {change.path}")
    for change in plan.changes:
        publish(change.path, change.content)
    if plan.changes and info is not None and pwsh is not None:
        stage_files(plan.changes, info, pwsh)
    for message in plan.messages:
        print(message)
    return 0


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        if isinstance(stream, TextIOWrapper):
            stream.reconfigure(encoding="utf-8", newline="\n", errors=stream.errors)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("kind", choices=("banner", "templates", "check-banner"))
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--add-package-version", action="store_true")
    args = parser.parse_args(argv)
    try:
        if args.kind == "check-banner":
            if args.add_package_version:
                raise ValueError("Only template synchronization can add a package version.")
            return check_banner(args.repo_root)
        return synchronize(
            args.repo_root, args.kind, add_package_version=args.add_package_version, cwd=Path.cwd()
        )
    except StageFailure as error:
        print(f"Version sync failed: {error}", file=sys.stderr)
        return error.status
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"Version sync failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
