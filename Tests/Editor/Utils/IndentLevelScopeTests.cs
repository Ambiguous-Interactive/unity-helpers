// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class IndentLevelScopeTests
    {
        private int _originalIndentLevel;

        [SetUp]
        public void SetUp()
        {
            _originalIndentLevel = EditorGUI.indentLevel;
        }

        [TearDown]
        public void TearDown()
        {
            EditorGUI.indentLevel = _originalIndentLevel;
        }

        [Test]
        public void IndentRaisesTheLevelForTheBodyAndRestoresAfterwards()
        {
            EditorGUI.indentLevel = 3;

            int inside;
            using (IndentLevelScope.Indent())
            {
                inside = EditorGUI.indentLevel;
            }

            Assert.AreEqual(4, inside, "Indent should raise the level inside the scope.");
            Assert.AreEqual(3, EditorGUI.indentLevel, "The level should be restored on exit.");
        }

        [Test]
        public void AtLevelSetsAnAbsoluteLevelAndRestoresAfterwards()
        {
            EditorGUI.indentLevel = 5;

            int inside;
            using (IndentLevelScope.AtLevel(0))
            {
                inside = EditorGUI.indentLevel;
            }

            Assert.AreEqual(0, inside, "AtLevel should set the requested level.");
            Assert.AreEqual(5, EditorGUI.indentLevel, "The level should be restored on exit.");
        }

        // The whole point of the scope. An IMGUI body throwing is ordinary rather than exceptional:
        // Unity unwinds a drawer with GUIUtility.ExitGUI() whenever a control opens an object picker,
        // and a hand-written indentLevel++/-- pair leaks its increment onto every property drawn
        // afterwards.
        [Test]
        public void TheLevelIsRestoredWhenTheBodyThrows()
        {
            EditorGUI.indentLevel = 2;

            Assert.Throws<InvalidOperationException>(() =>
            {
                using (IndentLevelScope.Indent())
                {
                    throw new InvalidOperationException("drawer failed");
                }
            });

            Assert.AreEqual(
                2,
                EditorGUI.indentLevel,
                "A throwing body must not leave the Inspector indented."
            );
        }

        // Restoring the saved level rather than decrementing is what heals a nested drawer that
        // leaked an increment of its own -- decrementing would restore it to the wrong level.
        [Test]
        public void ALeakInsideTheBodyIsHealedOnExit()
        {
            EditorGUI.indentLevel = 1;

            using (IndentLevelScope.Indent())
            {
                EditorGUI.indentLevel += 3;
            }

            Assert.AreEqual(
                1,
                EditorGUI.indentLevel,
                "Disposal should restore the entry level, not undo a single increment."
            );
        }

        [Test]
        public void NestedScopesUnwindToTheOutermostEntryLevel()
        {
            EditorGUI.indentLevel = 0;

            using (IndentLevelScope.Indent())
            {
                using (IndentLevelScope.Indent(2))
                {
                    Assert.AreEqual(3, EditorGUI.indentLevel);
                }

                Assert.AreEqual(1, EditorGUI.indentLevel);
            }

            Assert.AreEqual(0, EditorGUI.indentLevel);
        }

        [Test]
        public void TheLevelNeverGoesBelowZero()
        {
            EditorGUI.indentLevel = 0;

            using (IndentLevelScope.Indent(-4))
            {
                Assert.AreEqual(0, EditorGUI.indentLevel);
            }

            Assert.AreEqual(0, EditorGUI.indentLevel);
        }

        // A copy of the struct carries a copy of any plain field, so without a disposal lease each
        // copy would restore on its own -- and a copy disposed early would un-indent the rest of a
        // still-open scope's body.
        [Test]
        public void ACopyDisposedFirstDoesNotRestoreTheLevelEarly()
        {
            EditorGUI.indentLevel = 2;

            IndentLevelScope scope = IndentLevelScope.Indent();
            IndentLevelScope copy = scope;

            copy.Dispose();
            Assert.AreEqual(
                2,
                EditorGUI.indentLevel,
                "The first disposal, from whichever copy, is the one that restores."
            );

            EditorGUI.indentLevel = 7;
            scope.Dispose();
            Assert.AreEqual(
                7,
                EditorGUI.indentLevel,
                "A second disposal must not restore again and clobber the current level."
            );
        }
    }
#endif
}
