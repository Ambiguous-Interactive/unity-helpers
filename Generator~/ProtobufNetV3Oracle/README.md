# Frozen protobuf-net v3 oracle

These are the patched protobuf-net **3.2.56** .NET Standard 2.1 assemblies that
Unity Helpers shipped before its runtime dependency upgrade. The differential and
retained-consumer v3 lanes keep these bytes independently from the current runtime.

- Upstream source: `dfdfce61a739cfd76f05fcdacf8a4b3b9e94e684`.
- Informational version: `3.2.56+dfdfce61a7`.
- Assembly version: `3.0.0.0` for both DLLs.
- `protobuf-net.dll` SHA256: `01c0eb2062c70f3eb27b1c662288247514741391f82b549c63c68ffa7d672630`.
- `protobuf-net.Core.dll` SHA256: `be800dd4484bddada630bde8e7a7ea753022bba2856e8d8cf378a6a768424f04`.

The [original AOT patch](./il2cpp-aot.patch) is retained alongside the assemblies.
The [Apache 2.0 license](../../Runtime/Protobuf-Net/Licence.txt) applies to these
modified upstream binaries. `OracleIdentityTests` checks their exact hashes in each
isolated process. This directory is excluded from Unity imports and release payloads
by its `Generator~` ancestor. Never refresh this historical oracle during upgrades.
