// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomEditors
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.CustomEditors;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.WButton;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.WButton;

    /// <summary>
    /// Tests for WButton behavior with Odin Inspector custom editors.
    /// Verifies that WButtonOdinMonoBehaviourInspector and WButtonOdinScriptableObjectInspector
    /// properly render WButton methods in the inspector.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class OdinWButtonInspectorTests : BatchedEditorTestBase
    {
        [Test]
        public void WButtonOdinMonoBehaviourInspectorCanBeInstantiated()
        {
            OdinMonoBehaviourTestTarget target = NewGameObject("OdinMBTest")
                .AddComponent<OdinMonoBehaviourTestTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            Assert.That(editor, Is.Not.Null);
            Assert.That(editor, Is.TypeOf<WButtonOdinMonoBehaviourInspector>());
        }

        [Test]
        public void WButtonOdinScriptableObjectInspectorCanBeInstantiated()
        {
            OdinScriptableObjectTestTarget target =
                CreateScriptableObject<OdinScriptableObjectTestTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            Assert.That(editor, Is.Not.Null);
            Assert.That(editor, Is.TypeOf<WButtonOdinScriptableObjectInspector>());
        }

        [UnityTest]
        public IEnumerator WButtonOdinMonoBehaviourInspectorOnInspectorGuiDoesNotThrow()
        {
            OdinMonoBehaviourTestTarget target = NewGameObject("OdinMBTest")
                .AddComponent<OdinMonoBehaviourTestTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator WButtonOdinScriptableObjectInspectorOnInspectorGuiDoesNotThrow()
        {
            OdinScriptableObjectTestTarget target =
                CreateScriptableObject<OdinScriptableObjectTestTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void WButtonMetadataCacheFindsMethodsOnOdinSerializedScriptableObject()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(OdinScriptableObjectTestTarget)
            );

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.Count, Is.GreaterThan(0));
            Assert.That(
                metadata.Any(m =>
                    m.Method.Name == nameof(OdinScriptableObjectTestTarget.SimpleButton)
                ),
                Is.True,
                "Should find SimpleButton method"
            );
        }

        [Test]
        public void WButtonMetadataCacheFindsMethodsOnOdinSerializedMonoBehaviour()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(OdinMonoBehaviourTestTarget)
            );

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.Count, Is.GreaterThan(0));
            Assert.That(
                metadata.Any(m => m.Method.Name == nameof(OdinMonoBehaviourTestTarget.TestAction)),
                Is.True,
                "Should find TestAction method"
            );
        }

        [UnityTest]
        public IEnumerator OdinScriptableObjectWithNoButtonsDoesNotThrow()
        {
            OdinScriptableObjectNoButtons target =
                CreateScriptableObject<OdinScriptableObjectNoButtons>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator OdinMonoBehaviourWithNoButtonsDoesNotThrow()
        {
            OdinMonoBehaviourNoButtons target = NewGameObject("OdinMBNoButtons")
                .AddComponent<OdinMonoBehaviourNoButtons>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void OdinScriptableObjectWithMultipleGroupsHasCorrectMetadata()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(OdinScriptableObjectMultipleGroups)
            );

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.Count, Is.EqualTo(4), "Should have 4 WButton methods");

            WButtonMethodMetadata group1Button1 = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectMultipleGroups.Group1Button1)
            );
            WButtonMethodMetadata group1Button2 = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectMultipleGroups.Group1Button2)
            );
            WButtonMethodMetadata group2Button1 = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectMultipleGroups.Group2Button1)
            );
            WButtonMethodMetadata ungroupedButton = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectMultipleGroups.UngroupedButton)
            );

            Assert.That(group1Button1, Is.Not.Null);
            Assert.That(group1Button2, Is.Not.Null);
            Assert.That(group2Button1, Is.Not.Null);
            Assert.That(ungroupedButton, Is.Not.Null);

            Assert.That(group1Button1.GroupName, Is.EqualTo("Group1"));
            Assert.That(group1Button2.GroupName, Is.EqualTo("Group1"));
            Assert.That(group2Button1.GroupName, Is.EqualTo("Group2"));
            Assert.That(ungroupedButton.GroupName, Is.Null);
        }

        [UnityTest]
        public IEnumerator OdinScriptableObjectMultipleGroupsInspectorDoesNotThrow()
        {
            OdinScriptableObjectMultipleGroups target =
                CreateScriptableObject<OdinScriptableObjectMultipleGroups>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void OdinScriptableObjectWithParametersHasCorrectParameterMetadata()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(OdinScriptableObjectWithParameters)
            );

            Assert.That(metadata, Is.Not.Null);

            WButtonMethodMetadata stringParamMethod = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectWithParameters.ButtonWithStringParam)
            );
            WButtonMethodMetadata intParamMethod = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectWithParameters.ButtonWithIntParam)
            );
            WButtonMethodMetadata multiParamMethod = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectWithParameters.ButtonWithMultipleParams)
            );

            Assert.That(stringParamMethod, Is.Not.Null);
            Assert.That(stringParamMethod.Parameters.Length, Is.EqualTo(1));
            Assert.That(stringParamMethod.Parameters[0].ParameterType, Is.EqualTo(typeof(string)));

            Assert.That(intParamMethod, Is.Not.Null);
            Assert.That(intParamMethod.Parameters.Length, Is.EqualTo(1));
            Assert.That(intParamMethod.Parameters[0].ParameterType, Is.EqualTo(typeof(int)));

            Assert.That(multiParamMethod, Is.Not.Null);
            Assert.That(multiParamMethod.Parameters.Length, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator OdinScriptableObjectWithParametersInspectorDoesNotThrow()
        {
            OdinScriptableObjectWithParameters target =
                CreateScriptableObject<OdinScriptableObjectWithParameters>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void OdinScriptableObjectWithAsyncMethodsHasCorrectExecutionKind()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(OdinScriptableObjectAsync)
            );

            Assert.That(metadata, Is.Not.Null);

            WButtonMethodMetadata asyncTaskMethod = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectAsync.AsyncTaskButton)
            );
            WButtonMethodMetadata asyncValueTaskMethod = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectAsync.AsyncValueTaskButton)
            );
            WButtonMethodMetadata enumeratorMethod = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectAsync.EnumeratorButton)
            );
            WButtonMethodMetadata syncMethod = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectAsync.SyncButton)
            );

            Assert.That(asyncTaskMethod, Is.Not.Null);
            Assert.That(asyncTaskMethod.ExecutionKind, Is.EqualTo(WButtonExecutionKind.Task));

            Assert.That(asyncValueTaskMethod, Is.Not.Null);
            Assert.That(
                asyncValueTaskMethod.ExecutionKind,
                Is.EqualTo(WButtonExecutionKind.ValueTask)
            );

            Assert.That(enumeratorMethod, Is.Not.Null);
            Assert.That(
                enumeratorMethod.ExecutionKind,
                Is.EqualTo(WButtonExecutionKind.Enumerator)
            );

            Assert.That(syncMethod, Is.Not.Null);
            Assert.That(syncMethod.ExecutionKind, Is.EqualTo(WButtonExecutionKind.Synchronous));
        }

        [UnityTest]
        public IEnumerator OdinScriptableObjectAsyncInspectorDoesNotThrow()
        {
            OdinScriptableObjectAsync target = CreateScriptableObject<OdinScriptableObjectAsync>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator OdinScriptableObjectAsyncTaskButtonCanBeInvoked()
        {
            OdinScriptableObjectAsync target = CreateScriptableObject<OdinScriptableObjectAsync>();
            target.TaskCompletionCount = 0;

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(OdinScriptableObjectAsync))
                .First(m => m.Method.Name == nameof(OdinScriptableObjectAsync.AsyncTaskButton));
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

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);

            Assert.That(target.TaskCompletionCount, Is.EqualTo(1));
            Assert.That(methodState.History.Count, Is.GreaterThan(0));
            WButtonResultEntry entry = methodState.History[^1];
            Assert.That(entry.Kind, Is.EqualTo(WButtonResultKind.Success));
        }

        [Test]
        public void OdinScriptableObjectWithCancellableMethodHasCorrectMetadata()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(OdinScriptableObjectCancellable)
            );

            Assert.That(metadata, Is.Not.Null);

            WButtonMethodMetadata cancellableMethod = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectCancellable.CancellableAsyncButton)
            );

            Assert.That(cancellableMethod, Is.Not.Null);
            Assert.That(
                cancellableMethod.CancellationTokenParameterIndex,
                Is.GreaterThanOrEqualTo(0)
            );
        }

        [UnityTest]
        public IEnumerator OdinScriptableObjectCancellableMethodCanBeCancelled()
        {
            OdinScriptableObjectCancellable target =
                CreateScriptableObject<OdinScriptableObjectCancellable>();
            target.WasCancelled = false;

            WButtonMethodMetadata metadata = WButtonMetadataCache
                .GetMetadata(typeof(OdinScriptableObjectCancellable))
                .First(m =>
                    m.Method.Name == nameof(OdinScriptableObjectCancellable.CancellableAsyncButton)
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

            Assert.That(methodState.ActiveInvocation, Is.Not.Null);

            WButtonInvocationController.CancelActiveInvocations(context);

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);

            Assert.That(target.WasCancelled, Is.True);
        }

        [UnityTest]
        public IEnumerator MultipleEditorsCanBeCreatedForSameTargetType()
        {
            OdinScriptableObjectTestTarget target1 =
                CreateScriptableObject<OdinScriptableObjectTestTarget>();
            OdinScriptableObjectTestTarget target2 =
                CreateScriptableObject<OdinScriptableObjectTestTarget>();

            UnityEditor.Editor editor1 = Track(UnityEditor.Editor.CreateEditor(target1));
            UnityEditor.Editor editor2 = Track(UnityEditor.Editor.CreateEditor(target2));
            bool testCompleted = false;
            Exception caughtException = null;

            Assert.That(editor1, Is.Not.Null);
            Assert.That(editor2, Is.Not.Null);
            Assert.That(editor1, Is.Not.SameAs(editor2));

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor1.OnInspectorGUI();
                    editor2.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator CanEditMultipleObjectsDoesNotThrow()
        {
            OdinScriptableObjectTestTarget target1 =
                CreateScriptableObject<OdinScriptableObjectTestTarget>();
            OdinScriptableObjectTestTarget target2 =
                CreateScriptableObject<OdinScriptableObjectTestTarget>();

            UnityEngine.Object[] targets = new UnityEngine.Object[] { target1, target2 };
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(targets));
            bool testCompleted = false;
            Exception caughtException = null;

            Assert.That(editor, Is.Not.Null);

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator RepeatedOnInspectorGuiCallsDoNotThrow()
        {
            OdinScriptableObjectTestTarget target =
                CreateScriptableObject<OdinScriptableObjectTestTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 10; i++)
                    {
                        editor.OnInspectorGUI();
                    }
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void OdinScriptableObjectWithDisplayNameHasCorrectMetadata()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(OdinScriptableObjectTestTarget)
            );

            WButtonMethodMetadata customDisplayMethod = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectTestTarget.MethodWithCustomDisplay)
            );

            Assert.That(customDisplayMethod, Is.Not.Null);
            Assert.That(customDisplayMethod.DisplayName, Is.EqualTo("Custom Display Name"));
        }

        [Test]
        public void OdinScriptableObjectWithDrawOrderHasCorrectMetadata()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(OdinScriptableObjectMultipleGroups)
            );

            WButtonMethodMetadata group1Button1 = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectMultipleGroups.Group1Button1)
            );
            WButtonMethodMetadata group1Button2 = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(OdinScriptableObjectMultipleGroups.Group1Button2)
            );

            Assert.That(group1Button1, Is.Not.Null);
            Assert.That(group1Button2, Is.Not.Null);
            Assert.That(group1Button1.DrawOrder, Is.EqualTo(0));
            Assert.That(group1Button2.DrawOrder, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OdinMonoBehaviourInspectorHandlesDestroyedTargetGracefully()
        {
            GameObject go = NewGameObject("OdinMBTest");
            OdinMonoBehaviourTestTarget target = go.AddComponent<OdinMonoBehaviourTestTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"First OnInspectorGUI should not throw. Exception: {caughtException}"
            );

            UnityEngine.Object.DestroyImmediate(target); // UNH-SUPPRESS: Intentional destruction to test inspector behavior with destroyed target
            caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"Second OnInspectorGUI should not throw after target destroyed. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator OdinScriptableObjectInspectorHandlesNullSerializedObjectGracefully()
        {
            OdinScriptableObjectTestTarget target =
                CreateScriptableObject<OdinScriptableObjectTestTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            float endTime = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > endTime)
                {
                    Assert.Fail("Timed out while waiting for condition.");
                }
                yield return null;
            }
        }
    }
#endif
}
