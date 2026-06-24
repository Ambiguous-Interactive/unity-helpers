// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class StartTrackerTests : CommonTestBase
    {
        /// <summary>
        /// Verifies that Started becomes true after the one-frame delay.
        /// The delay ensures all Start() methods on the GameObject have been called.
        /// </summary>
        [UnityTest]
        public IEnumerator StartedBecomesTrueAfterStartDelay()
        {
            GameObject go = Track(new GameObject("Tracker", typeof(StartTracker)));
            StartTracker tracker = go.GetComponent<StartTracker>();

            // Initially false - Start() coroutine hasn't completed yet
            Assert.IsFalse(
                tracker.Started,
                "Started should be false immediately after creation (before Start() completes)"
            );

            // Frame 0 -> Frame 1: Start() coroutine executes yield return null
            yield return null;

            // Still false - the coroutine yielded but hasn't resumed yet
            Assert.IsFalse(
                tracker.Started,
                $"Started should still be false after first frame (coroutine yielded but not resumed). Frame: {Time.frameCount}"
            );

            // Frame 1 -> Frame 2: Coroutine resumes and sets Started = true
            yield return null;

            Assert.IsTrue(
                tracker.Started,
                $"Started should be true after second frame (coroutine resumed). Frame: {Time.frameCount}"
            );
        }

        /// <summary>
        /// Verifies that Started remains false before the delay completes.
        /// This tests the intentional one-frame delay that ensures all Start() methods are called.
        /// </summary>
        [UnityTest]
        public IEnumerator StartedIsFalseImmediatelyAfterCreation()
        {
            GameObject go = Track(new GameObject("Tracker", typeof(StartTracker)));
            StartTracker tracker = go.GetComponent<StartTracker>();

            Assert.IsFalse(
                tracker.Started,
                "Started should be false immediately after GameObject creation"
            );

            yield return null;
        }

        /// <summary>
        /// Verifies that multiple StartTrackers on different GameObjects work independently.
        /// </summary>
        [UnityTest]
        public IEnumerator MultipleTrackersWorkIndependently()
        {
            GameObject go1 = Track(new GameObject("Tracker1", typeof(StartTracker)));
            StartTracker tracker1 = go1.GetComponent<StartTracker>();

            // Wait one frame, then create second tracker
            yield return null;

            GameObject go2 = Track(new GameObject("Tracker2", typeof(StartTracker)));
            StartTracker tracker2 = go2.GetComponent<StartTracker>();

            // After another frame, tracker1 should be true, tracker2 should be false
            yield return null;

            Assert.IsTrue(
                tracker1.Started,
                $"Tracker1 should be Started after 2 frames. Frame: {Time.frameCount}"
            );
            Assert.IsFalse(
                tracker2.Started,
                $"Tracker2 should not be Started yet (only 1 frame). Frame: {Time.frameCount}"
            );

            // After another frame, both should be true
            yield return null;

            Assert.IsTrue(
                tracker1.Started,
                $"Tracker1 should still be Started. Frame: {Time.frameCount}"
            );
            Assert.IsTrue(
                tracker2.Started,
                $"Tracker2 should now be Started after 2 frames. Frame: {Time.frameCount}"
            );
        }

        /// <summary>
        /// Verifies behavior when the tracker is disabled at creation.
        /// </summary>
        [UnityTest]
        public IEnumerator DisabledTrackerDoesNotStart()
        {
            GameObject go = Track(new GameObject("Tracker"));
            go.SetActive(false);
            StartTracker tracker = go.AddComponent<StartTracker>();

            yield return null;
            yield return null;

            Assert.IsFalse(tracker.Started, "Disabled tracker should not have Started = true");

            // Enable and verify it starts properly
            go.SetActive(true);

            yield return null;
            yield return null;

            Assert.IsTrue(
                tracker.Started,
                "Tracker should be Started after being enabled and waiting 2 frames"
            );
        }

        /// <summary>
        /// Verifies that only one StartTracker can exist per GameObject (DisallowMultipleComponent).
        /// Tests the actual behavior rather than Unity's log output.
        /// </summary>
        [Test]
        public void DisallowsMultipleComponents()
        {
            GameObject go = Track(new GameObject("Tracker", typeof(StartTracker)));
            StartTracker first = go.GetComponent<StartTracker>();

            Assert.IsTrue(first != null, "First StartTracker should exist");
            Assert.AreEqual(
                1,
                go.GetComponents<StartTracker>().Length,
                "Should have exactly one StartTracker before attempting to add another"
            );

            // The engine's "Can't add '<T>' ... already added" [Error] for a [DisallowMultipleComponent]
            // violation is emitted on Unity 2022.1+ but NOT on 2021.3 (the addition is rejected
            // silently there). Only register the expectation where the engine actually logs it; the
            // behavioral assertions below verify the rejection on every version.
#if UNITY_2022_1_OR_NEWER
            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "^Can't add 'StartTracker' to Tracker because a 'StartTracker' is already added to the game object!$"
                )
            );
#endif
            StartTracker second = go.AddComponent<StartTracker>();

            // Verify the actual behavior: second component was not added
            Assert.IsTrue(
                second == null,
                "AddComponent should return null when DisallowMultipleComponent prevents addition"
            );
            Assert.AreEqual(
                1,
                go.GetComponents<StartTracker>().Length,
                "Should still have exactly one StartTracker after attempting to add another"
            );

            // Verify the original component is still intact
            StartTracker remaining = go.GetComponent<StartTracker>();
            Assert.AreSame(
                first,
                remaining,
                "The original StartTracker instance should remain unchanged"
            );
        }

        /// <summary>
        /// Verifies that destroying and recreating the tracker works correctly.
        /// </summary>
        [UnityTest]
        public IEnumerator DestroyAndRecreateTracker()
        {
            GameObject go = Track(new GameObject("Tracker", typeof(StartTracker)));
            StartTracker tracker = go.GetComponent<StartTracker>();

            // Wait for Started to become true
            yield return null;
            yield return null;

            Assert.IsTrue(tracker.Started, "Initial tracker should be Started");

            // Destroy the component
            Object.DestroyImmediate(tracker); // UNH-SUPPRESS: Test verifies new tracker state after component destruction
            yield return null;

            // Add a new tracker
            StartTracker newTracker = go.AddComponent<StartTracker>();
            Assert.IsFalse(newTracker.Started, "New tracker should start with Started = false");

            yield return null;
            yield return null;

            Assert.IsTrue(newTracker.Started, "New tracker should be Started after 2 frames");
        }

        /// <summary>
        /// Verifies Started is false immediately after creation (0 frames waited).
        /// </summary>
        [UnityTest]
        public IEnumerator StartedStateAtFrameZeroIsFalse()
        {
            GameObject go = Track(new GameObject("Tracker", typeof(StartTracker)));
            StartTracker tracker = go.GetComponent<StartTracker>();

            Assert.IsFalse(
                tracker.Started,
                $"After 0 frame(s), Started should be false. Actual frame count: {Time.frameCount}"
            );
            yield break;
        }

        /// <summary>
        /// Verifies Started is false after 1 frame waited.
        /// </summary>
        [UnityTest]
        public IEnumerator StartedStateAtFrameOneIsFalse()
        {
            GameObject go = Track(new GameObject("Tracker", typeof(StartTracker)));
            StartTracker tracker = go.GetComponent<StartTracker>();

            yield return null;

            Assert.IsFalse(
                tracker.Started,
                $"After 1 frame(s), Started should be false. Actual frame count: {Time.frameCount}"
            );
        }

        /// <summary>
        /// Verifies Started is true after 2 frames waited.
        /// </summary>
        [UnityTest]
        public IEnumerator StartedStateAtFrameTwoIsTrue()
        {
            GameObject go = Track(new GameObject("Tracker", typeof(StartTracker)));
            StartTracker tracker = go.GetComponent<StartTracker>();

            yield return null;
            yield return null;

            Assert.IsTrue(
                tracker.Started,
                $"After 2 frame(s), Started should be true. Actual frame count: {Time.frameCount}"
            );
        }

        /// <summary>
        /// Verifies Started is true after 3 frames waited.
        /// </summary>
        [UnityTest]
        public IEnumerator StartedStateAtFrameThreeIsTrue()
        {
            GameObject go = Track(new GameObject("Tracker", typeof(StartTracker)));
            StartTracker tracker = go.GetComponent<StartTracker>();

            yield return null;
            yield return null;
            yield return null;

            Assert.IsTrue(
                tracker.Started,
                $"After 3 frame(s), Started should be true. Actual frame count: {Time.frameCount}"
            );
        }
    }
}
