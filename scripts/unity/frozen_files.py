"""Inventory exact WProto source bytes without following filesystem redirects."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import uuid
from pathlib import Path
from typing import TypedDict, cast

SOURCE_ROOTS = ("Runtime", "Editor", "Tests", "Generator~", "scripts/unity")
GENERATOR_SUFFIXES = frozenset(
    (".cs", ".csproj", ".props", ".targets", ".rsp", ".json", ".xml", ".dll", ".meta")
)
EXPLICIT_SOURCES = (
    "package.json",
    ".github/unity-versions.json",
    ".github/workflows/unity-tests.yml",
    "scripts/unity/run-ci-tests.ps1",
    "scripts/unity/run-acceptance.ps1",
    "scripts/unity/verify-acceptance.ps1",
    "scripts/unity/verify-wproto-corpus.js",
    "scripts/unity/wproto-corpus.v1.json",
    "scripts/unity/lib/wproto-player-provenance.ps1",
    "scripts/unity/lib/wproto-player-provenance.ps1.meta",
    "scripts/python~/pyproject.toml",
    "scripts/python~/uv.lock",
)
HOST_ROOTS = ("Runtime", "Editor", "Tests", "Generator~")
HOST_GIT_INPUTS = (
    ".editorconfig",
    ".gitignore",
    ".gitattributes",
    "global.json",
    "NuGet.Config",
    "nuget.config",
    "Directory.Build.props",
    "Directory.Build.targets",
    ".github/workflows/wproto-generator.yml",
    "scripts/verify-wproto-evidence.ps1",
    "scripts/run-wproto-evidence.ps1",
    "scripts/unity/lib/test-result-evidence.ps1",
    "scripts/unity/lib/catastrophic-patterns.ps1",
)
HOST_TOOL_INPUTS = (
    "scripts/unity/lib/source-census-python.ps1",
    "scripts/unity/frozen_files.py",
    "scripts/python~/pyproject.toml",
    "scripts/python~/uv.lock",
)


class SourceFile(TypedDict):
    Path: str
    Sha256: str


class HostSourceFile(TypedDict):
    path: str
    sha256: str


def ordinal_key(value: str) -> bytes:
    return value.encode("utf-16-be", errors="surrogatepass")


def require_ordinary(path: Path, info: os.stat_result, *, directory: bool) -> os.stat_result:
    if stat.S_ISLNK(info.st_mode) or (
        getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
    ):
        raise ValueError(f"Frozen path has a reparse point: {path}")
    expected = stat.S_ISDIR if directory else stat.S_ISREG
    if not expected(info.st_mode):
        raise ValueError(f"Frozen path has the wrong file/directory kind: {path}")
    return info


def ordinary(path: Path, *, directory: bool) -> os.stat_result:
    return require_ordinary(path, path.lstat(), directory=directory)


def regular_path(path: Path, *, directory: bool) -> Path:
    absolute = Path(os.path.abspath(path))  # noqa: PTH100 - resolving would erase reparse ancestors.
    for current in (absolute, *absolute.parents):
        info = current.lstat()
        if stat.S_ISLNK(info.st_mode) or (
            getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
        ):
            raise ValueError(f"Frozen path has a reparse ancestor: {current}")
        ordinary(current, directory=directory if current == absolute else True)
    return absolute


def content_identity(info: os.stat_result) -> tuple[int, int, int, int, int]:
    return (info.st_dev, info.st_ino, info.st_mode, info.st_size, info.st_mtime_ns)


def identity(info: os.stat_result) -> tuple[int, int, int, int, int, int]:
    return (*content_identity(info), info.st_ctime_ns)


def file_hash(path: Path, observed: os.stat_result | None = None) -> tuple[str, int]:
    before = ordinary(path, directory=False) if observed is None else observed
    with path.open("rb") as stream:
        opened = os.fstat(stream.fileno())
        # Windows Python 3.11 stat and fstat expose different meanings for ctime.
        if content_identity(before) != content_identity(opened):
            raise ValueError(f"Frozen file changed before hashing: {path}")
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
        after_read = os.fstat(stream.fileno())
    after = ordinary(path, directory=False)
    if identity(opened) != identity(after_read) or identity(before) != identity(after):
        raise ValueError(f"Frozen file changed while hashing: {path}")
    return digest, after.st_size


def walk(
    root: Path, *, generator: bool = False, host: bool = False
) -> list[tuple[Path, bool, os.stat_result]]:
    pending = [root]
    entries: list[tuple[Path, bool, os.stat_result]] = []
    while pending:
        directory = pending.pop()
        ordinary(directory, directory=True)
        with os.scandir(directory) as children:
            for child in children:
                path = Path(child.path)
                info = path.lstat()
                is_directory = stat.S_ISDIR(info.st_mode)
                require_ordinary(path, info, directory=is_directory)
                if is_directory:
                    if generator and child.name.lower() in ("bin", "obj", ".git"):
                        continue
                    if host and child.name in ("bin", "obj", ".git"):
                        continue
                    pending.append(path)
                elif generator and path.suffix.lower() not in GENERATOR_SUFFIXES:
                    continue
                entries.append((path, is_directory, info))
                if len(entries) > 100_000:
                    raise ValueError("Frozen payload exceeds 100000 entries.")
    return entries


def source_files(repository: Path) -> list[SourceFile]:
    root = regular_path(repository, directory=True)
    paths: dict[str, os.stat_result | None] = {}
    for directory in SOURCE_ROOTS:
        source_root = regular_path(root / directory, directory=True)
        for path, is_directory, info in walk(source_root, generator=directory == "Generator~"):
            if is_directory:
                continue
            paths[path.relative_to(root).as_posix()] = info
            if len(paths) > 16_384:
                raise ValueError("WProto source census exceeds 16384 files.")
    for relative in EXPLICIT_SOURCES:
        regular_path(root / relative, directory=False)
        paths.setdefault(relative, None)
    with os.scandir(root) as children:
        for child in children:
            path = Path(child.path)
            if path.suffix.lower() in (".rsp", ".props", ".targets", ".csproj"):
                info = path.lstat()
                if stat.S_ISDIR(info.st_mode):
                    continue
                require_ordinary(path, info, directory=False)
                paths[child.name] = info
    if len(paths) > 16_384:
        raise ValueError("WProto source census exceeds 16384 files.")
    return [
        SourceFile(Path=relative, Sha256=file_hash(root / relative, paths[relative])[0])
        for relative in sorted(paths, key=ordinal_key)
    ]


def git_output(root: Path, arguments: list[str]) -> bytes:
    executable = shutil.which("git")
    if executable is None:
        raise ValueError("Git is required for the host source census.")
    result = subprocess.run(  # noqa: S603 - resolved executable and explicit argument vector.
        [executable, "-C", str(root), *arguments],
        capture_output=True,
        check=False,
        timeout=30,
    )
    if result.returncode != 0:
        raise ValueError("Cannot enumerate WProto build inputs.")
    return result.stdout


def host_source_files(repository: Path) -> list[HostSourceFile]:
    root = regular_path(repository, directory=True)
    actual_root = git_output(root, ["rev-parse", "--show-toplevel"]).decode().strip()
    if Path(actual_root) != root:
        raise ValueError("Host source census requires the Git repository root.")
    listing = git_output(
        root,
        ["ls-files", "--cached", "--others", "--exclude-standard", "-z", "--", *HOST_GIT_INPUTS],
    )
    paths = {
        path.decode("utf-8", errors="surrogateescape") for path in listing.split(b"\0") if path
    }
    observed: dict[str, os.stat_result] = {}
    for directory in HOST_ROOTS:
        source_root = regular_path(root / directory, directory=True)
        for path, is_directory, info in walk(source_root, host=True):
            if not is_directory:
                relative = path.relative_to(root).as_posix()
                paths.add(relative)
                observed[relative] = info
    paths.update(HOST_TOOL_INPUTS)
    if "Runtime/Core/Serialization/WallstopProto/WProtoReader.cs" not in paths:
        raise ValueError("Missing WProto source inputs.")
    if len(paths) > 16_384:
        raise ValueError("WProto source census exceeds 16384 files.")
    for relative in paths.intersection(HOST_GIT_INPUTS) | set(HOST_TOOL_INPUTS):
        regular_path(root / relative, directory=False)
    return [
        HostSourceFile(path=relative, sha256=file_hash(root / relative, observed.get(relative))[0])
        for relative in sorted(paths, key=ordinal_key)
    ]


def execute_request(value: object) -> object:
    if not isinstance(value, dict):
        raise ValueError("Frozen inventory requests must be objects.")
    request = cast("dict[str, object]", value)
    if set(request) - {"protocol", "requestId", "operation", "root"}:
        raise ValueError("Source census request has unknown fields.")
    protocol = request.get("protocol")
    identifier = request.get("requestId")
    operation = request.get("operation")
    root = request.get("root")
    if (
        type(protocol) is not int
        or protocol != 1
        or not isinstance(identifier, str)
        or not re.fullmatch(r"[a-f0-9]{32}", identifier)
        or operation not in ("sources", "host-sources")
        or not isinstance(root, str)
        or not root.strip()
    ):
        raise ValueError("Frozen inventory request has invalid fields.")
    if operation == "sources":
        return source_files(Path(root))
    return host_source_files(Path(root))


def request_identifier(value: object) -> object:
    if not isinstance(value, dict):
        return None
    return cast("dict[str, object]", value).get("requestId")


def serve() -> int:
    implementation = Path(__file__)
    loaded_hash = file_hash(implementation)[0]
    session = uuid.uuid4().hex
    while True:
        line = sys.stdin.readline(8_388_609)
        if not line:
            return 0
        response: dict[str, object] = {
            "protocol": 1,
            "ok": False,
            "processId": os.getpid(),
            "sessionId": session,
            "toolSha256": loaded_hash,
        }
        try:
            if len(line) > 8_388_608 or not line.endswith("\n"):
                raise ValueError("Frozen inventory request exceeds its bound or is incomplete.")
            request: object = json.loads(line)
            response["requestId"] = request_identifier(request)
            if file_hash(implementation)[0] != loaded_hash:
                raise ValueError("Frozen inventory implementation changed; start a fresh worker.")
            result = execute_request(request)
            if file_hash(implementation)[0] != loaded_hash:
                raise ValueError("Frozen inventory implementation changed during the request.")
            response.update(ok=True, value=result)
        except (OSError, ValueError, subprocess.TimeoutExpired) as error:
            response["error"] = str(error)
        print(json.dumps(response, ensure_ascii=True), flush=True)
        if len(line) > 8_388_608 or not line.endswith("\n"):
            return 1


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repository", type=Path, nargs="?")
    parser.add_argument("--host", action="store_true")
    parser.add_argument("--serve", action="store_true")
    options = parser.parse_args(arguments)
    if options.serve:
        return serve()
    if options.repository is None:
        parser.error("A repository root is required.")
    try:
        result = (
            host_source_files(options.repository)
            if options.host
            else source_files(options.repository)
        )
        print(json.dumps(result, ensure_ascii=True))
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        print(f"WProto source census failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
