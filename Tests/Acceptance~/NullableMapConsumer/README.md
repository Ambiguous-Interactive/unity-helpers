# Independent nullable map consumer

This tracked template builds a separate consumer project for issues #950 and #953. It uses ordinary consumer contracts and metadata preservation, with no generic serializer roots or package test assemblies. It lives under `Tests/Acceptance~` because it is a standalone acceptance test project. The trailing `~` keeps Unity from importing it as part of the package. Git dependencies still download these tracked files; npm publishing excludes the entire `Tests` directory.

Select `nullableconsumer` in the Unity Tests workflow’s manual acceptance input. It runs on Unity 2021.3.45f1 and 6000.6.0f1. Each build uses High stripping, Release IL2CPP, and no development mode. Each version runs 22 fresh player processes: 16 retained nested-map operations and six nullable-presence operations across 24 maps. Intermediate version selections fail instead of skipping acceptance.

The operations check public typed/object equality and hashing, public serialization, foreign serialization, merge identity, comparers, and mutations. The merge process verifies disjoint additions preserve collection identity and comparer order, and rejects overlapping Guid keys under the shipped compatibility level while preserving the existing sorted entries. Each read process deserializes and verifies its literal payload before constructing any fixture collection, then checks that writing the decoded values reproduces the exact bytes. All 15 nullable-value maps check present-default changes to null and missing entries. Nullable keys cover plain, cached, and CLR dictionaries where their constraints permit them.

`HistoricalGoldens~` retains the original 130-byte populated payload, whose two nullable zeros lack presence. `Goldens~` contains the corrected 134-byte payload and an independent 352-byte presence payload. The null and empty literals remain unchanged. Historical omitted presence cannot be recovered.

The [runner](../../../scripts/unity/run-nullable-consumer.ps1) prepares settings and a scene before binding the package, tools, configuration, consumer sources, and goldens. The [verifier](../../../scripts/unity/verify-nullable-consumer.ps1) checks those inputs against the current checkout, the actual linker descriptors and converter modules, the complete player-file manifest, and all 22 process records. The runner freezes that manifest outside the player directory before the first process, including the launcher, GameAssembly.dll, UnityPlayer.dll, metadata, settings, inputs, and build report. It verifies the complete inventory and every file hash before and after each process; the declaration and each process record bind the manifest hash. The build retains stdout and stderr, and records actual linker and converter commands with their inline or response-file inputs. Explicit converter assembly lists remain authoritative; nearby files do not qualify as inputs. Source checks and synthetic parser controls qualify the tooling; actual floor/latest player results are required for acceptance.

On Unity 2021.3, the consumer editor callback snapshots generated linker XMLs during `GenerateAdditionalLinkXmlFile`, before staging cleanup. It returns no additional descriptor. The archive binds the original absolute source paths and exact bytes; the verifier requires the consumed scene descriptor to match its lifecycle snapshot. Unity 6000.6 retains the existing post-build capture path. Missing, conflicting, linked or corrupted snapshots fail acceptance; this capture does not supply extra generic preservation hints.

The shared dependency recipe authors `com.unity.toolchain.win-x86_64-linux-x86_64` at exactly `2.0.11` before the floor editor starts. This pins the project dependency Unity 2021.3.45f1 otherwise adds during preparation; the latest consumer has no such additional dependency. Verification compares the complete manifest with ordinal JSON keys, values and kinds, ignoring only object-property order. Unknown dependencies and changed versions remain failures. This project input does not identify which toolchain executed the Windows converter.

## Browser execution

The [browser runner](../../../scripts/unity/run-browser-consumer.py) copies this template and the
package's Runtime, Editor, and linker inputs into a fresh project. It builds a nondevelopment
WebGL player with High stripping, Release IL2CPP, and `SINGLE_THREADED`. It uses a clean build cache
because incremental WebGL builds can omit native script registration in the host environment.
The original project and package remain untouched.

The browser copy makes all four consumer declarations partial and adds matching `WProtoContract`
and `WProtoMember` annotations while retaining their protobuf-net annotations for foreign reads and writes.
It checks each original tag before editing and binds the resulting sources in the manifest.
Startup requires generated formatters for all four contracts. A legacy fallback cannot satisfy
this check. The original template remains the legacy-backend consumer used by the Windows gate.

Install a Chromium browser for Playwright, or pass an existing Chromium executable. Run from the
repository root, using a fresh artifact directory outside Unity's imported folders:

```bash
uv run --with playwright==1.56.0 playwright install chromium
uv run scripts/unity/run-browser-consumer.py --editor /path/to/Unity --unity 6000.4.6f1 --artifacts /path/to/fresh-artifacts
```

Each of the 22 retained-map operations runs in a fresh browser context and page. The runner also
checks safe enum conversion and scalar wire encoding for all eight underlying types, the retained
WyRandom sequence, and 2D/3D R-tree queries against direct bounds tests. Two separate negative
controls must reject corrupt golden bytes and a wrong source identity. This is correctness
evidence for these cases; it does not measure allocations or throughput or cover every subsystem.

`source-manifest.json` binds the copied package and consumer inputs. `identity.json` binds the
commit, source manifest, Unity version, and unique run token. `player-manifest.json` binds every
served player file and is checked before and after each case. Per-case receipts retain exact
results and browser errors. Each browser execution gets a separate directory under `browser-runs`.
Its `results.json` is written only after the complete matrix passes. `latest-run.json` reports the
latest execution's running, passed, or failed state; failed reruns preserve prior receipts.
Each execution also records the runner hash, Playwright version, and browser version.
Build logs and actual editor exit codes remain available when a build fails. Use `--browser-only`
to rerun the same immutable player; this does not rebuild or update its identity.

This manual browser check is separate from the Windows IL2CPP workflow and does not establish
floor/latest Unity acceptance, native crash resolution, or a complete WebGL CI matrix.
