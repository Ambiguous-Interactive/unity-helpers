"""Prepare release files and extract the existing changelog sections."""

from __future__ import annotations

import argparse
import json
import re
import sys
from datetime import datetime
from pathlib import Path
from typing import cast

from check_runner import mapping
from cli_encoding import configure_output
from generate_agent_skill_stubs import require_owned_path
from generate_skills_index import publish

ROOT = Path(__file__).resolve().parents[1]
UPM_BUDGET = 8000


def normalize(content: str) -> str:
    return content.replace("\r\n", "\n").replace("\r", "\n")


def semver(version: str) -> tuple[int, int, int]:
    match = re.match(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", version)
    if match is None:
        raise ValueError(
            f"Invalid semver '{version}'. Expected X.Y.Z with numeric components and no leading zeroes."
        )
    parts = tuple(int(match[index]) for index in (1, 2, 3))
    if any(part > 2147483647 for part in parts):
        raise ValueError("Release version component exceeds Int32 range")
    return parts[0], parts[1], parts[2]


def next_version(current: str, bump: str = "patch", version: str = "") -> str:
    major, minor, patch = semver(current)
    if version.strip():
        if semver(version) <= (major, minor, patch):
            raise ValueError(
                f"Explicit version {version} must be strictly greater than the current version {current}."
            )
        return version
    if bump == "major":
        return f"{major + 1}.0.0"
    if bump == "minor":
        return f"{major}.{minor + 1}.0"
    if bump == "patch":
        return f"{major}.{minor}.{patch + 1}"
    raise ValueError(f"Unknown bump kind '{bump}'. Expected: major, minor, patch.")


def fence_mask(lines: list[str]) -> list[bool]:
    character = ""
    width = 0
    mask: list[bool] = []
    for line in lines:
        inside = bool(character)
        marker = re.match(r"^(`{3,}|~{3,})", line.lstrip())
        if marker:
            if not character:
                character, width = marker[1][0], len(marker[1])
            elif marker[1][0] == character and len(marker[1]) >= width:
                character, width = "", 0
        mask.append(inside or bool(character))
    return mask


def heading(lines: list[str], fenced: list[bool], version: str) -> int | None:
    pattern = re.compile(rf"^## \[{re.escape(version)}\](?: - \d{{4}}-\d{{2}}-\d{{2}})?$", re.I)
    return next(
        (index for index, line in enumerate(lines) if not fenced[index] and pattern.match(line)),
        None,
    )


def section_body(lines: list[str], fenced: list[bool], index: int) -> tuple[list[str], int]:
    end = next(
        (i for i in range(index + 1, len(lines)) if not fenced[i] and lines[i].startswith("## ")),
        len(lines),
    )
    body = lines[index + 1 : end]
    while body and not body[0].strip():
        del body[0]
    while body and not body[-1].strip():
        body.pop()
    return body, end


def has_content(body: list[str]) -> bool:
    return any(line.strip() and not line.startswith("### ") for line in body)


def changelog_section(content: str, version: str) -> str:
    lines = normalize(content).split("\n")
    fenced = fence_mask(lines)
    index = heading(lines, fenced, version)
    if index is None:
        raise ValueError(f"CHANGELOG.md has no '## [{version}]' section.")
    body, _ = section_body(lines, fenced, index)
    if not has_content(body):
        raise ValueError(f"CHANGELOG.md section '## [{version}]' has no release-note content.")
    return "\n".join(body)


def rotate_changelog(content: str, version: str, date: str) -> tuple[str, bool]:
    normalized = normalize(content)
    lines = normalized.split("\n")
    fenced = fence_mask(lines)
    indexes = [
        i for i, line in enumerate(lines) if not fenced[i] and line.casefold() == "## [unreleased]"
    ]
    if len(indexes) != 1:
        raise ValueError(
            f"CHANGELOG.md must contain exactly one '## [Unreleased]' heading; found {len(indexes)}."
        )
    index = indexes[0]
    body, end = section_body(lines, fenced, index)
    if heading(lines, fenced, version) is not None:
        if has_content(body):
            raise ValueError(
                f"CHANGELOG.md already contains '## [{version}]' but '## [Unreleased]' still has release-note content."
            )
        changelog_section(normalized, version)
        return normalized.rstrip() + "\n", False
    if not has_content(body):
        raise ValueError("The '## [Unreleased]' section has no release-note content.")
    rotated = [*lines[: index + 1], "", f"## [{version}] - {date}", "", *body]
    if end < len(lines):
        rotated.extend(["", *lines[end:]])
    return "\n".join(rotated).rstrip() + "\n", True


def json_object(content: str) -> dict[str, object]:
    return mapping(cast("object", json.loads(content)))


def text_field(document: dict[str, object], name: str) -> str:
    value = document.get(name)
    if not isinstance(value, str):
        raise ValueError(f"package.json {name} must be a string")
    return value


def package_version(content: str, current: str, version: str) -> str:
    parsed = json_object(content)
    actual = next((value for key, value in parsed.items() if key.casefold() == "version"), None)
    if actual != current:
        raise ValueError(
            f"package.json parsed version '{actual}' did not match expected current version '{current}'."
        )
    pattern = re.compile(r'("version"\s*:\s*")' + re.escape(current) + r'(")')
    matches = list(pattern.finditer(content))
    if len(matches) != 1:
        raise ValueError(
            f"Expected exactly one package.json version property for '{current}'; found {len(matches)}."
        )
    updated = pattern.sub(lambda match: match[1] + version + match[2], content)
    if json_object(updated).get("version") != version:
        raise ValueError("package.json rewrite verification failed")
    return normalize(updated).rstrip() + "\n"


def lock_version(content: str, version: str) -> str:
    lock = json_object(content)
    if "version" in lock:
        lock["version"] = version
    if "packages" in lock:
        packages = mapping(lock["packages"])
        if "" in packages:
            package = mapping(packages[""])
            if "version" in package:
                package["version"] = version
    return json.dumps(lock, ensure_ascii=False, indent=2) + "\n"


def read(path: Path) -> str:
    require_owned_path(path)
    if not path.is_file():
        raise ValueError(f"Required release file not found: {path}")
    return path.read_bytes().decode("utf-8-sig")


def prepare(root: Path, bump: str, version: str, date: str, *, dry_run: bool = False) -> int:
    if re.fullmatch(r"\d{4}-\d{2}-\d{2}", date) is None:
        raise ValueError(f"Release date '{date}' must use YYYY-MM-DD format.")
    root = root.resolve(strict=True)
    package, changelog, lock = (
        root / name for name in ("package.json", "CHANGELOG.md", "package-lock.json")
    )
    content = read(package)
    current = text_field(json_object(content), "version")
    version = next_version(current, bump, version)
    updated_package = package_version(content, current, version)
    updated_changelog, rotated = rotate_changelog(read(changelog), version, date)
    updated_lock = lock_version(read(lock), version) if lock.exists() else None
    if not dry_run:
        if rotated:
            publish(changelog, updated_changelog)
        publish(package, updated_package)
        if updated_lock is not None:
            publish(lock, updated_lock)
    print(f"prepare-release: {current} -> {version}")
    if dry_run:
        print("prepare-release: dry run; no files were written.")
    else:
        print(f"prepare-release: rewrote {package}")
        if updated_lock is not None:
            print(f"prepare-release: rewrote {lock}")
        print(
            f"prepare-release: rotated {changelog}"
            if rotated
            else f"prepare-release: {changelog} already had the target version heading."
        )
    return 0


def release_notes(root: Path, version: str, *, footer: bool = False) -> str:
    package = json_object(read(root / "package.json"))
    section = changelog_section(read(root / "CHANGELOG.md"), version).rstrip()
    if not footer:
        return section + "\n"
    name = text_field(package, "name")
    return (
        section + "\n\n---\n\n## Install\n\n"
        "- Import the attached `.unitypackage` into a Unity project, or\n"
        f"- install `{name}@{version}` from npm / OpenUPM through Unity Package Manager.\n\n"
        "The release includes the npm tarball and the `.unitypackage`, each with a `.sha256` checksum.\n"
    )


def utf16_length(text: str) -> int:
    return len(text.encode("utf-16-le", errors="surrogatepass")) // 2


def upm_value(section: str, url: str) -> str:
    normalized = normalize(section).rstrip()
    if utf16_length(normalized) <= UPM_BUDGET:
        return normalized
    pointer = f"\n\n(Truncated. Full changelog: {url})"
    room = UPM_BUDGET - utf16_length(pointer)
    kept: list[str] = []
    used = 0
    for line in normalized.split("\n"):
        cost = utf16_length(line) + 1
        if used + cost > room:
            break
        kept.append(line)
        used += cost
    return "\n".join(kept).rstrip() + pointer


def upm_content(content: str, value: str) -> str:
    encoded = json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    block = f'  "_upm": {{\n    "changelog": {encoded}\n  }},\n'
    matches = list(re.finditer(r'(?ms)^[ ]{2}"_upm":[ ]*\{.*?^[ ]{2}\},\r?\n', content))
    if len(matches) > 1:
        raise ValueError(f"Expected at most one package.json _upm block; found {len(matches)}.")
    if matches:
        start, end = matches[0].span()
    else:
        anchors = list(re.finditer(r'(?m)^[ ]{2}"changelogUrl":[^\n]*\n', content))
        if len(anchors) != 1:
            raise ValueError(
                f"Expected exactly one package.json changelogUrl property to anchor _upm after; found {len(anchors)}."
            )
        start = end = anchors[0].end()
    updated = content[:start] + block + content[end:]
    if mapping(json_object(updated).get("_upm")).get("changelog") != value:
        raise ValueError(
            "package.json rewrite verification failed; the parsed _upm.changelog did not match."
        )
    return normalize(updated).rstrip() + "\n"


def sync_upm(root: Path, *, check: bool = False) -> int:
    path = root / "package.json"
    content = read(path)
    package = json_object(content)
    url = text_field(package, "changelogUrl")
    if not url.strip():
        raise ValueError(
            "package.json has no changelogUrl; UPM needs one to offer a Changelog link."
        )
    version = text_field(package, "version")
    expected = upm_value(changelog_section(read(root / "CHANGELOG.md"), version), url)
    actual = mapping(package["_upm"]).get("changelog") if "_upm" in package else ""
    if actual == expected:
        print(
            f"sync-upm-changelog: package.json _upm.changelog is in step with CHANGELOG.md [{version}]."
        )
        return 0
    if check:
        raise ValueError(
            f"package.json _upm.changelog is stale for version {version}. Run 'npm run sync:upm-changelog'."
        )
    publish(path, upm_content(content, expected))
    print(
        f"sync-upm-changelog: wrote {utf16_length(expected)} characters of [{version}] notes to package.json."
    )
    return 0


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    prepare_parser = commands.add_parser("prepare")
    prepare_parser.add_argument("--bump", choices=("major", "minor", "patch"), default="patch")
    prepare_parser.add_argument("--version", default="")
    prepare_parser.add_argument("--date", default=datetime.now().astimezone().date().isoformat())
    prepare_parser.add_argument("--dry-run", action="store_true")
    notes_parser = commands.add_parser("notes")
    notes_parser.add_argument("--version", required=True)
    notes_parser.add_argument("--footer", action="store_true")
    notes_parser.add_argument("--output-path", type=Path)
    sync_parser = commands.add_parser("sync-upm")
    sync_parser.add_argument("--check", action="store_true")
    section_parser = commands.add_parser("section")
    section_parser.add_argument("--version", required=True)
    for command in (prepare_parser, notes_parser, sync_parser, section_parser):
        command.add_argument("--repo-root", type=Path, default=ROOT)
    args = parser.parse_args(arguments)
    try:
        root = args.repo_root.resolve(strict=True)
        if args.command == "prepare":
            return prepare(root, args.bump, args.version, args.date, dry_run=args.dry_run)
        if args.command == "sync-upm":
            return sync_upm(root, check=args.check)
        if args.command == "section":
            changelog_section(read(root / "CHANGELOG.md"), args.version)
            return 0
        notes = release_notes(root, args.version, footer=args.footer)
        if args.output_path:
            args.output_path.parent.mkdir(parents=True, exist_ok=True)
            publish(args.output_path, notes)
            print(f"write-release-notes: wrote {args.output_path}")
        else:
            print(notes)
        return 0
    except (OSError, ValueError) as error:
        print(f"{args.command} failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
