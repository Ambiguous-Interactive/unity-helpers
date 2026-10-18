# Exported schema conformance

This separate Linux x64 .NET 9 suite compiles the real schema exporter and the ordinary
`WProtoSchemaTextTests` fixture. It also sends the export directly to **protoc 27.2**, from the pinned
private `Grpc.Tools` 2.67.0 build dependency. The executable version and SHA-256 must match the
reviewed Linux x64 binary. The tool and its package never enter the Unity runtime or package payload.

```bash
dotnet test Generator~/WallstopStudios.UnityHelpers.SchemaConformance.Tests/WallstopStudios.UnityHelpers.SchemaConformance.Tests.csproj -c Release -p:ProtobufNetOracle=v3
```

The shared host assembly supplies the real wire implementation. An explicit friend assembly grants
the linked exporter access to its internal value-enumeration helper. This keeps schema reflection
out of the main oracle test assembly, whose allocation tests require that isolation.

## Independent readers and values

Each interoperability case exports its actual C# contract and requires no exporter diagnostics.
The native compiler first produces a descriptor set. Its C++ implementation then encodes an
independent literal text-format input, which WProto must decode to explicitly asserted values.
In the other direction, protoc decodes WProto output and must report the same text-format values
as its independent input. Different valid wire encodings are allowed; no general byte-order
equivalence is asserted.

The finite panel covers scalar boundaries, ZigZag, floating-point bits, Unicode and binary data,
absent/present nullable defaults, packed/unpacked collections, jagged and rectangular wrappers,
native string maps with default keys/values, GUID entry-message maps, BCL temporal/GUID/decimal
layouts and singular/repeated/map URI text. The exporter uses fixed-width integer schema fields
for floating-point bits. The independent text inputs spell those bits explicitly.

Wide enum cases cover the full `uint`, `long` and `ulong` ranges, unnamed values, nullable presence,
packed/unpacked runs, jagged/rectangular wrappers and dictionary keys/values. Numeric schema fields
preserve those domains. Independent wrong-width schemas compile but refuse the valid boundary
values. Shared literal wire goldens retain the existing signed-varint enum encoding.

Malformed-schema and changed-tag controls require native rejection or changed values in both reader
directions. The ordinary shared fixture also checks explicit surrogate precedence over native shapes.
These cases qualify their declared inputs; they do not establish all schema features, complete
capability-catalog evidence, fuzz safety, consumer rollout, Unity backends or performance targets.

## Retained output

Every compiler process has a 15-second deadline. Its arguments, UTC bounds, exit code, timeout state,
raw stdout and stderr are retained. Successful cases also retain the actual export, descriptor set
and WProto bytes as test attachments. Compiler identity is recorded in NUnit output.
Set `WPROTO_SCHEMA_EVIDENCE_ROOT` to select the process-output directory; otherwise it stays under
the ignored test binary directory. The generator workflow retains this output with the ordinary
TRX results, separately from the strict v2/v3 invocation receipts.
