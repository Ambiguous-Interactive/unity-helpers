# Compiler-floor conformance

This host-only .NET 9 suite loads the **shipped generator DLL** through Microsoft.CodeAnalysis
3.8.0. It parses C# 9 consumer contracts and emits them against the SDK's pinned
`NETStandard.Library.Ref` 2.1.0 reference pack and actual TypeCheck runtime metadata.
The compiler API versions and the target core-library identity are asserted independently.

```bash
dotnet test Generator~/WallstopStudios.UnityHelpers.Proto.CompilerFloor.Tests/WallstopStudios.UnityHelpers.Proto.CompilerFloor.Tests.csproj -c Release -- RunConfiguration.TreatNoTestsAsError=true
```

The twelve cases cover absent, source-defined, public, inaccessible internal, and friend-assembly
module-initializer attributes. Successful consumers must contain an actual module constructor.
Removing the generated polyfill refuses compilation; removing the attribute use removes that
constructor. A consumer without a registrar receives no polyfill. The inaccessible-dependency case
fails with CS0122 against the previous shipped generator.

Each invocation retains the synthetic inputs, generated source, emitted binaries, diagnostics and
hashes of the compiler, generator, fixture and reference assemblies. Reference and compiler bytes
are copied beside that manifest. Set `WPROTO_COMPILER_EVIDENCE_ROOT` to select the output root;
the default is under the ignored test binary directory.

This proves finite compiler/API and emitted-IL behavior on CoreCLR. The runtime reference is the
existing Unity-facing TypeCheck assembly; this suite does not compile the entire platform-neutral
runtime source closure. It does not run a Unity compiler process, execute startup in a player, or
establish AOT, configuration-matrix, native-safety or throughput acceptance. Strict host-oracle
invocation receipts remain separate from these ordinary NUnit results.
