// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Tests for the child height change signaling mechanism used to fix
    /// nested foldout overlap issues in SerializableDictionary and SerializableSet.
    /// </summary>
    /// <remarks>
    /// The SignalChildHeightChanged() methods set a static frame counter that causes
    /// the row render cache to be invalidated when a child property drawer's foldout
    /// state changes (e.g., "Collection Styling (Advanced)" foldout in nested properties).
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableCollectionChildHeightSignalTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            SerializableDictionaryPropertyDrawerTestAccess.ResetChildHeightChangedFrame();
            SerializableSetPropertyDrawerTestAccess.ResetChildHeightChangedFrame();
        }

        [TearDown]
        public override void TearDown()
        {
            SerializableDictionaryPropertyDrawerTestAccess.ResetChildHeightChangedFrame();
            SerializableSetPropertyDrawerTestAccess.ResetChildHeightChangedFrame();
            base.TearDown();
        }

        [Test]
        public void DictionarySignalChildHeightChangedMethodExists()
        {
            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();

            int frameValue =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            Assert.GreaterOrEqual(
                frameValue,
                0,
                "SignalChildHeightChanged should set frame to a valid value."
            );
        }

        [Test]
        public void SetSignalChildHeightChangedMethodExists()
        {
            SerializableSetPropertyDrawer.SignalChildHeightChanged();

            int frameValue = SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            Assert.GreaterOrEqual(
                frameValue,
                0,
                "SignalChildHeightChanged should set frame to a valid value."
            );
        }

        [Test]
        public void DictionarySignalSetsFrameCounterToCurrentFrame()
        {
            SerializableDictionaryPropertyDrawerTestAccess.ResetChildHeightChangedFrame();
            int expectedFrame = Time.frameCount;

            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();

            int actualFrame =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            Assert.AreEqual(
                expectedFrame,
                actualFrame,
                "Signal should set frame counter to current frame."
            );
        }

        [Test]
        public void SetSignalSetsFrameCounterToCurrentFrame()
        {
            SerializableSetPropertyDrawerTestAccess.ResetChildHeightChangedFrame();
            int expectedFrame = Time.frameCount;

            SerializableSetPropertyDrawer.SignalChildHeightChanged();

            int actualFrame = SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            Assert.AreEqual(
                expectedFrame,
                actualFrame,
                "Signal should set frame counter to current frame."
            );
        }

        [Test]
        public void DictionaryResetClearsFrameCounter()
        {
            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();

            SerializableDictionaryPropertyDrawerTestAccess.ResetChildHeightChangedFrame();

            int frameValue =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            Assert.AreEqual(-1, frameValue, "Reset should set frame counter to -1.");
        }

        [Test]
        public void SetResetClearsFrameCounter()
        {
            SerializableSetPropertyDrawer.SignalChildHeightChanged();

            SerializableSetPropertyDrawerTestAccess.ResetChildHeightChangedFrame();

            int frameValue = SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            Assert.AreEqual(-1, frameValue, "Reset should set frame counter to -1.");
        }

        [Test]
        public void DictionaryMultipleSignalsInSameFrameDoNotCauseIssues()
        {
            int expectedFrame = Time.frameCount;

            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();
            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();
            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();

            int actualFrame =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            Assert.AreEqual(
                expectedFrame,
                actualFrame,
                "Multiple signals in same frame should result in same frame value."
            );
        }

        [Test]
        public void SetMultipleSignalsInSameFrameDoNotCauseIssues()
        {
            int expectedFrame = Time.frameCount;

            SerializableSetPropertyDrawer.SignalChildHeightChanged();
            SerializableSetPropertyDrawer.SignalChildHeightChanged();
            SerializableSetPropertyDrawer.SignalChildHeightChanged();

            int actualFrame = SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            Assert.AreEqual(
                expectedFrame,
                actualFrame,
                "Multiple signals in same frame should result in same frame value."
            );
        }

        [Test]
        public void DictionaryFrameCounterInitializesToNegativeOne()
        {
            SerializableDictionaryPropertyDrawerTestAccess.ResetChildHeightChangedFrame();

            int frameValue =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();

            Assert.AreEqual(-1, frameValue, "Frame counter should initialize to -1.");
        }

        [Test]
        public void SetFrameCounterInitializesToNegativeOne()
        {
            SerializableSetPropertyDrawerTestAccess.ResetChildHeightChangedFrame();

            int frameValue = SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();

            Assert.AreEqual(-1, frameValue, "Frame counter should initialize to -1.");
        }

        [Test]
        public void DictionarySignalIsIdempotentWithinSameFrame()
        {
            SerializableDictionaryPropertyDrawerTestAccess.ResetChildHeightChangedFrame();

            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();
            int firstCallFrame =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();

            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();
            int secondCallFrame =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();

            Assert.AreEqual(
                firstCallFrame,
                secondCallFrame,
                "Signal should be idempotent within the same frame."
            );
        }

        [Test]
        public void SetSignalIsIdempotentWithinSameFrame()
        {
            SerializableSetPropertyDrawerTestAccess.ResetChildHeightChangedFrame();

            SerializableSetPropertyDrawer.SignalChildHeightChanged();
            int firstCallFrame =
                SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();

            SerializableSetPropertyDrawer.SignalChildHeightChanged();
            int secondCallFrame =
                SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();

            Assert.AreEqual(
                firstCallFrame,
                secondCallFrame,
                "Signal should be idempotent within the same frame."
            );
        }

        [Test]
        public void DictionarySignalValueIsNonNegativeAfterSignaling()
        {
            SerializableDictionaryPropertyDrawerTestAccess.ResetChildHeightChangedFrame();

            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();

            int frameValue =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            Assert.GreaterOrEqual(
                frameValue,
                0,
                "Frame value should be non-negative after signaling."
            );
        }

        [Test]
        public void SetSignalValueIsNonNegativeAfterSignaling()
        {
            SerializableSetPropertyDrawerTestAccess.ResetChildHeightChangedFrame();

            SerializableSetPropertyDrawer.SignalChildHeightChanged();

            int frameValue = SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            Assert.GreaterOrEqual(
                frameValue,
                0,
                "Frame value should be non-negative after signaling."
            );
        }

        [Test]
        public void BothDrawersCanBeSignaledIndependently()
        {
            SerializableDictionaryPropertyDrawerTestAccess.ResetChildHeightChangedFrame();
            SerializableSetPropertyDrawerTestAccess.ResetChildHeightChangedFrame();

            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();

            int dictionaryFrame =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            int setFrame = SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();

            Assert.AreNotEqual(
                dictionaryFrame,
                setFrame,
                "Dictionary and Set drawers should have independent frame counters."
            );
            Assert.GreaterOrEqual(dictionaryFrame, 0, "Dictionary frame should be set.");
            Assert.AreEqual(-1, setFrame, "Set frame should remain unset.");
        }

        [Test]
        public void SignalingBothDrawersUpdatesFrameIndependently()
        {
            SerializableDictionaryPropertyDrawerTestAccess.ResetChildHeightChangedFrame();
            SerializableSetPropertyDrawerTestAccess.ResetChildHeightChangedFrame();

            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();
            SerializableSetPropertyDrawer.SignalChildHeightChanged();

            int dictionaryFrame =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            int setFrame = SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();

            Assert.AreEqual(
                dictionaryFrame,
                setFrame,
                "Both drawers should have same frame when signaled in same frame."
            );
            Assert.AreEqual(
                Time.frameCount,
                dictionaryFrame,
                "Dictionary frame should match current frame."
            );
            Assert.AreEqual(Time.frameCount, setFrame, "Set frame should match current frame.");
        }

        [Test]
        public void DictionaryResetDoesNotAffectSetDrawer()
        {
            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();
            SerializableSetPropertyDrawer.SignalChildHeightChanged();
            int setFrameBeforeReset =
                SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();

            SerializableDictionaryPropertyDrawerTestAccess.ResetChildHeightChangedFrame();

            int setFrameAfterReset =
                SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            Assert.AreEqual(
                setFrameBeforeReset,
                setFrameAfterReset,
                "Resetting Dictionary should not affect Set drawer."
            );
        }

        [Test]
        public void SetResetDoesNotAffectDictionaryDrawer()
        {
            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();
            SerializableSetPropertyDrawer.SignalChildHeightChanged();
            int dictionaryFrameBeforeReset =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();

            SerializableSetPropertyDrawerTestAccess.ResetChildHeightChangedFrame();

            int dictionaryFrameAfterReset =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            Assert.AreEqual(
                dictionaryFrameBeforeReset,
                dictionaryFrameAfterReset,
                "Resetting Set should not affect Dictionary drawer."
            );
        }

        [Test]
        public void DictionarySignalMatchesCurrentTimeFrameCount()
        {
            SerializableDictionaryPropertyDrawerTestAccess.ResetChildHeightChangedFrame();
            int frameBefore = Time.frameCount;

            SerializableDictionaryPropertyDrawer.SignalChildHeightChanged();

            int signalFrame =
                SerializableDictionaryPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            int frameAfter = Time.frameCount;

            Assert.GreaterOrEqual(
                signalFrame,
                frameBefore,
                "Signal frame should be at least the frame before signaling."
            );
            Assert.LessOrEqual(
                signalFrame,
                frameAfter,
                "Signal frame should be at most the frame after signaling."
            );
        }

        [Test]
        public void SetSignalMatchesCurrentTimeFrameCount()
        {
            SerializableSetPropertyDrawerTestAccess.ResetChildHeightChangedFrame();
            int frameBefore = Time.frameCount;

            SerializableSetPropertyDrawer.SignalChildHeightChanged();

            int signalFrame = SerializableSetPropertyDrawerTestAccess.GetChildHeightChangedFrame();
            int frameAfter = Time.frameCount;

            Assert.GreaterOrEqual(
                signalFrame,
                frameBefore,
                "Signal frame should be at least the frame before signaling."
            );
            Assert.LessOrEqual(
                signalFrame,
                frameAfter,
                "Signal frame should be at most the frame after signaling."
            );
        }
    }
}
