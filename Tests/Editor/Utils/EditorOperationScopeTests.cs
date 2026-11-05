// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class EditorOperationScopeTests : CommonTestBase
    {
        private const string OperationName = "Scope Test Operation";
        private const string FollowingOperationName = "Following Scope Operation";
        private const string FailureMessage = "scope failure control";
        private const int ProfilerIterations = 512;
        private const int ProfilerCapacity = 65536;
        private static readonly Action ManualOperation = RunManualOperation;
        private static readonly Action ScopedOperation = RunScopedOperation;

        [Test]
        public void DefaultAndCopiedUndoScopesReleaseTheirGroupOnlyOnce()
        {
            UndoGroupScope empty = default;
            int initialGroup = Undo.GetCurrentGroup();
            empty.Dispose();
            Assert.AreEqual(initialGroup, Undo.GetCurrentGroup());
            UndoGroupScope scope = UndoGroupScope.Begin(OperationName);
            UndoGroupScope copy = scope;
            Assert.IsTrue(scope.IsHeld);
            Assert.IsTrue(copy.IsHeld);
            bool sameOwner = scope.Equals(copy);
            Assert.IsTrue(sameOwner);
            scope.Dispose();
            int releasedGroup = Undo.GetCurrentGroup();
            Assert.IsFalse(copy.IsHeld);
            copy.Dispose();
            Assert.AreEqual(releasedGroup, Undo.GetCurrentGroup());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void InvalidUndoGroupNamesLeaveTheCurrentGroupAlone(string name)
        {
            int originalGroup = Undo.GetCurrentGroup();
            using UndoGroupScope scope = UndoGroupScope.Begin(name);
            Assert.IsFalse(scope.IsHeld);
            Assert.AreEqual(originalGroup, Undo.GetCurrentGroup());
        }

        [Test]
        public void ProgressScopeCopiesShareOneCleanupClaim()
        {
            DisposableScope scope = EditorUi.BeginProgressScope();
            DisposableScope copy = scope;
            Assert.IsTrue(copy.IsHeld);
            scope.Dispose();
            Assert.IsFalse(copy.IsHeld);
            copy.Dispose();
            Assert.IsFalse(scope.IsHeld);
        }

        [Test]
        public void ExceptionUnwindingReleasesProgressAndKeepsTheMutationUndoable()
        {
            GameObject host = Track(new GameObject(nameof(EditorOperationScopeTests)));
            Transform target = host.transform;
            UndoGroupScope undoCopy = default;
            DisposableScope progressCopy = default;
            TestDelegate body = () =>
            {
                using UndoGroupScope undo = UndoGroupScope.Begin(OperationName);
                undoCopy = undo;
                using DisposableScope progress = EditorUi.BeginProgressScope();
                progressCopy = progress;
                Undo.RecordObject(target, OperationName);
                target.localPosition = Vector3.one;
                Undo.FlushUndoRecordObjects();
                throw new InvalidOperationException(FailureMessage);
            };
            Assert.Throws<InvalidOperationException>(body);
            Assert.IsFalse(undoCopy.IsHeld);
            Assert.IsFalse(progressCopy.IsHeld);
            Undo.PerformUndo();
            Assert.AreEqual(Vector3.zero, target.localPosition);
        }

        [Test]
        public void FollowingOperationsStaySeparateFromTheCollapsedGroup()
        {
            GameObject host = Track(new GameObject(nameof(EditorOperationScopeTests)));
            Transform target = host.transform;
            using (UndoGroupScope scope = UndoGroupScope.Begin(OperationName))
            {
                Undo.RecordObject(target, OperationName);
                target.localPosition = Vector3.one;
                Undo.FlushUndoRecordObjects();
            }
            Vector3 followingPosition = Vector3.one * 2f;
            Undo.RecordObject(target, FollowingOperationName);
            target.localPosition = followingPosition;
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.AreEqual(Vector3.one, target.localPosition);
            Undo.PerformUndo();
            Assert.AreEqual(Vector3.zero, target.localPosition);
            Undo.PerformRedo();
            Assert.AreEqual(Vector3.one, target.localPosition);
            Undo.PerformRedo();
            Assert.AreEqual(followingPosition, target.localPosition);
        }

        [Test]
        public void NestedGroupsCollapseIntoOneOuterUndoStep()
        {
            GameObject host = Track(new GameObject(nameof(EditorOperationScopeTests)));
            Transform target = host.transform;
            Vector3 nestedPosition = Vector3.one * 2f;
            using (UndoGroupScope outer = UndoGroupScope.Begin(OperationName))
            {
                Undo.RecordObject(target, OperationName);
                target.localPosition = Vector3.one;
                Undo.FlushUndoRecordObjects();
                using (UndoGroupScope inner = UndoGroupScope.Begin(FollowingOperationName))
                {
                    Undo.RecordObject(target, FollowingOperationName);
                    target.localPosition = nestedPosition;
                    Undo.FlushUndoRecordObjects();
                }
            }
            Undo.PerformUndo();
            Assert.AreEqual(Vector3.zero, target.localPosition);
            Undo.PerformRedo();
            Assert.AreEqual(nestedPosition, target.localPosition);
        }

        [Test]
        [Category("Performance")]
        public void WarmScopesAddNoProfilerAllocationsToTheUnderlyingOperations()
        {
            byte[] retained = null;
            byte[] existing = new byte[4096];
            Action allocate = () => retained = new byte[4096];
            Action reuse = () => retained = existing;
            const int controlIterations = 16;
            long positive = ProfilerAllocationCounter.Measure(
                allocate,
                controlIterations,
                ProfilerCapacity
            );
            Assert.AreEqual(controlIterations, positive);
            long negative = ProfilerAllocationCounter.Measure(
                reuse,
                controlIterations,
                ProfilerCapacity
            );
            Assert.AreEqual(0, negative);
            long baseline = ProfilerAllocationCounter.Measure(
                ManualOperation,
                ProfilerIterations,
                ProfilerCapacity
            );
            long candidate = ProfilerAllocationCounter.Measure(
                ScopedOperation,
                ProfilerIterations,
                ProfilerCapacity
            );
            TestContext.WriteLine(
                $"EDITOR_SCOPES baseline={baseline} scoped={candidate} control={positive}"
            );
            Assert.AreEqual(baseline, candidate);
            GC.KeepAlive(retained);
        }

        private static void RunManualOperation()
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(OperationName);
            EditorUi.ClearProgress();
            Undo.CollapseUndoOperations(group);
            Undo.IncrementCurrentGroup();
        }

        private static void RunScopedOperation()
        {
            using UndoGroupScope undo = UndoGroupScope.Begin(OperationName);
            using DisposableScope progress = EditorUi.BeginProgressScope();
        }
    }
#endif
}
