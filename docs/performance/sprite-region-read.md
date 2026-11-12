# Sprite rectangle pixel copies

Readable RGBA32 sprite queries now copy the selected rectangle from the base mip,
rather than copying the entire texture. The intermediate pixel array holds only
that rectangle. Pixel colours, alpha policy, pivots and units per pixel retain
their existing meaning. The managed copy owns its pixels; changing it does not
change the texture. Full-texture reads and other readable formats retain their
existing conversion path.

Three fresh-process pairs on Windows x64 Unity 6000.6.7f1 Editor Mono measure the
public alpha-mask and local-art-bounds APIs. Every input is fully opaque RGBA32.
Each workload receives 64 warm calls, calibration to at least 20 ms and 21 raw
rounds. The second pair reverses process order. Table medians are medians of the
three process medians.

| API and rectangle            | Before median | After median | Paired after/before range |
| ---------------------------- | ------------: | -----------: | ------------------------: |
| Mask, 16×16 in 1024×1024     |    3048.33 µs |      4.08 µs |             0.0009–0.0020 |
| Mask, 256×256 in 1024×1024   |    4124.69 µs |    418.16 µs |             0.0975–0.1123 |
| Mask, full 32×32 texture     |      10.28 µs |      9.56 µs |             0.4968–1.2739 |
| Bounds, 16×16 in 1024×1024   |    3148.91 µs |      3.60 µs |             0.0007–0.0011 |
| Bounds, 256×256 in 1024×1024 |    2693.38 µs |    176.66 µs |             0.0601–0.0714 |
| Bounds, full 32×32 texture   |       4.41 µs |      4.19 µs |             0.8962–0.9728 |

Both subrectangle workloads improve in every pair. The small full-texture mask
control varies, including one slower candidate process; it establishes no stable
speed improvement. These observations cover texture retrieval and each public
query, but exclude texture creation and sprite creation. They do not qualify
Editor source-PNG decoding, other texture formats, packed sprites, IL2CPP or
WebGL performance. They are not total allocation-byte measurements.

Four native storage regressions fail against the original implementation and pass
against the candidate. They check returned array length, nonzero source offsets,
base-mip selection, exact colours, pivot mapping and independence from the texture.
Three other-format controls retain converted-pixel semantics. The focused native
correctness cohort passes 186 cases without skips. Each accepted timing process
passes its six cases, returns raw exit zero and reaches both output stream ends
without a drain stop. Source hashes remain stable within each process. Loaded
module identities and production assembly hashes distinguish the two variants.

The [replay archive](./sprite-region-read.zip) contains the two implementations,
timing and correctness sources, all six complete NUnit trees, all 21-round output
sets, process receipts, credential-checked logs, a hash inventory, analysis and the
native controller. Its README records the base checkout and replay steps. The final XML-doc addition changes no executable statements in the measured reader. New
replays receive their own source, assembly and process identities. A preliminary
launch selected the normal Runtime assembly and ran zero cases; the runner
rejected it and it contributes no accepted observation.
