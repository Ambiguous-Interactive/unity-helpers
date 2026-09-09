// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.Random
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RandomComparerTests
    {
        [Test]
        public void CompareCachesGeneratedValuesPerElement()
        {
            CountingRandom random = new(50, 10, 20);
            RandomComparer<string> comparer = new(random);

            int firstComparison = comparer.Compare("left", "right");
            int secondComparison = comparer.Compare("left", "right");

            Assert.AreEqual(firstComparison, secondComparison);
            Assert.AreEqual(2, random.NextCallCount);
        }

        [Test]
        public void CompareAssignsNewValuesToPreviouslyUnseenElements()
        {
            CountingRandom random = new(10, 100, 5);
            RandomComparer<string> comparer = new(random);

            _ = comparer.Compare("alpha", "beta");
            Assert.AreEqual(2, random.NextCallCount);

            _ = comparer.Compare("alpha", "gamma");
            Assert.AreEqual(3, random.NextCallCount);
        }

        private sealed class CountingRandom : IRandom
        {
            public int NextCallCount { get; private set; }

            public RandomState InternalState => default;

            private readonly Queue<int> _values;

            public CountingRandom(params int[] values)
            {
                _values = new Queue<int>(values);
            }

            private static T NotSupported<T>() => throw new NotSupportedException();

            public int Next()
            {
                NextCallCount++;
                if (_values.Count == 0)
                {
                    throw new InvalidOperationException("No more values configured");
                }

                return _values.Dequeue();
            }

            public int Next(int max) => NotSupported<int>();

            public int Next(int min, int max) => NotSupported<int>();

            public uint NextUint() => NotSupported<uint>();

            public uint NextUint(uint max) => NotSupported<uint>();

            public uint NextUint(uint min, uint max) => NotSupported<uint>();

            public short NextShort() => NotSupported<short>();

            public short NextShort(short max) => NotSupported<short>();

            public short NextShort(short min, short max) => NotSupported<short>();

            public byte NextByte() => NotSupported<byte>();

            public byte NextByte(byte max) => NotSupported<byte>();

            public byte NextByte(byte min, byte max) => NotSupported<byte>();

            public long NextLong() => NotSupported<long>();

            public long NextLong(long max) => NotSupported<long>();

            public long NextLong(long min, long max) => NotSupported<long>();

            public ulong NextUlong() => NotSupported<ulong>();

            public ulong NextUlong(ulong max) => NotSupported<ulong>();

            public ulong NextUlong(ulong min, ulong max) => NotSupported<ulong>();

            public bool NextBool() => NotSupported<bool>();

            public void NextBytes(byte[] buffer) => throw new NotSupportedException();

            public float NextFloat() => NotSupported<float>();

            public float NextFloat(float max) => NotSupported<float>();

            public float NextFloat(float min, float max) => NotSupported<float>();

            public double NextDouble() => NotSupported<double>();

            public double NextDouble(double max) => NotSupported<double>();

            public double NextDouble(double min, double max) => NotSupported<double>();

            public double NextGaussian(double mean, double stdDev) => NotSupported<double>();

            public Guid NextGuid() => NotSupported<Guid>();

            public WGuid NextWGuid() => NotSupported<WGuid>();

            public T NextOf<T>(IEnumerable<T> enumerable) => NotSupported<T>();

            public T NextOf<T>(IReadOnlyCollection<T> collection) => NotSupported<T>();

            public T NextOf<T>(IReadOnlyList<T> list) => NotSupported<T>();

            public T NextOfParams<T>(params T[] elements) => NotSupported<T>();

            public T NextEnum<T>()
                where T : unmanaged, Enum => NotSupported<T>();

            public T NextEnumExcept<T>(T exception1)
                where T : unmanaged, Enum => NotSupported<T>();

            public T NextEnumExcept<T>(T exception1, T exception2)
                where T : unmanaged, Enum => NotSupported<T>();

            public T NextEnumExcept<T>(T exception1, T exception2, T exception3)
                where T : unmanaged, Enum => NotSupported<T>();

            public T NextEnumExcept<T>(T exception1, T exception2, T exception3, T exception4)
                where T : unmanaged, Enum => NotSupported<T>();

            public T NextEnumExcept<T>(
                T exception1,
                T exception2,
                T exception3,
                T exception4,
                params T[] exceptions
            )
                where T : unmanaged, Enum => NotSupported<T>();

            public float[,] NextNoiseMap(
                float[,] noiseMap,
                PerlinNoise noise = null,
                float scale = 2.5f,
                int octaves = 8,
                float persistence = 0.5f,
                float lacunarity = 2,
                UnityEngine.Vector2 baseOffset = default,
                float octaveOffsetRange = 100000,
                bool normalize = true
            ) => NotSupported<float[,]>();

            public IRandom Copy() => NotSupported<IRandom>();
        }
    }
}
