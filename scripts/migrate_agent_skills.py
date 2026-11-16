"""Split and verify canonical skills without changing their reconstructed contents."""

from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unicodedata
from pathlib import Path
from typing import NotRequired, TypedDict, cast

from check_runner import mapping, strings
from cli_encoding import configure_output
from generate_agent_skill_stubs import require_owned_path
from generate_skills_index import publish

ROOT = Path(__file__).resolve().parents[1]
MAX_LINES = 199
CHUNK_LINES = 180


class Split(TypedDict):
    name: str
    sha256: str
    parts: list[str]
    routerSha256: NotRequired[str]


Update = tuple[Path, Split, str, list[str], str]
Snapshots = dict[Path, bytes | None]


def digest(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def line_count(text: str) -> int:
    return len(text.rstrip().split("\n"))


def read(path: Path, snapshots: Snapshots | None = None) -> str:
    require_owned_path(path)
    if path.is_symlink() or not path.is_file():
        raise ValueError(f"Expected a physical regular file: {path}")
    content = path.read_bytes()
    if snapshots is not None:
        if path in snapshots and snapshots[path] != content:
            raise ValueError(f"Skill input changed during migration: {path}")
        snapshots[path] = content
    return content.decode("utf-8")


def verify_snapshots(snapshots: Snapshots) -> None:
    for path, expected in snapshots.items():
        require_owned_path(path)
        current = path.read_bytes() if path.exists() else None
        if current != expected:
            raise ValueError(f"Skill input changed during migration: {path}")


def child(directory: Path, name: str) -> Path:
    require_owned_path(directory)
    if not name or name in {".", ".."} or any(character in name for character in "/\\:\0"):
        raise ValueError(f"Expected a literal skill or reference filename: {name!r}")
    path = directory / name
    if directory.is_symlink() or path.is_symlink():
        raise ValueError(f"Refusing a redirected skill path: {path}")
    return path


def prefix_of(text: str) -> str:
    trigger = re.search(r"<!--\s*trigger:[^\r\n]*?-->\n\n", text)
    if trigger is None or not text.startswith("# "):
        raise ValueError("Skill must start with a title and trigger comment")
    return text[: trigger.end()]


def router_digest(text: str) -> str:
    return digest(
        "\n".join(
            line.rstrip() for line in text[len(prefix_of(text)) :].split("\n") if line.rstrip()
        )
    )


def fenced_lines(lines: list[str]) -> list[bool]:
    character: str | None = None
    width = 0
    result: list[bool] = []
    for line in lines:
        match = re.match(r"^ {0,3}(`{3,}|~{3,})", line)
        was_fenced = character is not None
        if match and character is None:
            character, width = match[1][0], len(match[1])
        elif character is not None and re.fullmatch(
            rf" {{0,3}}{re.escape(character)}{{{width},}}[ \t]*", line.rstrip("\r\n")
        ):
            character, width = None, 0
        result.append(was_fenced or character is not None or match is not None)
    return result


def chunks_of(body: str) -> list[str]:
    lines = re.findall(r"[^\n]*\n|[^\n]+$", body)
    fenced = fenced_lines([line.removesuffix("\n") for line in lines])
    chunks: list[str] = []
    start = 0
    while start < len(lines):
        if len(lines) - start <= CHUNK_LINES:
            chunks.append("".join(lines[start:]))
            break
        candidates: list[tuple[int, int]] = []
        for end in range(start + 1, min(start + CHUNK_LINES, len(lines)) + 1):
            if lines[end - 1].strip() or fenced[end - 1]:
                continue
            following = lines[end] if end < len(lines) else ""
            rank = 3 if following.startswith("## ") else 2 if following.startswith("### ") else 1
            candidates.append((rank, end))
        preferred = [candidate for candidate in candidates if candidate[1] >= start + 120]
        if not candidates:
            raise ValueError(f"No safe split boundary within {CHUNK_LINES} lines")
        _, end = max(preferred or candidates)
        chunks.append("".join(lines[start:end]))
        start = end
    return chunks


def rewrite_links(text: str, name: str, root: Path, *, reverse: bool = False) -> str:
    skills, references = root / ".llm/skills", root / ".llm/references"
    fenced = False
    character: str | None = None
    width = 0
    result: list[str] = []

    def replace(match: re.Match[str]) -> str:
        target = match[1]
        local = f"../skills/{name}.md"
        if not reverse and target.startswith("#"):
            return f"]({local}{target})"
        if reverse and target.startswith(local + "#"):
            return f"]({target[len(local) :]})"
        if re.match(r"^\.{1,2}/", target) is None:
            return match[0]
        parts = target.split("#", 2)
        filename = parts[0]
        fragment = parts[1] if len(parts) > 1 else ""
        source, destination = (references, skills) if reverse else (skills, references)
        absolute = Path(os.path.abspath(source / filename))  # noqa: PTH100 - lexical link transformation, not filesystem resolution.
        relative = os.path.relpath(absolute, destination).replace(os.sep, "/")
        if relative == ".":
            relative = ""
        if not relative.startswith("."):
            relative = "./" + relative
        return f"]({relative}{'#' + fragment if fragment else ''})"

    for line in re.split(r"(?<=\n)", text):
        marker = re.match(r"^ {0,3}(`{3,}|~{3,})", line)
        if marker:
            if not fenced:
                fenced, character, width = True, marker[1][0], len(marker[1])
            elif (
                marker[1][0] == character
                and len(marker[1]) >= width
                and re.fullmatch(r" {0,3}(?:`{3,}|~{3,})[ \t]*", line.rstrip("\r\n"))
            ):
                fenced = False
            result.append(line)
        else:
            result.append(line if fenced else re.sub(r"\]\(([^)]+)\)", replace, line))
    return "".join(result)


def reference_text(name: str, index: int, body: str, root: Path) -> str:
    return f"# {name} - Part {index + 1}\n\n## Split Content\n\n{rewrite_links(body, name, root)}"


def reference_body(text: str, part: str) -> str:
    heading = re.match(r"^# [^\n]+\n\n(?:## Split Content\n\n)?", text)
    if heading is None:
        raise ValueError(f"Missing reference title: {part}")
    return text[heading.end() :]


def slug(text: str, used: set[str]) -> str:
    text = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", text)
    text = re.sub(r"<[^>]+>", "", text).lower().strip()
    base = "".join(
        character
        for character in text
        if character in "_- " or unicodedata.category(character)[0] in "LN"
    ).replace(" ", "-")
    result, suffix = base, 0
    while result in used:
        suffix += 1
        result = f"{base}-{suffix}"
    used.add(result)
    return result


def router_text(prefix: str, name: str, chunks: list[str]) -> str:
    headings: list[str] = []
    for index, chunk in enumerate(chunks):
        lines = chunk.split("\n")
        fenced = fenced_lines(lines)
        used = {slug(f"{name} - Part {index + 1}", set()), "split-content"}
        for line_index, line in enumerate(lines):
            if fenced[line_index] or re.match(r"^#{2,6} ", line) is None:
                continue
            anchor = slug(re.sub(r"^#{2,6} ", "", line), used)
            target = f"../references/{name}-part-{index + 1}.md#{anchor}"
            if line.startswith("## ") or "[" in line:
                headings.append(f"{line}\n\n[Read section]({target})")
            else:
                match = re.fullmatch(r"(#{3,6}) (.*)", line)
                if match is None:
                    raise ValueError("Invalid routed heading")
                headings.append(f"{match[1]} [{match[2]}]({target})")
    navigation = "\n".join(
        f"- [Part {index + 1}](../references/{name}-part-{index + 1}.md)"
        for index in range(len(chunks))
    )
    return f"{prefix}## Reference Parts\n\n{navigation}\n\n" + "\n\n".join(headings) + "\n"


def load_manifest(
    path: Path, snapshots: Snapshots | None = None
) -> tuple[dict[str, object], list[Split]]:
    manifest = mapping(cast("object", json.loads(read(path, snapshots))))
    raw = manifest.get("skills")
    if not isinstance(raw, list):
        raise ValueError("Skill manifest requires a skills array")
    entries: list[Split] = []
    for value in cast("list[object]", raw):
        row = mapping(value)
        if not isinstance(row.get("name"), str) or not isinstance(row.get("sha256"), str):
            raise ValueError("Skill split requires name and sha256 strings")
        strings(row.get("parts"))
        if "routerSha256" in row and not isinstance(row["routerSha256"], str):
            raise ValueError("Router sha256 must be a string")
        entry = cast("Split", row)
        child(path.parent / "skills", entry["name"] + ".md")
        for part in entry["parts"]:
            child(path.parent / "references", part)
        entries.append(entry)
    if len({entry["name"] for entry in entries}) != len(entries):
        raise ValueError("Skill split names must be unique")
    return manifest, entries


def format_manifest(manifest: dict[str, object], root: Path) -> str:
    with tempfile.TemporaryDirectory(prefix="skill-manifest-format-") as temporary:
        ignore = Path(temporary) / "empty-ignore"
        ignore.write_bytes(b"")
        result = subprocess.run(  # noqa: S603 - fixed installed formatter, owned empty ignore file and literal stdin filepath.
            [
                sys.executable,
                str(ROOT / "scripts/run_node_bin.py"),
                "prettier",
                "--parser",
                "json",
                "--ignore-path",
                str(ignore),
                "--with-node-modules",
                "--stdin-filepath",
                str(root / ".llm/skill-splits.json"),
            ],
            input=json.dumps(manifest, ensure_ascii=False).encode("utf-8"),
            cwd=root,
            capture_output=True,
            check=False,
            timeout=60,
        )
    if result.returncode:
        raise ValueError(f"Manifest formatting failed: {result.stderr.decode('utf-8').strip()}")
    if not result.stdout.strip():
        raise ValueError("Manifest formatter returned no content")
    return result.stdout.decode("utf-8")


def chunks_for(
    entry: Split, root: Path, snapshots: Snapshots | None = None
) -> tuple[Path, str, list[str]]:
    source = child(root / ".llm/skills", entry["name"] + ".md")
    canonical = read(source, snapshots)
    router_sha256 = entry.get("routerSha256")
    if router_sha256 and router_digest(canonical) != router_sha256:
        raise ValueError(f"Router changed outside refresh: {entry['name']}")
    chunks = [
        rewrite_links(
            reference_body(read(child(root / ".llm/references", part), snapshots), part),
            entry["name"],
            root,
            reverse=True,
        )
        for part in entry["parts"]
    ]
    return source, canonical, chunks


def check_entry(entry: Split, root: Path) -> None:
    _, canonical, chunks = chunks_for(entry, root)
    if line_count(canonical) > MAX_LINES:
        raise ValueError(f"Oversized router: {entry['name']}")
    for part in entry["parts"]:
        if line_count(read(child(root / ".llm/references", part))) > MAX_LINES:
            raise ValueError(f"Oversized reference: {part}")
        if f"](../references/{part})" not in canonical:
            raise ValueError(f"Router does not link to {part}")
    prefix = prefix_of(canonical)
    if digest(prefix + "".join(chunks)) != entry["sha256"]:
        raise ValueError(f"Reconstruction mismatch: {entry['name']}")
    for link in re.findall(
        r"\]\(\.\./references/[^)]*\)", router_text(prefix, entry["name"], chunks)
    ):
        if link not in canonical:
            raise ValueError(f"Missing anchored section link in {entry['name']}: {link}")


def commit_updates(
    updates: list[Update],
    manifest: dict[str, object],
    root: Path,
    expected_manifest: bytes | None,
    snapshots: Snapshots,
) -> None:
    for _, entry, _, _, _ in updates:
        for part in entry["parts"]:
            destination = child(root / ".llm/references", part)
            if destination not in snapshots:
                snapshots[destination] = destination.read_bytes() if destination.exists() else None
    verify_snapshots(snapshots)
    formatted = format_manifest(manifest, root)
    manifest_path = root / ".llm/skill-splits.json"
    recovery = root / ".llm/.skill-migration-recovery"
    recovery.mkdir(mode=0o700)
    retain = False
    try:
        verify_snapshots(snapshots)
        current_manifest = manifest_path.read_bytes() if manifest_path.exists() else None
        if current_manifest != expected_manifest:
            raise ValueError(
                "Skill manifest changed during migration; retry from the current source"
            )
        owned = (
            {entry["name"]: set(entry["parts"]) for entry in load_manifest(manifest_path)[1]}
            if current_manifest is not None
            else {}
        )
        files: list[tuple[Path, str]] = []
        for source, entry, router, reference_texts, original_source in updates:
            if read(source) != original_source:
                raise ValueError(f"Skill source changed during migration: {source}")
            for part, content in zip(entry["parts"], reference_texts, strict=True):
                destination = child(root / ".llm/references", part)
                if destination.exists() and part not in owned.get(entry["name"], set()):
                    raise ValueError(f"Refusing an existing unowned reference destination: {part}")
                files.append((destination, content))
            files.append((source, router))
        files.append((manifest_path, formatted))
        if len({path for path, _ in files}) != len(files):
            raise ValueError("Skill migration contains duplicate publication destinations")
        for path, _ in files:
            require_owned_path(path)
            if not path.parent.is_dir() or not path.parent.stat().st_mode & 0o222:
                raise ValueError(f"Refusing a non-writable migration directory: {path.parent}")
            if path.exists() and (not path.is_file() or not path.stat().st_mode & 0o222):
                raise ValueError(f"Refusing a non-writable regular migration destination: {path}")
        staged: list[tuple[Path, Path, Path | None]] = []
        journal: list[dict[str, object]] = []
        for index, (path, content) in enumerate(files):
            backup = recovery / f"before-{index}" if path.exists() else None
            if backup is not None:
                shutil.copy2(path, backup)
            after = recovery / f"after-{index}"
            publish(after, content)
            if backup is not None:
                shutil.copymode(backup, after)
            staged.append((path, after, backup))
            journal.append(
                {
                    "path": path.relative_to(root).as_posix(),
                    "backup": backup.name if backup is not None else None,
                    "before_sha256": hashlib.sha256(backup.read_bytes()).hexdigest()
                    if backup is not None
                    else None,
                    "after_sha256": digest(content),
                }
            )
        (recovery / "recovery.json").write_text(
            json.dumps({"files": journal}, indent=2) + "\n", encoding="utf-8"
        )
        verify_snapshots(snapshots)
        attempted: list[tuple[Path, Path | None, bytes]] = []
        retain = True
        try:
            for path, after, backup in staged:
                verify_snapshots({path: snapshots[path]})
                published = after.read_bytes()
                attempted.append((path, backup, published))
                after.replace(path)
                snapshots[path] = published
            verify_snapshots(snapshots)
        except BaseException as error:
            failures: list[str] = []
            for path, backup, published in reversed(attempted):
                try:
                    require_owned_path(path)
                    current = path.read_bytes() if path.exists() else None
                    original = backup.read_bytes() if backup is not None else None
                    if current == original:
                        continue
                    if current != published:
                        failures.append(
                            f"{path}: changed during migration; preserving authored edit"
                        )
                        continue
                    if backup is None:
                        path.unlink(missing_ok=True)
                    else:
                        backup.replace(path)
                except OSError as rollback_error:
                    failures.append(f"{path}: {rollback_error}")
            if failures:
                raise ValueError(
                    f"Skill publication rollback failed; recovery files retained at {recovery}: {'; '.join(failures)}"
                ) from error
            retain = False
            raise
        else:
            retain = False
    finally:
        if not retain:
            shutil.rmtree(recovery)


def run(mode: str, root: Path = ROOT) -> int:
    if mode not in {"--write", "--check", "--plan", "--refresh"}:
        raise ValueError("Usage: migrate_agent_skills.py --plan|--write|--check|--refresh")
    root = root.resolve(strict=True)
    if (root / ".llm").is_symlink():
        raise ValueError("Refusing a redirected instruction directory")
    pending = list((root / ".llm").glob(".skill-migration-*"))
    if pending:
        raise ValueError(f"Pending skill migration recovery; inspect {pending[0]} before retrying")
    skills = root / ".llm/skills"
    require_owned_path(skills)
    if not skills.is_dir():
        raise ValueError("Skills directory is missing")
    sources = sorted(
        (
            path
            for path in skills.iterdir()
            if path.name.endswith(".md") and path.name != "index.md"
        ),
        key=lambda path: path.name.encode("utf-16-be", errors="surrogatepass"),
    )
    if not sources:
        raise ValueError("Skill split scan found no authored .md files")
    manifest_path = root / ".llm/skill-splits.json"
    snapshots: Snapshots = {}
    manifest: dict[str, object]
    entries: list[Split]
    if manifest_path.exists():
        manifest, entries = load_manifest(manifest_path, snapshots)
    elif mode in {"--write", "--plan"}:
        entries = []
        manifest = {"version": 1, "skills": entries}
        snapshots[manifest_path] = None
    else:
        raise ValueError("Skill split manifest is missing")
    expected_manifest = snapshots[manifest_path]
    updates: list[Update] = []
    if mode == "--refresh":
        for entry in entries:
            source, canonical, chunks = chunks_for(entry, root, snapshots)
            prefix = prefix_of(canonical)
            router = router_text(prefix, entry["name"], chunks)
            references = [
                reference_text(entry["name"], index, chunk, root)
                for index, chunk in enumerate(chunks)
            ]
            if line_count(router) > MAX_LINES or any(
                line_count(text) > MAX_LINES for text in references
            ):
                raise ValueError(f"Split exceeds {MAX_LINES} lines after refresh: {entry['name']}")
            updates.append((source, entry, router, references, canonical))
            entry["sha256"], entry["routerSha256"] = (
                digest(prefix + "".join(chunks)),
                router_digest(router),
            )
        commit_updates(updates, manifest, root, expected_manifest, snapshots)
    elif mode in {"--write", "--plan"}:
        known = {entry["name"] for entry in entries}
        for source in sources:
            original = read(source, snapshots)
            name = source.name[:-3]
            if line_count(original) <= MAX_LINES or name in known:
                continue
            prefix = prefix_of(original)
            chunks = chunks_of(original[len(prefix) :])
            parts = [f"{name}-part-{index + 1}.md" for index in range(len(chunks))]
            for part in parts:
                destination = child(root / ".llm/references", part)
                if destination.exists():
                    raise ValueError(f"Refusing an existing unowned reference destination: {part}")
            router = router_text(prefix, name, chunks)
            if line_count(router) > MAX_LINES:
                raise ValueError(f"Router exceeds {MAX_LINES} lines: {name}")
            references = [
                reference_text(name, index, chunk, root) for index, chunk in enumerate(chunks)
            ]
            if any(line_count(text) > MAX_LINES for text in references):
                raise ValueError(f"Reference exceeds {MAX_LINES} lines: {name}")
            reconstructed = prefix + "".join(
                rewrite_links(reference_body(text, name), name, root, reverse=True)
                for text in references
            )
            if reconstructed != original:
                raise ValueError(f"Migration would change source content: {name}")
            entry = Split(
                name=name, sha256=digest(original), parts=parts, routerSha256=router_digest(router)
            )
            updates.append((source, entry, router, references, original))
            entries.append(entry)
        if mode == "--plan":
            if not updates:
                print("All skills are already below 200 lines.")
            else:
                largest = max(updates, key=lambda update: line_count(update[2]))
                print(
                    f"Ready to split {len(updates)} skills into {sum(len(update[1]['parts']) for update in updates)} references. Largest router: {largest[0].name} ({line_count(largest[2])} lines)."
                )
            return 0
        entries.sort(key=lambda entry: entry["name"].encode("utf-16-be", errors="surrogatepass"))
        commit_updates(
            updates, {"version": 1, "skills": entries}, root, expected_manifest, snapshots
        )
    for entry in entries:
        check_entry(entry, root)
    for source in sources:
        if line_count(read(source)) > MAX_LINES:
            raise ValueError(f"Skill exceeds {MAX_LINES} lines: {source.name}")
    print(f"Verified {len(entries)} reconstructed skill splits and all skill sizes.")
    return 0


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    arguments = list(sys.argv[1:] if arguments is None else arguments)
    root = ROOT
    if "--repo-root" in arguments:
        index = arguments.index("--repo-root")
        if index + 1 >= len(arguments):
            print("--repo-root requires a path", file=sys.stderr)
            return 2
        root = Path(arguments[index + 1]).resolve()
        del arguments[index : index + 2]
    if len(arguments) != 1 or arguments[0] not in {"--write", "--check", "--plan", "--refresh"}:
        print("Usage: migrate_agent_skills.py --plan|--write|--check|--refresh", file=sys.stderr)
        return 2
    try:
        return run(arguments[0], root)
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
