// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Math
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Math;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ParabolaTests
    {
        private const float Tolerance = 0.0001f;

        private static IEnumerable<TestCaseData> TryGetValueAtTestCases()
        {
            yield return new TestCaseData(10f, 20f, 0f, true, 0f).SetName(
                "TryGetValueAt.Origin.ReturnsZero"
            );
            yield return new TestCaseData(10f, 20f, 20f, true, 0f).SetName(
                "TryGetValueAt.AtLength.ReturnsZero"
            );
            yield return new TestCaseData(10f, 20f, 10f, true, 10f).SetName(
                "TryGetValueAt.AtVertex.ReturnsMaxHeight"
            );
            yield return new TestCaseData(10f, 20f, 5f, true, 7.5f).SetName(
                "TryGetValueAt.QuarterPoint.ReturnsCalculatedValue"
            );
            yield return new TestCaseData(10f, 20f, 15f, true, 7.5f).SetName(
                "TryGetValueAt.ThreeQuarterPoint.ReturnsSymmetricValue"
            );
            yield return new TestCaseData(10f, 20f, -1f, false, float.NaN).SetName(
                "TryGetValueAt.NegativeX.ReturnsFalse"
            );
            yield return new TestCaseData(10f, 20f, 21f, false, float.NaN).SetName(
                "TryGetValueAt.BeyondLength.ReturnsFalse"
            );
            yield return new TestCaseData(15f, 30f, 15f, true, 15f).SetName(
                "TryGetValueAt.DifferentParabola.ReturnsCorrectVertex"
            );
        }

        [TestCaseSource(nameof(TryGetValueAtTestCases))]
        public void TryGetValueAtReturnsExpected(
            float maxHeight,
            float length,
            float x,
            bool expectedResult,
            float expectedY
        )
        {
            Parabola parabola = new(maxHeight: maxHeight, length: length);

            bool result = parabola.TryGetValueAt(x, out float y);

            Assert.AreEqual(expectedResult, result);
            if (expectedResult)
            {
                Assert.AreEqual(expectedY, y, Tolerance);
            }
            else
            {
                Assert.IsTrue(float.IsNaN(y));
            }
        }

        private static IEnumerable<TestCaseData> TryGetValueAtNormalizedTestCases()
        {
            yield return new TestCaseData(10f, 20f, 0f, true, 0f).SetName(
                "TryGetValueAtNormalized.AtZero.ReturnsZero"
            );
            yield return new TestCaseData(10f, 20f, 1f, true, 0f).SetName(
                "TryGetValueAtNormalized.AtOne.ReturnsZero"
            );
            yield return new TestCaseData(10f, 20f, 0.5f, true, 10f).SetName(
                "TryGetValueAtNormalized.AtHalf.ReturnsMaxHeight"
            );
            yield return new TestCaseData(10f, 20f, 0.25f, true, 7.5f).SetName(
                "TryGetValueAtNormalized.AtQuarter.ReturnsCalculatedValue"
            );
            yield return new TestCaseData(10f, 20f, -0.1f, false, float.NaN).SetName(
                "TryGetValueAtNormalized.NegativeT.ReturnsFalse"
            );
            yield return new TestCaseData(10f, 20f, 1.1f, false, float.NaN).SetName(
                "TryGetValueAtNormalized.TGreaterThanOne.ReturnsFalse"
            );
        }

        [TestCaseSource(nameof(TryGetValueAtNormalizedTestCases))]
        public void TryGetValueAtNormalizedReturnsExpected(
            float maxHeight,
            float length,
            float t,
            bool expectedResult,
            float expectedY
        )
        {
            Parabola parabola = new(maxHeight: maxHeight, length: length);

            bool result = parabola.TryGetValueAtNormalized(t, out float y);

            Assert.AreEqual(expectedResult, result);
            if (expectedResult)
            {
                Assert.AreEqual(expectedY, y, Tolerance);
            }
            else
            {
                Assert.IsTrue(float.IsNaN(y));
            }
        }

        private static IEnumerable<TestCaseData> ConstructorInvalidParametersTestCases()
        {
            yield return new TestCaseData(10f, 0f).SetName(
                "Constructor.ZeroLength.ThrowsArgumentException"
            );
            yield return new TestCaseData(10f, -5f).SetName(
                "Constructor.NegativeLength.ThrowsArgumentException"
            );
            yield return new TestCaseData(0f, 10f).SetName(
                "Constructor.ZeroMaxHeight.ThrowsArgumentException"
            );
            yield return new TestCaseData(-5f, 10f).SetName(
                "Constructor.NegativeMaxHeight.ThrowsArgumentException"
            );
        }

        [TestCaseSource(nameof(ConstructorInvalidParametersTestCases))]
        public void ConstructorThrowsForInvalidParameters(float maxHeight, float length)
        {
            Assert.Throws<ArgumentException>(() =>
                new Parabola(maxHeight: maxHeight, length: length)
            );
        }

        private static IEnumerable<TestCaseData> FromCoefficientsInvalidTestCases()
        {
            yield return new TestCaseData(0.1f, 2f, 20f).SetName(
                "FromCoefficients.PositiveA.ThrowsArgumentException"
            );
            yield return new TestCaseData(0f, 2f, 20f).SetName(
                "FromCoefficients.ZeroA.ThrowsArgumentException"
            );
            yield return new TestCaseData(-0.1f, 1f, 20f).SetName(
                "FromCoefficients.InvalidIntercept.ThrowsArgumentException"
            );
            yield return new TestCaseData(-0.1f, 2f, -20f).SetName(
                "FromCoefficients.NegativeLength.ThrowsArgumentException"
            );
        }

        [TestCaseSource(nameof(FromCoefficientsInvalidTestCases))]
        public void FromCoefficientsThrowsForInvalidParameters(float a, float b, float length)
        {
            Assert.Throws<ArgumentException>(() =>
                Parabola.FromCoefficients(a: a, b: b, length: length)
            );
        }

        [Test]
        public void ConstructorWithValidParametersCreatesParabola()
        {
            Parabola parabola = new(maxHeight: 10f, length: 20f);

            Assert.AreEqual(20f, parabola.Length, Tolerance);
            Assert.AreEqual(10f, parabola.MaxHeight, Tolerance);
        }

        [Test]
        public void ConstructorCalculatesCorrectCoefficients()
        {
            Parabola parabola = new(maxHeight: 10f, length: 20f);

            Assert.AreEqual(-0.1f, parabola.A, Tolerance);
            Assert.AreEqual(2f, parabola.B, Tolerance);
        }

        [Test]
        public void FromCoefficientsWithValidParametersCreatesParabola()
        {
            Parabola parabola = Parabola.FromCoefficients(a: -0.1f, b: 2f, length: 20f);

            Assert.AreEqual(20f, parabola.Length, Tolerance);
            Assert.AreEqual(10f, parabola.MaxHeight, Tolerance);
            Assert.AreEqual(-0.1f, parabola.A, Tolerance);
            Assert.AreEqual(2f, parabola.B, Tolerance);
        }

        [Test]
        public void VertexXReturnsHalfLength()
        {
            Parabola parabola = new(maxHeight: 10f, length: 20f);

            Assert.AreEqual(10f, parabola.VertexX, Tolerance);
        }

        [Test]
        public void VertexReturnsCorrectCoordinates()
        {
            Parabola parabola = new(maxHeight: 15f, length: 30f);
            (float x, float y) vertex = parabola.Vertex;

            Assert.AreEqual(15f, vertex.x, Tolerance);
            Assert.AreEqual(15f, vertex.y, Tolerance);
        }

        [Test]
        public void XRangeReturnsCorrectBounds()
        {
            Parabola parabola = new(maxHeight: 10f, length: 25f);
            (float min, float max) range = parabola.XRange;

            Assert.AreEqual(0f, range.min, Tolerance);
            Assert.AreEqual(25f, range.max, Tolerance);
        }

        [Test]
        public void TryGetValueAtIsSymmetricAroundVertex()
        {
            Parabola parabola = new(maxHeight: 10f, length: 20f);

            Assert.IsTrue(parabola.TryGetValueAt(5f, out float y1));
            Assert.IsTrue(parabola.TryGetValueAt(15f, out float y2));

            Assert.AreEqual(y1, y2, Tolerance);
        }

        [Test]
        public void GetValueAtUncheckedCalculatesCorrectValue()
        {
            Parabola parabola = new(maxHeight: 10f, length: 20f);

            float y = parabola.GetValueAtUnchecked(5f);

            Assert.AreEqual(7.5f, y, Tolerance);
        }

        [Test]
        public void GetValueAtUncheckedDoesNotCheckBounds()
        {
            Parabola parabola = new(maxHeight: 10f, length: 20f);

            float yNegative = parabola.GetValueAtUnchecked(-5f);
            float yBeyond = parabola.GetValueAtUnchecked(25f);

            Assert.IsFalse(float.IsNaN(yNegative));
            Assert.IsFalse(float.IsNaN(yBeyond));
        }

        [Test]
        public void EqualsReturnsTrueForSameParameters()
        {
            Parabola p1 = new(maxHeight: 10f, length: 20f);
            Parabola p2 = new(maxHeight: 10f, length: 20f);

            Assert.IsTrue(p1.Equals(p2));
            Assert.IsTrue(p1 == p2);
            Assert.IsFalse(p1 != p2);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentParameters()
        {
            Parabola p1 = new(maxHeight: 10f, length: 20f);
            Parabola p2 = new(maxHeight: 15f, length: 20f);

            Assert.IsFalse(p1.Equals(p2));
            Assert.IsFalse(p1 == p2);
            Assert.IsTrue(p1 != p2);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentLength()
        {
            Parabola p1 = new(maxHeight: 10f, length: 20f);
            Parabola p2 = new(maxHeight: 10f, length: 25f);

            Assert.IsFalse(p1.Equals(p2));
        }

        [Test]
        public void EqualsWithObjectReturnsTrueForSameParabola()
        {
            Parabola p1 = new(maxHeight: 10f, length: 20f);
            object p2 = new Parabola(maxHeight: 10f, length: 20f);

            Assert.IsTrue(p1.Equals(p2));
        }

        [Test]
        public void EqualsWithObjectReturnsFalseForNonParabola()
        {
            Parabola p1 = new(maxHeight: 10f, length: 20f);
            object p2 = "not a parabola";

            Assert.IsFalse(p1.Equals(p2));
        }

        [Test]
        public void GetHashCodeReturnsSameValueForEqualParabolas()
        {
            Parabola p1 = new(maxHeight: 10f, length: 20f);
            Parabola p2 = new(maxHeight: 10f, length: 20f);

            Assert.AreEqual(p1.GetHashCode(), p2.GetHashCode());
        }

        [Test]
        public void GetHashCodeReturnsDifferentValueForDifferentParabolas()
        {
            Parabola p1 = new(maxHeight: 10f, length: 20f);
            Parabola p2 = new(maxHeight: 15f, length: 20f);

            Assert.AreNotEqual(p1.GetHashCode(), p2.GetHashCode());
        }

        [Test]
        public void ToStringReturnsFormattedString()
        {
            Parabola parabola = new(maxHeight: 10f, length: 20f);

            string result = parabola.ToString();

            StringAssert.Contains("maxHeight=10.00", result);
            StringAssert.Contains("length=20.00", result);
            StringAssert.Contains("vertex=(10.00, 10.00)", result);
        }

        [Test]
        public void SmallParabolaMaintainsPrecision()
        {
            Parabola parabola = new(maxHeight: 0.001f, length: 0.002f);

            bool result = parabola.TryGetValueAt(0.001f, out float y);

            Assert.IsTrue(result);
            Assert.AreEqual(0.001f, y, 0.000001f);
        }

        [Test]
        public void LargeParabolaMaintainsPrecision()
        {
            Parabola parabola = new(maxHeight: 10000f, length: 20000f);

            bool result = parabola.TryGetValueAt(10000f, out float y);

            Assert.IsTrue(result);
            Assert.AreEqual(10000f, y, 1f);
        }

        [Test]
        public void ParabolaWithVeryDifferentScalesWorks()
        {
            Parabola parabola = new(maxHeight: 0.1f, length: 1000f);

            Assert.IsTrue(parabola.TryGetValueAt(0f, out float y0));
            Assert.IsTrue(parabola.TryGetValueAt(500f, out float yVertex));
            Assert.IsTrue(parabola.TryGetValueAt(1000f, out float yEnd));

            Assert.AreEqual(0f, y0, Tolerance);
            Assert.AreEqual(0.1f, yVertex, 0.001f);
            Assert.AreEqual(0f, yEnd, Tolerance);
        }

        [Test]
        public void ParabolaIsSymmetricAroundVertex()
        {
            Parabola parabola = new(maxHeight: 12f, length: 30f);

            for (float offset = 1f; offset <= 10f; offset += 1f)
            {
                float x1 = 15f - offset;
                float x2 = 15f + offset;

                Assert.IsTrue(parabola.TryGetValueAt(x1, out float y1));
                Assert.IsTrue(parabola.TryGetValueAt(x2, out float y2));

                Assert.AreEqual(y1, y2, Tolerance, $"Values at x={x1} and x={x2} should be equal");
            }
        }

        [Test]
        public void ParabolaInterceptsAreAtExpectedLocations()
        {
            Parabola parabola = new(maxHeight: 8f, length: 16f);

            Assert.IsTrue(parabola.TryGetValueAt(0f, out float y0));
            Assert.IsTrue(parabola.TryGetValueAt(16f, out float yEnd));

            Assert.AreEqual(0f, y0, Tolerance, "Y should be 0 at x=0");
            Assert.AreEqual(0f, yEnd, Tolerance, "Y should be 0 at x=Length");
        }

        [Test]
        public void ParabolaVertexIsAtExpectedLocation()
        {
            Parabola parabola = new(maxHeight: 25f, length: 50f);

            float vertexX = parabola.VertexX;
            Assert.IsTrue(parabola.TryGetValueAt(vertexX, out float vertexY));

            Assert.AreEqual(25f, vertexX, Tolerance);
            Assert.AreEqual(25f, vertexY, Tolerance);
        }

        [Test]
        public void ParabolaDerivativeAtVertexIsZero()
        {
            Parabola parabola = new(maxHeight: 10f, length: 20f);

            float vertexX = parabola.VertexX;
            float derivative = 2f * parabola.A * vertexX + parabola.B;

            Assert.AreEqual(0f, derivative, Tolerance);
        }

        [Test]
        public void ParabolaHasNegativeSecondDerivative()
        {
            Parabola parabola = new(maxHeight: 10f, length: 20f);

            float secondDerivative = 2f * parabola.A;

            Assert.Less(secondDerivative, 0f);
        }

        [Test]
        public void FromCoefficientsAndConstructorProduceSameParabola()
        {
            Parabola p1 = new(maxHeight: 10f, length: 20f);
            Parabola p2 = Parabola.FromCoefficients(p1.A, p1.B, p1.Length);

            Assert.AreEqual(p1.Length, p2.Length, Tolerance);
            Assert.AreEqual(p1.MaxHeight, p2.MaxHeight, Tolerance);
            Assert.AreEqual(p1.A, p2.A, Tolerance);
            Assert.AreEqual(p1.B, p2.B, Tolerance);
        }
    }
}
