# Bundled .NET assemblies

The BCL and Immutable DLLs are the exact `lib/netstandard2.0` assets from official
NuGet 10.0.14 packages, source commit `0427b9abbead3fa64c6655eb4290c537397131fa`.
Unsafe remains at 6.1.2. Existing plugin importers and GUIDs are preserved.
The four BCL XML files retain the official package content with byte order marks and trailing whitespace removed.

| DLL                                        | Package version | DLL SHA256                                                         |
| ------------------------------------------ | --------------- | ------------------------------------------------------------------ |
| Microsoft.Bcl.AsyncInterfaces.dll          | 10.0.14         | `71ebd816fc62255b8097a6aa813d73299928b64a3bb6cb0a667c1ce53c2b44f3` |
| System.IO.Pipelines.dll                    | 10.0.14         | `9f5514995fc52aad781a2f7fc8ef999ecc78e8ccacaa15a6491d550de7f55bcf` |
| System.Text.Encodings.Web.dll              | 10.0.14         | `6630388e3573f1d6500891723b30f21d8545e14370707b4d4328e6f61aaca233` |
| System.Text.Json.dll                       | 10.0.14         | `136262fc12b3473cf408a4f5a3e604c73019b8baa0d3593205a87df67c7c4228` |
| System.Collections.Immutable.dll           | 10.0.14         | `08b4fe148bbb6998b8be8cf7db85d2c1265b4e5be302515aa7cdd7085e90d4ff` |
| System.Runtime.CompilerServices.Unsafe.dll | 6.1.2           | `fd67abc4c4af10affbcd42fd68a2a7bc361b26714afe6965d4d0d139459d8297` |

## Package provenance

| NuGet package                                                                                                               | Package SHA256                                                     |
| --------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| [microsoft.bcl.asyncinterfaces 10.0.14](https://www.nuget.org/packages/microsoft.bcl.asyncinterfaces/10.0.14)               | `fb9352e6211df8509094861281e627547956e3a81fbba41b6af1d829bae5c80b` |
| [system.io.pipelines 10.0.14](https://www.nuget.org/packages/system.io.pipelines/10.0.14)                                   | `cc153aae0db94e775e985f907c2bed88bad3d3e8c1660597412ca37905560037` |
| [system.text.encodings.web 10.0.14](https://www.nuget.org/packages/system.text.encodings.web/10.0.14)                       | `3db3934f595c30618e751c003852dc9b6406781653f9a1936c8df0203e379261` |
| [system.text.json 10.0.14](https://www.nuget.org/packages/system.text.json/10.0.14)                                         | `efb4116efc788602d167da9bc086c65d97cb32fd4624110eb00e3ff88e57c8c8` |
| [system.collections.immutable 10.0.14](https://www.nuget.org/packages/system.collections.immutable/10.0.14)                 | `ae295dafa95728eca04110ed7d43ed939ae47693bd09aa85a3671b5eab499207` |
| [system.runtime.compilerservices.unsafe 6.1.2](https://www.nuget.org/packages/system.runtime.compilerservices.unsafe/6.1.2) | `5f6a7f53af3465f92beb6da873ebe0e496206c313313b98badee4355a6b25937` |

Download packages from NuGet's v3 flat container, verify their package hashes, then
extract only the named .NET Standard 2.0 members. Keep the matching XML files and
preserve existing importers. Do not copy another Unsafe DLL into this directory.

For System.Text.Json 10.0.14, the .NET Standard dependency group names companion
BCL 10.0.14, Buffers 4.6.1, Memory 4.6.3, Unsafe 6.1.2 and Tasks.Extensions 4.6.3.
Unity supplies the framework assemblies; a NuGet version alone does not establish
the actual loaded framework identity. Check the binder and supported editor/player
profiles when refreshing these assets. See the
[assembly ownership guide](../../docs/guides/bundled-assembly-conflicts.md).
