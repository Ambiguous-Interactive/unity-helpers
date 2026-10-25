#!/usr/bin/env python3
"""Back up, restore, and initialize private devcontainer agent state."""

import argparse
import hashlib
import io
import json
import os
import shutil
import sqlite3
import stat
import tarfile
import tempfile
from contextlib import closing
from datetime import UTC, datetime
from pathlib import Path, PurePosixPath

DIRECTORIES = (
    ".codex",
    ".claude",
    ".claude-zai",
    ".claude-openrouter",
    ".config/opencode",
    ".local/share/opencode",
    ".local/state/opencode",
    ".config/nanocoder",
    ".local/share/nanocoder",
    ".copilot",
    ".config/gh",
    ".mcp-auth",
)
MANIFEST = "agent-state-manifest.json"
MARKER = ".devcontainer-restored-sha256"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def copy_directory(source, destination):
    def ignore_special(directory, names):
        return [
            name
            for name in names
            if not (
                stat.S_ISREG((Path(directory) / name).lstat().st_mode)
                or stat.S_ISDIR((Path(directory) / name).lstat().st_mode)
                or stat.S_ISLNK((Path(directory) / name).lstat().st_mode)
            )
        ]

    shutil.copytree(source, destination, symlinks=True, ignore=ignore_special)
    for database in source.rglob("*"):
        if database.is_symlink() or not database.is_file():
            continue
        if database.suffix not in (".sqlite", ".sqlite3", ".db"):
            continue
        with database.open("rb") as stream:
            if stream.read(16) != b"SQLite format 3\x00":
                continue
        target = destination / database.relative_to(source)
        target.unlink()
        with (
            closing(
                sqlite3.connect(database.as_uri() + "?mode=ro", uri=True)
            ) as original,
            closing(sqlite3.connect(target)) as snapshot,
        ):
            original.backup(snapshot)
            if snapshot.execute("PRAGMA quick_check").fetchone()[0] != "ok":
                raise ValueError("SQLite snapshot failed integrity verification")
        for suffix in ("-wal", "-shm"):
            Path(str(target) + suffix).unlink(missing_ok=True)


def backup(home, workspace, archive):
    if archive.exists() or archive.with_suffix(archive.suffix + ".sha256").exists():
        raise ValueError("Backup already exists; choose a new archive path")
    archive.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    if any(archive.is_relative_to(home / relative) for relative in DIRECTORIES):
        raise ValueError("Backup must be outside the state directories")
    with tempfile.TemporaryDirectory(prefix="agent-state-") as directory:
        staging = Path(directory)
        for relative in DIRECTORIES:
            source = home / relative
            if source.is_dir():
                copy_directory(source, staging / relative)
        legacy = home / ".claude.json"
        relocated = staging / ".claude/.claude.json"
        if legacy.is_file():
            if relocated.exists() and legacy.read_bytes() != relocated.read_bytes():
                raise ValueError(
                    "Both Claude global configurations exist and differ; resolve before backup"
                )
            relocated.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(legacy, relocated)
        legacy_copilot = workspace / ".copilot"
        if (
            legacy_copilot.is_dir()
            and not (home / ".codex" / MARKER).is_file()
            and legacy_copilot.resolve() != (home / ".copilot").resolve()
        ):
            destination = staging / ".copilot"
            if (
                destination.exists()
                and any(destination.iterdir())
                and any(
                    path.name != "mcp-config.json" for path in legacy_copilot.iterdir()
                )
            ):
                raise ValueError(
                    "Both home and workspace Copilot state exist; resolve before backup"
                )
            if not destination.exists() or not any(destination.iterdir()):
                if destination.exists():
                    destination.rmdir()
                copy_directory(legacy_copilot, destination)
        files = {}
        links = {}
        for path in staging.rglob("*"):
            if path.is_symlink():
                links[path.relative_to(staging).as_posix()] = os.readlink(path)
                path.unlink()
            elif path.is_file():
                files[path.relative_to(staging).as_posix()] = digest(path)
        manifest = json.dumps(
            {
                "version": 1,
                "created": datetime.now(UTC).isoformat(),
                "files": files,
                "links": links,
            }
        ).encode()
        with tempfile.NamedTemporaryFile(dir=archive.parent, delete=False) as temporary:
            temporary_path = Path(temporary.name)
        try:
            with tarfile.open(temporary_path, "w:gz") as bundle:
                info = tarfile.TarInfo(MANIFEST)
                info.size = len(manifest)
                info.mode = 0o600
                bundle.addfile(info, io.BytesIO(manifest))
                for relative in DIRECTORIES:
                    source = staging / relative
                    if source.exists():
                        bundle.add(source, arcname=relative)
            os.link(temporary_path, archive)
        finally:
            temporary_path.unlink(missing_ok=True)
    checksum = archive.with_suffix(archive.suffix + ".sha256")
    with checksum.open("x") as stream:
        stream.write(digest(archive) + "\n")
    checksum.chmod(0o600)
    print(f"Backup saved: {archive} ({len(files)} verified files; archive mode 0600)")


def stage_restore(home, archive, staging, automatic):
    expected = archive.with_suffix(archive.suffix + ".sha256").read_text().strip()
    marker = home / ".codex" / MARKER
    if automatic and marker.is_file() and marker.read_text().strip() == expected:
        return None
    actual = digest(archive)
    if actual != expected:
        raise ValueError("Backup checksum mismatch; nothing restored")
    with tarfile.open(archive, "r:gz") as bundle:
        for member in bundle.getmembers():
            path = PurePosixPath(member.name)
            if path.is_absolute() or ".." in path.parts:
                raise ValueError("Unsafe archive path; nothing restored")
            if member.name != MANIFEST and not any(
                member.name == root or member.name.startswith(root + "/")
                for root in DIRECTORIES
            ):
                raise ValueError("Unexpected archive entry; nothing restored")
        for member in bundle.getmembers():
            destination = staging / member.name
            if member.isdir():
                destination.mkdir(mode=0o700, parents=True, exist_ok=True)
            elif member.isfile():
                destination.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
                with (
                    bundle.extractfile(member) as source,
                    destination.open("xb") as target,
                ):
                    shutil.copyfileobj(source, target)
                destination.chmod(member.mode & 0o700)
            else:
                raise ValueError("Unsupported archive entry type; nothing restored")
    manifest = json.loads((staging / MANIFEST).read_text())
    if not isinstance(manifest, dict) or manifest.get("version") != 1:
        raise ValueError("Unsupported backup version; nothing restored")
    if not isinstance(manifest.get("files"), dict) or not isinstance(
        manifest.get("links", {}), dict
    ):
        raise ValueError("Invalid backup manifest; nothing restored")
    staged_files = {
        path.relative_to(staging).as_posix()
        for path in staging.rglob("*")
        if path.is_file() and path != staging / MANIFEST
    }
    if set(manifest["files"]) != staged_files:
        raise ValueError("Backup file inventory mismatch; nothing restored")
    for relative, expected_hash in manifest["files"].items():
        path = staging / relative
        if (
            not path.resolve().is_relative_to(staging)
            or not path.is_file()
            or path.is_symlink()
            or digest(path) != expected_hash
        ):
            raise ValueError("Backup file verification failed; nothing restored")
    for relative, link in manifest.get("links", {}).items():
        if (
            not isinstance(relative, str)
            or not isinstance(link, str)
            or not link
            or "\0" in link
        ):
            raise ValueError("Invalid backup link; nothing restored")
        path = PurePosixPath(relative)
        if (
            path.is_absolute()
            or ".." in path.parts
            or not any(relative.startswith(root + "/") for root in DIRECTORIES)
        ):
            raise ValueError("Unsafe link path; nothing restored")
        if (staging / relative).exists() or any(
            str(parent) in manifest.get("links", {}) for parent in path.parents
        ):
            raise ValueError("Conflicting link paths; nothing restored")
    for relative in DIRECTORIES:
        target = home / relative
        if not target.resolve().is_relative_to(home):
            raise ValueError("Restore destination escapes home; nothing restored")
        includes_links = any(
            link.startswith(relative + "/") for link in manifest.get("links", {})
        )
        if ((staging / relative).exists() or includes_links) and (
            target.is_symlink()
            or (target.exists() and (not target.is_dir() or any(target.iterdir())))
        ):
            raise ValueError(
                f"Restore destination is not empty: {target}; nothing restored"
            )
    return manifest, actual


def restore(home, archive, automatic=False):
    with tempfile.TemporaryDirectory(prefix="agent-restore-") as directory:
        staging = Path(directory)
        try:
            staged = stage_restore(home, archive, staging, automatic)
        except (
            OSError,
            ValueError,
            KeyError,
            TypeError,
            tarfile.TarError,
            sqlite3.Error,
        ) as error:
            if not automatic:
                raise
            print(
                f"WARNING: Automatic agent state restore skipped: {error}. "
                "Backup retained; continuing setup. Use explicit restore after resolving the reported issue."
            )
            return
        if staged is None:
            return
        manifest, actual = staged
        marker = home / ".codex" / MARKER
        for relative in DIRECTORIES:
            source = staging / relative
            if source.exists():
                destination = home / relative
                destination.mkdir(mode=0o700, parents=True, exist_ok=True)
                shutil.copytree(source, destination, dirs_exist_ok=True, symlinks=True)
        for relative, link in manifest.get("links", {}).items():
            destination = home / relative
            if (
                not destination.parent.resolve().is_relative_to(home)
                or destination.exists()
                or destination.is_symlink()
            ):
                raise ValueError("Unsafe or conflicting restored link path")
            destination.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
            destination.symlink_to(link)
        marker.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        marker.write_text(actual + "\n")
        marker.chmod(0o600)
    print("Agent state restored and checksums verified; backup retained.")


def prepare(home, workspace):
    archive = workspace / "progress/devcontainer-state/migration.tar.gz"
    if archive.is_file():
        restore(home, archive, automatic=True)
    for relative in DIRECTORIES:
        directory = home / relative
        directory.mkdir(mode=0o700, parents=True, exist_ok=True)
        directory.chmod(0o700)
    config = home / ".codex/config.toml"
    if not config.exists():
        with config.open("x") as stream:
            stream.write('cli_auth_credentials_store = "file"\n')
        config.chmod(0o600)
    else:
        import tomllib

        settings = tomllib.loads(config.read_text())
        mode = settings.get("cli_auth_credentials_store", "auto")
        if mode not in ("file", "auto"):
            print(
                f"WARNING: Codex credential storage is {mode}; directory persistence does not preserve an OS keyring or ephemeral credentials."
            )
    link_mcp(home, workspace)


def link_mcp(home, workspace):
    (home / ".copilot").mkdir(mode=0o700, parents=True, exist_ok=True)
    generated = workspace / ".copilot/mcp-config.json"
    target = home / ".copilot/mcp-config.json"
    if generated.is_file():
        if target.is_symlink() and target.resolve() == generated.resolve():
            return
        if target.exists() or target.is_symlink():
            if target.is_file() and target.read_bytes() == generated.read_bytes():
                target.unlink()
            else:
                print(
                    "WARNING: Keeping personal Copilot MCP config; workspace config differs."
                )
                return
        target.symlink_to(generated)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("backup", "restore", "prepare", "link-mcp"))
    parser.add_argument("archive", nargs="?", type=Path)
    parser.add_argument("--home", type=Path, default=Path.home())
    parser.add_argument(
        "--workspace", type=Path, default=Path(__file__).resolve().parent.parent
    )
    args = parser.parse_args()
    os.umask(0o077)
    home, workspace = args.home.resolve(), args.workspace.resolve()
    archive = (
        args.archive or workspace / "progress/devcontainer-state/migration.tar.gz"
    ).resolve()
    try:
        if args.command == "backup":
            backup(home, workspace, archive)
        elif args.command == "restore":
            restore(home, archive)
        elif args.command == "link-mcp":
            link_mcp(home, workspace)
        else:
            prepare(home, workspace)
    except (
        OSError,
        ValueError,
        KeyError,
        TypeError,
        tarfile.TarError,
        sqlite3.Error,
    ) as error:
        parser.exit(1, f"agent-state: {error}\n")


if __name__ == "__main__":
    main()
