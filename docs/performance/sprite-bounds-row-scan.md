# Sprite bounds row scans

`SpriteArtBounds.TryGetLocalArtBounds` scans each row from its two edges. Once it finds
both visible ends, interior pixels cannot change that row's bounds. It preserves the
same alpha thresholds, pivots, transparent result and coordinate calculation.

On Windows x64 Unity 6000.6.7f1 Editor Mono, three alternating baseline/candidate
process pairs measured 256×256 inputs. Each process warmed 64 calls, calibrated
batches to at least 20 ms, and retained 21 rounds for each workload.
Times below are medians of process medians. Ratios show the range of the three
paired process ratios; lower is faster.

| Input         | API             | Before, µs | After, µs | Paired after / before |
| ------------- | --------------- | ---------: | --------: | --------------------: |
| Opaque        | Existing mask   |      38.02 |      0.54 |         0.0136–0.0148 |
| Single column | Existing mask   |      25.86 |     26.06 |           0.977–1.050 |
| Transparent   | Existing mask   |      22.90 |     24.49 |           1.063–1.087 |
| Opaque        | Readable sprite |     295.79 |     72.14 |           0.240–0.379 |
| Single column | Readable sprite |     276.78 |    259.57 |           0.875–1.006 |
| Transparent   | Readable sprite |     258.46 |    255.15 |           0.982–1.457 |

Dense inputs benefit most. Transparent cached masks were 6–9% slower; the
transparent sprite measurements varied enough to prevent a stable speed claim.
The sprite path still copies texture pixels. A separate controlled allocation
check recorded 32 allocation events for 32 direct queries and 64 when explicitly
building masks first. These event counts do not measure bytes or retained memory.

All six timing processes passed six shape checks, exited zero, ended both output
streams, and retained matching source and loaded-method identities. The existing
bounds fixture plus 72 new pixel-oracle cases passed all 116 cases. No IL2CPP,
other editor version, arbitrary texture size or universal timing margin is claimed.

[Replay sources, raw rounds and receipts](https://raw.githubusercontent.com/Ambiguous-Interactive/unity-helpers/587d3de6fde16c76a0ba256872cfe8c800bf1287/docs/performance/sprite-bounds-row-scan.zip) retain every
sample and identify both source variants. Earlier runs whose NUnit progress output
was discarded remain excluded from the timing result.
