# Independent nullable map consumer

This tracked template builds a separate consumer project for issues #950 and #953. It uses ordinary consumer contracts and metadata preservation, with no generic serializer roots or package test assemblies. The trailing `~` keeps its sources out of the package editor; npm publishing excludes the template.

Select `nullableconsumer` in the Unity Tests workflow’s manual acceptance input. It runs on Unity 2021.3.45f1 and 6000.6.0f1. Each build uses High stripping, Release IL2CPP, and no development mode. Each version runs 22 fresh player processes: 16 retained nested-map operations and six nullable-presence operations across 24 maps. Intermediate version selections fail instead of skipping acceptance.

The operations check public typed/object equality and hashing, public serialization, foreign serialization, merge identity, comparers, and mutations. All 15 nullable-value maps check present-default changes to null and missing entries. Nullable keys cover plain, cached, and CLR dictionaries where their constraints permit them.

`HistoricalGoldens~` retains the original 130-byte populated payload, whose two nullable zeros lack presence. `Goldens~` contains the corrected 134-byte payload and an independent 352-byte presence payload. The null and empty literals remain unchanged. Historical omitted presence cannot be recovered.

The [runner](../../scripts/unity/run-nullable-consumer.ps1) prepares settings and a scene before binding the package, tools, configuration, consumer sources, and goldens. The [verifier](../../scripts/unity/verify-nullable-consumer.ps1) checks those inputs against the current checkout, the actual linker descriptors and converter modules, the executable, and all 22 process records. Source checks and synthetic parser controls qualify the tooling; actual floor/latest player results are required for acceptance.
