"""Validate the npm archive against tracked Unity package contents."""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tarfile
import tempfile
from pathlib import Path, PurePosixPath

from cli_encoding import configure_output
from release_tools import json_object, read, text_field
from typecheck_clean import npm_command

FORBIDDEN = (
    ".artifacts",
    ".cursor",
    ".git",
    ".github",
    ".githooks",
    ".llm",
    ".mcp.json",
    "node_modules",
    "package-lock.json",
    "Tests",
)
ALLOWED_TOP_LEVEL = (
    "CHANGELOG.md",
    "CHANGELOG.md.meta",
    "Editor",
    "Editor.meta",
    "LICENSE",
    "LICENSE.meta",
    "README.md",
    "README.md.meta",
    "Runtime",
    "Runtime.meta",
    "Samples~",
    "Shaders",
    "Shaders.meta",
    "Styles",
    "Styles.meta",
    "URP",
    "URP.meta",
    "docs",
    "docs.meta",
    "link.xml",
    "link.xml.meta",
    "package.json",
    "package.json.meta",
    "scripts",
    "scripts.meta",
)
REQUIRED = (
    "CHANGELOG.md",
    "CHANGELOG.md.meta",
    "Editor",
    "Editor.meta",
    "LICENSE",
    "LICENSE.meta",
    "README.md",
    "README.md.meta",
    "Runtime",
    "Runtime.meta",
    "Samples~",
    "scripts.meta",
    "scripts/postinstall-hooks.js",
    "scripts/postinstall-hooks.js.meta",
    "Shaders",
    "Shaders.meta",
    "Styles",
    "Styles.meta",
    "URP",
    "URP.meta",
    "link.xml",
    "link.xml.meta",
    "package.json",
    "package.json.meta",
)
CONTENT_ROOTS = (
    "CHANGELOG.md",
    "CHANGELOG.md.meta",
    "Editor",
    "Editor.meta",
    "LICENSE",
    "LICENSE.meta",
    "README.md",
    "README.md.meta",
    "Runtime",
    "Runtime.meta",
    "Samples~",
    "Shaders",
    "Shaders.meta",
    "Styles",
    "Styles.meta",
    "URP",
    "URP.meta",
    "docs",
    "docs.meta",
    "link.xml",
    "link.xml.meta",
    "package.json",
    "package.json.meta",
    "scripts.meta",
    "scripts/postinstall-hooks.js",
    "scripts/postinstall-hooks.js.meta",
)
UNITY_FOLDERS = ("Runtime", "Editor", "Samples~", "Shaders", "Styles", "URP")
CS_ROOTS = ("Runtime/", "Editor/", "Samples~/", "Styles/")
SCRIPT_FILES = ("postinstall-hooks.js", "postinstall-hooks.js.meta")


def archive_inventory(archive: Path) -> tuple[set[str], set[str]]:
    files: set[str] = set()
    paths: set[str] = set()
    seen: set[str] = set()
    with tarfile.open(archive, "r:gz") as package:
        for entry in package:
            path = PurePosixPath(entry.name)
            if (
                not path.parts
                or path.parts[0] != "package"
                or path.is_absolute()
                or any(part in {".", ".."} for part in entry.name.split("/"))
                or "\\" in entry.name
                or ":" in entry.name
                or not (entry.isfile() or entry.isdir())
            ):
                raise ValueError(f"Unsafe npm archive entry: {entry.name!r}")
            if len(path.parts) == 1:
                if not entry.isdir():
                    raise ValueError("npm archive package root must be a directory")
                continue
            relative = PurePosixPath(*path.parts[1:])
            name = relative.as_posix()
            if (
                name in seen
                or (entry.isfile() and name in paths)
                or any(parent.as_posix() in files for parent in relative.parents if parent.parts)
            ):
                raise ValueError(f"Duplicate npm archive entry: {name}")
            seen.add(name)
            if entry.isfile():
                files.add(name)
            paths.add(name)
            paths.update(parent.as_posix() for parent in relative.parents if parent.parts)
    if not paths:
        raise ValueError("Package directory not found after reading archive")
    return files, paths


def forbidden_artifact(path: str) -> bool:
    return path.casefold().startswith("pr-description.md")


def expected_exclusion(path: str) -> bool:
    name = PurePosixPath(path).name.casefold()
    return name == ".gitkeep" or name.endswith((".dll", ".pdb", ".tmp", ".log", ".rsp"))


def validation_errors(files: set[str], paths: set[str], tracked: set[str]) -> list[str]:
    errors: list[str] = []
    errors.extend(
        f"Forbidden development entry included in npm package: {name}"
        for name in FORBIDDEN
        if name in paths
    )
    for name in sorted(path for path in paths if "/" not in path):
        if forbidden_artifact(name):
            errors.append(f"Forbidden release artifact included in npm package: {name}")
        elif name not in ALLOWED_TOP_LEVEL:
            errors.append(f"Unexpected top-level entry included in npm package: {name}")
    errors.extend(
        f"Unexpected script included in npm package: {name}"
        for name in sorted(files)
        if name.startswith("scripts/") and name.removeprefix("scripts/") not in SCRIPT_FILES
    )
    errors.extend(
        f"Missing required package entry: {name}" for name in REQUIRED if name not in paths
    )
    errors.extend(
        f"C# source outside Unity package roots included in npm package: {name}"
        for name in sorted(files)
        if name.casefold().endswith(".cs") and not name.startswith(CS_ROOTS)
    )
    for folder in UNITY_FOLDERS:
        if folder not in paths:
            errors.append(f"Missing required folder: {folder}")
            continue
        if folder in files:
            errors.append(f"Required Unity root is not a directory: {folder}")
            continue
        if folder != "Samples~" and folder + ".meta" not in paths:
            errors.append(f"Missing .meta file for folder: {folder}")
        for name in sorted(path for path in paths if path.startswith(folder + "/")):
            if name.casefold().endswith(".meta"):
                if name[:-5] not in paths:
                    errors.append(f"Orphaned .meta file (missing source): {name}")
            elif name + ".meta" not in paths:
                kind = "file" if name in files else "directory"
                errors.append(f"Missing .meta file for {kind}: {name}")
    errors.extend(
        f"File in git repo but missing in npm package: {name}"
        for name in sorted(tracked - files)
        if not expected_exclusion(name)
    )
    errors.extend(
        f"File in npm package but not tracked in git repo: {name}"
        for name in sorted(files - tracked)
        if not forbidden_artifact(name)
    )
    return sorted(errors)


def tracked_files(root: Path) -> set[str]:
    git = shutil.which("git")
    if git is None:
        raise ValueError("Git is required to collect tracked package files")
    environment = {
        key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")
    }
    result = subprocess.run(  # noqa: S603 - fixed read-only Git inventory with literal package roots.
        [git, "-C", str(root), "ls-files", "-z", "--", *CONTENT_ROOTS],
        env=environment,
        capture_output=True,
        check=False,
    )
    if result.returncode:
        raise ValueError("git ls-files failed while collecting tracked package files.")
    return {name.replace("\\", "/") for name in result.stdout.decode("utf-8").split("\0") if name}


def validate(root: Path, *, verbose: bool = False) -> int:
    root = root.resolve(strict=True)
    package = root / "package.json"
    if not package.is_file():
        raise ValueError(f"package.json not found at {package}")
    if not text_field(json_object(read(package)), "version").strip():
        raise ValueError("package.json must define a non-empty version.")
    with tempfile.TemporaryDirectory(prefix="npm-package-validation-") as directory:
        temporary = Path(directory).resolve(strict=True)
        stdout, stderr = temporary / "npm-pack.stdout.json", temporary / "npm-pack.stderr.log"
        if verbose:
            print("[validate-npm-package] Running npm pack...")
        with stdout.open("wb") as output, stderr.open("wb") as error:
            result = subprocess.run(  # noqa: S603 - installed npm CLI without a shell; stdout/stderr stream to owned files.
                [*npm_command(), "pack", "--json", "--pack-destination", str(temporary)],
                cwd=root,
                stdout=output,
                stderr=error,
                check=False,
            )
        if result.returncode:
            print(
                f"[validate-npm-package] npm pack failed with exit code {result.returncode}.",
                flush=True,
            )
            for path in (stdout, stderr):
                with path.open("rb") as stream:
                    shutil.copyfileobj(stream, sys.stdout.buffer)
            return result.returncode
        archives = list(temporary.glob("*.tgz"))
        if len(archives) != 1:
            raise ValueError(f"Expected one npm tarball; found {len(archives)}")
        archive = archives[0]
        if archive.is_symlink() or archive.resolve() != archive.absolute():
            raise ValueError("Refusing a linked npm archive")
        files, paths = archive_inventory(archive)
        if verbose:
            print(
                f"[validate-npm-package] npm pack produced {archive.name} with {len(files)} file(s)."
            )
        errors = validation_errors(files, paths, tracked_files(root))
        if errors:
            print(f"[validate-npm-package] \nValidation failed with {len(errors)} error(s):\n")
            for message in errors:
                print(f"  ✗ {message}")
            print("\n[validate-npm-package] NPM package validation failed.")
            return 1
    print("\n[validate-npm-package] ✓ All Unity files have corresponding .meta files")
    print("[validate-npm-package] ✓ All .meta files have corresponding source files")
    print("[validate-npm-package] ✓ NPM package content matches git repository")
    print("\n[validate-npm-package] NPM package validation passed!")
    return 0


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(arguments)
    try:
        return validate(args.repo_root, verbose=args.verbose)
    except (OSError, ValueError, tarfile.TarError, subprocess.SubprocessError) as error:
        print(f"[validate-npm-package] {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
