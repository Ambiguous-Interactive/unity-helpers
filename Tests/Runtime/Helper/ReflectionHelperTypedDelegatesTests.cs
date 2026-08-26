// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Reflection;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ReflectionHelperTypedDelegatesTests : CommonTestBase
    {
        [Test]
        public void TypedFieldGetterAndSetterWork()
        {
            SampleComponent sample = new();
            FieldInfo field = typeof(SampleComponent).GetField(
                nameof(SampleComponent.InstanceField)
            );
            Func<SampleComponent, int> getter = ReflectionHelpers.GetFieldGetter<
                SampleComponent,
                int
            >(field);
            FieldSetter<SampleComponent, int> setter = ReflectionHelpers.GetFieldSetter<
                SampleComponent,
                int
            >(field);

            setter(ref sample, 42);
            Assert.AreEqual(42, getter(sample));
        }

        [Test]
        public void TypedStaticFieldGetterAndSetterWork()
        {
            FieldInfo field = typeof(SampleComponent).GetField(
                nameof(SampleComponent.StaticField),
                BindingFlags.Public | BindingFlags.Static
            );

            Func<string> getter = ReflectionHelpers.GetStaticFieldGetter<string>(field);
            Action<string> setter = ReflectionHelpers.GetStaticFieldSetter<string>(field);

            setter("hello");
            Assert.AreEqual("hello", getter());
        }

        [Test]
        public void TypedPropertyGetterAndSetterWork()
        {
            SampleComponent instance = new();
            PropertyInfo property = typeof(SampleComponent).GetProperty(
                nameof(SampleComponent.Name)
            );
            Func<SampleComponent, string> getter = ReflectionHelpers.GetPropertyGetter<
                SampleComponent,
                string
            >(property);
            Action<SampleComponent, string> setter = ReflectionHelpers.GetPropertySetter<
                SampleComponent,
                string
            >(property);

            setter(instance, "value");
            Assert.AreEqual("value", getter(instance));
        }

        [Test]
        public void TypedStaticPropertyGetterAndSetterWork()
        {
            PropertyInfo property = typeof(SampleComponent).GetProperty(
                nameof(SampleComponent.StaticCount),
                BindingFlags.Public | BindingFlags.Static
            );

            Func<int> getter = ReflectionHelpers.GetStaticPropertyGetter<int>(property);
            Action<int> setter = ReflectionHelpers.GetStaticPropertySetter<int>(property);

            setter(5);
            Assert.AreEqual(5, getter());
        }

        [Test]
        public void TypedInstanceMethodInvokerExecutesMethod()
        {
            SampleComponent instance = new();
            MethodInfo method = typeof(SampleComponent).GetMethod(
                nameof(SampleComponent.Increment)
            );
            Func<SampleComponent, int, int> invoker = ReflectionHelpers.GetInstanceMethodInvoker<
                SampleComponent,
                int,
                int
            >(method);

            int result = invoker(instance, 3);
            Assert.AreEqual(3, result);
            Assert.AreEqual(3, instance.InstanceField);
        }

        [Test]
        public void TypedStaticMethodInvokerExecutesMethod()
        {
            MethodInfo method = typeof(SampleComponent).GetMethod(
                nameof(SampleComponent.Concat),
                BindingFlags.Public | BindingFlags.Static
            );

            Func<string, string, string> invoker = ReflectionHelpers.GetStaticMethodInvoker<
                string,
                string,
                string
            >(method);

            Assert.AreEqual("ab", invoker("a", "b"));
        }

        private sealed class SampleComponent
        {
            public int InstanceField;
            public static string StaticField = null;

            public string Name { get; set; }
            public static int StaticCount { get; set; }

            public int Increment(int value)
            {
                InstanceField += value;
                return InstanceField;
            }

            public static string Concat(string left, string right)
            {
                return string.Concat(left, right);
            }
        }
    }
}
