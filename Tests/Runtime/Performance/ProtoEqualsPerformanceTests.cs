// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Extension;

    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ProtoEqualsPerformanceTests
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
                Name = new string('x', (i % 13) + 8),
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
        public void CompareProtoEqualsSmallMediumLarge()
        {
            UnityEngine.Debug.Log(
                "| Payload | Optimized ProtoEquals (ms) | Classic ProtoEquals (ms) | Speedup |"
            );
            UnityEngine.Debug.Log(
                "| ------- | -------------------------:| ------------------------:| -------:|"
            );

            RunEqualsBenchmark("Small", () => MakeSmall(123));
            RunEqualsBenchmark("Medium", () => MakeMedium(123, 16));
            RunEqualsBenchmark("Large", () => MakeLarge(123, 8 * 1024, 64));
        }

        private static void RunEqualsBenchmark<T>(string label, Func<T> factory)
        {
            T a = factory();
            T b = factory();

            // Warmup
            _ = a.ProtoEquals(b);
            _ = ClassicProtoEquals(a, b);

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = a.ProtoEquals(b);
            }
            sw.Stop();
            long optimizedMs = sw.ElapsedMilliseconds;

            sw.Restart();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = ClassicProtoEquals(a, b);
            }
            sw.Stop();
            long classicMs = sw.ElapsedMilliseconds;

            double speedup =
                classicMs > 0 ? (double)classicMs / optimizedMs : double.PositiveInfinity;
            UnityEngine.Debug.Log(
                $"| {label} | {optimizedMs, 25:N0} | {classicMs, 23:N0} | {speedup, 7:0.00}x |"
            );
        }

        private static bool ClassicProtoEquals<T>(T a, T b)
        {
            using MemoryStream ams = new();
            using MemoryStream bms = new();
            Serializer.Serialize(ams, a);
            Serializer.Serialize(bms, b);
            byte[] ab = ams.ToArray();
            byte[] bb = bms.ToArray();
            if (ab.Length != bb.Length)
            {
                return false;
            }
            ReadOnlySpan<byte> sa = new(ab);
            ReadOnlySpan<byte> sb = new(bb);
            return sa.SequenceEqual(sb);
        }

        private static int[] MakeIntArray(int len, int seed)
        {
            int[] arr = new int[len];
            int x = seed;
            for (int i = 0; i < len; ++i)
            {
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
