# Bundled protobuf-net

The two protobuf-net assemblies are patched builds of upstream **3.4.40**, targeting
.NET Standard 2.1. Source commit: `3aa672eaaf7763b86d62bec9ed7d7896b0511a8b`.
Upstream source and license: [protobuf-net](https://github.com/protobuf-net/protobuf-net/tree/3aa672eaaf7763b86d62bec9ed7d7896b0511a8b)
and [bundled license](./Licence.txt).

[The source patch](./il2cpp-aot.patch) changes runtime behavior:

Upstream 3.4.40 already constructs nullable and nonnullable fallback value checkers directly.
The rebase keeps that implementation and its trimming annotations. The package patch unwraps
nullable member types before effective-type classification, avoiding reflective dynamic-stub
construction for primitive nullable members.

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

Use Git, Python 3.10 or newer, and .NET SDK **9.0.319**. From the package root:

```bash
python3 scripts/build-protobuf-candidate.py --output /tmp/protobuf-candidate
python3 scripts/verify-protobuf-shipped.py --candidate-root /tmp/protobuf-candidate/assemblies
```

The output directory must be new. `--dotnet` selects the SDK executable;
`--upstream` can select a full local clone while retaining the pinned commit and
canonical source URL. The builder keeps upstream history, signing key and Release
.NET Standard 2.1 target, applies the exact patch, and retains source inventories,
NuGet assets, command logs, process status and DLL/XML hashes. CI rebuilds and
byte-compares both shipped DLLs and the patch against this recipe.

| File                  | SHA256                                                             |
| --------------------- | ------------------------------------------------------------------ |
| protobuf-net.dll      | `6f75170c678c32eb8dddde13ef697387205a4e6dd52bd0b3240fd5f1cd6de6d2` |
| protobuf-net.Core.dll | `11a176b195d44b88ae13c3a0687d874eb8b937c88e2d1c6a2978c7f996ce8da4` |

The runtime uses Immutable 10.0.13 and Unsafe 6.1.2 from the official .NET Standard
2.0 NuGet assets. Their existing importers retain assembly ownership rules.
The [historical v3 oracle](../../Generator~/ProtobufNetV3Oracle/README.md) preserves
the previous patched 3.2.56 DLLs and original patch. The v2 oracle is unchanged;
current-upstream tests run in a separate process rather than replacing either
historical witness. The retained-consumer suite runs against both v3 and current
protobuf. Native backend and process-health qualification remains tracked in
[#1052](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1052).
