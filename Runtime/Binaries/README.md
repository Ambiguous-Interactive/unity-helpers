# Bundled .NET assemblies

The BCL and Immutable DLLs are the exact `lib/netstandard2.0` assets from official
NuGet 10.0.13 packages, source commit `4099f83977f4d29604a9aeb971fb0a7bc1d5f699`.
Unsafe remains at 6.1.2. Existing plugin importers and GUIDs are preserved.
The four BCL XML files retain the official package content with byte order marks and trailing whitespace removed.

| DLL                                        | Package version | DLL SHA256                                                         |
| ------------------------------------------ | --------------- | ------------------------------------------------------------------ |
| Microsoft.Bcl.AsyncInterfaces.dll          | 10.0.13         | `de8463922b609ff1f5d90a7e78040599c95932e688716c31e981bc160a2f2ee8` |
| System.IO.Pipelines.dll                    | 10.0.13         | `0c97048fdedc4ccb6f0f41f2545efe307a24cff47324c15fd279a77fe5c2690e` |
| System.Text.Encodings.Web.dll              | 10.0.13         | `7004c5ce76750196f4024c5bd88935c5d71decedc3eaac9f0a494b39ca6be3a7` |
| System.Text.Json.dll                       | 10.0.13         | `d6ece4fedda76a70760cc62c57350e32657fe15a2642792a494a5a05e55268cf` |
| System.Collections.Immutable.dll           | 10.0.13         | `499172bf4ea27ad9928c2df9fddb44b489fb06cbbe80cd3b141192cac6507bfe` |
| System.Runtime.CompilerServices.Unsafe.dll | 6.1.2           | `fd67abc4c4af10affbcd42fd68a2a7bc361b26714afe6965d4d0d139459d8297` |

## Package provenance

| NuGet package                                                                                                               | Package SHA256                                                     |
| --------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| [microsoft.bcl.asyncinterfaces 10.0.13](https://www.nuget.org/packages/microsoft.bcl.asyncinterfaces/10.0.13)               | `6b5580a4250caa42abe7c6938444283d04ea28fe956634648efafc291a935a78` |
| [system.io.pipelines 10.0.13](https://www.nuget.org/packages/system.io.pipelines/10.0.13)                                   | `5cab29bacbeee21ff054dcca2f40a20530752671aa96dd8514bb695773317220` |
| [system.text.encodings.web 10.0.13](https://www.nuget.org/packages/system.text.encodings.web/10.0.13)                       | `6ea32156a8c5bd877a6cf109cc5c3482f98df4a0715d4f8dcdb3a50ba1a9a137` |
| [system.text.json 10.0.13](https://www.nuget.org/packages/system.text.json/10.0.13)                                         | `c36743c19c6e9d3e1f9ea8b38f724487b9fb8a959be53c621c9cc8ae93ba0b93` |
| [system.collections.immutable 10.0.13](https://www.nuget.org/packages/system.collections.immutable/10.0.13)                 | `74c7c3ab2dcce3385482c7504bacfb0ba051aefaae0be7e09c1297b894f80616` |
| [system.runtime.compilerservices.unsafe 6.1.2](https://www.nuget.org/packages/system.runtime.compilerservices.unsafe/6.1.2) | `5f6a7f53af3465f92beb6da873ebe0e496206c313313b98badee4355a6b25937` |

Download packages from NuGet's v3 flat container, verify their package hashes, then
extract only the named .NET Standard 2.0 members. Keep the matching XML files and
preserve existing importers. Do not copy another Unsafe DLL into this directory.

For System.Text.Json 10.0.13, the .NET Standard dependency group names companion
BCL 10.0.13, Buffers 4.6.1, Memory 4.6.3, Unsafe 6.1.2 and Tasks.Extensions 4.6.3.
Unity supplies the framework assemblies; a NuGet version alone does not establish
the actual loaded framework identity. Check the binder and supported editor/player
profiles when refreshing these assets. See the
[assembly ownership guide](../../docs/guides/bundled-assembly-conflicts.md).
