"""Check or format selected C# sources with the repository's pinned CSharpier."""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
PREFIX = "[lint-csharp-format]"


def run(arguments: list[str], root: Path = ROOT) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument("--fix", action="store_true")
    parser.add_argument("--skip-when-unavailable", action="store_true")
    parser.add_argument("--paths", nargs="*", default=[])
    options = parser.parse_args(arguments)
    verbose = bool(options.verbose)
    fix = bool(options.fix)
    skip = bool(options.skip_when_unavailable)
    paths: list[str] = options.paths
    manifest = root / ".config/dotnet-tools.json"
    dotnet = shutil.which("dotnet")
    unavailable = (
        "no .NET tool manifest at .config/dotnet-tools.json."
        if not manifest.exists()
        else "dotnet was not found on PATH, so C# formatting cannot be verified."
        if dotnet is None
        else None
    )
    if unavailable is not None:
        print(f"{PREFIX} {'Skipped: ' if skip else ''}{unavailable}")
        if not skip:
            print("  Install the .NET SDK and run dotnet tool restore from the repository root.")
        return 0 if skip else 1
    if dotnet is None:
        return 1
    try:
        if verbose:
            print(f"{PREFIX} Restoring .NET tools.", flush=True)
        restore = subprocess.run(  # noqa: S603 - fixed dotnet command.
            [dotnet, "tool", "restore"],
            cwd=root,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
            timeout=300,
        )
        if restore.returncode != 0:
            print(f"{PREFIX} dotnet tool restore failed with exit code {restore.returncode}.")
            print(restore.stdout)
            print(
                '  Run "dotnet tool restore" from the repository root and resolve the error above.'
            )
            return 1
        targets = [path for path in paths if path.strip()]
        scope = "the repository"
        if targets:
            targets = [path for path in targets if (root / path).exists()]
            if not targets:
                if verbose:
                    print(f"{PREFIX} No existing C# targets to check.")
                return 0
            scope = f"{len(targets)} changed file(s)"
        else:
            targets = ["."]
        verb = "format" if fix else "check"
        if verbose:
            print(f"{PREFIX} Running csharpier {verb} over {scope}.", flush=True)
        result = subprocess.run(  # noqa: S603 - pinned formatter and literal path arguments.
            [dotnet, "tool", "run", "csharpier", verb, *targets],
            cwd=root,
            check=False,
            timeout=600,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        print(f"{PREFIX} {error}", file=sys.stderr)
        return 1
    if result.returncode != 0:
        message = (
            f"csharpier format failed with exit code {result.returncode}."
            if fix
            else f"C# formatting does not match CSharpier ({scope})."
        )
        print(f"{PREFIX} {message}")
        if not fix:
            print("  Fix with: dotnet tool run csharpier format .")
            print("  Or scope it: npm run agent:preflight:fix")
        return result.returncode if result.returncode >= 0 else 1
    if verbose:
        print(f"{PREFIX} C# formatting matches CSharpier.")
    return 0


if __name__ == "__main__":
    configure_output()
    sys.exit(run(sys.argv[1:]))
