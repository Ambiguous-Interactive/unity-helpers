// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using Sirenix.OdinInspector.Editor;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ReadOnly;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.ReadOnly;

    /// <summary>
    /// Tests for WReadOnlyOdinDrawer ensuring WReadOnly attributes work correctly
    /// with Odin Inspector for SerializedMonoBehaviour and SerializedScriptableObject types.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WReadOnlyOdinDrawerTests : CommonTestBase
    {
        [Test]
        public void DrawerTypeExistsAndIsPublic()
        {
            Type drawerType = typeof(WReadOnlyOdinDrawer);

            Assert.That(drawerType, Is.Not.Null, "WReadOnlyOdinDrawer type should exist");
            Assert.That(
                drawerType.IsPublic,
                Is.True,
                "WReadOnlyOdinDrawer should be public for Odin to discover it"
            );
        }

        [Test]
        public void DrawerInheritsFromOdinAttributeDrawer()
        {
            Type drawerType = typeof(WReadOnlyOdinDrawer);
            Type expectedBaseType = typeof(OdinAttributeDrawer<WReadOnlyAttribute>);

            Assert.That(
                drawerType.BaseType,
                Is.EqualTo(expectedBaseType),
                "WReadOnlyOdinDrawer should inherit from OdinAttributeDrawer<WReadOnlyAttribute>"
            );
        }

        [Test]
        public void DrawerIsSealed()
        {
            Type drawerType = typeof(WReadOnlyOdinDrawer);

            Assert.That(
                drawerType.IsSealed,
                Is.True,
                "WReadOnlyOdinDrawer should be sealed for performance"
            );
        }

        [Test]
        public void DrawerRegistrationForScriptableObjectIsCorrect()
        {
            OdinReadOnlyScriptableObjectTarget target =
                CreateScriptableObject<OdinReadOnlyScriptableObjectTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for target");
        }

        [Test]
        public void DrawerRegistrationForMonoBehaviourIsCorrect()
        {
            OdinReadOnlyMonoBehaviourTarget target = NewGameObject("ReadOnlyMB")
                .AddComponent<OdinReadOnlyMonoBehaviourTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for MonoBehaviour target");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForScriptableObject()
        {
            OdinReadOnlyScriptableObjectTarget target =
                CreateScriptableObject<OdinReadOnlyScriptableObjectTarget>();
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for ScriptableObject. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForMonoBehaviour()
        {
            OdinReadOnlyMonoBehaviourTarget target = NewGameObject("ReadOnlyMB")
                .AddComponent<OdinReadOnlyMonoBehaviourTarget>();
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for MonoBehaviour. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator StringFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyStringTarget target = CreateScriptableObject<OdinReadOnlyStringTarget>();
            target.readOnlyString = "Test Value";
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only string field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator IntFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyIntTarget target = CreateScriptableObject<OdinReadOnlyIntTarget>();
            target.readOnlyInt = 42;
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only int field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator FloatFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyFloatTarget target = CreateScriptableObject<OdinReadOnlyFloatTarget>();
            target.readOnlyFloat = 3.14f;
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only float field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ObjectReferenceFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyObjectReferenceTarget target =
                CreateScriptableObject<OdinReadOnlyObjectReferenceTarget>();
            target.readOnlyObject = CreateScriptableObject<OdinReferencedObject>();
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only object reference field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NullObjectReferenceFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyObjectReferenceTarget target =
                CreateScriptableObject<OdinReadOnlyObjectReferenceTarget>();
            target.readOnlyObject = null;
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for null read-only object reference field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator BoolFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyBoolTarget target = CreateScriptableObject<OdinReadOnlyBoolTarget>();
            target.readOnlyBool = true;
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only bool field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator VectorFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyVectorTarget target = CreateScriptableObject<OdinReadOnlyVectorTarget>();
            target.readOnlyVector = new Vector3(1f, 2f, 3f);
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only Vector3 field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ColorFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyColorTarget target = CreateScriptableObject<OdinReadOnlyColorTarget>();
            target.readOnlyColor = Color.red;
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only Color field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator EnumFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyEnumTarget target = CreateScriptableObject<OdinReadOnlyEnumTarget>();
            target.readOnlyEnum = OdinTestEnum.Value2;
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only enum field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ArrayFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyArrayTarget target = CreateScriptableObject<OdinReadOnlyArrayTarget>();
            target.readOnlyArray = new[] { 1, 2, 3 };
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only array field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ListFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyListTarget target = CreateScriptableObject<OdinReadOnlyListTarget>();
            target.readOnlyList = new List<string> { "a", "b", "c" };
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only list field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator MultipleReadOnlyFieldsDoNotThrow()
        {
            OdinReadOnlyMultipleFieldsTarget target =
                CreateScriptableObject<OdinReadOnlyMultipleFieldsTarget>();
            target.readOnlyInt = 100;
            target.readOnlyString = "Test";
            target.readOnlyFloat = 1.5f;
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for multiple read-only fields. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator MixedReadOnlyAndEditableFieldsDoNotThrow()
        {
            OdinReadOnlyMixedFieldsTarget target =
                CreateScriptableObject<OdinReadOnlyMixedFieldsTarget>();
            target.readOnlyField = "Read Only";
            target.editableField = "Editable";
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for mixed read-only and editable fields. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator GuiEnabledIsRestoredAfterDrawing()
        {
            bool initialGuiEnabled = GUI.enabled;
            OdinReadOnlyScriptableObjectTarget target =
                CreateScriptableObject<OdinReadOnlyScriptableObjectTarget>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;
            bool guiEnabledAfterDraw = false;

            try
            {
                yield return TestIMGUIExecutor.Run(() =>
                {
                    try
                    {
                        GUI.enabled = true;
                        editor.OnInspectorGUI();
                        guiEnabledAfterDraw = GUI.enabled;
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
                Assert.That(
                    guiEnabledAfterDraw,
                    Is.True,
                    "GUI.enabled should be restored to true after drawing"
                );
            }
            finally
            {
                GUI.enabled = initialGuiEnabled;
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        [UnityTest]
        public IEnumerator GuiEnabledIsRestoredWhenInitiallyFalse()
        {
            bool initialGuiEnabled = GUI.enabled;
            OdinReadOnlyScriptableObjectTarget target =
                CreateScriptableObject<OdinReadOnlyScriptableObjectTarget>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;
            bool guiEnabledAfterDraw = true;

            try
            {
                yield return TestIMGUIExecutor.Run(() =>
                {
                    try
                    {
                        GUI.enabled = false;
                        editor.OnInspectorGUI();
                        guiEnabledAfterDraw = GUI.enabled;
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
                Assert.That(
                    guiEnabledAfterDraw,
                    Is.False,
                    "GUI.enabled should be restored to false when it was initially false"
                );
            }
            finally
            {
                GUI.enabled = initialGuiEnabled;
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        [UnityTest]
        public IEnumerator SerializableClassFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlySerializableClassTarget target =
                CreateScriptableObject<OdinReadOnlySerializableClassTarget>();
            target.readOnlyClass = new OdinTestSerializableClass
            {
                intValue = 10,
                stringValue = "Test",
            };
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only serializable class field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator StructFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyStructTarget target = CreateScriptableObject<OdinReadOnlyStructTarget>();
            target.readOnlyRect = new Rect(0, 0, 100, 100);
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only struct field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator DoubleFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyDoubleTarget target = CreateScriptableObject<OdinReadOnlyDoubleTarget>();
            target.readOnlyDouble = 3.14159265358979;
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only double field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator LongFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyLongTarget target = CreateScriptableObject<OdinReadOnlyLongTarget>();
            target.readOnlyLong = 9223372036854775807L;
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only long field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator AnimationCurveFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyAnimationCurveTarget target =
                CreateScriptableObject<OdinReadOnlyAnimationCurveTarget>();
            target.readOnlyCurve = AnimationCurve.Linear(0, 0, 1, 1);
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only AnimationCurve field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator GradientFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyGradientTarget target =
                CreateScriptableObject<OdinReadOnlyGradientTarget>();
            target.readOnlyGradient = new Gradient();
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only Gradient field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator LayerMaskFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyLayerMaskTarget target =
                CreateScriptableObject<OdinReadOnlyLayerMaskTarget>();
            target.readOnlyLayerMask = 1;
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only LayerMask field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator QuaternionFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyQuaternionTarget target =
                CreateScriptableObject<OdinReadOnlyQuaternionTarget>();
            target.readOnlyQuaternion = Quaternion.identity;
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only Quaternion field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator BoundsFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyBoundsTarget target = CreateScriptableObject<OdinReadOnlyBoundsTarget>();
            target.readOnlyBounds = new Bounds(Vector3.zero, Vector3.one);
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only Bounds field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator Vector2FieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyVector2Target target = CreateScriptableObject<OdinReadOnlyVector2Target>();
            target.readOnlyVector2 = new Vector2(1f, 2f);
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only Vector2 field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator Vector4FieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyVector4Target target = CreateScriptableObject<OdinReadOnlyVector4Target>();
            target.readOnlyVector4 = new Vector4(1f, 2f, 3f, 4f);
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only Vector4 field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator Vector2IntFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyVector2IntTarget target =
                CreateScriptableObject<OdinReadOnlyVector2IntTarget>();
            target.readOnlyVector2Int = new Vector2Int(1, 2);
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only Vector2Int field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator Vector3IntFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyVector3IntTarget target =
                CreateScriptableObject<OdinReadOnlyVector3IntTarget>();
            target.readOnlyVector3Int = new Vector3Int(1, 2, 3);
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only Vector3Int field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator DictionaryFieldWithReadOnlyAttributeDoesNotThrow()
        {
            OdinReadOnlyDictionaryTarget target =
                CreateScriptableObject<OdinReadOnlyDictionaryTarget>();
            target.readOnlyDictionary = new Dictionary<string, int> { { "one", 1 }, { "two", 2 } };
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only dictionary field. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ReadOnlyWithOtherOdinAttributesDoesNotThrow()
        {
            OdinReadOnlyWithOtherAttributesTarget target =
                CreateScriptableObject<OdinReadOnlyWithOtherAttributesTarget>();
            target.readOnlyWithRange = 50;
            target.readOnlyWithTooltip = "Tooltip text";
            Editor editor = Track(Editor.CreateEditor(target));
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
                $"OnInspectorGUI should not throw for read-only fields with other Odin attributes. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }
    }
#endif
}
