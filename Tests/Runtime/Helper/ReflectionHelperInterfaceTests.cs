// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [Category("Fast")]
    public sealed class ReflectionHelperInterfaceTests
    {
        private static ReflectionCapabilityScope EnterMode(bool expressions, bool dynamicIl)
        {
            if (expressions && !ReflectionHelpers.ExpressionsEnabled)
            {
                Assert.Ignore("Expression compilation is not available on this platform.");
            }
            if (dynamicIl && !ReflectionHelpers.DynamicIlEnabled)
            {
                Assert.Ignore("Dynamic IL is not available on this platform.");
            }
            return new ReflectionCapabilityScope(expressions, dynamicIl);
        }

        private static void AssertValue<T>(T expected, object actual)
        {
            Assert.AreEqual(expected, actual);
            if (expected != null)
            {
                Assert.AreEqual(expected.GetType(), actual.GetType());
            }
        }

        private static void AssertStaticField<T>(Type owner, string name, T expected)
        {
            FieldInfo field = owner.GetField(name);
            Assert.NotNull(field);
            Func<object, object> boxed = ReflectionHelpers.GetFieldGetter(field);
            Func<IReflectionAccessorTarget<int>, T> typed = ReflectionHelpers.GetFieldGetter<
                IReflectionAccessorTarget<int>,
                T
            >(field);
            Func<object> staticBoxed = ReflectionHelpers.GetStaticFieldGetter(field);
            Func<T> staticTyped = ReflectionHelpers.GetStaticFieldGetter<T>(field);

            AssertValue(expected, boxed(null));
            AssertValue(expected, boxed(new object()));
            AssertValue(expected, typed(null));
            AssertValue(expected, staticBoxed());
            AssertValue(expected, staticTyped());
            Assert.AreSame(boxed, ReflectionHelpers.GetFieldGetter(field));
            Assert.AreSame(
                typed,
                ReflectionHelpers.GetFieldGetter<IReflectionAccessorTarget<int>, T>(field)
            );
            Assert.AreSame(staticBoxed, ReflectionHelpers.GetStaticFieldGetter(field));
            Assert.AreSame(staticTyped, ReflectionHelpers.GetStaticFieldGetter<T>(field));
        }

        private static void AssertClassStaticField<T>(T expected)
        {
            ReflectionAccessorTarget<T>.SharedValue = expected;
            try
            {
                AssertStaticField(
                    typeof(ReflectionAccessorTarget<T>),
                    nameof(ReflectionAccessorTarget<T>.SharedValue),
                    expected
                );
            }
            finally
            {
                ReflectionAccessorTarget<T>.SharedValue = default;
            }
        }

        private static void AssertProperty<T>(T initial, T replacement)
        {
            ReflectionAccessorTarget<T> target = new() { Value = initial };
            PropertyInfo property = typeof(IReflectionAccessorTarget<T>).GetProperty(
                nameof(IReflectionAccessorTarget<T>.Value)
            );
            Assert.NotNull(property);
            Func<object, object> getter = ReflectionHelpers.GetPropertyGetter(property);
            Func<ReflectionAccessorTarget<T>, T> concreteGetter =
                ReflectionHelpers.GetPropertyGetter<ReflectionAccessorTarget<T>, T>(property);
            Func<IReflectionAccessorTarget<T>, T> interfaceGetter =
                ReflectionHelpers.GetPropertyGetter<IReflectionAccessorTarget<T>, T>(property);
            Action<object, object> setter = ReflectionHelpers.GetPropertySetter(property);
            Action<ReflectionAccessorTarget<T>, T> concreteSetter =
                ReflectionHelpers.GetPropertySetter<ReflectionAccessorTarget<T>, T>(property);
            Action<IReflectionAccessorTarget<T>, T> interfaceSetter =
                ReflectionHelpers.GetPropertySetter<IReflectionAccessorTarget<T>, T>(property);

            AssertValue(initial, getter(target));
            AssertValue(initial, concreteGetter(target));
            AssertValue(initial, interfaceGetter(target));
            setter(target, replacement);
            AssertValue(replacement, target.Value);
            target.Value = initial;
            concreteSetter(target, replacement);
            AssertValue(replacement, target.Value);
            target.Value = initial;
            interfaceSetter(target, replacement);
            AssertValue(replacement, target.Value);
            AssertValue(replacement, getter(target));
            AssertValue(replacement, concreteGetter(target));
            AssertValue(replacement, interfaceGetter(target));
            Assert.AreSame(getter, ReflectionHelpers.GetPropertyGetter(property));
            Assert.AreSame(setter, ReflectionHelpers.GetPropertySetter(property));
        }

        private static void AssertIndexer<T>(T initial, T replacement)
        {
            ReflectionAccessorTarget<T> target = new() { Value = initial };
            MemberInfo[] members = typeof(IReflectionAccessorTarget<T>).GetDefaultMembers();
            Assert.AreEqual(1, members.Length);
            PropertyInfo property = (PropertyInfo)members[0];
            Func<object, object[], object> getter = ReflectionHelpers.GetIndexerGetter(property);
            Action<object, object, object[]> setter = ReflectionHelpers.GetIndexerSetter(property);
            AssertValue(initial, getter(target, new object[] { 3 }));
            Assert.AreEqual(3, target.LastIndex);
            setter(target, replacement, new object[] { 7 });
            Assert.AreEqual(7, target.LastIndex);
            AssertValue(replacement, target.Value);
            AssertValue(replacement, getter(target, new object[] { 11 }));
            Assert.AreEqual(11, target.LastIndex);
            Assert.AreSame(getter, ReflectionHelpers.GetIndexerGetter(property));
            Assert.AreSame(setter, ReflectionHelpers.GetIndexerSetter(property));
        }

        private static void ClearCaches()
        {
            ReflectionHelpers.ClearFieldGetterCache();
            ReflectionHelpers.ClearFieldSetterCache();
            ReflectionHelpers.ClearPropertyCache();
        }

        [SetUp]
        public void SetUp()
        {
            ClearCaches();
        }

        [TearDown]
        public void TearDown()
        {
            ClearCaches();
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void LiteralFieldsReadWithoutStorageInstructions(
            bool expressions,
            bool dynamicIl,
            bool classOwner
        )
        {
            using ReflectionCapabilityScope scope = EnterMode(expressions, dynamicIl);
            Type owner = classOwner
                ? typeof(ReflectionAccessorTarget<int>)
                : typeof(IReflectionAccessorTarget<int>);
            AssertStaticField(owner, nameof(IReflectionAccessorTarget<int>.IntegerConstant), 17);
            AssertStaticField(owner, nameof(IReflectionAccessorTarget<int>.LongConstant), 1L << 40);
            AssertStaticField(
                owner,
                nameof(IReflectionAccessorTarget<int>.TextConstant),
                "constant"
            );
            AssertStaticField<string>(
                owner,
                nameof(IReflectionAccessorTarget<int>.NullConstant),
                null
            );
            AssertStaticField(
                owner,
                nameof(IReflectionAccessorTarget<int>.EnumConstant),
                HideFlags.DontSave
            );
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void StaticValueTypeFieldsIgnoreTheInstanceArgument(bool expressions, bool dynamicIl)
        {
            using ReflectionCapabilityScope scope = EnterMode(expressions, dynamicIl);
            AssertStaticField(typeof(Guid), nameof(Guid.Empty), Guid.Empty);
            AssertStaticField(typeof(DateTime), nameof(DateTime.MinValue), DateTime.MinValue);
            AssertStaticField(typeof(TimeSpan), nameof(TimeSpan.Zero), TimeSpan.Zero);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void StaticReferenceTypeFieldsIgnoreTheInstanceArgument(
            bool expressions,
            bool dynamicIl
        )
        {
            using ReflectionCapabilityScope scope = EnterMode(expressions, dynamicIl);
            AssertClassStaticField(17);
            AssertClassStaticField(1L << 40);
            AssertClassStaticField("constant");
            AssertClassStaticField<string>(null);
            AssertClassStaticField(HideFlags.DontSave);
            AssertClassStaticField(Vector3.one);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void InterfacePropertiesDispatchForConcreteAndInterfaceTargets(
            bool expressions,
            bool dynamicIl
        )
        {
            using ReflectionCapabilityScope scope = EnterMode(expressions, dynamicIl);
            AssertProperty(13, 29);
            AssertProperty("initial", "replacement");
            AssertProperty("initial", null);
            AssertProperty(HideFlags.None, HideFlags.DontSave);
            AssertProperty(Vector3.one, Vector3.zero);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void InterfaceIndexersDispatchWithArguments(bool expressions, bool dynamicIl)
        {
            using ReflectionCapabilityScope scope = EnterMode(expressions, dynamicIl);
            AssertIndexer(13, 29);
            AssertIndexer("initial", "replacement");
            AssertIndexer("initial", null);
            AssertIndexer(HideFlags.None, HideFlags.DontSave);
            AssertIndexer(Vector3.one, Vector3.zero);
        }
    }
}
