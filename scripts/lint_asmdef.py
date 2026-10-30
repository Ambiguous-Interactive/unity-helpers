"""Validate assembly references, Unity version expressions, and per-assembly defines."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import unicodedata
from dataclasses import dataclass
from pathlib import Path
from typing import cast

ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOTS = ("Runtime", "Editor", "Tests", "Samples~", "Styles", "URP", "Shaders")
EXCLUDED = frozenset(("node_modules", ".git", "obj", "bin", "Library", "Temp"))
EXTERNAL_ASSEMBLIES = frozenset(
    name.casefold()
    for name in (
        "UnityEngine",
        "UnityEngine.UI",
        "UnityEngine.TestRunner",
        "UnityEditor",
        "UnityEditor.UI",
        "UnityEditor.TestRunner",
        "Unity.TextMeshPro",
        "Unity.TextMeshPro.Editor",
        "Unity.InputSystem",
        "Unity.InputSystem.Editor",
        "Unity.Addressables",
        "Unity.Addressables.Editor",
        "Unity.ResourceManager",
        "Unity.Burst",
        "Unity.Burst.Editor",
        "Unity.Collections",
        "Unity.Jobs",
        "Unity.Mathematics",
        "Unity.Mathematics.Editor",
        "Unity.RenderPipelines.Core.Runtime",
        "Unity.RenderPipelines.Core.Editor",
        "Unity.RenderPipelines.Universal.Runtime",
        "Unity.RenderPipelines.Universal.Editor",
        "Unity.RenderPipelines.HighDefinition.Runtime",
        "Unity.RenderPipelines.HighDefinition.Editor",
        "Unity.Netcode.Runtime",
        "Unity.Netcode.Editor",
        "Unity.Netcode.Components",
        "Unity.Services.Core",
        "Unity.Services.Authentication",
        "Unity.VisualScripting.Core",
        "Unity.VisualScripting.Flow",
        "Unity.Localization",
        "Unity.Localization.Editor",
        "Unity.2D.Animation.Runtime",
        "Unity.2D.Animation.Editor",
        "Unity.2D.SpriteShape.Runtime",
        "Unity.2D.SpriteShape.Editor",
        "Unity.2D.Tilemap.Extras",
        "Unity.Timeline",
        "Unity.Timeline.Editor",
        "Unity.Cinemachine",
        "Unity.Cinemachine.Editor",
        "Unity.Entities",
        "Unity.Entities.Editor",
        "Unity.Transforms",
        "Unity.Physics",
        "Unity.Rendering.Hybrid",
        "Unity.ProBuilder",
        "Unity.ProBuilder.Editor",
        "Unity.Polybrush",
        "Unity.Polybrush.Editor",
        "Unity.Recorder",
        "Unity.Recorder.Editor",
        "Zenject",
        "Zenject-usage",
        "Zenject.Editor",
        "VContainer",
        "VContainer.Unity",
        "VContainer.Editor",
        "Reflex",
        "Reflex.Editor",
        "Sirenix.OdinInspector.Attributes",
        "Sirenix.OdinInspector.Editor",
        "Sirenix.Serialization",
        "Sirenix.Serialization.Config",
        "Sirenix.Utilities",
        "Sirenix.Utilities.Editor",
        "UniTask",
        "UniTask.Linq",
        "UniTask.Addressables",
        "UniTask.DOTween",
        "UniTask.TextMeshPro",
        "DOTween.Modules",
        "DG.Tweening",
        "R3.Unity",
        "R3",
        "UniRx",
        "UniRx.Async",
        "Newtonsoft.Json",
        "Unity.Nuget.Newtonsoft-Json",
        "MessagePack",
        "MessagePack.Unity",
        "MessagePack.Annotations",
        "Mirror",
        "Mirror.Components",
        "PhotonUnityNetworking",
        "PhotonRealtime",
        "NaughtyAttributes.Core",
        "NaughtyAttributes.Editor",
        "Spine.Unity",
        "Spine.Unity.Editor",
    )
)


@dataclass(frozen=True)
class ExpressionResult:
    valid: bool
    reason: str = ""
    suggestion: str = ""


def is_blank(value: str) -> bool:
    return all(
        character in "\t\n\v\f\r\u0085" or unicodedata.category(character) in {"Zs", "Zl", "Zp"}
        for character in value
    )


def version_expression(expression: str | None) -> ExpressionResult:
    if expression is None or is_blank(expression):
        return ExpressionResult(True)
    begin, end = 0, len(expression) - 1
    left_symbol, right_symbol = "_", "_"
    if not "0" <= expression[0] <= "9":
        left_symbol = expression[0]
        if left_symbol not in "[(":
            return ExpressionResult(False, f"invalid leading character '{left_symbol}'")
        begin += 1
    if not re.fullmatch(r"[0-9a-zA-Z.-]", expression[-1]):
        right_symbol = expression[-1]
        if right_symbol not in "])":
            return ExpressionResult(False, f"invalid trailing character '{right_symbol}'")
        end -= 1
    if (left_symbol == "_") != (right_symbol == "_"):
        return ExpressionResult(False, "unbalanced brackets (missing one of '[', '(', ']', ')')")
    tokens = expression[begin : end + 1].split(",")
    left = tokens[0]
    right = tokens[1] if len(tokens) > 1 else ""
    for token in (left, right):
        if not token:
            continue
        if not "0" <= token[0] <= "9":
            return ExpressionResult(False, f"version token '{token}' must start with a digit")
        if not re.fullmatch(r"[0-9a-zA-Z]", token[-1]):
            return ExpressionResult(
                False, f"version token '{token}' must end with a letter or digit"
            )
        for character in token:
            if not re.fullmatch(r"[0-9a-zA-Z.-]", character):
                return ExpressionResult(
                    False, f"version token '{token}' contains invalid character '{character}'"
                )
    key = (left_symbol, right_symbol, "," in expression, bool(left), bool(right))
    valid_keys = {
        ("_", "_", False, True, False),
        ("(", ")", True, True, False),
        ("[", "]", False, True, False),
        ("(", "]", True, False, True),
        ("(", "]", True, True, True),
        ("(", ")", True, False, True),
        ("[", "]", True, True, True),
        ("(", ")", True, True, True),
        ("[", ")", True, True, True),
    }
    if key in valid_keys:
        return ExpressionResult(True)
    suggestion = ""
    open_range = re.fullmatch(r"\[\s*(\d[^,\]\)]*?)\s*,\s*[\)\]]\s*", expression)
    if open_range:
        bare = open_range[1].strip()
        suggestion = f"use the bare version '{bare}' (Unity reads a bare version as '>= {bare}'); Unity has no '[X,)' open-ended range"
    return ExpressionResult(
        False,
        "not a valid Unity version-define expression (the define is silently never applied)",
        suggestion,
    )


def read_object(path: Path) -> dict[str, object]:
    value: object = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError("Assembly JSON must be an object")
    return cast("dict[str, object]", value)


def version_defines(document: dict[str, object]) -> list[dict[str, object]]:
    raw = document.get("versionDefines")
    if raw is None:
        return []
    if not isinstance(raw, list):
        raise ValueError("versionDefines must be an array")
    values: list[dict[str, object]] = []
    for value in cast("list[object]", raw):
        if not isinstance(value, dict):
            raise ValueError("versionDefines entries must be objects")
        values.append(cast("dict[str, object]", value))
    return values


def optional_string(document: dict[str, object], name: str) -> str:
    value = document.get(name)
    if value is None:
        return ""
    if not isinstance(value, str):
        raise ValueError(f"'{name}' must be a string")
    return value


def raise_walk_error(error: OSError) -> None:
    raise error


def discover(repository: Path) -> dict[str, list[Path]]:
    files: dict[str, list[Path]] = {".asmdef": [], ".asmref": [], ".cs": []}
    for name in SOURCE_ROOTS:
        root = repository / name
        if not root.is_dir():
            raise ValueError(
                f"Source root not found: {name}. If it moved, update SOURCE_ROOTS in the same commit."
            )
        for directory, children, names in os.walk(root, onerror=raise_walk_error):
            children[:] = sorted(child for child in children if child not in EXCLUDED)
            for filename in sorted(names):
                suffix = Path(filename).suffix.lower()
                if suffix in files:
                    files[suffix].append(Path(directory) / filename)
    return {suffix: sorted(paths) for suffix, paths in files.items()}


def known_reference(name: str, assemblies: set[str]) -> bool:
    folded = name.casefold()
    return (
        folded.startswith(("guid:", "unity.", "unityengine.", "unityeditor."))
        or folded in EXTERNAL_ASSEMBLIES
        or folded in assemblies
    )


def check(repository: Path, *, verbose: bool = False) -> int:
    errors: list[str] = []
    warnings: list[str] = []
    documents: dict[Path, dict[str, object]] = {}
    try:
        sources = discover(repository)
        definitions = sources[".asmdef"]
        references = sources[".asmref"]
        if not definitions:
            raise ValueError("Assembly scan found no .asmdef files")
    except (OSError, ValueError) as error:
        print(f"[lint-asmdef] ERROR: {error}")
        return 1
    for path in definitions + references:
        try:
            documents[path] = read_object(path)
        except (OSError, ValueError) as error:
            errors.append(
                f"[{path.relative_to(repository).as_posix()}] Invalid JSON syntax: {error}"
            )
    assemblies = {
        value.casefold()
        for path in definitions
        if path in documents
        if isinstance(value := documents[path].get("name"), str) and value
    }
    symbol_packages: dict[str, set[str]] = {}
    declared: dict[Path, tuple[Path, set[str], set[str]]] = {}
    for path, document in documents.items():
        relative = path.relative_to(repository).as_posix()
        if verbose:
            print(f"[lint-asmdef] Validating: {relative}")
        try:
            if path.suffix.lower() == ".asmref":
                reference = optional_string(document, "reference")
                if not reference:
                    errors.append(f"[{relative}] Missing required 'reference' field")
                elif not known_reference(reference, assemblies):
                    errors.append(
                        f"[{relative}] Referenced assembly '{reference}' not found in repository"
                    )
                continue
            name = optional_string(document, "name")
            if not name:
                errors.append(f"[{relative}] Missing required 'name' field")
            elif name.casefold() != path.stem.casefold():
                errors.append(
                    f"[{relative}] Name mismatch: 'name' field is '{name}' but filename suggests '{path.stem}'"
                )
            if not optional_string(document, "rootNamespace"):
                warnings.append(
                    f"[{relative}] Missing 'rootNamespace' field (recommended for code organization)"
                )
            raw_references = document.get("references")
            if raw_references is not None:
                if not isinstance(raw_references, list):
                    raise ValueError("references must be an array")
                for reference_value in cast("list[object]", raw_references):
                    if not isinstance(reference_value, str):
                        raise ValueError("references entries must be strings")
                    if not known_reference(reference_value, assemblies):
                        errors.append(
                            f"[{relative}] Referenced assembly '{reference_value}' not found in repository"
                        )
            symbols: set[str] = set()
            packages: set[str] = set()
            declared[path.parent] = (path, symbols, packages)
            for define in version_defines(document):
                package = optional_string(define, "name")
                symbol = optional_string(define, "define")
                expression = optional_string(define, "expression")
                if is_blank(package):
                    errors.append(
                        f"[{relative}] versionDefines entry missing 'name' (package/module id)"
                    )
                if is_blank(symbol):
                    errors.append(
                        f"[{relative}] versionDefines entry for '{package}' missing 'define' symbol"
                    )
                else:
                    symbols.add(symbol)
                    if not is_blank(package):
                        packages.add(package)
                        symbol_packages.setdefault(symbol, set()).add(package)
                result = version_expression(expression)
                if not result.valid:
                    message = f"[{relative}] versionDefines '{symbol}' (package '{package}') has invalid expression '{expression}': {result.reason}"
                    if result.suggestion:
                        message += f" -- {result.suggestion}"
                    errors.append(message)
        except ValueError as error:
            errors.append(f"[{relative}] {error}")
    if symbol_packages:
        try:
            for path in sources[".cs"]:
                owner = next(
                    (declared[parent] for parent in path.parents if parent in declared), None
                )
                if owner is None:
                    continue
                text = path.read_text(encoding="utf-8-sig")
                text = re.sub(r"/\*.*?\*/", "", text, flags=re.DOTALL)
                text = re.sub(r"//.*$", "", text, flags=re.MULTILINE)
                used: set[str] = set()
                for expression in re.findall(r"^\s*#\s*(?:el)?if\b(.*)$", text, re.MULTILINE):
                    used.update(re.findall(r"[A-Za-z_][A-Za-z0-9_]*", expression))
                assembly_path, symbols, packages = owner
                for symbol in sorted(used.difference(symbols).intersection(symbol_packages)):
                    opened = packages.intersection(symbol_packages[symbol])
                    if opened:
                        package = sorted(opened)[0]
                        relative = assembly_path.relative_to(repository).as_posix()
                        message = f'[{relative}] code uses \'#if {symbol}\' but this asmdef declares other versionDefines for package \'{package}\' WITHOUT declaring \'{symbol}\'. Unity versionDefines are per-assembly, so \'{symbol}\' never fires here and the wrong #if branch compiles. Add: {{ "name": "{package}", "expression": "<min-version>", "define": "{symbol}" }}'
                        if message not in errors:
                            errors.append(message)
        except (OSError, UnicodeError) as error:
            errors.append(str(error))
    if verbose:
        print(
            f"[lint-asmdef] Files checked: {len(definitions) + len(references)}; Errors: {len(errors)}; Warnings: {len(warnings)}"
        )
    for warning in warnings:
        print(f"[lint-asmdef] WARNING: {warning}")
    for diagnostic in errors:
        print(f"[lint-asmdef] ERROR: {diagnostic}")
    if errors:
        print(
            f"[lint-asmdef] ERROR: Assembly Definition validation failed with {len(errors)} error(s)."
        )
        return 1
    print("[lint-asmdef] All Assembly Definition files are valid!")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, default=ROOT)
    parser.add_argument("--verbose", "-VerboseOutput", action="store_true")
    args = parser.parse_args()
    return check(args.repository.resolve(), verbose=args.verbose)


if __name__ == "__main__":
    sys.exit(main())
