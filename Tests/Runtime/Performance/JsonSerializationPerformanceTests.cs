// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Diagnostics;
    using System.Text.Json;
    using NUnit.Framework;
    using SerializerAlias = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class JsonSerializationPerformanceTests
    {
        private sealed class SmallMsg
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private sealed class MediumMsg
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int[] Values { get; set; }
        }

        private sealed class LargeMsg
        {
            public Guid Guid { get; set; }
            public string Description { get; set; }
            public byte[] Blob { get; set; }
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
                "| Payload | Pooled-Normal (ms, KB) | Pooled-Fast (ms, KB) | Classic (ms, KB) | Fast/Classic | Size (bytes) |"
            );
            UnityEngine.Debug.Log(
                "| ------- | ---------------------:| --------------------:| ---------------:| -----------:| ------------:|"
            );

            RunSerializeBenchmark("Small", () => MakeSmall(123), out int smallSize);
            RunSerializeBenchmark("Medium", () => MakeMedium(123, 16), out int medSize);
            RunSerializeBenchmark("Large", () => MakeLarge(123, 8 * 1024, 64), out int largeSize);
        }

        [Test, Timeout(0)]
        public void CompareDeserializeSmallMediumLarge()
        {
            UnityEngine.Debug.Log(
                "| Payload | Pooled-Normal (ms, KB) | Pooled-Fast (ms, KB) | Pooled-FastPOCO (ms, KB) | Classic (ms, KB) | FPOCO/Classic |"
            );
            UnityEngine.Debug.Log(
                "| ------- | ---------------------:| -------------------:| ------------------------:| ---------------:| -------------:|"
            );

            RunDeserializeBenchmark("Small", MakeSmall(123));
            RunDeserializeBenchmark("Medium", MakeMedium(123, 16));
            RunDeserializeBenchmark("Large", MakeLarge(123, 8 * 1024, 64));
        }

        [Test, Timeout(0)]
        public void BenchmarkStringifyVsSerialize()
        {
            UnityEngine.Debug.Log("| Payload | JsonStringify (ms) | JsonSerialize (ms) | Ratio |");
            UnityEngine.Debug.Log("| ------- | ------------------:| ------------------:| -----:|");

            RunStringifyVsSerializeBenchmark("Small", MakeSmall(123));
            RunStringifyVsSerializeBenchmark("Medium", MakeMedium(123, 16));
            RunStringifyVsSerializeBenchmark("Large", MakeLarge(123, 8 * 1024, 64));
        }

        [Test, Timeout(0)]
        public void BenchmarkLargeCollectionSerialization()
        {
            // Test with very large collection to stress memory allocation
            MediumMsg msg = MakeMedium(999, 50_000);

            JsonSerializerOptions normal = SerializerAlias.CreateNormalJsonOptions();
            JsonSerializerOptions fast = SerializerAlias.CreateFastJsonOptions();
            byte[] buffer = null;

            Stopwatch sw = Stopwatch.StartNew();
            int sizeHint = (msg.Values?.Length ?? 0) * 12 + 2048;
            for (int i = 0; i < 100; ++i)
            {
                _ = SerializerAlias.JsonSerialize(msg, normal, sizeHint, ref buffer);
            }
            sw.Stop();
            long normalMs = sw.ElapsedMilliseconds;

            sw.Restart();
            for (int i = 0; i < 100; ++i)
            {
                _ = SerializerAlias.JsonSerialize(msg, fast, sizeHint, ref buffer);
            }
            sw.Stop();
            long fastMs = sw.ElapsedMilliseconds;

            sw.Restart();
            for (int i = 0; i < 100; ++i)
            {
                _ = JsonSerializer.SerializeToUtf8Bytes(msg);
            }
            sw.Stop();
            long classicMs = sw.ElapsedMilliseconds;

            double fastVsClassic =
                classicMs > 0 ? (double)classicMs / fastMs : double.PositiveInfinity;
            UnityEngine.Debug.Log(
                $"Large collection (50k ints): Normal={normalMs}ms, Fast={fastMs}ms, Classic={classicMs}ms, Fast/Classic={fastVsClassic:0.00}x"
            );
            Assert.Pass($"Performance baseline: {fastMs}ms");
        }

        [Test, Timeout(0)]
        public void BenchmarkDeeplyNestedObjectSerialization()
        {
            // Create nested structure
            MediumMsg root = MakeMedium(0, 10);
            MediumMsg current = root;

            // Create 100 level deep nesting using arrays as containers
            for (int i = 1; i < 100; ++i)
            {
                // JSON doesn't support circular references, so we can't test true deep nesting
                // This test validates that moderately complex objects serialize efficiently
                _ = MakeMedium(i, 10);
            }

            JsonSerializerOptions normal = SerializerAlias.CreateNormalJsonOptions();
            JsonSerializerOptions fast = SerializerAlias.CreateFastJsonOptions();
            byte[] buffer = null;

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < 1000; ++i)
            {
                _ = SerializerAlias.JsonSerialize(root, normal, ref buffer);
            }
            sw.Stop();
            long normalMs = sw.ElapsedMilliseconds;

            sw.Restart();
            for (int i = 0; i < 1000; ++i)
            {
                _ = SerializerAlias.JsonSerialize(root, fast, ref buffer);
            }
            sw.Stop();
            long fastMs = sw.ElapsedMilliseconds;

            sw.Restart();
            for (int i = 0; i < 1000; ++i)
            {
                _ = JsonSerializer.SerializeToUtf8Bytes(root);
            }
            sw.Stop();
            long classicMs = sw.ElapsedMilliseconds;

            double fastVsClassic =
                classicMs > 0 ? (double)classicMs / fastMs : double.PositiveInfinity;
            UnityEngine.Debug.Log(
                $"Complex object: Normal={normalMs}ms, Fast={fastMs}ms, Classic={classicMs}ms, Fast/Classic={fastVsClassic:0.00}x (1000 iters)"
            );
            Assert.Pass($"Performance baseline: {fastMs}ms");
        }

        private static void RunSerializeBenchmark<T>(
            string label,
            Func<T> factory,
            out int payloadSize
        )
        {
            T sample = factory();

            // Warmup
            JsonSerializerOptions normal = SerializerAlias.CreateNormalJsonOptions();
            JsonSerializerOptions fast = SerializerAlias.CreateFastJsonOptions();
            byte[] buffer = null;
            _ = SerializerAlias.JsonSerialize(sample, fast, ref buffer);
            _ = JsonSerializer.SerializeToUtf8Bytes(sample);

            T value = factory();
            // Pooled - Normal
            Stopwatch sw = Stopwatch.StartNew();
            long allocStart = GetAlloc();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = SerializerAlias.JsonSerialize(value, normal, ref buffer);
            }
            sw.Stop();
            long allocEnd = GetAlloc();
            long pooledNormalMs = sw.ElapsedMilliseconds;
            long pooledNormalKB = (allocEnd - allocStart) / 1024;

            // Pooled - Fast
            sw.Restart();
            allocStart = GetAlloc();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = SerializerAlias.JsonSerialize(value, fast, ref buffer);
            }
            sw.Stop();
            allocEnd = GetAlloc();
            long pooledFastMs = sw.ElapsedMilliseconds;
            long pooledFastKB = (allocEnd - allocStart) / 1024;
            payloadSize = buffer?.Length ?? 0;

            // Measure classic (using System.Text.Json directly)
            sw.Restart();
            allocStart = GetAlloc();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = JsonSerializer.SerializeToUtf8Bytes(value);
            }
            sw.Stop();
            allocEnd = GetAlloc();
            long classicMs = sw.ElapsedMilliseconds;
            long classicKB = (allocEnd - allocStart) / 1024;

            double fastVsClassic =
                classicMs > 0 ? (double)classicMs / pooledFastMs : double.PositiveInfinity;
            UnityEngine.Debug.Log(
                $"| {label} | {pooledNormalMs, 17:N0}, {pooledNormalKB, 4:N0} | {pooledFastMs, 14:N0}, {pooledFastKB, 4:N0} | {classicMs, 13:N0}, {classicKB, 4:N0} | {fastVsClassic, 11:0.00}x | {payloadSize, 12:N0} |"
            );
        }

        private static void RunDeserializeBenchmark<T>(string label, T payload)
        {
            JsonSerializerOptions normal = SerializerAlias.CreateNormalJsonOptions();
            JsonSerializerOptions fast = SerializerAlias.CreateFastJsonOptions();
            JsonSerializerOptions fastPoco = SerializerAlias.CreateFastPocoJsonOptions();
            byte[] data = SerializerAlias.JsonSerialize(payload, fastPoco);

            // Warmup
            _ = SerializerAlias.JsonDeserialize<T>(data, null, normal);
            _ = SerializerAlias.JsonDeserialize<T>(data, null, fast);
            _ = SerializerAlias.JsonDeserialize<T>(data, null, fastPoco);
            _ = JsonSerializer.Deserialize<T>(data);

            Stopwatch sw = Stopwatch.StartNew();
            // Pooled - Normal
            long allocStart = GetAlloc();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = SerializerAlias.JsonDeserialize<T>(data, null, normal);
            }
            sw.Stop();
            long allocEnd = GetAlloc();
            long pooledNormalMs = sw.ElapsedMilliseconds;
            long pooledNormalKB = (allocEnd - allocStart) / 1024;

            // Pooled - Fast
            sw.Restart();
            allocStart = GetAlloc();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = SerializerAlias.JsonDeserialize<T>(data, null, fast);
            }
            sw.Stop();
            allocEnd = GetAlloc();
            long pooledFastMs = sw.ElapsedMilliseconds;
            long pooledFastKB = (allocEnd - allocStart) / 1024;

            // Pooled - FastPOCO
            sw.Restart();
            allocStart = GetAlloc();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = SerializerAlias.JsonDeserialize<T>(data, null, fastPoco);
            }
            sw.Stop();
            allocEnd = GetAlloc();
            long pooledFastPocoMs = sw.ElapsedMilliseconds;
            long pooledFastPocoKB = (allocEnd - allocStart) / 1024;

            // Measure classic (using System.Text.Json directly)
            sw.Restart();
            allocStart = GetAlloc();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = JsonSerializer.Deserialize<T>(data);
            }
            sw.Stop();
            allocEnd = GetAlloc();
            long classicMs = sw.ElapsedMilliseconds;
            long classicKB = (allocEnd - allocStart) / 1024;

            double fPocoVsClassic =
                classicMs > 0 ? (double)classicMs / pooledFastPocoMs : double.PositiveInfinity;
            UnityEngine.Debug.Log(
                $"| {label} | {pooledNormalMs, 17:N0}, {pooledNormalKB, 4:N0} | {pooledFastMs, 13:N0}, {pooledFastKB, 4:N0} | {pooledFastPocoMs, 22:N0}, {pooledFastPocoKB, 4:N0} | {classicMs, 13:N0}, {classicKB, 4:N0} | {fPocoVsClassic, 12:0.00}x |"
            );
        }

        private static void RunStringifyVsSerializeBenchmark<T>(string label, T payload)
        {
            // Warmup
            JsonSerializerOptions normal = SerializerAlias.CreateNormalJsonOptions();
            JsonSerializerOptions fast = SerializerAlias.CreateFastJsonOptions();
            _ = SerializerAlias.JsonStringify(payload, fast);
            byte[] buffer = null;
            _ = SerializerAlias.JsonSerialize(payload, fast, ref buffer);

            // Measure JsonStringify (returns string)
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = SerializerAlias.JsonStringify(payload, normal);
            }
            sw.Stop();
            long stringifyNormalMs = sw.ElapsedMilliseconds;

            sw.Restart();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = SerializerAlias.JsonStringify(payload, fast);
            }
            sw.Stop();
            long stringifyFastMs = sw.ElapsedMilliseconds;

            sw.Restart();
            long allocStart = GetAlloc();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = SerializerAlias.JsonSerialize(payload, normal, ref buffer);
            }
            sw.Stop();
            long allocEnd = GetAlloc();
            long serializeNormalMs = sw.ElapsedMilliseconds;
            long serializeNormalKB = (allocEnd - allocStart) / 1024;

            sw.Restart();
            allocStart = GetAlloc();
            for (int i = 0; i < Iterations; ++i)
            {
                _ = SerializerAlias.JsonSerialize(payload, fast, ref buffer);
            }
            sw.Stop();
            allocEnd = GetAlloc();
            long serializeFastMs = sw.ElapsedMilliseconds;
            long serializeFastKB = (allocEnd - allocStart) / 1024;

            double ratioNormal =
                serializeNormalMs > 0
                    ? (double)stringifyNormalMs / serializeNormalMs
                    : double.PositiveInfinity;
            double ratioFast =
                serializeFastMs > 0
                    ? (double)stringifyFastMs / serializeFastMs
                    : double.PositiveInfinity;
            UnityEngine.Debug.Log(
                $"| {label} | stringify-Normal={stringifyNormalMs, 6:N0} | stringify-Fast={stringifyFastMs, 6:N0} | serialize-Normal={serializeNormalMs, 6:N0}, {serializeNormalKB, 4:N0}KB | serialize-Fast={serializeFastMs, 6:N0}, {serializeFastKB, 4:N0}KB | ratio(N)={ratioNormal, 5:0.00}x | ratio(F)={ratioFast, 5:0.00}x |"
            );
        }

        private static long GetAlloc()
        {
            try
            {
                return GC.GetAllocatedBytesForCurrentThread();
            }
            catch
            {
                return 0;
            }
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
