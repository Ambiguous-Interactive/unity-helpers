# JSON Codegen Performance

## TL;DR: A Generated Converter Beats the Reflection Path by 1.4x to 3.0x

- On a ten-member save-game record, a hand-written `JsonConverter<T>` that writes and reads members
  one at a time is about **2.0x faster to serialize** and **3.0x faster to deserialize** than
  System.Text.Json's reflection path through the same package options.
- The margin narrows as the payload grows but does not close: the same record carrying a
  256-element `List<int>` is still about **1.4x-1.6x** to serialize and **1.6x-1.7x** to deserialize.
- **The package's option profile is not what makes the difference.** Turning off
  `ReferenceHandler.IgnoreCycles`, case-insensitive property matching and string enums -- the whole
  gap between `CreateNormalJsonOptions()` and `CreateFastJsonOptions()` -- moves the ratio by at
  most 0.15x. What codegen removes is per-member metadata dispatch, and that survives every option.
- Every number here is a **floor**, for the reason given under
  [Why these are lower bounds](#why-these-are-lower-bounds).

## The Measurement

The fixture is `Tests/Runtime/Performance/JsonCodegenPerformanceTests.cs`. It exists because
[#561](https://github.com/Ambiguous-Interactive/unity-helpers/issues/561) asks for a Roslyn JSON
generator "only if it was a measured win", so the number has to come before the design.

**The record.** Ten members shaped like a save slot rather than a micro-type: `int`, `float`,
`bool`, `string`, an enum, a `Vector3`, a `List<int>`, a `long`, a second `int`, and a nested object
of three more members. Two sizes, differing only in the list: 4 elements and 256.

**The reference** is that record through the package's own shipped options -- `CreateNormalJsonOptions()`
and `CreateFastJsonOptions()` -- which is what a consumer gets today.

**The subject** is the same record through a hand-written `JsonConverter<T>` registered ahead of the
package converters: cached UTF-8 property names, one member written or read at a time, the enum
resolved by a `switch`, no metadata lookup anywhere. That is the code a source generator emits, so
it is the honest upper bound on what codegen could buy -- a real generator cannot beat it and would
pay more than it in generality.

**Before anything is timed**, the two sides are held to byte-identical output _and_ each is made to
read the other's payload back into an equal object graph. A benchmark of two things that disagree is
not a benchmark.

**The protocol** is the repository's counterbalanced `ABBABAAB` protocol
(`WallstopStudios.UnityHelpers.Tests.Core.BenchmarkProtocol`), three batches, twelve raw cycles per
side, a settled heap before every slot, geometric-mean ratio, per-cycle spread retained. A workload
whose spread exceeds 3% is logged but not published, because a wider one is a reading of the
machine.

Measured on Unity **6000.4.6f1**, Editor Mono, **2026-08-29**, on a machine that was also serving
another editor session -- which is why most cells exceeded the 3% publication gate on the first two
runs even though the ratios themselves reproduced. Three independent runs:

| Workload                |   Run 1 |   Run 2 |   Run 3 | Worst spread seen |
| ----------------------- | ------: | ------: | ------: | ----------------: |
| Normal serialize, 4     | 2.0900x | 2.1168x | 2.0814x |              6.4% |
| Normal deserialize, 4   | 3.0124x | 3.0559x |   3.00x |              3.8% |
| Normal serialize, 256   | 1.5643x | 1.5477x | 1.5372x |              4.7% |
| Normal deserialize, 256 | 1.6889x | 1.6499x | 1.6614x |             11.5% |
| Fast serialize, 4       | 2.0027x | 2.0028x | 2.0026x |              6.0% |
| Fast deserialize, 4     | 3.0161x | 3.0054x |   3.00x |              4.2% |
| Fast serialize, 256     | 1.4040x | 1.4100x |   1.41x |              4.6% |
| Fast deserialize, 256   | 1.6676x | 1.6676x |   1.64x |              7.1% |

The run-to-run agreement is the part worth trusting: no cell moved more than 2.4% between runs, and
`Fast serialize, 4` landed within 0.0002x of itself three times. The spreads say the machine was
busy; the ratios say the machine's business landed on both sides equally, which is what the
counterbalanced order is for.

One, one, and then four of the eight cells came inside the 3% gate on the three runs. The quietest
run published this table, which is the fixture's own output verbatim:

| Workload                 | Ratio | Reference Spread | Subject Spread |
| ------------------------ | ----: | ---------------: | -------------: |
| Normal deserialize small |  3.00 |           0.0218 |         0.0181 |
| Fast deserialize small   |  3.00 |           0.0228 |         0.0280 |
| Fast serialize large     |  1.41 |           0.0269 |         0.0215 |
| Fast deserialize large   |  1.64 |           0.0290 |         0.0223 |

A cell the gate rejects is still logged, ratio and both spreads included, so a busy machine costs
the table a row rather than costing the run its evidence.

## Why These Are Lower Bounds

The test assembly's `overrideReferences` list does not carry `System.Text.Encodings.Web`, and
building a pre-escaped `JsonEncodedText` needs `JavaScriptEncoder` from it. The subject therefore
hands the writer raw UTF-8 name spans, which are re-scanned for characters needing escapes on every
single property write -- work a generated converter would have done once at compile time. That cost
lands on the subject, so every ratio above understates what codegen would actually deliver.

## Reading the Result

Two things follow, and they point in different directions.

**Serialization is largely already solved, for free.** System.Text.Json ships its own source
generator (`[JsonSerializable]` on a `JsonSerializerContext`), and in serialization mode it emits a
fast path shaped almost exactly like this fixture's subject: members written one at a time against
pre-encoded names. A game that annotates its hot save models gets most of the serialize column
without this package generating anything. That path has **not** been measured here -- Unity does not
run System.Text.Json's generator against package sources -- so treat it as documented behavior
rather than as a number.

**Deserialization is where the unclaimed margin is.** System.Text.Json's generator emits metadata,
not a hand-written reader, for the read path; it removes reflective member access but still runs the
generic object converter's property loop. The 3.0x measured here on the small record is the space a
bespoke reader could claim, and it is the largest single number in the table.

## Practical Rules

- **Do not reach for a custom converter on a model you serialize occasionally.** A save file written
  once a minute does not care about 2x. The numbers above are per-call ratios on a hot loop.
- **Do reach for one on a model in a per-frame or per-message path** -- network payloads, replay
  frames, telemetry batches. `JsonSerializerOptions.Converters` takes yours ahead of the package's,
  and `Serializer.CreateNormalJsonOptions()` returns an instance you own and can add to.
- **Cache the options instance.** Both sides here reuse one; a fresh `JsonSerializerOptions` per call
  discards System.Text.Json's metadata caches and swamps everything this page measures.
- **Do not pick `CreateFastJsonOptions()` expecting this margin.** It is worth having for its own
  reasons, but against a generated converter it moves the ratio by at most 0.15x.

## Caveats

- Editor Mono only. IL2CPP has not been re-measured, and its inlining of the reflection path's
  cached delegates differs from Mono's.
- The record is one shape. A model that is mostly strings, or mostly a dictionary, will not divide
  the same way -- the 256-element list row is the evidence that composition moves the answer.
- The fixture reports; it does not gate. Nothing in CI fails on these ratios.
