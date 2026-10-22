# Current protobuf-net candidate

This is the AOT patch rebased onto protobuf-net **3.4.40**, upstream commit
`3aa672eaaf7763b86d62bec9ed7d7896b0511a8b`. It supports the dependency admission
work in [#1052](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1052).
The package still ships the patched 3.2.56 binaries. Historical v2 and v3 oracle
identities remain frozen.

The rebase keeps upstream's direct nullable/nonnullable value checkers and updated
trimming annotations. It carries forward the package's lazy default holder,
runtime map bridges, collection factories, contract and surrogate nodes, nullable
services, external providers, and serialization ownership scopes. See the
[shipped patch contract](../../Runtime/Protobuf-Net/README.md).

## Build and test

Install .NET SDK **9.0.319**, Python 3.10 or newer, and Git. Use a new output
directory outside the package, or inside a dot-prefixed or `~`-suffixed directory
that Unity ignores. The builder refuses existing output directories.

```bash
python3 scripts/build-protobuf-candidate.py --output /tmp/protobuf-candidate
dotnet test Generator~/WallstopStudios.UnityHelpers.Proto.Generator.Tests/WallstopStudios.UnityHelpers.Proto.Generator.Tests.csproj \
  -c Release -p:ProtobufNetOracle=candidate \
  -p:ProtobufNetCandidateRoot=/tmp/protobuf-candidate/assemblies \
  -- RunConfiguration.TreatNoTestsAsError=true
```

`--dotnet` selects an explicit executable. `--upstream` can name an existing full
upstream clone for offline use; the source commit remains pinned.
The builder resets the cloned origin to the canonical upstream URL so offline and
hosted builds record the same repository provenance.
Partial clones
need their complete history objects because upstream's versioning task reads
historical blobs without Git's lazy fetching.

The builder clones the source, checks and applies the patch, selects the exact SDK,
and builds signed Release .NET Standard 2.1 libraries. It narrows only the two
runtime projects' target lists; upstream's build tools still target .NET Standard
2.0. It retains command output, exit status, SDK information, NuGet assets and DLL/XML
hashes. A timeout kills the process tree and fails the build.

The candidate lane uses a separate output directory and test process. Its identity
test checks the source commit, SDK, target, applied patch hash, loaded assembly
versions and DLL hashes against the build receipt. It runs the same differential
and source-linked regression fixtures as v3. Build warnings remain visible.

The candidate needs `System.Collections.Immutable` 10.0.13 and its dependencies.
Its host NuGet references apply only to the candidate lane. Do not copy them into
the Unity package: assembly ownership, retained consumer migration, supported Unity
versions, stripped IL2CPP and native process health still need qualification.
The receipt records `nativeQualificationComplete: false`; host tests cannot change
that conclusion. See the [assembly ownership guide](../../docs/guides/bundled-assembly-conflicts.md).

Upstream source and this derivative patch retain the
[protobuf-net Apache 2.0 license](../../Runtime/Protobuf-Net/Licence.txt).
