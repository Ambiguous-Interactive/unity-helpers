// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RestorableEditorGlobalTests
    {
        private float _originalLabelWidth;

        [SetUp]
        public void SetUp()
        {
            _originalLabelWidth = EditorGUIUtility.labelWidth;
        }

        [TearDown]
        public void TearDown()
        {
            EditorGUIUtility.labelWidth = _originalLabelWidth;
        }

        [Test]
        public void LabelWidthScopesDisposedOutOfOrderRestoreTheOriginalValue()
        {
            EditorGUIUtility.labelWidth = 40f;
            RestorableEditorGlobal<float>.Scope outer = EditorGlobalScopes.LabelWidth.Acquire(80f);
            RestorableEditorGlobal<float>.Scope inner = EditorGlobalScopes.LabelWidth.Acquire(120f);

            outer.Dispose();
            Assert.AreEqual(120f, EditorGUIUtility.labelWidth);

            inner.Dispose();
            Assert.AreEqual(40f, EditorGUIUtility.labelWidth);
        }

        [Test]
        public void ADisposedCopyCannotReapplyAStaleGlobalValue()
        {
            EditorGUIUtility.labelWidth = 30f;
            RestorableEditorGlobal<float>.Scope scope = EditorGlobalScopes.LabelWidth.Acquire(60f);
            RestorableEditorGlobal<float>.Scope copy = scope;

            scope.Dispose();
            EditorGUIUtility.labelWidth = 90f;
            copy.Dispose();

            Assert.AreEqual(90f, EditorGUIUtility.labelWidth);
        }

        [Test]
        public void APackageScopeRestoresAnInterleavedExternalValue()
        {
            EditorGUIUtility.labelWidth = 20f;
            RestorableEditorGlobal<float>.Scope outer = EditorGlobalScopes.LabelWidth.Acquire(40f);
            EditorGUIUtility.labelWidth = 60f;
            RestorableEditorGlobal<float>.Scope inner = EditorGlobalScopes.LabelWidth.Acquire(80f);

            inner.Dispose();
            Assert.AreEqual(60f, EditorGUIUtility.labelWidth);

            EditorGUIUtility.labelWidth = 40f;
            outer.Dispose();
            Assert.AreEqual(20f, EditorGUIUtility.labelWidth);
        }

        [Test]
        public void AStaleCopyCannotReleaseAScopeThatReusesItsSlot()
        {
            EditorGUIUtility.labelWidth = 25f;
            RestorableEditorGlobal<float>.Scope original = EditorGlobalScopes.LabelWidth.Acquire(
                50f
            );
            RestorableEditorGlobal<float>.Scope stale = original;
            original.Dispose();

            RestorableEditorGlobal<float>.Scope replacement = EditorGlobalScopes.LabelWidth.Acquire(
                75f
            );
            stale.Dispose();
            Assert.AreEqual(75f, EditorGUIUtility.labelWidth);

            replacement.Dispose();
            Assert.AreEqual(25f, EditorGUIUtility.labelWidth);
        }

        [Test]
        public void MoreThanFourNestedScopesRestoreAfterOutOfOrderDisposal()
        {
            EditorGUIUtility.labelWidth = 10f;
            RestorableEditorGlobal<float>.Scope first = EditorGlobalScopes.LabelWidth.Acquire(20f);
            RestorableEditorGlobal<float>.Scope second = EditorGlobalScopes.LabelWidth.Acquire(30f);
            RestorableEditorGlobal<float>.Scope third = EditorGlobalScopes.LabelWidth.Acquire(40f);
            RestorableEditorGlobal<float>.Scope fourth = EditorGlobalScopes.LabelWidth.Acquire(50f);
            RestorableEditorGlobal<float>.Scope fifth = EditorGlobalScopes.LabelWidth.Acquire(60f);

            third.Dispose();
            first.Dispose();
            fourth.Dispose();
            second.Dispose();
            Assert.AreEqual(60f, EditorGUIUtility.labelWidth);

            fifth.Dispose();
            Assert.AreEqual(10f, EditorGUIUtility.labelWidth);
        }
    }
#endif
}
