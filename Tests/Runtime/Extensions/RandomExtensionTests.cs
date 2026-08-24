// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Runtime.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RandomExtensionTests : CommonTestBase
    {
        [Test]
        public void NextVector2WithAmplitude()
        {
            for (int i = 0; i < 100; ++i)
            {
                float amplitude = PRNG.Instance.NextFloat(0.1f, 100f);
                Vector2 result = PRNG.Instance.NextVector2(amplitude);
                Assert.LessOrEqual(Mathf.Abs(result.x), amplitude);
                Assert.LessOrEqual(Mathf.Abs(result.y), amplitude);
                Assert.GreaterOrEqual(result.x, -amplitude);
                Assert.GreaterOrEqual(result.y, -amplitude);
            }
        }

        [Test]
        public void NextVector2WithMinMaxAmplitude()
        {
            for (int i = 0; i < 100; ++i)
            {
                float min = PRNG.Instance.NextFloat(-100f, 0f);
                float max = PRNG.Instance.NextFloat(0f, 100f);
                Vector2 result = PRNG.Instance.NextVector2(min, max);
                Assert.GreaterOrEqual(result.x, min);
                Assert.Less(result.x, max);
                Assert.GreaterOrEqual(result.y, min);
                Assert.Less(result.y, max);
            }
        }

        [Test]
        public void NextVector2InRange()
        {
            HashSet<float> seenAngles = new();
            for (int i = 0; i < 1_000; ++i)
            {
                Vector2 vector = PRNG.Instance.NextVector2(-100, 100);
                float range = PRNG.Instance.NextFloat(100f);
                Vector2 inRange = PRNG.Instance.NextVector2InRange(range, vector);
                Assert.LessOrEqual(Vector2.Distance(vector, inRange), range);
                seenAngles.Add(Vector2.SignedAngle(vector, inRange));
            }

            Assert.LessOrEqual(3, seenAngles.Count);
        }

        [Test]
        public void NextVector3WithAmplitude()
        {
            for (int i = 0; i < 100; ++i)
            {
                float amplitude = PRNG.Instance.NextFloat(0.1f, 100f);
                Vector3 result = PRNG.Instance.NextVector3(amplitude);
                Assert.LessOrEqual(Mathf.Abs(result.x), amplitude);
                Assert.LessOrEqual(Mathf.Abs(result.y), amplitude);
                Assert.LessOrEqual(Mathf.Abs(result.z), amplitude);
                Assert.GreaterOrEqual(result.x, -amplitude);
                Assert.GreaterOrEqual(result.y, -amplitude);
                Assert.GreaterOrEqual(result.z, -amplitude);
            }
        }

        [Test]
        public void NextVector3WithMinMaxAmplitude()
        {
            for (int i = 0; i < 100; ++i)
            {
                float min = PRNG.Instance.NextFloat(-100f, 0f);
                float max = PRNG.Instance.NextFloat(0f, 100f);
                Vector3 result = PRNG.Instance.NextVector3(min, max);
                Assert.GreaterOrEqual(result.x, min);
                Assert.Less(result.x, max);
                Assert.GreaterOrEqual(result.y, min);
                Assert.Less(result.y, max);
                Assert.GreaterOrEqual(result.z, min);
                Assert.Less(result.z, max);
            }
        }

        [Test]
        public void NextVector3InRange()
        {
            for (int i = 0; i < 100; ++i)
            {
                Vector3 origin = PRNG.Instance.NextVector3(-100, 100);
                float range = PRNG.Instance.NextFloat(1f, 100f);
                Vector3 result = PRNG.Instance.NextVector3InRange(range, origin);
                Assert.LessOrEqual(Vector3.Distance(origin, result), range);
            }
        }

        [Test]
        public void NextVector3OnSphere()
        {
            for (int i = 0; i < 100; ++i)
            {
                float radius = PRNG.Instance.NextFloat(1f, 100f);
                Vector3 center = PRNG.Instance.NextVector3(-50, 50);
                Vector3 result = PRNG.Instance.NextVector3OnSphere(radius, center);
                float distance = Vector3.Distance(center, result);
                Assert.AreEqual(radius, distance, 0.001f);
            }
        }

        [Test]
        public void NextVector3InSphere()
        {
            for (int i = 0; i < 100; ++i)
            {
                float radius = PRNG.Instance.NextFloat(1f, 100f);
                Vector3 center = PRNG.Instance.NextVector3(-50, 50);
                Vector3 result = PRNG.Instance.NextVector3InSphere(radius, center);
                Assert.LessOrEqual(Vector3.Distance(center, result), radius);
            }
        }

        [Test]
        public void NextQuaternion()
        {
            HashSet<Quaternion> seen = new();
            for (int i = 0; i < 100; ++i)
            {
                Quaternion result = PRNG.Instance.NextQuaternion();
                float magnitude = Mathf.Sqrt(
                    result.x * result.x
                        + result.y * result.y
                        + result.z * result.z
                        + result.w * result.w
                );
                Assert.AreEqual(1f, magnitude, 0.001f);
                seen.Add(result);
            }

            Assert.GreaterOrEqual(seen.Count, 90);
        }

        [Test]
        public void NextQuaternionAxisAngle()
        {
            for (int i = 0; i < 100; ++i)
            {
                Vector3 axis = PRNG.Instance.NextDirection3D();
                float minAngle = PRNG.Instance.NextFloat(0f, 180f);
                float maxAngle = PRNG.Instance.NextFloat(minAngle, 360f);
                Quaternion result = PRNG.Instance.NextQuaternionAxisAngle(axis, minAngle, maxAngle);
                float magnitude = Mathf.Sqrt(
                    result.x * result.x
                        + result.y * result.y
                        + result.z * result.z
                        + result.w * result.w
                );
                Assert.AreEqual(1f, magnitude, 0.001f);
            }
        }

        [Test]
        public void NextQuaternionLookRotation()
        {
            for (int i = 0; i < 100; ++i)
            {
                Quaternion result = PRNG.Instance.NextQuaternionLookRotation();
                float magnitude = Mathf.Sqrt(
                    result.x * result.x
                        + result.y * result.y
                        + result.z * result.z
                        + result.w * result.w
                );
                Assert.AreEqual(1f, magnitude, 0.001f);
            }
        }

        [Test]
        public void NextColorWithoutAlpha()
        {
            for (int i = 0; i < 100; ++i)
            {
                Color result = PRNG.Instance.NextColor(false);
                Assert.GreaterOrEqual(result.r, 0f);
                Assert.LessOrEqual(result.r, 1f);
                Assert.GreaterOrEqual(result.g, 0f);
                Assert.LessOrEqual(result.g, 1f);
                Assert.GreaterOrEqual(result.b, 0f);
                Assert.LessOrEqual(result.b, 1f);
                Assert.AreEqual(1f, result.a);
            }
        }

        [Test]
        public void NextColorWithAlpha()
        {
            for (int i = 0; i < 100; ++i)
            {
                Color result = PRNG.Instance.NextColor(true);
                Assert.GreaterOrEqual(result.r, 0f);
                Assert.LessOrEqual(result.r, 1f);
                Assert.GreaterOrEqual(result.g, 0f);
                Assert.LessOrEqual(result.g, 1f);
                Assert.GreaterOrEqual(result.b, 0f);
                Assert.LessOrEqual(result.b, 1f);
                Assert.GreaterOrEqual(result.a, 0f);
                Assert.LessOrEqual(result.a, 1f);
            }
        }

        [Test]
        public void NextColorInRange()
        {
            Color baseColor = new(0.5f, 0.5f, 0.5f, 1f);
            for (int i = 0; i < 100; ++i)
            {
                float hueVar = PRNG.Instance.NextFloat(0f, 0.5f);
                float satVar = PRNG.Instance.NextFloat(0f, 0.5f);
                float valVar = PRNG.Instance.NextFloat(0f, 0.5f);
                Color result = PRNG.Instance.NextColorInRange(baseColor, hueVar, satVar, valVar);
                Assert.GreaterOrEqual(result.r, 0f);
                Assert.LessOrEqual(result.r, 1f);
                Assert.GreaterOrEqual(result.g, 0f);
                Assert.LessOrEqual(result.g, 1f);
                Assert.GreaterOrEqual(result.b, 0f);
                Assert.LessOrEqual(result.b, 1f);
            }
        }

        [Test]
        public void NextColorInRangeKeepsBaseAlpha()
        {
            for (int i = 0; i < 250; ++i)
            {
                float alpha = PRNG.Instance.NextFloat();
                Color baseColor = new(0.2f, 0.5f, 0.9f, alpha);
                Color result = PRNG.Instance.NextColorInRange(baseColor, 0.3f, 0.3f, 0.3f);
                Assert.AreEqual(alpha, result.a);
            }
        }

        [Test]
        public void NextColorInRangeAcceptsZeroVariance()
        {
            Color baseColor = new(0.2f, 0.5f, 0.9f, 0.4f);

            Color result = PRNG.Instance.NextColorInRange(baseColor, 0f, 0f, 0f);

            Assert.AreEqual(baseColor.r, result.r, 1e-4f);
            Assert.AreEqual(baseColor.g, result.g, 1e-4f);
            Assert.AreEqual(baseColor.b, result.b, 1e-4f);
            Assert.AreEqual(baseColor.a, result.a);
        }

        [Test]
        public void NextColorInRangeReadsNegativeVarianceAsMagnitude()
        {
            Color baseColor = new(0.2f, 0.5f, 0.9f, 1f);
            HashSet<Color> seen = new();
            for (int i = 0; i < 250; ++i)
            {
                Color result = PRNG.Instance.NextColorInRange(baseColor, -0.3f, -0.3f, -0.3f);
                Assert.GreaterOrEqual(result.r, 0f);
                Assert.LessOrEqual(result.r, 1f);
                Assert.GreaterOrEqual(result.g, 0f);
                Assert.LessOrEqual(result.g, 1f);
                Assert.GreaterOrEqual(result.b, 0f);
                Assert.LessOrEqual(result.b, 1f);
                seen.Add(result);
            }

            Assert.Greater(seen.Count, 1);
        }

        [Test]
        public void NextColorInRangeTreatsNonFiniteVarianceAsZero()
        {
            Color baseColor = new(0.2f, 0.5f, 0.9f, 0.4f);

            foreach (
                float variance in new[]
                {
                    float.NaN,
                    float.PositiveInfinity,
                    float.NegativeInfinity,
                }
            )
            {
                Color result = PRNG.Instance.NextColorInRange(
                    baseColor,
                    variance,
                    variance,
                    variance
                );
                Assert.IsFalse(float.IsNaN(result.r));
                Assert.IsFalse(float.IsNaN(result.g));
                Assert.IsFalse(float.IsNaN(result.b));
                Assert.AreEqual(baseColor.r, result.r, 1e-4f);
                Assert.AreEqual(baseColor.g, result.g, 1e-4f);
                Assert.AreEqual(baseColor.b, result.b, 1e-4f);
                Assert.AreEqual(baseColor.a, result.a);
            }
        }

        [Test]
        public void NextColorInRangeWrapsHueAcrossTheSeam()
        {
            const int iterations = 4000;
            int wrapped = 0;
            for (int i = 0; i < iterations; ++i)
            {
                Color result = PRNG.Instance.NextColorInRange(Color.red, 0.1f, 0f, 0f);
                Color.RGBToHSV(result, out float hue, out float _, out float _);
                if (0.5f < hue)
                {
                    ++wrapped;
                }
            }

            // Half of a symmetric perturbation around hue 0 lands on the far side of the seam.
            Assert.Greater(wrapped, iterations / 4);
            Assert.Less(wrapped, iterations * 3 / 4);
        }

        [Test]
        public void NextColorChannelsStayWithinTheHalfOpenUnitRange()
        {
            for (int i = 0; i < 2000; ++i)
            {
                Color result = PRNG.Instance.NextColor(true);
                Assert.GreaterOrEqual(result.r, 0f);
                Assert.Less(result.r, 1f);
                Assert.GreaterOrEqual(result.g, 0f);
                Assert.Less(result.g, 1f);
                Assert.GreaterOrEqual(result.b, 0f);
                Assert.Less(result.b, 1f);
                Assert.GreaterOrEqual(result.a, 0f);
                Assert.Less(result.a, 1f);
            }
        }

        [Test]
        public void NextColor32WithoutAlpha()
        {
            for (int i = 0; i < 100; ++i)
            {
                Color32 result = PRNG.Instance.NextColor32(false);
                Assert.AreEqual(255, result.a);
            }
        }

        [Test]
        public void NextColor32WithAlpha()
        {
            HashSet<byte> seenAlphas = new();
            for (int i = 0; i < 500; ++i)
            {
                Color32 result = PRNG.Instance.NextColor32(true);
                seenAlphas.Add(result.a);
            }

            Assert.GreaterOrEqual(seenAlphas.Count, 100);
        }

        [Test]
        public void NextVector2IntWithAmplitude()
        {
            for (int i = 0; i < 100; ++i)
            {
                int amplitude = PRNG.Instance.Next(1, 100);
                Vector2Int result = PRNG.Instance.NextVector2Int(amplitude);
                Assert.GreaterOrEqual(result.x, -amplitude);
                Assert.Less(result.x, amplitude);
                Assert.GreaterOrEqual(result.y, -amplitude);
                Assert.Less(result.y, amplitude);
            }
        }

        [Test]
        public void NextVector2IntWithMinMaxAmplitude()
        {
            for (int i = 0; i < 100; ++i)
            {
                int min = PRNG.Instance.Next(-100, 0);
                int max = PRNG.Instance.Next(1, 100);
                Vector2Int result = PRNG.Instance.NextVector2Int(min, max);
                Assert.GreaterOrEqual(result.x, min);
                Assert.Less(result.x, max);
                Assert.GreaterOrEqual(result.y, min);
                Assert.Less(result.y, max);
            }
        }

        [Test]
        public void NextVector2IntWithMinMaxVectors()
        {
            for (int i = 0; i < 100; ++i)
            {
                Vector2Int min = new(PRNG.Instance.Next(-100, 0), PRNG.Instance.Next(-100, 0));
                Vector2Int max = new(PRNG.Instance.Next(1, 100), PRNG.Instance.Next(1, 100));
                Vector2Int result = PRNG.Instance.NextVector2Int(min, max);
                Assert.GreaterOrEqual(result.x, min.x);
                Assert.Less(result.x, max.x);
                Assert.GreaterOrEqual(result.y, min.y);
                Assert.Less(result.y, max.y);
            }
        }

        [Test]
        public void NextVector3IntWithAmplitude()
        {
            for (int i = 0; i < 100; ++i)
            {
                int amplitude = PRNG.Instance.Next(1, 100);
                Vector3Int result = PRNG.Instance.NextVector3Int(amplitude);
                Assert.GreaterOrEqual(result.x, -amplitude);
                Assert.Less(result.x, amplitude);
                Assert.GreaterOrEqual(result.y, -amplitude);
                Assert.Less(result.y, amplitude);
                Assert.GreaterOrEqual(result.z, -amplitude);
                Assert.Less(result.z, amplitude);
            }
        }

        [Test]
        public void NextVector3IntWithMinMaxAmplitude()
        {
            for (int i = 0; i < 100; ++i)
            {
                int min = PRNG.Instance.Next(-100, 0);
                int max = PRNG.Instance.Next(1, 100);
                Vector3Int result = PRNG.Instance.NextVector3Int(min, max);
                Assert.GreaterOrEqual(result.x, min);
                Assert.Less(result.x, max);
                Assert.GreaterOrEqual(result.y, min);
                Assert.Less(result.y, max);
                Assert.GreaterOrEqual(result.z, min);
                Assert.Less(result.z, max);
            }
        }

        [Test]
        public void NextVector3IntWithMinMaxVectors()
        {
            for (int i = 0; i < 100; ++i)
            {
                Vector3Int min = new(
                    PRNG.Instance.Next(-100, 0),
                    PRNG.Instance.Next(-100, 0),
                    PRNG.Instance.Next(-100, 0)
                );
                Vector3Int max = new(
                    PRNG.Instance.Next(1, 100),
                    PRNG.Instance.Next(1, 100),
                    PRNG.Instance.Next(1, 100)
                );
                Vector3Int result = PRNG.Instance.NextVector3Int(min, max);
                Assert.GreaterOrEqual(result.x, min.x);
                Assert.Less(result.x, max.x);
                Assert.GreaterOrEqual(result.y, min.y);
                Assert.Less(result.y, max.y);
                Assert.GreaterOrEqual(result.z, min.z);
                Assert.Less(result.z, max.z);
            }
        }

        [Test]
        public void NextDirection2D()
        {
            for (int i = 0; i < 100; ++i)
            {
                Vector2 result = PRNG.Instance.NextDirection2D();
                float magnitude = result.magnitude;
                Assert.AreEqual(1f, magnitude, 0.001f);
            }
        }

        [Test]
        public void NextDirection3D()
        {
            for (int i = 0; i < 100; ++i)
            {
                Vector3 result = PRNG.Instance.NextDirection3D();
                float magnitude = result.magnitude;
                Assert.AreEqual(1f, magnitude, 0.001f);
            }
        }

        [Test]
        public void NextAngle()
        {
            for (int i = 0; i < 100; ++i)
            {
                float result = PRNG.Instance.NextAngle();
                Assert.GreaterOrEqual(result, 0f);
                Assert.Less(result, 360f);
            }
        }

        [Test]
        public void NextAngleWithRange()
        {
            for (int i = 0; i < 100; ++i)
            {
                float min = PRNG.Instance.NextFloat(-180f, 180f);
                float max = PRNG.Instance.NextFloat(min, 360f);
                float result = PRNG.Instance.NextAngle(min, max);
                Assert.GreaterOrEqual(result, min);
                Assert.Less(result, max);
            }
        }

        [Test]
        public void NextVector2InRect()
        {
            for (int i = 0; i < 100; ++i)
            {
                Rect rect = new(
                    PRNG.Instance.NextFloat(-100f, 0f),
                    PRNG.Instance.NextFloat(-100f, 0f),
                    PRNG.Instance.NextFloat(1f, 100f),
                    PRNG.Instance.NextFloat(1f, 100f)
                );
                Vector2 result = PRNG.Instance.NextVector2InRect(rect);
                Assert.GreaterOrEqual(result.x, rect.xMin);
                Assert.Less(result.x, rect.xMax);
                Assert.GreaterOrEqual(result.y, rect.yMin);
                Assert.Less(result.y, rect.yMax);
            }
        }

        [Test]
        public void NextVector3InBounds()
        {
            for (int i = 0; i < 100; ++i)
            {
                Vector3 center = PRNG.Instance.NextVector3(-50, 50);
                Vector3 size = new(
                    PRNG.Instance.NextFloat(1f, 100f),
                    PRNG.Instance.NextFloat(1f, 100f),
                    PRNG.Instance.NextFloat(1f, 100f)
                );
                Bounds bounds = new(center, size);
                Vector3 result = PRNG.Instance.NextVector3InBounds(bounds);
                Assert.GreaterOrEqual(result.x, bounds.min.x);
                Assert.Less(result.x, bounds.max.x);
                Assert.GreaterOrEqual(result.y, bounds.min.y);
                Assert.Less(result.y, bounds.max.y);
                Assert.GreaterOrEqual(result.z, bounds.min.z);
                Assert.Less(result.z, bounds.max.z);
            }
        }

        [Test]
        public void NextWeightedElement()
        {
            List<string> items = new() { "rare", "common" };
            List<float> weights = new() { 1f, 99f };
            HashSet<string> seen = new();
            int commonCount = 0;
            int rareCount = 0;
            for (int i = 0; i < 1000; ++i)
            {
                string result = PRNG.Instance.NextWeightedElement(items, weights);
                seen.Add(result);
                if (result == "common")
                {
                    commonCount++;
                }
                else if (result == "rare")
                {
                    rareCount++;
                }
            }

            Assert.AreEqual(2, seen.Count);
            Assert.Greater(commonCount, rareCount);
            Assert.Greater(commonCount, 900);
        }

        [Test]
        public void NextWeightedElementThrowsOnNullItems()
        {
            List<float> weights = new() { 1f };
            Assert.Throws<ArgumentNullException>(() =>
                PRNG.Instance.NextWeightedElement<string>(null, weights)
            );
        }

        [Test]
        public void NextWeightedElementThrowsOnNullWeights()
        {
            List<string> items = new() { "item" };
            Assert.Throws<ArgumentNullException>(() =>
                PRNG.Instance.NextWeightedElement(items, null)
            );
        }

        [Test]
        public void NextWeightedElementThrowsOnMismatchedCounts()
        {
            List<string> items = new() { "item" };
            List<float> weights = new() { 1f, 2f };
            Assert.Throws<ArgumentException>(() =>
                PRNG.Instance.NextWeightedElement(items, weights)
            );
        }

        [Test]
        public void NextWeighted()
        {
            List<(string, float)> items = new() { ("rare", 1f), ("common", 99f) };
            HashSet<string> seen = new();
            int commonCount = 0;
            int rareCount = 0;
            for (int i = 0; i < 1000; ++i)
            {
                string result = PRNG.Instance.NextWeighted(items);
                seen.Add(result);
                if (result == "common")
                {
                    commonCount++;
                }
                else if (result == "rare")
                {
                    rareCount++;
                }
            }

            Assert.AreEqual(2, seen.Count);
            Assert.Greater(commonCount, rareCount);
            Assert.Greater(commonCount, 900);
        }

        [Test]
        public void NextWeightedThrowsOnEmptyCollection()
        {
            List<(string, float)> empty = new();
            Assert.Throws<ArgumentException>(() => PRNG.Instance.NextWeighted(empty));
        }

        [Test]
        public void NextWeightedThrowsOnNegativeWeight()
        {
            List<(string, float)> items = new() { ("item", -1f) };
            Assert.Throws<ArgumentException>(() => PRNG.Instance.NextWeighted(items));
        }

        [Test]
        public void NextWeightedThrowsOnZeroTotalWeight()
        {
            List<(string, float)> items = new() { ("item", 0f) };
            Assert.Throws<ArgumentException>(() => PRNG.Instance.NextWeighted(items));
        }

        [Test]
        public void NextWeightedIndex()
        {
            float[] weights = { 1f, 99f };
            HashSet<int> seen = new();
            int index0Count = 0;
            int index1Count = 0;
            for (int i = 0; i < 1000; ++i)
            {
                int result = PRNG.Instance.NextWeightedIndex(weights);
                seen.Add(result);
                if (result == 0)
                {
                    index0Count++;
                }
                else if (result == 1)
                {
                    index1Count++;
                }
            }

            Assert.AreEqual(2, seen.Count);
            Assert.Greater(index1Count, index0Count);
            Assert.Greater(index1Count, 900);
        }

        [Test]
        public void NextWeightedIndexThrowsOnNull()
        {
            Assert.Throws<ArgumentException>(() => PRNG.Instance.NextWeightedIndex(null));
        }

        [Test]
        public void NextWeightedIndexThrowsOnEmpty()
        {
            Assert.Throws<ArgumentException>(() =>
                PRNG.Instance.NextWeightedIndex(Array.Empty<float>())
            );
        }

        [Test]
        public void NextWeightedIndexThrowsOnNegativeWeight()
        {
            Assert.Throws<ArgumentException>(() => PRNG.Instance.NextWeightedIndex(new[] { -1f }));
        }

        [Test]
        public void NextWeightedIndexThrowsOnZeroTotalWeight()
        {
            Assert.Throws<ArgumentException>(() => PRNG.Instance.NextWeightedIndex(new[] { 0f }));
        }

        [Test]
        public void NextBoolWithProbability()
        {
            int trueCount = 0;
            for (int i = 0; i < 1000; ++i)
            {
                if (PRNG.Instance.NextBool(0.7f))
                {
                    trueCount++;
                }
            }

            Assert.Greater(trueCount, 600);
            Assert.Less(trueCount, 800);
        }

        [Test]
        public void NextBoolWithProbabilityThrowsOnInvalidValue()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PRNG.Instance.NextBool(-0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => PRNG.Instance.NextBool(1.1f));
        }

        [Test]
        public void NextSign()
        {
            HashSet<int> seen = new();
            for (int i = 0; i < 100; ++i)
            {
                int result = PRNG.Instance.NextSign();
                Assert.IsTrue(result == 1 || result == -1);
                seen.Add(result);
            }

            Assert.AreEqual(2, seen.Count);
        }

        [Test]
        public void NextFloatAround()
        {
            for (int i = 0; i < 100; ++i)
            {
                float center = PRNG.Instance.NextFloat(-100f, 100f);
                float variance = PRNG.Instance.NextFloat(1f, 50f);
                float result = PRNG.Instance.NextFloatAround(center, variance);
                Assert.GreaterOrEqual(result, center - variance);
                Assert.Less(result, center + variance);
            }
        }

        [Test]
        public void NextIntAround()
        {
            for (int i = 0; i < 100; ++i)
            {
                int center = PRNG.Instance.Next(-100, 100);
                int variance = PRNG.Instance.Next(1, 50);
                int result = PRNG.Instance.NextIntAround(center, variance);
                Assert.GreaterOrEqual(result, center - variance);
                Assert.LessOrEqual(result, center + variance);
            }
        }

        [Test]
        public void NextSubset()
        {
            int[] items = Enumerable.Range(0, 20).ToArray();
            for (int count = 0; count <= items.Length; ++count)
            {
                int[] subset = PRNG.Instance.NextSubset(items, count).ToArray();
                Assert.AreEqual(count, subset.Length);
                foreach (int item in subset)
                {
                    Assert.Contains(item, items);
                }
            }
        }

        [Test]
        public void NextSubsetThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                PRNG.Instance.NextSubset<int>(null, 5).ToArray()
            );
        }

        [Test]
        public void NextSubsetThrowsOnNegativeCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PRNG.Instance.NextSubset(new[] { 1, 2, 3 }, -1).ToArray()
            );
        }

        [Test]
        public void NextSubsetThrowsOnCountExceedingSize()
        {
            Assert.Throws<ArgumentException>(() =>
                PRNG.Instance.NextSubset(new[] { 1, 2, 3 }, 5).ToArray()
            );
        }

        [Test]
        public void NextOfExceptWithList()
        {
            List<int> values = new() { 1, 2, 3, 4, 5 };
            HashSet<int> seen = new();
            for (int i = 0; i < 200; ++i)
            {
                int result = PRNG.Instance.NextOfExcept(values, 1, 2, 3);
                Assert.IsFalse(result == 1 || result == 2 || result == 3);
                Assert.IsTrue(result == 4 || result == 5);
                seen.Add(result);
            }

            Assert.AreEqual(2, seen.Count);
        }

        [Test]
        public void NextOfExceptWithEnumerable()
        {
            IEnumerable<int> values = Enumerable.Range(1, 5);
            HashSet<int> seen = new();
            for (int i = 0; i < 200; ++i)
            {
                int result = PRNG.Instance.NextOfExcept(values, 1, 2);
                Assert.IsFalse(result == 1 || result == 2);
                seen.Add(result);
            }

            Assert.GreaterOrEqual(seen.Count, 2);
        }

        [Test]
        public void NextOfParamsCoversAllInputs()
        {
            HashSet<int> seen = new();
            for (int i = 0; i < 100; ++i)
            {
                int value = PRNG.Instance.NextOfParams(1, 2, 3);
                Assert.That(new[] { 1, 2, 3 }, Does.Contain(value));
                seen.Add(value);
            }

            Assert.AreEqual(3, seen.Count);
        }

        [Test]
        public void NextEnumExceptSkipsAllProvidedExceptions()
        {
            HashSet<TestValues> seen = new();
            for (int i = 0; i < 200; ++i)
            {
                TestValues value = PRNG.Instance.NextEnumExcept(
                    TestValues.Value0,
                    TestValues.Value1,
                    TestValues.Value2,
                    TestValues.Value3,
                    TestValues.Value4
                );
                Assert.IsFalse(
                    value
                        is TestValues.Value0
                            or TestValues.Value1
                            or TestValues.Value2
                            or TestValues.Value3
                            or TestValues.Value4
                );
                seen.Add(value);
            }

            Assert.GreaterOrEqual(seen.Count, 1);
        }
    }
}
