// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using NUnit.Framework;
    using UnityEngine.TestTools.Constraints;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Is = UnityEngine.TestTools.Constraints.Is;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RestorableGlobalTests : CommonTestBase
    {
        private int _cell;
        private bool _readThrows;
        private bool _writeThrows;
        private int _writesAttempted;

        [Test]
        public void ABorrowAppliesTheValueAndDisposalRestoresWhatWasThere()
        {
            RestorableGlobal<int> owner = NewOwner(7);

            using (owner.Borrow(11))
            {
                Assert.AreEqual(11, _cell);
                Assert.AreEqual(1, owner.Depth);
            }

            Assert.AreEqual(7, _cell);
            Assert.AreEqual(0, owner.Depth);
        }

        [Test]
        public void NestedBorrowsRestoreInReverseOrder()
        {
            RestorableGlobal<int> owner = NewOwner(1);

            using (owner.Borrow(2))
            {
                using (owner.Borrow(3))
                {
                    Assert.AreEqual(3, _cell);
                }

                Assert.AreEqual(2, _cell);
            }

            Assert.AreEqual(1, _cell);
        }

        [Test]
        public void ReleasingAnOlderBorrowFirstLeavesTheNewerValueApplied()
        {
            RestorableGlobal<int> owner = NewOwner(1);
            RestorableGlobal<int>.Scope older = owner.Borrow(2);
            RestorableGlobal<int>.Scope newer = owner.Borrow(3);

            older.Dispose();
            Assert.AreEqual(
                3,
                _cell,
                "the newer borrow is still live and is still entitled to the value it asked for"
            );

            newer.Dispose();
            Assert.AreEqual(
                1,
                _cell,
                "the released borrow's restore value is inherited, so the last release returns the original"
            );
        }

        [TestCase(0, 1, 2, 3, 4, TestName = "DisposalOrder.Oldest.First")]
        [TestCase(4, 3, 2, 1, 0, TestName = "DisposalOrder.Newest.First")]
        [TestCase(2, 0, 3, 1, 4, TestName = "DisposalOrder.Scrambled")]
        [TestCase(1, 3, 0, 4, 2, TestName = "DisposalOrder.Interleaved")]
        public void AnyDisposalOrderOfDeepNestingEndsAtTheOriginalValue(
            int first,
            int second,
            int third,
            int fourth,
            int fifth
        )
        {
            RestorableGlobal<int> owner = NewOwner(100);
            RestorableGlobal<int>.Scope[] scopes = new RestorableGlobal<int>.Scope[5];
            for (int index = 0; index < scopes.Length; index++)
            {
                scopes[index] = owner.Borrow(index + 1);
            }

            Assert.AreEqual(5, _cell);
            Assert.AreEqual(5, owner.Depth);

            int[] order = { first, second, third, fourth, fifth };
            for (int step = 0; step < order.Length; step++)
            {
                scopes[order[step]].Dispose();
            }

            Assert.AreEqual(0, owner.Depth);
            Assert.AreEqual(100, _cell);
        }

        [Test]
        public void ADisposedCopyCannotReimposeAStaleValue()
        {
            RestorableGlobal<int> owner = NewOwner(5);
            RestorableGlobal<int>.Scope scope = owner.Borrow(9);
            RestorableGlobal<int>.Scope copy = scope;

            scope.Dispose();
            Assert.AreEqual(5, _cell);

            _cell = 42;
            copy.Dispose();

            Assert.AreEqual(42, _cell, "a copy of a released borrow must change nothing");
        }

        [Test]
        public void DisposingTheSameScopeTwiceReleasesOnce()
        {
            RestorableGlobal<int> owner = NewOwner(5);
            RestorableGlobal<int>.Scope scope = owner.Borrow(9);

            scope.Dispose();
            _cell = 42;
            scope.Dispose();

            Assert.AreEqual(42, _cell);
            Assert.AreEqual(0, owner.Depth);
        }

        [Test]
        public void AStaleCopyCannotReleaseTheBorrowThatReusedItsSlot()
        {
            RestorableGlobal<int> owner = NewOwner(5);
            RestorableGlobal<int>.Scope original = owner.Borrow(9);
            RestorableGlobal<int>.Scope stale = original;
            original.Dispose();

            RestorableGlobal<int>.Scope replacement = owner.Borrow(13);
            stale.Dispose();
            Assert.AreEqual(
                13,
                _cell,
                "the identifier is never reused, so the stale release misses"
            );
            Assert.AreEqual(1, owner.Depth);

            replacement.Dispose();
            Assert.AreEqual(5, _cell);
        }

        [Test]
        public void IsHeldAnswersForEveryCopyAtOnce()
        {
            RestorableGlobal<int> owner = NewOwner(5);
            RestorableGlobal<int>.Scope scope = owner.Borrow(9);
            RestorableGlobal<int>.Scope copy = scope;

            Assert.IsTrue(scope.IsHeld);
            Assert.IsTrue(copy.IsHeld);

            copy.Dispose();

            Assert.IsFalse(scope.IsHeld);
            Assert.IsFalse(copy.IsHeld);
        }

        [Test]
        public void ADefaultScopeIsNotHeldAndDisposesToNothing()
        {
            RestorableGlobal<int>.Scope scope = default;

            Assert.IsFalse(scope.IsHeld);
            Assert.DoesNotThrow(() => scope.Dispose());
            Assert.DoesNotThrow(() => scope.Dispose());
        }

        [Test]
        public void AnExternalWriteBetweenBorrowsIsWhatTheOuterReleaseRestores()
        {
            RestorableGlobal<int> owner = NewOwner(1);
            RestorableGlobal<int>.Scope outer = owner.Borrow(2);
            _cell = 50;
            RestorableGlobal<int>.Scope inner = owner.Borrow(3);

            outer.Dispose();
            Assert.AreEqual(3, _cell);

            inner.Dispose();
            Assert.AreEqual(
                50,
                _cell,
                "the inner borrow captured the external write, so that is what comes back"
            );
        }

        [Test]
        public void ReleaseAllRestoresTheOldestValueAndStrandsEveryScope()
        {
            RestorableGlobal<int> owner = NewOwner(1);
            RestorableGlobal<int>.Scope first = owner.Borrow(2);
            RestorableGlobal<int>.Scope second = owner.Borrow(3);

            owner.ReleaseAll();

            Assert.AreEqual(1, _cell);
            Assert.AreEqual(0, owner.Depth);
            Assert.IsFalse(first.IsHeld);
            Assert.IsFalse(second.IsHeld);

            _cell = 77;
            first.Dispose();
            second.Dispose();

            Assert.AreEqual(77, _cell);
        }

        [Test]
        public void ReleaseAllOnAnOwnerWithNoBorrowsChangesNothing()
        {
            RestorableGlobal<int> owner = NewOwner(1);

            Assert.DoesNotThrow(() => owner.ReleaseAll());

            Assert.AreEqual(1, _cell);
            Assert.AreEqual(0, owner.Depth);
        }

        [Test]
        public void AGetterThatThrowsBorrowsNothingAndLeavesTheGlobalAlone()
        {
            RestorableGlobal<int> owner = NewOwner(4);
            _readThrows = true;

            bool borrowed = owner.TryBorrow(9, out RestorableGlobal<int>.Scope scope);

            Assert.IsFalse(borrowed);
            Assert.IsFalse(scope.IsHeld);
            Assert.AreEqual(0, owner.Depth);
            Assert.AreEqual(4, _cell);
            Assert.AreEqual(0, _writesAttempted);
            Assert.DoesNotThrow(() => scope.Dispose());
        }

        [Test]
        public void ASetterThatThrowsWhileBorrowingStillSchedulesTheRestore()
        {
            RestorableGlobal<int> owner = NewOwner(4);
            _writeThrows = true;

            bool borrowed = owner.TryBorrow(9, out RestorableGlobal<int>.Scope scope);

            Assert.IsFalse(borrowed, "the caller is told the value did not take");
            Assert.IsTrue(scope.IsHeld, "the borrow is recorded so the restore still happens");
            Assert.AreEqual(1, owner.Depth);

            _writeThrows = false;
            scope.Dispose();

            Assert.AreEqual(4, _cell);
            Assert.AreEqual(0, owner.Depth);
        }

        [Test]
        public void ASetterThatThrowsWhileReleasingDoesNotThrowFromDispose()
        {
            RestorableGlobal<int> owner = NewOwner(4);
            RestorableGlobal<int>.Scope scope = owner.Borrow(9);
            _writeThrows = true;

            Assert.DoesNotThrow(() => scope.Dispose());
            Assert.AreEqual(0, owner.Depth);
            Assert.IsFalse(scope.IsHeld);
        }

        [TestCase(true, false, TestName = "Delegates.Getter.Null")]
        [TestCase(false, true, TestName = "Delegates.Setter.Null")]
        [TestCase(true, true, TestName = "Delegates.Both.Null")]
        public void AnOwnerMissingADelegateBorrowsNothing(bool readIsNull, bool writeIsNull)
        {
            _cell = 3;
            RestorableGlobal<int> owner = new RestorableGlobal<int>(
                readIsNull ? null : new Func<int>(() => _cell),
                writeIsNull ? null : new Action<int>(value => _cell = value)
            );

            bool borrowed = owner.TryBorrow(9, out RestorableGlobal<int>.Scope scope);

            Assert.IsFalse(borrowed);
            Assert.IsFalse(scope.IsHeld);
            Assert.AreEqual(3, _cell);
            Assert.DoesNotThrow(() => scope.Dispose());
        }

        [Test]
        public void ABorrowOfAReferenceTypeRestoresTheReference()
        {
            string cell = "original";
            RestorableGlobal<string> owner = new RestorableGlobal<string>(
                () => cell,
                value => cell = value
            );

            using (owner.Borrow("borrowed"))
            {
                Assert.AreEqual("borrowed", cell);
            }

            Assert.AreEqual("original", cell);
        }

        [Test]
        public void ABorrowAllocatesNothingOnceTheTableHasWarmed()
        {
            RestorableGlobal<int> owner = NewOwner(0);
            for (int warmUp = 0; warmUp < 16; ++warmUp)
            {
                RestorableGlobal<int>.Scope outer = owner.Borrow(warmUp);
                RestorableGlobal<int>.Scope inner = owner.Borrow(warmUp + 1);
                inner.Dispose();
                outer.Dispose();
            }

            if (!AllocationProbe.RecorderCanSeeAnAllocation())
            {
                Assert.Ignore(
                    "GC allocation recording is inert on this player, so a 'did not allocate' "
                        + "verdict would prove nothing"
                );
            }

            Assert.That(
                () =>
                {
                    for (int i = 0; i < AllocationProbe.Iterations; ++i)
                    {
                        using (owner.Borrow(i))
                        {
                            if (_cell != i)
                            {
                                throw new InvalidOperationException("the borrow did not apply");
                            }
                        }
                    }
                },
                Is.Not.AllocatingGCMemory(),
                "the scope is a struct and the slot table is already grown, so a borrow is free"
            );
        }

        private RestorableGlobal<int> NewOwner(int initial)
        {
            _cell = initial;
            _readThrows = false;
            _writeThrows = false;
            _writesAttempted = 0;
            return new RestorableGlobal<int>(ReadCell, WriteCell);
        }

        private int ReadCell()
        {
            if (_readThrows)
            {
                throw new InvalidOperationException("the getter refused");
            }

            return _cell;
        }

        private void WriteCell(int value)
        {
            _writesAttempted++;
            if (_writeThrows)
            {
                throw new InvalidOperationException("the setter refused");
            }

            _cell = value;
        }
    }
}
