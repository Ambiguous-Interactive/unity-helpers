# Bundled .NET assemblies

The BCL and Immutable DLLs are the exact `lib/netstandard2.0` assets from official
NuGet 10.0.15 packages, source commit `8e0f8ed2312954727f8919d7d1c2f78b483b4ee7`.
Unsafe remains at 6.1.2. Existing plugin importers and GUIDs are preserved.
The four BCL XML files retain the official package content with byte order marks and trailing whitespace removed.

| DLL                                        | Package version | DLL SHA256                                                         |
| ------------------------------------------ | --------------- | ------------------------------------------------------------------ |
| Microsoft.Bcl.AsyncInterfaces.dll          | 10.0.15         | `dab404508025e77915c96c38f08bb8a30225e9680019f0b1048c83cac08aa5c9` |
| System.IO.Pipelines.dll                    | 10.0.15         | `f5f6772582f4333dc610fa8124c91a74a0f04bcb27080135eb392a26939ff31d` |
| System.Text.Encodings.Web.dll              | 10.0.15         | `8167744732c3b869f6f89db31e3f32f5bb1ca7e6e1b2d398ee3c80a34a024dbf` |
| System.Text.Json.dll                       | 10.0.15         | `03f0c580fd89f146edb2b5f871d3c88d3b583464364b1813ac04f58856989daa` |
| System.Collections.Immutable.dll           | 10.0.15         | `df380cef22673d80fea1ad89f3d029ed9de824cdf9a719875e4dd3d4c67456c7` |
| System.Runtime.CompilerServices.Unsafe.dll | 6.1.2           | `fd67abc4c4af10affbcd42fd68a2a7bc361b26714afe6965d4d0d139459d8297` |

## Package provenance

| NuGet package                                                                                                               | Package SHA256                                                     |
| --------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| [microsoft.bcl.asyncinterfaces 10.0.15](https://www.nuget.org/packages/microsoft.bcl.asyncinterfaces/10.0.15)               | `9145155abd79d138c5da8430f902c79173f9a896a154a385f9860b86574ed119` |
| [system.io.pipelines 10.0.15](https://www.nuget.org/packages/system.io.pipelines/10.0.15)                                   | `57018a1a267bc89091df9bf50e634c124f8f41b217aa23afe67617d512ff1836` |
| [system.text.encodings.web 10.0.15](https://www.nuget.org/packages/system.text.encodings.web/10.0.15)                       | `536a03c14089c17e0c52dd3e9b67c7cf93d0c5f73b25300da4a423c8f9b7fb89` |
| [system.text.json 10.0.15](https://www.nuget.org/packages/system.text.json/10.0.15)                                         | `6ff09d6665841718ed13d69074f6cc4470b54d4951731dceb4091f2518ef8890` |
| [system.collections.immutable 10.0.15](https://www.nuget.org/packages/system.collections.immutable/10.0.15)                 | `d5b088d590bb94d54d60629c149894c082878e3e0043a3bfedc48b3ccf2a5d76` |
| [system.runtime.compilerservices.unsafe 6.1.2](https://www.nuget.org/packages/system.runtime.compilerservices.unsafe/6.1.2) | `5f6a7f53af3465f92beb6da873ebe0e496206c313313b98badee4355a6b25937` |

Download packages from NuGet's v3 flat container, verify their package hashes, then
extract only the named .NET Standard 2.0 members. Keep the matching XML files and
preserve existing importers. Do not copy another Unsafe DLL into this directory.

For System.Text.Json 10.0.15, the .NET Standard dependency group names companion
BCL 10.0.15, Buffers 4.6.1, Memory 4.6.3, Unsafe 6.1.2 and Tasks.Extensions 4.6.3.
Unity supplies the framework assemblies; a NuGet version alone does not establish
the actual loaded framework identity. Check the binder and supported editor/player
profiles when refreshing these assets. See the
[assembly ownership guide](../../docs/guides/bundled-assembly-conflicts.md).
