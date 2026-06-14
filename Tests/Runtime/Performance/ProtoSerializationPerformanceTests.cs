// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf;
    using SerializerAlias = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ProtoSerializationPerformanceTests
    {
        [ProtoContract]
        private sealed class SmallMsg
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }
        }

        [ProtoContract]
        private sealed class MediumMsg
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }

            [ProtoMember(3)]
            public int[] Values { get; set; }
        }

        [ProtoContract]
        private sealed class LargeMsg
        {
            [ProtoMember(1)]
            public Guid Guid { get; set; }

            [ProtoMember(2)]
            public string Description { get; set; }

            [ProtoMember(3)]
            public byte[] Blob { get; set; }

            [ProtoMember(4)]
            public MediumMsg Nested { get; set; }
        }

        private static SmallMsg MakeSmall(int i) => new() { Id = i, Name = "Name_" + i };

        private static MediumMsg MakeMedium(int i, int len) =>
            new()
            {
                Id = i,
                Name = new string('x', (i % 17) + 8),
                Values = MakeIntArray(len, seed: i),
            };

        private static LargeMsg MakeLarge(int i, int blobSize, int nestedLen) =>
            new()
            {
                Guid = Guid.NewGuid(),
                Description = new string('d', (i % 31) + 64),
                Blob = MakeBytes(blobSize, seed: i),
                Nested = MakeMedium(i, nestedLen),
            };

        private const int Iterations = 10_000;

        [Test, Timeout(0)]
        public void CompareSerializeSmallMediumLarge()
        {
            UnityEngine.Debug.Log(
                "| Payload | Pooled Serialize (ms) | Classic Serialize (ms) | Speedup | Size (bytes) |"
            );
            UnityEngine.Debug.Log(
                "| ------- | ---------------------:| ---------------------:| -------:| ------------:|"
            );

            RunSerializeBenchmark("Small", () => MakeSmall(123), out int smallSize);
            RunSerializeBenchmark("Medium", () => MakeMedium(123, 16), out int medSize);
            RunSerializeBenchmark("Large", () => MakeLarge(123, 8 * 1024, 64), out int largeSize);
        }

        [Test, Timeout(0)]
        public void CompareDeserializeSmallMediumLarge()
        {
            UnityEngine.Debug.Log(
                "| Payload | Pooled Deserialize (ms) | Classic Deserialize (ms) | Speedup |"
            );
            UnityEngine.Debug.Log(
                "| ------- | -----------------------:| -----------------------:| -------:|"
            );

            RunDeserializeBenchmark("Small", MakeSmall(123));
            RunDeserializeBenchmark("Medium", MakeMedium(123, 16));
            RunDeserializeBenchmark("Large", MakeLarge(123, 8 * 1024, 64));
        }

        private static void RunSerializeBenchmark<T>(
            string label,
            Func<T> factory,
            out int payloadSize
        )
        {
            T sample = factory();
            byte[] buffer = null;

            // Warmup
            _ = SerializerAlias.ProtoSerialize(sample, ref buffer);
            using (MemoryStream warm = new())
            {
                Serializer.Serialize(warm, sample);
            }

            // Measure pooled
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = SerializerAlias.ProtoSerialize(sample, ref buffer);
            }
            sw.Stop();
            long pooledMs = sw.ElapsedMilliseconds;
            payloadSize = buffer?.Length ?? 0;

            // Measure classic
            sw.Restart();
            for (int i = 0; i < Iterations; ++i)
            {
                using MemoryStream ms = new();
                Serializer.Serialize(ms, sample);
                _ = ms.ToArray();
            }
            sw.Stop();
            long classicMs = sw.ElapsedMilliseconds;

            double speedup = classicMs > 0 ? (double)classicMs / pooledMs : double.PositiveInfinity;
            UnityEngine.Debug.Log(
                $"| {label} | {pooledMs, 23:N0} | {classicMs, 23:N0} | {speedup, 7:0.00}x | {payloadSize, 12:N0} |"
            );
        }

        private static void RunDeserializeBenchmark<T>(string label, T payload)
        {
            byte[] data = SerializerAlias.ProtoSerialize(payload);

            // Warmup
            _ = SerializerAlias.ProtoDeserialize<T>(data);
            using (MemoryStream warm = new(data, writable: false))
            {
                _ = (T)Serializer.Deserialize(typeof(T), warm);
            }

            // Measure pooled
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = SerializerAlias.ProtoDeserialize<T>(data);
            }
            sw.Stop();
            long pooledMs = sw.ElapsedMilliseconds;

            // Measure classic
            sw.Restart();
            for (int i = 0; i < Iterations; ++i)
            {
                using MemoryStream ms = new(data, writable: false);
                _ = (T)Serializer.Deserialize(typeof(T), ms);
            }
            sw.Stop();
            long classicMs = sw.ElapsedMilliseconds;

            double speedup = classicMs > 0 ? (double)classicMs / pooledMs : double.PositiveInfinity;
            UnityEngine.Debug.Log(
                $"| {label} | {pooledMs, 25:N0} | {classicMs, 25:N0} | {speedup, 7:0.00}x |"
            );
        }

        private static int[] MakeIntArray(int len, int seed)
        {
            int[] arr = new int[len];
            int x = seed;
            for (int i = 0; i < len; ++i)
            {
                // simple LCG for reproducibility
                x = unchecked(x * 1103515245 + 12345);
                arr[i] = x;
            }
            return arr;
        }

        private static byte[] MakeBytes(int len, int seed)
        {
            byte[] b = new byte[len];
            int x = seed;
            for (int i = 0; i < len; ++i)
            {
                x = unchecked(x * 1664525 + 1013904223);
                b[i] = (byte)(x >> 24);
            }
            return b;
        }
    }
}
