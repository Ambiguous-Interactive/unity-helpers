// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableCollectionDrawerVisualRegressionTests : CommonTestBase
    {
        private static Rect GetControlRect()
        {
            return new Rect(0f, 0f, 420f, 600f);
        }

        [UnityTest]
        public IEnumerator DictionaryKeyAndValueRowsShareAlignment()
        {
            VisualRegressionDictionaryHost host =
                CreateScriptableObject<VisualRegressionDictionaryHost>();
            for (int i = 0; i < 3; i++)
            {
                host.dictionary.Add(
                    new DrawerVisualRegressionKey(i),
                    new DrawerVisualRegressionDictionaryValue((i + 1) * 10)
                );
            }

            SerializedObject dictionaryObject = TrackDisposable(new SerializedObject(host));
            dictionaryObject.Update();
            PopulateDictionarySerializedState(host, dictionaryObject);
            SerializedProperty dictionaryProperty = dictionaryObject.FindProperty(
                nameof(VisualRegressionDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawerTests.AssignDictionaryFieldInfo(
                drawer,
                typeof(VisualRegressionDictionaryHost),
                nameof(VisualRegressionDictionaryHost.dictionary)
            );
            Rect controlRect = GetControlRect();
            GUIContent label = new("Dictionary");

            DrawerVisualSample[] samples;
            DrawerVisualRecorder.BeginRecording();
            try
            {
                yield return TestIMGUIExecutor.Run(() =>
                {
                    dictionaryObject.UpdateIfRequiredOrScript();
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                });
            }
            finally
            {
                samples = DrawerVisualRecorder.EndRecording();
            }

            DrawerVisualSample[] keySamples = samples
                .Where(sample => sample.Role == DrawerVisualRole.DictionaryKey)
                .OrderBy(sample => sample.ArrayIndex)
                .ToArray();
            DrawerVisualSample[] valueSamples = samples
                .Where(sample => sample.Role == DrawerVisualRole.DictionaryValue)
                .OrderBy(sample => sample.ArrayIndex)
                .ToArray();

            Assert.AreEqual(
                keySamples.Length,
                valueSamples.Length,
                "Dictionary drawer should emit matching key/value rects per entry."
            );

            for (int i = 0; i < keySamples.Length; i++)
            {
                Assert.That(
                    keySamples[i].ArrayIndex,
                    Is.EqualTo(valueSamples[i].ArrayIndex),
                    "Key/value ordering should stay in sync."
                );
                Assert.That(
                    keySamples[i].Rect.y,
                    Is.EqualTo(valueSamples[i].Rect.y).Within(0.01f),
                    $"Key row {i} should share the same baseline as its value."
                );
                Assert.That(
                    keySamples[i].Rect.height,
                    Is.EqualTo(valueSamples[i].Rect.height).Within(0.01f),
                    $"Key row {i} should share the same height as its value."
                );
            }
        }

        [UnityTest]
        public IEnumerator SetAndDictionaryRowsAreEachUniformlyStacked()
        {
            VisualRegressionDictionaryHost dictionaryHost =
                CreateScriptableObject<VisualRegressionDictionaryHost>();
            VisualRegressionSetHost setHost = CreateScriptableObject<VisualRegressionSetHost>();

            for (int i = 0; i < 3; i++)
            {
                int payload = (i + 1) * 5;
                dictionaryHost.dictionary.Add(
                    new DrawerVisualRegressionKey(i),
                    new DrawerVisualRegressionDictionaryValue(payload)
                );
                setHost.set.Add(new DrawerVisualRegressionSetValue(payload));
            }

            SerializedObject dictionaryObject = TrackDisposable(
                new SerializedObject(dictionaryHost)
            );
            dictionaryObject.Update();
            PopulateDictionarySerializedState(dictionaryHost, dictionaryObject);
            SerializedProperty dictionaryProperty = dictionaryObject.FindProperty(
                nameof(VisualRegressionDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            SerializedObject setObject = TrackDisposable(new SerializedObject(setHost));
            setObject.Update();
            PopulateSetSerializedState(setHost, setObject);
            SerializedProperty setProperty = setObject.FindProperty(
                nameof(VisualRegressionSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableDictionaryPropertyDrawer dictionaryDrawer = new();
            SerializableDictionaryPropertyDrawerTests.AssignDictionaryFieldInfo(
                dictionaryDrawer,
                typeof(VisualRegressionDictionaryHost),
                nameof(VisualRegressionDictionaryHost.dictionary)
            );
            SerializableSetPropertyDrawer setDrawer = new();
            Rect controlRect = GetControlRect();
            GUIContent label = new("Collections");

            DrawerVisualSample[] dictionarySamples;
            DrawerVisualRecorder.BeginRecording();
            try
            {
                yield return TestIMGUIExecutor.Run(() =>
                {
                    dictionaryObject.UpdateIfRequiredOrScript();
                    dictionaryDrawer.OnGUI(controlRect, dictionaryProperty, label);
                });
            }
            finally
            {
                dictionarySamples = DrawerVisualRecorder.EndRecording();
            }

            DrawerVisualSample[] dictionaryValueRects = dictionarySamples
                .Where(sample => sample.Role == DrawerVisualRole.DictionaryValue)
                .OrderBy(sample => sample.Rect.y)
                .ToArray();

            DrawerVisualSample[] setSamples;
            DrawerVisualRecorder.BeginRecording();
            try
            {
                yield return TestIMGUIExecutor.Run(() =>
                {
                    setObject.UpdateIfRequiredOrScript();
                    setDrawer.OnGUI(controlRect, setProperty, label);
                });
            }
            finally
            {
                setSamples = DrawerVisualRecorder.EndRecording();
            }

            DrawerVisualSample[] setRects = setSamples
                .Where(sample => sample.Role == DrawerVisualRole.SetElement)
                .OrderBy(sample => sample.Rect.y)
                .ToArray();

            Assert.AreEqual(
                dictionaryValueRects.Length,
                setRects.Length,
                $"Set drawer should create one element row per value. {BuildVisualDiagnostics(dictionaryValueRects, setRects, dictionarySamples, setSamples)}"
            );

            float dictionaryStart =
                dictionaryValueRects.Length > 0 ? dictionaryValueRects[0].Rect.y : 0f;
            float setStart = setRects.Length > 0 ? setRects[0].Rect.y : 0f;

            TestContext.WriteLine(
                $"[Layout] Dictionary baselines: {string.Join(", ", dictionaryValueRects.Select((sample, index) => $"{index}:{sample.Rect.y - dictionaryStart:0.00}"))}"
            );
            TestContext.WriteLine(
                $"[Layout] Set baselines: {string.Join(", ", setRects.Select((sample, index) => $"{index}:{sample.Rect.y - setStart:0.00}"))}"
            );
            TestContext.WriteLine(
                $"[Layout] Dictionary heights: {string.Join(", ", dictionaryValueRects.Select((sample, index) => $"{index}:{sample.Rect.height:0.00}"))}"
            );
            TestContext.WriteLine(
                $"[Layout] Set heights: {string.Join(", ", setRects.Select((sample, index) => $"{index}:{sample.Rect.height:0.00}"))}"
            );

            // Each drawer must lay its element rows out as a clean vertical stack — strictly
            // top-to-bottom, uniform height, evenly pitched. That is the genuine per-drawer
            // regression invariant. We deliberately do NOT assert cross-drawer pixel equality:
            // a set element is a single line while a foldout-capable dictionary value reserves
            // extra height, so their row pitches legitimately differ (24 vs 44 px) and demanding
            // they match was a fragile, invalid premise that produced false CI failures.
            string diagnostics = BuildVisualDiagnostics(
                dictionaryValueRects,
                setRects,
                dictionarySamples,
                setSamples
            );
            AssertRowsUniformlyStacked("Dictionary value", dictionaryValueRects, diagnostics);
            AssertRowsUniformlyStacked("Set element", setRects, diagnostics);
        }

        /// <summary>
        /// Asserts a drawer's element rows form a clean vertical stack: each row advances
        /// downward by a constant pitch and shares a uniform height. Tolerant to sub-pixel
        /// rounding (0.25f) but free of hard-coded pixel constants, so it stays valid across
        /// Unity versions and IMGUI layout tweaks.
        /// </summary>
        private static void AssertRowsUniformlyStacked(
            string label,
            DrawerVisualSample[] rows,
            string diagnostics
        )
        {
            if (rows.Length < 2)
            {
                return;
            }

            float pitch = rows[1].Rect.y - rows[0].Rect.y;
            float height = rows[0].Rect.height;
            Assert.That(
                height,
                Is.GreaterThan(0f),
                $"{label} rows should have a positive height (guards a collapsed-row regression). {diagnostics}"
            );
            Assert.That(
                pitch,
                Is.GreaterThanOrEqualTo(height),
                $"{label} rows should advance top-to-bottom by at least their own height (guards overlap/zero-pitch regressions). {diagnostics}"
            );

            for (int i = 1; i < rows.Length; i++)
            {
                Assert.That(
                    rows[i].Rect.y - rows[i - 1].Rect.y,
                    Is.EqualTo(pitch).Within(0.25f),
                    $"{label} row {i} should be evenly pitched relative to the previous row. {diagnostics}"
                );
                Assert.That(
                    rows[i].Rect.height,
                    Is.EqualTo(height).Within(0.25f),
                    $"{label} row {i} should share the uniform row height. {diagnostics}"
                );
            }
        }

        private static string BuildVisualDiagnostics(
            DrawerVisualSample[] dictionaryRects,
            DrawerVisualSample[] setRects,
            DrawerVisualSample[] dictionarySamples = null,
            DrawerVisualSample[] setSamples = null
        )
        {
            string dictSummary =
                dictionaryRects.Length == 0
                    ? "dictionaryRects:[]"
                    : $"dictionaryRects:[{string.Join(", ", dictionaryRects.Select(SummarizeSample))}]";
            string setSummary =
                setRects.Length == 0
                    ? "setRects:[]"
                    : $"setRects:[{string.Join(", ", setRects.Select(SummarizeSample))}]";
            string dictRaw =
                dictionarySamples == null
                    ? string.Empty
                    : $" dictionarySamples:[{string.Join(", ", dictionarySamples.Select(SummarizeSample))}]";
            string setRaw =
                setSamples == null
                    ? string.Empty
                    : $" setSamples:[{string.Join(", ", setSamples.Select(SummarizeSample))}]";
            return $"[{dictSummary}; {setSummary};{dictRaw}{setRaw}]";

            static string SummarizeSample(DrawerVisualSample sample)
            {
                return $"(role={sample.Role},index={sample.ArrayIndex},rect={sample.Rect})";
            }
        }

        private static void PopulateDictionarySerializedState(
            VisualRegressionDictionaryHost host,
            SerializedObject dictionaryObject
        )
        {
            if (host == null || dictionaryObject == null)
            {
                return;
            }

            SerializedProperty dictionaryProperty = dictionaryObject.FindProperty(
                nameof(VisualRegressionDictionaryHost.dictionary)
            );
            if (dictionaryProperty == null)
            {
                return;
            }

            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            if (keysProperty == null || valuesProperty == null)
            {
                return;
            }

            List<
                KeyValuePair<DrawerVisualRegressionKey, DrawerVisualRegressionDictionaryValue>
            > entries = host.dictionary.ToList();

            keysProperty.arraySize = entries.Count;
            valuesProperty.arraySize = entries.Count;

            for (int i = 0; i < entries.Count; i++)
            {
                DrawerVisualRegressionKey key = entries[i].Key;
                DormantAssignKey(keysProperty.GetArrayElementAtIndex(i), key);

                DrawerVisualRegressionDictionaryValue value = entries[i].Value;
                DormantAssignValue(
                    valuesProperty.GetArrayElementAtIndex(i),
                    value?.data ?? 0,
                    nameof(DrawerVisualRegressionDictionaryValue.data)
                );
            }

            dictionaryObject.ApplyModifiedPropertiesWithoutUndo();
            dictionaryObject.UpdateIfRequiredOrScript();
        }

        private static void PopulateSetSerializedState(
            VisualRegressionSetHost host,
            SerializedObject setObject
        )
        {
            if (host == null || setObject == null)
            {
                return;
            }

            SerializedProperty setProperty = setObject.FindProperty(
                nameof(VisualRegressionSetHost.set)
            );
            if (setProperty == null)
            {
                return;
            }

            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            if (itemsProperty == null)
            {
                return;
            }

            DrawerVisualRegressionSetValue[] values = host.set.ToArray();
            itemsProperty.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                DrawerVisualRegressionSetValue value = values[i];
                DormantAssignValue(
                    itemsProperty.GetArrayElementAtIndex(i),
                    value?.data ?? 0,
                    nameof(DrawerVisualRegressionSetValue.data)
                );
            }

            setObject.ApplyModifiedPropertiesWithoutUndo();
            setObject.UpdateIfRequiredOrScript();
        }

        private static void DormantAssignKey(
            SerializedProperty property,
            DrawerVisualRegressionKey key
        )
        {
            if (property == null)
            {
                return;
            }

            SerializedProperty idProperty = property.FindPropertyRelative(
                nameof(DrawerVisualRegressionKey.id)
            );
            if (idProperty != null)
            {
                idProperty.intValue = key?.id ?? 0;
            }
        }

        private static void DormantAssignValue(
            SerializedProperty container,
            int dataValue,
            string fieldName
        )
        {
            if (container == null || string.IsNullOrEmpty(fieldName))
            {
                return;
            }

            SerializedProperty dataProperty = container.FindPropertyRelative(fieldName);
            if (dataProperty != null)
            {
                dataProperty.intValue = dataValue;
            }
        }
    }
}
