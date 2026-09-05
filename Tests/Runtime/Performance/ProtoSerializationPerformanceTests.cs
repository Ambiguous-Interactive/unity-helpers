// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using SerializerAlias = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed partial class ProtoSerializationPerformanceTests
    {
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
                Identifier = "2f3a9b4c-8d1f-4cba-8df7-2af00f5c6c1e",
                Description = new string('d', (i % 31) + 64),
                Blob = MakeBytes(blobSize, seed: i),
                Nested = MakeMedium(i, nestedLen),
            };

        private const int Iterations = 10_000;

        [Test, Timeout(0)]
        public void CompareSerializeSmallMediumLarge()
        {
            UnityEngine.Debug.Log(
                "| Payload | WallstopProto (ms) | protobuf-net (ms) | Speedup | Size (bytes) |"
            );
            UnityEngine.Debug.Log(
                "| ------- | ------------------:| ----------------:| -------:| ------------:|"
            );

            RunSerializeBenchmark("Small", () => MakeSmall(123), out int smallSize);
            RunSerializeBenchmark("Medium", () => MakeMedium(123, 16), out int medSize);
            RunSerializeBenchmark("Large", () => MakeLarge(123, 8 * 1024, 64), out int largeSize);
        }

        [Test, Timeout(0)]
        public void CompareDeserializeSmallMediumLarge()
        {
            UnityEngine.Debug.Log("| Payload | WallstopProto (ms) | protobuf-net (ms) | Speedup |");
            UnityEngine.Debug.Log("| ------- | ------------------:| ----------------:| -------:|");

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
            using MemoryStream protobufNetBuffer = new();

            _ = SerializerAlias.ProtoSerialize(sample, ref buffer);
            Serializer.Serialize(protobufNetBuffer, sample);

            /*
                The alias uses WallstopProto for these dual-annotated contracts; calling ProtoBuf.Serializer
                directly below is the reflection-based comparison.
            */
            Stopwatch sw = Stopwatch.StartNew();
            int written = 0;
            for (int i = 0; i < Iterations; ++i)
            {
                written = SerializerAlias.ProtoSerialize(sample, ref buffer);
            }
            sw.Stop();
            long wallstopProtoMs = sw.ElapsedMilliseconds;
            payloadSize = written;

            sw.Restart();
            for (int i = 0; i < Iterations; ++i)
            {
                protobufNetBuffer.Position = 0;
                protobufNetBuffer.SetLength(0);
                Serializer.Serialize(protobufNetBuffer, sample);
            }
            sw.Stop();
            long protobufNetMs = sw.ElapsedMilliseconds;

            double speedup =
                0 < wallstopProtoMs
                    ? (double)protobufNetMs / wallstopProtoMs
                    : double.PositiveInfinity;
            UnityEngine.Debug.Log(
                $"| {label} | {wallstopProtoMs, 18:N0} | {protobufNetMs, 16:N0} | {speedup, 7:0.00}x | {payloadSize, 12:N0} |"
            );
        }

        private static void RunDeserializeBenchmark<T>(string label, T payload)
        {
            byte[] data = SerializerAlias.ProtoSerialize(payload);
            using MemoryStream protobufNetBuffer = new(data, writable: false);

            _ = SerializerAlias.ProtoDeserialize<T>(data);
            _ = (T)Serializer.Deserialize(typeof(T), protobufNetBuffer);

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = SerializerAlias.ProtoDeserialize<T>(data);
            }
            sw.Stop();
            long wallstopProtoMs = sw.ElapsedMilliseconds;

            sw.Restart();
            for (int i = 0; i < Iterations; ++i)
            {
                protobufNetBuffer.Position = 0;
                _ = (T)Serializer.Deserialize(typeof(T), protobufNetBuffer);
            }
            sw.Stop();
            long protobufNetMs = sw.ElapsedMilliseconds;

            double speedup =
                0 < wallstopProtoMs
                    ? (double)protobufNetMs / wallstopProtoMs
                    : double.PositiveInfinity;
            UnityEngine.Debug.Log(
                $"| {label} | {wallstopProtoMs, 18:N0} | {protobufNetMs, 16:N0} | {speedup, 7:0.00}x |"
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

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class SmallMsg
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            [WProtoMember(2)]
            public string Name { get; set; }
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class MediumMsg
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            [WProtoMember(2)]
            public string Name { get; set; }

            [ProtoMember(3)]
            [WProtoMember(3)]
            public int[] Values { get; set; }
        }

        [ProtoContract]
        [WProtoContract]
        internal sealed partial class LargeMsg
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public string Identifier { get; set; }

            [ProtoMember(2)]
            [WProtoMember(2)]
            public string Description { get; set; }

            [ProtoMember(3)]
            [WProtoMember(3)]
            public byte[] Blob { get; set; }

            [ProtoMember(4)]
            [WProtoMember(4)]
            public MediumMsg Nested { get; set; }
        }
    }
}
