// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.TestDoubles
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Random;

    /// <summary>
    /// Deterministic <see cref="IRandom"/> implementation that returns configured float/double samples.
    /// Used to simulate edge conditions (always 0/1, overflows, limited call budgets) in unit tests.
    /// </summary>
    public sealed class EdgeCaseRandom : IRandom
    {
        private readonly Queue<float> _floatSequence;
        private readonly Queue<double> _doubleSequence;
        private readonly float _floatFallback;
        private readonly double _doubleFallback;
        private readonly int _maxFloatCalls;
        private readonly int _maxDoubleCalls;
        private int _floatCalls;
        private int _doubleCalls;

        public EdgeCaseRandom(
            IEnumerable<float> floatSequence = null,
            IEnumerable<double> doubleSequence = null,
            float floatFallback = 0f,
            double doubleFallback = 0d,
            int maxFloatCalls = int.MaxValue,
            int maxDoubleCalls = int.MaxValue
        )
        {
            _floatSequence = floatSequence != null ? new Queue<float>(floatSequence) : null;
            _doubleSequence = doubleSequence != null ? new Queue<double>(doubleSequence) : null;
            _floatFallback = floatFallback;
            _doubleFallback = doubleFallback;
            _maxFloatCalls = maxFloatCalls;
            _maxDoubleCalls = maxDoubleCalls;
        }

        public RandomState InternalState => throw new NotSupportedException();

        private float SampleFloat()
        {
            if (_maxFloatCalls < ++_floatCalls)
            {
                throw new InvalidOperationException("Exceeded configured float call budget.");
            }

            if (_floatSequence != null && 0 < _floatSequence.Count)
            {
                return _floatSequence.Dequeue();
            }

            return _floatFallback;
        }

        private double SampleDouble()
        {
            if (_maxDoubleCalls < ++_doubleCalls)
            {
                throw new InvalidOperationException("Exceeded configured double call budget.");
            }

            if (_doubleSequence != null && 0 < _doubleSequence.Count)
            {
                return _doubleSequence.Dequeue();
            }

            return _doubleFallback;
        }

        public int Next()
        {
            throw new NotSupportedException();
        }

        public int Next(int max)
        {
            throw new NotSupportedException();
        }

        public int Next(int min, int max)
        {
            throw new NotSupportedException();
        }

        public uint NextUint()
        {
            throw new NotSupportedException();
        }

        public uint NextUint(uint max)
        {
            throw new NotSupportedException();
        }

        public uint NextUint(uint min, uint max)
        {
            throw new NotSupportedException();
        }

        public short NextShort()
        {
            throw new NotSupportedException();
        }

        public short NextShort(short max)
        {
            throw new NotSupportedException();
        }

        public short NextShort(short min, short max)
        {
            throw new NotSupportedException();
        }

        public byte NextByte()
        {
            throw new NotSupportedException();
        }

        public byte NextByte(byte max)
        {
            throw new NotSupportedException();
        }

        public byte NextByte(byte min, byte max)
        {
            throw new NotSupportedException();
        }

        public long NextLong()
        {
            throw new NotSupportedException();
        }

        public long NextLong(long max)
        {
            throw new NotSupportedException();
        }

        public long NextLong(long min, long max)
        {
            throw new NotSupportedException();
        }

        public ulong NextUlong()
        {
            throw new NotSupportedException();
        }

        public ulong NextUlong(ulong max)
        {
            throw new NotSupportedException();
        }

        public ulong NextUlong(ulong min, ulong max)
        {
            throw new NotSupportedException();
        }

        public bool NextBool()
        {
            throw new NotSupportedException();
        }

        public void NextBytes(byte[] buffer)
        {
            throw new NotSupportedException();
        }

        public void NextBytes(Span<byte> buffer)
        {
            throw new NotSupportedException();
        }

        public float NextFloat()
        {
            return SampleFloat();
        }

        public float NextFloat(float max)
        {
            float sample = SampleFloat();
            return 1f <= sample ? max : max * sample;
        }

        public float NextFloat(float min, float max)
        {
            float sample = SampleFloat();
            if (sample <= 0f)
            {
                return min;
            }

            if (1f <= sample)
            {
                return max;
            }

            return min + (max - min) * sample;
        }

        public double NextDouble()
        {
            return SampleDouble();
        }

        public double NextDouble(double max)
        {
            double sample = SampleDouble();
            return 1d <= sample ? max : max * sample;
        }

        public double NextDouble(double min, double max)
        {
            double sample = SampleDouble();
            if (sample <= 0d)
            {
                return min;
            }

            if (1d <= sample)
            {
                return max;
            }

            return min + (max - min) * sample;
        }

        public double NextGaussian(double mean = 0, double stdDev = 1)
        {
            throw new NotSupportedException();
        }

        public Guid NextGuid()
        {
            throw new NotSupportedException();
        }

        public WGuid NextWGuid()
        {
            throw new NotSupportedException();
        }

        public T NextOf<T>(IEnumerable<T> enumerable)
        {
            throw new NotSupportedException();
        }

        public T NextOf<T>(IReadOnlyCollection<T> collection)
        {
            throw new NotSupportedException();
        }

        public T NextOf<T>(IReadOnlyList<T> list)
        {
            throw new NotSupportedException();
        }

        public T NextOfParams<T>(params T[] elements)
        {
            throw new NotSupportedException();
        }

        public T NextEnum<T>()
            where T : unmanaged, Enum
        {
            throw new NotSupportedException();
        }

        public T NextEnumExcept<T>(T exception1)
            where T : unmanaged, Enum
        {
            throw new NotSupportedException();
        }

        public T NextEnumExcept<T>(T exception1, T exception2)
            where T : unmanaged, Enum
        {
            throw new NotSupportedException();
        }

        public T NextEnumExcept<T>(T exception1, T exception2, T exception3)
            where T : unmanaged, Enum
        {
            throw new NotSupportedException();
        }

        public T NextEnumExcept<T>(T exception1, T exception2, T exception3, T exception4)
            where T : unmanaged, Enum
        {
            throw new NotSupportedException();
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
            throw new NotSupportedException();
        }

        public float[,] NextNoiseMap(
            float[,] noiseMap,
            PerlinNoise noise = null,
            float scale = 2.5f,
            int octaves = 8,
            float persistence = 0.5f,
            float lacunarity = 2f,
            Vector2 baseOffset = default,
            float octaveOffsetRange = 100000f,
            bool normalize = true
        )
        {
            throw new NotSupportedException();
        }

        public IRandom Copy()
        {
            throw new NotSupportedException();
        }
    }
}
