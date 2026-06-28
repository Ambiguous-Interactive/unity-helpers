// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CoroutineHandlerTests : CommonTestBase
    {
        // Tracking handled by CommonTestBase

        [UnityTest]
        public IEnumerator CreatesInstanceOnFirstAccess()
        {
            Assert.IsFalse(CoroutineHandler.HasInstance);

            CoroutineHandler instance = CoroutineHandler.Instance;
            Track(instance.gameObject);

            Assert.IsTrue(CoroutineHandler.HasInstance);
            Assert.IsTrue(instance != null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReturnsSameInstanceOnMultipleAccesses()
        {
            CoroutineHandler instance1 = CoroutineHandler.Instance;
            Track(instance1.gameObject);
            CoroutineHandler instance2 = CoroutineHandler.Instance;

            Assert.AreSame(instance1, instance2);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CanStartCoroutine()
        {
            bool coroutineRan = false;

            CoroutineHandler inst = CoroutineHandler.Instance;
            Track(inst.gameObject);
            inst.StartCoroutine(TestCoroutine());

            yield return WaitUntil(() => coroutineRan, nameof(coroutineRan));

            Assert.IsTrue(coroutineRan);
            yield break;

            IEnumerator TestCoroutine()
            {
                yield return null;
                coroutineRan = true;
            }
        }

        [UnityTest]
        public IEnumerator CanStopCoroutine()
        {
            int counter = 0;

            CoroutineHandler inst2 = CoroutineHandler.Instance;
            Track(inst2.gameObject);
            Coroutine coroutine = inst2.StartCoroutine(TestCoroutine());

            yield return WaitUntil(() => counter >= 2, nameof(counter));

            int countBeforeStop = counter;
            CoroutineHandler.Instance.StopCoroutine(coroutine);

            yield return null;
            yield return null;

            Assert.AreEqual(countBeforeStop, counter);
            yield break;

            IEnumerator TestCoroutine()
            {
                while (true)
                {
                    counter++;
                    yield return null;
                }
            }
        }

        [UnityTest]
        public IEnumerator CanStopAllCoroutines()
        {
            int counter1 = 0;
            int counter2 = 0;

            CoroutineHandler inst3 = CoroutineHandler.Instance;
            Track(inst3.gameObject);
            inst3.StartCoroutine(TestCoroutine1());
            inst3.StartCoroutine(TestCoroutine2());

            yield return WaitUntil(() => counter1 > 0 && counter2 > 0, "both counters");

            int count1BeforeStop = counter1;
            int count2BeforeStop = counter2;

            inst3.StopAllCoroutines();

            yield return null;
            yield return null;

            Assert.AreEqual(count1BeforeStop, counter1);
            Assert.AreEqual(count2BeforeStop, counter2);
            yield break;

            IEnumerator TestCoroutine1()
            {
                while (true)
                {
                    counter1++;
                    yield return null;
                }
            }

            IEnumerator TestCoroutine2()
            {
                while (true)
                {
                    counter2++;
                    yield return null;
                }
            }
        }

        [UnityTest]
        public IEnumerator CoroutineRunsOverMultipleFrames()
        {
            int frameCount = 0;

            CoroutineHandler inst4 = CoroutineHandler.Instance;
            Track(inst4.gameObject);
            inst4.StartCoroutine(TestCoroutine());

            yield return WaitUntil(() => frameCount == 5, nameof(frameCount), maxFrames: 12);

            Assert.AreEqual(5, frameCount);
            yield break;

            IEnumerator TestCoroutine()
            {
                for (int i = 0; i < 5; i++)
                {
                    frameCount++;
                    yield return null;
                }
            }
        }

        [UnityTest]
        public IEnumerator CoroutineCanWaitForSeconds()
        {
            bool completed = false;
            const float WaitSeconds = 0.01f;
            float startTime = Time.time;

            CoroutineHandler inst = CoroutineHandler.Instance;
            Track(inst.gameObject);
            inst.StartCoroutine(TestCoroutine());

            Assert.IsFalse(completed);

            yield return WaitUntil(() => completed, nameof(completed), maxFrames: 60);

            Assert.IsTrue(completed);
            Assert.GreaterOrEqual(Time.time - startTime, WaitSeconds);
            yield break;

            IEnumerator TestCoroutine()
            {
                yield return new WaitForSeconds(WaitSeconds);
                completed = true;
            }
        }

        [UnityTest]
        public IEnumerator CanRunMultipleCoroutinesConcurrently()
        {
            bool coroutine1Completed = false;
            bool coroutine2Completed = false;
            bool coroutine3Completed = false;

            CoroutineHandler inst = CoroutineHandler.Instance;
            Track(inst.gameObject);
            inst.StartCoroutine(TestCoroutine1());
            inst.StartCoroutine(TestCoroutine2());
            inst.StartCoroutine(TestCoroutine3());

            yield return WaitUntil(
                () => coroutine1Completed && coroutine2Completed && coroutine3Completed,
                "all coroutines completed"
            );

            Assert.IsTrue(coroutine1Completed);
            Assert.IsTrue(coroutine2Completed);
            Assert.IsTrue(coroutine3Completed);
            yield break;

            IEnumerator TestCoroutine1()
            {
                yield return null;
                coroutine1Completed = true;
            }

            IEnumerator TestCoroutine2()
            {
                yield return null;
                coroutine2Completed = true;
            }

            IEnumerator TestCoroutine3()
            {
                yield return null;
                coroutine3Completed = true;
            }
        }

        [UnityTest]
        public IEnumerator CoroutineCanYieldNestedCoroutine()
        {
            bool innerCompleted = false;
            bool outerCompleted = false;

            CoroutineHandler inst = CoroutineHandler.Instance;
            Track(inst.gameObject);
            inst.StartCoroutine(OuterCoroutine());

            yield return WaitUntil(
                () => innerCompleted && outerCompleted,
                "nested coroutine completed"
            );

            Assert.IsTrue(innerCompleted);
            Assert.IsTrue(outerCompleted);
            yield break;

            IEnumerator InnerCoroutine()
            {
                yield return null;
                innerCompleted = true;
            }

            IEnumerator OuterCoroutine()
            {
                yield return InnerCoroutine();
                outerCompleted = true;
            }
        }

        [UnityTest]
        public IEnumerator SingletonPersistsAcrossFrames()
        {
            CoroutineHandler instance1 = CoroutineHandler.Instance;
            Track(instance1.gameObject);

            yield return null;
            yield return null;

            CoroutineHandler instance2 = CoroutineHandler.Instance;

            Assert.AreSame(instance1, instance2);
        }

        [UnityTest]
        public IEnumerator InstanceIsDontDestroyOnLoad()
        {
            CoroutineHandler instance = CoroutineHandler.Instance;
            Track(instance.gameObject);

            yield return null;

            Assert.IsTrue(
                instance.gameObject.scene.name == "DontDestroyOnLoad"
                    || instance.gameObject.hideFlags.HasFlag(HideFlags.DontSave)
            );
        }

        [UnityTest]
        public IEnumerator StoppingNonexistentCoroutineDoesNotThrow()
        {
            Coroutine coroutine = CoroutineHandler.Instance.StartCoroutine(DummyCoroutine());
            Track(CoroutineHandler.Instance.gameObject);

            yield return null;
            yield return null;

            CoroutineHandler.Instance.StopCoroutine(coroutine);

            yield return null;

            Assert.Pass();
            yield break;

            IEnumerator DummyCoroutine()
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator HandlesExceptionInCoroutine()
        {
            bool continueAfterException = false;

            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex(".*Test exception.*")
            );
            CoroutineHandler inst = CoroutineHandler.Instance;
            Track(inst.gameObject);
            inst.StartCoroutine(ThrowingCoroutine());
            inst.StartCoroutine(SafeCoroutine());

            yield return WaitUntil(
                () => continueAfterException,
                nameof(continueAfterException),
                maxFrames: 12
            );

            Assert.IsTrue(continueAfterException);
            yield break;

            IEnumerator ThrowingCoroutine()
            {
                yield return null;
                throw new System.Exception("Test exception");
            }

            IEnumerator SafeCoroutine()
            {
                yield return null;
                yield return null;
                continueAfterException = true;
            }
        }

        [UnityTest]
        public IEnumerator CoroutineStopsWhenObjectDestroyed()
        {
            int counter = 0;

            CoroutineHandler instance = CoroutineHandler.Instance;
            Track(instance.gameObject);
            instance.StartCoroutine(TestCoroutine());

            yield return null;
            yield return null;

            int countBeforeDestroy = counter;

            GameObject handlerObject = instance.gameObject;
            Object.Destroy(handlerObject); // UNH-SUPPRESS: Test verifies coroutine stops after destruction

            yield return WaitUntilDestroyed(handlerObject);
            int countAfterDestroy = counter;
            yield return null;
            yield return null;

            Assert.GreaterOrEqual(countAfterDestroy, countBeforeDestroy);
            Assert.AreEqual(countAfterDestroy, counter);
            yield break;

            IEnumerator TestCoroutine()
            {
                while (true)
                {
                    counter++;
                    yield return null;
                }
            }
        }

        private static IEnumerator WaitUntil(
            Func<bool> condition,
            string description,
            int maxFrames = 30,
            float maxSeconds = 5f
        )
        {
            // Bound by BOTH a frame budget and a wall-clock budget. In headless batchmode the frame
            // rate can exceed several thousand FPS, so a fixed frame count can elapse in well under a
            // millisecond -- far too little real time for a time-gated condition (e.g. a coroutine that
            // yields WaitForSeconds). Keep waiting while EITHER budget remains so frame-gated and
            // time-gated conditions are both satisfied.
            float deadline = Time.time + maxSeconds;
            int frames = 0;
            while (!condition() && (frames < maxFrames || Time.time < deadline))
            {
                yield return null;
                frames++;
            }

            Assert.IsTrue(
                condition(),
                $"Timed out after {frames} frame(s) / {maxSeconds:0.###}s waiting for {description}. Frame={Time.frameCount}, time={Time.time:0.###}."
            );
        }
    }
}
