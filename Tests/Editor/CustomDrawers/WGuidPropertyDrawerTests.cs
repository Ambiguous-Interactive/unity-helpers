// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System;
    using System.Buffers.Binary;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WGuidPropertyDrawerTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            WGuidPropertyDrawer.ClearCachedStates();
        }

        [TearDown]
        public void TearDownDrawerTests()
        {
            WGuidPropertyDrawer.ClearCachedStates();
        }

        [Test]
        public void HandleTextChangeClearsGuidWhenInputEmpty()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            Assert.NotNull(property);
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            WGuidPropertyDrawer.GenerateNewGuid(property, lowProperty, highProperty, state);
            serializedObject.Update();
            Assert.IsFalse(ReadGuid(property).IsEmpty);

            WGuidPropertyDrawer.HandleTextChange(
                property,
                lowProperty,
                highProperty,
                state,
                string.Empty
            );
            serializedObject.Update();

            Assert.IsTrue(ReadGuid(property).IsEmpty);
            Assert.IsFalse(state.hasPendingInvalid);
            Assert.IsEmpty(state.warningMessage);
        }

        [Test]
        public void HandleTextChangeAppliesValidGuid()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            Guid expected = Guid.NewGuid();
            WGuidPropertyDrawer.HandleTextChange(
                property,
                lowProperty,
                highProperty,
                state,
                expected.ToString("D")
            );
            serializedObject.Update();

            WGuid actual = ReadGuid(property);
            Assert.AreEqual(expected, actual.ToGuid());
            Assert.IsFalse(state.hasPendingInvalid);
            Assert.IsEmpty(state.warningMessage);
        }

        [Test]
        public void HandleTextChangeRejectsInvalidGuid()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            WGuidPropertyDrawer.GenerateNewGuid(property, lowProperty, highProperty, state);
            serializedObject.Update();
            long originalLow = lowProperty.longValue;
            long originalHigh = highProperty.longValue;

            WGuidPropertyDrawer.HandleTextChange(
                property,
                lowProperty,
                highProperty,
                state,
                "invalid-guid"
            );
            serializedObject.Update();

            Assert.AreEqual(originalLow, lowProperty.longValue);
            Assert.AreEqual(originalHigh, highProperty.longValue);
            Assert.IsTrue(state.hasPendingInvalid);
            Assert.AreEqual($"Enter a valid {nameof(Guid)} string.", state.warningMessage);
        }

        [Test]
        public void HandleTextChangeRejectsNonVersionFourGuid()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            WGuidPropertyDrawer.HandleTextChange(
                property,
                lowProperty,
                highProperty,
                state,
                "00000000-0000-1000-8000-000000000000"
            );
            serializedObject.Update();

            Assert.IsTrue(state.hasPendingInvalid);
            Assert.AreEqual(
                $"{nameof(WGuid)} expects a version 4 {nameof(Guid)}.",
                state.warningMessage
            );
            Assert.IsTrue(ReadGuid(property).IsEmpty);
        }

        [Test]
        public void GenerateNewGuidProducesVersionFourValue()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            WGuidPropertyDrawer.GenerateNewGuid(property, lowProperty, highProperty, state);
            serializedObject.Update();

            WGuid result = ReadGuid(property);
            Assert.IsFalse(result.IsEmpty);
            Assert.IsTrue(result.IsVersion4);
            Assert.IsFalse(state.hasPendingInvalid);
            Assert.IsEmpty(state.warningMessage);
        }

        [Test]
        public void GetPropertyHeightIncludesWarningSpaceWhenInvalid()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            WGuidPropertyDrawer.HandleTextChange(
                property,
                lowProperty,
                highProperty,
                state,
                "invalid-guid"
            );
            serializedObject.Update();

            WGuidPropertyDrawer drawer = new();
            float height = drawer.GetPropertyHeight(property, GUIContent.none);
            Assert.Greater(height, EditorGUIUtility.singleLineHeight);
        }

        [Test]
        public void OnGUIHandlesDisposedSerializedObjectWithoutException()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            Assert.NotNull(property);

            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.GenerateNewGuid(property, lowProperty, highProperty, state);

            serializedObject.Dispose();

            GuidContainer newContainer = CreateScriptableObject<GuidContainer>();
            using SerializedObject newSerializedObject = new(newContainer);
            newSerializedObject.Update();

            SerializedProperty newProperty = newSerializedObject.FindProperty(
                nameof(GuidContainer.guid)
            );

            WGuidPropertyDrawer drawer = new();
            Assert.DoesNotThrow(() =>
            {
                float height = drawer.GetPropertyHeight(newProperty, GUIContent.none);
                Assert.GreaterOrEqual(height, 0f);
            });
        }

        [Test]
        public void CachingRevalidatesForDifferentSerializedObjects()
        {
            GuidContainer container1 = CreateScriptableObject<GuidContainer>();
            GuidContainer container2 = CreateScriptableObject<GuidContainer>();

            using SerializedObject serializedObject1 = new(container1);
            using SerializedObject serializedObject2 = new(container2);
            serializedObject1.Update();
            serializedObject2.Update();

            SerializedProperty property1 = serializedObject1.FindProperty(
                nameof(GuidContainer.guid)
            );
            SerializedProperty property2 = serializedObject2.FindProperty(
                nameof(GuidContainer.guid)
            );

            Assert.AreEqual(property1.propertyPath, property2.propertyPath);

            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property1);
            SerializedProperty low1 = property1.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty high1 = property1.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.GenerateNewGuid(property1, low1, high1, state);
            serializedObject1.Update();
            WGuid guid1 = ReadGuid(property1);

            SerializedProperty low2 = property2.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty high2 = property2.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.GenerateNewGuid(property2, low2, high2, state);
            serializedObject2.Update();
            WGuid guid2 = ReadGuid(property2);

            Assert.IsFalse(guid1.IsEmpty);
            Assert.IsFalse(guid2.IsEmpty);
            // They could be equal by chance but that's astronomically unlikely
        }

        [Test]
        public void StateInvalidateCacheClearsAllCachedData()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            state.cachedLowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            state.cachedHighProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            state.cachedSerializedObject = serializedObject;
            state.lastCacheFrame = 999;

            state.InvalidateCache();

            Assert.IsTrue(state.cachedLowProperty == null);
            Assert.IsTrue(state.cachedHighProperty == null);
            Assert.IsTrue(state.cachedSerializedObject == null);
            Assert.AreEqual(-1, state.lastCacheFrame);
        }

        [Test]
        public void ConvertToStringHandlesZeroValues()
        {
            string result = WGuidPropertyDrawer.ConvertToString(0L, 0L);
            Assert.AreEqual(Guid.Empty.ToString(), result);
        }

        [Test]
        public void ConvertToStringHandlesNonZeroValues()
        {
            WGuid testGuid = WGuid.NewGuid();
            Span<byte> buffer = stackalloc byte[16];
            testGuid.TryWriteBytes(buffer);
            long low = unchecked(
                (long)
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                        buffer.Slice(0, 8)
                    )
            );
            long high = unchecked(
                (long)
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                        buffer.Slice(8, 8)
                    )
            );

            string result = WGuidPropertyDrawer.ConvertToString(low, high);

            Assert.AreEqual(testGuid.ToString(), result);
        }

        [Test]
        public void HandleTextChangeWithNullInputTreatedAsEmpty()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            WGuidPropertyDrawer.GenerateNewGuid(property, lowProperty, highProperty, state);
            serializedObject.Update();
            Assert.IsFalse(ReadGuid(property).IsEmpty);

            WGuidPropertyDrawer.HandleTextChange(property, lowProperty, highProperty, state, null);
            serializedObject.Update();

            Assert.IsTrue(ReadGuid(property).IsEmpty);
            Assert.IsFalse(state.hasPendingInvalid);
        }

        [Test]
        public void HandleTextChangeWithWhitespaceOnlyTreatedAsEmpty()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            WGuidPropertyDrawer.GenerateNewGuid(property, lowProperty, highProperty, state);
            serializedObject.Update();
            Assert.IsFalse(ReadGuid(property).IsEmpty);

            WGuidPropertyDrawer.HandleTextChange(
                property,
                lowProperty,
                highProperty,
                state,
                "   \t\n   "
            );
            serializedObject.Update();

            Assert.IsTrue(ReadGuid(property).IsEmpty);
            Assert.IsFalse(state.hasPendingInvalid);
        }

        [Test]
        public void HandleTextChangeTrimsWhitespaceFromValidGuid()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            Guid expected = Guid.NewGuid();
            string inputWithWhitespace = $"  {expected:D}  ";

            WGuidPropertyDrawer.HandleTextChange(
                property,
                lowProperty,
                highProperty,
                state,
                inputWithWhitespace
            );
            serializedObject.Update();

            WGuid actual = ReadGuid(property);
            Assert.AreEqual(expected, actual.ToGuid());
            Assert.IsFalse(state.hasPendingInvalid);
        }

        [Test]
        public void GetStateReturnsSameInstanceForSamePropertyPath()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));

            WGuidPropertyDrawer.DrawerState state1 = WGuidPropertyDrawer.GetState(property);
            WGuidPropertyDrawer.DrawerState state2 = WGuidPropertyDrawer.GetState(property);

            Assert.AreSame(state1, state2);
        }

        [Test]
        public void ClearCachedStatesRemovesAllStates()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));

            WGuidPropertyDrawer.DrawerState state1 = WGuidPropertyDrawer.GetState(property);
            state1.displayText = "test-value";

            WGuidPropertyDrawer.ClearCachedStates();

            WGuidPropertyDrawer.DrawerState state2 = WGuidPropertyDrawer.GetState(property);
            Assert.AreNotSame(state1, state2);
            Assert.AreNotEqual("test-value", state2.displayText);
        }

        [Test]
        public void GetPropertyHeightReturnsMinimumLineHeightWhenValid()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);
            state.hasPendingInvalid = false;
            state.warningMessage = string.Empty;

            WGuidPropertyDrawer drawer = new();
            float height = drawer.GetPropertyHeight(property, GUIContent.none);

            Assert.AreEqual(EditorGUIUtility.singleLineHeight, height);
        }

        [Test]
        public void GenerateNewGuidClearsWarningState()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            state.hasPendingInvalid = true;
            state.warningMessage = "Some warning";

            WGuidPropertyDrawer.GenerateNewGuid(property, lowProperty, highProperty, state);
            serializedObject.Update();

            Assert.IsFalse(state.hasPendingInvalid);
            Assert.IsEmpty(state.warningMessage);
            Assert.IsFalse(ReadGuid(property).IsEmpty);
        }

        [Test]
        public void HandleTextChangeUpdatesDisplayTextOnSuccess()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            Guid expected = Guid.NewGuid();
            WGuidPropertyDrawer.HandleTextChange(
                property,
                lowProperty,
                highProperty,
                state,
                expected.ToString("D")
            );

            Assert.AreEqual(
                expected.ToString().ToLowerInvariant(),
                state.displayText.ToLowerInvariant()
            );
            Assert.AreEqual(state.displayText, state.serializedText);
        }

        [Test]
        public void HandleTextChangePreservesInvalidDisplayText()
        {
            GuidContainer container = CreateScriptableObject<GuidContainer>();
            using SerializedObject serializedObject = new(container);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(nameof(GuidContainer.guid));
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(property);

            string invalidInput = "not-a-valid-guid";
            WGuidPropertyDrawer.HandleTextChange(
                property,
                lowProperty,
                highProperty,
                state,
                invalidInput
            );

            Assert.AreEqual(invalidInput, state.displayText);
            Assert.IsTrue(state.hasPendingInvalid);
        }

        [Test]
        public void MultipleContainersIndependentStates()
        {
            GuidContainer container1 = CreateScriptableObject<GuidContainer>();
            GuidContainer container2 = CreateScriptableObject<GuidContainer>();

            using SerializedObject so1 = new(container1);
            using SerializedObject so2 = new(container2);
            so1.Update();
            so2.Update();

            SerializedProperty prop1 = so1.FindProperty(nameof(GuidContainer.guid));
            SerializedProperty prop2 = so2.FindProperty(nameof(GuidContainer.guid));

            SerializedProperty low1 = prop1.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty high1 = prop1.FindPropertyRelative(WGuid.HighFieldName);
            SerializedProperty low2 = prop2.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty high2 = prop2.FindPropertyRelative(WGuid.HighFieldName);

            WGuidPropertyDrawer.DrawerState state = WGuidPropertyDrawer.GetState(prop1);

            WGuidPropertyDrawer.GenerateNewGuid(prop1, low1, high1, state);
            so1.Update();
            WGuid guid1 = ReadGuid(prop1);

            WGuidPropertyDrawer.GenerateNewGuid(prop2, low2, high2, state);
            so2.Update();
            WGuid guid2 = ReadGuid(prop2);

            so1.Update();
            WGuid guid1After = ReadGuid(prop1);
            Assert.AreEqual(guid1, guid1After);

            Assert.IsFalse(guid2.IsEmpty);
        }

        private static WGuid ReadGuid(SerializedProperty property)
        {
            SerializedProperty lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            SerializedProperty highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            Span<byte> buffer = stackalloc byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(
                buffer.Slice(0, 8),
                unchecked((ulong)lowProperty.longValue)
            );
            BinaryPrimitives.WriteUInt64LittleEndian(
                buffer.Slice(8, 8),
                unchecked((ulong)highProperty.longValue)
            );
            Guid guid = new(buffer);
            return new WGuid(guid);
        }
    }
}
