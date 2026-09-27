// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Utils
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class DisposableScopeTests
    {
        private static readonly Action NoOp = DoNothing;

        private static void DoNothing() { }

        [Test]
        public void DefaultAndNullScopesAreHarmless()
        {
            DisposableScope missing = default;
            DisposableScope nullCleanup = DisposableScope.Create(null);

            Assert.That(missing.IsHeld, Is.False);
            Assert.That(nullCleanup.IsHeld, Is.False);
            Assert.DoesNotThrow(missing.Dispose);
            Assert.DoesNotThrow(nullCleanup.Dispose);
        }

        [Test]
        public void CopiesRunCleanupOnlyOnce()
        {
            int calls = 0;
            DisposableScope original = DisposableScope.Create(() => ++calls);
            DisposableScope copy = original;

            copy.Dispose();
            original.Dispose();
            copy.Dispose();

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(original.IsHeld, Is.False);
            Assert.That(copy.IsHeld, Is.False);
        }

        [Test]
        public void ChainedCleanupsRunInReverseOrderOnce()
        {
            List<int> order = new();
            DisposableScope<DisposableScope<DisposableScope>> scope = DisposableScope
                .Create(() => order.Add(1))
                .WithCleanup(() => order.Add(2))
                .WithCleanup(() => order.Add(3));
            DisposableScope<DisposableScope<DisposableScope>> copy = scope;

            copy.Dispose();
            scope.Dispose();

            Assert.That(order, Is.EqualTo(new[] { 3, 2, 1 }));
        }

        [Test]
        public void NullAddedCleanupStillDisposesEarlierCleanup()
        {
            int calls = 0;
            DisposableScope<DisposableScope> scope = DisposableScope
                .Create(() => ++calls)
                .WithCleanup(null);

            scope.Dispose();
            scope.Dispose();

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void FailingCleanupDoesNotPreventEarlierCleanups()
        {
            int calls = 0;
            DisposableScope<DisposableScope> scope = DisposableScope
                .Create(() => ++calls)
                .WithCleanup(() => throw new InvalidOperationException());

            Assert.DoesNotThrow(scope.Dispose);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void ScopeCreationAndDisposalDoNotAllocateAfterWarmup()
        {
            AllocationProbe.IgnoreWhenUnmeasurable();

            DisposableScope warm = DisposableScope.Create(NoOp);
            warm.Dispose();

            GCAssert.DoesNotAllocate(() =>
            {
                using DisposableScope<DisposableScope> scope = DisposableScope
                    .Create(NoOp)
                    .WithCleanup(NoOp);
            });
        }
    }
}
