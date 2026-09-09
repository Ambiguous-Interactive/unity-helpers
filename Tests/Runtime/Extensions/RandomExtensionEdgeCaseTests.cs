// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.TestDoubles;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RandomExtensionEdgeCaseTests
    {
        private static IEnumerable<TestCaseData> QuaternionSampleData()
        {
            yield return new TestCaseData(new[] { 1f, 1f, 1f }).SetName("Quaternion AllOnes");
            yield return new TestCaseData(new[] { 0f, 1f, 0f }).SetName("Quaternion ZeroAndOne");
            yield return new TestCaseData(new[] { float.NaN, float.NaN, float.NaN }).SetName(
                "Quaternion AllNaN"
            );
            yield return new TestCaseData(new[] { -5f, 2f, 0.25f }).SetName(
                "Quaternion OutOfRangeValues"
            );
        }

        [Test]
        public void NextVector3OnSphereFallsBackWhenGeneratorStuckAtPositiveOne()
        {
            EdgeCaseRandom random = new(floatFallback: 1f, maxFloatCalls: 400);
            Vector3 result = random.NextVector3OnSphere(3f);
            Assert.That(result, Is.EqualTo(new Vector3(3f, 0f, 0f)));
        }

        [Test]
        public void NextVector3OnSphereFallsBackWhenGeneratorStuckAtZeroAndRespectsCenter()
        {
            EdgeCaseRandom random = new(floatFallback: 0f, maxFloatCalls: 400);
            Vector3 center = new(-2f, 4f, 1f);
            Vector3 result = random.NextVector3OnSphere(-2f, center);
            Assert.That(result, Is.EqualTo(center + new Vector3(2f, 0f, 0f)));
        }

        [Test]
        public void NextVector3OnSphereHandlesNaNSamples()
        {
            EdgeCaseRandom random = new(floatSequence: Enumerable.Repeat(float.NaN, 512).ToArray());
            Vector3 center = new(1f, 2f, 3f);
            Vector3 result = random.NextVector3OnSphere(5f, center);
            Assert.That(result, Is.EqualTo(center + new Vector3(5f, 0f, 0f)));
        }

        [Test]
        public void NextVector3OnSphereReturnsCenterWhenRadiusZero()
        {
            EdgeCaseRandom random = new(floatFallback: 0.5f);
            Vector3 center = new(3f, -2f, 1f);
            Vector3 result = random.NextVector3OnSphere(0f, center);
            Assert.That(result, Is.EqualTo(center));
        }

        [Test]
        public void NextVector3OnSphereNormalizesNegativeRadiusMagnitude()
        {
            EdgeCaseRandom random = new(floatSequence: new[] { 0.5f, -0.25f, 0.125f });
            Vector3 sample = random.NextVector3OnSphere(-3f);
            Assert.That(sample.magnitude, Is.EqualTo(3f).Within(1e-4f));
        }

        [TestCaseSource(nameof(QuaternionSampleData))]
        public void NextQuaternionClampsMalformedSamples(float[] samples)
        {
            EdgeCaseRandom random = new(floatSequence: samples, floatFallback: 0.5f);
            Quaternion rotation = random.NextQuaternion();
            Assert.IsTrue(float.IsFinite(rotation.x));
            Assert.IsTrue(float.IsFinite(rotation.y));
            Assert.IsTrue(float.IsFinite(rotation.z));
            Assert.IsTrue(float.IsFinite(rotation.w));
            float magnitude = Mathf.Sqrt(
                rotation.x * rotation.x
                    + rotation.y * rotation.y
                    + rotation.z * rotation.z
                    + rotation.w * rotation.w
            );
            Assert.That(magnitude, Is.EqualTo(1f).Within(1e-3f));
        }
    }
}
