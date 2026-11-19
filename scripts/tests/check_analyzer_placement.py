"""Verify Unity labels and the effective MSBuild analyzer items."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from tempfile import TemporaryDirectory
from typing import TypedDict, cast
from xml.sax.saxutils import escape, quoteattr

ROOT = Path(__file__).resolve().parents[2]
ANALYZER_DIRECTORY = Path("Runtime/Analyzers")
PROJECT_DIRECTORY = Path("Generator~")
PRODUCTION_PROJECTS = ("TypeCheck", "EditorCheck", "IntegrationCheck")
PRODUCTION_IDS = ("WUH019", "WUH021", "WUH023")
CONSUMER_OPT_INS = {
    "WUH024": "enum cache advice stays an explicit per-project opt-in",
    "WUH029": "member order stays an explicit consumer opt-in while repository source gates retain ownership",
    "WUH030": "string method policy stays an explicit consumer opt-in",
}


class AnalyzerItem(TypedDict):
    FullPath: str


class Checks:
    def __init__(self) -> None:
        self.passed = 0

    def require(self, description: str, condition: bool, details: str = "") -> None:
        if not condition:
            raise ValueError(f"{description}: {details}" if details else description)
        self.passed += 1


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def governing_assembly(file: Path, root: Path) -> Path | None:
    for directory in file.parents:
        definition = next(
            (path for path in sorted(directory.iterdir()) if path.suffix.lower() == ".asmdef"),
            None,
        )
        if definition is not None or directory == root:
            return definition
    return None


def imported_analyzer_copies(root: Path, names: set[str]) -> dict[str, list[Path]]:
    copies: dict[str, list[Path]] = {name: [] for name in names}
    directories = [root]
    while directories:
        directory = directories.pop()
        with os.scandir(directory) as entries:
            for entry in entries:
                name = entry.name
                if name == "node_modules" or name.startswith(".") or name.endswith("~"):
                    continue
                path = Path(entry.path)
                if entry.is_dir(follow_symlinks=False):
                    directories.append(path)
                elif name in copies:
                    copies[name].append(path)
    return copies


def check_unity_metadata(root: Path, binaries: list[Path], checks: Checks) -> None:
    copies = imported_analyzer_copies(root, {binary.name for binary in binaries})
    for binary in binaries:
        label = binary.relative_to(root)
        sidecar = binary.with_suffix(".dll.meta")
        checks.require(f"{label} has a .meta sidecar", sidecar.exists(), str(sidecar))
        meta = read(sidecar)
        checks.require(
            f"{label} is labelled RoslynAnalyzer",
            re.search(r"labels:\s*\n\s*-\s*RoslynAnalyzer\b", meta) is not None,
            "without the label Unity loads it as a plain managed plugin, not a compiler input",
        )
        checks.require(
            f"{label} disables every platform",
            all(
                re.search(rf"{platform}:\s*\n\s*enabled:\s*0", meta)
                for platform in ("Any", "Editor")
            ),
            "an analyzer enabled for a platform would be copied into player builds",
        )
        definition = governing_assembly(binary, root)
        checks.require(f"an .asmdef governs {label}", definition is not None, "found none")
        if definition is None:
            continue
        assembly = cast("dict[str, object]", json.loads(read(definition)))
        checks.require(
            f"the assembly governing {label} is not editor-only",
            assembly.get("includePlatforms") != ["Editor"],
            f"{definition.relative_to(root)} restricts includePlatforms to Editor, so no consumer "
            "runtime assembly can reference it and the analyzer would never run on consumer code",
        )
        strays = sorted(path.relative_to(root) for path in copies[binary.name] if path != binary)
        checks.require(
            f"no second copy of {label} is anywhere Unity imports",
            not strays,
            ", ".join(map(str, strays)),
        )
        project = PROJECT_DIRECTORY / binary.stem / f"{binary.stem}.csproj"
        checks.require(
            f"{label} is built by a project in Generator~",
            (root / project).exists(),
            f"expected {project}",
        )


def check_rulesets(root: Path, binaries: list[Path], checks: Checks) -> tuple[list[str], str, str]:
    directory = root / PROJECT_DIRECTORY
    diagnostics = read(
        directory / "WallstopStudios.UnityHelpers.Analyzers/UnityHelpersDiagnostics.cs"
    )
    catalog = read(root / "Editor/Tools/AnalyzerPolicyAPI.cs")
    descriptors = re.findall(r'^\s+"(WUH[0-9]{3})",$', diagnostics, flags=re.M)
    catalog_ids = re.findall(r'new\(\s*"(WUH[0-9]{3})",', catalog)
    checks.require(
        "the analyzer-policy editor catalog matches every shipped WUH descriptor",
        catalog_ids == descriptors,
        f"descriptors={','.join(descriptors)} catalog={','.join(catalog_ids)}",
    )
    shared = read(directory / "CheckProjects.ruleset")
    enabled = sorted(re.findall(r'<Rule Id="(WUH[0-9]{3})" Action="Warning" />', shared))
    checks.require(
        "shared opt-in WUH policies are enabled for package-owned check projects",
        enabled == ["WUH010", "WUH013", "WUH018"],
        ",".join(enabled),
    )
    build_policy = read(directory / "Directory.Build.props")
    for binary in binaries:
        checks.require(
            f"every owned .NET project loads {binary.name}",
            f"Runtime/Analyzers/{binary.name}" in build_policy,
            "Generator~/Directory.Build.props must load the shipped binary",
        )
    production = read(directory / "ProductionCheckProjects.ruleset")
    checks.require(
        "the production policy imports shared policies and enables WUH019, WUH021 and WUH023",
        '<Include Path="CheckProjects.ruleset" Action="Default" />' in production
        and all(f'<Rule Id="{rule}" Action="Warning" />' in production for rule in PRODUCTION_IDS),
        "WUH019, WUH021 and WUH023 are production-only opt-ins; tests and tooling retain the shared ruleset",
    )
    checks.require(
        "shared projects explicitly disable WUH019, WUH021 and WUH023",
        all(f'<Rule Id="{rule}" Action="None" />' in shared for rule in PRODUCTION_IDS),
        "The shared ruleset retains the default-disabled WUH019, WUH021 and WUH023 policies",
    )
    for project in PRODUCTION_PROJECTS:
        checks.require(
            f"production {project} enables loop-bound caching",
            f"'$(MSBuildProjectName)' == 'WallstopStudios.UnityHelpers.{project}'" in build_policy,
            project,
        )
    warnings = re.findall(r"<WarningsAsErrors([^>]*)>([^<]+)</WarningsAsErrors>", build_policy)
    checks.require(
        "WUH019, WUH021 and WUH023 promotion is limited to the three production projects",
        any(
            not condition and all(rule not in rules for rule in PRODUCTION_IDS)
            for condition, rules in warnings
        )
        and any(
            all(rule in rules for rule in PRODUCTION_IDS)
            and all(
                f"WallstopStudios.UnityHelpers.{project}" in condition
                for project in PRODUCTION_PROJECTS
            )
            and "TestCheck" not in condition
            and "EditorTestCheck" not in condition
            for condition, rules in warnings
        ),
        "Explicit WarningsAsErrors IDs override ruleset None; only production may list WUH019, WUH021 or WUH023",
    )
    return descriptors, shared, build_policy


def path_identity(path: str | Path, root: Path) -> str:
    return os.path.normcase(os.path.abspath(root / path))  # noqa: PTH100 - compare without dereferencing symlinks.


def evaluated_items(dotnet: str, root: Path, project: Path, oracle: str) -> list[AnalyzerItem]:
    result = subprocess.run(  # noqa: S603 - evaluate the fixed MSBuild query on owned projects.
        [
            dotnet,
            "msbuild",
            str(project),
            "--nologo",
            "-getItem:Analyzer",
            f"-p:ProtobufNetOracle={oracle}",
        ],
        cwd=root,
        capture_output=True,
        check=False,
        encoding="utf-8",
        timeout=30,
    )
    if result.returncode:
        raise ValueError(
            f"MSBuild failed for {project} ({oracle}): {result.stderr or result.stdout}"
        )
    if len(result.stdout.encode("utf-8")) > 1024 * 1024:
        raise ValueError(f"MSBuild analyzer output exceeded 1 MiB for {project}")
    data = cast("dict[str, object]", json.loads(result.stdout))
    group = cast("dict[str, object]", data.get("Items", {}))
    items = group.get("Analyzer")
    if not isinstance(items, list):
        raise ValueError(f"MSBuild did not return analyzer items for {project}")
    return cast("list[AnalyzerItem]", items)


def valid_placement(items: list[AnalyzerItem], root: Path, binaries: list[Path]) -> bool:
    actual = [path_identity(item["FullPath"], root) for item in items]
    for binary in binaries:
        preferred = path_identity(binary, root)
        matching = [path for path in actual if Path(path).name == Path(preferred).name]
        if matching != [preferred]:
            return False
    return True


def check_projects(root: Path, binaries: list[Path], dotnet: str, checks: Checks) -> None:
    directory = root / PROJECT_DIRECTORY
    with os.scandir(directory) as entries:
        projects = sorted(
            file
            for entry in entries
            if entry.is_dir(follow_symlinks=False)
            for file in Path(entry.path).iterdir()
            if file.suffix == ".csproj"
        )
    checks.require("owned project discovery is nonempty", bool(projects), "Generator~")
    checks.require(
        "compiler compatibility project remains in the placement check",
        any(
            project.name == "WallstopStudios.UnityHelpers.Proto.CompilerFloor.Tests.csproj"
            for project in projects
        ),
    )
    for project in projects:
        for oracle in ("v3", "v2"):
            checks.require(
                f"owned project inherits each shipped analyzer once: {project.relative_to(root)} ({oracle})",
                valid_placement(evaluated_items(dotnet, root, project, oracle), root, binaries),
                "MSBuild must evaluate exactly one compiler input at each shipped path",
            )


def check_msbuild_controls(root: Path, binaries: list[Path], dotnet: str, checks: Checks) -> None:
    with TemporaryDirectory(prefix="wuh analyzer placement-") as temporary:
        fixture = Path(temporary)
        project = fixture / "Fixture.proj"
        import_path = quoteattr(str(root / PROJECT_DIRECTORY / "Directory.Build.props"))
        inherited = f"<Import Project={import_path} />"

        def probe(body: str, oracle: str = "v3") -> bool:
            project.write_text(f"<Project>{body}</Project>", encoding="utf-8")
            return valid_placement(evaluated_items(dotnet, root, project, oracle), root, binaries)

        references = "".join(
            '<ItemGroup><Reference Include="Fixture">'
            f"<HintPath>{escape(str(binary))}</HintPath></Reference></ItemGroup>"
            for binary in binaries
        )
        checks.require(
            "ordinary assembly references do not duplicate compiler inputs",
            probe(inherited + references),
        )
        for binary in binaries:
            file = quoteattr(str(binary))
            add_analyzer = f"<Analyzer Include={file} />"
            remove_analyzer = f"<Analyzer Remove={file} />"
            replacement = quoteattr(str(fixture / binary.name))
            candidates = (
                ("duplicate analyzer", f"<ItemGroup>{add_analyzer}</ItemGroup>", "v3"),
                (
                    "property-aliased duplicate",
                    (
                        f"<PropertyGroup><OwnedCompiler>{escape(str(binary))}</OwnedCompiler></PropertyGroup>"
                        '<ItemGroup><Analyzer Include="$(OwnedCompiler)" /></ItemGroup>'
                    ),
                    "v3",
                ),
                (
                    "v2-only duplicate",
                    f"<ItemGroup Condition=\"'$(ProtobufNetOracle)' == 'v2'\">{add_analyzer}</ItemGroup>",
                    "v2",
                ),
                (
                    "an analyzer from a different directory",
                    f"<ItemGroup>{remove_analyzer}<Analyzer Include={replacement} /></ItemGroup>",
                    "v3",
                ),
                ("missing analyzer", f"<ItemGroup>{remove_analyzer}</ItemGroup>", "v3"),
            )
            for label, body, oracle in candidates:
                checks.require(
                    f"{label} is rejected: {binary.name}", not probe(inherited + body, oracle)
                )
        checks.require("an empty compiler item set is rejected", not probe(""))


def run(root: Path) -> int:
    directory = root / ANALYZER_DIRECTORY
    binaries = sorted(path for path in directory.iterdir() if path.suffix == ".dll")
    checks = Checks()
    checks.require("at least one analyzer ships", bool(binaries), "Runtime/Analyzers is empty")
    check_unity_metadata(root, binaries, checks)
    descriptors, shared, build_policy = check_rulesets(root, binaries, checks)
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        raise ValueError("dotnet is required to evaluate project analyzer items")
    check_projects(root, binaries, dotnet, checks)
    check_msbuild_controls(root, binaries, dotnet, checks)
    promoted = re.findall(r"(?:^|;)(WUH[0-9]{3})(?=;|<)", build_policy)
    checks.require(
        "shared WUH policies are promoted globally and WUH019, WUH021 and WUH023 only in production",
        sorted(promoted) == sorted(rule for rule in descriptors if rule not in CONSUMER_OPT_INS),
        f"descriptors={','.join(descriptors)} promoted={','.join(promoted)}",
    )
    for rule, label in CONSUMER_OPT_INS.items():
        checks.require(
            label, f'<Rule Id="{rule}" Action="None" />' in shared and rule not in promoted
        )
    return checks.passed


def main() -> int:
    try:
        count = run(ROOT)
    except (OSError, ValueError, KeyError, TypeError, subprocess.TimeoutExpired) as error:
        print(error, file=sys.stderr)
        return 1
    print(f"Analyzer placement contract passed ({count} checks).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
