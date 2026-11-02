"""Keep the proof's production-expression parser typed and closed to other Python syntax."""

from __future__ import annotations

import ast
import importlib.util
import shutil
import subprocess
from pathlib import Path
from typing import Protocol, cast

import pytest
import z3

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "bounded_sampling", ROOT / "scripts/random-quality/verify-bounded-sampling.py"
)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("Missing bounded-sampling proof")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class SamplingProof(Protocol):
    def evaluate_expression(
        self, node: ast.expr, variables: dict[str, z3.BitVecRef]
    ) -> z3.BitVecRef | int: ...
    def production_multiply_high(
        self, source: str
    ) -> tuple[dict[str, z3.BitVecRef], dict[str, z3.BitVecRef], z3.BitVecRef]: ...
    def production_proofs(self, source: str) -> None: ...


PROOF = cast("SamplingProof", MODULE)


@pytest.mark.parametrize("expression", ["x + y", "x * y + 1", "narrow(x)", "shift(x, 32)"])
def test_supported_expression_matches_independent_symbolic_arithmetic(expression: str) -> None:
    first, second = z3.BitVec("x", 64), z3.BitVec("y", 64)
    expected = {
        "x + y": first + second,
        "x * y + 1": first * second + 1,
        "narrow(x)": first & 0xFFFFFFFF,
        "shift(x, 32)": z3.LShR(first, 32),
    }[expression]
    actual = PROOF.evaluate_expression(
        ast.parse(expression, mode="eval").body, {"x": first, "y": second}
    )
    assert isinstance(actual, z3.BitVecRef)
    solver = z3.Solver()
    solver.set(timeout=30000)
    solver.add(actual != expected)
    assert solver.check() == z3.unsat


@pytest.mark.parametrize(
    "expression",
    [
        "unknown",
        "x - y",
        "x ** 2",
        "x.real",
        "x[0]",
        "[x]",
        "str(x)",
        "narrow()",
        "narrow(1)",
        "narrow(x, y)",
        "shift(x, y)",
        "shift(x, n=2)",
        "shift(*[x, 1])",
        "(lambda: x)()",
        "True",
        "'x'",
    ],
)
def test_unsupported_syntax_is_refused_without_execution(expression: str) -> None:
    with pytest.raises(AssertionError, match="Unsupported production expression syntax"):
        PROOF.evaluate_expression(
            ast.parse(expression, mode="eval").body,
            {"x": z3.BitVec("x", 64), "y": z3.BitVec("y", 64)},
        )


@pytest.mark.parametrize(
    "body",
    [
        "return 0;",
        "ulong result = 7; return result;",
        "long result = x; return result;",
        "ulong result = unknown; return result;",
        "ulong result = x.real; return result;",
        "ulong result = x + y;",
    ],
)
def test_incomplete_or_unsupported_production_body_is_refused(body: str) -> None:
    source = "private static ulong MulHi64(ulong x, ulong y) {" + body + "}"
    with pytest.raises(AssertionError, match=r"Unsupported|Production"):
        PROOF.production_multiply_high(source)


def test_real_production_arithmetic_and_mutation_are_distinguished() -> None:
    source = (ROOT / "Runtime/Core/Random/AbstractRandom.cs").read_text(encoding="utf-8")
    PROOF.production_proofs(source)
    original = "ulong p00 = x0 * y0;"
    assert source.count(original) == 1
    with pytest.raises(AssertionError, match="counterexample"):
        PROOF.production_proofs(source.replace(original, "ulong p00 = x0 * y0 + 1;"))


@pytest.mark.parametrize(
    ("checker", "marker"), [("mypy", "arg-type"), ("pyright", "reportArgumentType")]
)
def test_symbolic_api_types_reject_a_plain_integer(
    checker: str, marker: str, tmp_path: Path
) -> None:
    executable = shutil.which(checker)
    assert executable is not None
    source = tmp_path / "bad_symbolic_argument.py"
    source.write_text("import z3\nz3.Solver().add(42)\n", encoding="utf-8")
    config = ROOT / "scripts/python~/pyproject.toml"
    arguments = ["--config-file", str(config)] if checker == "mypy" else ["--project", str(config)]
    result = subprocess.run(
        [executable, *arguments, str(source)],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )
    assert result.returncode != 0
    assert marker in result.stdout + result.stderr
