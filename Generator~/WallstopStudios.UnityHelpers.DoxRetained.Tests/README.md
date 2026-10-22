# DoxReloaded retained consumer admission

This isolated .NET 9 test project compiles the captured `Data.Persistence.Models.LevelProgressModel`
and `SettingsModel` declarations without editing them. It admits eleven frozen, save-derived inputs
against generated WProto formatters and a protobuf-net v3 runtime model using the **same consumer
types**, memory reads and buffer writes. The captured attributed `ProgressOracle` and `SettingsOracle`
types provide a separate comparator for the runtime-model metadata; they are not the consumer types.

```bash
dotnet test Generator~/WallstopStudios.UnityHelpers.DoxRetained.Tests/WallstopStudios.UnityHelpers.DoxRetained.Tests.csproj -c Release -p:ProtobufNetOracle=v3
```

The generator workflow runs this command separately from the ordinary host oracle suites. Its
`Generator~/**` path filter covers the project and its frozen data. The project references the existing
v3 host runtime closure and inherits the shipped compiler inputs from `Generator~/Directory.Build.props`.
Explicit compile references select the repository's pinned protobuf-net and protobuf-net.Core DLLs;
the project reference does not supply these raw DLL references transitively.
The historical lane rejects v2 and external oracle-path overrides. Its loaded assemblies must
match the frozen byte hashes in `DoxRetainedInventory.v1.json`, using the preserved v3 DLLs
under `Generator~/ProtobufNetV3Oracle`.

The same retained inputs also run against the current source-built runtime, in a separate process:

```bash
dotnet test Generator~/WallstopStudios.UnityHelpers.DoxRetained.Tests/WallstopStudios.UnityHelpers.DoxRetained.Tests.csproj -c Release -p:ProtobufNetOracle=candidate -p:ProtobufNetCandidateRoot=/tmp/protobuf-candidate/assemblies
```

That lane checks the current build receipt, patch and loaded DLL hashes instead of expecting
the old DLL bytes. Both lanes preserve every captured source, value, payload and inventory byte.

## Captures and provenance

- The two model source hashes match the retained October 16 consumer source census. The two attributed
  oracle sources were captured from the consumer's tests and do not have that census identity. The
  inventory records this distinction for all four files.
- Ten progress payloads and one settings payload were captured as oracle reconstructions from retained
  SQLite rows and JSON settings. The original wire encoder command and binary identity were not
  preserved. These inputs are **not claimed to be historical production-writer bytes**.
- `level-progress-oracle.json.txt` contains the independent row values, including exact `ProgressJson`
  strings. The earlier admission checked them against the retained SQLite database. This durable suite
  uses those frozen JSON values; it does not execute SQLite. `settings.json.txt` supplies the independent
  map values. Input identifiers, row associations, lengths and SHA-256 values are fixed.
- The source census and exploratory admission are historical provenance, not results from this new
  fixture. The inventory names their capture locations and the prior admission revision.

The `.cs.txt`, `.json.txt` and `.bin` files are byte captures. Git attributes disable text conversion;
do not format or normalize them. The project explicitly compiles the four captured `.cs.txt` files.
Keeping them under `Generator~` prevents these game namespaces from entering the ordinary Unity test
assembly. All other new C# files follow the repository formatting and license rules.

## What the fixture establishes

Each retained input requires a generated formatter, exact semantic values from the independent JSON,
and byte agreement for WProto and same-type v3 writes. Both readers also consume the other writer's
output. Empty/default contracts, map initialization and the consumer dictionary-copy constructor are
checked separately. The comparator freezes Level200 metadata, tags, the false default and map policy;
omission controls demonstrate the effect of leaving out the false default or `IsMap=true`.

The settings readonly dictionary is constructed through the generated immutable path. The fixture
asserts that its formatter does **not** implement `IWProtoMergeFormatter<SettingsModel>`. Protobuf-net
merging into an existing settings object is checked against its attributed oracle, including map
identity and duplicate-key replacement. This does not claim reusable WProto settings reads.

Mutation cases reject aliases, duplicates, missing or extra identities, changed row associations,
lengths, captured bytes and independent semantic values. These ordinary NUnit cases run in their
normal fixture; there is no separate test runner or lifecycle mechanism.

The small local domain, SQLite-attribute and helper declarations provide only the signatures needed
to compile the captures. Unused JSON, ordering, hashing and equality helpers throw if invoked. The
public Unity preserve marker comes from the existing host closure. No game logic, SQLite behavior,
Unity runtime behavior, complete consumer deployment, historical writer compatibility, timing target
or native backend acceptance follows from this admission. The synthetic seven-family timing corpus
and its weights remain unchanged.
