// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WButton
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WButtonDisabledStateTests : CommonTestBase
    {
        [Test]
        public void GetInvocationStatusNoActiveInvocationReturnsZeroRunningCount()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m => m.Method.Name == nameof(DisabledStateTestTarget.SyncButton));
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);

            WButtonMethodState[] states = new[] { methodState };
            GetInvocationStatusViaReflection(states, out int runningCount, out bool cancellable);

            Assert.That(runningCount, Is.EqualTo(0));
            Assert.That(cancellable, Is.False);
        }

        [UnityTest]
        public IEnumerator GetInvocationStatusActiveInvocationReturnsPositiveRunningCount()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m => m.Method.Name == nameof(DisabledStateTestTarget.SlowAsyncButton));
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonMethodState[] states = new[] { methodState };
            GetInvocationStatusViaReflection(states, out int runningCount, out bool cancellable);

            Assert.That(
                runningCount,
                Is.GreaterThan(0),
                "Running count should be positive while task is executing"
            );
            Assert.That(
                cancellable,
                Is.False,
                "Non-cancellable task should report cancellable as false"
            );

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);
        }

        [UnityTest]
        public IEnumerator GetInvocationStatusCancellableInvocationReturnsCancellableTrue()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m =>
                    m.Method.Name == nameof(DisabledStateTestTarget.CancellableAsyncButton)
                );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonMethodState[] states = new[] { methodState };
            GetInvocationStatusViaReflection(states, out int runningCount, out bool cancellable);

            Assert.That(
                runningCount,
                Is.GreaterThan(0),
                "Running count should be positive while task is executing"
            );
            Assert.That(cancellable, Is.True, "Cancellable task should report cancellable as true");

            WButtonInvocationController.CancelActiveInvocations(context);
            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);
        }

        [UnityTest]
        public IEnumerator ActiveInvocationSetWhileRunningClearedOnCompletion()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m => m.Method.Name == nameof(DisabledStateTestTarget.FastAsyncButton));
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            Assert.That(
                methodState.ActiveInvocation == null,
                "ActiveInvocation should be null before execution"
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);

            Assert.That(
                methodState.ActiveInvocation == null,
                "ActiveInvocation should be null after completion"
            );
        }

        [UnityTest]
        public IEnumerator CancelActiveInvocationsCancelsRunningTask()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();
            target.WasCancelled = false;

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m =>
                    m.Method.Name == nameof(DisabledStateTestTarget.CancellableAsyncButton)
                );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            Assert.That(
                methodState.ActiveInvocation != null,
                "ActiveInvocation should be set while running"
            );

            WButtonInvocationController.CancelActiveInvocations(context);

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);

            Assert.That(target.WasCancelled, Is.True, "Task should have been cancelled");
        }

        [UnityTest]
        public IEnumerator InvocationHandleStatusChangesToCancelRequestedOnCancel()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m =>
                    m.Method.Name == nameof(DisabledStateTestTarget.CancellableAsyncButton)
                );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonInvocationHandle handle = methodState.ActiveInvocation;
            Assert.That(handle != null);
            Assert.That(handle.Status, Is.EqualTo(WButtonInvocationStatus.Running));

            handle.Cancel();

            Assert.That(
                handle.Status,
                Is.EqualTo(WButtonInvocationStatus.CancelRequested),
                "Status should change to CancelRequested after Cancel is called"
            );

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);
        }

        [UnityTest]
        public IEnumerator GetInvocationStatusCancelRequestedStatusStillReportsAsRunning()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m =>
                    m.Method.Name == nameof(DisabledStateTestTarget.CancellableAsyncButton)
                );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonInvocationHandle handle = methodState.ActiveInvocation;
            Assert.That(handle != null);
            handle.Cancel();

            WButtonMethodState[] states = new[] { methodState };
            GetInvocationStatusViaReflection(states, out int runningCount, out bool cancellable);

            Assert.That(
                runningCount,
                Is.GreaterThan(0),
                "CancelRequested status should still count as running for UI purposes"
            );

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);
        }

        [UnityTest]
        public IEnumerator MultipleTargetsAllTrackedIndependently()
        {
            DisabledStateTestTarget target1 = CreateScriptableObject<DisabledStateTestTarget>();
            DisabledStateTestTarget target2 = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m => m.Method.Name == nameof(DisabledStateTestTarget.SlowAsyncButton));

            WButtonTargetState targetState1 = WButtonStateRepository.GetOrCreate(target1);
            WButtonMethodState methodState1 = targetState1.GetOrCreateMethodState(metadata);

            WButtonTargetState targetState2 = WButtonStateRepository.GetOrCreate(target2);
            WButtonMethodState methodState2 = targetState2.GetOrCreateMethodState(metadata);

            WButtonMethodContext context = new(
                metadata,
                new[] { methodState1, methodState2 },
                new UnityEngine.Object[] { target1, target2 }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonMethodState[] states = new[] { methodState1, methodState2 };
            GetInvocationStatusViaReflection(states, out int runningCount, out bool cancellable);

            Assert.That(runningCount, Is.EqualTo(2), "Both targets should be tracked as running");

            yield return WaitUntilWithDiagnostics(
                () =>
                    methodState1.ActiveInvocation == null && methodState2.ActiveInvocation == null,
                5f,
                () =>
                    $"methodState1.ActiveInvocation={(methodState1.ActiveInvocation != null ? "Active" : "null")}, "
                    + $"methodState2.ActiveInvocation={(methodState2.ActiveInvocation != null ? "Active" : "null")}, "
                    + $"methodState1.History.Count={methodState1.History.Count}, "
                    + $"methodState2.History.Count={methodState2.History.Count}"
            );

            Assert.That(
                methodState1.History.Count,
                Is.GreaterThan(0),
                "methodState1 should have recorded history after completion"
            );
            Assert.That(
                methodState2.History.Count,
                Is.GreaterThan(0),
                "methodState2 should have recorded history after completion"
            );
        }

        [UnityTest]
        public IEnumerator EnumeratorTrackedAsRunningUntilComplete()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m => m.Method.Name == nameof(DisabledStateTestTarget.SlowEnumeratorButton));
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonMethodState[] states = new[] { methodState };
            GetInvocationStatusViaReflection(states, out int runningCount, out bool cancellable);

            Assert.That(runningCount, Is.GreaterThan(0), "Enumerator should be tracked as running");

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);

            GetInvocationStatusViaReflection(states, out runningCount, out cancellable);
            Assert.That(
                runningCount,
                Is.EqualTo(0),
                "Enumerator should no longer be running after completion"
            );
        }

        [UnityTest]
        public IEnumerator EnumeratorMethodAlwaysSupportsCancellation()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m => m.Method.Name == nameof(DisabledStateTestTarget.SlowEnumeratorButton));
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonMethodState[] states = new[] { methodState };
            GetInvocationStatusViaReflection(states, out int runningCount, out bool cancellable);

            Assert.That(runningCount, Is.GreaterThan(0), "Enumerator should be tracked as running");
            Assert.That(
                cancellable,
                Is.True,
                "Enumerator methods should always support cancellation"
            );

            WButtonInvocationController.CancelActiveInvocations(context);
            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);
        }

        [UnityTest]
        public IEnumerator EnumeratorCancellationRecordsHistoryEntry()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m => m.Method.Name == nameof(DisabledStateTestTarget.SlowEnumeratorButton));
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonInvocationController.CancelActiveInvocations(context);

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);

            Assert.That(
                methodState.History.Count,
                Is.GreaterThan(0),
                "History should have an entry after cancellation"
            );
            WButtonResultEntry lastEntry = methodState.History[^1];
            Assert.That(
                lastEntry.Kind,
                Is.EqualTo(WButtonResultKind.Cancelled),
                "History entry should indicate cancellation"
            );
        }

        [Test]
        public void SynchronousMethodNeverReportsAsRunning()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m => m.Method.Name == nameof(DisabledStateTestTarget.SyncButton));
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            WButtonMethodState[] states = new[] { methodState };
            GetInvocationStatusViaReflection(states, out int runningCount, out bool cancellable);

            Assert.That(
                runningCount,
                Is.EqualTo(0),
                "Synchronous methods complete immediately and should not report as running"
            );
            Assert.That(methodState.ActiveInvocation == null);
        }

        [UnityTest]
        public IEnumerator CancelledTaskRecordsHistoryEntry()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m =>
                    m.Method.Name == nameof(DisabledStateTestTarget.CancellableAsyncButton)
                );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonInvocationController.CancelActiveInvocations(context);

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);

            Assert.That(
                methodState.History.Count,
                Is.GreaterThan(0),
                "History should have an entry after cancellation"
            );
            WButtonResultEntry lastEntry = methodState.History[^1];
            Assert.That(
                lastEntry.Kind,
                Is.EqualTo(WButtonResultKind.Cancelled),
                "History entry should indicate cancellation"
            );
        }

        [UnityTest]
        public IEnumerator InvocationHandleSupportsCancellationTrueForCancellableMethod()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m =>
                    m.Method.Name == nameof(DisabledStateTestTarget.CancellableAsyncButton)
                );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonInvocationHandle handle = methodState.ActiveInvocation;
            Assert.That(handle != null);
            Assert.That(handle.SupportsCancellation, Is.True);

            WButtonInvocationController.CancelActiveInvocations(context);
            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);
        }

        [UnityTest]
        public IEnumerator InvocationHandleSupportsCancellationFalseForNonCancellableAsyncMethod()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m => m.Method.Name == nameof(DisabledStateTestTarget.SlowAsyncButton));
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonInvocationHandle handle = methodState.ActiveInvocation;
            Assert.That(handle != null);
            Assert.That(handle.SupportsCancellation, Is.False);

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);
        }

        [Test]
        public void GetInvocationStatusNullStatesReturnsZero()
        {
            GetInvocationStatusViaReflection(null, out int runningCount, out bool cancellable);

            Assert.That(runningCount, Is.EqualTo(0));
            Assert.That(cancellable, Is.False);
        }

        [Test]
        public void GetInvocationStatusEmptyStatesReturnsZero()
        {
            WButtonMethodState[] states = Array.Empty<WButtonMethodState>();
            GetInvocationStatusViaReflection(states, out int runningCount, out bool cancellable);

            Assert.That(runningCount, Is.EqualTo(0));
            Assert.That(cancellable, Is.False);
        }

        [UnityTest]
        public IEnumerator RepeatedInvocationCancelsPreviousAndStartsNew()
        {
            DisabledStateTestTarget target = CreateScriptableObject<DisabledStateTestTarget>();
            target.InvocationCount = 0;

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(DisabledStateTestTarget))
                .First(m =>
                    m.Method.Name == nameof(DisabledStateTestTarget.CountingCancellableButton)
                );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(metadata);
            WButtonMethodContext context = new(
                metadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonInvocationHandle firstHandle = methodState.ActiveInvocation;
            Assert.That(firstHandle != null);

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return null;

            WButtonInvocationHandle secondHandle = methodState.ActiveInvocation;
            Assert.That(secondHandle != null);
            Assert.That(
                secondHandle,
                Is.Not.SameAs(firstHandle),
                "New invocation should create new handle"
            );

            Assert.That(
                firstHandle.Status == WButtonInvocationStatus.CancelRequested
                    || firstHandle.Status == WButtonInvocationStatus.Cancelled,
                "Previous invocation should be cancelled"
            );

            WButtonInvocationController.CancelActiveInvocations(context);
            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);
        }

        private static void GetInvocationStatusViaReflection(
            WButtonMethodState[] states,
            out int runningCount,
            out bool cancellable
        )
        {
            WButtonGUI.GetInvocationStatus(states, out runningCount, out cancellable);
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            float endTime = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (endTime < Time.realtimeSinceStartup)
                {
                    Assert.Fail("Timed out while waiting for condition.");
                }
                yield return null;
            }
        }

        private static IEnumerator WaitUntilWithDiagnostics(
            Func<bool> condition,
            float timeoutSeconds,
            Func<string> diagnosticsProvider
        )
        {
            float endTime = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (endTime < Time.realtimeSinceStartup)
                {
                    string diagnostics =
                        diagnosticsProvider?.Invoke() ?? "No diagnostics available";
                    Assert.Fail(
                        $"Timed out while waiting for condition. Diagnostics: {diagnostics}"
                    );
                }
                yield return null;
            }
        }
    }
}
#endif
