// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor
{
#if UNITY_EDITOR
    using System.Collections;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WEnumToggleButtonsDrawerTests : CommonTestBase
    {
        private static void AssertColorApproximately(Color expected, Color actual)
        {
            const float Tolerance = 0.0001f;
            Assert.AreEqual(expected.r, actual.r, Tolerance, "Unexpected red channel.");
            Assert.AreEqual(expected.g, actual.g, Tolerance, "Unexpected green channel.");
            Assert.AreEqual(expected.b, actual.b, Tolerance, "Unexpected blue channel.");
            Assert.AreEqual(expected.a, actual.a, Tolerance, "Unexpected alpha channel.");
        }

        private static void ConfigureDrawer(
            WEnumToggleButtonsDrawer drawer,
            FieldInfo fieldInfo,
            WEnumToggleButtonsAttribute toggleAttribute
        )
        {
            PropertyDrawerTestHelper.ConfigureDrawer(drawer, fieldInfo, toggleAttribute);
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            WEnumToggleButtonsPagination.Reset();
        }

        [UnityTest]
        public IEnumerator DrawPaginationPreservesGuiEnabledState()
        {
            bool assertionMade = false;
            yield return TestIMGUIExecutor.Run(() =>
            {
                WEnumToggleButtonsPagination.PaginationState state = new()
                {
                    PageSize = 5,
                    TotalItems = 10,
                    PageIndex = 0,
                };

                Rect rect = new(0f, 0f, 200f, EditorGUIUtility.singleLineHeight);

                bool originalEnabled = GUI.enabled;
                GUI.enabled = true;
                try
                {
                    WEnumToggleButtonsDrawer.DrawPagination(rect, state);
                    Assert.IsTrue(
                        GUI.enabled,
                        "DrawPagination leaked GUI.enabled=false to subsequent controls."
                    );
                    assertionMade = true;
                }
                finally
                {
                    GUI.enabled = originalEnabled;
                }
            });
            Assert.IsTrue(assertionMade);
        }

        [Test]
        public void ResolveWEnumToggleButtonsPaletteReturnsExpectedDefaults()
        {
            Color expectedSelected = new(0.243f, 0.525f, 0.988f, 1f);
            Color expectedLightInactive = new(0.78f, 0.78f, 0.78f, 1f);
            Color expectedDarkInactive = new(0.35f, 0.35f, 0.35f, 1f);

            UnityHelpersSettings.WEnumToggleButtonsPaletteEntry lightPalette =
                UnityHelpersSettings.ResolveWEnumToggleButtonsPalette(
                    UnityHelpersSettings.WEnumToggleButtonsLightThemeColorKey
                );
            UnityHelpersSettings.WEnumToggleButtonsPaletteEntry darkPalette =
                UnityHelpersSettings.ResolveWEnumToggleButtonsPalette(
                    UnityHelpersSettings.WEnumToggleButtonsDarkThemeColorKey
                );
            UnityHelpersSettings.WEnumToggleButtonsPaletteEntry defaultPalette =
                UnityHelpersSettings.ResolveWEnumToggleButtonsPalette(
                    UnityHelpersSettings.DefaultWEnumToggleButtonsColorKey
                );

            AssertColorApproximately(expectedSelected, lightPalette.SelectedBackgroundColor);
            AssertColorApproximately(expectedLightInactive, lightPalette.InactiveBackgroundColor);
            AssertColorApproximately(Color.white, lightPalette.SelectedTextColor);
            AssertColorApproximately(Color.black, lightPalette.InactiveTextColor);

            AssertColorApproximately(expectedSelected, darkPalette.SelectedBackgroundColor);
            AssertColorApproximately(expectedDarkInactive, darkPalette.InactiveBackgroundColor);
            AssertColorApproximately(Color.white, darkPalette.SelectedTextColor);
            AssertColorApproximately(Color.white, darkPalette.InactiveTextColor);

            UnityHelpersSettings.WEnumToggleButtonsPaletteEntry expectedDefaultPalette =
                EditorGUIUtility.isProSkin ? darkPalette : lightPalette;

            AssertColorApproximately(
                expectedDefaultPalette.SelectedBackgroundColor,
                defaultPalette.SelectedBackgroundColor
            );
            AssertColorApproximately(
                expectedDefaultPalette.SelectedTextColor,
                defaultPalette.SelectedTextColor
            );
            AssertColorApproximately(
                expectedDefaultPalette.InactiveBackgroundColor,
                defaultPalette.InactiveBackgroundColor
            );
            AssertColorApproximately(
                expectedDefaultPalette.InactiveTextColor,
                defaultPalette.InactiveTextColor
            );
        }

        [UnityTest]
        public IEnumerator GetPropertyHeightUsesCachedLayoutWhenAvailable()
        {
            ToggleDropDownAsset asset = CreateScriptableObject<ToggleDropDownAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ToggleDropDownAsset.mode)
            );
            Assert.IsTrue(property != null, "Failed to locate serialized property for test asset.");

            FieldInfo fieldInfo = typeof(ToggleDropDownAsset).GetField(
                nameof(ToggleDropDownAsset.mode),
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.IsTrue(fieldInfo != null, "Failed to locate field info for test asset.");

            WEnumToggleButtonsAttribute toggleAttribute =
                fieldInfo.GetCustomAttribute<WEnumToggleButtonsAttribute>();
            Assert.IsTrue(
                toggleAttribute != null,
                "Expected WEnumToggleButtonsAttribute on test field."
            );

            WEnumToggleButtonsDrawer drawer = new();
            ConfigureDrawer(drawer, fieldInfo, toggleAttribute);

            GUIContent label = new("Mode");

            bool assertionMade = false;
            yield return TestIMGUIExecutor.Run(() =>
            {
                float baselineHeight = drawer.GetPropertyHeight(property, label);
                Assert.Greater(baselineHeight, 0f, "Baseline height should be positive.");

                Rect position = new(0f, 0f, 400f, baselineHeight + 40f);
                drawer.OnGUI(position, property, label);

                float cachedHeight = drawer.GetPropertyHeight(property, label);
                Assert.AreEqual(
                    baselineHeight,
                    cachedHeight,
                    "Drawer should reuse cached layout height when signature matches."
                );

                assertionMade = true;
            });

            Assert.IsTrue(assertionMade);
        }

        [Test]
        public void CreateToggleSetHandlesMissingFieldInfo()
        {
            ToggleDropDownAsset asset = CreateScriptableObject<ToggleDropDownAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ToggleDropDownAsset.mode)
            );
            Assert.IsTrue(property != null, "Failed to locate serialized property for test asset.");

            ToggleSet toggleSet = WEnumToggleButtonsUtility.CreateToggleSet(
                property,
                fieldInfo: null
            );
            Assert.IsFalse(
                toggleSet.IsEmpty,
                "Toggle set should be created even when FieldInfo is unavailable."
            );
        }

        [UnityTest]
        public IEnumerator GetPropertyHeightIgnoresExternalIndentation()
        {
            ToggleDropDownAsset asset = CreateScriptableObject<ToggleDropDownAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ToggleDropDownAsset.mode)
            );
            Assert.IsTrue(property != null, "Failed to locate serialized property for test asset.");

            FieldInfo fieldInfo = typeof(ToggleDropDownAsset).GetField(
                nameof(ToggleDropDownAsset.mode),
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.IsTrue(fieldInfo != null);

            WEnumToggleButtonsAttribute toggleAttribute =
                fieldInfo.GetCustomAttribute<WEnumToggleButtonsAttribute>();
            Assert.IsTrue(toggleAttribute != null);

            WEnumToggleButtonsDrawer drawer = new();
            ConfigureDrawer(drawer, fieldInfo, toggleAttribute);

            GUIContent label = new("Mode");

            bool assertionMade = false;
            yield return TestIMGUIExecutor.Run(() =>
            {
                float baseline = drawer.GetPropertyHeight(property, label);

                int previousIndent = EditorGUI.indentLevel;
                try
                {
                    EditorGUI.indentLevel = 5;
                    float indented = drawer.GetPropertyHeight(property, label);
                    Assert.AreEqual(
                        baseline,
                        indented,
                        0.0001f,
                        "Property height should not change due to outer indent levels."
                    );
                    assertionMade = true;
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndent;
                }
            });

            Assert.IsTrue(assertionMade);
        }

        [Test]
        public void GetPropertyHeightRespectsGroupPaddingContext()
        {
            ToggleDropDownAsset asset = CreateScriptableObject<ToggleDropDownAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ToggleDropDownAsset.mode)
            );
            Assert.IsTrue(property != null, "Failed to locate serialized property for test asset.");

            FieldInfo fieldInfo = typeof(ToggleDropDownAsset).GetField(
                nameof(ToggleDropDownAsset.mode),
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.IsTrue(fieldInfo != null, "FieldInfo should be found for mode field");

            WEnumToggleButtonsAttribute toggleAttribute =
                fieldInfo.GetCustomAttribute<WEnumToggleButtonsAttribute>();
            Assert.IsTrue(
                toggleAttribute != null,
                "WEnumToggleButtonsAttribute should be present on mode field"
            );

            WEnumToggleButtonsDrawer drawer = new();
            ConfigureDrawer(drawer, fieldInfo, toggleAttribute);

            GUIContent label = new("Mode");

            float baselineHeight = drawer.GetPropertyHeight(property, label);
            Assert.Greater(baselineHeight, 0f);

            float constrainedHeight;
            using (GroupGUIWidthUtility.PushContentPadding(600f))
            {
                constrainedHeight = drawer.GetPropertyHeight(property, label);
            }

            Assert.Greater(
                constrainedHeight,
                baselineHeight,
                "Additional group padding should reduce usable width and increase height."
            );

            float restoredHeight = drawer.GetPropertyHeight(property, label);
            Assert.AreEqual(
                baselineHeight,
                restoredHeight,
                0.0001f,
                "Padding scope disposal should restore the baseline width estimate."
            );
        }
    }
#endif
}
