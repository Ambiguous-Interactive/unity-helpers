"""Inventory WProto sources and frozen player inputs without following filesystem redirects."""

from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import uuid
from contextlib import ExitStack, contextmanager
from functools import cache
from pathlib import Path
from typing import TYPE_CHECKING, TypedDict, cast

if TYPE_CHECKING:
    import io
    from collections.abc import Callable, Generator

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
    "scripts/unity/lib/catastrophic-patterns.ps1",
)
HOST_TOOL_INPUTS = (
    "scripts/unity/lib/test-result-evidence.ps1",
    "scripts/unity/test_result_evidence.py",
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


def content_identity(
    info: os.stat_result, *, comparing_descriptor: bool = False
) -> tuple[int, int, int, int, int]:
    # Windows path stat infers execute bits from the extension; fstat has no filename.
    mode = (
        info.st_mode & ~0o111 if comparing_descriptor and sys.platform == "win32" else info.st_mode
    )
    return (info.st_dev, info.st_ino, mode, info.st_size, info.st_mtime_ns)


def identity(info: os.stat_result) -> tuple[int, int, int, int, int, int]:
    return (*content_identity(info), info.st_ctime_ns)


if sys.platform == "win32":

    @cache
    def windows_kernel32() -> ctypes.WinDLL:
        from ctypes import wintypes

        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateFileW.argtypes = (
            wintypes.LPCWSTR,
            wintypes.DWORD,
            wintypes.DWORD,
            wintypes.LPVOID,
            wintypes.DWORD,
            wintypes.DWORD,
            wintypes.HANDLE,
        )
        kernel.CreateFileW.restype = wintypes.HANDLE
        kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
        kernel.CloseHandle.restype = wintypes.BOOL
        kernel.FindFirstChangeNotificationW.argtypes = (
            wintypes.LPCWSTR,
            wintypes.BOOL,
            wintypes.DWORD,
        )
        kernel.FindFirstChangeNotificationW.restype = wintypes.HANDLE
        kernel.FindCloseChangeNotification.argtypes = (wintypes.HANDLE,)
        kernel.FindCloseChangeNotification.restype = wintypes.BOOL
        kernel.WaitForSingleObject.argtypes = (wintypes.HANDLE, wintypes.DWORD)
        kernel.WaitForSingleObject.restype = wintypes.DWORD
        return kernel

    def _open_windows_handle(path: Path) -> int:
        kernel = windows_kernel32()
        generic_read, share_read, open_existing, ordinary_attributes = (
            0x80000000,
            1,
            3,
            0x80,
        )
        handle: int | None = kernel.CreateFileW(
            str(path),
            generic_read,
            share_read,
            None,
            open_existing,
            ordinary_attributes,
            None,
        )
        if handle is None or handle == ctypes.c_void_p(-1).value:
            raise ctypes.WinError(ctypes.get_last_error())
        return handle

    def open_source(path: Path) -> io.BufferedReader[io.FileIO]:
        import msvcrt

        kernel = windows_kernel32()
        handle = _open_windows_handle(path)
        try:
            descriptor = msvcrt.open_osfhandle(handle, os.O_RDONLY | os.O_BINARY)
        except BaseException:
            kernel.CloseHandle(handle)
            raise
        try:
            return cast("io.BufferedReader[io.FileIO]", os.fdopen(descriptor, "rb"))
        except BaseException:
            os.close(descriptor)
            raise

else:

    def open_source(path: Path) -> io.BufferedReader[io.FileIO]:
        return cast("io.BufferedReader[io.FileIO]", path.open("rb"))


@contextmanager
def retain_source(path: Path) -> Generator[None, None, None]:
    if sys.platform == "win32":
        handle = _open_windows_handle(path)
        try:
            yield
        finally:
            windows_kernel32().CloseHandle(handle)
    else:
        yield


def file_hash(path: Path, observed: os.stat_result | None = None) -> tuple[str, int]:
    before = ordinary(path, directory=False) if observed is None else observed
    with open_source(path) as stream:
        opened = os.fstat(stream.fileno())
        # Windows Python 3.11 stat and fstat expose different meanings for ctime.
        if content_identity(before, comparing_descriptor=True) != content_identity(
            opened, comparing_descriptor=True
        ):
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
    actual_root = os.fsdecode(
        git_output(root, ["rev-parse", "--show-toplevel"]).removesuffix(b"\n")
    )
    if Path(actual_root) != root:
        raise ValueError("Host source census requires the Git repository root.")
    listing = git_output(
        root,
        [
            "ls-files",
            "--cached",
            "--others",
            "--exclude-standard",
            "-z",
            "--",
            *HOST_GIT_INPUTS,
        ],
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


class InventoryRow(TypedDict):
    Path: str
    Kind: str
    Length: int | None
    Sha256: str | None


class InventoryResult(TypedDict):
    rows: list[InventoryRow]
    error: str | None


def _inventory_checkout(repository: Path) -> tuple[Path, bytes]:
    root = regular_path(repository, directory=True)
    tool = regular_path(root / "scripts/unity/frozen_files.py", directory=False)
    actual_root = Path(
        os.fsdecode(git_output(root, ["rev-parse", "--show-toplevel"]).rstrip(b"\r\n"))
    )
    if actual_root != root or Path(os.path.abspath(__file__)) != tool:  # noqa: PTH100
        raise ValueError("Frozen inventory tool does not belong to the requested checkout.")
    revision = git_output(root, ["rev-parse", "--verify", "HEAD"]).strip()
    if not re.fullmatch(rb"[a-f0-9]{40}", revision):
        raise ValueError("Frozen inventory checkout has no valid commit identity.")
    return root, revision


def _declared_hashes(declaration: object, maximum: int) -> dict[str, str]:
    if not isinstance(declaration, list):
        raise ValueError("Frozen input lists must be arrays.")
    declarations = cast("list[object]", declaration)
    if not 1 <= len(declarations) <= maximum:
        raise ValueError(f"Frozen input lists require between 1 and {maximum} files.")
    result: dict[str, str] = {}
    for row in declarations:
        if not isinstance(row, dict):
            raise ValueError("Frozen input paths and hashes must be scalar strings.")
        entry = cast("dict[str, object]", row)
        relative, digest = entry.get("Path"), entry.get("Sha256")
        if not isinstance(relative, str) or not isinstance(digest, str):
            raise ValueError("Frozen input paths and hashes must be scalar strings.")
        if (
            not relative.strip()
            or len(ordinal_key(relative)) > 2048
            or re.search(r'[\\:<>"|?*\x00-\x1f]', relative)
            or any(
                part in ("", ".", "..") or part.endswith((".", " ")) for part in relative.split("/")
            )
            or not re.fullmatch(r"[a-f0-9]{64}", digest)
            or relative in result
        ):
            raise ValueError(f"Invalid or colliding frozen input path/hash: {relative}")
        result[relative] = digest
    return result


def _payload_states(root: Path) -> dict[str, tuple[bool, os.stat_result]]:
    states = {
        path.relative_to(root).as_posix(): (directory, info) for path, directory, info in walk(root)
    }
    if any("\\" in name for name in states):
        raise ValueError("Frozen payload contains a backslash in a relative path.")
    if not any(not directory for directory, _ in states.values()):
        raise ValueError("Frozen payload contains no files.")
    return states


def frozen_player_inventory(root_path: Path, declaration: object, maximum: int) -> InventoryResult:
    root = regular_path(root_path, directory=True)
    before_root = ordinary(root, directory=True)
    declared = None if declaration is None else _declared_hashes(declaration, maximum)
    payload = _payload_states(root) if declared is None else {}
    rows: list[InventoryRow] = []
    observed: dict[Path, os.stat_result] = {}
    problem: str | None = None
    with ExitStack() as retained:
        for relative in sorted(payload if declared is None else declared, key=ordinal_key):
            path = root / relative
            try:
                if declared is None:
                    directory, info = payload[relative]
                else:
                    info = path.lstat()
                    if stat.S_ISDIR(info.st_mode):
                        raise ValueError(f"Frozen input is not a file: {relative}")
                    regular_path(path, directory=False)
                    directory = False
                if directory:
                    rows.append(
                        InventoryRow(Path=relative, Kind="directory", Length=None, Sha256=None)
                    )
                    continue
                if sys.platform == "win32":
                    retained.enter_context(retain_source(path))
                digest, size = file_hash(path, info)
                observed[path] = info
                rows.append(InventoryRow(Path=relative, Kind="file", Length=size, Sha256=digest))
                if declared is not None and declared[relative] != digest:
                    raise ValueError(f"Frozen declared file hash does not match: {relative}")
            except (OSError, ValueError) as error:
                problem = str(error)
                break
        regular_path(root, directory=True)
        after_root = ordinary(root, directory=True)
        if declared is None:
            current = _payload_states(root)
            if (
                current.keys() != payload.keys()
                or identity(before_root) != identity(after_root)
                or any(
                    identity(state) != identity(current[name][1])
                    for name, (_, state) in payload.items()
                )
            ):
                raise ValueError("Frozen player payload changed during inventory.")
        elif (before_root.st_dev, before_root.st_ino) != (
            after_root.st_dev,
            after_root.st_ino,
        ):
            raise ValueError("Frozen player inventory root changed.")
        for path, info in observed.items():
            if declared is not None:
                regular_path(path, directory=False)
            if identity(info) != identity(ordinary(path, directory=False)):
                raise ValueError(f"Frozen file changed during inventory: {path.relative_to(root)}")
    return InventoryResult(rows=rows, error=problem)


class RecordedInventoryError(ValueError):
    """A rejection of the recorded inventory, reported through the original entry point."""


def _assert_recorded_root(root: Path, root_before: os.stat_result, *, complete: bool) -> None:
    root_after = ordinary(root, directory=True)
    if complete and identity(root_before) != identity(root_after):
        raise RecordedInventoryError("WProto complete inventory membership changed.")
    if (root_before.st_dev, root_before.st_ino) != (root_after.st_dev, root_after.st_ino):
        raise RecordedInventoryError("WProto recorded inventory root changed.")


@contextmanager
def observe_windows_membership(
    root: Path, root_before: os.stat_result, *, complete: bool
) -> Generator[None, None, None]:
    if sys.platform == "win32":
        if complete:
            kernel = windows_kernel32()
            file_name_change, directory_name_change = 0x00000001, 0x00000002
            handle: int | None = kernel.FindFirstChangeNotificationW(
                str(root), True, file_name_change | directory_name_change
            )
            if handle is None or handle == ctypes.c_void_p(-1).value:
                raise ctypes.WinError(ctypes.get_last_error())
            try:
                yield
                # The subtree notification excludes the root itself.
                _assert_recorded_root(root, root_before, complete=True)
                wait_object, wait_timeout = 0x00000000, 0x00000102
                status: int = kernel.WaitForSingleObject(handle, 0)
                if status == wait_object:
                    raise RecordedInventoryError("WProto complete inventory membership changed.")
                if status != wait_timeout:
                    raise ctypes.WinError(ctypes.get_last_error())
            finally:
                kernel.FindCloseChangeNotification(handle)
            return
    yield


def _recorded_path(root: Path, relative: str) -> Path:
    if re.search(r"^/|\\|:|(?:^|/)\.\.?(?:/|$)", relative):
        raise RecordedInventoryError(
            "WProto recorded inventory contains an invalid or repeated relative path."
        )
    path = Path(os.path.abspath(root / relative))  # noqa: PTH100
    if not path.is_relative_to(root) or path == root:
        raise RecordedInventoryError("WProto recorded path leaves its root.")
    return path


def recorded_stat(root: Path, path: Path) -> os.stat_result:
    try:
        info = path.lstat()
        for current in (path, *path.parents):
            ancestor = info if current == path else current.lstat()
            if stat.S_ISLNK(ancestor.st_mode) or (
                getattr(ancestor, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
            ):
                raise RecordedInventoryError("WProto recorded path has a reparse ancestor.")
            if current == root:
                break
    except FileNotFoundError as error:
        raise RecordedInventoryError(
            f"Cannot find path '{path}' because it does not exist."
        ) from error
    return info


if sys.platform == "linux":
    INOTIFY_OPEN_FLAGS = os.O_CLOEXEC | os.O_NONBLOCK
else:
    INOTIFY_OPEN_FLAGS = 0


class LinuxRecordedInventoryWatch:
    _IN_MODIFY = 0x00000002
    _IN_ATTRIB = 0x00000004
    _IN_MOVED_FROM = 0x00000040
    _IN_MOVED_TO = 0x00000080
    _IN_CREATE = 0x00000100
    _IN_DELETE = 0x00000200
    _IN_DELETE_SELF = 0x00000400
    _IN_MOVE_SELF = 0x00000800
    _IN_DONT_FOLLOW = 0x02000000

    def __init__(self, cleanup: ExitStack) -> None:
        libc = ctypes.CDLL(None, use_errno=True)
        libc.inotify_init1.argtypes = (ctypes.c_int,)
        libc.inotify_init1.restype = ctypes.c_int
        libc.inotify_add_watch.argtypes = (ctypes.c_int, ctypes.c_char_p, ctypes.c_uint32)
        libc.inotify_add_watch.restype = ctypes.c_int
        self._add_watch = libc.inotify_add_watch
        self._descriptor: int = libc.inotify_init1(INOTIFY_OPEN_FLAGS)
        if self._descriptor < 0:
            raise OSError(ctypes.get_errno(), "Cannot observe recorded inventory changes.")
        cleanup.callback(os.close, self._descriptor)
        self._watched: set[Path] = set()

    def add(self, path: Path, *, directory: bool, complete: bool) -> None:
        if path in self._watched:
            return
        mask = self._IN_DELETE_SELF | self._IN_MOVE_SELF | self._IN_DONT_FOLLOW
        if directory:
            if complete:
                mask |= self._IN_MOVED_FROM | self._IN_MOVED_TO | self._IN_CREATE | self._IN_DELETE
        else:
            mask |= self._IN_MODIFY | self._IN_ATTRIB
        if self._add_watch(self._descriptor, os.fsencode(path), mask) < 0:
            raise OSError(ctypes.get_errno(), "Cannot observe recorded inventory path.", path)
        self._watched.add(path)

    def verify(self) -> None:
        try:
            events = os.read(self._descriptor, 4096)
        except BlockingIOError:
            return
        if events:
            raise RecordedInventoryError("WProto recorded file changed during verification.")


def _assert_recorded_directory(
    before: os.stat_result, current: os.stat_result, *, complete: bool
) -> None:
    if complete and identity(current) != identity(before):
        raise RecordedInventoryError("WProto complete inventory membership changed.")
    if (before.st_dev, before.st_ino, before.st_mode) != (
        current.st_dev,
        current.st_ino,
        current.st_mode,
    ):
        raise RecordedInventoryError("WProto recorded directory changed kind.")


def _assert_recorded_state(
    root: Path,
    root_before: os.stat_result,
    recorded_names: set[str],
    observed: dict[Path, tuple[bool, os.stat_result]],
    *,
    complete: bool,
) -> None:
    if complete:
        try:
            actual_names = {path.relative_to(root).as_posix() for path, _, _ in walk(root)}
        except (OSError, ValueError) as error:
            raise RecordedInventoryError("WProto complete inventory membership changed.") from error
        if actual_names != recorded_names:
            raise RecordedInventoryError("WProto complete inventory membership changed.")
    regular_path(root, directory=True)
    for path, (directory, before) in observed.items():
        current = recorded_stat(root, path)
        if directory:
            _assert_recorded_directory(before, current, complete=complete)
        elif identity(current) != identity(before):
            raise RecordedInventoryError("WProto recorded file changed.")
    _assert_recorded_root(root, root_before, complete=complete)


def verify_recorded_inventory(
    root_path: Path,
    records: object,
    *,
    complete: bool,
    check_inputs: Callable[[], None] | None = None,
    records_path: Path | None = None,
) -> None:
    root = regular_path(root_path, directory=True)
    root_before = ordinary(root, directory=True)
    portable_recheck = (
        check_inputs is not None and sys.platform != "win32" and not INOTIFY_OPEN_FLAGS
    )
    receipt_before = (
        ordinary(records_path, directory=False)
        if portable_recheck and records_path is not None
        else None
    )
    if not isinstance(records, list):
        raise RecordedInventoryError("WProto recorded inventory is empty.")
    recorded_names: set[str] = set()
    observed: dict[Path, tuple[bool, os.stat_result]] = {}
    with ExitStack() as retained, observe_windows_membership(root, root_before, complete=complete):
        watcher = LinuxRecordedInventoryWatch(retained) if INOTIFY_OPEN_FLAGS else None
        if watcher is not None:
            watcher.add(root, directory=True, complete=complete)
            if records_path is not None:
                watcher.add(records_path, directory=False, complete=False)
        for row in cast("list[object]", records):
            record = cast("dict[str, object]", row) if isinstance(row, dict) else {}
            relative = record.get("Path")
            if not isinstance(relative, str):
                raise RecordedInventoryError(
                    "WProto recorded inventory contains an invalid or repeated relative path."
                )
            path = _recorded_path(root, relative)
            if path in observed:
                raise RecordedInventoryError(
                    "WProto recorded inventory contains an invalid or repeated relative path."
                )
            recorded_names.add(relative)
            info = recorded_stat(root, path)
            directory = record.get("Kind") == "directory"
            if watcher is not None:
                for parent in path.parents:
                    watcher.add(parent, directory=True, complete=complete)
                    if parent == root:
                        break
                watcher.add(path, directory=directory, complete=complete)
            if directory:
                if not stat.S_ISDIR(info.st_mode):
                    raise RecordedInventoryError("WProto recorded directory changed kind.")
            elif record.get("Kind") == "file":
                digest = record.get("Sha256")
                if (
                    not stat.S_ISREG(info.st_mode)
                    or not isinstance(digest, str)
                    or not re.fullmatch(r"[a-f0-9]{64}", digest)
                    or info.st_size != record.get("Length")
                ):
                    raise RecordedInventoryError("WProto recorded file changed.")
                try:
                    if sys.platform == "win32":
                        retained.enter_context(retain_source(path))
                    actual, _ = file_hash(path, info)
                except (OSError, ValueError) as error:
                    raise RecordedInventoryError("WProto recorded file changed.") from error
                if actual != digest:
                    raise RecordedInventoryError("WProto recorded file changed.")
            else:
                raise RecordedInventoryError("WProto recorded inventory kind is invalid.")
            observed[path] = (directory, info)

        if not recorded_names:
            raise RecordedInventoryError("WProto recorded inventory is empty.")
        _assert_recorded_state(root, root_before, recorded_names, observed, complete=complete)
        if check_inputs is not None:
            check_inputs()
            if sys.platform == "win32" and not complete:
                # A subset has no membership watch. Recheck its recorded directories
                # by identity so unrelated entries may still be added or removed.
                _assert_recorded_root(root, root_before, complete=False)
                for path, (directory, before) in observed.items():
                    if directory:
                        _assert_recorded_directory(
                            before, recorded_stat(root, path), complete=False
                        )
        if portable_recheck:
            # Compare the same identities on both sides of the receipt and checkout checks.
            _assert_recorded_state(root, root_before, recorded_names, observed, complete=complete)
            if (
                records_path is not None
                and receipt_before is not None
                and identity(ordinary(records_path, directory=False)) != identity(receipt_before)
            ):
                raise RecordedInventoryError("WProto recorded receipt changed during verification.")
        if watcher is not None:
            # Local filesystem notifications cover earlier paths after their final stat.
            watcher.verify()


def _owned_records_path(path: Path) -> Path:
    # The OS temporary directory can have an alias, but the receipt file must be ordinary.
    return regular_path(path.parent.resolve(strict=True) / path.name, directory=False)


def _verify_recorded_request(
    root: Path, request: dict[str, object], check_checkout: Callable[[], None]
) -> str | None:
    path, digest, complete = (
        request.get("recordsPath"),
        request.get("recordsSha256"),
        request.get("complete"),
    )
    if (
        not isinstance(path, str)
        or not Path(path).is_absolute()
        or not isinstance(digest, str)
        or not re.fullmatch(r"[a-f0-9]{64}", digest)
        or type(complete) is not bool
    ):
        raise ValueError("Recorded inventory request has invalid fields.")
    declared_path = Path(path)
    records_path = _owned_records_path(declared_path)
    with retain_source(records_path):
        before = ordinary(records_path, directory=False)
        with open_source(records_path) as stream:
            payload = stream.read()
        if hashlib.sha256(payload).hexdigest() != digest:
            raise ValueError("Recorded inventory request does not match its input hash.")
        records: object = json.loads(payload)

        def check_inputs() -> None:
            try:
                final_digest, _ = file_hash(records_path, before)
            except (OSError, ValueError) as error:
                raise ValueError(
                    "Recorded inventory request changed during verification."
                ) from error
            if final_digest != digest:
                raise ValueError("Recorded inventory request changed during verification.")
            check_checkout()
            if _owned_records_path(declared_path) != records_path:
                raise ValueError(
                    "Recorded inventory request directory changed during verification."
                )

        try:
            verify_recorded_inventory(
                root,
                records,
                complete=complete,
                check_inputs=check_inputs,
                records_path=records_path,
            )
        except RecordedInventoryError as error:
            return str(error)
    return None


def execute_request(value: object) -> object:
    if not isinstance(value, dict):
        raise ValueError("Frozen inventory requests must be objects.")
    request = cast("dict[str, object]", value)
    operation = request.get("operation")
    allowed = {"protocol", "requestId", "operation", "root"}
    if operation == "inventory":
        allowed.update(("repository", "declaredFiles", "maximumFiles"))
    if operation == "verify-recorded":
        allowed.update(("repository", "recordsPath", "recordsSha256", "complete"))
    if set(request) - allowed:
        raise ValueError("Source census request has unknown fields.")
    protocol = request.get("protocol")
    identifier = request.get("requestId")
    root = request.get("root")
    if (
        type(protocol) is not int
        or protocol != 1
        or not isinstance(identifier, str)
        or not re.fullmatch(r"[a-f0-9]{32}", identifier)
        or operation not in ("sources", "host-sources", "inventory", "verify-recorded")
        or not isinstance(root, str)
        or not root.strip()
    ):
        raise ValueError("Frozen inventory request has invalid fields.")
    if operation == "sources":
        return source_files(Path(root))
    if operation == "host-sources":
        return host_source_files(Path(root))
    repository, maximum = request.get("repository"), request.get("maximumFiles")
    if not isinstance(repository, str) or not repository.strip():
        raise ValueError("Frozen inventory checkout or file bound is invalid.")
    repository_path = Path(repository)
    checkout = _inventory_checkout(repository_path)

    def check_checkout() -> None:
        if _inventory_checkout(repository_path) != checkout:
            raise ValueError("Frozen inventory checkout changed during the request.")

    if operation == "verify-recorded":
        return _verify_recorded_request(Path(root), request, check_checkout)
    if type(maximum) is not int:
        raise ValueError("Frozen inventory checkout or file bound is invalid.")
    result = frozen_player_inventory(Path(root), request.get("declaredFiles"), maximum)
    check_checkout()
    return result


def request_identifier(value: object) -> object:
    if not isinstance(value, dict):
        return None
    return cast("dict[str, object]", value).get("requestId")


def serve() -> int:
    input_stream = cast("io.TextIOWrapper[io.BufferedReader]", sys.stdin)
    input_stream.reconfigure(encoding="utf-8", errors="strict")
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
