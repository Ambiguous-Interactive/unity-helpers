// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Serialization;

    [Category("Performance")]
    public sealed partial class ProtoSerializationPerformanceTests
    {
        private static void WriteCorpusRuntimeIdentity(string operation)
        {
            string runId = "unbound";
            string declarationSha256 = "unbound";
            string corpusSha256 = "unbound";
            string sourceCommit = "unbound";
            ReadFrozenAnchor(ref runId, ref declarationSha256, ref corpusSha256, ref sourceCommit);
            using MemoryStream stream = new MemoryStream();
            Utf8JsonWriter writer = new Utf8JsonWriter(stream);
            try
            {
                writer.WriteStartObject();
                writer.WriteNumber("SchemaVersion", 1);
                writer.WriteString("FrozenRunId", runId);
                writer.WriteString("FrozenDeclarationSha256", declarationSha256);
                writer.WriteString("FrozenCorpusSha256", corpusSha256);
                writer.WriteString("SourceCommit", sourceCommit);
                writer.WriteStartObject("RuntimeIdentity");
                writer.WriteString("UnityVersion", UnityEngine.Application.unityVersion);
                writer.WriteString("Backend", BenchmarkRuntimeIdentity.Backend);
                writer.WriteString("Platform", UnityEngine.Application.platform.ToString());
                writer.WriteNumber("ProcessBits", IntPtr.Size * 8);
                writer.WriteBoolean("IsEditor", UnityEngine.Application.isEditor);
                writer.WriteBoolean("DevelopmentBuild", UnityEngine.Debug.isDebugBuild);
#if WALLSTOP_PROTO
                writer.WriteBoolean("WallstopProto", true);
#else
                writer.WriteBoolean("WallstopProto", false);
#endif
                writer.WriteEndObject();
                writer.WriteStartArray("OracleAssemblies");
                WriteOracleIdentity(writer, typeof(ProtoBuf.Serializer).Assembly);
                WriteOracleIdentity(writer, typeof(ProtoBuf.ProtoReader).Assembly);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            finally
            {
                writer.Dispose();
            }
            string record = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
            UnityEngine.Debug.Log(
                $"WPROTO_CORPUS_RUNTIME {WProtoBenchmarkCorpus.CorpusVersion} {operation} {record}"
            );
        }

        private static void WriteOracleIdentity(Utf8JsonWriter writer, Assembly assembly)
        {
            AssemblyName identity = assembly.GetName();
            using FileStream bytes = File.OpenRead(assembly.Location);
            using SHA256 hash = SHA256.Create();
            string digest = BitConverter
                .ToString(hash.ComputeHash(bytes))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
            writer.WriteStartObject();
            writer.WriteString("Name", identity.Name);
            writer.WriteString("Version", identity.Version.ToString());
            writer.WriteString("Sha256", digest);
            writer.WriteEndObject();
        }

        static partial void ReadFrozenAnchor(
            ref string runId,
            ref string declarationSha256,
            ref string corpusSha256,
            ref string sourceCommit
        );

        [TestCase(WProtoBenchmarkCorpus.ReadMemory)]
        [TestCase(WProtoBenchmarkCorpus.ReadStream)]
        [TestCase(WProtoBenchmarkCorpus.ReadFinite)]
        [TestCase(WProtoBenchmarkCorpus.ReadDirect)]
        [TestCase(WProtoBenchmarkCorpus.ReadPublic)]
        [TestCase(WProtoBenchmarkCorpus.WriteBuffer)]
        [TestCase(WProtoBenchmarkCorpus.WriteStream)]
        [TestCase(WProtoBenchmarkCorpus.WriteArray)]
        [TestCase(WProtoBenchmarkCorpus.WriteDirect)]
        [TestCase(WProtoBenchmarkCorpus.WritePublic)]
        [Explicit("Run the full calibrated corpus as a preregistered benchmark campaign.")]
        [Timeout(1_800_000)]
        public void CompareFrozenSevenFamilyCorpus(string operation)
        {
#if !ENABLE_IL2CPP
            WriteCorpusRuntimeIdentity(operation);
#endif
            RequireObservableClock(operation);
            WProtoBenchmarkCorpus.Measure(operation);
        }
    }
}
