#!/usr/bin/env python3
"""Exhaustive reduced-width sampling and executable arithmetic proofs for issue #638.

Requires a C++17 compiler and z3-solver. Production arithmetic is interpreted from
AbstractRandom.cs; unsupported source syntax fails instead of proving a stale model.
"""

# cspell:words cout endl unsat BitVec BitVecVal ULE

import argparse
import contextlib
import io
import pathlib
import re
import subprocess
import tempfile

import z3

ROOT = pathlib.Path(__file__).resolve().parents[2]
EXHAUSTIVE = r"""
#include <algorithm>
#include <cstdint>
#include <iostream>
#include <vector>

int main() {
    uint64_t subjects = 0;
    bool moduloKilled = false, thresholdKilled = false, highKilled = false;
    for (unsigned width : {8u, 16u}) {
        const uint64_t domain = uint64_t{1} << width;
        const uint64_t mask = domain - 1;
        std::vector<uint64_t> buckets(domain);
        for (uint64_t bound = 1; bound < domain; ++bound) {
            const uint64_t threshold = ((domain - bound) & mask) % bound;
            if (threshold != domain % bound) return 1;
            std::fill(buckets.begin(), buckets.begin() + bound, 0);
            const bool powerOfTwo = (bound & (bound - 1)) == 0;
            for (uint64_t word = 0; word < domain; ++word) {
                ++subjects;
                const uint64_t product = word * bound;
                const uint64_t low = product & mask;
                const uint64_t quotient = product / domain;
                const bool accepted = threshold <= low;
                if (width == 8 && !powerOfTwo) {
                    moduloKilled |= accepted && word % bound != quotient;
                    thresholdKilled |= accepted != (threshold + 1 <= low);
                    highKilled |= accepted && (product >> (width - 1)) != quotient;
                }
                if (!accepted) continue;
                const uint64_t result = powerOfTwo ? word & (bound - 1) : product >> width;
                const uint64_t oracle = powerOfTwo ? word % bound : quotient;
                if (result != oracle || bound <= result) return 2;
                ++buckets[result];
            }
            for (uint64_t bucket = 0; bucket < bound; ++bucket) {
                if (buckets[bucket] != domain / bound) return 3;
            }
        }
        std::cout << "exhaustive width=" << width << " bounds=" << mask << std::endl;
    }
    if (!moduloKilled || !thresholdKilled || !highKilled) return 4;
    std::cout << "subjects=" << subjects << " arithmetic-mutants=3/3 killed" << std::endl;
}
"""


def prove(name, counterexample, *assumptions):
    solver = z3.Solver()
    solver.set(timeout=60000)
    solver.add(*assumptions, counterexample)
    outcome = solver.check()
    if outcome == z3.sat:
        raise AssertionError(f"{name}: counterexample {solver.model()}")
    if outcome != z3.unsat:
        raise RuntimeError(f"{name}: {outcome}; {solver.reason_unknown()}")
    print(f"proved {name}", flush=True)


def production_multiply_high(source):
    match = re.search(
        r"private static ulong MulHi64\(ulong x, ulong y\)\s*\{([^}]+)\}", source
    )
    if match is None:
        raise AssertionError("Production MulHi64 body was not found")
    variables = {name: z3.BitVec(name, 64) for name in ("x", "y")}
    partials = {}
    for statement in match[1].strip().split(";"):
        statement = statement.strip()
        if not statement:
            continue
        if statement.startswith("return "):
            return variables, partials, variables[statement[7:].strip()]
        declaration = re.fullmatch(r"ulong (\w+) = (.+)", statement)
        if declaration is None:
            raise AssertionError(f"Unsupported production statement: {statement}")
        name, expression = declaration.groups()
        expression = re.sub(r"\(uint\)(\w+)", r"narrow(\1)", expression)
        expression = re.sub(r"(\w+) >> (\d+)", r"shift(\1, \2)", expression)
        if not re.fullmatch(r"[\w\s()+*,]+", expression):
            raise AssertionError(f"Unsupported production expression: {expression}")
        variables[name] = eval(
            expression,
            {"__builtins__": {}},
            {
                **variables,
                "narrow": lambda value: value & z3.BitVecVal(0xFFFFFFFF, 64),
                "shift": z3.LShR,
            },
        )
        if name in ("p00", "p01", "p10", "p11"):
            partials[name] = variables[name]
    raise AssertionError("Production MulHi64 return was not found")


def proofs():
    for width in (8, 16, 32, 64):
        bound = z3.BitVec(f"bound{width}", width)
        word = z3.BitVec(f"word{width}", width)
        wide_bound = z3.ZeroExt(1, bound)
        prove(
            f"unsigned wrap {width}",
            z3.ZeroExt(1, -bound) != (1 << width) - wide_bound,
            bound != 0,
        )
        product = z3.ZeroExt(width, word) * z3.ZeroExt(width, bound)
        # Widened unsigned multiplication cannot reach bound * 2**width.
        # State its monotonic premise over integer operands; no bit-vector
        # multiplication solver is asked to rediscover nonlinear arithmetic.
        integer_word, integer_bound = z3.Ints(
            f"integer_word{width} integer_bound{width}"
        )
        prove(
            f"result bounds {width}",
            integer_word * integer_bound / (1 << width) >= integer_bound,
            0 <= integer_word,
            integer_word < 1 << width,
            0 < integer_bound,
            integer_bound < 1 << width,
        )
        prove(
            f"product decomposition {width}",
            product
            != z3.Concat(
                z3.Extract(2 * width - 1, width, product),
                z3.Extract(width - 1, 0, product),
            ),
        )
    divisor = z3.Int("divisor")
    prove(
        "threshold remainder",
        ((1 << 64) - divisor) % divisor != (1 << 64) % divisor,
        0 < divisor,
    )
    production_proofs((ROOT / "Runtime/Core/Random/AbstractRandom.cs").read_text())


def production_proofs(source):
    variables, partials, actual = production_multiply_high(source)
    x, y = variables["x"], variables["y"]
    expected_limbs = {
        "x0": x & 0xFFFFFFFF,
        "x1": z3.LShR(x, 32),
        "y0": y & 0xFFFFFFFF,
        "y1": z3.LShR(y, 32),
    }
    for name, expected in expected_limbs.items():
        prove(f"production limb {name}", variables[name] != expected)
    for name, first, second in (
        ("p00", "x0", "y0"),
        ("p01", "x0", "y1"),
        ("p10", "x1", "y0"),
        ("p11", "x1", "y1"),
    ):
        prove(
            f"production partial product {name}",
            partials[name] != expected_limbs[first] * expected_limbs[second],
        )
    replacements = {name: z3.BitVec(f"abstract_{name}", 64) for name in partials}
    abstract_actual = z3.substitute(
        actual, *[(value, replacements[name]) for name, value in partials.items()]
    )
    if "x" in str(abstract_actual) or "y" in str(abstract_actual):
        raise AssertionError("Production partial products were not abstracted")
    p00, p01, p10, p11 = [replacements[name] for name in ("p00", "p01", "p10", "p11")]
    product = (
        z3.ZeroExt(64, p00)
        + (z3.ZeroExt(64, p01) << 32)
        + (z3.ZeroExt(64, p10) << 32)
        + (z3.ZeroExt(64, p11) << 64)
    )
    maximum_partial = ((1 << 32) - 1) ** 2
    prove(
        "production MulHi64 carry reconstruction",
        abstract_actual != z3.Extract(127, 64, product),
        *[z3.ULE(value, maximum_partial) for value in replacements.values()],
    )
    x0, x1, y0, y1 = z3.Ints("x0 x1 y0 y1")
    expanded = x0 * y0 + (x0 * y1 + x1 * y0) * (1 << 32) + x1 * y1 * (1 << 64)
    prove(
        "limb product identity",
        z3.simplify((x0 + x1 * (1 << 32)) * (y0 + y1 * (1 << 32)) - expanded, som=True)
        != 0,
    )


def production_mutants():
    source = (ROOT / "Runtime/Core/Random/AbstractRandom.cs").read_text()
    mutants = (
        ("partial product", "ulong p00 = x0 * y0;", "ulong p00 = x0 * y0 + 1;"),
        ("limb extraction", "ulong x1 = x >> 32;", "ulong x1 = x >> 31;"),
        ("carry shift", "(middle >> 32)", "(middle >> 31)"),
    )
    for name, original, replacement in mutants:
        if source.count(original) != 1:
            raise AssertionError(f"Production mutant {name} has no unique subject")
        try:
            with contextlib.redirect_stdout(io.StringIO()):
                production_proofs(source.replace(original, replacement))
        except AssertionError as error:
            if ": counterexample " not in str(error):
                raise
            print(f"killed production mutant: {name}", flush=True)
        else:
            raise AssertionError(f"Production mutant survived: {name}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--proofs-only", action="store_true")
    args = parser.parse_args()
    print(f"z3={z3.get_version_string()}", flush=True)
    proofs()
    production_mutants()
    if not args.proofs_only:
        with tempfile.TemporaryDirectory(prefix="bounded-random-proof-") as directory:
            source = pathlib.Path(directory) / "exhaustive.cpp"
            executable = pathlib.Path(directory) / "exhaustive"
            source.write_text(EXHAUSTIVE)
            subprocess.run(
                ["c++", "-std=c++17", "-O3", str(source), "-o", str(executable)],
                check=True,
            )
            subprocess.run([str(executable)], check=True)


if __name__ == "__main__":
    main()
