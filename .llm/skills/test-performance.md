# Skill: Test Performance Budgets

<!-- trigger: slow test, test performance, iteration count, sample count, benchmark, stress, category, assetdatabase refresh | Keep the main test suite fast: categorize/exclude perf tests, size samples statistically, batch asset I/O | Core -->

**Trigger**: When writing or reviewing tests that loop many times, measure throughput, sample randomness, or touch the AssetDatabase — and whenever lint reports UNH007/UNH008/UNH009.

---

## The fast suite vs. the benchmark job

CI runs the test matrix with the NUnit filter `UH_UNITY_TEST_CATEGORY="!Performance;!Stress"`, so anything
tagged `[Category("Performance")]` or `[Category("Stress")]` is **excluded from the main matrix** and runs
**only** in the dedicated `unity-benchmarks.yml` job (which opts those categories back in and sets
`UH_RANDOM_SAMPLE_COUNT` to the thorough value). Two consequences:

- A genuine benchmark / soak / huge-iteration test MUST carry `Performance` or `Stress`, or it slows every PR.
- A **correctness** test must stay fast and stay in the main suite — do NOT tag it `Performance`/`Stress` just
  to dodge a budget; reduce its cost instead.

| Category      | Meaning                                            | Runs in main matrix? |
| ------------- | -------------------------------------------------- | -------------------- |
| `Performance` | Throughput/allocation benchmark (Stopwatch/budget) | No (benchmark job)   |
| `Stress`      | Very high-iteration robustness/soak                | No (benchmark job)   |
| (none)        | Normal correctness test — must be fast             | Yes                  |

## Enforced rules (scripts/lint-tests.ps1)

- **UNH007** (blocking): a literal loop bound `>= 50,000` in a test that is NOT in a `Performance`/`Stress`
  fixture. Fix by reducing the count, moving the test into a perf fixture, or `// UNH-SUPPRESS` with a reason.
  (Const/field bounds like `< SampleCount` are intentionally not matched.)
- **UNH008** (blocking): a fixture that lives under a `Performance/` folder or is named `*PerformanceTests` /
  `*BenchmarkTests` MUST declare `[Category("Performance")]` (or `[Category("Stress")]`).
- **UNH009** (advisory, non-blocking): per-test `AssetDatabase.Refresh()` / `SaveAndReimport()` churns the
  importer. Prefer `BatchedEditorTestBase`. Reported every run so churn stays visible; it does not fail the
  build because converting a fixture to a batched base can change timing-dependent behaviour.

## Smarter coverage, not brute force

Big speedups come from sizing work to the goal, not from looping more:

1. **Statistically-sized sampling.** `Tests/Runtime/Random/RandomTestBase.cs` is the model: a fast default
   sample count for the main suite, an env-overridable thorough count (`UH_RANDOM_SAMPLE_COUNT`) for the
   benchmark job, and a `sqrt(average)` deviation FLOOR so reduced-N runs stay reliably green while large-N
   runs keep their original tighter sensitivity. A uniform-RNG sanity check needs far fewer samples than a
   brute-force loop — pick N from the statistic (chi-square sufficiency is ~1k–10k), don't default to millions.
2. **Parametrize + parallelize.** Convert independent brute-force iterations into `[TestCase]`/`[Values]` cases
   and mark pure, Unity-free tests `[Parallelizable]` — see [test-parallelization-rules](./test-parallelization-rules.md)
   for the hard constraint (Editor/Unity-object tests must NOT be parallelized).
3. **Avoid AssetDatabase churn.** Prefer in-memory assets (`Tests/Core/TextureTestHelper.cs`) over importing
   from disk; when you must touch assets, inherit `BatchedEditorTestBase` so a single refresh is deferred to
   `OneTimeTearDown` instead of one per test.
4. **EditMode-first.** Pure C# logic belongs in EditMode (no Play-Mode domain reload). Reserve PlayMode for
   runtime-only behaviour (physics, coroutines, Update).

## Checklist before adding a heavy test

- [ ] Is this measuring performance/throughput? → `[Category("Performance")]`.
- [ ] Is it a high-iteration soak/robustness run? → `[Category("Stress")]` (and consider an env-overridable count).
- [ ] Is it a correctness test? → keep it fast: size the loop to what the assertion actually needs (`< 50,000`),
      or reduce data size; do not tag it perf to dodge UNH007.
- [ ] Touching assets? → inherit `BatchedEditorTestBase` and/or use in-memory `TextureTestHelper`.
- [ ] Run `pwsh -NoProfile -File scripts/lint-tests.ps1` — zero UNH007/UNH008, and review UNH009 advisories.

## Related Skills

- [test-parallelization-rules](./test-parallelization-rules.md) — when `[Parallelizable]` is allowed.
- [unity-performance-patterns](./unity-performance-patterns.md) — allocation/perf patterns in runtime code.
- [test-data-driven](./test-data-driven.md) — `[TestCase]`/`[ValueSource]` parametrization.
- [unity-devcontainer-testing](./unity-devcontainer-testing.md) — running the suite locally.
