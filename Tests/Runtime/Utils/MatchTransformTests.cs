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
    public sealed class MatchTransformTests : CommonTestBase
    {
        // Tracking handled by CommonTestBase

        [UnityTest]
        public IEnumerator MatchesTransformInUpdate()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(5f, 10f, 0f) } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.mode = MatchTransformMode.Update;

            matcher.SendMessage("Awake");
            matcher.SendMessage("Update");
            yield return null;

            Assert.AreEqual(target.transform.position, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator MatchesTransformInFixedUpdate()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(5f, 10f, 0f) } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.mode = MatchTransformMode.FixedUpdate;

            matcher.SendMessage("Awake");
            matcher.SendMessage("FixedUpdate");
            yield return null;

            Assert.AreEqual(target.transform.position, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator MatchesTransformInLateUpdate()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(5f, 10f, 0f) } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.mode = MatchTransformMode.LateUpdate;

            matcher.SendMessage("Awake");
            matcher.SendMessage("LateUpdate");
            yield return null;

            Assert.AreEqual(target.transform.position, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator MatchesTransformOnAwake()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(5f, 10f, 0f) } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.mode = MatchTransformMode.Awake;

            matcher.SendMessage("Awake");
            yield return null;

            Assert.AreEqual(target.transform.position, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator MatchesTransformOnStart()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(5f, 10f, 0f) } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.mode = MatchTransformMode.Start;

            matcher.SendMessage("Awake");
            matcher.SendMessage("Start");
            yield return null;

            Assert.AreEqual(target.transform.position, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator AppliesLocalOffset()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(5f, 10f, 0f) } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.localOffset = new Vector3(2f, 3f, 1f);
            matcher.mode = MatchTransformMode.Update;

            matcher.SendMessage("Awake");
            matcher.SendMessage("Update");
            yield return null;

            Vector3 expected = target.transform.position + matcher.localOffset;
            Assert.AreEqual(expected, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator DoesNotMatchWithNullTarget()
        {
            GameObject follower = Track(
                new GameObject("Follower", typeof(MatchTransform))
                {
                    transform = { position = new Vector3(1f, 2f, 3f) },
                }
            );
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = null;
            matcher.mode = MatchTransformMode.Update;

            Vector3 initialPosition = follower.transform.position;

            matcher.SendMessage("Awake");
            matcher.SendMessage("Update");
            yield return null;

            Assert.AreEqual(initialPosition, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator UpdatesContinuouslyInUpdate()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = Vector3.zero } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.mode = MatchTransformMode.Update;

            matcher.SendMessage("Awake");

            target.transform.position = new Vector3(5f, 0f, 0f);
            matcher.SendMessage("Update");
            yield return null;
            Assert.AreEqual(target.transform.position, follower.transform.position);

            target.transform.position = new Vector3(10f, 5f, 0f);
            matcher.SendMessage("Update");
            yield return null;
            Assert.AreEqual(target.transform.position, follower.transform.position);

            target.transform.position = new Vector3(-3f, 7f, 2f);
            matcher.SendMessage("Update");
            yield return null;
            Assert.AreEqual(target.transform.position, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator SupportsCombinedModes()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(5f, 10f, 0f) } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.mode = MatchTransformMode.Update | MatchTransformMode.FixedUpdate;

            matcher.SendMessage("Awake");

            matcher.SendMessage("Update");
            yield return null;
            Assert.AreEqual(target.transform.position, follower.transform.position);

            target.transform.position = new Vector3(15f, 20f, 5f);
            matcher.SendMessage("FixedUpdate");
            yield return null;
            Assert.AreEqual(target.transform.position, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator DoesNotUpdateInWrongMode()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(5f, 10f, 0f) } }
            );

            GameObject follower = Track(
                new GameObject("Follower", typeof(MatchTransform))
                {
                    transform = { position = Vector3.zero },
                }
            );
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.mode = MatchTransformMode.FixedUpdate;

            matcher.SendMessage("Awake");
            matcher.SendMessage("Update");

            // Do not yield a frame here; yielding would allow FixedUpdate to run and update the position.
            Assert.AreNotEqual(target.transform.position, follower.transform.position);
            yield break;
        }

        [UnityTest]
        public IEnumerator AssignsTransformComponent()
        {
            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();

            matcher.SendMessage("Awake");
            yield return null;

            Assert.IsTrue(matcher._transform != null);
            Assert.AreEqual(follower.transform, matcher._transform);
        }

        [UnityTest]
        public IEnumerator MatchesNegativePositions()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(-10f, -20f, -5f) } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.mode = MatchTransformMode.Update;

            matcher.SendMessage("Awake");
            matcher.SendMessage("Update");
            yield return null;

            Assert.AreEqual(target.transform.position, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator MatchesZeroPosition()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = Vector3.zero } }
            );

            GameObject follower = Track(
                new GameObject("Follower", typeof(MatchTransform))
                {
                    transform = { position = new Vector3(10f, 10f, 10f) },
                }
            );
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.mode = MatchTransformMode.Update;

            matcher.SendMessage("Awake");
            matcher.SendMessage("Update");
            yield return null;

            Assert.AreEqual(Vector3.zero, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator OffsetWorksWithNegativeValues()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(5f, 5f, 0f) } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.localOffset = new Vector3(-2f, -3f, -1f);
            matcher.mode = MatchTransformMode.Update;

            matcher.SendMessage("Awake");
            matcher.SendMessage("Update");
            yield return null;

            Vector3 expected = new(3f, 2f, -1f);
            Assert.AreEqual(expected, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator OffsetWorksWithZero()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(5f, 10f, 0f) } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.localOffset = Vector3.zero;
            matcher.mode = MatchTransformMode.Update;

            matcher.SendMessage("Awake");
            matcher.SendMessage("Update");
            yield return null;

            Assert.AreEqual(target.transform.position, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator MultipleMatchersCanFollowSameTarget()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(10f, 20f, 0f) } }
            );

            GameObject follower1 = Track(new GameObject("Follower1", typeof(MatchTransform)));
            MatchTransform matcher1 = follower1.GetComponent<MatchTransform>();
            matcher1.toMatch = target.transform;
            matcher1.mode = MatchTransformMode.Update;

            GameObject follower2 = Track(new GameObject("Follower2", typeof(MatchTransform)));
            MatchTransform matcher2 = follower2.GetComponent<MatchTransform>();
            matcher2.toMatch = target.transform;
            matcher2.mode = MatchTransformMode.Update;

            matcher1.SendMessage("Awake");
            matcher2.SendMessage("Awake");
            matcher1.SendMessage("Update");
            matcher2.SendMessage("Update");
            yield return null;

            Assert.AreEqual(target.transform.position, follower1.transform.position);
            Assert.AreEqual(target.transform.position, follower2.transform.position);
        }

        [UnityTest]
        public IEnumerator ChangingTargetDynamically()
        {
            GameObject target1 = Track(
                new GameObject("Target1") { transform = { position = new Vector3(5f, 5f, 0f) } }
            );

            GameObject target2 = Track(
                new GameObject("Target2") { transform = { position = new Vector3(10f, 10f, 0f) } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target1.transform;
            matcher.mode = MatchTransformMode.Update;

            matcher.SendMessage("Awake");
            matcher.SendMessage("Update");
            yield return null;

            Assert.AreEqual(target1.transform.position, follower.transform.position);

            matcher.toMatch = target2.transform;
            matcher.SendMessage("Update");
            yield return null;

            Assert.AreEqual(target2.transform.position, follower.transform.position);
        }

        [UnityTest]
        public IEnumerator AllModesWorkIndependently()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(1f, 2f, 3f) } }
            );

            GameObject awakeFollower = Track(
                new GameObject("AwakeFollower", typeof(MatchTransform))
            );
            MatchTransform awakeMatcher = awakeFollower.GetComponent<MatchTransform>();
            awakeMatcher.toMatch = target.transform;
            awakeMatcher.mode = MatchTransformMode.Awake;

            GameObject startFollower = Track(
                new GameObject("StartFollower", typeof(MatchTransform))
            );
            MatchTransform startMatcher = startFollower.GetComponent<MatchTransform>();
            startMatcher.toMatch = target.transform;
            startMatcher.mode = MatchTransformMode.Start;

            GameObject updateFollower = Track(
                new GameObject("UpdateFollower", typeof(MatchTransform))
            );
            MatchTransform updateMatcher = updateFollower.GetComponent<MatchTransform>();
            updateMatcher.toMatch = target.transform;
            updateMatcher.mode = MatchTransformMode.Update;

            GameObject fixedUpdateFollower = Track(
                new GameObject("FixedUpdateFollower", typeof(MatchTransform))
            );
            MatchTransform fixedUpdateMatcher = fixedUpdateFollower.GetComponent<MatchTransform>();
            fixedUpdateMatcher.toMatch = target.transform;
            fixedUpdateMatcher.mode = MatchTransformMode.FixedUpdate;

            GameObject lateUpdateFollower = Track(
                new GameObject("LateUpdateFollower", typeof(MatchTransform))
            );
            MatchTransform lateUpdateMatcher = lateUpdateFollower.GetComponent<MatchTransform>();
            lateUpdateMatcher.toMatch = target.transform;
            lateUpdateMatcher.mode = MatchTransformMode.LateUpdate;

            awakeMatcher.SendMessage("Awake");
            Assert.AreEqual(target.transform.position, awakeFollower.transform.position);

            startMatcher.SendMessage("Awake");
            startMatcher.SendMessage("Start");
            Assert.AreEqual(target.transform.position, startFollower.transform.position);

            updateMatcher.SendMessage("Awake");
            updateMatcher.SendMessage("Update");
            Assert.AreEqual(target.transform.position, updateFollower.transform.position);

            fixedUpdateMatcher.SendMessage("Awake");
            fixedUpdateMatcher.SendMessage("FixedUpdate");
            Assert.AreEqual(target.transform.position, fixedUpdateFollower.transform.position);

            lateUpdateMatcher.SendMessage("Awake");
            lateUpdateMatcher.SendMessage("LateUpdate");
            Assert.AreEqual(target.transform.position, lateUpdateFollower.transform.position);

            yield return null;
        }

        [UnityTest]
        public IEnumerator ChangingOffsetDynamically()
        {
            GameObject target = Track(
                new GameObject("Target") { transform = { position = new Vector3(5f, 10f, 0f) } }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.localOffset = new Vector3(1f, 1f, 0f);
            matcher.mode = MatchTransformMode.Update;

            matcher.SendMessage("Awake");
            matcher.SendMessage("Update");
            yield return null;

            Assert.AreEqual(new Vector3(6f, 11f, 0f), follower.transform.position);

            matcher.localOffset = new Vector3(2f, 2f, 0f);
            matcher.SendMessage("Update");
            yield return null;

            Assert.AreEqual(new Vector3(7f, 12f, 0f), follower.transform.position);
        }

        [UnityTest]
        public IEnumerator WorksWithLargePositions()
        {
            GameObject target = Track(
                new GameObject("Target")
                {
                    transform = { position = new Vector3(10000f, 20000f, 5000f) },
                }
            );

            GameObject follower = Track(new GameObject("Follower", typeof(MatchTransform)));
            MatchTransform matcher = follower.GetComponent<MatchTransform>();
            matcher.toMatch = target.transform;
            matcher.mode = MatchTransformMode.Update;

            matcher.SendMessage("Awake");
            matcher.SendMessage("Update");
            yield return null;

            Assert.AreEqual(target.transform.position, follower.transform.position);
        }
    }
}
