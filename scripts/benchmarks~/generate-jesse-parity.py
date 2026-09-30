# MIT License - Copyright (c) 2026 wallstop
# Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

import argparse
import hashlib
import subprocess
from pathlib import Path


def generate_probe(
    baseline: str,
    candidate_ref: str | None,
    wide: bool,
    struct_comparer: bool,
    output: Path,
) -> Path:
    root: Path = Path(__file__).resolve().parents[2]
    source_path: str = "Runtime/Core/Extension/Sorting/IListSortJesse.cs"
    baseline_bytes: bytes = subprocess.check_output(
        ["git", "show", f"{baseline}:{source_path}"], cwd=root
    )
    candidate_bytes: bytes = (
        subprocess.check_output(
            ["git", "show", f"{candidate_ref}:{source_path}"], cwd=root
        )
        if candidate_ref
        else (root / source_path).read_bytes()
    )
    common_paths: list[Path] = [
        root / "Runtime/Core/Extension/Sorting" / name
        for name in (
            "IListSortIpn.cs",
            "IListSortShared.cs",
            "IListSortPatternDefeatingQuick.cs",
        )
    ]
    common_sources: list[str] = [path.read_text(encoding="utf-8") for path in common_paths]
    pieces: list[str] = []
    variants: tuple[tuple[str, bytes], ...] = (
        ("JesseBenchmarkOld", baseline_bytes),
        ("JesseBenchmarkCandidate", candidate_bytes),
    )
    for name, source_bytes in variants:
        source: str = source_bytes.decode("utf-8").replace("\r\n", "\n")
        for original in [source, *common_sources]:
            renamed: str = original.replace(
                "public static partial class IListExtensions",
                f"public static partial class {name}",
            ).replace("this IList<T> list", "IList<T> list")
            pieces.append(renamed)
        pieces.append(
            "namespace WallstopStudios.UnityHelpers.Core.Extension { "
            f"public static partial class {name} {{ "
            "private static void WriteBack<T>(System.Collections.Generic.IList<T> list, "
            "T[] data, int count) { for (int index = 0; index < count; ++index) "
            "{ list[index] = data[index]; } } } }"
        )
    template_directory: Path = Path(__file__).resolve().parent
    template_paths: list[Path] = [
        template_directory / ("JesseWideParityHarness.cs" if wide else "JesseParityHarness.cs")
    ]
    if wide:
        template_paths.append(template_directory / "WideRecordComparer.cs")
    template_bytes: bytes = b"".join(path.read_bytes() for path in template_paths)
    template: str = template_bytes.decode("utf-8")
    if struct_comparer:
        template = template.replace(
            "private static readonly IComparer<WideRecord> WideComparer",
            "private static readonly WideRecordComparer WideComparer",
        ).replace(
            "private sealed class WideRecordComparer",
            "private readonly struct WideRecordComparer",
        )
    hashes: dict[str, str] = {
        "CANDIDATE_SHA_PLACEHOLDER": hashlib.sha256(candidate_bytes).hexdigest(),
        "BASELINE_SHA_PLACEHOLDER": hashlib.sha256(baseline_bytes).hexdigest(),
        "HELPERS_SHA_PLACEHOLDER": hashlib.sha256(
            b"".join(path.read_bytes() for path in common_paths)
        ).hexdigest(),
        "HARNESS_SHA_PLACEHOLDER": hashlib.sha256(template.encode("utf-8")).hexdigest(),
    }
    for placeholder, digest in hashes.items():
        template = template.replace(placeholder, digest)
    pieces.append(template)
    output_path: Path = root / output
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text("\n".join(pieces), encoding="utf-8")
    print(f"candidate {hashes['CANDIDATE_SHA_PLACEHOLDER']}")
    print(f"baseline {hashes['BASELINE_SHA_PLACEHOLDER']}")
    print(f"probe {output_path}")
    return output_path


def main() -> None:
    parser: argparse.ArgumentParser = argparse.ArgumentParser(
        description="Generate a paired JesseSort Unity probe without modifying production sources."
    )
    parser.add_argument("--baseline", default="99f0520c", help="Previous implementation commit.")
    parser.add_argument("--candidate-ref", help="Candidate Git commit; defaults to the working tree.")
    parser.add_argument("--wide", action="store_true", help="Use 40-byte records instead of integers.")
    parser.add_argument("--struct-comparer", action="store_true", help="Use a concrete struct comparer with --wide.")
    parser.add_argument("--output", default="progress/.jesse-benchmark/JessePairedNative.cs")
    options: argparse.Namespace = parser.parse_args()
    if options.struct_comparer and not options.wide:
        parser.error("--struct-comparer requires --wide")
    generate_probe(
        str(options.baseline),
        str(options.candidate_ref) if options.candidate_ref else None,
        bool(options.wide),
        bool(options.struct_comparer),
        Path(str(options.output)),
    )


if __name__ == "__main__":
    main()
