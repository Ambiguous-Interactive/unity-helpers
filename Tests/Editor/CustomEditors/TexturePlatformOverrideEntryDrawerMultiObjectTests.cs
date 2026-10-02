// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.CustomEditors;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using Object = UnityEngine.Object;
    using PlatformPropertyNames = UnityHelpers.Editor.Sprites.TextureSettingsApplierWindow.PlatformOverrideEntry.SerializedPropertyNames;

    [TestFixture]
    [Category("Slow")]
    [Category("Integration")]
    public sealed class TexturePlatformOverrideEntryDrawerMultiObjectTests : CommonTestBase
    {
        [Test]
        public void MultiObjectSamePlatformNameDoesNotShowMixed()
        {
            MultiObjectTexturePlatformOverrideTarget first =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            MultiObjectTexturePlatformOverrideTarget second =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            first.entry.platformName = "Standalone";
            second.entry.platformName = "Standalone";

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectTexturePlatformOverrideTarget.entry)
            );
            Assert.IsTrue(property != null, "Failed to locate entry property.");

            SerializedProperty nameProp = property.FindPropertyRelative(
                PlatformPropertyNames.PlatformName
            );
            Assert.IsTrue(nameProp != null, "Failed to locate platformName property.");

            Assert.IsFalse(
                nameProp.hasMultipleDifferentValues,
                "Multiple objects with the same platform name should NOT have hasMultipleDifferentValues set."
            );
        }

        [Test]
        public void MultiObjectDifferentPlatformNameShowsMixed()
        {
            MultiObjectTexturePlatformOverrideTarget first =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            MultiObjectTexturePlatformOverrideTarget second =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            first.entry.platformName = "Standalone";
            second.entry.platformName = "Android";

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectTexturePlatformOverrideTarget.entry)
            );
            Assert.IsTrue(property != null, "Failed to locate entry property.");

            SerializedProperty nameProp = property.FindPropertyRelative(
                PlatformPropertyNames.PlatformName
            );
            Assert.IsTrue(nameProp != null, "Failed to locate platformName property.");

            Assert.IsTrue(
                nameProp.hasMultipleDifferentValues,
                "Multiple objects with different platform names SHOULD have hasMultipleDifferentValues set."
            );
        }

        [Test]
        public void MultiObjectSameApplyFlagDoesNotShowMixed()
        {
            MultiObjectTexturePlatformOverrideTarget first =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            MultiObjectTexturePlatformOverrideTarget second =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            first.entry.applyFormat = true;
            second.entry.applyFormat = true;

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectTexturePlatformOverrideTarget.entry)
            );
            Assert.IsTrue(property != null, "Failed to locate entry property.");

            SerializedProperty applyProp = property.FindPropertyRelative(
                PlatformPropertyNames.ApplyFormat
            );
            Assert.IsTrue(applyProp != null, "Failed to locate applyFormat property.");

            Assert.IsFalse(
                applyProp.hasMultipleDifferentValues,
                "Multiple objects with the same apply flag should NOT have hasMultipleDifferentValues set."
            );
        }

        [Test]
        public void MultiObjectDifferentApplyFlagShowsMixed()
        {
            MultiObjectTexturePlatformOverrideTarget first =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            MultiObjectTexturePlatformOverrideTarget second =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            first.entry.applyFormat = true;
            second.entry.applyFormat = false;

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectTexturePlatformOverrideTarget.entry)
            );
            Assert.IsTrue(property != null, "Failed to locate entry property.");

            SerializedProperty applyProp = property.FindPropertyRelative(
                PlatformPropertyNames.ApplyFormat
            );
            Assert.IsTrue(applyProp != null, "Failed to locate applyFormat property.");

            Assert.IsTrue(
                applyProp.hasMultipleDifferentValues,
                "Multiple objects with different apply flags SHOULD have hasMultipleDifferentValues set."
            );
        }

        [Test]
        public void MultiObjectPlatformNameChangePropagatesToAllObjects()
        {
            MultiObjectTexturePlatformOverrideTarget first =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            MultiObjectTexturePlatformOverrideTarget second =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            first.entry.platformName = "Standalone";
            second.entry.platformName = "Android";

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectTexturePlatformOverrideTarget.entry)
            );
            SerializedProperty nameProp = property.FindPropertyRelative(
                PlatformPropertyNames.PlatformName
            );
            Assert.IsTrue(
                nameProp.hasMultipleDifferentValues,
                "Precondition: objects should start with different values."
            );

            nameProp.stringValue = "WebGL";
            serializedObject.ApplyModifiedProperties();

            Assert.That(
                first.entry.platformName,
                Is.EqualTo("WebGL"),
                "First object should have been updated to the new value."
            );
            Assert.That(
                second.entry.platformName,
                Is.EqualTo("WebGL"),
                "Second object should have been updated to the new value."
            );

            serializedObject.Update();
            Assert.IsFalse(
                nameProp.hasMultipleDifferentValues,
                "After applying the same value to all targets, hasMultipleDifferentValues should be false."
            );
        }

        [UnityTest]
        public IEnumerator OnGUIWithMixedPlatformNamesDoesNotThrow()
        {
            MultiObjectTexturePlatformOverrideTarget first =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            MultiObjectTexturePlatformOverrideTarget second =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            first.entry.platformName = "Standalone";
            second.entry.platformName = "Android";

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectTexturePlatformOverrideTarget.entry)
            );

            TexturePlatformOverrideEntryDrawer drawer = new();
            float height = drawer.GetPropertyHeight(property, GUIContent.none);
            Rect position = new(0, 0, 400, height);
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    drawer.OnGUI(position, property, GUIContent.none);
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"OnGUI with mixed platform names should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnGUIWithMixedApplyFlagsDoesNotThrow()
        {
            MultiObjectTexturePlatformOverrideTarget first =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            MultiObjectTexturePlatformOverrideTarget second =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            first.entry.platformName = "Standalone";
            second.entry.platformName = "Standalone";
            first.entry.applyResizeAlgorithm = true;
            second.entry.applyResizeAlgorithm = false;
            first.entry.applyFormat = false;
            second.entry.applyFormat = true;

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectTexturePlatformOverrideTarget.entry)
            );

            TexturePlatformOverrideEntryDrawer drawer = new();
            float height = drawer.GetPropertyHeight(property, GUIContent.none);
            Rect position = new(0, 0, 400, height);
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    drawer.OnGUI(position, property, GUIContent.none);
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"OnGUI with mixed apply flags should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnGUIWithMixedValuesDoesNotModifyEitherTarget()
        {
            MultiObjectTexturePlatformOverrideTarget first =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            MultiObjectTexturePlatformOverrideTarget second =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            first.entry.platformName = "Standalone";
            second.entry.platformName = "Android";
            first.entry.applyResizeAlgorithm = true;
            second.entry.applyResizeAlgorithm = false;

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectTexturePlatformOverrideTarget.entry)
            );

            TexturePlatformOverrideEntryDrawer drawer = new();
            float height = drawer.GetPropertyHeight(property, GUIContent.none);
            Rect position = new(0, 0, 400, height);

            yield return TestIMGUIExecutor.Run(() =>
            {
                for (int i = 0; i < 5; ++i)
                {
                    drawer.OnGUI(position, property, GUIContent.none);
                }
            });

            serializedObject.ApplyModifiedProperties();
            Assert.That(
                first.entry.platformName,
                Is.EqualTo("Standalone"),
                "OnGUI should not modify first object's platform name during render"
            );
            Assert.That(
                second.entry.platformName,
                Is.EqualTo("Android"),
                "OnGUI should not modify second object's platform name during render"
            );
            Assert.That(
                first.entry.applyResizeAlgorithm,
                Is.True,
                "OnGUI should not modify first object's apply flag during render"
            );
            Assert.That(
                second.entry.applyResizeAlgorithm,
                Is.False,
                "OnGUI should not modify second object's apply flag during render"
            );
        }

        [UnityTest]
        public IEnumerator RepeatedOnGUICallsWithMixedValuesDoNotDirtySerializedObject()
        {
            MultiObjectTexturePlatformOverrideTarget first =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            MultiObjectTexturePlatformOverrideTarget second =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            first.entry.platformName = "Standalone";
            second.entry.platformName = "Android";

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectTexturePlatformOverrideTarget.entry)
            );

            TexturePlatformOverrideEntryDrawer drawer = new();
            float height = drawer.GetPropertyHeight(property, GUIContent.none);
            Rect position = new(0, 0, 400, height);

            yield return TestIMGUIExecutor.Run(() =>
            {
                for (int i = 0; i < 10; ++i)
                {
                    drawer.OnGUI(position, property, GUIContent.none);
                }
            });

            Assert.That(
                serializedObject.hasModifiedProperties,
                Is.False,
                "Repeated OnGUI calls with mixed values should not dirty the SerializedObject"
            );
        }

        [Test]
        public void MultiObjectEmptyAndKnownPlatformShowsMixed()
        {
            MultiObjectTexturePlatformOverrideTarget first =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            MultiObjectTexturePlatformOverrideTarget second =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            first.entry.platformName = string.Empty;
            second.entry.platformName = "Standalone";

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectTexturePlatformOverrideTarget.entry)
            );
            Assert.IsTrue(property != null, "Failed to locate entry property.");

            SerializedProperty nameProp = property.FindPropertyRelative(
                PlatformPropertyNames.PlatformName
            );
            Assert.IsTrue(nameProp != null, "Failed to locate platformName property.");

            Assert.IsTrue(
                nameProp.hasMultipleDifferentValues,
                "Empty (Custom sentinel) and known platform should have hasMultipleDifferentValues set."
            );
        }

        [UnityTest]
        public IEnumerator OnGUIWithSameValuesMultiObjectDoesNotThrow()
        {
            MultiObjectTexturePlatformOverrideTarget first =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            MultiObjectTexturePlatformOverrideTarget second =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            first.entry.platformName = "Standalone";
            second.entry.platformName = "Standalone";
            first.entry.applyFormat = true;
            second.entry.applyFormat = true;

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectTexturePlatformOverrideTarget.entry)
            );

            TexturePlatformOverrideEntryDrawer drawer = new();
            float height = drawer.GetPropertyHeight(property, GUIContent.none);
            Rect position = new(0, 0, 400, height);
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    drawer.OnGUI(position, property, GUIContent.none);
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"OnGUI with same values multi-object should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnGUIWithMixedCustomAndKnownPlatformDoesNotThrow()
        {
            MultiObjectTexturePlatformOverrideTarget first =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            MultiObjectTexturePlatformOverrideTarget second =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();
            first.entry.platformName = string.Empty;
            second.entry.platformName = "Standalone";

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectTexturePlatformOverrideTarget.entry)
            );

            TexturePlatformOverrideEntryDrawer drawer = new();
            float height = drawer.GetPropertyHeight(property, GUIContent.none);
            Rect position = new(0, 0, 400, height);
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    drawer.OnGUI(position, property, GUIContent.none);
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"OnGUI with mixed Custom/known platform should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [Test]
        public void DefaultPlatformNameIsKnownPlatform()
        {
            MultiObjectTexturePlatformOverrideTarget target =
                CreateScriptableObject<MultiObjectTexturePlatformOverrideTarget>();

            Assert.That(
                target.entry.platformName,
                Is.EqualTo(TexturePlatformNameHelper.DefaultPlatformName),
                "New PlatformOverrideEntry should default to DefaultTexturePlatform, "
                    + "not empty string (which would be treated as Custom and skipped by the API)"
            );
        }
    }
#endif
}
