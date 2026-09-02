// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EditorGlobalScopesTests
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
            RestorableGlobal<float>.Scope outer = EditorGlobalScopes.LabelWidth.Borrow(80f);
            RestorableGlobal<float>.Scope inner = EditorGlobalScopes.LabelWidth.Borrow(120f);

            outer.Dispose();
            Assert.AreEqual(120f, EditorGUIUtility.labelWidth);

            inner.Dispose();
            Assert.AreEqual(40f, EditorGUIUtility.labelWidth);
        }

        [Test]
        public void ADisposedCopyCannotReapplyAStaleGlobalValue()
        {
            EditorGUIUtility.labelWidth = 30f;
            RestorableGlobal<float>.Scope scope = EditorGlobalScopes.LabelWidth.Borrow(60f);
            RestorableGlobal<float>.Scope copy = scope;

            scope.Dispose();
            EditorGUIUtility.labelWidth = 90f;
            copy.Dispose();

            Assert.AreEqual(90f, EditorGUIUtility.labelWidth);
        }

        [Test]
        public void APackageScopeRestoresAnInterleavedExternalValue()
        {
            EditorGUIUtility.labelWidth = 20f;
            RestorableGlobal<float>.Scope outer = EditorGlobalScopes.LabelWidth.Borrow(40f);
            EditorGUIUtility.labelWidth = 60f;
            RestorableGlobal<float>.Scope inner = EditorGlobalScopes.LabelWidth.Borrow(80f);

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
            RestorableGlobal<float>.Scope original = EditorGlobalScopes.LabelWidth.Borrow(50f);
            RestorableGlobal<float>.Scope stale = original;
            original.Dispose();

            RestorableGlobal<float>.Scope replacement = EditorGlobalScopes.LabelWidth.Borrow(75f);
            stale.Dispose();
            Assert.AreEqual(75f, EditorGUIUtility.labelWidth);

            replacement.Dispose();
            Assert.AreEqual(25f, EditorGUIUtility.labelWidth);
        }

        [Test]
        public void MoreThanFourNestedScopesRestoreAfterOutOfOrderDisposal()
        {
            EditorGUIUtility.labelWidth = 10f;
            RestorableGlobal<float>.Scope first = EditorGlobalScopes.LabelWidth.Borrow(20f);
            RestorableGlobal<float>.Scope second = EditorGlobalScopes.LabelWidth.Borrow(30f);
            RestorableGlobal<float>.Scope third = EditorGlobalScopes.LabelWidth.Borrow(40f);
            RestorableGlobal<float>.Scope fourth = EditorGlobalScopes.LabelWidth.Borrow(50f);
            RestorableGlobal<float>.Scope fifth = EditorGlobalScopes.LabelWidth.Borrow(60f);

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
