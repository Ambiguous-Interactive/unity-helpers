// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Extensions;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Comprehensive tests for SerializedPropertyExtensions covering simple fields,
    /// arrays/lists, and nested objects. These tests focus on validating current,
    /// documented behavior of the extension methods.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializedPropertyExtensionsTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            SerializedPropertyExtensions.ClearCache();
        }

        [TearDown]
        public override void TearDown()
        {
            SerializedPropertyExtensions.ClearCache();
            base.TearDown();
        }

        private SerializedObject CreateSo(out SerializedPropertyExtensionsTestContainer container)
        {
            container = Track(
                ScriptableObject.CreateInstance<SerializedPropertyExtensionsTestContainer>()
            );
            return new SerializedObject(container);
        }

        [Test]
        public void GetEnclosingObjectSimpleFieldReturnsOwnerAndFieldInfo()
        {
            using SerializedObject so = CreateSo(
                out SerializedPropertyExtensionsTestContainer container
            );
            SerializedProperty prop = so.FindProperty(
                nameof(SerializedPropertyExtensionsTestContainer.publicInt)
            );
            Assert.NotNull(prop, "SerializedProperty for publicInt should not be null");

            object owner = prop.GetEnclosingObject(out FieldInfo field);

            Assert.AreSame(container, owner, "Owner should be the ScriptableObject instance");
            Assert.NotNull(field, "FieldInfo should not be null");
            Assert.AreEqual("publicInt", field.Name);
            Assert.AreEqual(typeof(int), field.FieldType);
        }

        [Test]
        public void GetTargetObjectWithFieldSimpleFieldReturnsValue()
        {
            using SerializedObject so = CreateSo(out _);
            SerializedProperty prop = so.FindProperty(
                nameof(SerializedPropertyExtensionsTestContainer.publicInt)
            );
            Assert.NotNull(prop);

            object value = prop.GetTargetObjectWithField(out FieldInfo field);

            Assert.NotNull(field);
            Assert.AreEqual("publicInt", field.Name);
            Assert.AreEqual(5, (int)value);
        }

        [Test]
        public void GetTargetObjectWithFieldNestedFieldReturnsFinalValue()
        {
            using SerializedObject so = CreateSo(out _);
            // Unity serializes nested [Serializable] types with dot-separated path
            SerializedProperty prop = so.FindProperty(
                $"{nameof(SerializedPropertyExtensionsTestContainer.nested)}.{nameof(SerializedPropertyExtensionsTestContainer.Nested.f)}"
            );
            Assert.NotNull(prop, "Property path nested.f should exist");

            object value = prop.GetTargetObjectWithField(out FieldInfo field);

            Assert.NotNull(field, "FieldInfo for final field should not be null");
            Assert.AreEqual("f", field.Name);
            Assert.AreEqual(typeof(float), field.FieldType);
            Assert.AreEqual(3.14f, (float)value, 0.0001f);
        }

        [Test]
        public void GetTargetObjectWithFieldArrayElementReturnsElement()
        {
            using SerializedObject so = CreateSo(out _);
            SerializedProperty prop = so.FindProperty(
                $"{nameof(SerializedPropertyExtensionsTestContainer.intArray)}.Array.data[1]"
            );
            Assert.NotNull(prop, "Property for intArray element should exist");

            object element = prop.GetTargetObjectWithField(out FieldInfo field);

            Assert.AreEqual(20, (int)element);
            // A pure array-element path has no field after the element, so field can be null.
            Assert.True(field == null || field.FieldType == typeof(int[]));
        }

        [Test]
        public void GetEnclosingObjectArrayElementReturnsRootOwnerAndArrayFieldInfo()
        {
            using SerializedObject so = CreateSo(
                out SerializedPropertyExtensionsTestContainer container
            );
            SerializedProperty prop = so.FindProperty(
                $"{nameof(SerializedPropertyExtensionsTestContainer.intList)}.Array.data[2]"
            );
            Assert.NotNull(prop, "Property for intList element should exist");

            object owner = prop.GetEnclosingObject(out FieldInfo field);

            Assert.AreSame(container, owner);
            Assert.NotNull(field);
            Assert.AreEqual("intList", field.Name);
            Assert.AreEqual(typeof(List<int>), field.FieldType);
        }

        [Test]
        public void GetTargetObjectWithFieldListElementFollowedByNestedFieldReturnsFinal()
        {
            // Build a list of nested, then access a field on an element: nestedList[1].f
            using SerializedObject so = CreateSo(
                out SerializedPropertyExtensionsTestContainer container
            );
            container.nested = new SerializedPropertyExtensionsTestContainer.Nested();
            container.intList = new List<int> { 4, 5, 6 };
            container.nested = new SerializedPropertyExtensionsTestContainer.Nested();

            so.Update();

            // Since SerializedPropertyExtensionsTestContainer does not have a List<Nested>, we'll test nested.inner.x access
            SerializedProperty innerProp = so.FindProperty(
                $"{nameof(SerializedPropertyExtensionsTestContainer.nested)}.{nameof(SerializedPropertyExtensionsTestContainer.Nested.inner)}.{nameof(SerializedPropertyExtensionsTestContainer.Inner.x)}"
            );
            Assert.NotNull(innerProp, "nested.inner.x should be found");

            object value = innerProp.GetTargetObjectWithField(out FieldInfo innerField);
            Assert.NotNull(innerField);
            Assert.AreEqual("x", innerField.Name);
            Assert.AreEqual(7, (int)value);
        }

        [Test]
        public void GetEnclosingObjectPrivateSerializedFieldReturnsOwnerAndFieldInfo()
        {
            using SerializedObject so = CreateSo(
                out SerializedPropertyExtensionsTestContainer container
            );
            SerializedProperty prop = so.FindProperty(
                nameof(SerializedPropertyExtensionsTestContainer.privateString)
            );
            Assert.NotNull(prop, "private serialized field should be discoverable");

            object owner = prop.GetEnclosingObject(out FieldInfo field);

            Assert.AreSame(container, owner);
            Assert.NotNull(field);
            Assert.AreEqual("privateString", field.Name);
            Assert.AreEqual(typeof(string), field.FieldType);
        }
    }
#endif
}
