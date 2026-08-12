# protobuf-net v2 oracle

This directory contains the `netstandard2.0` assembly from the official
[`protobuf-net` 2.4.9 NuGet package](https://www.nuget.org/packages/protobuf-net/2.4.9).
It is used only by the WallstopProto differential test process and is excluded from every Unity
package payload by the `Generator~` directory suffix.

- Package SHA-256: `e5d138e05dc48af4f896dcb5853f3a50e58f321c98460e5795ec6f8da8ff3949`
- Assembly SHA-256: `8f8f1c205ecebb5bd74d0bed130e8ce8745e8053f52838517d74535f2096742c`
- Assembly informational version: `2.4.9.1+f4bacb1a94`
- Assembly version: `2.4.0.0`
- License: Apache License 2.0, already reproduced in
  [the package third-party notices](../../docs/project/third-party-notices.md#protobuf-net)

The v2 and v3 assemblies have the same simple name, so loading them side by side would let .NET or
Unity bind both aliases to one physical assembly and create a false dual-oracle result. CI instead
runs the complete differential suite in two isolated test processes. `OracleIdentityTests` verifies
which physical oracle each process loaded before its conformance tests can pass.
