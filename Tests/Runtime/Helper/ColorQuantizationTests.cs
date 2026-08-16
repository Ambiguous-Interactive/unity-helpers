// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ColorQuantizationTests : CommonTestBase
    {
        private const int FuzzIterations = 10_000;

        private static readonly float[] Cutoffs =
        {
            0f,
            0.004f,
            0.01f,
            0.1f,
            0.25f,
            0.5f,
            0.75f,
            0.9f,
            0.999f,
            1f,
        };

        private static readonly object[] EncodeCases =
        {
            new object[] { 0f, (byte)0 },
            new object[] { 1f, (byte)255 },
            new object[] { 0.1f, (byte)26 },
            new object[] { 0.2f, (byte)51 },
            new object[] { 0.3f, (byte)76 },
            new object[] { 0.4f, (byte)102 },
            new object[] { 0.999f, (byte)255 },
        };

        [Test]
        [TestCaseSource(nameof(EncodeCases))]
        public void ToByteEncodesToNearestChannel(float normalized, byte expected)
        {
            Assert.AreEqual(expected, ColorQuantization.ToByte(normalized));
        }

        [Test]
        public void ToByteMatchesUnityColor32Conversion()
        {
            IRandom random = PRNG.Instance;
            for (int i = 0; i < FuzzIterations; ++i)
            {
                float value = random.NextFloat();
                Color32 unity = new Color(value, value, value, value);
                Assert.AreEqual(
                    unity.r,
                    ColorQuantization.ToByte(value),
                    $"{value} disagreed with Unity's Color -> Color32 conversion."
                );
            }
        }

        [Test]
        public void ToNormalizedSpansTheFullUnitRange()
        {
            Assert.AreEqual(0f, ColorQuantization.ToNormalized(0));
            Assert.AreEqual(1f, ColorQuantization.ToNormalized(255));
        }

        [Test]
        public void EveryChannelSurvivesADecodeEncodeRoundTrip()
        {
            for (int channel = 0; channel <= byte.MaxValue; ++channel)
            {
                float normalized = ColorQuantization.ToNormalized((byte)channel);
                Assert.AreEqual(
                    (byte)channel,
                    ColorQuantization.ToByte(normalized),
                    $"Channel {channel} did not round-trip."
                );
            }
        }

        /// <remarks>
        /// The defining property: <c>channel &lt;= ToThresholdByte(cutoff)</c> must be the same question
        /// as <c>ToNormalized(channel) &lt;= cutoff</c>, for every channel and every cutoff. Rounding or
        /// ceiling the cutoff instead misclassifies the channel on the boundary, which is what let two
        /// callers of the same cutoff disagree about which pixels were transparent.
        /// </remarks>
        [Test]
        public void ToThresholdByteReproducesTheFloatComparisonExactly()
        {
            foreach (float cutoff in Cutoffs)
            {
                byte threshold = ColorQuantization.ToThresholdByte(cutoff);
                for (int channel = 0; channel <= byte.MaxValue; ++channel)
                {
                    bool floatAnswer = ColorQuantization.ToNormalized((byte)channel) <= cutoff;
                    Assert.AreEqual(
                        floatAnswer,
                        channel <= threshold,
                        $"Cutoff {cutoff} disagreed at channel {channel}."
                    );
                }
            }
        }

        [Test]
        public void ToThresholdByteReproducesTheFloatComparisonUnderFuzzing()
        {
            IRandom random = PRNG.Instance;
            for (int i = 0; i < FuzzIterations; ++i)
            {
                float cutoff = random.NextFloat();
                byte threshold = ColorQuantization.ToThresholdByte(cutoff);
                byte channel = random.NextByte();
                bool floatAnswer = ColorQuantization.ToNormalized(channel) <= cutoff;
                Assert.AreEqual(
                    floatAnswer,
                    channel <= threshold,
                    $"Cutoff {cutoff} disagreed at channel {channel}."
                );
            }
        }

        /// <remarks>
        /// NaN is the case worth pinning. <c>Mathf.Clamp01(NaN)</c> returns NaN - every comparison
        /// against NaN is false, so it falls through both of that method's branches - which would leave
        /// a float-to-int conversion of NaN, undefined in C#. It yields 0 on x64 only because
        /// <c>(byte)int.MinValue</c> is 0. Both methods test their bounds so that 0 is the answer by
        /// construction rather than by architecture.
        /// </remarks>
        [Test]
        public void HostileInputsSaturateRatherThanRelyingOnAnUndefinedCast()
        {
            Assert.IsTrue(
                float.IsNaN(Mathf.Clamp01(float.NaN)),
                "Clamp01 no longer passes NaN through."
            );

            Assert.AreEqual(0, ColorQuantization.ToByte(float.NaN));
            Assert.AreEqual(0, ColorQuantization.ToByte(float.NegativeInfinity));
            Assert.AreEqual(0, ColorQuantization.ToByte(-12f));
            Assert.AreEqual(255, ColorQuantization.ToByte(float.PositiveInfinity));
            Assert.AreEqual(255, ColorQuantization.ToByte(12f));

            Assert.AreEqual(0, ColorQuantization.ToThresholdByte(float.NaN));
            Assert.AreEqual(0, ColorQuantization.ToThresholdByte(float.NegativeInfinity));
            Assert.AreEqual(0, ColorQuantization.ToThresholdByte(-12f));
            Assert.AreEqual(255, ColorQuantization.ToThresholdByte(float.PositiveInfinity));
            Assert.AreEqual(255, ColorQuantization.ToThresholdByte(12f));
        }

        /// <remarks>
        /// Truncating instead of rounding doubles the mean absolute quantization error and makes 255
        /// unreachable for every input short of exactly 1. Both are asserted so that a regression to
        /// <c>(byte)(value * 255f)</c> reddens rather than merely shifting results by one.
        /// </remarks>
        [Test]
        public void ToByteHalvesTheErrorOfTruncation()
        {
            IRandom random = PRNG.Instance;
            double roundedError = 0;
            double truncatedError = 0;
            bool sawMaxChannel = false;
            for (int i = 0; i < FuzzIterations; ++i)
            {
                float value = random.NextFloat();
                byte rounded = ColorQuantization.ToByte(value);
                byte truncated = (byte)(Mathf.Clamp01(value) * 255f);
                sawMaxChannel |= rounded == byte.MaxValue;
                roundedError += Mathf.Abs(ColorQuantization.ToNormalized(rounded) - value);
                truncatedError += Mathf.Abs(ColorQuantization.ToNormalized(truncated) - value);
            }

            Assert.Less(
                roundedError * 1.5,
                truncatedError,
                $"Rounded error {roundedError} was not materially below truncated error {truncatedError}."
            );
            Assert.IsTrue(sawMaxChannel, "Rounding never produced a fully saturated channel.");
        }

        /// <remarks>
        /// The property that makes this the one usable definition of "the same colour": it is an
        /// equivalence relation, so a hash can agree with it. An absolute tolerance is not transitive
        /// and no hash can, which is what broke four colour caches.
        /// </remarks>
        [Test]
        public void AreSameColorIsExactlyEqualityOfTheEncodedChannels()
        {
            IRandom random = PRNG.Instance;
            for (int i = 0; i < FuzzIterations; ++i)
            {
                Color left = new(
                    random.NextFloat(),
                    random.NextFloat(),
                    random.NextFloat(),
                    random.NextFloat()
                );
                Color right = random.NextBool()
                    ? left
                    : new Color(
                        left.r + (random.NextFloat() - 0.5f) * ColorQuantization.ChannelStep * 2f,
                        left.g,
                        left.b,
                        left.a
                    );

                bool same = ColorQuantization.AreSameColor(left, right);
                bool encodesAlike =
                    ColorQuantization.ToByte(left.r) == ColorQuantization.ToByte(right.r)
                    && ColorQuantization.ToByte(left.g) == ColorQuantization.ToByte(right.g)
                    && ColorQuantization.ToByte(left.b) == ColorQuantization.ToByte(right.b)
                    && ColorQuantization.ToByte(left.a) == ColorQuantization.ToByte(right.a);

                Assert.AreEqual(encodesAlike, same, $"{left} against {right}");
                Assert.AreEqual(same, ColorQuantization.AreSameColor(right, left), "not symmetric");
            }
        }

        /// <remarks>
        /// A change notifier comparing with <c>Mathf.Abs(a - b) &lt; tolerance</c> answered
        /// <see langword="false"/> for a NaN channel, because every comparison against NaN is false, so
        /// it reported "changed" on every check and never settled.
        /// </remarks>
        [Test]
        public void AreSameColorIsReflexiveForChannelsThatAreNotNumbers()
        {
            Color notANumber = new(float.NaN, float.NaN, float.NaN, float.NaN);
            Color infinite = new(
                float.PositiveInfinity,
                float.NegativeInfinity,
                float.PositiveInfinity,
                float.NegativeInfinity
            );

            Assert.IsTrue(ColorQuantization.AreSameColor(notANumber, notANumber));
            Assert.IsTrue(ColorQuantization.AreSameColor(infinite, infinite));
            Assert.IsTrue(
                ColorQuantization.AreSameColor(notANumber, Color.clear),
                "NaN encodes to 0, so it is the same colour as one whose channels are 0."
            );
            Assert.IsFalse(ColorQuantization.AreSameColor(notANumber, Color.white));
        }

        [Test]
        public void AreSameColorSeparatesEveryAdjacentChannelPair()
        {
            for (int channel = 0; channel < byte.MaxValue; ++channel)
            {
                Color lower = new(ColorQuantization.ToNormalized((byte)channel), 0f, 0f, 1f);
                Color upper = new(ColorQuantization.ToNormalized((byte)(channel + 1)), 0f, 0f, 1f);

                Assert.IsTrue(ColorQuantization.AreSameColor(lower, lower), $"channel {channel}");
                Assert.IsFalse(
                    ColorQuantization.AreSameColor(lower, upper),
                    $"channels {channel} and {channel + 1} compared equal."
                );
            }
        }
    }
}
