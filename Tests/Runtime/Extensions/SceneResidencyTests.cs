// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.TestTools.Constraints;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Is = UnityEngine.TestTools.Constraints.Is;

    /// <summary>
    /// Covers <see cref="UnityExtensions.IsDontDestroyOnLoad"/>, whose signature reads as a free
    /// predicate and therefore has to be one.
    /// </summary>
    /// <remarks>
    /// Reading <c>Scene.name</c> marshals a fresh managed string out of native memory on every
    /// call. A consumer profile attributed 112 of the 176 bytes per frame their
    /// <c>BehaviourUpdate</c> allocated to two call sites of this method, and Unity's collector is
    /// non-generational and non-compacting, so a steady drip is the shape that actually hurts
    /// (#549). The answer is cached against the scene's handle instead.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SceneResidencyTests : CommonTestBase
    {
        /// <summary>
        /// The handle cache answers for every scene, not just the first one it saw.
        /// </summary>
        /// <remarks>
        /// Caching against a scene handle is only sound if a handle identifies a scene, so the
        /// interesting case is an object that moves: it leaves the scene whose handle was cached as
        /// "not DontDestroyOnLoad" and arrives in one whose handle answers true, and neither cache
        /// may disagree with the name it was derived from.
        /// </remarks>
        [UnityTest]
        public IEnumerator IsDontDestroyOnLoadAgreesWithTheSceneNameAcrossAMove()
        {
            GameObject resident = Track(new GameObject(nameof(resident)));
            GameObject migrating = Track(new GameObject(nameof(migrating)));

            for (int i = 0; i < 3; ++i)
            {
                Assert.IsFalse(resident.IsDontDestroyOnLoad());
                Assert.IsFalse(migrating.IsDontDestroyOnLoad());
            }

            UnityEngine.Object.DontDestroyOnLoad(migrating);

            for (int i = 0; i < 3; ++i)
            {
                Assert.IsTrue(migrating.IsDontDestroyOnLoad());
                Assert.IsFalse(
                    resident.IsDontDestroyOnLoad(),
                    "learning the DontDestroyOnLoad handle must not change the answer for anything else"
                );
            }

            GameObject arrivedLater = Track(new GameObject(nameof(arrivedLater)));
            Assert.IsFalse(arrivedLater.IsDontDestroyOnLoad());

            Assert.IsFalse(
                ((GameObject)null).IsDontDestroyOnLoad(),
                "a destroyed or absent GameObject resides nowhere"
            );

            yield return null;
        }

        /// <summary>
        /// Somewhere the recorder can see it, the predicate allocates nothing.
        /// </summary>
        /// <remarks>
        /// The control runs FIRST and decides whether this platform can be measured at all. On an
        /// IL2CPP standalone player the sink below allocates nothing the recorder reports, so the
        /// control does not move -- and a "did not allocate" verdict from an instrument that cannot
        /// see allocation is not a pass, it is the absence of a measurement. This skips there
        /// rather than claiming a result, and asserts for real on the Mono players and the editor.
        /// </remarks>
        [UnityTest]
        public IEnumerator IsDontDestroyOnLoadAllocatesNothingOnceTheSceneIsKnown()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("GC allocation recording is only meaningful in play mode");
            }

            GameObject resident = Track(new GameObject(nameof(resident)));
            GameObject persistent = Track(new GameObject(nameof(persistent)));
            UnityEngine.Object.DontDestroyOnLoad(persistent);

            // Warm both caches: the one call per scene that still reads the name.
            Assert.IsFalse(resident.IsDontDestroyOnLoad());
            Assert.IsTrue(persistent.IsDontDestroyOnLoad());

            yield return null;

            if (!RecorderCanSeeAnAllocation())
            {
                Assert.Ignore(
                    "GC allocation recording is inert on this player, so a 'did not allocate' "
                        + "verdict would prove nothing"
                );
            }

            Assert.That(
                () =>
                {
                    for (int i = 0; i < AllocationProbeIterations; ++i)
                    {
                        if (resident.IsDontDestroyOnLoad() || !persistent.IsDontDestroyOnLoad())
                        {
                            throw new InvalidOperationException("the probe answered incorrectly");
                        }
                    }
                },
                Is.Not.AllocatingGCMemory(),
                "the answer comes from a cached scene, so no managed string is marshalled"
            );

            yield return null;
        }

        private const int AllocationProbeIterations = 256;

        /// <summary>
        /// Sink for the control's allocations, so nothing can prove them dead and remove them.
        /// </summary>
        private static string _allocationSink;

        /// <summary>
        /// Whether an allocation this test can definitely cause is one the recorder reports.
        /// </summary>
        private static bool RecorderCanSeeAnAllocation()
        {
            try
            {
                Assert.That(
                    () =>
                    {
                        for (int i = 0; i < AllocationProbeIterations; ++i)
                        {
                            // Length varies and the result escapes to a static, so this cannot be
                            // constant-folded or elided.
                            _allocationSink = new string('x', 8 + (i & 7));
                        }
                    },
                    Is.AllocatingGCMemory()
                );
                return true;
            }
            catch (AssertionException)
            {
                return false;
            }
        }
    }
}
