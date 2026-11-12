# Sprite ink-region traversal

The ink-region query now remembers pixels rejected during connectivity traversal. Each pixel
is decoded once in that traversal, rather than again when another neighbor or seed reaches it.
Colour counts, opaque-share filtering, four-connected regions, tie order and local coordinates
keep their previous behavior. Source pixels are not modified.

Three alternating baseline/candidate process pairs on Windows x64 Unity 6000.6.7f1 Editor Mono
measure the internal pixel-region query with 256 × 256 inputs. Each capture uses 64 warm calls,
batches calibrated to at least 20 ms, and 21 recorded rounds per pattern. Centres have independent
analytic expectations. The timed loop also checks successful results and their centres.

| Pattern                                        | Before median | After median | Paired after/before range |
| ---------------------------------------------- | ------------: | -----------: | ------------------------: |
| Solid black                                    |      2.563 ms |     2.564 ms |               0.981–1.036 |
| Black/white checkerboard                       |      3.757 ms |     2.528 ms |               0.611–0.685 |
| One black row on white                         |      2.371 ms |     2.373 ms |               0.971–1.004 |
| One opaque black pixel, translucent background |      0.608 ms |     0.617 ms |               0.998–1.026 |

Table medians are medians of the three process medians. The checkerboard improves in every pair;
the other patterns include small regressions. These measurements exclude texture retrieval and
do not establish an end-to-end sprite API speedup, IL2CPP performance, allocation counts, or
whole-suite test runtime.

Every process passes the same 59 cases with zero skips, returns native exit code zero, and reaches
both output stream ends without a drain stop. The correctness fixture includes 12 cases covering
all 64 binary 3 × 2 patterns under four effective-alpha policies and three colour-share thresholds.
Its independent connectivity oracle uses component unions, and also checks subregions, pivots and
pixel scales. Existing invalid-input, colour, connectivity and tie cases remain.

Production source hashes stay unchanged within each capture. Logged loaded-module identities
match across the three baseline runs and across the three candidate runs, and differ between
variants. The runner records the production assembly hash after each process. These observations
identify the measured variants; they are not a full compiler or player qualification.

The [replay archive](./sprite-ink-traversal.zip) retains both implementation variants, both test
fixtures, the native controller, complete NUnit results, raw round lines, source hashes, assembly
identities and process receipts. Run the timing fixture explicitly in an isolated checkout; its
`Performance` category keeps the repeated measurements out of normal test iteration.
