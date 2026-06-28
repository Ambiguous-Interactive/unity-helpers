// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    public enum TestValues
    {
        Value0,
        Value1,
        Value2,
        Value3,
        Value4,
        Value5,
        Value6,
        Value7,
        Value8,
        Value9,
    }

    public abstract class RandomTestBase
    {
        private const int NumGeneratorChecks = 1_000;
        private const int NormalIterations = 1_000;

        // The distribution tests dominate suite runtime: each loops SampleCount
        // times across ~21 PRNG subclasses (~half a billion iterations at the
        // historical count). The fast default keeps the MAIN suite quick while
        // staying statistically valid (see the sqrt deviation floor in
        // TestAndVerify); the perf/stress CI job exports UH_RANDOM_SAMPLE_COUNT =
        // ThoroughSampleCount to restore the original, tighter bias-detection
        // sensitivity. Override the env var for a thorough local run.
        private const int DefaultFastSampleCount = 250_000;
        private const int ThoroughSampleCount = 12_750_000;
        private static readonly int SampleCount = ResolveSampleCount();

        // Floor for the allowed per-bin deviation, in standard deviations. Counts
        // over `sampleLength` bins are ~Poisson(average) with stddev sqrt(average),
        // so the natural max deviation grows like sqrt(average). A fixed RELATIVE
        // tolerance (GetDeviationFor) is correct at large N but turns flaky as N
        // shrinks; flooring at this many sigma keeps reduced-sample runs reliably
        // green while large-N runs keep their original tolerance unchanged.
        private const double DeviationSigmaFloor = 5.5;
        protected const uint DeterministicSeed32 = 0xC0FFEE11U;
        protected const ulong DeterministicSeed64 = 0x0123456789ABCDEFUL;
        protected const ulong DeterministicSeed64B = 0xF0E1D2C3B4A59687UL;
        protected const int DeterministicSeedInt = 0x1BADC0DE;
        protected static readonly Guid DeterministicGuid = new(
            "11223344-5566-7788-99AA-BBCCDDEEFF00"
        );

        private readonly int[] _samples = new int[1_000];

        protected abstract IRandom NewRandom();

        [SetUp]
        public virtual void Setup()
        {
            Array.Clear(_samples, 0, _samples.Length);
        }

        [TearDown]
        public virtual void Teardown()
        {
            // No-op in base
        }

        [Test]
        [Parallelizable]
        public void Bool()
        {
            TestAndVerify(
                random => Math.Min(_samples.Length - 1, Convert.ToInt32(random.NextBool())),
                maxLength: Math.Min(2, _samples.Length)
            );
        }

        [Test]
        [Parallelizable]
        public void Int()
        {
            TestAndVerify(random => random.Next(0, _samples.Length));
        }

        [Test]
        [Parallelizable]
        public void IntRaw()
        {
            int sampleLength = GetSampleLength();
            TestAndVerify(random =>
                (int)((1.0 * random.Next()) / (1.0 * int.MaxValue) * sampleLength)
            );
        }

        [Test]
        [Parallelizable]
        public void IntRange()
        {
            TestAndVerify(random => random.Next(_samples.Length));
        }

        [Test]
        [Parallelizable]
        public void IntDistribution()
        {
            TestAndVerify(random =>
                (int)(random.Next() / ((1.0 * int.MaxValue) / _samples.Length))
            );
        }

        [Test]
        [Parallelizable]
        public void IntMaxRange()
        {
            TestAndVerify(random =>
                (int)(
                    (random.Next(int.MinValue, int.MaxValue) + (-1.0 * int.MinValue))
                    / (1.0 * int.MaxValue - int.MinValue)
                    * _samples.Length
                )
            );
        }

        [Test]
        [Parallelizable]
        public void Uint()
        {
            TestAndVerify(random => (int)random.NextUint(0, (uint)_samples.Length));
        }

        [Test]
        [Parallelizable]
        public void UintRange()
        {
            TestAndVerify(random => (int)random.NextUint((uint)_samples.Length));
        }

        [Test]
        [Parallelizable]
        public void UintDistribution()
        {
            TestAndVerify(random =>
                (int)(random.NextUint() / ((1.0 * uint.MaxValue) / _samples.Length))
            );
        }

        [Test]
        [Parallelizable]
        public void UintMaxRange()
        {
            TestAndVerify(random =>
                (int)(
                    (random.NextUint(uint.MinValue, uint.MaxValue) + (1.0 * uint.MinValue))
                    / (1.0 * uint.MaxValue)
                    * _samples.Length
                )
            );
        }

        [Test]
        [Parallelizable]
        public void Short()
        {
            TestAndVerify(
                random => random.NextShort(0, (short)_samples.Length),
                maxLength: (short.MaxValue - short.MinValue)
            );
        }

        [Test]
        [Parallelizable]
        public void ShortRaw()
        {
            int sampleLength = GetSampleLength(short.MaxValue);
            TestAndVerify(
                random => (int)((1.0 * random.NextShort()) / (1.0 * short.MaxValue) * sampleLength),
                maxLength: short.MaxValue
            );
        }

        [Test]
        [Parallelizable]
        public void ShortRange()
        {
            TestAndVerify(
                random => random.NextShort((short)_samples.Length),
                maxLength: (short.MaxValue - short.MinValue)
            );
        }

        [Test]
        [Parallelizable]
        public void ShortMaxRange()
        {
            TestAndVerify(random =>
                (int)(
                    (random.NextShort(short.MinValue, short.MaxValue) + (-1.0 * short.MinValue))
                    / (1.0 * short.MaxValue - short.MinValue)
                    * _samples.Length
                )
            );
        }

        [Test]
        [Parallelizable]
        public void Byte()
        {
            TestAndVerify(
                random =>
                    random.NextByte(
                        0,
                        (byte)(_samples.Length < byte.MaxValue ? _samples.Length : byte.MaxValue)
                    ),
                byte.MaxValue
            );
        }

        [Test]
        [Parallelizable]
        public void ByteRaw()
        {
            int sampleLength = GetSampleLength(byte.MaxValue);
            TestAndVerify(
                random => (int)((1.0 * random.NextByte()) / (1.0 * byte.MaxValue) * sampleLength),
                maxLength: byte.MaxValue
            );
        }

        [Test]
        [Parallelizable]
        public void ByteRange()
        {
            TestAndVerify(
                random =>
                    random.NextByte(
                        (byte)(_samples.Length < byte.MaxValue ? _samples.Length : byte.MaxValue)
                    ),
                byte.MaxValue
            );
        }

        [Test]
        [Parallelizable]
        public void ByteMaxRange()
        {
            int sampleCount = Math.Min((byte.MaxValue - byte.MinValue), _samples.Length);
            TestAndVerify(
                random =>
                    Math.Clamp(
                        (int)(
                            (random.NextByte(byte.MinValue, byte.MaxValue) + (-1.0 * byte.MinValue))
                            / (1.0 * byte.MaxValue - byte.MinValue)
                            * sampleCount
                        ),
                        0,
                        sampleCount - 1
                    ),
                maxLength: sampleCount
            );
        }

        [Test]
        [Parallelizable]
        public void Float()
        {
            TestAndVerify(random =>
                Math.Clamp(
                    (int)Math.Floor(random.NextFloat(0, _samples.Length)),
                    0,
                    _samples.Length - 1
                )
            );
        }

        [Test]
        [Parallelizable]
        public void FloatRange()
        {
            TestAndVerify(random =>
                Math.Clamp(
                    (int)Math.Floor(random.NextFloat(_samples.Length)),
                    0,
                    _samples.Length - 1
                )
            );
        }

        [Test]
        [Parallelizable]
        public void FloatDistribution()
        {
            TestAndVerify(random => (int)(random.NextFloat() * _samples.Length));
        }

        [Test]
        [Parallelizable]
        public void FloatMaxRange()
        {
            TestAndVerify(random =>
                Math.Clamp(
                    (int)(
                        (random.NextFloat(float.MinValue, float.MaxValue) + (-1.0 * float.MinValue))
                        / (1.0 * float.MaxValue - float.MinValue)
                        * _samples.Length
                    ),
                    0,
                    _samples.Length - 1
                )
            );
        }

        [Test]
        [Parallelizable]
        public void Double()
        {
            TestAndVerify(random =>
                Math.Clamp(
                    (int)Math.Floor(random.NextDouble(0, _samples.Length)),
                    0,
                    _samples.Length - 1
                )
            );
        }

        [Test]
        [Parallelizable]
        public void DoubleRange()
        {
            TestAndVerify(random =>
                Math.Clamp(
                    (int)Math.Floor(random.NextDouble(_samples.Length)),
                    0,
                    _samples.Length - 1
                )
            );
        }

        [Test]
        [Parallelizable]
        public void DoubleDistribution()
        {
            TestAndVerify(random => (int)(random.NextDouble() * _samples.Length));
        }

        [Test]
        [Parallelizable]
        public void DoubleMaxRange()
        {
            IRandom random = NewRandom();
            for (int i = 0; i < SampleCount; ++i)
            {
                double value = random.NextDouble(double.MinValue, double.MaxValue);
                Assert.IsFalse(double.IsNaN(value));
                Assert.IsFalse(double.IsInfinity(value));
            }
        }

        [Test]
        [Parallelizable]
        public void Long()
        {
            TestAndVerify(random => (int)random.NextLong(0, _samples.Length));
        }

        [Test]
        [Parallelizable]
        public void LongRaw()
        {
            int sampleLength = GetSampleLength();
            TestAndVerify(random =>
                (int)(1.0 * random.NextLong() / (1.0 * long.MaxValue) * sampleLength)
            );
        }

        [Test]
        [Parallelizable]
        public void LongRange()
        {
            TestAndVerify(random => (int)random.NextLong(_samples.Length));
        }

        [Test]
        [Parallelizable]
        public void LongMaxRange()
        {
            TestAndVerify(random =>
                (int)(
                    (random.NextLong(long.MinValue, long.MaxValue) + (-1.0 * long.MinValue))
                    / (1.0 * long.MaxValue - long.MinValue)
                    * _samples.Length
                )
            );
        }

        [Test]
        [Parallelizable]
        public void Ulong()
        {
            TestAndVerify(random => (int)random.NextUlong(0, (ulong)_samples.Length));
        }

        [Test]
        [Parallelizable]
        public void UlongRaw()
        {
            int sampleLength = GetSampleLength();
            TestAndVerify(random =>
                (int)(1.0 * random.NextUlong() / (1.0 * ulong.MaxValue) * sampleLength)
            );
        }

        [Test]
        [Parallelizable]
        public void UlongRange()
        {
            TestAndVerify(random => (int)random.NextUlong((ulong)_samples.Length));
        }

        [Test]
        [Parallelizable]
        public void UlongMaxRange()
        {
            TestAndVerify(random =>
                (int)(
                    (random.NextUlong(ulong.MinValue, ulong.MaxValue) + (-1.0 * ulong.MinValue))
                    / (1.0 * ulong.MaxValue - ulong.MinValue)
                    * _samples.Length
                )
            );
        }

        [Test]
        [Parallelizable]
        public void NextBytes()
        {
            IRandom random = NewRandom();
            HashSet<byte> seen = new();
            int total = 0;
            for (int i = 0; i < NormalIterations; ++i)
            {
                int size = random.Next(1, 40);
                total += size;
                byte[] buffer = new byte[size];
                random.NextBytes(buffer);
                foreach (byte value in buffer)
                {
                    seen.Add(value);
                }
            }

            double seenTargetMin = Math.Min(total, byte.MaxValue) * 0.9;
            double seenTargetMax = Math.Min(total, byte.MaxValue) * 1.1;
            Assert.LessOrEqual(seenTargetMin, seen.Count);
            Assert.GreaterOrEqual(seenTargetMax, seen.Count);
        }

        [Test]
        [Parallelizable]
        public void NextGaussian()
        {
            IRandom random = NewRandom();
            List<double> values = new();
            for (int i = 0; i < NormalIterations; ++i)
            {
                double value = random.NextGaussian();
                values.Add(value);
            }

            Assert.IsTrue(CheckApproximateNormality(values));
        }

        [Test]
        [Parallelizable]
        public void NextGuid()
        {
            IRandom random = NewRandom();

            HashSet<Guid> seen = new();
            for (int i = 0; i < NormalIterations; ++i)
            {
                Guid value = random.NextGuid();
                seen.Add(value);
            }

            Assert.LessOrEqual(NormalIterations * 0.9, seen.Count);
        }

        [Test]
        [Parallelizable]
        public void NextWGuid()
        {
            IRandom random = NewRandom();

            HashSet<WGuid> seen = new();
            for (int i = 0; i < NormalIterations; ++i)
            {
                WGuid value = random.NextWGuid();
                seen.Add(value);
            }

            Assert.LessOrEqual(NormalIterations * 0.9, seen.Count);
        }

        [Test]
        [Parallelizable]
        public void Copy()
        {
            IRandom random1 = NewRandom();
            IRandom random2 = random1.Copy();
            Assert.AreEqual(random1.InternalState, random2.InternalState);
            // UnityRandom has shared state, the below test is not possible for it. We did all we could.
            if (NewRandom() is not UnityRandom)
            {
                for (int i = 0; i < NumGeneratorChecks; ++i)
                {
                    Assert.AreEqual(random1.Next(), random2.Next());
                    Assert.AreEqual(random1.InternalState, random2.InternalState);
                }
            }

            Assert.AreEqual(random1.InternalState, random2.InternalState);
            IRandom random3 = random1.Copy();
            Assert.AreEqual(random1.InternalState, random3.InternalState);
            if (NewRandom() is not UnityRandom)
            {
                for (int i = 0; i < NumGeneratorChecks; ++i)
                {
                    Assert.AreEqual(random1.Next(), random3.Next());
                    Assert.AreEqual(random1.InternalState, random3.InternalState);
                }
            }
        }

        [Test]
        [Parallelizable]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void JsonSerialization()
        {
            IRandom random = NewRandom();
            string json = random.ToJson();
            IRandom deserialized = Serializer.JsonDeserialize<IRandom>(json, random.GetType());
            Assert.AreEqual(random.InternalState, deserialized.InternalState);

            if (random is not UnityRandom)
            {
                for (int i = 0; i < NumGeneratorChecks; ++i)
                {
                    Assert.AreEqual(random.Next(), deserialized.Next(), "Iteration: " + i);
                    Assert.AreEqual(
                        random.InternalState,
                        deserialized.InternalState,
                        "Iteration: " + i
                    );
                }
            }
        }

        [Test]
        [Parallelizable]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void JsonSerializationWithMix()
        {
            for (int preMix = 1; preMix < 10; ++preMix)
            {
                IRandom random = NewRandom();
                for (int j = 0; j < preMix; ++j)
                {
                    _ = random.Next();
                }

                string json = random.ToJson();
                IRandom deserialized = Serializer.JsonDeserialize<IRandom>(json, random.GetType());
                Assert.AreEqual(random.InternalState, deserialized.InternalState);

                if (random is not UnityRandom)
                {
                    for (int i = 0; i < NumGeneratorChecks; ++i)
                    {
                        Assert.AreEqual(random.Next(), deserialized.Next(), "Iteration: " + i);
                        Assert.AreEqual(
                            random.InternalState,
                            deserialized.InternalState,
                            "Iteration: " + i
                        );
                    }
                }
            }
        }

        [Test]
        [Parallelizable]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtobufSerialization()
        {
            IRandom random = NewRandom();
            byte[] serialized = Serializer.ProtoSerialize(random);
            IRandom deserialized = Serializer.ProtoDeserialize<IRandom>(serialized);
            Assert.AreEqual(random.InternalState, deserialized.InternalState);

            if (random is not UnityRandom)
            {
                for (int i = 0; i < NumGeneratorChecks; ++i)
                {
                    Assert.AreEqual(random.Next(), deserialized.Next());
                    Assert.AreEqual(random.InternalState, deserialized.InternalState);
                }
            }
        }

        [Test]
        [Parallelizable]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtobufSerializationWithMix()
        {
            for (int preMix = 1; preMix < 10; ++preMix)
            {
                IRandom random = NewRandom();
                for (int j = 0; j < preMix; ++j)
                {
                    _ = random.Next();
                }

                byte[] serialized = Serializer.ProtoSerialize(random);
                IRandom deserialized = Serializer.ProtoDeserialize<IRandom>(serialized);
                Assert.AreEqual(random.InternalState, deserialized.InternalState);

                if (random is not UnityRandom)
                {
                    for (int i = 0; i < NumGeneratorChecks; ++i)
                    {
                        Assert.AreEqual(random.Next(), deserialized.Next(), "Iteration: " + i);
                        Assert.AreEqual(
                            random.InternalState,
                            deserialized.InternalState,
                            "Iteration: " + i
                        );
                    }
                }
            }
        }

        [Test]
        [Parallelizable]
        public void NextEnumerable()
        {
            IRandom random = NewRandom();
            for (int i = 0; i < NormalIterations; ++i)
            {
                HashSet<TestValues> selected = Enum.GetValues(typeof(TestValues))
                    .OfType<TestValues>()
                    .Shuffled(random)
                    .Skip(3)
                    .ToHashSet();

                TestValues value = random.NextOf(selected.Shuffled(random));
                Assert.IsTrue(selected.Contains(value));
            }
        }

        [Test]
        [Parallelizable]
        public void NextEnumerableExcept()
        {
            IRandom random = NewRandom();
            for (int i = 0; i < NormalIterations; ++i)
            {
                TestValues exception = random.NextEnum<TestValues>();
                HashSet<TestValues> selected = Enum.GetValues(typeof(TestValues))
                    .OfType<TestValues>()
                    .Shuffled(random)
                    .Skip(3)
                    .ToHashSet();

                TestValues value = random.NextOfExcept(selected.Shuffled(random), exception);
                Assert.IsTrue(selected.Contains(value));
                Assert.AreNotEqual(value, exception);
            }
        }

        [Test]
        [Parallelizable]
        public void NextArray()
        {
            IRandom random = NewRandom();
            for (int i = 0; i < NormalIterations; ++i)
            {
                TestValues[] values = Enum.GetValues(typeof(TestValues))
                    .OfType<TestValues>()
                    .Shuffled(random)
                    .Skip(3)
                    .ToArray();

                TestValues value = random.NextOf(values);
                Assert.IsTrue(values.Contains(value));
            }
        }

        [Test]
        [Parallelizable]
        public void NextList()
        {
            IRandom random = NewRandom();
            for (int i = 0; i < NormalIterations; ++i)
            {
                List<TestValues> values = Enum.GetValues(typeof(TestValues))
                    .OfType<TestValues>()
                    .Shuffled(random)
                    .Skip(3)
                    .ToList();

                TestValues value = random.NextOf(values);
                Assert.IsTrue(values.Contains(value));
            }
        }

        [Test]
        [Parallelizable]
        public void NextHashSet()
        {
            IRandom random = NewRandom();
            for (int i = 0; i < NormalIterations; ++i)
            {
                HashSet<TestValues> values = Enum.GetValues(typeof(TestValues))
                    .OfType<TestValues>()
                    .Shuffled(random)
                    .Skip(3)
                    .ToHashSet();

                TestValues value = random.NextOf(values);
                Assert.IsTrue(values.Contains(value));
            }
        }

        [Test]
        [Parallelizable]
        public void NextLinkedList()
        {
            IRandom random = NewRandom();
            for (int i = 0; i < NormalIterations; ++i)
            {
                LinkedList<TestValues> values = Enum.GetValues(typeof(TestValues))
                    .OfType<TestValues>()
                    .Shuffled(random)
                    .Skip(3)
                    .ToLinkedList();

                TestValues value = random.NextOf(values);
                Assert.IsTrue(values.Contains(value));
            }
        }

        [Test]
        [Parallelizable]
        public void NextEnum()
        {
            IRandom random = NewRandom();
            HashSet<TestValues> seenEnums = new();
            for (int i = 0; i < NormalIterations; ++i)
            {
                TestValues value = random.NextEnum<TestValues>();
                _ = seenEnums.Add(value);
            }

            Assert.AreEqual(Enum.GetValues(typeof(TestValues)).Length, seenEnums.Count);
        }

        [Test]
        [Parallelizable]
        public void NextEnumExcept()
        {
            IRandom random = NewRandom();
            HashSet<TestValues> seenEnums = new();
            for (int i = 0; i < NormalIterations; ++i)
            {
                TestValues value = random.NextEnumExcept(TestValues.Value8, TestValues.Value9);
                _ = seenEnums.Add(value);
            }

            Assert.AreEqual(Enum.GetValues(typeof(TestValues)).Length - 2, seenEnums.Count);
            Assert.IsFalse(seenEnums.Contains(TestValues.Value8));
            Assert.IsFalse(seenEnums.Contains(TestValues.Value9));
        }

        [Test]
        [Parallelizable]
        public void NextNoiseMap()
        {
            IRandom random = NewRandom();
            for (int i = 0; i < NormalIterations; ++i)
            {
                int width = random.Next(1, 75);
                int height = random.Next(1, 75);
                float[,] noise = new float[width, height];
                random.NextNoiseMap(noise);
                foreach (float value in noise)
                {
                    Assert.LessOrEqual(0f, value);
                    Assert.GreaterOrEqual(1.1f, value);
                }
            }
        }

        protected virtual double GetDeviationFor(string caller)
        {
            return 0.0625;
        }

        private static bool CheckApproximateNormality(IEnumerable<double> data)
        {
            IReadOnlyList<double> input = data as IReadOnlyList<double> ?? data.ToArray();
            int n = input.Count;
            if (n < 3)
            {
                return true;
            }

            double mean = input.Average();
            double variance = input.Sum(x => Math.Pow(x - mean, 2)) / n;
            double stdDev = Math.Sqrt(variance);

            double skewness = input.Sum(x => Math.Pow((x - mean) / stdDev, 3)) / n;
            double kurtosis = input.Sum(x => Math.Pow((x - mean) / stdDev, 4)) / n - 3; // Excess kurtosis

            // Thresholds can be defined based on empirical rules or domain-specific values
            const double skewnessThreshold = 0.5;
            const double kurtosisThreshold = 1.0;

            return Math.Abs(skewness) < skewnessThreshold && Math.Abs(kurtosis) < kurtosisThreshold;
        }

        protected int GetSampleLength(int? sampleLength = null)
        {
            return Math.Min(_samples.Length, sampleLength ?? _samples.Length);
        }

        private static int ResolveSampleCount()
        {
            string raw = Environment.GetEnvironmentVariable("UH_RANDOM_SAMPLE_COUNT");
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out int parsed) && parsed > 0)
            {
                return parsed;
            }

            return DefaultFastSampleCount;
        }

        private void TestAndVerify(
            Func<IRandom, int> sample,
            int? maxLength = null,
            [CallerMemberName] string caller = ""
        )
        {
            IRandom random = NewRandom();
            int sampleLength = _samples.Length;
            for (int i = 0; i < SampleCount; ++i)
            {
                int index = sample(random);
                if (index < 0 || sampleLength < index)
                {
                    Assert.Fail("Index {0} out of range", index);
                }
                else
                {
                    _samples[Math.Min(sampleLength - 1, index)]++;
                }
            }

            sampleLength = GetSampleLength(maxLength);
            double average = SampleCount * 1.0 / sampleLength;
            // Allow the LOOSER of the configured relative tolerance and a
            // sqrt(average)-based statistical floor. At the historical SampleCount
            // the relative term dominates (identical behaviour); at the reduced
            // fast-suite count the sqrt floor prevents spurious failures from
            // natural sampling variance across `sampleLength` bins.
            double deviationAllowed = Math.Max(
                average * GetDeviationFor(caller),
                DeviationSigmaFloor * Math.Sqrt(average)
            );
            List<int> zeroCountIndexes = new();
            List<int> outsideRange = new();
            for (int i = 0; i < sampleLength; i++)
            {
                int count = _samples[i];
                if (count == 0)
                {
                    zeroCountIndexes.Add(i);
                }

                if (deviationAllowed < Math.Abs(count - average))
                {
                    outsideRange.Add(i);
                }
            }

            Assert.AreEqual(
                0,
                zeroCountIndexes.Count,
                "No samples at {0} indices: [{1}]",
                zeroCountIndexes.Count,
                string.Join(",", zeroCountIndexes)
            );
            Assert.AreEqual(
                0,
                outsideRange.Count,
                "{0} indexes outside of dev {1:0.00}. Expected: {2:0.00}. Found: [{3}]",
                outsideRange.Count,
                deviationAllowed,
                average,
                string.Join(",", outsideRange.Select(index => _samples[index]))
            );
        }
    }
}
