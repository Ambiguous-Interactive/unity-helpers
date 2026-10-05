# Bundled protobuf-net

The two protobuf-net assemblies are patched builds of upstream **3.2.56**, targeting
.NET Standard 2.1. Source commit: `dfdfce61a739cfd76f05fcdacf8a4b3b9e94e684`.
The annotated tag object is `421b80a9b128be779036e600b996efb9e48b9f92`.
Upstream source and license: [protobuf-net](https://github.com/protobuf-net/protobuf-net/tree/dfdfce61a739cfd76f05fcdacf8a4b3b9e94e684)
and [bundled license](./Licence.txt).

[The source patch](./il2cpp-aot.patch) changes two internal paths:

- Construct fallback value checkers directly, avoiding reflected closed generic constructors.
  Primary primitive checkers keep their original default-value policy. Nonnullable structs
  remain present even at their default value. Nullable fallback structs use constrained
  `Equals(null)` to check presence without calling the underlying struct's equality method.
- Unwrap nullable member types before effective-type classification, so primitive nullable
  members do not depend on reflective dynamic-stub construction.

Map serialization, field numbers, default omission and public APIs retain upstream behavior.
The original strong-name key, assembly versions and target framework are preserved.
These are modified upstream binaries, rather than unmodified NuGet artifacts. The patches
address the observed dictionary-entry and nullable-member failures; other dynamic protobuf-net
contracts still require their own IL2CPP qualification. See
[serialization compatibility](../../docs/features/serialization/serialization.md#protobuf-equality-and-hashing).

## Rebuild

Use Git and .NET SDK **9.0.306**. Run from the package root. Keep the full source history:
upstream versioning computes the assembly file version from it.

```bash
package_root="$PWD"
source_root="$(mktemp -d)"
git clone --branch 3.2.56 https://github.com/protobuf-net/protobuf-net.git "$source_root"
git -C "$source_root" checkout dfdfce61a739cfd76f05fcdacf8a4b3b9e94e684
git -C "$source_root" apply "$package_root/Runtime/Protobuf-Net/il2cpp-aot.patch"
cat > "$source_root/global.json" <<'JSON'
{
  "sdk": {
    "version": "9.0.306",
    "rollForward": "disable",
    "allowPrerelease": false
  }
}
JSON
(
  cd "$source_root"
  dotnet build src/protobuf-net/protobuf-net.csproj -c Release -f netstandard2.1 \
    -p:TargetFrameworks=netstandard2.1 -p:GeneratePackageOnBuild=false \
    -p:UseSharedCompilation=false --nologo
)
sha256sum "$source_root/src/protobuf-net/bin/Release/netstandard2.1/protobuf-net.dll"
sha256sum "$source_root/src/protobuf-net.Core/bin/Release/netstandard2.1/protobuf-net.Core.dll"
```

The upstream build uses its pinned central package versions, Release optimization,
deterministic compilation, embedded debugging information and its supplied signing key.
The build uses SDK 9 in place of upstream's SDK 8 selection; this SDK change does not change
the .NET Standard 2.1 target. Check the hashes before replacing the shipped files.

| File                  | SHA256                                                             |
| --------------------- | ------------------------------------------------------------------ |
| protobuf-net.dll      | `78b2b328cab561e9db45b127f2862d8641b81bb22b161cd9e5b217665fece3e5` |
| protobuf-net.Core.dll | `d14eeba59814700aee7f596086bc974c7bf12de80545ee156ac4affed44d1cf6` |
