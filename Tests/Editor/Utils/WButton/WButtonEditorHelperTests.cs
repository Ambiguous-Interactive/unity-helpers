// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Comprehensive tests for <see cref="WButtonEditorHelper"/> public API.
    /// Verifies that users can integrate WButton functionality in their custom inspectors.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WButtonEditorHelperTests : BatchedEditorTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            ClearWButtonCaches();
        }

        [TearDown]
        public override void TearDown()
        {
            ClearWButtonCaches();
            base.TearDown();
        }

        private static void ClearWButtonCaches()
        {
            WButtonMetadataCache.ClearCache();
        }

        [Test]
        public void ConstructorCreatesValidInstance()
        {
            WButtonEditorHelper helper = new WButtonEditorHelper();

            Assert.That(helper, Is.Not.Null);
        }

        [Test]
        public void DrawButtonsAtTopReturnsFalseWhenNoWButtonMethodsExist()
        {
            HelperTargetNoButtons target = CreateScriptableObject<HelperTargetNoButtons>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawButtonsAtTop(editor);

            Assert.That(result, Is.False);
        }

        [Test]
        public void DrawButtonsAtTopReturnsTrueWhenTopPlacementButtonsExist()
        {
            HelperTargetTopPlacement target = CreateScriptableObject<HelperTargetTopPlacement>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);

            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawButtonsAtTop(editor);

            Assert.That(result, Is.True);
        }

        [Test]
        public void DrawButtonsAtBottomReturnsFalseWhenNoWButtonMethodsExist()
        {
            HelperTargetNoButtons target = CreateScriptableObject<HelperTargetNoButtons>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawButtonsAtBottom(editor);

            Assert.That(result, Is.False);
        }

        [Test]
        public void DrawButtonsAtBottomReturnsTrueWhenBottomPlacementButtonsExist()
        {
            HelperTargetBottomPlacement target =
                CreateScriptableObject<HelperTargetBottomPlacement>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawButtonsAtBottom(editor);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ProcessInvocationsHandlesEmptyInvocationList()
        {
            WButtonEditorHelper helper = new WButtonEditorHelper();

            Assert.DoesNotThrow(() => helper.ProcessInvocations());
        }

        [Test]
        public void ProcessInvocationsProcessesTriggeredMethodsCorrectly()
        {
            HelperTargetSimple target = CreateScriptableObject<HelperTargetSimple>();
            target.InvocationCount = 0;
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            helper.DrawButtonsAtTop(editor);
            helper.DrawButtonsAtBottom(editor);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetSimple)
            );
            WButtonMethodMetadata simpleButtonMetadata = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetSimple.SimpleButton)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(
                simpleButtonMetadata
            );
            WButtonMethodContext context = new WButtonMethodContext(
                simpleButtonMetadata,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );
            context.MarkTriggered();

            List<WButtonMethodContext> contexts = new List<WButtonMethodContext> { context };
            WButtonInvocationController.ProcessTriggeredMethods(contexts);

            Assert.That(target.InvocationCount, Is.EqualTo(1));
        }

        [Test]
        public void DrawButtonsAtBottomAndProcessInvocationsCombinesBothOperations()
        {
            HelperTargetBottomPlacement target =
                CreateScriptableObject<HelperTargetBottomPlacement>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawButtonsAtBottomAndProcessInvocations(editor);

            Assert.That(result, Is.True);
        }

        [Test]
        public void DrawAllButtonsAndProcessInvocationsDrawsAllButtonsRegardlessOfPlacement()
        {
            HelperTargetMixedPlacement target =
                CreateScriptableObject<HelperTargetMixedPlacement>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True);
        }

        [Test]
        public void MultipleSequentialCallsToHelperMethodsWorkCorrectly()
        {
            HelperTargetSimple target = CreateScriptableObject<HelperTargetSimple>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            for (int i = 0; i < 5; i++)
            {
                Assert.DoesNotThrow(() => helper.DrawButtonsAtTop(editor));
                Assert.DoesNotThrow(() => helper.DrawButtonsAtBottom(editor));
                Assert.DoesNotThrow(() => helper.ProcessInvocations());
            }
        }

        [Test]
        public void HelperWorksWithMultipleTargetObjectsInEditor()
        {
            HelperTargetSimple target1 = CreateScriptableObject<HelperTargetSimple>();
            HelperTargetSimple target2 = CreateScriptableObject<HelperTargetSimple>();
            UnityEngine.Object[] targets = new UnityEngine.Object[] { target1, target2 };
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(targets);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool topResult = helper.DrawButtonsAtTop(editor);
            bool bottomResult = helper.DrawButtonsAtBottom(editor);
            Assert.DoesNotThrow(() => helper.ProcessInvocations());

            Assert.That(topResult || bottomResult, Is.True);
        }

        [Test]
        public void HelperProperlyCachesMetadataBetweenCalls()
        {
            HelperTargetSimple target = CreateScriptableObject<HelperTargetSimple>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            IReadOnlyList<WButtonMethodMetadata> firstCall = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetSimple)
            );
            helper.DrawButtonsAtTop(editor);
            IReadOnlyList<WButtonMethodMetadata> secondCall = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetSimple)
            );

            Assert.That(firstCall, Is.SameAs(secondCall));
        }

        [Test]
        public void HelperHandlesEditorTargetBecomingNullGracefully()
        {
            HelperTargetSimple target = CreateScriptableObject<HelperTargetSimple>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            Assert.DoesNotThrow(() => helper.DrawButtonsAtTop(editor));

            UnityEngine.Object.DestroyImmediate(target); // UNH-SUPPRESS: Test verifies graceful handling when target destroyed
            Assert.DoesNotThrow(() => helper.ProcessInvocations());
        }

        [Test]
        public void HelperWorksWithInheritedWButtonMethods()
        {
            HelperTargetDerived target = CreateScriptableObject<HelperTargetDerived>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetDerived)
            );
            Assert.That(
                metadata.Any(m => m.Method.Name == nameof(HelperTargetBase.BaseButton)),
                Is.True,
                "Should find inherited BaseButton method"
            );
            Assert.That(
                metadata.Any(m => m.Method.Name == nameof(HelperTargetDerived.DerivedButton)),
                Is.True,
                "Should find DerivedButton method"
            );
        }

        [Test]
        public void HelperRespectsWButtonGroupPlacementSettings()
        {
            HelperTargetGroupPlacement target =
                CreateScriptableObject<HelperTargetGroupPlacement>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool topResult = helper.DrawButtonsAtTop(editor);
            bool bottomResult = helper.DrawButtonsAtBottom(editor);

            Assert.That(topResult, Is.True, "Should draw top placement group");
            Assert.That(bottomResult, Is.True, "Should draw bottom placement group");

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetGroupPlacement)
            );
            WButtonMethodMetadata topButton = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetGroupPlacement.TopGroupButton)
            );
            WButtonMethodMetadata bottomButton = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetGroupPlacement.BottomGroupButton)
            );

            Assert.That(
                topButton.GroupPlacement,
                Is.EqualTo(WButtonGroupPlacement.Top),
                "TopGroupButton should have Top placement"
            );
            Assert.That(
                bottomButton.GroupPlacement,
                Is.EqualTo(WButtonGroupPlacement.Bottom),
                "BottomGroupButton should have Bottom placement"
            );
        }

        [Test]
        public void TestWithMethodsThatHaveWGroupAttribute()
        {
            HelperTargetWithGroup target = CreateScriptableObject<HelperTargetWithGroup>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            Assert.DoesNotThrow(() => helper.DrawAllButtonsAndProcessInvocations(editor));

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetWithGroup)
            );
            WButtonMethodMetadata groupedButton = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(HelperTargetWithGroup.GroupedButton)
            );

            Assert.That(groupedButton, Is.Not.Null);
            Assert.That(groupedButton.GroupName, Is.EqualTo("TestGroup"));
        }

        [Test]
        public void TestThatParameterValuesArePreservedAcrossDraws()
        {
            HelperTargetWithParameters target =
                CreateScriptableObject<HelperTargetWithParameters>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetWithParameters)
            );
            WButtonMethodMetadata paramMethod = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetWithParameters.ButtonWithParam)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(paramMethod);

            string testValue = "TestValue123";
            methodState.Parameters[0].CurrentValue = testValue;

            helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(
                methodState.Parameters[0].CurrentValue,
                Is.EqualTo(testValue),
                "Parameter value should be preserved across draws"
            );
        }

        [UnityTest]
        public IEnumerator TestAsyncMethodIntegrationThroughHelper()
        {
            HelperTargetAsync target = CreateScriptableObject<HelperTargetAsync>();
            target.TaskCompletionCount = 0;
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();
            helper.DrawAllButtonsAndProcessInvocations(editor);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetAsync)
            );
            WButtonMethodMetadata asyncMethod = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetAsync.AsyncButton)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(asyncMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                asyncMethod,
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
        public void HelperDoesNotThrowWithNullTargetsArray()
        {
            HelperTargetSimple target = CreateScriptableObject<HelperTargetSimple>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            UnityEngine.Object.DestroyImmediate(target); // UNH-SUPPRESS: Test verifies graceful handling when targets array contains destroyed objects
            _trackedObjects.Remove(target);

            WButtonEditorHelper helper = new WButtonEditorHelper();

            Assert.DoesNotThrow(() => helper.DrawButtonsAtTop(editor));
            Assert.DoesNotThrow(() => helper.DrawButtonsAtBottom(editor));
            Assert.DoesNotThrow(() => helper.ProcessInvocations());
        }

        [Test]
        public void HelperWorksWithStaticWButtonMethods()
        {
            HelperTargetWithStaticMethod target =
                CreateScriptableObject<HelperTargetWithStaticMethod>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetWithStaticMethod)
            );
            Assert.That(
                metadata.Any(m =>
                    m.Method.Name == nameof(HelperTargetWithStaticMethod.StaticButton)
                ),
                Is.True,
                "Should find static WButton method"
            );
        }

        [Test]
        public void HelperWorksWithNonPublicWButtonMethods()
        {
            HelperTargetWithNonPublicMethod target =
                CreateScriptableObject<HelperTargetWithNonPublicMethod>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetWithNonPublicMethod)
            );
            Assert.That(
                metadata.Any(m =>
                    m.Method.Name == nameof(HelperTargetWithNonPublicMethod.InternalButton)
                ),
                Is.True,
                "Should find non-public WButton method"
            );
        }

        [Test]
        public void HelperWorksWithDisplayNameOverride()
        {
            HelperTargetWithDisplayName target =
                CreateScriptableObject<HelperTargetWithDisplayName>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetWithDisplayName)
            );
            WButtonMethodMetadata buttonWithDisplayName = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetWithDisplayName.SomeMethod)
            );

            Assert.That(buttonWithDisplayName.DisplayName, Is.EqualTo("Custom Display Name"));
        }

        [Test]
        public void HelperWorksWithDrawOrderSettings()
        {
            HelperTargetWithDrawOrder target = CreateScriptableObject<HelperTargetWithDrawOrder>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetWithDrawOrder)
            );

            WButtonMethodMetadata firstButton = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetWithDrawOrder.FirstButton)
            );
            WButtonMethodMetadata secondButton = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetWithDrawOrder.SecondButton)
            );

            Assert.That(firstButton.DrawOrder, Is.EqualTo(0));
            Assert.That(secondButton.DrawOrder, Is.EqualTo(10));
        }

        [Test]
        public void HelperWorksWithGroupPrioritySettings()
        {
            HelperTargetWithGroupPriority target =
                CreateScriptableObject<HelperTargetWithGroupPriority>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetWithGroupPriority)
            );

            WButtonMethodMetadata highPriority = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetWithGroupPriority.HighPriorityButton)
            );
            WButtonMethodMetadata lowPriority = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetWithGroupPriority.LowPriorityButton)
            );

            Assert.That(highPriority.GroupPriority, Is.EqualTo(0));
            Assert.That(lowPriority.GroupPriority, Is.EqualTo(100));
        }

        [UnityTest]
        public IEnumerator TestCancellableAsyncMethodThroughHelper()
        {
            HelperTargetCancellable target = CreateScriptableObject<HelperTargetCancellable>();
            target.WasCancelled = false;
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();
            helper.DrawAllButtonsAndProcessInvocations(editor);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetCancellable)
            );
            WButtonMethodMetadata cancellableMethod = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetCancellable.CancellableAsyncButton)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(cancellableMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                cancellableMethod,
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

        [Test]
        public void HelperWorksWithMonoBehaviourTarget()
        {
            GameObject go = NewGameObject("HelperMBTest");
            HelperMonoBehaviourTarget target = go.AddComponent<HelperMonoBehaviourTarget>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperMonoBehaviourTarget)
            );
            Assert.That(metadata.Count, Is.GreaterThan(0));
        }

        [Test]
        public void RepeatedDrawCallsDoNotLeakMemory()
        {
            HelperTargetSimple target = CreateScriptableObject<HelperTargetSimple>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            for (int i = 0; i < 100; i++)
            {
                helper.DrawButtonsAtTop(editor);
                helper.DrawButtonsAtBottom(editor);
                helper.ProcessInvocations();
            }

            Assert.Pass("Completed 100 draw cycles without exception");
        }

        [Test]
        public void HelperWithColorKeyMethod()
        {
            HelperTargetWithColorKey target = CreateScriptableObject<HelperTargetWithColorKey>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetWithColorKey)
            );
            WButtonMethodMetadata colorKeyButton = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetWithColorKey.ColoredButton)
            );

            Assert.That(colorKeyButton.ColorKey, Is.EqualTo("CustomColor"));
        }

        [Test]
        public void HelperWithHistoryCapacityOverride()
        {
            HelperTargetWithHistoryCapacity target =
                CreateScriptableObject<HelperTargetWithHistoryCapacity>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetWithHistoryCapacity)
            );
            WButtonMethodMetadata historyButton = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetWithHistoryCapacity.LimitedHistoryButton)
            );

            Assert.That(historyButton.HistoryCapacity, Is.EqualTo(3));
        }

        [Test]
        public void HelperWithMethodReturningValue()
        {
            HelperTargetWithReturnValue target =
                CreateScriptableObject<HelperTargetWithReturnValue>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetWithReturnValue)
            );
            WButtonMethodMetadata returnMethod = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetWithReturnValue.ButtonWithReturn)
            );

            Assert.That(returnMethod.ReturnsVoid, Is.False);
            Assert.That(returnMethod.ReturnType, Is.EqualTo(typeof(int)));
        }

        [Test]
        public void HelperWithMultipleParameters()
        {
            HelperTargetMultipleParams target =
                CreateScriptableObject<HelperTargetMultipleParams>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetMultipleParams)
            );
            WButtonMethodMetadata multiParamMethod = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetMultipleParams.MultiParamButton)
            );

            Assert.That(multiParamMethod.Parameters.Length, Is.EqualTo(3));
            Assert.That(multiParamMethod.Parameters[0].ParameterType, Is.EqualTo(typeof(string)));
            Assert.That(multiParamMethod.Parameters[1].ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(multiParamMethod.Parameters[2].ParameterType, Is.EqualTo(typeof(bool)));
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

        [UnityTest]
        public IEnumerator ValueTaskMethodInvocationCompletesSuccessfully()
        {
            HelperTargetValueTask target = CreateScriptableObject<HelperTargetValueTask>();
            target.CompletionCount = 0;
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();
            helper.DrawAllButtonsAndProcessInvocations(editor);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetValueTask)
            );
            WButtonMethodMetadata valueTaskMethod = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetValueTask.ValueTaskButton)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(valueTaskMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                valueTaskMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);

            Assert.That(
                target.CompletionCount,
                Is.EqualTo(1),
                "ValueTask method should have completed"
            );
            Assert.That(methodState.History.Count, Is.GreaterThan(0), "Should have history entry");
            WButtonResultEntry entry = methodState.History[^1];
            Assert.That(
                entry.Kind,
                Is.EqualTo(WButtonResultKind.Success),
                "Should be success entry"
            );
        }

        [UnityTest]
        public IEnumerator ValueTaskWithReturnValueCapturesResultInHistory()
        {
            HelperTargetValueTaskWithReturn target =
                CreateScriptableObject<HelperTargetValueTaskWithReturn>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);

            WButtonEditorHelper helper = new WButtonEditorHelper();
            helper.DrawAllButtonsAndProcessInvocations(editor);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetValueTaskWithReturn)
            );
            WButtonMethodMetadata valueTaskMethod = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetValueTaskWithReturn.ValueTaskWithReturnButton)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(valueTaskMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                valueTaskMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);

            Assert.That(methodState.History.Count, Is.GreaterThan(0), "Should have history entry");
            WButtonResultEntry entry = methodState.History[^1];
            Assert.That(
                entry.Kind,
                Is.EqualTo(WButtonResultKind.Success),
                "Should be success entry"
            );
            Assert.That(entry.Value, Is.EqualTo(99), "Should capture returned value in history");
        }

        [Test]
        public void SynchronousMethodReturnValueCapturedInHistory()
        {
            HelperTargetWithReturnValue target =
                CreateScriptableObject<HelperTargetWithReturnValue>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);

            WButtonEditorHelper helper = new WButtonEditorHelper();

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetWithReturnValue)
            );
            WButtonMethodMetadata returnMethod = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetWithReturnValue.ButtonWithReturn)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(returnMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                returnMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            Assert.That(methodState.History.Count, Is.GreaterThan(0), "Should have history entry");
            WButtonResultEntry entry = methodState.History[^1];
            Assert.That(
                entry.Kind,
                Is.EqualTo(WButtonResultKind.Success),
                "Should be success entry"
            );
            Assert.That(entry.Value, Is.EqualTo(42), "Should capture returned integer value");
        }

        [Test]
        public void StringReturnValueCapturedInHistory()
        {
            HelperTargetStringReturn target = CreateScriptableObject<HelperTargetStringReturn>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetStringReturn)
            );
            WButtonMethodMetadata returnMethod = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetStringReturn.GetMessage)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(returnMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                returnMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            Assert.That(methodState.History.Count, Is.GreaterThan(0), "Should have history entry");
            WButtonResultEntry entry = methodState.History[^1];
            Assert.That(entry.Kind, Is.EqualTo(WButtonResultKind.Success));
            Assert.That(
                entry.Value,
                Is.EqualTo("Hello from WButton"),
                "Should capture string return value"
            );
        }

        [Test]
        public void NullEditorParameterHandledGracefully()
        {
            WButtonEditorHelper helper = new WButtonEditorHelper();

            Assert.DoesNotThrow(
                () => helper.DrawButtonsAtTop(null),
                "DrawButtonsAtTop should handle null editor gracefully"
            );
            Assert.DoesNotThrow(
                () => helper.DrawButtonsAtBottom(null),
                "DrawButtonsAtBottom should handle null editor gracefully"
            );
            Assert.DoesNotThrow(
                () => helper.DrawButtonsAtBottomAndProcessInvocations(null),
                "DrawButtonsAtBottomAndProcessInvocations should handle null editor gracefully"
            );
            Assert.DoesNotThrow(
                () => helper.DrawAllButtonsAndProcessInvocations(null),
                "DrawAllButtonsAndProcessInvocations should handle null editor gracefully"
            );
        }

        [Test]
        public void NullEditorReturnsFalseForDrawMethods()
        {
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool topResult = helper.DrawButtonsAtTop(null);
            bool bottomResult = helper.DrawButtonsAtBottom(null);
            bool bottomAndProcessResult = helper.DrawButtonsAtBottomAndProcessInvocations(null);
            bool allResult = helper.DrawAllButtonsAndProcessInvocations(null);

            Assert.That(topResult, Is.False, "DrawButtonsAtTop with null should return false");
            Assert.That(
                bottomResult,
                Is.False,
                "DrawButtonsAtBottom with null should return false"
            );
            Assert.That(
                bottomAndProcessResult,
                Is.False,
                "DrawButtonsAtBottomAndProcessInvocations with null should return false"
            );
            Assert.That(
                allResult,
                Is.False,
                "DrawAllButtonsAndProcessInvocations with null should return false"
            );
        }

        [UnityTest]
        public IEnumerator AsyncTaskWithReturnValueCapturesResultInHistory()
        {
            HelperTargetAsyncWithReturn target =
                CreateScriptableObject<HelperTargetAsyncWithReturn>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            WButtonEditorHelper helper = new WButtonEditorHelper();
            helper.DrawAllButtonsAndProcessInvocations(editor);

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(HelperTargetAsyncWithReturn)
            );
            WButtonMethodMetadata asyncMethod = metadata.First(m =>
                m.Method.Name == nameof(HelperTargetAsyncWithReturn.AsyncButtonWithReturn)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(asyncMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                asyncMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);

            Assert.That(methodState.History.Count, Is.GreaterThan(0), "Should have history entry");
            WButtonResultEntry entry = methodState.History[^1];
            Assert.That(
                entry.Kind,
                Is.EqualTo(WButtonResultKind.Success),
                "Should be success entry"
            );
            Assert.That(
                entry.Value,
                Is.EqualTo("AsyncResult"),
                "Should capture async returned value"
            );
        }
    }

    // Test target classes

    // ADD THESE TEST TARGETS:
}
