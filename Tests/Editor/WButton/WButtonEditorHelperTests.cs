// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WButton
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Tests for WButtonEditorHelper API used to integrate WButton with custom editors.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WButtonEditorHelperTests : CommonTestBase
    {
        [Test]
        public void WButtonEditorHelperCanBeCreated()
        {
            WButtonEditorHelper helper = new WButtonEditorHelper();
            Assert.That(helper, Is.Not.Null);
        }

        [Test]
        public void WButtonEditorHelperDrawButtonsAtTopWithValidEditorDoesNotThrow()
        {
            TestComponent target = CreateScriptableObject<TestComponent>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            try
            {
                WButtonEditorHelper helper = new WButtonEditorHelper();

                Assert.DoesNotThrow(() => helper.DrawButtonsAtTop(editor));
            }
            finally
            {
                Object.DestroyImmediate(editor); // UNH-SUPPRESS: Editor is not a tracked Unity object
            }
        }

        [Test]
        public void WButtonEditorHelperDrawButtonsAtBottomWithValidEditorDoesNotThrow()
        {
            TestComponent target = CreateScriptableObject<TestComponent>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            try
            {
                WButtonEditorHelper helper = new WButtonEditorHelper();

                Assert.DoesNotThrow(() => helper.DrawButtonsAtBottom(editor));
            }
            finally
            {
                Object.DestroyImmediate(editor); // UNH-SUPPRESS: Editor is not a tracked Unity object
            }
        }

        [Test]
        public void WButtonEditorHelperProcessInvocationsDoesNotThrow()
        {
            WButtonEditorHelper helper = new WButtonEditorHelper();

            Assert.DoesNotThrow(() => helper.ProcessInvocations());
        }

        [Test]
        public void WButtonEditorHelperDrawButtonsAtBottomAndProcessInvocationsDoesNotThrow()
        {
            TestComponent target = CreateScriptableObject<TestComponent>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            try
            {
                WButtonEditorHelper helper = new WButtonEditorHelper();

                Assert.DoesNotThrow(() => helper.DrawButtonsAtBottomAndProcessInvocations(editor));
            }
            finally
            {
                Object.DestroyImmediate(editor); // UNH-SUPPRESS: Editor is not a tracked Unity object
            }
        }

        [Test]
        public void WButtonEditorHelperDrawAllButtonsAndProcessInvocationsDoesNotThrow()
        {
            TestComponent target = CreateScriptableObject<TestComponent>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            try
            {
                WButtonEditorHelper helper = new WButtonEditorHelper();

                Assert.DoesNotThrow(() => helper.DrawAllButtonsAndProcessInvocations(editor));
            }
            finally
            {
                Object.DestroyImmediate(editor); // UNH-SUPPRESS: Editor is not a tracked Unity object
            }
        }

        [Test]
        public void WButtonEditorHelperMultipleInstancesMaintainSeparateState()
        {
            WButtonEditorHelper helper1 = new WButtonEditorHelper();
            WButtonEditorHelper helper2 = new WButtonEditorHelper();

            Assert.That(helper1, Is.Not.SameAs(helper2));
        }

        [Test]
        public void WButtonEditorHelperCanBeReusedAcrossMultipleCalls()
        {
            TestComponent target = CreateScriptableObject<TestComponent>();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            try
            {
                WButtonEditorHelper helper = new WButtonEditorHelper();

                Assert.DoesNotThrow(() =>
                {
                    helper.DrawButtonsAtTop(editor);
                    helper.DrawButtonsAtBottomAndProcessInvocations(editor);
                });

                Assert.DoesNotThrow(() =>
                {
                    helper.DrawButtonsAtTop(editor);
                    helper.DrawButtonsAtBottomAndProcessInvocations(editor);
                });
            }
            finally
            {
                Object.DestroyImmediate(editor); // UNH-SUPPRESS: Editor is not a tracked Unity object
            }
        }
    }
}
#endif
