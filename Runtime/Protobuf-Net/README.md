# Bundled protobuf-net

The two protobuf-net assemblies are patched builds of upstream **3.2.56**, targeting
.NET Standard 2.1. Source commit: `dfdfce61a739cfd76f05fcdacf8a4b3b9e94e684`.
The annotated tag object is `421b80a9b128be779036e600b996efb9e48b9f92`.
Upstream source and license: [protobuf-net](https://github.com/protobuf-net/protobuf-net/tree/dfdfce61a739cfd76f05fcdacf8a4b3b9e94e684)
and [bundled license](./Licence.txt).

[The source patch](./il2cpp-aot.patch) changes runtime behavior:

- Construct fallback value checkers directly, avoiding reflected closed generic constructors.
  Primary primitive checkers keep their original default-value policy. Nonnullable structs
  remain present even at their default value. Nullable fallback structs use constrained
  `Equals(null)` to check presence without calling the underlying struct's equality method.
- Unwrap nullable member types before effective-type classification, so primitive nullable
  members do not depend on reflective dynamic-stub construction.

- Read and write runtime map entries through nongeneric slot nodes when dynamic code is unavailable.
  The nodes retain the active model, actual key/value services, wire options, factories and merge
  behavior. Compiled models keep the original typed emitter.
- Reject indexed properties before inspecting tuple setters. Indexers cannot be read by the
  tuple serializer; ordinary tuples, key/value pairs and index-free init-only contracts keep
  their existing classification.

- Register real typed list and vector providers for intrinsic scalar types, including nullable
  scalars and repeated bytes. Owned generic set and callback-backed collection static constructors
  register their actual providers before metadata lookup or explicit model registration.
  They opt in through `TypedRepeatedProviderAttribute`; metadata discovery initializes the base type
  that declares this attribute without running unrelated consumer subclass initializers. Runtime
  lookup checks the original provider identity before
  using a registered instance and constructing its original typed repeated decorator directly;
  custom provider precedence and compiled emitters remain intact.
  These factories construct their singleton serializers directly.
  Registration also supplies a direct item-contract serializer factory. Each model receives
  a fresh item serializer; models do not share member or callback state. Registered typed
  factories and inherited contract construction retain their existing paths.

- Construct ordinary unregistered runtime contract metadata through a nongeneric node.
  The node retains the actual declared type, constructor, factory, callbacks and member rules.
  Typed services bridge reads, writes and instance creation to that node. Boxed value types
  retain copy isolation, including zero-initialized values for missing input. Custom providers
  retain precedence; inherited contracts and compiled emitters retain their existing behavior.

- Wrap reference-type contract writes implementing `ISerializationWriteScope` in an ownership
  scope, including interpreted and emitted serializers. Entry precedes callbacks; exit runs
  in `finally` after successful entry. Inherited contracts enter once, and consumer
  after-serialization callbacks still run only when their original write succeeds.
  The interpreted interface cast runs in a nongeneric helper; the root and reference-type
  guards remain in the caller.
  Value types cannot implement this scope. Entry must fail atomically and exit must not throw.
  Owned deque and cyclic buffer scratch state supports nested writes of the same instance.

- Preserve present nullable defaults in map keys and values. Only null nullable slots are omitted;
  primary checker policies and nonnullable default omission keep their existing behavior.
- Register underlying nullable enum and contract serializers during root and member map discovery.
  Model-local nullable contract services delegate to the existing serializer, with matching emitted
  services for compiled models. Provider-supplied nullable services keep precedence, including
  factory-returned services. Recompiling an underlying contract invalidates its cached nullable service.

- Construct runtime surrogate nodes without a reflected generic constructor. Typed surrogate
  services retain their actual feature flags, conversion failures and provider precedence.
- Register typed codecs and factories for the owned plain and sorted dictionary wrappers.
  Reads validate nullable presence data against parallel arrays before constructing the owner;
  writes retain the existing fields, tags and callback behavior. Nontrivial default instances
  initialize lazily and retain their cached identity. Failed initialization identifies the
  dedicated default holder.
- Reuse runtime enum map decorators by the original serializer's reference identity. Each
  operation resolves services from its active model; custom factories and compiled services
  retain precedence.
- Register functional factories for accessible closed external serializer providers through
  generated startup code. Type-only model registrations retain their existing services,
  including factory-returned nullable services and original constructor/proxy failures.
  Registration does not construct providers or share model state. Earlier access before
  generated startup, inaccessible providers and arbitrary consumer AOT contracts require
  separate qualification.

Field numbers and nonnullable default omission retain upstream behavior. Nullable map defaults
now carry explicit presence bytes; old omitted fields cannot recover a lost default value.
The additive `RuntimeTypeModel.RegisterRepeatedSerializer<TCollection, TItem>` API registers an
existing provider without initializing or freezing a model. It rejects mismatched item types,
map providers and conflicting registrations. It does not make arbitrary dynamic consumer
contracts AOT compatible.
The original strong-name key, assembly versions and target framework are preserved.
These are modified upstream binaries, rather than unmodified NuGet artifacts. The patches
address the observed dictionary-entry, nullable-member, reflected map-constructor and indexed
tuple-classification and repeated-provider factory failures;
other dynamic protobuf-net
contracts still require their own IL2CPP qualification. See
[serialization compatibility](../../docs/features/serialization/serialization.md#protobuf-equality-and-hashing).

## Rebuild

Use Git and .NET SDK **9.0.306**. Run from the package root. Keep the full source history:
upstream versioning computes the assembly file version from it.

```bash
package_root="$PWD"
source_root="$(mktemp -d)"
git -c core.autocrlf=false clone --branch 3.2.56 https://github.com/protobuf-net/protobuf-net.git "$source_root"
git -C "$source_root" config core.autocrlf false
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
| protobuf-net.dll      | `01c0eb2062c70f3eb27b1c662288247514741391f82b549c63c68ffa7d672630` |
| protobuf-net.Core.dll | `be800dd4484bddada630bde8e7a7ea753022bba2856e8d8cf378a6a768424f04` |
