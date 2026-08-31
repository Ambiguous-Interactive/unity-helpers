// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor
{
#if UNITY_EDITOR
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Eight editor windows build a <see cref="SerializedObject"/> over themselves in
    /// <c>OnEnable</c> and none of them released it, so every open-and-close leaked one native
    /// object and left the window's cached <see cref="SerializedProperty"/> fields pointing into it
    /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/641">#641</see>).
    /// </summary>
    /// <remarks>
    /// <para>Using a disposed <see cref="SerializedObject"/> throws, and <b>which</b> exception it
    /// throws is the editor's business, not this suite's: measured
    /// <see cref="NullReferenceException"/> on 6000.4.6f1 and
    /// <c>ArgumentNullException: Value cannot be null. Parameter name: _unity_self</c> on
    /// 2022.3.45f1. Pinning the type passed locally and failed CI, so the assertion is that it
    /// throws at all.</para>
    /// <para>That throw is what separates "the field was nulled" from "the native object was
    /// released", so the suite asserts both.</para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EditorWindowSerializedStateTests : CommonTestBase
    {
        [Test]
        public void EveryWindowReleasesItsSerializedObjectOnTeardown()
        {
            AssertSerializedStateLifecycle<ImageBlurTool>(window =>
                window.SerializedStateForTesting
            );
            AssertSerializedStateLifecycle<SpriteSettingsApplierWindow>(window =>
                window.SerializedStateForTesting
            );
            AssertSerializedStateLifecycle<TextureSettingsApplierWindow>(window =>
                window.SerializedStateForTesting
            );
            AssertSerializedStateLifecycle<AnimationCopierWindow>(window =>
                window.SerializedStateForTesting
            );
            AssertSerializedStateLifecycle<SpriteCropper>(window =>
                window.SerializedStateForTesting
            );
            AssertSerializedStateLifecycle<AnimationCreatorWindow>(window =>
                window.SerializedStateForTesting
            );
            AssertSerializedStateLifecycle<SpritePivotAdjuster>(window =>
                window.SerializedStateForTesting
            );
            AssertSerializedStateLifecycle<SpriteSheetExtractor>(window =>
                window.SerializedStateForTesting
            );
        }

        private void AssertSerializedStateLifecycle<TWindow>(
            Func<TWindow, SerializedObject> readSerializedState
        )
            where TWindow : EditorWindow
        {
            string name = typeof(TWindow).Name;

            TWindow window = Track(ScriptableObject.CreateInstance<TWindow>());
            SerializedObject bound = readSerializedState(window);
            Assert.IsTrue(bound != null, "{0} bound no serialized object on enable", name);
            bound.Update();

            Object.DestroyImmediate(window); // UNH-SUPPRESS: teardown is the subject

            Assert.IsTrue(
                readSerializedState(window) == null,
                "{0} still owns a serialized object after teardown",
                name
            );
            Assert.Catch(
                () => bound.Update(),
                "{0} left its serialized object alive after teardown",
                name
            );

            TWindow reopened = Track(ScriptableObject.CreateInstance<TWindow>());
            SerializedObject rebound = readSerializedState(reopened);
            Assert.IsTrue(rebound != null, "{0} did not rebind when reopened", name);
            Assert.AreNotSame(bound, rebound, "{0} reused the released serialized object", name);
            rebound.Update();

            Object.DestroyImmediate(reopened); // UNH-SUPPRESS: teardown is the subject
            Assert.Catch(
                () => rebound.Update(),
                "{0} left its second serialized object alive after teardown",
                name
            );
        }
    }
#endif
}
