// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
#if !SINGLE_THREADED
    using System.Threading;
#endif

    /// <summary>
    /// Pins the fill, reset and reentrancy contract of the four <c>FieldsByType</c> metadata caches.
    /// </summary>
    /// <remarks>
    /// <para>The three relational caches expose <c>ClearCachedFieldMetadata</c> and
    /// <c>HasCachedFieldMetadata</c>, so a cold first fill is reachable from a fixture.
    /// <see cref="ValidateAssignmentExtensions"/> exposes neither, so its cases assert only what is
    /// observable from outside: every caller agrees and nothing throws.</para>
    /// <para>The relational factory reads <c>AttributeMetadataCache.Instance</c>, whose FIRST
    /// resolution is main-thread-only. Warming it before each race is what keeps a red here about
    /// the cache rather than about Unity's main-thread guard.</para>
    /// <para>Under <c>SINGLE_THREADED</c> these caches compile as plain
    /// <see cref="Dictionary{TKey, TValue}"/>, so the racing cases are excluded and the sequential
    /// first-fill contract carries both configurations.</para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentCacheConcurrencyTests : CommonTestBase
    {
        private const int LookupThreadCount = 8;
        private const int LookupsPerThread = 64;
        private const int ResetRounds = 16;
        private const int ReentrantDepthLimit = 3;

        private static readonly TimeSpan ThreadJoinTimeout = TimeSpan.FromSeconds(30);
        private static readonly Type WarmupComponentType = typeof(PrewarmTesterComponent);
        private static readonly Type SubjectComponentType = typeof(RelationalComponentTester);

        private static IEnumerable<TestCaseData> RelationalCaches()
        {
            yield return new TestCaseData(
                (Func<Type, Array>)SiblingComponentExtensions.GetOrCreateFields,
                (Action)SiblingComponentExtensions.ClearCachedFieldMetadata,
                (Func<Type, bool>)SiblingComponentExtensions.HasCachedFieldMetadata
            ).SetName("Sibling");
            yield return new TestCaseData(
                (Func<Type, Array>)ParentComponentExtensions.GetOrCreateFields,
                (Action)ParentComponentExtensions.ClearCachedFieldMetadata,
                (Func<Type, bool>)ParentComponentExtensions.HasCachedFieldMetadata
            ).SetName("Parent");
            yield return new TestCaseData(
                (Func<Type, Array>)ChildComponentExtensions.GetOrCreateFields,
                (Action)ChildComponentExtensions.ClearCachedFieldMetadata,
                (Func<Type, bool>)ChildComponentExtensions.HasCachedFieldMetadata
            ).SetName("Child");
        }

        [SetUp]
        public void WarmMainThreadDependenciesThenClearCaches()
        {
            SiblingComponentExtensions.GetOrCreateFields(WarmupComponentType);
            ParentComponentExtensions.GetOrCreateFields(WarmupComponentType);
            ChildComponentExtensions.GetOrCreateFields(WarmupComponentType);
            SiblingComponentExtensions.ClearCachedFieldMetadata();
            ParentComponentExtensions.ClearCachedFieldMetadata();
            ChildComponentExtensions.ClearCachedFieldMetadata();
        }

        [TestCaseSource(nameof(RelationalCaches))]
        public void FirstFillPublishesOneMetadataInstanceToEveryCaller(
            Func<Type, Array> lookup,
            Action clearCache,
            Func<Type, bool> hasCachedMetadata
        )
        {
            clearCache();
            Assert.IsFalse(
                hasCachedMetadata(SubjectComponentType),
                "Cache still reported metadata for the subject type after a clear."
            );

            Array firstFill = lookup(SubjectComponentType);

            Assert.IsTrue(firstFill != null, "The first fill produced no metadata array.");
            Assert.IsTrue(
                0 < firstFill.Length,
                "The subject type resolved no relational fields, so this case has no subject."
            );
            Assert.IsTrue(
                hasCachedMetadata(SubjectComponentType),
                "The first fill did not publish the subject type into the cache."
            );
            Assert.AreSame(
                firstFill,
                lookup(SubjectComponentType),
                "A later lookup rebuilt the metadata instead of returning the cached instance."
            );
        }

        [Test]
        public void ReentrantValidationLookupTerminatesWithConsistentResults()
        {
            GameObject owner = Track(
                new GameObject("ReentrantValidation", typeof(AssignmentComponent))
            );
            AssignmentComponent component = owner.GetComponent<AssignmentComponent>();
            component.requiredObject = Track(new GameObject("ReentrantValidationTarget"));
            component.requiredString = "assigned";
            component.requiredList.Add(1);
            component.requiredCollection.Enqueue(2);
            ReentrantValidationSource source = new(
                () => component.AreAnyAssignmentsInvalid(),
                ReentrantDepthLimit
            );
            component.requiredEnumerable = source;

            bool anyInvalid = component.AreAnyAssignmentsInvalid();

            Assert.IsTrue(
                source.CapturedException == null,
                "A reentrant validation lookup threw: {0}",
                source.CapturedException
            );
            Assert.AreEqual(
                ReentrantDepthLimit,
                source.ReentrantCallCount,
                "The reentrant probe did not reach its depth limit."
            );
            Assert.AreEqual(
                ReentrantDepthLimit,
                source.DeepestObservedDepth,
                "Nested validation lookups did not nest to the requested depth."
            );
            Assert.IsFalse(
                source.LastReentrantResult,
                "A nested validation lookup disagreed with the outer one."
            );
            Assert.IsFalse(
                anyInvalid,
                "A fully assigned component reported an invalid assignment."
            );
        }

#if !SINGLE_THREADED

        private static Array[] RaceLookups(Func<Type, Array> lookup)
        {
            Array[] observed = new Array[LookupThreadCount];
            Exception capturedException = null;
            int mismatchCount = 0;
            using Barrier startLine = new(LookupThreadCount);
            Thread[] workers = new Thread[LookupThreadCount];
            for (int index = 0; index < LookupThreadCount; index++)
            {
                int slot = index;
                workers[slot] = new Thread(() =>
                {
                    try
                    {
                        startLine.SignalAndWait(ThreadJoinTimeout);
                        Array first = lookup(SubjectComponentType);
                        observed[slot] = first;
                        for (int repeat = 1; repeat < LookupsPerThread; repeat++)
                        {
                            if (first != lookup(SubjectComponentType))
                            {
                                Interlocked.Increment(ref mismatchCount);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Interlocked.CompareExchange(ref capturedException, exception, null);
                    }
                });
            }

            StartThenJoin(workers);

            Assert.IsTrue(
                capturedException == null,
                "A racing lookup threw: {0}",
                capturedException
            );
            Assert.AreEqual(
                0,
                mismatchCount,
                "A lookup after the first fill returned a different metadata instance."
            );
            return observed;
        }

        private static void StartThenJoin(Thread[] workers)
        {
            foreach (Thread worker in workers)
            {
                worker.Start();
            }

            foreach (Thread worker in workers)
            {
                Assert.IsTrue(
                    worker.Join(ThreadJoinTimeout),
                    "A worker did not finish within the join timeout, which is a deadlock."
                );
            }
        }

        [TestCaseSource(nameof(RelationalCaches))]
        public void ConcurrentFirstFillPublishesOneMetadataInstanceToEveryCaller(
            Func<Type, Array> lookup,
            Action clearCache,
            Func<Type, bool> hasCachedMetadata
        )
        {
            clearCache();
            Assert.IsFalse(
                hasCachedMetadata(SubjectComponentType),
                "Cache still reported metadata for the subject type after a clear."
            );

            Array[] observed = RaceLookups(lookup);

            Assert.IsTrue(
                hasCachedMetadata(SubjectComponentType),
                "The raced first fill did not publish the subject type into the cache."
            );
            foreach (Array resolved in observed)
            {
                Assert.IsTrue(resolved != null, "A racing caller received no metadata array.");
                Assert.AreSame(
                    observed[0],
                    resolved,
                    "Racing callers received different metadata instances for the same first fill."
                );
            }
        }

        [TestCaseSource(nameof(RelationalCaches))]
        public void ClearDuringConcurrentLookupsNeverYieldsPartialMetadata(
            Func<Type, Array> lookup,
            Action clearCache,
            Func<Type, bool> hasCachedMetadata
        )
        {
            clearCache();
            int expectedFieldCount = lookup(SubjectComponentType).Length;
            Assert.IsTrue(
                0 < expectedFieldCount,
                "The subject type resolved no relational fields, so a partial-metadata check would be vacuous."
            );
            clearCache();
            Assert.IsFalse(
                hasCachedMetadata(SubjectComponentType),
                "Cache still reported metadata for the subject type after a clear."
            );

            Exception capturedException = null;
            int nullResultCount = 0;
            int partialResultCount = 0;
            using Barrier startLine = new(LookupThreadCount + 1);
            Thread[] workers = new Thread[LookupThreadCount + 1];
            for (int index = 0; index < LookupThreadCount; index++)
            {
                workers[index] = new Thread(() =>
                {
                    try
                    {
                        startLine.SignalAndWait(ThreadJoinTimeout);
                        for (int repeat = 0; repeat < LookupsPerThread; repeat++)
                        {
                            Array resolved = lookup(SubjectComponentType);
                            if (resolved == null)
                            {
                                Interlocked.Increment(ref nullResultCount);
                            }
                            else if (resolved.Length != expectedFieldCount)
                            {
                                Interlocked.Increment(ref partialResultCount);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Interlocked.CompareExchange(ref capturedException, exception, null);
                    }
                });
            }
            workers[LookupThreadCount] = new Thread(() =>
            {
                try
                {
                    startLine.SignalAndWait(ThreadJoinTimeout);
                    for (int round = 0; round < ResetRounds; round++)
                    {
                        clearCache();
                    }
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref capturedException, exception, null);
                }
            });

            StartThenJoin(workers);

            Assert.IsTrue(
                capturedException == null,
                "A lookup racing a cache clear threw: {0}",
                capturedException
            );
            Assert.AreEqual(
                0,
                nullResultCount,
                "A lookup racing a cache clear returned no metadata array."
            );
            Assert.AreEqual(
                0,
                partialResultCount,
                "A lookup racing a cache clear observed a partially built metadata array."
            );
        }

        [Test]
        public void ConcurrentValidationLookupsAgreeWithoutThrowing()
        {
            GameObject owner = Track(
                new GameObject("ValidationCacheRace", typeof(AssignmentComponent))
            );
            AssignmentComponent component = owner.GetComponent<AssignmentComponent>();

            Exception capturedException = null;
            int disagreementCount = 0;
            using Barrier startLine = new(LookupThreadCount);
            Thread[] workers = new Thread[LookupThreadCount];
            for (int index = 0; index < LookupThreadCount; index++)
            {
                workers[index] = new Thread(() =>
                {
                    try
                    {
                        startLine.SignalAndWait(ThreadJoinTimeout);
                        for (int repeat = 0; repeat < LookupsPerThread; repeat++)
                        {
                            if (!component.AreAnyAssignmentsInvalid())
                            {
                                Interlocked.Increment(ref disagreementCount);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Interlocked.CompareExchange(ref capturedException, exception, null);
                    }
                });
            }

            StartThenJoin(workers);

            Assert.IsTrue(
                capturedException == null,
                "A racing validation lookup threw: {0}",
                capturedException
            );
            Assert.AreEqual(
                0,
                disagreementCount,
                "A racing validation lookup called an unassigned component valid."
            );
            /*
                Asserted after the race rather than before it: reading the answer on this thread
                first would warm the cache and cost the race whatever chance of a cold fill the run
                afforded. ValidateAssignmentExtensions exposes no clear seam, so that chance is all
                there is.
            */
            Assert.IsTrue(
                component.AreAnyAssignmentsInvalid(),
                "The unassigned component reported no invalid assignment, so the race had no subject."
            );
        }
#endif

        private sealed class ReentrantValidationSource : IEnumerable<int>
        {
            private static readonly int[] SinglePayload = new int[] { 1 };

            internal int ReentrantCallCount { get; private set; }

            internal int DeepestObservedDepth { get; private set; }

            internal bool LastReentrantResult { get; private set; }

            internal Exception CapturedException { get; private set; }

            private readonly Func<bool> _reentrantProbe;
            private readonly int _depthLimit;

            private int _activeDepth;

            internal ReentrantValidationSource(Func<bool> reentrantProbe, int depthLimit)
            {
                _reentrantProbe = reentrantProbe;
                _depthLimit = depthLimit;
            }

            public IEnumerator<int> GetEnumerator()
            {
                ProbeReentrantly();
                return ((IEnumerable<int>)SinglePayload).GetEnumerator();
            }

            private void ProbeReentrantly()
            {
                if (_depthLimit <= _activeDepth)
                {
                    return;
                }

                _activeDepth++;
                if (DeepestObservedDepth < _activeDepth)
                {
                    DeepestObservedDepth = _activeDepth;
                }

                try
                {
                    LastReentrantResult = _reentrantProbe();
                    ReentrantCallCount++;
                }
                catch (Exception exception)
                {
                    CapturedException = exception;
                }
                finally
                {
                    _activeDepth--;
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
