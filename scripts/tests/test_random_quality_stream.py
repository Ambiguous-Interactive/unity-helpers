"""Preserve the standalone random streams and every original inventory and source control."""

from __future__ import annotations

import hashlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
import tomllib
from dataclasses import dataclass
from pathlib import Path
from typing import cast

import pytest

ROOT = Path(__file__).resolve().parents[2]
PROJECT = (
    ROOT
    / "Generator~/WallstopStudios.UnityHelpers.RandomQuality/WallstopStudios.UnityHelpers.RandomQuality.csproj"
)
SEED = "00010203-0405-0607-0809-0a0b0c0d0e0f"
NAMES = [
    "BlastCircuitRandom",
    "DotNetRandom",
    "FlurryBurstRandom",
    "IllusionFlow",
    "LinearCongruentialGenerator",
    "PcgRandom",
    "PhotonSpinRandom",
    "RomuDuo",
    "Sfc64Random",
    "SplitMix64",
    "SquirrelRandom",
    "StormDropRandom",
    "SystemRandom",
    "WaveSplatRandom",
    "WDoomRandom",
    "WyRandom",
    "XoroShiroRandom",
    "XorShiftRandom",
    "Xoshiro128StarStar",
    "Xoshiro256StarStar",
]
CAPTURE_LIMIT = 4 * 1024 * 1024


@dataclass(frozen=True)
class ProcessResult:
    status: int
    stdout: bytes
    stderr: bytes


@dataclass(frozen=True)
class Vector:
    generator: str
    width: int
    seed: str
    byte_count: int
    sha256: str
    first_hex: str

    @property
    def key(self) -> str:
        return f"{self.generator}/{self.width}/{self.seed}"

    @property
    def unity_cell(self) -> tuple[str, int, str, str]:
        return self.generator, self.width, self.seed, self.sha256


def object_map(value: object) -> dict[str, object]:
    assert isinstance(value, dict)
    assert all(isinstance(key, str) for key in cast("dict[object, object]", value))
    return cast("dict[str, object]", value)


def object_list(value: object) -> list[object]:
    assert isinstance(value, list)
    return cast("list[object]", value)


def read_json(path: Path) -> dict[str, object]:
    return object_map(json.loads(path.read_text(encoding="utf-8")))


def string(value: object) -> str:
    assert isinstance(value, str)
    return value


def integer(value: object) -> int:
    assert type(value) is int
    return value


def run_process(command: list[str], *, timeout: float = 30) -> ProcessResult:
    with tempfile.TemporaryFile() as output, tempfile.TemporaryFile() as error:
        completed = subprocess.run(
            command, cwd=ROOT, stdout=output, stderr=error, check=False, timeout=timeout
        )
        output.seek(0)
        error.seek(0)
        stdout, stderr = output.read(CAPTURE_LIMIT + 1), error.read(CAPTURE_LIMIT + 1)
    assert len(stdout) <= CAPTURE_LIMIT
    assert len(stderr) <= CAPTURE_LIMIT
    return ProcessResult(completed.returncode, stdout, stderr)


def assert_inventory(candidate: list[str], expected: list[str]) -> None:
    assert candidate == expected, (
        "the quality manifest must cover the live host --list inventory exactly"
    )


def assert_unity_vectors(
    candidate: list[tuple[str, int, str, str]], expected: list[tuple[str, int, str, str]]
) -> None:
    assert candidate == expected, (
        "Unity runtime tests must execute every frozen raw-stream hash with its original width and seed"
    )


def vectors() -> list[Vector]:
    document = read_json(ROOT / "scripts/random-quality/raw-stream-vectors.json")
    assert re.fullmatch("[0-9a-f]{40}", string(document["baselineCommit"]))
    result: list[Vector] = []
    for item in object_list(document["vectors"]):
        entry = object_map(item)
        result.append(
            Vector(
                string(entry["generator"]),
                integer(entry["width"]),
                string(entry["seed"]),
                integer(entry["bytes"]),
                string(entry["sha256"]),
                string(entry["first256BytesHex"]),
            )
        )
    return result


@pytest.fixture(scope="module")
def host() -> list[str]:
    dotnet = shutil.which("dotnet")
    assert dotnet is not None
    built = run_process(
        [dotnet, "build", str(PROJECT), "-c", "Release", "--nologo", "-v", "quiet"], timeout=180
    )
    assert built.status == 0, (built.stdout + built.stderr).decode(errors="replace")
    return [
        dotnet,
        str(PROJECT.parent / "bin/Release/net9.0/WallstopStudios.UnityHelpers.RandomQuality.dll"),
    ]


def test_proof_environment_policy() -> None:
    policy = (ROOT / ".github/workflows/local-gates.yml").read_text(encoding="utf-8")
    assert (
        "timeout 2m uv run --project scripts/python~ --locked --no-dev --group proof python scripts/random-quality/verify-bounded-sampling.py"
        in policy
    )
    settings = object_map(
        tomllib.loads((ROOT / "scripts/python~/pyproject.toml").read_text(encoding="utf-8"))
    )
    groups = object_map(settings["dependency-groups"])
    assert groups["proof"] == ["z3-solver==5.4.0.0"]

    def proto_defines(path: str) -> list[dict[str, object]]:
        return [
            entry
            for value in object_list(read_json(ROOT / path)["versionDefines"])
            if (entry := object_map(value))["define"] == "WALLSTOP_PROTO"
        ]

    runtime = proto_defines("Runtime/WallstopStudios.UnityHelpers.asmdef")
    assert len(runtime) == 1
    assert (
        proto_defines(
            "Tests/Runtime/Random/WallstopStudios.UnityHelpers.Tests.Runtime.Random.asmdef"
        )
        == runtime
    )


def test_host_inventory_and_manifest_controls(host: list[str]) -> None:
    listed = run_process([*host, "--list"])
    assert listed.status == 0, listed.stderr.decode(errors="replace")
    names = listed.stdout.decode().strip().splitlines()
    assert names == NAMES
    manifest = read_json(ROOT / "scripts/random-quality/expected-outcomes.json")
    manifest_names = [
        string(object_map(value)["name"]) for value in object_list(manifest["generators"])
    ]
    assert_inventory(manifest_names, names)
    for omitted in names:
        with pytest.raises(AssertionError, match="quality manifest must cover"):
            assert_inventory([name for name in manifest_names if name != omitted], names)
    with pytest.raises(AssertionError, match="quality manifest must cover"):
        assert_inventory([*manifest_names, "UnexpectedGenerator"], names)


def test_frozen_inventory_and_unity_controls() -> None:
    frozen = vectors()
    expected_keys = sorted(
        f"{name}/{width}/{seed}"
        for name in NAMES
        for width in [32, 64]
        for seed in [SEED, "12345678-1234-1234-1234-123456789012"]
    )
    assert sorted(vector.key for vector in frozen) == expected_keys
    source = (ROOT / "Tests/Runtime/Random/RandomRawStreamCompatibilityTests.cs").read_text(
        encoding="utf-8"
    )
    candidate = [
        (name, int(width), seed, digest)
        for name, width, seed, digest in re.findall(
            r'\[TestCase\(\s*nameof\((\w+)\),\s*(32|64),\s*"([^"]+)",\s*"([0-9a-f]{64})"\s*\)\]',
            source,
        )
    ]
    expected = [vector.unity_cell for vector in frozen]
    assert_unity_vectors(candidate, expected)
    first = candidate[0]
    for altered in [
        candidate[1:],
        [*candidate, first],
        [(first[0], 64, first[2], first[3]), *candidate[1:]],
        [(first[0], first[1], first[2], "0" * 64), *candidate[1:]],
    ]:
        with pytest.raises(AssertionError, match="Unity runtime tests must execute"):
            assert_unity_vectors(altered, expected)
    assert re.search(r"StreamBytes\s*=\s*1024\s*\*\s*1024\s*;", source)
    assert not re.search(r"SkipUnderIL2CPP|Assert\.Ignore|\[Ignore|#if", source)


@pytest.mark.parametrize("vector", vectors(), ids=[vector.key for vector in vectors()])
def test_frozen_raw_stream(host: list[str], vector: Vector) -> None:
    assert vector.byte_count == 1048576
    assert re.fullmatch("[0-9a-f]{64}", vector.sha256)
    assert re.fullmatch("[0-9a-f]{512}", vector.first_hex)
    raw = run_process(
        [
            *host,
            "--generator",
            vector.generator,
            "--seed",
            vector.seed,
            "--width",
            str(vector.width),
            "--bytes",
            str(vector.byte_count),
        ]
    )
    assert raw.status == 0, raw.stderr.decode(errors="replace")
    assert raw.stderr == b""
    assert len(raw.stdout) == vector.byte_count
    assert raw.stdout[:256] == bytes.fromhex(vector.first_hex)
    assert hashlib.sha256(raw.stdout).hexdigest() == vector.sha256


def method_body(source: str, open_brace: int) -> str:
    depth = 0
    for index in range(open_brace, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[open_brace + 1 : index]
    raise AssertionError("unterminated random draw method")


def test_hot_paths_retain_every_original_repair_guard() -> None:
    root = ROOT / "Runtime/Core/Random"
    expected = sorted([*NAMES, "NativePcgRandom", "UnityRandom"])
    discovered = sorted(
        path.stem
        for path in root.glob("*.cs")
        if "[RandomGeneratorMetadata(" in path.read_text(encoding="utf-8")
        or path.name == "NativePcgRandom.cs"
    )
    assert discovered == expected
    signature = r"\b(?:public|protected|private|internal)\s+(?:(?:static|virtual|override|unsafe|readonly|new)\s+)*[\w<>,.?\[\]]+\s+(Next\w*)\s*\([^;{}]*\)\s*\{"
    for generator in expected:
        source = (root / f"{generator}.cs").read_text(encoding="utf-8")
        methods = list(re.finditer(signature, source))
        assert methods, generator
        for match in methods:
            body = method_body(source, match.start() + match[0].rindex("{"))
            assert not re.search(r"\b(?:Ensure|Normalize|Repair|Initialize)\w*\s*\(", body), (
                f"{generator}.{match[1]}"
            )
    abstract = (root / "AbstractRandom.cs").read_text(encoding="utf-8")
    methods = list(
        re.finditer(
            r"\bpublic\s+(?:(?:static|virtual|override|unsafe|readonly|new)\s+)*[\w<>,.?\[\]]+\s+(Next\w*)\s*\([^;{}]*\)\s*\{",
            abstract,
        )
    )
    assert methods
    for match in methods:
        assert not re.search(
            r"\b(?:Repair|Normalize|Initialize)\w*\s*\(",
            method_body(abstract, match.start() + match[0].rindex("{")),
        )
    guid = re.search(r"private byte\[\] GenerateGuidBytes\(\)\s*\{", abstract)
    assert guid
    assert not re.search(
        r"\b(?:if\s*\(|new byte\s*\[|RepairCommonState\s*\()",
        method_body(abstract, guid.start() + guid[0].rindex("{")),
    )
    assert re.search(r"private void OnProtoDeserialize\(\)\s*\{\s*RepairCommonState\(\);", abstract)
    for name, forbidden in [("PhotonSpinRandom", r"_hasPrimed"), ("StormDropRandom", r"\bif\s*\(")]:
        source = (root / f"{name}.cs").read_text(encoding="utf-8")
        draw_match = re.search(r"public override uint NextUint\(\)\s*\{([\s\S]*?)\n\s*\}", source)
        assert draw_match
        assert not re.search(forbidden, draw_match[1])


@pytest.mark.parametrize("name", NAMES)
def test_short_unaligned_stream_is_seeded(host: list[str], name: str) -> None:
    arguments = [*host, "--generator", name, "--seed", SEED, "--bytes", "37"]
    first, second = run_process(arguments), run_process(arguments)
    assert first.status == second.status == 0
    assert first.stderr == b""
    assert len(first.stdout) == 37
    assert first.stdout == second.stdout


def test_published_pcg_and_photon_prefixes_and_seed_separation(host: list[str]) -> None:
    arguments = [*host, "--generator", "PcgRandom", "--seed", SEED, "--bytes", "257"]
    first, second = run_process(arguments), run_process(arguments)
    assert first.status == second.status == 0
    assert first.stderr == second.stderr == b""
    assert len(first.stdout) == 257
    assert first.stdout == second.stdout
    assert first.stdout[:8] == bytes.fromhex("98b0e0c0ca3eccd3")
    photon = run_process(
        [
            *host,
            "--generator",
            "PhotonSpinRandom",
            "--seed",
            "12345678-1234-1234-1234-123456789012",
            "--bytes",
            "64",
        ]
    )
    assert photon.status == 0
    assert photon.stdout == bytes.fromhex(
        "28e54e19a64cf2b2506af201132481cd02efe058c0930ad31200a2afba564f573ea9b29585deae825d66a48f515207d13e97b231b96534e39ed4b986bda3b097"
    )
    other = run_process(
        [
            *host,
            "--generator",
            "PcgRandom",
            "--seed",
            "10111213-1415-1617-1819-1a1b1c1d1e1f",
            "--bytes",
            "257",
        ]
    )
    assert other.status == 0
    assert first.stdout != other.stdout


def test_invalid_generator_keeps_binary_output_clean(host: list[str]) -> None:
    invalid = run_process([*host, "--generator", "NotAGenerator", "--bytes", "4"])
    assert invalid.status == 2
    assert invalid.stdout == b""


def test_process_failure_and_capture_limit_controls() -> None:
    result = run_process(
        [
            sys.executable,
            "-c",
            "import sys; sys.stdout.buffer.write(b'out\\n'); sys.stderr.buffer.write(b'err\\n'); sys.exit(2)",
        ]
    )
    assert result == ProcessResult(2, b"out\n", b"err\n")
    with pytest.raises(AssertionError):
        run_process(
            [
                sys.executable,
                "-c",
                f"import sys; sys.stdout.buffer.write(b'x' * {CAPTURE_LIMIT + 1})",
            ]
        )
    with pytest.raises(subprocess.TimeoutExpired):
        run_process([sys.executable, "-c", "import time; time.sleep(2)"], timeout=0.1)


def test_method_body_rejects_unterminated_input() -> None:
    with pytest.raises(AssertionError, match="unterminated random draw method"):
        method_body("{missing", 0)
