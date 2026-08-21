# Random quality byte stream

This host turns a package `IRandom` implementation into deterministic little-endian `uint` bytes for
PractRand, TestU01 adapters, or another external statistical battery. It is test infrastructure under
`Generator~`; Unity ignores it and it is not included in the package.

```bash
dotnet run --project Generator~/WallstopStudios.UnityHelpers.RandomQuality \
  -c Release -- \
  --generator PcgRandom \
  --seed 00010203-0405-0607-0809-0a0b0c0d0e0f \
  --bytes 1073741824 \
  | RNG_test stdin32
```

Use `--list` for the explicit generator inventory. `UnityRandom` is deliberately absent because its
state belongs to a live Unity player; the standalone host cannot reproduce that engine-owned stream.
`NativePcgRandom` is deliberately absent because it is a Unity-native value type rather than an
`IRandom` implementation. Unity runtime tests cover its numeric behavior; this host exposes managed
streams that external batteries can consume without Unity collections.
The weak generators remain in the inventory as expected-failure controls.

## Scheduled battery runs

[`.github/workflows/random-quality.yml`](../../.github/workflows/random-quality.yml) runs this host
against PractRand on a weekly schedule and on manual dispatch. It is never a pull-request gate: a
statistical battery takes minutes per generator and its verdict is a judgement, so the fast
deterministic guard is the per-PR bit-plane linearity test in
`Tests/Runtime/Random/GeneratorBitPlaneLinearityTests.cs` instead.

The workflow pins PractRand by archive URL and SHA-256, builds it from source, caches the build, runs
every name from `--list` for a dispatch-selectable byte budget, records the exact command and seed
next to each report, and uploads the complete per-generator output as an artifact. It then compares
every outcome against
[`scripts/random-quality/expected-outcomes.json`](../../scripts/random-quality/expected-outcomes.json)
and opens or updates a GitHub issue when they disagree.

That manifest is the source of truth for what each generator is expected to do. It records, per
generator, whether it should pass or fail, why, and -- for the expected-failure controls -- `failsBy`,
the smallest length at which a definitive `FAIL` was actually measured. Every generator expected to
pass carries the mirror of that, `cleanThrough`: the largest length at which a clean run was actually
observed. **A pass is only evidence at a depth where something worse would have been caught**, and
`SystemRandom` -- rated `Poor` here -- is clean through 4GB, so the contract test refuses a
`cleanThrough` shallower than the deepest control's `failsBy`. That is currently 8GB, which is also
the workflow's default budget. The threshold matters: a
control that passes is only evidence of a broken harness when the run was long enough to have reached
the length where its failure is known to appear. `SystemRandom`, for instance, survives 4GB and only
fails at 8GB, so a 1GB run reports it as inconclusive rather than as a fault. `DotNetRandom` carries a
null `failsBy` because it reflects into the host `System.Random` and has never been caught inside
16GB, so it is never asserted at all.

`scripts/tests/test-random-quality-outcomes.mjs` (`npm run test:random-quality-outcomes`) is the
contract test for the manifest and the report parser.
