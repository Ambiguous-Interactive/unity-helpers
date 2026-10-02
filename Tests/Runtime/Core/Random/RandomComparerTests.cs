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

            private static T NotSupported<T>()
            {
                throw new NotSupportedException();
            }

            public int Next()
            {
                ++NextCallCount;
                if (!_values.TryDequeue(out int value))
                {
                    throw new InvalidOperationException("No more values configured");
                }

                return value;
            }

            public int Next(int max)
            {
                return NotSupported<int>();
            }

            public int Next(int min, int max)
            {
                return NotSupported<int>();
            }

            public uint NextUint()
            {
                return NotSupported<uint>();
            }

            public uint NextUint(uint max)
            {
                return NotSupported<uint>();
            }

            public uint NextUint(uint min, uint max)
            {
                return NotSupported<uint>();
            }

            public short NextShort()
            {
                return NotSupported<short>();
            }

            public short NextShort(short max)
            {
                return NotSupported<short>();
            }

            public short NextShort(short min, short max)
            {
                return NotSupported<short>();
            }

            public byte NextByte()
            {
                return NotSupported<byte>();
            }

            public byte NextByte(byte max)
            {
                return NotSupported<byte>();
            }

            public byte NextByte(byte min, byte max)
            {
                return NotSupported<byte>();
            }

            public long NextLong()
            {
                return NotSupported<long>();
            }

            public long NextLong(long max)
            {
                return NotSupported<long>();
            }

            public long NextLong(long min, long max)
            {
                return NotSupported<long>();
            }

            public ulong NextUlong()
            {
                return NotSupported<ulong>();
            }

            public ulong NextUlong(ulong max)
            {
                return NotSupported<ulong>();
            }

            public ulong NextUlong(ulong min, ulong max)
            {
                return NotSupported<ulong>();
            }

            public bool NextBool()
            {
                return NotSupported<bool>();
            }

            public void NextBytes(byte[] buffer)
            {
                throw new NotSupportedException();
            }

            public float NextFloat()
            {
                return NotSupported<float>();
            }

            public float NextFloat(float max)
            {
                return NotSupported<float>();
            }

            public float NextFloat(float min, float max)
            {
                return NotSupported<float>();
            }

            public double NextDouble()
            {
                return NotSupported<double>();
            }

            public double NextDouble(double max)
            {
                return NotSupported<double>();
            }

            public double NextDouble(double min, double max)
            {
                return NotSupported<double>();
            }

            public double NextGaussian(double mean, double stdDev)
            {
                return NotSupported<double>();
            }

            public Guid NextGuid()
            {
                return NotSupported<Guid>();
            }

            public WGuid NextWGuid()
            {
                return NotSupported<WGuid>();
            }

            public T NextOf<T>(IEnumerable<T> enumerable)
            {
                return NotSupported<T>();
            }

            public T NextOf<T>(IReadOnlyCollection<T> collection)
            {
                return NotSupported<T>();
            }

            public T NextOf<T>(IReadOnlyList<T> list)
            {
                return NotSupported<T>();
            }

            public T NextOfParams<T>(params T[] elements)
            {
                return NotSupported<T>();
            }

            public T NextEnum<T>()
                where T : unmanaged, Enum
            {
                return NotSupported<T>();
            }

            public T NextEnumExcept<T>(T exception1)
                where T : unmanaged, Enum
            {
                return NotSupported<T>();
            }

            public T NextEnumExcept<T>(T exception1, T exception2)
                where T : unmanaged, Enum
            {
                return NotSupported<T>();
            }

            public T NextEnumExcept<T>(T exception1, T exception2, T exception3)
                where T : unmanaged, Enum
            {
                return NotSupported<T>();
            }

            public T NextEnumExcept<T>(T exception1, T exception2, T exception3, T exception4)
                where T : unmanaged, Enum
            {
                return NotSupported<T>();
            }

            public T NextEnumExcept<T>(
                T exception1,
                T exception2,
                T exception3,
                T exception4,
                params T[] exceptions
            )
                where T : unmanaged, Enum
            {
                return NotSupported<T>();
            }

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
            )
            {
                return NotSupported<float[,]>();
            }

            public IRandom Copy()
            {
                return NotSupported<IRandom>();
            }
        }
    }
}
