// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Animation;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Exhaustive test suite for AnimationCreatorWindow functionality including
    /// FramerateMode, AnimationData, curve-based timing, preview calculations,
    /// cycle offset, and clip generation.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class AnimationCreatorWindowTests : CommonTestBase
    {
        // Test Data Generators

        private static IEnumerable<TestCaseData> FramerateModeEnumValuesCases()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            yield return new TestCaseData(FramerateMode.None, 0).SetName(
#pragma warning restore CS0618 // Type or member is obsolete
                "FramerateMode.None.HasValue0"
            );
            yield return new TestCaseData(FramerateMode.Constant, 1).SetName(
                "FramerateMode.Constant.HasValue1"
            );
            yield return new TestCaseData(FramerateMode.Curve, 2).SetName(
                "FramerateMode.Curve.HasValue2"
            );
        }

        private static IEnumerable<TestCaseData> ConstantFpsTestCases()
        {
            // Normal cases
            yield return new TestCaseData(12f, 0, 12f).SetName("ConstantFps.Normal.12fps");
            yield return new TestCaseData(24f, 0, 24f).SetName("ConstantFps.Normal.24fps");
            yield return new TestCaseData(30f, 0, 30f).SetName("ConstantFps.Normal.30fps");
            yield return new TestCaseData(60f, 0, 60f).SetName("ConstantFps.Normal.60fps");

            // Edge cases
            yield return new TestCaseData(0.001f, 0, 0.001f).SetName(
                "ConstantFps.Edge.VerySmallFps"
            );
            yield return new TestCaseData(1000f, 0, 1000f).SetName("ConstantFps.Edge.HighFps");

            // Negative/Zero cases (should use default)
            yield return new TestCaseData(0f, 0, AnimationData.DefaultFramesPerSecond).SetName(
                "ConstantFps.Negative.ZeroUsesDefault"
            );
            yield return new TestCaseData(-5f, 0, AnimationData.DefaultFramesPerSecond).SetName(
                "ConstantFps.Negative.NegativeUsesDefault"
            );
        }

        private static IEnumerable<TestCaseData> CurveFpsTestCases()
        {
            // Normal cases with simple curves
            yield return new TestCaseData(AnimationCurve.Constant(0f, 1f, 12f), 5, 0, 12f).SetName(
                "CurveFps.Normal.FlatCurve12"
            );
            yield return new TestCaseData(AnimationCurve.Constant(0f, 1f, 24f), 5, 2, 24f).SetName(
                "CurveFps.Normal.FlatCurve24.MiddleFrame"
            );

            // Linear curves
            yield return new TestCaseData(
                AnimationCurve.Linear(0f, 10f, 1f, 20f),
                5,
                0,
                10f
            ).SetName("CurveFps.Normal.LinearStart");
            yield return new TestCaseData(
                AnimationCurve.Linear(0f, 10f, 1f, 20f),
                5,
                4,
                20f
            ).SetName("CurveFps.Normal.LinearEnd");

            // Edge cases
            yield return new TestCaseData(AnimationCurve.Constant(0f, 1f, 12f), 1, 0, 12f).SetName(
                "CurveFps.Edge.SingleFrame"
            );

            // Extreme FPS values
            yield return new TestCaseData(
                AnimationCurve.Constant(0f, 1f, 1000f),
                5,
                2,
                1000f
            ).SetName("CurveFps.Extreme.VeryHighFps");
            yield return new TestCaseData(
                AnimationCurve.Constant(0f, 1f, 0.1f),
                5,
                2,
                0.1f
            ).SetName("CurveFps.Extreme.VeryLowFps");
        }

        private static IEnumerable<TestCaseData> ScrubberFrameCalculationCases()
        {
            // Normal cases
            yield return new TestCaseData(0f, 5, 0).SetName("Scrubber.Normal.Start");
            yield return new TestCaseData(0.5f, 5, 2).SetName("Scrubber.Normal.Middle");
            yield return new TestCaseData(1f, 5, 4).SetName("Scrubber.Normal.End");
            yield return new TestCaseData(0.25f, 5, 1).SetName("Scrubber.Normal.QuarterWay");
            yield return new TestCaseData(0.75f, 5, 3).SetName("Scrubber.Normal.ThreeQuarters");

            // Edge cases
            yield return new TestCaseData(0f, 1, 0).SetName("Scrubber.Edge.SingleFrame");
            yield return new TestCaseData(1f, 1, 0).SetName("Scrubber.Edge.SingleFrameEnd");
            yield return new TestCaseData(0.5f, 2, 1).SetName("Scrubber.Edge.TwoFrames");
            yield return new TestCaseData(0f, 100, 0).SetName("Scrubber.Edge.ManyFramesStart");
            yield return new TestCaseData(1f, 100, 99).SetName("Scrubber.Edge.ManyFramesEnd");

            // Boundary clamping
            yield return new TestCaseData(-1f, 5, 0).SetName("Scrubber.Negative.ClampedToStart");
            yield return new TestCaseData(2f, 5, 4).SetName("Scrubber.Negative.ClampedToEnd");
            yield return new TestCaseData(-100f, 5, 0).SetName("Scrubber.Extreme.VeryNegative");
            yield return new TestCaseData(100f, 5, 4).SetName("Scrubber.Extreme.VeryPositive");

            // Impossible cases
            yield return new TestCaseData(0.5f, 0, 0).SetName("Scrubber.Impossible.ZeroFrames");
            yield return new TestCaseData(0.5f, -5, 0).SetName(
                "Scrubber.Impossible.NegativeFrames"
            );

            // With 2 frames, value * (2-1) = value, so the .5 boundary is at scrubberValue = 0.5.
            yield return new TestCaseData(0.5f, 2, 1).SetName(
                "Scrubber.BankersRounding.TwoFrames.ExactHalf"
            );
            yield return new TestCaseData(0.500001f, 2, 1).SetName(
                "Scrubber.BankersRounding.TwoFrames.JustAboveHalf"
            );
            yield return new TestCaseData(0.499999f, 2, 0).SetName(
                "Scrubber.BankersRounding.TwoFrames.JustBelowHalf"
            );

            // With 4 frames: value * (4-1) = value * 3, boundaries at 0.5/3, 1.5/3, 2.5/3
            yield return new TestCaseData(0.5f / 3f, 4, 1).SetName(
                "Scrubber.BankersRounding.FourFrames.FirstHalf"
            );
            yield return new TestCaseData(1.5f / 3f, 4, 2).SetName(
                "Scrubber.BankersRounding.FourFrames.SecondHalf"
            );
            yield return new TestCaseData(2.5f / 3f, 4, 3).SetName(
                "Scrubber.BankersRounding.FourFrames.ThirdHalf"
            );

            // With 6 frames: value * (6-1) = value * 5, boundaries at 0.5/5, 1.5/5, 2.5/5, 3.5/5, 4.5/5
            yield return new TestCaseData(0.5f / 5f, 6, 1).SetName(
                "Scrubber.BankersRounding.SixFrames.FirstHalf"
            );
            yield return new TestCaseData(1.5f / 5f, 6, 2).SetName(
                "Scrubber.BankersRounding.SixFrames.SecondHalf"
            );
            yield return new TestCaseData(2.5f / 5f, 6, 3).SetName(
                "Scrubber.BankersRounding.SixFrames.ThirdHalf"
            );
            yield return new TestCaseData(3.5f / 5f, 6, 4).SetName(
                "Scrubber.BankersRounding.SixFrames.FourthHalf"
            );
            yield return new TestCaseData(4.5f / 5f, 6, 5).SetName(
                "Scrubber.BankersRounding.SixFrames.FifthHalf"
            );

            // With 8 frames: value * (8-1) = value * 7
            yield return new TestCaseData(0.5f / 7f, 8, 1).SetName(
                "Scrubber.BankersRounding.EightFrames.FirstHalf"
            );
            yield return new TestCaseData(3.5f / 7f, 8, 4).SetName(
                "Scrubber.BankersRounding.EightFrames.MiddleHalf"
            );
            yield return new TestCaseData(6.5f / 7f, 8, 7).SetName(
                "Scrubber.BankersRounding.EightFrames.LastHalf"
            );

            // With 10 frames: value * (10-1) = value * 9
            yield return new TestCaseData(0.5f / 9f, 10, 1).SetName(
                "Scrubber.BankersRounding.TenFrames.FirstHalf"
            );
            yield return new TestCaseData(4.5f / 9f, 10, 5).SetName(
                "Scrubber.BankersRounding.TenFrames.MiddleHalf"
            );
            yield return new TestCaseData(8.5f / 9f, 10, 9).SetName(
                "Scrubber.BankersRounding.TenFrames.LastHalf"
            );

            // Float precision edge cases - testing exact 0.5f values
            yield return new TestCaseData(0.5f, 3, 1).SetName(
                "Scrubber.FloatPrecision.ThreeFrames.ExactHalf"
            );
            yield return new TestCaseData(0.5f, 4, 2).SetName(
                "Scrubber.FloatPrecision.FourFrames.ExactHalf"
            );
            yield return new TestCaseData(0.5f, 5, 2).SetName(
                "Scrubber.FloatPrecision.FiveFrames.ExactHalf"
            );
            yield return new TestCaseData(0.5f, 6, 3).SetName(
                "Scrubber.FloatPrecision.SixFrames.ExactHalf"
            );
            yield return new TestCaseData(0.5f, 7, 3).SetName(
                "Scrubber.FloatPrecision.SevenFrames.ExactHalf"
            );
            yield return new TestCaseData(0.5f, 8, 4).SetName(
                "Scrubber.FloatPrecision.EightFrames.ExactHalf"
            );
            yield return new TestCaseData(0.5f, 9, 4).SetName(
                "Scrubber.FloatPrecision.NineFrames.ExactHalf"
            );
            yield return new TestCaseData(0.5f, 10, 5).SetName(
                "Scrubber.FloatPrecision.TenFrames.ExactHalf"
            );

            // Just above and just below .5 for intermediate values
            float epsilon = 0.000001f;
            yield return new TestCaseData(0.5f + epsilon, 10, 5).SetName(
                "Scrubber.FloatPrecision.TenFrames.JustAboveHalf"
            );
            yield return new TestCaseData(0.5f - epsilon, 10, 4).SetName(
                "Scrubber.FloatPrecision.TenFrames.JustBelowHalf"
            );

            /*
                For 3 frames 0.5 * 2 = 1.0, for 5 frames 0.5 * 4 = 2.0 and for 7 frames 0.5 * 6 =
                3.0, none of which is a .5 boundary. A value that yields exactly X.5 is
                X.5 / (frameCount - 1).
            */
            yield return new TestCaseData(1.5f / 4f, 5, 2).SetName(
                "Scrubber.BankersRounding.FiveFrames.YieldsOnePointFive"
            );
            yield return new TestCaseData(2.5f / 4f, 5, 3).SetName(
                "Scrubber.BankersRounding.FiveFrames.YieldsTwoPointFive"
            );
            yield return new TestCaseData(3.5f / 4f, 5, 4).SetName(
                "Scrubber.BankersRounding.FiveFrames.YieldsThreePointFive"
            );
        }

        private static IEnumerable<TestCaseData> CycleOffsetClampCases()
        {
            // Normal cases
            yield return new TestCaseData(0f, 0f).SetName("CycleOffset.Normal.Zero");
            yield return new TestCaseData(0.25f, 0.25f).SetName("CycleOffset.Normal.Quarter");
            yield return new TestCaseData(0.5f, 0.5f).SetName("CycleOffset.Normal.Half");
            yield return new TestCaseData(0.75f, 0.75f).SetName("CycleOffset.Normal.ThreeQuarters");
            yield return new TestCaseData(1f, 1f).SetName("CycleOffset.Normal.Full");

            // Edge cases
            yield return new TestCaseData(0.0001f, 0.0001f).SetName("CycleOffset.Edge.VerySmall");
            yield return new TestCaseData(0.9999f, 0.9999f).SetName(
                "CycleOffset.Edge.AlmostFullLoop"
            );

            // Clamping cases
            yield return new TestCaseData(-0.1f, 0f).SetName("CycleOffset.Negative.ClampedToZero");
            yield return new TestCaseData(-1f, 0f).SetName(
                "CycleOffset.Negative.NegativeOneClamped"
            );
            yield return new TestCaseData(1.5f, 1f).SetName("CycleOffset.Negative.OverOneClamped");
            yield return new TestCaseData(2f, 1f).SetName("CycleOffset.Negative.TwoClamped");

            // Extreme values
            yield return new TestCaseData(-1000f, 0f).SetName("CycleOffset.Extreme.VeryNegative");
            yield return new TestCaseData(1000f, 1f).SetName("CycleOffset.Extreme.VeryPositive");
            yield return new TestCaseData(float.NegativeInfinity, 0f).SetName(
                "CycleOffset.Impossible.NegativeInfinity"
            );
            yield return new TestCaseData(float.PositiveInfinity, 1f).SetName(
                "CycleOffset.Impossible.PositiveInfinity"
            );
        }

        private static IEnumerable<TestCaseData> AnimationClipGenerationCases()
        {
            // Constant framerate cases
            yield return new TestCaseData(
                FramerateMode.Constant,
                12f,
                AnimationCurve.Constant(0f, 1f, 12f),
                4,
                false,
                0f
            ).SetName("ClipGen.Constant.12fps.4Frames");
            yield return new TestCaseData(
                FramerateMode.Constant,
                24f,
                AnimationCurve.Constant(0f, 1f, 24f),
                8,
                true,
                0f
            ).SetName("ClipGen.Constant.24fps.8Frames.Loop");
            yield return new TestCaseData(
                FramerateMode.Constant,
                30f,
                AnimationCurve.Constant(0f, 1f, 30f),
                2,
                false,
                0.5f
            ).SetName("ClipGen.Constant.30fps.2Frames.HalfOffset");

            // Curve framerate cases
            yield return new TestCaseData(
                FramerateMode.Curve,
                12f,
                AnimationCurve.EaseInOut(0f, 6f, 1f, 18f),
                6,
                false,
                0f
            ).SetName("ClipGen.Curve.EaseIn.6Frames");
            yield return new TestCaseData(
                FramerateMode.Curve,
                12f,
                AnimationCurve.Linear(0f, 10f, 1f, 30f),
                5,
                true,
                0.25f
            ).SetName("ClipGen.Curve.Linear.5Frames.Loop.QuarterOffset");

            // Edge cases
            yield return new TestCaseData(
                FramerateMode.Constant,
                60f,
                AnimationCurve.Constant(0f, 1f, 60f),
                1,
                false,
                0f
            ).SetName("ClipGen.Edge.SingleFrame");
            yield return new TestCaseData(
                FramerateMode.Constant,
                1f,
                AnimationCurve.Constant(0f, 1f, 1f),
                3,
                false,
                0f
            ).SetName("ClipGen.Edge.1fps");

            // Extreme cases
            yield return new TestCaseData(
                FramerateMode.Constant,
                120f,
                AnimationCurve.Constant(0f, 1f, 120f),
                10,
                true,
                1f
            ).SetName("ClipGen.Extreme.120fps.FullOffset");
        }

        // FramerateMode Enum Tests

        [TestCaseSource(nameof(FramerateModeEnumValuesCases))]
        public void FramerateModeHasCorrectExplicitValue(FramerateMode mode, int expectedValue)
        {
            Assert.AreEqual(expectedValue, (int)mode);
        }

        [Test]
        public void FramerateModeNoneIsObsolete()
        {
            System.Reflection.FieldInfo field = typeof(FramerateMode).GetField(
#pragma warning disable CS0618 // Type or member is obsolete
                nameof(FramerateMode.None)
#pragma warning restore CS0618 // Type or member is obsolete
            );
            Assert.IsTrue(field != null, "FramerateMode.None field should exist");

            object[] obsoleteAttributes = field.GetCustomAttributes(
                typeof(ObsoleteAttribute),
                false
            );
            Assert.AreEqual(
                1,
                obsoleteAttributes.Length,
                "FramerateMode.None should be marked obsolete"
            );
        }

        [Test]
        public void FramerateModeAllValuesAreDistinct()
        {
            Array values = Enum.GetValues(typeof(FramerateMode));
            HashSet<int> intValues = new();

            for (int i = 0; i < values.Length; i++)
            {
                object value = values.GetValue(i);
                int intValue = (int)value;
                Assert.IsTrue(
                    intValues.Add(intValue),
                    $"Duplicate value {intValue} found for {value}"
                );
            }
        }

        // AnimationData Tests

        [Test]
        public void AnimationDataDefaultConstructorInitializesFieldsCorrectly()
        {
            AnimationData data = new();

            Assert.IsTrue(
                data.frames != null,
                "Frames list should be initialized by default constructor"
            );
            Assert.AreEqual(0, data.frames.Count);
            Assert.AreEqual(AnimationData.DefaultFramesPerSecond, data.framesPerSecond);
            Assert.AreEqual(string.Empty, data.animationName);
            Assert.IsFalse(data.isCreatedFromAutoParse);
            Assert.IsFalse(data.loop);
            Assert.AreEqual(FramerateMode.Constant, data.framerateMode);
            Assert.IsTrue(
                data.framesPerSecondCurve != null,
                "Frames per second curve should be initialized by default constructor"
            );
            Assert.AreEqual(0f, data.cycleOffset);
            Assert.IsFalse(data.showPreview);
        }

        [Test]
        public void AnimationDataDefaultFramesPerSecondIs12()
        {
            Assert.AreEqual(12f, AnimationData.DefaultFramesPerSecond);
        }

        [Test]
        public void AnimationDataDefaultCurveIsConstantAtDefaultFps()
        {
            AnimationData data = new();

            AnimationCurve curve = data.framesPerSecondCurve;
            Assert.IsTrue(curve != null, "Default curve should not be null");

            float startValue = curve.Evaluate(0f);
            float midValue = curve.Evaluate(0.5f);
            float endValue = curve.Evaluate(1f);

            Assert.AreEqual(
                AnimationData.DefaultFramesPerSecond,
                startValue,
                0.001f,
                "Curve start value"
            );
            Assert.AreEqual(
                AnimationData.DefaultFramesPerSecond,
                midValue,
                0.001f,
                "Curve mid value"
            );
            Assert.AreEqual(
                AnimationData.DefaultFramesPerSecond,
                endValue,
                0.001f,
                "Curve end value"
            );
        }

        [Test]
        public void AnimationDataShowPreviewIsNonSerialized()
        {
            System.Reflection.FieldInfo field = typeof(AnimationData).GetField(
                nameof(AnimationData.showPreview)
            );
            Assert.IsTrue(field != null, "AnimationData.showPreview field should exist");

            object[] nonSerializedAttributes = field.GetCustomAttributes(
                typeof(NonSerializedAttribute),
                false
            );
            Assert.AreEqual(
                1,
                nonSerializedAttributes.Length,
                "showPreview should have NonSerialized attribute"
            );
        }

        // GetCurrentFps Tests

        [TestCaseSource(nameof(ConstantFpsTestCases))]
        public void GetCurrentFpsConstantModeReturnsConfiguredOrDefaultFps(
            float fps,
            int frameIndex,
            float expectedFps
        )
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Constant,
                framesPerSecond = fps,
                frames = CreateSpriteList(5),
            };

            float result = AnimationCreatorWindow.GetCurrentFpsForTests(data, frameIndex);

            Assert.AreEqual(expectedFps, result, 0.001f);
        }

        [TestCaseSource(nameof(CurveFpsTestCases))]
        public void GetCurrentFpsCurveModeEvaluatesCurveAtNormalizedPosition(
            AnimationCurve curve,
            int frameCount,
            int frameIndex,
            float expectedFps
        )
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Curve,
                framesPerSecondCurve = curve,
                frames = CreateSpriteList(frameCount),
            };

            float result = AnimationCreatorWindow.GetCurrentFpsForTests(data, frameIndex);

            Assert.AreEqual(expectedFps, result, 0.01f);
        }

        [Test]
        public void GetCurrentFpsCurveModeWithZeroFpsValueReturnsDefault()
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Curve,
                framesPerSecondCurve = AnimationCurve.Constant(0f, 1f, 0f),
                frames = CreateSpriteList(5),
            };

            float result = AnimationCreatorWindow.GetCurrentFpsForTests(data, 2);

            Assert.AreEqual(AnimationData.DefaultFramesPerSecond, result);
        }

        [Test]
        public void GetCurrentFpsCurveModeWithNegativeFpsValueReturnsDefault()
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Curve,
                framesPerSecondCurve = AnimationCurve.Constant(0f, 1f, -10f),
                frames = CreateSpriteList(5),
            };

            float result = AnimationCreatorWindow.GetCurrentFpsForTests(data, 2);

            Assert.AreEqual(AnimationData.DefaultFramesPerSecond, result);
        }

        [Test]
        public void GetCurrentFpsSingleFrameAnimationReturnsCorrectFps()
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Curve,
                framesPerSecondCurve = AnimationCurve.Linear(0f, 6f, 1f, 24f),
                frames = CreateSpriteList(1),
            };

            float result = AnimationCreatorWindow.GetCurrentFpsForTests(data, 0);

            Assert.AreEqual(6f, result, 0.01f);
        }

        // Scrubber Calculation Tests

        [TestCaseSource(nameof(ScrubberFrameCalculationCases))]
        public void CalculateScrubberFrameReturnsExpectedFrame(
            float scrubberValue,
            int frameCount,
            int expectedFrame
        )
        {
            int result = AnimationCreatorWindow.CalculateScrubberFrame(scrubberValue, frameCount);

            float rawCalculation = scrubberValue * Mathf.Max(0, frameCount - 1);
            string diagnosticMessage =
                $"scrubberValue={scrubberValue}, frameCount={frameCount}, rawCalculation={rawCalculation}, "
                + $"expected={expectedFrame}, actual={result}, "
                + $"FloorToInt(raw + 0.5f)={Mathf.FloorToInt(rawCalculation + 0.5f)}, "
                + $"RoundToInt(raw)={Mathf.RoundToInt(rawCalculation)}";
            Assert.AreEqual(expectedFrame, result, diagnosticMessage);
        }

        [Test]
        public void CalculateScrubberFrameRoundsCorrectly()
        {
            Assert.AreEqual(0, AnimationCreatorWindow.CalculateScrubberFrame(0.0f, 10));
            Assert.AreEqual(0, AnimationCreatorWindow.CalculateScrubberFrame(0.05f, 10));
            Assert.AreEqual(1, AnimationCreatorWindow.CalculateScrubberFrame(0.06f, 10));
            Assert.AreEqual(5, AnimationCreatorWindow.CalculateScrubberFrame(0.5f, 10));
            Assert.AreEqual(5, AnimationCreatorWindow.CalculateScrubberFrame(0.555f, 10));
            Assert.AreEqual(5, AnimationCreatorWindow.CalculateScrubberFrame(0.556f, 10));
            Assert.AreEqual(9, AnimationCreatorWindow.CalculateScrubberFrame(1.0f, 10));
        }

        // Cycle Offset Tests

        [TestCaseSource(nameof(CycleOffsetClampCases))]
        public void CalculateCycleOffsetClampedClampsCorrectly(float input, float expected)
        {
            float result = AnimationCreatorWindow.CalculateCycleOffsetClamped(input);

            if (float.IsNaN(expected))
            {
                Assert.IsTrue(float.IsNaN(result), "Expected NaN result");
            }
            else
            {
                Assert.AreEqual(expected, result, 0.0001f);
            }
        }

        // Animation Clip Generation Tests

        [TestCaseSource(nameof(AnimationClipGenerationCases))]
        public void CreateAnimationClipGeneratesCorrectClip(
            FramerateMode mode,
            float fps,
            AnimationCurve curve,
            int frameCount,
            bool loop,
            float cycleOffset
        )
        {
            AnimationData data = new()
            {
                framerateMode = mode,
                framesPerSecond = fps,
                framesPerSecondCurve = curve,
                loop = loop,
                cycleOffset = cycleOffset,
            };

            List<Sprite> frames = CreateTrackedSpriteList(frameCount);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            Assert.IsTrue(clip != null, "Created animation clip should not be null");
            Assert.AreEqual(fps, clip.frameRate, "Frame rate should match");

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            Assert.AreEqual(loop, settings.loopTime, "Loop setting should match");
            Assert.AreEqual(
                Mathf.Clamp01(cycleOffset),
                settings.cycleOffset,
                0.0001f,
                "Cycle offset should match"
            );
        }

        [Test]
        public void CreateAnimationClipConstantFramerateHasUniformKeyframeTiming()
        {
            float fps = 24f;
            int frameCount = 5;

            AnimationData data = new()
            {
                framerateMode = FramerateMode.Constant,
                framesPerSecond = fps,
                loop = false,
            };

            List<Sprite> frames = CreateTrackedSpriteList(frameCount);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length);

            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );
            Assert.AreEqual(frameCount, keyframes.Length);

            float expectedInterval = 1f / fps;
            for (int i = 1; i < keyframes.Length; i++)
            {
                float actualInterval = keyframes[i].time - keyframes[i - 1].time;
                Assert.AreEqual(
                    expectedInterval,
                    actualInterval,
                    0.0001f,
                    $"Interval between frames {i - 1} and {i}"
                );
            }
        }

        [Test]
        public void CreateAnimationClipCurveFramerateHasVariableKeyframeTiming()
        {
            AnimationCurve fpsCurve = AnimationCurve.Linear(0f, 10f, 1f, 30f);
            int frameCount = 5;

            AnimationData data = new()
            {
                framerateMode = FramerateMode.Curve,
                framesPerSecond = 12f,
                framesPerSecondCurve = fpsCurve,
                loop = false,
            };

            List<Sprite> frames = CreateTrackedSpriteList(frameCount);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );

            Assert.AreEqual(0f, keyframes[0].time, 0.0001f, "First keyframe at time 0");

            float firstInterval = keyframes[1].time - keyframes[0].time;
            float lastInterval = keyframes[^1].time - keyframes[^2].time;

            Assert.Greater(
                firstInterval,
                lastInterval,
                "First interval should be longer (slower FPS at start)"
            );
        }

        [Test]
        public void CreateAnimationClipWithSingleFrameCreatesValidClip()
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Constant,
                framesPerSecond = 24f,
                loop = false,
            };

            List<Sprite> frames = CreateTrackedSpriteList(1);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            Assert.IsTrue(clip != null, "Created animation clip should not be null");

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length);

            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );
            Assert.AreEqual(1, keyframes.Length);
            Assert.AreEqual(0f, keyframes[0].time);
        }

        [Test]
        public void CreateAnimationClipPreservesFrameOrder()
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Constant,
                framesPerSecond = 12f,
            };

            List<Sprite> frames = CreateTrackedSpriteList(5);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );

            for (int i = 0; i < frames.Count; i++)
            {
                Assert.AreSame(
                    frames[i],
                    keyframes[i].value,
                    $"Frame {i} should be preserved in order"
                );
            }
        }

        // Edge Case Tests

        [Test]
        public void AnimationDataCanSetEmptyAnimationName()
        {
            AnimationData data = new() { animationName = string.Empty };

            Assert.AreEqual(string.Empty, data.animationName);
        }

        [Test]
        public void AnimationDataCanSetNullFramesList()
        {
            AnimationData data = new() { frames = null };

            Assert.IsTrue(
                data.frames == null,
                "Frames list should be null when explicitly set to null"
            );
        }

        [Test]
        public void GetCurrentFpsWithEmptyFramesListHandlesGracefully()
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Curve,
                framesPerSecondCurve = AnimationCurve.Linear(0f, 6f, 1f, 24f),
                frames = new List<Sprite>(),
            };

            float result = AnimationCreatorWindow.GetCurrentFpsForTests(data, 0);

            Assert.AreEqual(6f, result, 0.01f);
        }

        [Test]
        public void FramerateModeCastingFromInvalidIntDoesNotThrow()
        {
            FramerateMode mode = (FramerateMode)999;

            Assert.AreEqual(999, (int)mode);
            Assert.DoesNotThrow(() => _ = mode.ToString());
        }

        // Integration Tests

        [Test]
        public void FullWorkflowCreateAnimationWithCurveFramerate()
        {
            AnimationData data = new()
            {
                animationName = "TestWalkCycle",
                framerateMode = FramerateMode.Curve,
                framesPerSecond = 12f,
                framesPerSecondCurve = AnimationCurve.EaseInOut(0f, 8f, 1f, 16f),
                loop = true,
                cycleOffset = 0.25f,
            };

            List<Sprite> frames = CreateTrackedSpriteList(8);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            Assert.IsTrue(
                clip != null,
                "Created animation clip should not be null for full workflow test"
            );
            Assert.AreEqual(12f, clip.frameRate);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            Assert.IsTrue(settings.loopTime);
            Assert.AreEqual(0.25f, settings.cycleOffset, 0.0001f);

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length);

            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );
            Assert.AreEqual(8, keyframes.Length);

            float midInterval =
                keyframes[keyframes.Length / 2].time - keyframes[keyframes.Length / 2 - 1].time;
            float startInterval = keyframes[1].time - keyframes[0].time;
            Assert.Greater(
                startInterval,
                midInterval,
                "Start should be slower (lower FPS = longer interval)"
            );
        }

        [Test]
        public void CurvePresetsFlatCurveProducesConstantFps()
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Curve,
                framesPerSecond = 24f,
                framesPerSecondCurve = AnimationCurve.Constant(0f, 1f, 24f),
                frames = CreateSpriteList(5),
            };

            for (int i = 0; i < 5; i++)
            {
                float fps = AnimationCreatorWindow.GetCurrentFpsForTests(data, i);
                Assert.AreEqual(24f, fps, 0.01f, $"Frame {i} should have constant FPS");
            }
        }

        [Test]
        public void CurvePresetsEaseInCurveProducesIncreasingFps()
        {
            AnimationCurve easeIn = AnimationCurve.EaseInOut(0f, 6f, 1f, 18f);

            AnimationData data = new()
            {
                framerateMode = FramerateMode.Curve,
                framesPerSecondCurve = easeIn,
                frames = CreateSpriteList(5),
            };

            float previousFps = 0f;
            for (int i = 0; i < 5; i++)
            {
                float fps = AnimationCreatorWindow.GetCurrentFpsForTests(data, i);
                Assert.Greater(fps, previousFps, $"Frame {i} FPS should be greater than previous");
                previousFps = fps;
            }
        }

        [Test]
        public void CurvePresetsEaseOutCurveProducesDecreasingFps()
        {
            AnimationCurve easeOut = AnimationCurve.EaseInOut(0f, 18f, 1f, 6f);

            AnimationData data = new()
            {
                framerateMode = FramerateMode.Curve,
                framesPerSecondCurve = easeOut,
                frames = CreateSpriteList(5),
            };

            float previousFps = float.MaxValue;
            for (int i = 0; i < 5; i++)
            {
                float fps = AnimationCreatorWindow.GetCurrentFpsForTests(data, i);
                Assert.Less(fps, previousFps, $"Frame {i} FPS should be less than previous");
                previousFps = fps;
            }
        }

        /*
            SystemArrayPool returns power-of-two sized arrays and
            AnimationUtility.SetObjectReferenceCurve uses array.Length, so the trailing nulls
            corrupted the animation.
        */

        private static IEnumerable<TestCaseData> KeyframeCountVerificationCases()
        {
            yield return new TestCaseData(1).SetName("ArrayPoolFix.Keyframes.SingleFrame");
            yield return new TestCaseData(2).SetName("ArrayPoolFix.Keyframes.TwoFrames");
            yield return new TestCaseData(3).SetName("ArrayPoolFix.Keyframes.ThreeFrames");
            yield return new TestCaseData(4).SetName("ArrayPoolFix.Keyframes.FourFrames");
            yield return new TestCaseData(5).SetName("ArrayPoolFix.Keyframes.FiveFrames");
            yield return new TestCaseData(7).SetName("ArrayPoolFix.Keyframes.SevenFrames");
            yield return new TestCaseData(8).SetName("ArrayPoolFix.Keyframes.EightFrames");
            yield return new TestCaseData(9).SetName("ArrayPoolFix.Keyframes.NineFrames");
            yield return new TestCaseData(15).SetName("ArrayPoolFix.Keyframes.FifteenFrames");
            yield return new TestCaseData(16).SetName("ArrayPoolFix.Keyframes.SixteenFrames");
            yield return new TestCaseData(17).SetName("ArrayPoolFix.Keyframes.SeventeenFrames");
            yield return new TestCaseData(31).SetName("ArrayPoolFix.Keyframes.ThirtyOneFrames");
            yield return new TestCaseData(32).SetName("ArrayPoolFix.Keyframes.ThirtyTwoFrames");
            yield return new TestCaseData(33).SetName("ArrayPoolFix.Keyframes.ThirtyThreeFrames");
        }

        [TestCaseSource(nameof(KeyframeCountVerificationCases))]
        public void CreateAnimationClipHasExactKeyframeCountNoTrailingNulls(int frameCount)
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Constant,
                framesPerSecond = 12f,
                loop = false,
            };

            List<Sprite> frames = CreateTrackedSpriteList(frameCount);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length, "Should have exactly one object reference binding");

            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );

            Assert.AreEqual(
                frameCount,
                keyframes.Length,
                $"Keyframe array length should be exactly {frameCount}, not rounded up to power of 2"
            );
        }

        [TestCaseSource(nameof(KeyframeCountVerificationCases))]
        public void CreateAnimationClipHasNoNullSpriteReferences(int frameCount)
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Constant,
                framesPerSecond = 24f,
                loop = true,
            };

            List<Sprite> frames = CreateTrackedSpriteList(frameCount);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );

            for (int i = 0; i < keyframes.Length; i++)
            {
                Assert.IsTrue(
                    keyframes[i].value != null,
                    $"Keyframe {i} should have a non-null sprite reference"
                );
            }
        }

        private static IEnumerable<TestCaseData> AnimationDurationVerificationCases()
        {
            yield return new TestCaseData(12f, 1, 0f).SetName("ArrayPoolFix.Duration.12fps.1Frame");
            yield return new TestCaseData(12f, 2, 1f / 12f).SetName(
                "ArrayPoolFix.Duration.12fps.2Frames"
            );
            yield return new TestCaseData(12f, 5, 4f / 12f).SetName(
                "ArrayPoolFix.Duration.12fps.5Frames"
            );
            yield return new TestCaseData(24f, 4, 3f / 24f).SetName(
                "ArrayPoolFix.Duration.24fps.4Frames"
            );
            yield return new TestCaseData(30f, 6, 5f / 30f).SetName(
                "ArrayPoolFix.Duration.30fps.6Frames"
            );
            yield return new TestCaseData(60f, 10, 9f / 60f).SetName(
                "ArrayPoolFix.Duration.60fps.10Frames"
            );
            yield return new TestCaseData(12f, 17, 16f / 12f).SetName(
                "ArrayPoolFix.Duration.12fps.17Frames"
            );
            yield return new TestCaseData(12f, 33, 32f / 12f).SetName(
                "ArrayPoolFix.Duration.12fps.33Frames"
            );
        }

        [TestCaseSource(nameof(AnimationDurationVerificationCases))]
        public void CreateAnimationClipHasCorrectDurationBasedOnFrameCountAndFps(
            float fps,
            int frameCount,
            float expectedDuration
        )
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Constant,
                framesPerSecond = fps,
                loop = false,
            };

            List<Sprite> frames = CreateTrackedSpriteList(frameCount);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );

            float lastKeyframeTime = keyframes[keyframes.Length - 1].time;

            Assert.AreEqual(
                expectedDuration,
                lastKeyframeTime,
                0.0001f,
                $"Animation duration (last keyframe time) should be {expectedDuration}s for {frameCount} frames at {fps}fps"
            );
        }

        [Test]
        public void CreateAnimationClipFirstKeyframeIsAtTimeZero()
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Constant,
                framesPerSecond = 12f,
                loop = false,
            };

            List<Sprite> frames = CreateTrackedSpriteList(5);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );

            Assert.AreEqual(
                0f,
                keyframes[0].time,
                0.0001f,
                "First keyframe should always be at time 0"
            );
        }

        [Test]
        public void CreateAnimationClipAllKeyframesHaveValidSprites()
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Constant,
                framesPerSecond = 12f,
                loop = false,
            };

            int frameCount = 33;
            List<Sprite> frames = CreateTrackedSpriteList(frameCount);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );

            int nullCount = 0;
            for (int i = 0; i < keyframes.Length; i++)
            {
                if (keyframes[i].value == null)
                {
                    nullCount++;
                }
            }

            Assert.AreEqual(
                0,
                nullCount,
                $"Animation should have zero null keyframes, but found {nullCount} nulls in {keyframes.Length} keyframes"
            );
        }

        [Test]
        public void CreateAnimationClipKeyframeSpritesMatchInputOrder()
        {
            AnimationData data = new()
            {
                framerateMode = FramerateMode.Constant,
                framesPerSecond = 12f,
                loop = false,
            };

            int frameCount = 17;
            List<Sprite> frames = CreateTrackedSpriteList(frameCount);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );

            Assert.AreEqual(frameCount, keyframes.Length);

            for (int i = 0; i < frameCount; i++)
            {
                Assert.AreSame(
                    frames[i],
                    keyframes[i].value,
                    $"Keyframe {i} sprite should match input sprite at same index"
                );
            }
        }

        [Test]
        public void CreateAnimationClipCurveFramerateNoTrailingNullsForNonPowerOfTwoFrameCount()
        {
            AnimationCurve fpsCurve = AnimationCurve.Linear(0f, 10f, 1f, 30f);
            int frameCount = 7;

            AnimationData data = new()
            {
                framerateMode = FramerateMode.Curve,
                framesPerSecond = 12f,
                framesPerSecondCurve = fpsCurve,
                loop = true,
            };

            List<Sprite> frames = CreateTrackedSpriteList(frameCount);

            AnimationClip clip = Track(
                AnimationCreatorWindow.CreateAnimationClipForTests(data, frames)
            );

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );

            Assert.AreEqual(
                frameCount,
                keyframes.Length,
                "Curve framerate mode should also have exact keyframe count"
            );

            for (int i = 0; i < keyframes.Length; i++)
            {
                Assert.IsTrue(
                    keyframes[i].value != null,
                    $"Curve mode keyframe {i} should not be null"
                );
            }
        }

        // Helper Methods

        private List<Sprite> CreateSpriteList(int count)
        {
            List<Sprite> sprites = new(count);
            for (int i = 0; i < count; i++)
            {
                sprites.Add(null);
            }
            return sprites;
        }

        private List<Sprite> CreateTrackedSpriteList(int count)
        {
            List<Sprite> sprites = new(count);
            Texture2D texture = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false));

            for (int i = 0; i < count; i++)
            {
                Sprite sprite = Track(
                    Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f))
                );
                sprite.name = $"TestSprite_{i:D3}";
                sprites.Add(sprite);
            }

            return sprites;
        }
    }
#endif
}
