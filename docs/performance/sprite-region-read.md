# Sprite rectangle pixel copies

Readable RGBA32 sprite queries now copy the selected rectangle from the base mip,
rather than copying the entire texture. The intermediate pixel array holds only
that rectangle. Pixel colours, alpha policy, pivots and units per pixel retain
their existing meaning. The managed copy owns its pixels; changing it does not
change the texture. Full-texture reads retain their existing conversion path. RGB24 subregions are covered by
the [later comparison](#rgb24-subregions); other readable formats keep their conversion path.

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

## RGB24 subregions

Readable RGB24 subregions now decode only selected base-mip RGB bytes into an owned
region-sized `Color32` array, with opaque alpha. Full-texture reads retain their conversion path.

Three new serial fresh-process pairs on Windows x64 Unity 6000.6.7f1 Editor Mono include
both RGB24 workloads and RGBA32 controls. Each cell has 64 warm calls, calibration to at least
20 ms and 21 raw samples; the second pair reverses process order. Medians below are medians
of three process medians. These observations are separate from the earlier RGBA32 campaign.

| Format | API and rectangle            | Before median | After median | Paired after/before range |
| ------ | ---------------------------- | ------------: | -----------: | ------------------------: |
| RGB24  | Mask, 32×32 in 32×32         |     11.124 µs |     5.331 µs |             0.4793–0.9171 |
| RGB24  | Bounds, 32×32 in 32×32       |      5.479 µs |     4.777 µs |             0.3627–0.9506 |
| RGB24  | Mask, 16×16 in 1024×1024     |   3557.488 µs |     4.180 µs |             0.0011–0.0016 |
| RGB24  | Bounds, 16×16 in 1024×1024   |   3759.325 µs |     4.732 µs |             0.0012–0.0014 |
| RGB24  | Mask, 256×256 in 1024×1024   |   4124.350 µs |  1076.925 µs |             0.2565–0.2726 |
| RGB24  | Bounds, 256×256 in 1024×1024 |   3643.050 µs |   633.947 µs |             0.1554–0.1740 |
| RGBA32 | Mask, 32×32 in 32×32         |      8.449 µs |     8.356 µs |             0.8170–1.4262 |
| RGBA32 | Bounds, 32×32 in 32×32       |      4.207 µs |     4.658 µs |             0.9210–2.9445 |
| RGBA32 | Mask, 16×16 in 1024×1024     |      4.377 µs |     4.071 µs |             0.8709–1.3538 |
| RGBA32 | Bounds, 16×16 in 1024×1024   |      3.515 µs |     3.299 µs |             0.6621–0.9978 |
| RGBA32 | Mask, 256×256 in 1024×1024   |    440.103 µs |   455.480 µs |             0.7707–1.3204 |
| RGBA32 | Bounds, 256×256 in 1024×1024 |    200.565 µs |   212.867 µs |             0.7556–1.1629 |

All four RGB24 subregion cells improve in every pair. The full-texture and existing RGBA32
paths vary, including slower candidate observations, and establish no stable improvement.
The work excludes texture/sprite creation. It does not qualify packed sprites, Editor source-PNG
decoding, other formats, IL2CPP, WebGL or total allocated bytes.

Eight native storage cases cover RGB24/RGBA32 returned array length, offsets, corners, mip
selection, exact colours, pivot mapping and source independence. Before the RGB24 path, its four
storage cases fail while the four RGBA32 cases pass; all eight pass afterward. The broader
sprite correctness cohort passes 190 cases. All six accepted timing processes pass all 12 cases,
return raw exit zero and reach both stream ends without a drain stop. Source and production
assembly hashes stay stable per arm; loaded runtime module IDs are distinct between arms.

The [native replay archive](./native-runtime-qualification.zip) retains the frozen source snapshot,
both reader variants, all six complete NUnit trees with every raw sample, loaded identities,
process receipts, controllers and analysis. An earlier campaign overlapped a host benchmark and
is excluded from timing acceptance; its raw captures remain retained separately.

## Other byte formats

Readable ARGB32, BGRA32 and Alpha8 subregions also keep only their selected base-mip pixels.
Four offsets and sizes per format exercise corners, nonzero offsets and mipmapped inputs.
All twelve storage controls fail against the earlier full-texture path. The updated reader
matches Unity's `GetPixels32()` conversion and owns a region-sized copy; changing it cannot
change the source texture. Full-texture and other-format conversion paths keep their behavior.

ARGB32 and BGRA32 bytes follow Unity's documented
[ARGB32](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/TextureFormat.ARGB32.html)
and [BGRA32](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/TextureFormat.BGRA32.html)
channel order. Alpha8 reads preserve the CPU conversion's black RGB channels and stored alpha;
this is checked against `GetPixels32()`, independently of shader sampling conventions.
The first candidate used white RGB and failed all five Alpha8 conversion controls. Correcting
the conversion preserves those assertions. The final local Unity 6000.6.7f1 PlayMode cohort
passes 224 cases, with zero failures and 72 existing skips. It exits zero with both streams
complete and stable source hashes. This establishes scoped storage and conversion behavior,
not new timing or allocation-byte measurements for these formats.

The [additional replay archive](./local-pool-runtime-qualification.zip) retains the new format
correctness results, the initial Alpha8 mismatch and the original reader source.
