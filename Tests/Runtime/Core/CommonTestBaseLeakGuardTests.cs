// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// Guards the guard: proves the PlayMode scorched-earth leak net in
    /// <see cref="CommonTestBase"/> actually destroys an untracked GameObject a test leaks, and
    /// never touches an object that existed at the start-of-test baseline (no false positives).
    /// PlayMode only -- the net is a no-op in EditMode (synchronous destroy, no frame bleed).
    /// </summary>
    public sealed class CommonTestBaseLeakGuardTests : CommonTestBase
    {
        [UnityTest]
        public IEnumerator SweepDetectsAndDestroysUntrackedLeak()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("Scorched-earth leak guard is PlayMode-only.");
            }

            // A non-empty baseline proves CommonUnitySetUp actually captured the runner
            // infrastructure (guards against a silent capture regression that would make every
            // object look like a leak -- or nothing look like one).
            Assert.Greater(
                LeakGuardBaselineCountForTests,
                0,
                "PlayMode baseline must capture pre-existing roots"
            );

            // Created AFTER the baseline and deliberately NOT tracked -- exactly the cross-test leak
            // the net must catch.
            GameObject leak = new("UH_DeliberateLeakCanary");

            string diagnostic = RunLeakGuardSweepForTests();

            Assert.IsNotNull(diagnostic, "sweep should detect the untracked leak");
            StringAssert.Contains("UH_DeliberateLeakCanary", diagnostic);
            StringAssert.Contains("[uh-leak]", diagnostic);

            yield return null; // settle the deferred Object.Destroy
            Assert.IsTrue(leak == null, "swept leak should be destroyed");

            // Re-baseline so the real teardown sweep is a guaranteed no-op, independent of how many
            // frames the deferred destroy needed.
            CaptureLeakGuardBaselineForTests();
        }

        [UnityTest]
        public IEnumerator SweepSparesObjectsPresentAtBaseline()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("Scorched-earth leak guard is PlayMode-only.");
            }

            GameObject kept = Track(new GameObject("UH_BaselineSurvivor"));
            // Re-baseline so 'kept' counts as pre-existing -- the sweep must not touch it.
            CaptureLeakGuardBaselineForTests();

            string diagnostic = RunLeakGuardSweepForTests();

            Assert.IsNull(diagnostic, "objects present at the baseline must not be swept");
            Assert.IsFalse(kept == null, "baseline object must survive the sweep");
            yield return null;
        }
    }
}
