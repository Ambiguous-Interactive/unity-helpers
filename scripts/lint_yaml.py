"""Lint the selected YAML corpus with the locked yamllint dependency."""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
from pathlib import Path

from cli_encoding import configure_output, escape_command_data
from csharp_sources import is_link
from yamllint.config import YamlLintConfig, YamlLintConfigError
from yamllint.linter import run

ROOT = Path(__file__).resolve().parents[1]


def git(repository: Path, *arguments: str) -> bytes:
    """Read exact repository paths and blobs without shell or newline splitting."""
    executable = shutil.which("git")
    if executable is None:
        raise ValueError("Git is required to inventory YAML sources")
    result = subprocess.run(  # noqa: S603 - fixed Git commands and literal argument arrays.
        [executable, "-C", str(repository), *arguments],
        capture_output=True,
        check=True,
        timeout=30,
    )
    return result.stdout


def yaml_path(repository: Path, name: str, *, staged: bool = False) -> tuple[Path, str]:
    """Resolve a repository file without allowing linked parents or path escapes."""
    path = Path(os.path.abspath(repository / name))  # noqa: PTH100
    if not path.is_relative_to(repository) or path.suffix.lower() not in (
        ".yaml",
        ".yml",
    ):
        raise ValueError(f"Expected a YAML file inside the repository: {name}")
    relative = path.relative_to(repository)
    current = repository
    for part in () if staged else relative.parts:
        current /= part
        if (current.exists() or current.is_symlink()) and is_link(current):
            raise ValueError(f"YAML source is linked: {name}")
    return path, relative.as_posix()


def sources(repository: Path, paths: list[str] | None, *, staged: bool) -> list[tuple[str, bytes]]:
    """Read every selected input before linting; staged mode reads index blobs."""
    if staged:
        top = Path(
            git(repository, "rev-parse", "--show-toplevel").decode().rstrip("\r\n")
        ).resolve()
        if top != repository:
            raise ValueError("Git repository root does not match the selected root")
        payload = git(
            repository,
            "diff",
            "--cached",
            "--name-only",
            "--diff-filter=ACMR",
            "-z",
            "--",
            *(
                [f":(literal){yaml_path(repository, name, staged=True)[1]}" for name in paths]
                if paths
                else ["*.yml", "*.yaml"]
            ),
        )
        names = payload.decode("utf-8").split("\0")
    elif paths is not None:
        names = paths
    else:
        payload = git(
            repository,
            "ls-files",
            "--cached",
            "--others",
            "--exclude-standard",
            "-z",
            "--",
            "*.yml",
            "*.yaml",
        )
        deleted = (
            git(repository, "ls-files", "--deleted", "-z", "--", "*.yml", "*.yaml")
            .decode("utf-8")
            .split("\0")
        )
        names = list(set(payload.decode("utf-8").split("\0")) - set(deleted))
    found: list[tuple[str, bytes]] = []
    for name in sorted(set(names) - {""}):
        path, relative = yaml_path(repository, name, staged=staged)
        if staged:
            mode = git(
                repository,
                "--literal-pathspecs",
                "ls-files",
                "--stage",
                "-z",
                "--",
                relative,
            )
            if not mode.startswith(b"100644 ") and not mode.startswith(b"100755 "):
                raise ValueError(f"Staged YAML source is not a regular file: {relative}")
            data = git(repository, "show", f":{relative}")
        else:
            if not path.is_file():
                raise ValueError(f"YAML source not found or not a regular file: {relative}")
            data = path.read_bytes()
        found.append((relative, data))
    if not found and not staged:
        raise ValueError("The YAML scan found no files; a pass would mean nothing")
    return found


def main(argv: list[str] | None = None) -> int:
    """Run YAML rules with visible discovery, configuration, and input failures."""
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--paths", nargs="+")
    parser.add_argument("--staged-only", action="store_true")
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    repository = args.repo_root.resolve()
    try:
        config_path = repository / ".yamllint.yaml"
        if not config_path.is_file() or is_link(config_path):
            raise ValueError(f"yamllint config file not found or linked: {config_path}")
        config = YamlLintConfig(content=config_path.read_text(encoding="utf-8-sig"))
        selected = sources(repository, args.paths, staged=args.staged_only)
        checked = errors = warnings = 0
        for name, data in selected:
            if config.is_file_ignored(name):
                continue
            checked += 1
            for problem in run(data.decode("utf-8-sig"), config, filepath=name):
                errors += problem.level == "error"
                warnings += problem.level == "warning"
                print(
                    f"{escape_command_data(name)}:{problem.line}:{problem.column}: "
                    f"[{problem.level}] {escape_command_data(problem.desc)} ({problem.rule})"
                )
        if args.verbose or errors or warnings:
            print(
                f"[lint-yaml] Checked {checked} YAML file(s), ignored {len(selected) - checked}; "
                f"{errors} error(s), {warnings} warning(s)."
            )
        return int(bool(errors))
    except (
        OSError,
        UnicodeError,
        ValueError,
        YamlLintConfigError,
        subprocess.SubprocessError,
    ) as error:
        print(f"[lint-yaml] ERROR: {escape_command_data(str(error))}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
