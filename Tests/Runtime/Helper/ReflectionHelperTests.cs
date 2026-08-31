// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using CategoryAttribute = System.ComponentModel.CategoryAttribute;
    using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

    [AttributeUsage(AttributeTargets.All)]
    public class ReflectionTestAttribute : Attribute
    {
        public string Name { get; set; }
        public int Value { get; set; }

        public ReflectionTestAttribute() { }

        public ReflectionTestAttribute(string name, int value)
        {
            Name = name;
            Value = value;
        }
    }

    public struct TestStruct
    {
        public static int StaticIntValue;
        public int intValue;
    }

    public sealed class TestClass
    {
        public static int StaticIntValue;
        public int intValue;
    }

    public class TestMethodClass
    {
        public static int StaticMethodCallCount = 0;
        public int instanceMethodCallCount = 0;

        // Void methods
        public static void StaticVoidMethod()
        {
            StaticMethodCallCount++;
        }

        public void InstanceVoidMethod()
        {
            instanceMethodCallCount++;
        }

        // Return value methods
        public static int StaticIntMethod()
        {
            StaticMethodCallCount++;
            return 42;
        }

        public static string StaticStringMethod()
        {
            StaticMethodCallCount++;
            return "test";
        }

        public static bool StaticBoolMethod()
        {
            StaticMethodCallCount++;
            return true;
        }

        public int InstanceIntMethod()
        {
            instanceMethodCallCount++;
            return 100;
        }

        public string InstanceStringMethod()
        {
            instanceMethodCallCount++;
            return "instance";
        }

        // Methods with parameters
        public static int StaticMethodWithParam(int param)
        {
            StaticMethodCallCount++;
            return param * 2;
        }

        public static int StaticMethodTwoParams(int a, int b)
        {
            StaticMethodCallCount++;
            return a + b;
        }

        public int InstanceMethodWithParam(string param)
        {
            instanceMethodCallCount++;
            return param?.Length ?? 0;
        }

        public int InstanceMethodThreeParams(int a, string b, bool c)
        {
            instanceMethodCallCount++;
            return a + (b?.Length ?? 0) + (c ? 1 : 0);
        }

        public static void StaticVoidMethodWithParam(int param)
        {
            StaticMethodCallCount = param;
        }

        // Multiple parameter methods
        public static int StaticMethodMultipleParams(int a, string b, bool c)
        {
            StaticMethodCallCount++;
            return a + (b?.Length ?? 0) + (c ? 1 : 0);
        }

        public static int StaticMethodFourParams(int a, int b, int c, int d)
        {
            StaticMethodCallCount++;
            return a + b + c + d;
        }

        public static void StaticActionThree(int a, int b, int c)
        {
            StaticMethodCallCount = a + b + c;
        }

        public static void StaticActionTwo(int a, int b)
        {
            StaticMethodCallCount = a + b;
        }

        public static void StaticActionFour(int a, int b, int c, int d)
        {
            StaticMethodCallCount = a + b + c + d;
        }

        public int InstanceSum(int a, int b)
        {
            instanceMethodCallCount++;
            return a + b;
        }

        public void InstanceSetThree(int a, int b, int c)
        {
            instanceMethodCallCount = a + b + c;
        }

        public void InstanceSetOne(int value)
        {
            instanceMethodCallCount = value;
        }

        public void InstanceSetTwo(int a, int b)
        {
            instanceMethodCallCount = a + b;
        }

        public void InstanceSetFour(int a, int b, int c, int d)
        {
            instanceMethodCallCount = a + b + c + d;
        }

        public int InstanceSumFour(int a, int b, int c, int d)
        {
            instanceMethodCallCount++;
            return a + b + c + d;
        }

        public void Reset()
        {
            StaticMethodCallCount = 0;
            instanceMethodCallCount = 0;
        }

        public static void ResetStatic()
        {
            StaticMethodCallCount = 0;
        }
    }

    public sealed class TestConstructorClass
    {
        public int Value1 { get; }
        public string Value2 { get; }
        public bool Value3 { get; }

        public TestConstructorClass()
        {
            Value1 = 0;
            Value2 = "default";
            Value3 = false;
        }

        public TestConstructorClass(int value1)
        {
            Value1 = value1;
            Value2 = "single";
            Value3 = false;
        }

        public TestConstructorClass(int value1, string value2)
        {
            Value1 = value1;
            Value2 = value2;
            Value3 = false;
        }

        public TestConstructorClass(int value1, string value2, bool value3)
        {
            Value1 = value1;
            Value2 = value2;
            Value3 = value3;
        }
    }

    public readonly struct TestConstructorStruct
    {
        public int Value { get; }

        public TestConstructorStruct(int value)
        {
            Value = value;
        }
    }

    public sealed class TestPropertyClass
    {
        private static int _staticValue = 50;
        private int _instanceValue = 25;

        public static int StaticProperty
        {
            get => _staticValue;
            set => _staticValue = value;
        }

        public int InstanceProperty
        {
            get => _instanceValue;
            set => _instanceValue = value;
        }

        public static string StaticStringProperty { get; set; } = "static";

        public string InstanceStringProperty { get; set; } = "instance";

        // Read-only properties
        public static int StaticReadOnlyProperty => 999;
        public int InstanceReadOnlyProperty => 888;
    }

    public sealed class VariantPropertyClass
    {
        public object ObjectProperty { get; set; } = "instance";
        public static object StaticObjectProperty { get; set; } = "static";
        public object objectField = "instance-field";
        public static object StaticObjectField = "static-field";
    }

    public sealed class SelfReferentialType
    {
        public static SelfReferentialType InstanceField = new();
        public static SelfReferentialType InstanceProperty { get; } = new();
    }

    public struct ValueStruct
    {
        public int x;
        public int y;

        public int Sum(int a, int b) => a + b + x + y;
    }

    public sealed class IndexerClass
    {
        private readonly int[] _data = new int[10];
        public int this[int i]
        {
            get => _data[i];
            set => _data[i] = value;
        }
    }

    public static class RefOutMethods
    {
        public static void RefInc(ref int x)
        {
            x++;
        }

        public static void OutSet(out int x)
        {
            x = 7;
        }
    }

    [Description("Test class for attribute testing")]
    [ReflectionTestAttribute("ClassLevel", 10)]
    public sealed class TestAttributeClass
    {
        [Description("Static field with description")]
        [ReflectionTestAttribute("StaticField", 15)]
        public static int StaticFieldWithAttribute = 1;

        [Category("TestCategory")]
        [ReflectionTestAttribute("InstanceField", 5)]
        public int instanceFieldWithAttribute = 2;

        [Description("Static property for testing")]
        [ReflectionTestAttribute("StaticProperty", 20)]
        public static string StaticPropertyWithAttribute { get; set; } = "static";

        [Category("TestCategory")]
        [Description("Instance property")]
        [ReflectionTestAttribute("InstanceProperty", 8)]
        public string InstancePropertyWithAttribute { get; set; } = "instance";

        [Description("Static method for testing")]
        [ReflectionTestAttribute("StaticMethod", 12)]
        public static void StaticMethodWithAttribute() { }

        [Category("TestCategory")]
        [ReflectionTestAttribute("InstanceMethod", 7)]
        public void InstanceMethodWithAttribute() { }

        public void MethodWithAttributedParameter(
            [Description("Parameter with description")] int param
        ) { }
    }

    public sealed class GenericTestClass<T>
    {
        public T Value { get; set; }

        public GenericTestClass() { }

        public GenericTestClass(T value)
        {
            Value = value;
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ReflectionHelperTests : CommonTestBase
    {
        private const int NumTries = 1_000;

        [Test]
        public void GetFieldGetterClassMemberField()
        {
            TestClass testClass = new();
            Func<object, object> classGetter = ReflectionHelpers.GetFieldGetter(
                typeof(TestClass).GetField(nameof(TestClass.intValue))
            );
            Assert.AreEqual(testClass.intValue, classGetter(testClass));
            for (int i = 0; i < NumTries; ++i)
            {
                testClass.intValue = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(testClass.intValue, classGetter(testClass));
            }
        }

        [Test]
        public void GetFieldGetterClassStaticField()
        {
            TestClass testClass = new();
            Func<object, object> classGetter = ReflectionHelpers.GetFieldGetter(
                typeof(TestClass).GetField(
                    nameof(TestClass.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            Assert.AreEqual(TestClass.StaticIntValue, classGetter(testClass));
            for (int i = 0; i < NumTries; ++i)
            {
                TestClass.StaticIntValue = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(TestClass.StaticIntValue, classGetter(testClass));
            }
        }

        [Test]
        public void GetStaticFieldGetterThrowsOnNonStaticField()
        {
            Assert.Throws<ArgumentException>(() =>
                ReflectionHelpers.GetStaticFieldGetter(
                    typeof(TestClass).GetField(nameof(TestClass.intValue))
                )
            );
        }

        [Test]
        public void GetStaticFieldGetterClassStaticField()
        {
            Func<object> classGetter = ReflectionHelpers.GetStaticFieldGetter(
                typeof(TestClass).GetField(
                    nameof(TestClass.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            Assert.AreEqual(TestClass.StaticIntValue, classGetter());
            for (int i = 0; i < NumTries; ++i)
            {
                TestClass.StaticIntValue = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(TestClass.StaticIntValue, classGetter());
            }
        }

        [Test]
        public void GetFieldGetterStructMemberField()
        {
            TestStruct testStruct = new();
            Func<object, object> structGetter = ReflectionHelpers.GetFieldGetter(
                typeof(TestStruct).GetField(nameof(TestStruct.intValue))
            );
            Assert.AreEqual(testStruct.intValue, structGetter(testStruct));
            for (int i = 0; i < NumTries; ++i)
            {
                testStruct.intValue = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(testStruct.intValue, structGetter(testStruct));
            }
        }

        [Test]
        public void GetFieldGetterStructStaticField()
        {
            TestStruct testStruct = new();
            Func<object, object> structGetter = ReflectionHelpers.GetFieldGetter(
                typeof(TestStruct).GetField(
                    nameof(TestStruct.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            Assert.AreEqual(TestStruct.StaticIntValue, structGetter(testStruct));
            for (int i = 0; i < NumTries; ++i)
            {
                TestStruct.StaticIntValue = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(TestStruct.StaticIntValue, structGetter(testStruct));
            }
        }

        [Test]
        public void GetStaticFieldGetterStructStaticField()
        {
            Func<object> structGetter = ReflectionHelpers.GetStaticFieldGetter(
                typeof(TestStruct).GetField(
                    nameof(TestStruct.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            Assert.AreEqual(TestStruct.StaticIntValue, structGetter());
            for (int i = 0; i < NumTries; ++i)
            {
                TestStruct.StaticIntValue = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(TestStruct.StaticIntValue, structGetter());
            }
        }

        [Test]
        public void GetFieldSetterClassMemberField()
        {
            TestClass testClass = new();
            Action<object, object> structSetter = ReflectionHelpers.GetFieldSetter(
                typeof(TestClass).GetField(nameof(TestClass.intValue))
            );
            for (int i = 0; i < NumTries; ++i)
            {
                int expected = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                structSetter(testClass, expected);
                Assert.AreEqual(expected, testClass.intValue);
            }
        }

        [Test]
        public void GetFieldSetterClassStaticField()
        {
            TestClass testClass = new();
            Action<object, object> structSetter = ReflectionHelpers.GetFieldSetter(
                typeof(TestClass).GetField(
                    nameof(TestClass.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            for (int i = 0; i < NumTries; ++i)
            {
                int expected = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                structSetter(testClass, expected);
                Assert.AreEqual(expected, TestClass.StaticIntValue);
            }
        }

        [Test]
        public void GetStaticFieldSetterThrowsOnNonStaticField()
        {
            Assert.Throws<ArgumentException>(() =>
                ReflectionHelpers.GetStaticFieldSetter(
                    typeof(TestClass).GetField(nameof(TestClass.intValue))
                )
            );
        }

        [Test]
        public void GetStaticFieldSetterClassStaticField()
        {
            Action<object> structSetter = ReflectionHelpers.GetStaticFieldSetter(
                typeof(TestClass).GetField(
                    nameof(TestClass.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            for (int i = 0; i < NumTries; ++i)
            {
                int expected = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                structSetter(expected);
                Assert.AreEqual(expected, TestClass.StaticIntValue);
            }
        }

        [Test]
        public void GetFieldSetterStructMemberField()
        {
            // Need boxing
            object testStruct = new TestStruct();
            Action<object, object> structSetter = ReflectionHelpers.GetFieldSetter(
                typeof(TestStruct).GetField(nameof(TestStruct.intValue))
            );
            for (int i = 0; i < NumTries; ++i)
            {
                int expected = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                structSetter(testStruct, expected);
                Assert.AreEqual(expected, ((TestStruct)testStruct).intValue);
            }
        }

        [Test]
        public void GetFieldSetterStructStaticField()
        {
            // Need boxing
            object testStruct = new TestStruct();
            Action<object, object> structSetter = ReflectionHelpers.GetFieldSetter(
                typeof(TestStruct).GetField(
                    nameof(TestStruct.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            for (int i = 0; i < NumTries; ++i)
            {
                int expected = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                structSetter(testStruct, expected);
                Assert.AreEqual(expected, TestStruct.StaticIntValue);
            }
        }

        [Test]
        public void GetStaticFieldSetterStructStaticField()
        {
            Action<object> structSetter = ReflectionHelpers.GetStaticFieldSetter(
                typeof(TestStruct).GetField(
                    nameof(TestStruct.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            for (int i = 0; i < NumTries; ++i)
            {
                int expected = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                structSetter(expected);
                Assert.AreEqual(expected, TestStruct.StaticIntValue);
            }
        }

        [Test]
        public void GetFieldSetterClassGenericMemberField()
        {
            TestClass testClass = new();
            FieldSetter<TestClass, int> classSetter = ReflectionHelpers.GetFieldSetter<
                TestClass,
                int
            >(typeof(TestClass).GetField(nameof(TestClass.intValue)));
            for (int i = 0; i < NumTries; ++i)
            {
                int expected = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                classSetter(ref testClass, expected);
                Assert.AreEqual(expected, testClass.intValue);
            }
        }

        [Test]
        public void GetFieldSetterClassGenericStaticField()
        {
            TestClass testClass = new();
            FieldSetter<TestClass, int> classSetter = ReflectionHelpers.GetFieldSetter<
                TestClass,
                int
            >(
                typeof(TestClass).GetField(
                    nameof(TestClass.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            for (int i = 0; i < NumTries; ++i)
            {
                int expected = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                classSetter(ref testClass, expected);
                Assert.AreEqual(expected, TestClass.StaticIntValue);
            }
        }

        [Test]
        public void GetStaticFieldSetterGenericThrowsOnNonStaticField()
        {
            Assert.Throws<ArgumentException>(() =>
                ReflectionHelpers.GetStaticFieldSetter<int>(
                    typeof(TestClass).GetField(nameof(TestClass.intValue))
                )
            );
        }

        [Test]
        public void GetStaticFieldSetterClassGenericStaticField()
        {
            Action<int> classSetter = ReflectionHelpers.GetStaticFieldSetter<int>(
                typeof(TestClass).GetField(
                    nameof(TestClass.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            for (int i = 0; i < NumTries; ++i)
            {
                int expected = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                classSetter(expected);
                Assert.AreEqual(expected, TestClass.StaticIntValue);
            }
        }

        [Test]
        public void GetFieldSetterStructGenericMemberField()
        {
            TestStruct testStruct = new();
            FieldSetter<TestStruct, int> structSetter = ReflectionHelpers.GetFieldSetter<
                TestStruct,
                int
            >(typeof(TestStruct).GetField(nameof(TestStruct.intValue)));
            for (int i = 0; i < NumTries; ++i)
            {
                int expected = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                structSetter(ref testStruct, expected);
                Assert.AreEqual(expected, testStruct.intValue);
            }
        }

        [Test]
        public void GetFieldSetterStructGenericStaticField()
        {
            TestStruct testStruct = new();
            FieldSetter<TestStruct, int> structSetter = ReflectionHelpers.GetFieldSetter<
                TestStruct,
                int
            >(
                typeof(TestStruct).GetField(
                    nameof(TestStruct.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            for (int i = 0; i < NumTries; ++i)
            {
                int expected = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                structSetter(ref testStruct, expected);
                Assert.AreEqual(expected, TestStruct.StaticIntValue);
            }
        }

        [Test]
        public void GetStaticFieldSetterStructGenericStaticField()
        {
            Action<int> structSetter = ReflectionHelpers.GetStaticFieldSetter<int>(
                typeof(TestStruct).GetField(
                    nameof(TestStruct.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            for (int i = 0; i < NumTries; ++i)
            {
                int expected = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                structSetter(expected);
                Assert.AreEqual(expected, TestStruct.StaticIntValue);
            }
        }

        [Test]
        public void GetFieldGetterClassGenericMemberField()
        {
            TestClass testClass = new();
            Func<TestClass, int> classGetter = ReflectionHelpers.GetFieldGetter<TestClass, int>(
                typeof(TestClass).GetField(nameof(TestClass.intValue))
            );
            for (int i = 0; i < NumTries; ++i)
            {
                testClass.intValue = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(testClass.intValue, classGetter(testClass));
            }
        }

        [Test]
        public void GetFieldGetterClassGenericStaticField()
        {
            TestClass testClass = new();
            Func<TestClass, int> classGetter = ReflectionHelpers.GetFieldGetter<TestClass, int>(
                typeof(TestClass).GetField(
                    nameof(TestClass.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            for (int i = 0; i < NumTries; ++i)
            {
                TestClass.StaticIntValue = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(TestClass.StaticIntValue, classGetter(testClass));
            }
        }

        [Test]
        public void GetStaticFieldGetterGenericThrowsOnNonStaticField()
        {
            Assert.Throws<ArgumentException>(() =>
                ReflectionHelpers.GetStaticFieldGetter<int>(
                    typeof(TestClass).GetField(nameof(TestClass.intValue))
                )
            );
        }

        [Test]
        public void GetStaticFieldGetterClassGenericStaticField()
        {
            Func<int> classGetter = ReflectionHelpers.GetStaticFieldGetter<int>(
                typeof(TestClass).GetField(
                    nameof(TestClass.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            for (int i = 0; i < NumTries; ++i)
            {
                TestClass.StaticIntValue = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(TestClass.StaticIntValue, classGetter());
            }
        }

        [Test]
        public void GetFieldGetterStructGenericMemberField()
        {
            TestStruct testStruct = new();
            Func<TestStruct, int> structSetter = ReflectionHelpers.GetFieldGetter<TestStruct, int>(
                typeof(TestStruct).GetField(nameof(TestStruct.intValue))
            );
            for (int i = 0; i < NumTries; ++i)
            {
                testStruct.intValue = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(testStruct.intValue, structSetter(testStruct));
            }
        }

        [Test]
        public void GetFieldGetterStructGenericStaticField()
        {
            TestStruct testStruct = new();
            Func<TestStruct, int> structSetter = ReflectionHelpers.GetFieldGetter<TestStruct, int>(
                typeof(TestStruct).GetField(
                    nameof(TestStruct.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            for (int i = 0; i < NumTries; ++i)
            {
                TestStruct.StaticIntValue = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(TestStruct.StaticIntValue, structSetter(testStruct));
            }
        }

        [Test]
        public void GetStaticFieldGetterStructGenericStaticField()
        {
            Func<int> structSetter = ReflectionHelpers.GetStaticFieldGetter<int>(
                typeof(TestStruct).GetField(
                    nameof(TestStruct.StaticIntValue),
                    BindingFlags.Static | BindingFlags.Public
                )
            );
            for (int i = 0; i < NumTries; ++i)
            {
                TestStruct.StaticIntValue = PRNG.Instance.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(TestStruct.StaticIntValue, structSetter());
            }
        }

        [Test]
        public void ArrayCreator()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                int count = PRNG.Instance.Next(1_000);
                Array created = ReflectionHelpers.CreateArray(typeof(int), count);
                Assert.AreEqual(count, created.Length);
                Assert.IsTrue(created is int[]);
                int[] typed = (int[])created;
                Assert.AreEqual(count, typed.Length);
            }
        }

        [Test]
        public void ListCreator()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                IList created = ReflectionHelpers.CreateList(typeof(int));
                Assert.AreEqual(0, created.Count);
                Assert.IsTrue(created is List<int>);
                List<int> typedCreated = (List<int>)created;
                int count = PRNG.Instance.Next(50);
                List<int> expected = new();
                for (int j = 0; j < count; ++j)
                {
                    int element = PRNG.Instance.Next();
                    created.Add(element);
                    expected.Add(element);
                    Assert.AreEqual(j + 1, created.Count);
                    Assert.That(expected, Is.EqualTo(typedCreated));
                }
            }
        }

        [Test]
        public void ListWithSizeCreator()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                int capacity = PRNG.Instance.Next(1_000);
                IList created = ReflectionHelpers.CreateList(typeof(int), capacity);
                Assert.AreEqual(0, created.Count);
                Assert.IsTrue(created is List<int>);
                List<int> typedCreated = (List<int>)created;
                Assert.AreEqual(capacity, typedCreated.Capacity);

                int count = PRNG.Instance.Next(50);
                List<int> expected = new();
                for (int j = 0; j < count; ++j)
                {
                    int element = PRNG.Instance.Next();
                    created.Add(element);
                    expected.Add(element);
                    Assert.AreEqual(j + 1, created.Count);
                    Assert.That(expected, Is.EqualTo(typedCreated));
                }
            }
        }

        [Test]
        public void GetArrayCreator()
        {
            Func<int, Array> arrayCreator = ReflectionHelpers.GetArrayCreator(typeof(int));
            Assert.IsTrue(arrayCreator != null, "Array creator should not be null");

            for (int i = 0; i < 10; ++i)
            {
                int size = PRNG.Instance.Next(1, 100);
                Array created = arrayCreator(size);
                Assert.IsTrue(created != null, "Created array should not be null");
                Assert.AreEqual(size, created.Length);
                Assert.IsTrue(created is int[]);
            }
        }

        [Test]
        public void GetListCreator()
        {
            Func<IList> listCreator = ReflectionHelpers.GetListCreator(typeof(string));
            Assert.IsTrue(listCreator != null, "List creator should not be null");

            for (int i = 0; i < 10; ++i)
            {
                IList created = listCreator();
                Assert.IsTrue(created != null, "Created list should not be null");
                Assert.AreEqual(0, created.Count);
                Assert.IsTrue(created is List<string>);
            }
        }

        [Test]
        public void GetListWithCapacityCreator()
        {
            Func<int, IList> listCreator = ReflectionHelpers.GetListWithCapacityCreator(
                typeof(bool)
            );
            Assert.IsTrue(listCreator != null);

            for (int i = 0; i < 10; ++i)
            {
                int capacity = PRNG.Instance.Next(1, 100);
                IList created = listCreator(capacity);
                Assert.IsTrue(created != null);
                Assert.AreEqual(0, created.Count);
                Assert.IsTrue(created is List<bool>);
                Assert.AreEqual(capacity, ((List<bool>)created).Capacity);
            }
        }

        [Test]
        public void InvokeStaticVoidMethod()
        {
            TestMethodClass testObj = new();
            testObj.Reset();

            MethodInfo method = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticVoidMethod)
            );
            Assert.IsTrue(method != null);

            int initialCount = TestMethodClass.StaticMethodCallCount;
            object result = ReflectionHelpers.InvokeStaticMethod(method);

            Assert.IsTrue(result == null);
            Assert.AreEqual(initialCount + 1, TestMethodClass.StaticMethodCallCount);
        }

        [Test]
        public void InvokeStaticMethodWithReturnValue()
        {
            TestMethodClass testObj = new();
            testObj.Reset();

            MethodInfo intMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticIntMethod)
            );
            MethodInfo stringMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticStringMethod)
            );
            MethodInfo boolMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticBoolMethod)
            );

            Assert.AreEqual(42, ReflectionHelpers.InvokeStaticMethod(intMethod));
            Assert.AreEqual("test", ReflectionHelpers.InvokeStaticMethod(stringMethod));
            Assert.AreEqual(true, ReflectionHelpers.InvokeStaticMethod(boolMethod));
            Assert.AreEqual(3, TestMethodClass.StaticMethodCallCount);
        }

        [Test]
        public void InvokeStaticMethodWithParameters()
        {
            int directResult = TestMethodClass.StaticMethodWithParam(10);
            Assert.AreEqual(20, directResult);

            TestMethodClass testObj = new();
            testObj.Reset();

            MethodInfo paramMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticMethodWithParam)
            );
            MethodInfo voidParamMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticVoidMethodWithParam)
            );
            MethodInfo multiParamMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticMethodMultipleParams)
            );

            object result1 = ReflectionHelpers.InvokeStaticMethod(paramMethod, 10);
            Assert.AreEqual(20, result1);
            ReflectionHelpers.InvokeStaticMethod(voidParamMethod, 99);
            Assert.AreEqual(99, TestMethodClass.StaticMethodCallCount);
            object result3 = ReflectionHelpers.InvokeStaticMethod(multiParamMethod, 5, "abc", true);
            Assert.AreEqual(9, result3);
        }

        [Test]
        public void InvokeInstanceMethod()
        {
            TestMethodClass testObj = new();
            testObj.Reset();

            MethodInfo voidMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceVoidMethod)
            );
            MethodInfo intMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceIntMethod)
            );
            MethodInfo stringMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceStringMethod)
            );
            MethodInfo paramMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceMethodWithParam)
            );

            Assert.IsTrue(ReflectionHelpers.InvokeMethod(voidMethod, testObj) == null);
            Assert.AreEqual(1, testObj.instanceMethodCallCount);

            Assert.AreEqual(100, ReflectionHelpers.InvokeMethod(intMethod, testObj));
            Assert.AreEqual("instance", ReflectionHelpers.InvokeMethod(stringMethod, testObj));
            Assert.AreEqual(5, ReflectionHelpers.InvokeMethod(paramMethod, testObj, "hello"));
            Assert.AreEqual(4, testObj.instanceMethodCallCount);
        }

        [Test]
        public void GetMethodInvoker()
        {
            TestMethodClass testObj = new();
            testObj.Reset();

            MethodInfo voidMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceVoidMethod)
            );
            MethodInfo intMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceIntMethod)
            );
            MethodInfo paramMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceMethodWithParam)
            );

            Func<object, object[], object> voidInvoker = ReflectionHelpers.GetMethodInvoker(
                voidMethod
            );
            Func<object, object[], object> intInvoker = ReflectionHelpers.GetMethodInvoker(
                intMethod
            );
            Func<object, object[], object> paramInvoker = ReflectionHelpers.GetMethodInvoker(
                paramMethod
            );

            Assert.IsTrue(voidInvoker(testObj, Array.Empty<object>()) == null);
            Assert.AreEqual(1, testObj.instanceMethodCallCount);

            Assert.AreEqual(100, intInvoker(testObj, Array.Empty<object>()));
            Assert.AreEqual(2, testObj.instanceMethodCallCount);

            Assert.AreEqual(5, paramInvoker(testObj, new object[] { "hello" }));
            Assert.AreEqual(3, testObj.instanceMethodCallCount);
        }

        [Test]
        public void GetStaticMethodInvoker()
        {
            TestMethodClass testObj = new();
            testObj.Reset();

            MethodInfo voidMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticVoidMethod)
            );
            MethodInfo intMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticIntMethod)
            );
            MethodInfo paramMethod = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticMethodWithParam)
            );

            Func<object[], object> voidInvoker = ReflectionHelpers.GetStaticMethodInvoker(
                voidMethod
            );
            Func<object[], object> intInvoker = ReflectionHelpers.GetStaticMethodInvoker(intMethod);
            Func<object[], object> paramInvoker = ReflectionHelpers.GetStaticMethodInvoker(
                paramMethod
            );

            Assert.IsTrue(voidInvoker(Array.Empty<object>()) == null);
            Assert.AreEqual(1, TestMethodClass.StaticMethodCallCount);

            Assert.AreEqual(42, intInvoker(Array.Empty<object>()));
            Assert.AreEqual(2, TestMethodClass.StaticMethodCallCount);

            Assert.AreEqual(20, paramInvoker(new object[] { 10 }));
            Assert.AreEqual(3, TestMethodClass.StaticMethodCallCount);
        }

        [Test]
        public void CreateInstanceParameterless()
        {
            TestConstructorClass defaultInstance =
                ReflectionHelpers.CreateInstance<TestConstructorClass>();
            Assert.IsTrue(defaultInstance != null);
            Assert.AreEqual(0, defaultInstance.Value1);
            Assert.AreEqual("default", defaultInstance.Value2);
            Assert.AreEqual(false, defaultInstance.Value3);
        }

        [Test]
        public void CreateInstanceWithParameters()
        {
            TestConstructorClass singleParam =
                ReflectionHelpers.CreateInstance<TestConstructorClass>(42);
            Assert.IsTrue(singleParam != null);
            Assert.AreEqual(42, singleParam.Value1);
            Assert.AreEqual("single", singleParam.Value2);
            Assert.AreEqual(false, singleParam.Value3);

            TestConstructorClass twoParam = ReflectionHelpers.CreateInstance<TestConstructorClass>(
                10,
                "test"
            );
            Assert.IsTrue(twoParam != null);
            Assert.AreEqual(10, twoParam.Value1);
            Assert.AreEqual("test", twoParam.Value2);
            Assert.AreEqual(false, twoParam.Value3);

            TestConstructorClass threeParam =
                ReflectionHelpers.CreateInstance<TestConstructorClass>(5, "hello", true);
            Assert.IsTrue(threeParam != null);
            Assert.AreEqual(5, threeParam.Value1);
            Assert.AreEqual("hello", threeParam.Value2);
            Assert.AreEqual(true, threeParam.Value3);
        }

        [Test]
        public void CreateInstanceUsingConstructorInfo()
        {
            ConstructorInfo[] constructors = typeof(TestConstructorClass).GetConstructors();
            ConstructorInfo parameterlessConstructor = constructors.First(c =>
                c.GetParameters().Length == 0
            );
            ConstructorInfo singleParamConstructor = constructors.First(c =>
                c.GetParameters().Length == 1
            );

            Func<object[], object> parameterlessDelegate = ReflectionHelpers.GetConstructor(
                parameterlessConstructor
            );
            Func<object[], object> singleParamDelegate = ReflectionHelpers.GetConstructor(
                singleParamConstructor
            );

            TestConstructorClass instance1 = (TestConstructorClass)parameterlessDelegate(
                Array.Empty<object>()
            );
            Assert.AreEqual(0, instance1.Value1);
            Assert.AreEqual("default", instance1.Value2);

            TestConstructorClass instance2 = (TestConstructorClass)singleParamDelegate(
                new object[] { 99 }
            );
            Assert.AreEqual(99, instance2.Value1);
            Assert.AreEqual("single", instance2.Value2);
        }

        [Test]
        public void GetParameterlessConstructor()
        {
            Func<TestConstructorClass> constructor =
                ReflectionHelpers.GetParameterlessConstructor<TestConstructorClass>();
            Assert.IsTrue(constructor != null);

            for (int i = 0; i < 100; i++)
            {
                TestConstructorClass instance = constructor();
                Assert.IsTrue(instance != null);
                Assert.AreEqual(0, instance.Value1);
                Assert.AreEqual("default", instance.Value2);
                Assert.AreEqual(false, instance.Value3);
            }
        }

        [Test]
        public void CreateGenericInstance()
        {
            object intGeneric = ReflectionHelpers.CreateGenericInstance<object>(
                typeof(GenericTestClass<>),
                new[] { typeof(int) },
                42
            );

            Assert.IsTrue(intGeneric != null);
            Assert.IsInstanceOf<GenericTestClass<int>>(intGeneric);
            Assert.AreEqual(42, ((GenericTestClass<int>)intGeneric).Value);

            object stringGeneric = ReflectionHelpers.CreateGenericInstance<object>(
                typeof(GenericTestClass<>),
                new[] { typeof(string) },
                "hello"
            );

            Assert.IsTrue(stringGeneric != null);
            Assert.IsInstanceOf<GenericTestClass<string>>(stringGeneric);
            Assert.AreEqual("hello", ((GenericTestClass<string>)stringGeneric).Value);
        }

        [Test]
        public void GetGenericParameterlessConstructor()
        {
            Func<object> constructor = ReflectionHelpers.GetGenericParameterlessConstructor<object>(
                typeof(GenericTestClass<>),
                typeof(int)
            );

            Assert.IsTrue(constructor != null);

            object instance = constructor();
            Assert.IsTrue(instance != null);
            Assert.IsInstanceOf<GenericTestClass<int>>(instance);
        }

        [Test]
        public void CreateStructInstance()
        {
            TestConstructorStruct structInstance =
                ReflectionHelpers.CreateInstance<TestConstructorStruct>(123);
            Assert.AreEqual(123, structInstance.Value);
        }

        [Test]
        public void GetPropertyGetterStatic()
        {
            // Reset static state to initial values
            TestPropertyClass.StaticProperty = 50;
            TestPropertyClass.StaticStringProperty = "static";

            PropertyInfo staticProp = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.StaticProperty)
            );
            PropertyInfo staticStringProp = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.StaticStringProperty)
            );
            PropertyInfo staticReadOnlyProp = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.StaticReadOnlyProperty)
            );

            Func<object, object> staticGetter = ReflectionHelpers.GetPropertyGetter(staticProp);
            Func<object, object> staticStringGetter = ReflectionHelpers.GetPropertyGetter(
                staticStringProp
            );
            Func<object, object> staticReadOnlyGetter = ReflectionHelpers.GetPropertyGetter(
                staticReadOnlyProp
            );

            // Test initial values
            Assert.AreEqual(50, staticGetter(null));
            Assert.AreEqual("static", staticStringGetter(null));
            Assert.AreEqual(999, staticReadOnlyGetter(null));

            // Test after changing values
            TestPropertyClass.StaticProperty = 100;
            TestPropertyClass.StaticStringProperty = "changed";

            Assert.AreEqual(100, staticGetter(null));
            Assert.AreEqual("changed", staticStringGetter(null));
            Assert.AreEqual(999, staticReadOnlyGetter(null)); // Read-only shouldn't change
        }

        [Test]
        public void GetPropertyGetterInstance()
        {
            TestPropertyClass testObj = new();
            PropertyInfo instanceProp = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.InstanceProperty)
            );
            PropertyInfo instanceStringProp = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.InstanceStringProperty)
            );
            PropertyInfo instanceReadOnlyProp = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.InstanceReadOnlyProperty)
            );

            Func<object, object> instanceGetter = ReflectionHelpers.GetPropertyGetter(instanceProp);
            Func<object, object> instanceStringGetter = ReflectionHelpers.GetPropertyGetter(
                instanceStringProp
            );
            Func<object, object> instanceReadOnlyGetter = ReflectionHelpers.GetPropertyGetter(
                instanceReadOnlyProp
            );

            // Test initial values
            Assert.AreEqual(25, instanceGetter(testObj));
            Assert.AreEqual("instance", instanceStringGetter(testObj));
            Assert.AreEqual(888, instanceReadOnlyGetter(testObj));

            // Test after changing values
            testObj.InstanceProperty = 200;
            testObj.InstanceStringProperty = "modified";

            Assert.AreEqual(200, instanceGetter(testObj));
            Assert.AreEqual("modified", instanceStringGetter(testObj));
            Assert.AreEqual(888, instanceReadOnlyGetter(testObj)); // Read-only shouldn't change
        }

        [Test]
        public void GetStaticPropertyGetter()
        {
            Func<int> staticGetter = ReflectionHelpers.GetStaticPropertyGetter<int>(
                typeof(TestPropertyClass).GetProperty(nameof(TestPropertyClass.StaticProperty))
            );

            Func<string> staticStringGetter = ReflectionHelpers.GetStaticPropertyGetter<string>(
                typeof(TestPropertyClass).GetProperty(
                    nameof(TestPropertyClass.StaticStringProperty)
                )
            );

            Assert.AreEqual(TestPropertyClass.StaticProperty, staticGetter());
            Assert.AreEqual(TestPropertyClass.StaticStringProperty, staticStringGetter());

            TestPropertyClass.StaticProperty = 777;
            TestPropertyClass.StaticStringProperty = "new value";

            Assert.AreEqual(777, staticGetter());
            Assert.AreEqual("new value", staticStringGetter());
        }

        [Test]
        public void GetAllLoadedAssemblies()
        {
            Assembly[] assemblies = ReflectionHelpers.GetAllLoadedAssemblies().ToArray();
            Assert.IsTrue(assemblies != null);
            Assert.Greater(assemblies.Length, 0);
            Assert.IsTrue(assemblies.All(a => a != null));
            Assert.IsTrue(assemblies.All(a => !a.IsDynamic));
        }

        [Test]
        public void GetAllLoadedTypes()
        {
            Type[] types = ReflectionHelpers.GetAllLoadedTypes().Take(100).ToArray();
            Assert.IsTrue(types != null);
            Assert.Greater(types.Length, 0);
            Assert.IsTrue(types.All(t => t != null));
        }

        [Test]
        public void GetPropertySetterInstanceAndStatic()
        {
            TestPropertyClass instance = new();

            // Instance setter
            PropertyInfo instProp = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.InstanceProperty)
            );
            Action<object, object> instSetter = ReflectionHelpers.GetPropertySetter(instProp);
            instSetter(instance, 555);
            Assert.AreEqual(555, instance.InstanceProperty);

            // Static setter
            PropertyInfo staticProp = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.StaticProperty)
            );
            Action<object, object> staticSetter = ReflectionHelpers.GetPropertySetter(staticProp);
            staticSetter(null, 777);
            Assert.AreEqual(777, TestPropertyClass.StaticProperty);
        }

        [Test]
        public void GetPropertySetterThrowsOnReadOnly()
        {
            PropertyInfo roInstance = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.InstanceReadOnlyProperty)
            );
            Assert.Throws<ArgumentException>(
                () => ReflectionHelpers.GetPropertySetter(roInstance),
                "Property InstanceReadOnlyProperty has no setter should throw"
            );

            PropertyInfo roStatic = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.StaticReadOnlyProperty)
            );
            Assert.Throws<ArgumentException>(
                () => ReflectionHelpers.GetPropertySetter(roStatic),
                "Property StaticReadOnlyProperty has no setter should throw"
            );
        }

        [Test]
        public void GetPropertyGetterGenericCoversInstanceAndStatic()
        {
            TestPropertyClass obj = new() { InstanceProperty = 123 };

            Func<TestPropertyClass, int> instGetter = ReflectionHelpers.GetPropertyGetter<
                TestPropertyClass,
                int
            >(typeof(TestPropertyClass).GetProperty(nameof(TestPropertyClass.InstanceProperty)));
            Assert.AreEqual(123, instGetter(obj));

            TestPropertyClass.StaticProperty = 246;
            Func<TestPropertyClass, int> staticGetter = ReflectionHelpers.GetPropertyGetter<
                TestPropertyClass,
                int
            >(typeof(TestPropertyClass).GetProperty(nameof(TestPropertyClass.StaticProperty)));
            Assert.AreEqual(246, staticGetter(obj)); // instance arg ignored for static
        }

        [Test]
        public void HashSetCreatorAndAdder()
        {
            object intSetObj = ReflectionHelpers.CreateHashSet(typeof(int), 0);
            Assert.IsInstanceOf<HashSet<int>>(intSetObj);
            Action<object, object> addInt = ReflectionHelpers.GetHashSetAdder(typeof(int));
            addInt(intSetObj, 1);
            addInt(intSetObj, 1);
            addInt(intSetObj, 2);
            Assert.That((HashSet<int>)intSetObj, Is.EquivalentTo(new[] { 1, 2 }));

            object strSetObj = ReflectionHelpers.CreateHashSet(typeof(string), 0);
            Assert.IsInstanceOf<HashSet<string>>(strSetObj);
            Action<object, object> addStr = ReflectionHelpers.GetHashSetAdder(typeof(string));
            addStr(strSetObj, null);
            addStr(strSetObj, "a");
            addStr(strSetObj, "a");
            Assert.IsTrue(((HashSet<string>)strSetObj).Contains(null));
            Assert.That((HashSet<string>)strSetObj, Is.EquivalentTo(new[] { null, "a" }));
        }

        [Test]
        public void GetStaticMethodInvokerTypedTwoParams()
        {
            MethodInfo concat = typeof(string).GetMethod(
                nameof(string.Concat),
                new[] { typeof(string), typeof(string) }
            );
            Func<string, string, string> invoker = ReflectionHelpers.GetStaticMethodInvoker<
                string,
                string,
                string
            >(concat);
            Assert.AreEqual("ab", invoker("a", "b"));
        }

        [Test]
        public void GetStaticMethodInvokerTypedThrowsOnSignatureMismatch()
        {
            MethodInfo concat = typeof(string).GetMethod(
                nameof(string.Concat),
                new[] { typeof(string), typeof(string) }
            );
            Assert.Throws<ArgumentException>(
                () => ReflectionHelpers.GetStaticMethodInvoker<int, int, int>(concat),
                "Mismatched generic signature should throw"
            );

            MethodInfo instanceToString = typeof(object).GetMethod(nameof(ToString));
            Assert.Throws<ArgumentException>(
                () =>
                    ReflectionHelpers.GetStaticMethodInvoker<object, object, string>(
                        instanceToString
                    ),
                "Non-static method should throw"
            );
        }

        [Test]
        public void GetParameterlessConstructorTypeOverload()
        {
            Func<object> ctor = ReflectionHelpers.GetParameterlessConstructor(
                typeof(TestConstructorClass)
            );
            object instance = ctor();
            Assert.IsInstanceOf<TestConstructorClass>(instance);

            Assert.Throws<ArgumentException>(
                () => ReflectionHelpers.GetParameterlessConstructor(typeof(NoParameterlessCtor)),
                "Type without parameterless constructor should throw"
            );
        }

        [Test]
        public void BuildParameterlessInstanceMethodIfExists()
        {
            Action<TestMethodClass> action =
                ReflectionHelpers.BuildParameterlessInstanceMethodIfExists<TestMethodClass>(
                    "Reset"
                );
            Assert.IsTrue(action != null);
            TestMethodClass obj = new();
            TestMethodClass.StaticMethodCallCount = 5;
            obj.instanceMethodCallCount = 4;
            action(obj);
            Assert.AreEqual(0, TestMethodClass.StaticMethodCallCount);
            Assert.AreEqual(0, obj.instanceMethodCallCount);

            Action<TestMethodClass> missing =
                ReflectionHelpers.BuildParameterlessInstanceMethodIfExists<TestMethodClass>(
                    "DoesNotExist"
                );
            Assert.IsTrue(missing == null);
        }

        [Test]
        public void IsComponentEnabledCoversMonoBehaviourAndScriptableObject()
        {
            GameObject go = Track(new GameObject("probe"));
            EnabledProbe probe = go.AddComponent<EnabledProbe>();
            Assert.IsTrue(probe.IsComponentEnabled());
            probe.enabled = false;
            Assert.IsFalse(probe.IsComponentEnabled());

            ScriptableObject so = Track(ScriptableObject.CreateInstance<ScriptableObject>());
            Assert.IsTrue(so.IsComponentEnabled());
        }

        [Test]
        public void GetTypesFromAssembly()
        {
            Assembly testAssembly = typeof(ReflectionHelperTests).Assembly;
            Type[] types = ReflectionHelpers.GetTypesFromAssembly(testAssembly).ToArray();

            Assert.IsTrue(types != null);
            Assert.Greater(types.Length, 0);
            Assert.IsTrue(types.Contains(typeof(ReflectionHelperTests)));
            Assert.IsTrue(types.Contains(typeof(TestAttributeClass)));
            Assert.IsTrue(types.Contains(typeof(TestMethodClass)));
        }

        [Test]
        public void GetTypesFromAssemblyName()
        {
            Type[] types = ReflectionHelpers
                .GetTypesFromAssemblyName("System.Core")
                .Take(10)
                .ToArray();
            Assert.IsTrue(types != null);
            // May be empty on some platforms, but should not throw
        }

        [Test]
        public void GetTypesWithAttribute()
        {
            Type[] typesWithDescAttr = ReflectionHelpers
                .GetTypesWithAttribute<DescriptionAttribute>()
                .ToArray();
            Assert.IsTrue(typesWithDescAttr != null);
            Assert.IsTrue(typesWithDescAttr.Contains(typeof(TestAttributeClass)));

            Type[] typesByType = ReflectionHelpers
                .GetTypesWithAttribute(typeof(DescriptionAttribute))
                .ToArray();
            Assert.IsTrue(typesByType != null);
            Assert.IsTrue(typesByType.Contains(typeof(TestAttributeClass)));
        }

        [Test]
        public void HasAttributeSafe()
        {
            Type testType = typeof(TestAttributeClass);
            FieldInfo staticField = testType.GetField(
                nameof(TestAttributeClass.StaticFieldWithAttribute)
            );
            MethodInfo instanceMethod = testType.GetMethod(
                nameof(TestAttributeClass.InstanceMethodWithAttribute)
            );

            Assert.IsTrue(ReflectionHelpers.HasAttributeSafe<ReflectionTestAttribute>(testType));
            Assert.IsTrue(ReflectionHelpers.HasAttributeSafe<ReflectionTestAttribute>(staticField));
            Assert.IsTrue(
                ReflectionHelpers.HasAttributeSafe<ReflectionTestAttribute>(instanceMethod)
            );

            Assert.IsTrue(
                ReflectionHelpers.HasAttributeSafe(testType, typeof(ReflectionTestAttribute))
            );
            Assert.IsTrue(
                ReflectionHelpers.HasAttributeSafe(staticField, typeof(ReflectionTestAttribute))
            );
            Assert.IsTrue(
                ReflectionHelpers.HasAttributeSafe(instanceMethod, typeof(ReflectionTestAttribute))
            );

            // Test with types that don't have attributes
            Assert.IsFalse(
                ReflectionHelpers.HasAttributeSafe<ReflectionTestAttribute>(typeof(TestMethodClass))
            );
            Assert.IsFalse(
                ReflectionHelpers.HasAttributeSafe(
                    typeof(TestMethodClass),
                    typeof(ReflectionTestAttribute)
                )
            );

            // Test with null (should return false, not throw)
            Assert.IsFalse(ReflectionHelpers.HasAttributeSafe<ReflectionTestAttribute>(null));
            Assert.IsFalse(
                ReflectionHelpers.HasAttributeSafe(null, typeof(ReflectionTestAttribute))
            );
        }

        [Test]
        public void GetAttributeSafe()
        {
            Type testType = typeof(TestAttributeClass);
            FieldInfo instanceField = testType.GetField(
                nameof(TestAttributeClass.instanceFieldWithAttribute)
            );

            ReflectionTestAttribute classAttr =
                ReflectionHelpers.GetAttributeSafe<ReflectionTestAttribute>(testType);
            Assert.IsTrue(classAttr != null);
            Assert.AreEqual("ClassLevel", classAttr.Name);
            Assert.AreEqual(10, classAttr.Value);

            ReflectionTestAttribute fieldAttr =
                ReflectionHelpers.GetAttributeSafe<ReflectionTestAttribute>(instanceField);
            Assert.IsTrue(fieldAttr != null);
            Assert.AreEqual("InstanceField", fieldAttr.Name);
            Assert.AreEqual(5, fieldAttr.Value);

            // Test non-generic version
            Attribute classAttrObj = ReflectionHelpers.GetAttributeSafe(
                testType,
                typeof(ReflectionTestAttribute)
            );
            Assert.IsTrue(classAttrObj != null);
            Assert.IsInstanceOf<ReflectionTestAttribute>(classAttrObj);

            // Test with null
            Assert.IsTrue(
                ReflectionHelpers.GetAttributeSafe<ReflectionTestAttribute>(null) == null
            );
            Assert.IsTrue(
                ReflectionHelpers.GetAttributeSafe(null, typeof(ReflectionTestAttribute)) == null
            );
        }

        [Test]
        public void TryGetAttributeSafeGeneric()
        {
            Type testType = typeof(TestAttributeClass);

            bool found = ReflectionHelpers.TryGetAttributeSafe(
                testType,
                out ReflectionTestAttribute attribute
            );
            Assert.IsTrue(found);
            Assert.IsTrue(attribute != null);
            Assert.AreEqual("ClassLevel", attribute.Name);

            bool missing = ReflectionHelpers.TryGetAttributeSafe(
                testType,
                out ObsoleteAttribute missingAttribute
            );
            Assert.IsFalse(missing);
            Assert.IsTrue(missingAttribute == null);

            bool nullProvider = ReflectionHelpers.TryGetAttributeSafe(
                null,
                out ReflectionTestAttribute nullAttribute
            );
            Assert.IsFalse(nullProvider);
            Assert.IsTrue(nullAttribute == null);
        }

        [Test]
        public void TryGetAttributeSafeNonGeneric()
        {
            Type testType = typeof(TestAttributeClass);

            bool found = ReflectionHelpers.TryGetAttributeSafe(
                testType,
                typeof(ReflectionTestAttribute),
                out Attribute attribute
            );
            Assert.IsTrue(found);
            Assert.IsTrue(attribute != null);
            Assert.IsInstanceOf<ReflectionTestAttribute>(attribute);

            bool missing = ReflectionHelpers.TryGetAttributeSafe(
                testType,
                typeof(ObsoleteAttribute),
                out Attribute missingAttribute
            );
            Assert.IsFalse(missing);
            Assert.IsTrue(missingAttribute == null);

            bool nullProvider = ReflectionHelpers.TryGetAttributeSafe(
                null,
                typeof(ReflectionTestAttribute),
                out Attribute nullAttribute
            );
            Assert.IsFalse(nullProvider);
            Assert.IsTrue(nullAttribute == null);

            bool nullType = ReflectionHelpers.TryGetAttributeSafe(
                testType,
                null,
                out Attribute nullTypeAttribute
            );
            Assert.IsFalse(nullType);
            Assert.IsTrue(nullTypeAttribute == null);
        }

        [Test]
        public void GetAllAttributesSafe()
        {
            Type testType = typeof(TestAttributeClass);
            PropertyInfo instanceProperty = testType.GetProperty(
                nameof(TestAttributeClass.InstancePropertyWithAttribute)
            );

            ReflectionTestAttribute[] typeAttrs = testType
                .GetAllAttributesSafe<ReflectionTestAttribute>()
                .ToArray();
            Assert.IsTrue(typeAttrs != null);
            Assert.AreEqual(1, typeAttrs.Length);
            Assert.AreEqual("ClassLevel", typeAttrs[0].Name);

            ReflectionTestAttribute[] propAttrs = instanceProperty
                .GetAllAttributesSafe<ReflectionTestAttribute>()
                .ToArray();
            Assert.IsTrue(propAttrs != null);
            Assert.AreEqual(1, propAttrs.Length);
            Assert.AreEqual("InstanceProperty", propAttrs[0].Name);

            // Test non-generic version
            Attribute[] allTypeAttrs = testType.GetAllAttributesSafe().ToArray();
            Assert.IsTrue(allTypeAttrs != null);
            Assert.Greater(allTypeAttrs.Length, 0);

            Attribute[] allPropAttrs = instanceProperty
                .GetAllAttributesSafe(typeof(ReflectionTestAttribute))
                .ToArray();
            Assert.IsTrue(allPropAttrs != null);
            Assert.AreEqual(1, allPropAttrs.Length);

            // Test with null
            Assert.AreEqual(
                0,
                ReflectionHelpers.GetAllAttributesSafe<ReflectionTestAttribute>(null).Length
            );
            Assert.AreEqual(0, ReflectionHelpers.GetAllAttributesSafe(null).Length);
        }

        [Test]
        public void GetAllAttributeValuesSafe()
        {
            Type testType = typeof(TestAttributeClass);
            Dictionary<string, object> attrValues = testType.GetAllAttributeValuesSafe();

            Assert.IsTrue(attrValues != null);
            Assert.IsTrue(attrValues.ContainsKey("ReflectionTest"));
            Assert.IsInstanceOf<ReflectionTestAttribute>(attrValues.ValueFor("ReflectionTest"));

            // Test with null
            Dictionary<string, object> nullResult = ReflectionHelpers.GetAllAttributeValuesSafe(
                null
            );
            Assert.IsTrue(nullResult != null);
            Assert.AreEqual(0, nullResult.Count);
        }

        [Test]
        public void GetMembersWithAttributeSafe()
        {
            Type testType = typeof(TestAttributeClass);

            MethodInfo[] methods = testType
                .GetMethodsWithAttributeSafe<ReflectionTestAttribute>()
                .ToArray();
            Assert.IsTrue(methods != null);
            Assert.Greater(methods.Length, 0);
            Assert.IsTrue(
                methods.Any(m => m.Name == nameof(TestAttributeClass.StaticMethodWithAttribute))
            );
            Assert.IsTrue(
                methods.Any(m => m.Name == nameof(TestAttributeClass.InstanceMethodWithAttribute))
            );

            PropertyInfo[] properties = testType
                .GetPropertiesWithAttributeSafe<ReflectionTestAttribute>()
                .ToArray();
            Assert.IsTrue(properties != null);
            Assert.Greater(properties.Length, 0);
            Assert.IsTrue(
                properties.Any(p =>
                    p.Name == nameof(TestAttributeClass.StaticPropertyWithAttribute)
                )
            );

            FieldInfo[] fields = testType
                .GetFieldsWithAttributeSafe<ReflectionTestAttribute>()
                .ToArray();
            Assert.IsTrue(fields != null);
            Assert.Greater(fields.Length, 0);
            Assert.IsTrue(
                fields.Any(f => f.Name == nameof(TestAttributeClass.StaticFieldWithAttribute))
            );

            // Test with null
            Assert.AreEqual(
                0,
                ReflectionHelpers.GetMethodsWithAttributeSafe<ReflectionTestAttribute>(null).Length
            );
            Assert.AreEqual(
                0,
                ReflectionHelpers
                    .GetPropertiesWithAttributeSafe<ReflectionTestAttribute>(null)
                    .Length
            );
            Assert.AreEqual(
                0,
                ReflectionHelpers.GetFieldsWithAttributeSafe<ReflectionTestAttribute>(null).Length
            );
        }

        [Test]
        public void LoadStaticPropertiesForType()
        {
            Dictionary<string, PropertyInfo> staticProperties =
                ReflectionHelpers.LoadStaticPropertiesForType<TestPropertyClass>();
            Assert.IsTrue(staticProperties != null);

            // Note: This method looks for static properties that return the same type as the class
            // Our TestPropertyClass doesn't have static properties that return TestPropertyClass,
            // so the result should be empty, but the method should not throw
            Assert.IsTrue(staticProperties != null);
        }

        [Test]
        public void LoadStaticFieldsForType()
        {
            Dictionary<string, FieldInfo> staticFields =
                ReflectionHelpers.LoadStaticFieldsForType<TestPropertyClass>();
            Assert.IsTrue(staticFields != null);

            // Note: This method looks for static fields that are of the same type as the class
            // Our TestPropertyClass doesn't have such fields, so the result should be empty,
            // but the method should not throw
            Assert.IsTrue(staticFields != null);
        }

        [Test]
        public void IsAttributeDefined()
        {
            Type testType = typeof(TestAttributeClass);
            FieldInfo testField = testType.GetField(
                nameof(TestAttributeClass.instanceFieldWithAttribute)
            );

            // Test the extension method version
            Assert.IsTrue(testType.IsAttributeDefined(out ReflectionTestAttribute typeAttr));
            Assert.IsTrue(typeAttr != null);
            Assert.AreEqual("ClassLevel", typeAttr.Name);
            Assert.AreEqual(10, typeAttr.Value);

            Assert.IsTrue(testField.IsAttributeDefined(out ReflectionTestAttribute fieldAttr));
            Assert.IsTrue(fieldAttr != null);
            Assert.AreEqual("InstanceField", fieldAttr.Name);
            Assert.AreEqual(5, fieldAttr.Value);

            // Test with type that doesn't have the attribute
            Type methodType = typeof(TestMethodClass);
            Assert.IsFalse(methodType.IsAttributeDefined(out ReflectionTestAttribute noAttr));
            Assert.IsTrue(noAttr == null);
        }

        [Test]
        public void PropertySetterThrowsOnReadOnly()
        {
            PropertyInfo ro = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.InstanceReadOnlyProperty)
            );
            Assert.Throws<ArgumentException>(() => ReflectionHelpers.GetPropertySetter(ro));
            Assert.Throws<ArgumentException>(() =>
                ReflectionHelpers.GetPropertySetter<TestPropertyClass, int>(ro)
            );
        }

        [Test]
        public void StaticPropertySetterThrowsOnInstanceProperty()
        {
            PropertyInfo pi = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.InstanceProperty)
            );
            Assert.Throws<ArgumentException>(() =>
                ReflectionHelpers.GetStaticPropertySetter<int>(pi)
            );
        }

        [Test]
        public void IndexerHelpersThrowOnNonIndexer()
        {
            PropertyInfo pi = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.InstanceProperty)
            );
            Assert.Throws<ArgumentException>(() => ReflectionHelpers.GetIndexerGetter(pi));
            Assert.Throws<ArgumentException>(() => ReflectionHelpers.GetIndexerSetter(pi));
        }

        [Test]
        public void TypedPropertyGetterStaticIgnoresInstance()
        {
            PropertyInfo spi = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.StaticProperty)
            );
            Action<int> setter = ReflectionHelpers.GetStaticPropertySetter<int>(spi);
            setter(777);
            Func<TestPropertyClass, int> getter = ReflectionHelpers.GetPropertyGetter<
                TestPropertyClass,
                int
            >(spi);
            TestPropertyClass dummy = new();
            Assert.AreEqual(777, getter(dummy));
        }

        [Test]
        public void TypedPropertyGetterBoxesValueTypeWhenRequested()
        {
            PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.InstanceProperty)
            );
            Func<TestPropertyClass, object> getter = ReflectionHelpers.GetPropertyGetter<
                TestPropertyClass,
                object
            >(property);
            TestPropertyClass instance = new() { InstanceProperty = 512 };
            Assert.AreEqual(512, getter(instance));
        }

        [Test]
        public void TypedPropertySetterUnboxesValueTypes()
        {
            PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.InstanceProperty)
            );
            Action<TestPropertyClass, object> setter = ReflectionHelpers.GetPropertySetter<
                TestPropertyClass,
                object
            >(property);
            TestPropertyClass instance = new();
            setter(instance, 2048);
            Assert.AreEqual(2048, instance.InstanceProperty);
        }

        [Test]
        public void TypedStaticPropertySetterUnboxesValueTypes()
        {
            PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.StaticProperty)
            );
            object original = TestPropertyClass.StaticProperty;
            try
            {
                Action<object> setter = ReflectionHelpers.GetStaticPropertySetter<object>(property);
                setter(1024);
                Assert.AreEqual(1024, TestPropertyClass.StaticProperty);
            }
            finally
            {
                TestPropertyClass.StaticProperty = (int)original;
            }
        }

        [Test]
        public void TypedPropertyGetterCastsReferenceTypes()
        {
            VariantPropertyClass instance = new() { ObjectProperty = "hello" };
            PropertyInfo property = typeof(VariantPropertyClass).GetProperty(
                nameof(VariantPropertyClass.ObjectProperty)
            );
            Func<VariantPropertyClass, string> getter = ReflectionHelpers.GetPropertyGetter<
                VariantPropertyClass,
                string
            >(property);
            Assert.AreEqual("hello", getter(instance));
        }

        [Test]
        public void DynamicIlBoxedFieldGetterReturnsValue()
        {
            RunWithDynamicIlOnly(() =>
            {
                TestClass instance = new() { intValue = 321 };
                FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
                Func<object, object> getter = ReflectionHelpers.GetFieldGetter(field);
                Assert.AreEqual(321, getter(instance));
            });
        }

        [Test]
        public void DynamicIlBoxedFieldSetterMutatesInstance()
        {
            RunWithDynamicIlOnly(() =>
            {
                TestClass instance = new();
                FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
                Action<object, object> setter = ReflectionHelpers.GetFieldSetter(field);
                setter(instance, 654);
                Assert.AreEqual(654, instance.intValue);
            });
        }

        [Test]
        public void DynamicIlTypedFieldSetterMutatesInstance()
        {
            RunWithDynamicIlOnly(() =>
            {
                FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
                FieldSetter<TestClass, int> setter = ReflectionHelpers.GetFieldSetter<
                    TestClass,
                    int
                >(field);
                TestClass instance = new();
                setter(ref instance, 987);
                Assert.AreEqual(987, instance.intValue);
            });
        }

        [Test]
        public void DynamicIlTypedFieldGetterReturnsValue()
        {
            RunWithDynamicIlOnly(() =>
            {
                TestClass instance = new() { intValue = 741 };
                FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
                Func<TestClass, int> getter = ReflectionHelpers.GetFieldGetter<TestClass, int>(
                    field
                );
                Assert.AreEqual(741, getter(instance));
            });
        }

        [Test]
        public void DynamicIlTypedStaticFieldSetterMutatesValue()
        {
            RunWithDynamicIlOnly(() =>
            {
                FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.StaticIntValue));
                int original = TestClass.StaticIntValue;
                try
                {
                    Action<int> setter = ReflectionHelpers.GetStaticFieldSetter<int>(field);
                    setter(512);
                    Assert.AreEqual(512, TestClass.StaticIntValue);
                }
                finally
                {
                    TestClass.StaticIntValue = original;
                }
            });
        }

        [Test]
        public void DynamicIlTypedPropertySetterMutatesInstance()
        {
            RunWithDynamicIlOnly(() =>
            {
                PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                    nameof(TestPropertyClass.InstanceProperty)
                );
                Action<TestPropertyClass, int> setter = ReflectionHelpers.GetPropertySetter<
                    TestPropertyClass,
                    int
                >(property);
                TestPropertyClass instance = new();
                setter(instance, 222);
                Assert.AreEqual(222, instance.InstanceProperty);
            });
        }

        [Test]
        public void DynamicIlTypedPropertyGetterReturnsValue()
        {
            RunWithDynamicIlOnly(() =>
            {
                PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                    nameof(TestPropertyClass.InstanceProperty)
                );
                TestPropertyClass instance = new();
                Func<TestPropertyClass, int> getter = ReflectionHelpers.GetPropertyGetter<
                    TestPropertyClass,
                    int
                >(property);
                Assert.AreEqual(instance.InstanceProperty, getter(instance));
            });
        }

        [Test]
        public void DynamicIlTypedStaticPropertySetterMutatesValue()
        {
            RunWithDynamicIlOnly(() =>
            {
                PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                    nameof(TestPropertyClass.StaticProperty)
                );
                int original = TestPropertyClass.StaticProperty;
                try
                {
                    Action<int> setter = ReflectionHelpers.GetStaticPropertySetter<int>(property);
                    setter(314);
                    Assert.AreEqual(314, TestPropertyClass.StaticProperty);
                }
                finally
                {
                    TestPropertyClass.StaticProperty = original;
                }
            });
        }

        [Test]
        public void DynamicIlInstanceMethodInvokerReturnsValue()
        {
            RunWithDynamicIlOnly(() =>
            {
                MethodInfo method = typeof(TestMethodClass).GetMethod(
                    nameof(TestMethodClass.InstanceMethodWithParam)
                );
                Func<object, object[], object> invoker = ReflectionHelpers.GetMethodInvoker(method);
                TestMethodClass instance = new();
                object result = invoker(instance, new object[] { "abcd" });
                Assert.AreEqual(4, result);
            });
        }

        [Test]
        public void DynamicIlTypedInstanceMethodInvokerReturnsValue()
        {
            RunWithDynamicIlOnly(() =>
            {
                MethodInfo method = typeof(TestMethodClass).GetMethod(
                    nameof(TestMethodClass.InstanceIntMethod)
                );
                Func<TestMethodClass, int> invoker = ReflectionHelpers.GetInstanceMethodInvoker<
                    TestMethodClass,
                    int
                >(method);
                TestMethodClass instance = new();
                Assert.AreEqual(100, invoker(instance));
            });
        }

        [Test]
        public void DynamicIlTypedStaticMethodInvokerReturnsValue()
        {
            RunWithDynamicIlOnly(() =>
            {
                MethodInfo method = typeof(TestMethodClass).GetMethod(
                    nameof(TestMethodClass.StaticIntMethod)
                );
                Func<int> invoker = ReflectionHelpers.GetStaticMethodInvoker<int>(method);
                Assert.AreEqual(42, invoker());
            });
        }

        [Test]
        public void DynamicIlConstructorInvokerCreatesInstance()
        {
            RunWithDynamicIlOnly(() =>
            {
                ConstructorInfo ctor = typeof(TestConstructorClass).GetConstructor(
                    new[] { typeof(int), typeof(string) }
                );
                Func<object[], object> invoker = ReflectionHelpers.GetConstructor(ctor);
                object created = invoker(new object[] { 5, "five" });
                Assert.IsInstanceOf<TestConstructorClass>(created);
                TestConstructorClass instance = (TestConstructorClass)created;
                Assert.AreEqual(5, instance.Value1);
                Assert.AreEqual("five", instance.Value2);
            });
        }

        [Test]
        public void DynamicIlParameterlessConstructorCreatesInstance()
        {
            RunWithDynamicIlOnly(() =>
            {
                Func<TestConstructorClass> ctor =
                    ReflectionHelpers.GetParameterlessConstructor<TestConstructorClass>();
                TestConstructorClass instance = ctor();
                Assert.IsTrue(instance != null);
            });
        }

        [Test]
        public void DynamicIlIndexerSetterAndGetterRoundTrip()
        {
            RunWithDynamicIlOnly(() =>
            {
                PropertyInfo indexer = typeof(IndexerClass).GetProperty("Item");
                Func<object, object[], object> getter = ReflectionHelpers.GetIndexerGetter(indexer);
                Action<object, object, object[]> setter = ReflectionHelpers.GetIndexerSetter(
                    indexer
                );
                IndexerClass instance = new();
                setter(instance, 13, new object[] { 2 });
                Assert.AreEqual(13, getter(instance, new object[] { 2 }));
            });
        }

        private static void RunWithDynamicIlOnly(Action assertion)
        {
            if (!ReflectionHelpers.DynamicIlEnabled)
            {
                Assert.Ignore("Dynamic IL is not available on this platform.");
            }

            using (
                ReflectionHelpers.OverrideReflectionCapabilities(
                    expressions: false,
                    dynamicIl: true
                )
            )
            {
                assertion();
            }
        }

        [Test]
        public void TypedPropertySetterCastsReferenceTypes()
        {
            VariantPropertyClass instance = new();
            PropertyInfo property = typeof(VariantPropertyClass).GetProperty(
                nameof(VariantPropertyClass.ObjectProperty)
            );
            Action<VariantPropertyClass, string> setter = ReflectionHelpers.GetPropertySetter<
                VariantPropertyClass,
                string
            >(property);
            setter(instance, "updated");
            Assert.AreEqual("updated", instance.ObjectProperty);
        }

        [Test]
        public void TypedStaticPropertySetterCastsReferenceTypes()
        {
            PropertyInfo property = typeof(VariantPropertyClass).GetProperty(
                nameof(VariantPropertyClass.StaticObjectProperty)
            );
            object original = VariantPropertyClass.StaticObjectProperty;
            try
            {
                Action<string> setter = ReflectionHelpers.GetStaticPropertySetter<string>(property);
                setter("world");
                Func<string> getter = ReflectionHelpers.GetStaticPropertyGetter<string>(property);
                Assert.AreEqual("world", getter());
            }
            finally
            {
                VariantPropertyClass.StaticObjectProperty = original;
            }
        }

        [Test]
        public void ParameterlessConstructorDelegateCreatesInstances()
        {
            Func<object> creator = ReflectionHelpers.GetParameterlessConstructor(
                typeof(TestConstructorClass)
            );
            object first = creator();
            object second = creator();
            Assert.IsInstanceOf<TestConstructorClass>(first);
            Assert.IsInstanceOf<TestConstructorClass>(second);
            Assert.AreNotSame(first, second);
        }

        [Test]
        public void ParameterlessConstructorFallbackUsesReflectionWhenExpressionsDisabled()
        {
            using (
                ReflectionHelpers.OverrideReflectionCapabilities(
                    expressions: false,
                    dynamicIl: false
                )
            )
            {
                Func<TestConstructorClass> creator =
                    ReflectionHelpers.GetParameterlessConstructor<TestConstructorClass>();
                TestConstructorClass instance = creator();
                Assert.IsTrue(instance != null);
                Assert.AreEqual(0, instance.Value1);
            }
        }

        [Test]
        public void ConstructorInvokerCreatesTypedInstance()
        {
            ConstructorInfo ctor = typeof(GenericTestClass<int>).GetConstructor(
                new[] { typeof(int) }
            );
            Func<object[], object> invoker = ReflectionHelpers.GetConstructor(ctor);
            object result = invoker(new object[] { 256 });
            Assert.IsInstanceOf<GenericTestClass<int>>(result);
            Assert.AreEqual(256, ((GenericTestClass<int>)result).Value);
        }

        [Test]
        public void ConstructorInvokerFallsBackWhenExpressionsDisabled()
        {
            ConstructorInfo ctor = typeof(GenericTestClass<int>).GetConstructor(
                new[] { typeof(int) }
            );
            using (
                ReflectionHelpers.OverrideReflectionCapabilities(
                    expressions: false,
                    dynamicIl: false
                )
            )
            {
                Func<object[], object> invoker = ReflectionHelpers.GetConstructor(ctor);
                object result = invoker(new object[] { 64 });
                Assert.IsInstanceOf<GenericTestClass<int>>(result);
                Assert.AreEqual(64, ((GenericTestClass<int>)result).Value);
            }
        }

        [Test]
        public void IndexerGetterThrowsOnInvalidIndexCount()
        {
            PropertyInfo idxProp = typeof(IndexerClass).GetProperty("Item");
            Func<object, object[], object> getter = ReflectionHelpers.GetIndexerGetter(idxProp);
            IndexerClass obj = new();
            Assert.Throws<IndexOutOfRangeException>(() => getter(obj, Array.Empty<object>()));
        }

        [Test]
        public void IndexerHelpersFallbackWhenExpressionsDisabled()
        {
            PropertyInfo idxProp = typeof(IndexerClass).GetProperty("Item");
            using (
                ReflectionHelpers.OverrideReflectionCapabilities(
                    expressions: false,
                    dynamicIl: false
                )
            )
            {
                Func<object, object[], object> getter = ReflectionHelpers.GetIndexerGetter(idxProp);
                Action<object, object, object[]> setter = ReflectionHelpers.GetIndexerSetter(
                    idxProp
                );
                IndexerClass obj = new();
                setter(obj, 99, new object[] { 3 });
                Assert.AreEqual(99, getter(obj, new object[] { 3 }));
            }
        }

        [Test]
        public void TypedFieldGetterBoxesValueTypeWhenRequested()
        {
            FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
            Func<TestClass, object> getter = ReflectionHelpers.GetFieldGetter<TestClass, object>(
                field
            );
            TestClass instance = new() { intValue = 512 };
            Assert.AreEqual(512, getter(instance));
        }

        [Test]
        public void TypedFieldSetterUnboxesValueTypes()
        {
            FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
            FieldSetter<TestClass, object> setter = ReflectionHelpers.GetFieldSetter<
                TestClass,
                object
            >(field);
            TestClass instance = new();
            setter(ref instance, 2048);
            Assert.AreEqual(2048, instance.intValue);
        }

        [Test]
        public void TypedStaticFieldSetterUnboxesValueTypes()
        {
            FieldInfo field = typeof(TestClass).GetField(
                nameof(TestClass.StaticIntValue),
                BindingFlags.Static | BindingFlags.Public
            );
            int original = TestClass.StaticIntValue;
            try
            {
                Action<object> setter = ReflectionHelpers.GetStaticFieldSetter<object>(field);
                setter(1024);
                Assert.AreEqual(1024, TestClass.StaticIntValue);
            }
            finally
            {
                TestClass.StaticIntValue = original;
            }
        }

        [Test]
        public void TypedFieldGetterCastsReferenceTypes()
        {
            VariantPropertyClass instance = new() { objectField = "hello" };
            FieldInfo field = typeof(VariantPropertyClass).GetField(
                nameof(VariantPropertyClass.objectField)
            );
            Func<VariantPropertyClass, string> getter = ReflectionHelpers.GetFieldGetter<
                VariantPropertyClass,
                string
            >(field);
            Assert.AreEqual("hello", getter(instance));
        }

        [Test]
        public void TypedFieldSetterFallbacksWhenCapabilitiesDisabled()
        {
            FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
            using (
                ReflectionHelpers.OverrideReflectionCapabilities(
                    expressions: false,
                    dynamicIl: false
                )
            )
            {
                FieldSetter<TestClass, int> setter = ReflectionHelpers.GetFieldSetter<
                    TestClass,
                    int
                >(field);
                TestClass instance = new();
                setter(ref instance, 321);
                Assert.AreEqual(321, instance.intValue);
            }
        }

        [Test]
        public void TypedPropertySetterFallbacksWhenCapabilitiesDisabled()
        {
            PropertyInfo property = typeof(GenericTestClass<int>).GetProperty(
                nameof(GenericTestClass<int>.Value)
            );
            using (
                ReflectionHelpers.OverrideReflectionCapabilities(
                    expressions: false,
                    dynamicIl: false
                )
            )
            {
                Action<GenericTestClass<int>, int> setter = ReflectionHelpers.GetPropertySetter<
                    GenericTestClass<int>,
                    int
                >(property);
                GenericTestClass<int> instance = new();
                setter(instance, 87);
                Assert.AreEqual(87, instance.Value);
            }
        }

        [Test]
        public void TypedPropertyGetterFallbacksWhenCapabilitiesDisabled()
        {
            PropertyInfo property = typeof(GenericTestClass<int>).GetProperty(
                nameof(GenericTestClass<int>.Value)
            );
            GenericTestClass<int> instance = new(45);
            using (
                ReflectionHelpers.OverrideReflectionCapabilities(
                    expressions: false,
                    dynamicIl: false
                )
            )
            {
                Func<GenericTestClass<int>, int> getter = ReflectionHelpers.GetPropertyGetter<
                    GenericTestClass<int>,
                    int
                >(property);
                Assert.AreEqual(45, getter(instance));
            }
        }

        [Test]
        public void TypedFieldSetterCastsReferenceTypes()
        {
            VariantPropertyClass instance = new();
            FieldInfo field = typeof(VariantPropertyClass).GetField(
                nameof(VariantPropertyClass.objectField)
            );
            FieldSetter<VariantPropertyClass, string> setter = ReflectionHelpers.GetFieldSetter<
                VariantPropertyClass,
                string
            >(field);
            setter(ref instance, "updated");
            Assert.AreEqual("updated", instance.objectField);
        }

        [Test]
        public void TypedStaticFieldSetterCastsReferenceTypes()
        {
            FieldInfo field = typeof(VariantPropertyClass).GetField(
                nameof(VariantPropertyClass.StaticObjectField),
                BindingFlags.Static | BindingFlags.Public
            );
            object original = VariantPropertyClass.StaticObjectField;
            try
            {
                Action<string> setter = ReflectionHelpers.GetStaticFieldSetter<string>(field);
                setter("world");
                Func<string> getter = ReflectionHelpers.GetStaticFieldGetter<string>(field);
                Assert.AreEqual("world", getter());
            }
            finally
            {
                VariantPropertyClass.StaticObjectField = original;
            }
        }

        [Test]
        public void TypedFieldGetterSupportsFallbackWhenInstanceTypeDiffers()
        {
            FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
            Func<object, int> getter = ReflectionHelpers.GetFieldGetter<object, int>(field);
            TestClass instance = new() { intValue = 77 };
            object boxed = instance;
            Assert.AreEqual(77, getter(boxed));
        }

        [Test]
        public void TypedFieldSetterSupportsFallbackWhenInstanceTypeDiffers()
        {
            FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
            FieldSetter<object, int> setter = ReflectionHelpers.GetFieldSetter<object, int>(field);
            object boxed = new TestClass();
            setter(ref boxed, 88);
            Assert.AreEqual(88, ((TestClass)boxed).intValue);
        }

        [Test]
        public void TypedStaticInvokerWrongReturnTypeThrows()
        {
            MethodInfo m0 = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticIntMethod)
            );
            Assert.Throws<ArgumentException>(() =>
                ReflectionHelpers.GetStaticMethodInvoker<string>(m0)
            );
        }

        [Test]
        public void TypedInstanceInvokerWrongInstanceTypeThrows()
        {
            MethodInfo mi = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceIntMethod)
            );
            Assert.Throws<ArgumentException>(() =>
                ReflectionHelpers.GetInstanceMethodInvoker<string, int>(mi)
            );
        }

        [Test]
        public void ValueTypeInstanceTypedInvoker()
        {
            MethodInfo mi = typeof(ValueStruct).GetMethod(nameof(ValueStruct.Sum));
            Func<ValueStruct, int, int, int> inv = ReflectionHelpers.GetInstanceMethodInvoker<
                ValueStruct,
                int,
                int,
                int
            >(mi);
            ValueStruct vs = new() { x = 1, y = 2 };
            Assert.AreEqual(1 + 2 + 1 + 2, inv(vs, 1, 2));
        }

        [Test]
        public void HashSetAdderTypeMismatchThrows()
        {
            object set = ReflectionHelpers.CreateHashSet(typeof(int), 0);
            Action<object, object> add = ReflectionHelpers.GetHashSetAdder(typeof(int));
            Assert.Throws<InvalidCastException>(() => add(set, "oops"));
        }

        [Test]
        public void ArrayCreatorNegativeLengthThrows()
        {
            Func<int, Array> makeObj = ReflectionHelpers.GetArrayCreator(typeof(int));
            Assert.Throws<OverflowException>(() => makeObj(-1));
            Func<int, int[]> makeT = ReflectionHelpers.GetArrayCreator<int>();
            Assert.Throws<OverflowException>(() => makeT(-1));
        }

        [Test]
        public void LoadStaticFieldsAndPropertiesForType()
        {
            Dictionary<string, FieldInfo> fields =
                ReflectionHelpers.LoadStaticFieldsForType<SelfReferentialType>();
            Dictionary<string, PropertyInfo> props =
                ReflectionHelpers.LoadStaticPropertiesForType<SelfReferentialType>();
            Assert.IsTrue(fields.ContainsKey(nameof(SelfReferentialType.InstanceField)));
            Assert.IsTrue(fields.ContainsKey(nameof(SelfReferentialType.InstanceField).ToLower()));
            Assert.IsTrue(props.ContainsKey(nameof(SelfReferentialType.InstanceProperty)));
            Assert.IsTrue(
                props.ContainsKey(nameof(SelfReferentialType.InstanceProperty).ToLower())
            );
        }

        [Test]
        public void IndexerSetterInvalidIndexTypeThrows()
        {
            PropertyInfo idx = typeof(IndexerClass).GetProperty("Item");
            Action<object, object, object[]> setter = ReflectionHelpers.GetIndexerSetter(idx);
            IndexerClass obj = new();
            Assert.Throws<InvalidCastException>(() => setter(obj, 3, new object[] { "notInt" }));
        }

        [Test]
        public void StaticActionInvokerThrowsOnNonStatic()
        {
            MethodInfo m = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceVoidMethod)
            );
            Assert.Throws<ArgumentException>(() => ReflectionHelpers.GetStaticActionInvoker(m));
        }

        [Test]
        public void TypedPropertySetters()
        {
            PropertyInfo pi = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.InstanceProperty)
            );
            Action<TestPropertyClass, int> set = ReflectionHelpers.GetPropertySetter<
                TestPropertyClass,
                int
            >(pi);
            TestPropertyClass obj = new();
            set(obj, 123);
            Assert.AreEqual(123, obj.InstanceProperty);

            PropertyInfo spi = typeof(TestPropertyClass).GetProperty(
                nameof(TestPropertyClass.StaticProperty)
            );
            Action<int> sset = ReflectionHelpers.GetStaticPropertySetter<int>(spi);
            sset(456);
            Assert.AreEqual(456, TestPropertyClass.StaticProperty);
        }

        [Test]
        public void TypedStaticMethodInvokersFuncAndAction()
        {
            MethodInfo m0 = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticIntMethod)
            );
            Func<int> f0 = ReflectionHelpers.GetStaticMethodInvoker<int>(m0);
            Assert.AreEqual(42, f0());

            MethodInfo m1 = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticMethodWithParam)
            );
            Func<int, int> f1 = ReflectionHelpers.GetStaticMethodInvoker<int, int>(m1);
            Assert.AreEqual(10, f1(5));

            MethodInfo m3 = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticMethodMultipleParams)
            );
            Func<int, string, bool, int> f3 = ReflectionHelpers.GetStaticMethodInvoker<
                int,
                string,
                bool,
                int
            >(m3);
            Assert.AreEqual(1 + 3 + 1, f3(1, "hey", true));

            MethodInfo m4 = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticMethodFourParams)
            );
            Func<int, int, int, int, int> f4 = ReflectionHelpers.GetStaticMethodInvoker<
                int,
                int,
                int,
                int,
                int
            >(m4);
            Assert.AreEqual(10, f4(1, 2, 3, 4));

            MethodInfo a0 = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticVoidMethod)
            );
            TestMethodClass.StaticMethodCallCount = 0;
            Action a0i = ReflectionHelpers.GetStaticActionInvoker(a0);
            a0i();
            Assert.AreEqual(1, TestMethodClass.StaticMethodCallCount);

            MethodInfo a3 = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticActionThree)
            );
            Action<int, int, int> a3i = ReflectionHelpers.GetStaticActionInvoker<int, int, int>(a3);
            a3i(1, 2, 3);
            Assert.AreEqual(6, TestMethodClass.StaticMethodCallCount);
        }

        [Test]
        public void TypedInstanceMethodInvokersFuncAndAction()
        {
            TestMethodClass o = new();
            o.Reset();

            MethodInfo i0 = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceIntMethod)
            );
            Func<TestMethodClass, int> fi0 = ReflectionHelpers.GetInstanceMethodInvoker<
                TestMethodClass,
                int
            >(i0);
            Assert.AreEqual(100, fi0(o));
            Assert.AreEqual(1, o.instanceMethodCallCount);

            MethodInfo i1 = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceMethodWithParam)
            );
            Func<TestMethodClass, string, int> fi1 = ReflectionHelpers.GetInstanceMethodInvoker<
                TestMethodClass,
                string,
                int
            >(i1);
            Assert.AreEqual(4, fi1(o, "four"));

            MethodInfo i2 = typeof(TestMethodClass).GetMethod(nameof(TestMethodClass.InstanceSum));
            Func<TestMethodClass, int, int, int> fi2 = ReflectionHelpers.GetInstanceMethodInvoker<
                TestMethodClass,
                int,
                int,
                int
            >(i2);
            Assert.AreEqual(7, fi2(o, 3, 4));

            MethodInfo ia3 = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceSetThree)
            );
            Action<TestMethodClass, int, int, int> ai3 = ReflectionHelpers.GetInstanceActionInvoker<
                TestMethodClass,
                int,
                int,
                int
            >(ia3);
            ai3(o, 1, 2, 3);
            Assert.AreEqual(6, o.instanceMethodCallCount);

            MethodInfo i4 = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceSumFour)
            );
            Func<TestMethodClass, int, int, int, int, int> fi4 =
                ReflectionHelpers.GetInstanceMethodInvoker<
                    TestMethodClass,
                    int,
                    int,
                    int,
                    int,
                    int
                >(i4);
            Assert.AreEqual(10, fi4(o, 1, 2, 3, 4));
        }

        [Test]
        public void RefOutTypedInvokersThrow()
        {
            MethodInfo refm = typeof(RefOutMethods).GetMethod(nameof(RefOutMethods.RefInc));
            Assert.Throws<NotSupportedException>(() =>
                ReflectionHelpers.GetStaticActionInvoker<int>(refm)
            );

            MethodInfo outm = typeof(RefOutMethods).GetMethod(nameof(RefOutMethods.OutSet));
            Assert.Throws<NotSupportedException>(() =>
                ReflectionHelpers.GetStaticActionInvoker<int>(outm)
            );
        }

        [Test]
        public void IndexerGetterAndSetter()
        {
            PropertyInfo idxProp = typeof(IndexerClass).GetProperty("Item");
            Func<object, object[], object> getter = ReflectionHelpers.GetIndexerGetter(idxProp);
            Action<object, object, object[]> setter = ReflectionHelpers.GetIndexerSetter(idxProp);
            IndexerClass obj = new();
            setter(obj, 5, new object[] { 2 });
            Assert.AreEqual(5, getter(obj, new object[] { 2 }));
        }

        [Test]
        public void DictionaryCreators()
        {
            int capacity = 32;
            object dictObj = ReflectionHelpers.CreateDictionary(
                typeof(int),
                typeof(string),
                capacity
            );
            Assert.IsInstanceOf<Dictionary<int, string>>(dictObj);
            Func<int, Dictionary<int, string>> typed = ReflectionHelpers.GetDictionaryCreator<
                int,
                string
            >();
            Dictionary<int, string> d2 = typed(capacity);
            Assert.IsTrue(d2 != null);
            Assert.AreEqual(0, d2.Count);
        }

        [Test]
        public void TypedCollectionCreators()
        {
            Func<int, int[]> makeArray = ReflectionHelpers.GetArrayCreator<int>();
            int[] arr = makeArray(5);
            Assert.AreEqual(5, arr.Length);

            Func<IList> makeList = ReflectionHelpers.GetListCreator<string>();
            IList list = makeList();
            Assert.IsInstanceOf<List<string>>(list);

            Func<int, IList> makeListCap = ReflectionHelpers.GetListWithCapacityCreator<float>();
            IList listCap = makeListCap(16);
            Assert.IsInstanceOf<List<float>>(listCap);
            Assert.AreEqual(16, ((List<float>)listCap).Capacity);

            Func<int, HashSet<int>> makeSet =
                ReflectionHelpers.GetHashSetWithCapacityCreator<int>();
            HashSet<int> set = makeSet(8);
            Action<HashSet<int>, int> add = ReflectionHelpers.GetHashSetAdder<int>();
            add(set, 3);
            add(set, 3);
            add(set, 4);
            Assert.IsTrue(set.Contains(3));
            Assert.IsTrue(set.Contains(4));
            Assert.AreEqual(2, set.Count);
        }

        [Test]
        public void IsActiveAndEnabledChecksGameObjectState()
        {
            GameObject go = Track(new GameObject("probe2"));
            EnabledProbe probe = go.AddComponent<EnabledProbe>();
            go.SetActive(false);
            Assert.IsFalse(probe.IsActiveAndEnabled());
            go.SetActive(true);
            probe.enabled = true;
            Assert.IsTrue(probe.IsActiveAndEnabled());
            probe.enabled = false;
            Assert.IsFalse(probe.IsActiveAndEnabled());
        }

        [Test]
        public void InvokeInstanceVoidMethod()
        {
            TestMethodClass obj = new();
            MethodInfo m = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceVoidMethod)
            );
            object result = ReflectionHelpers.InvokeMethod(m, obj);
            Assert.IsTrue(result == null);
            Assert.AreEqual(1, obj.instanceMethodCallCount);
        }

        [Test]
        public void InvokeInstanceMethodWithReturnValue()
        {
            TestMethodClass obj = new();
            MethodInfo m = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceStringMethod)
            );
            object result = ReflectionHelpers.InvokeMethod(m, obj);
            Assert.AreEqual("instance", result);
        }

        [Test]
        public void InvokeInstanceMethodWithParameters()
        {
            TestMethodClass obj = new();
            MethodInfo m = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceMethodWithParam)
            );
            object result = ReflectionHelpers.InvokeMethod(m, obj, "hello");
            Assert.AreEqual(5, result);
        }

        [Test]
        public void BoxedSetterOnStructDoesNotMutateOriginal()
        {
            FieldInfo field = typeof(TestStruct).GetField(nameof(TestStruct.intValue));
            Action<object, object> setter = ReflectionHelpers.GetFieldSetter(field);
            TestStruct s = default;
            setter(s, 99);
            Assert.AreEqual(0, s.intValue);
        }

        [Test]
        public void GenericParameterlessConstructorThrowsOnMissing()
        {
            Assert.Throws<ArgumentException>(() =>
                ReflectionHelpers.GetParameterlessConstructor<NoParameterlessCtor>()
            );
        }

        [Test]
        public void GenericParameterlessConstructorWorks()
        {
            Func<List<int>> ctor = ReflectionHelpers.GetParameterlessConstructor<List<int>>();
            List<int> list = ctor();
            Assert.IsInstanceOf<List<int>>(list);
        }

        [Test]
        public void DictionaryWithCapacityCreator()
        {
            Func<int, Dictionary<int, string>> creator = ReflectionHelpers.GetDictionaryCreator<
                int,
                string
            >();
            Dictionary<int, string> d = creator(16);
            Assert.IsInstanceOf<Dictionary<int, string>>(d);
            d[1] = "a";
            Assert.AreEqual("a", d.ValueFor(1));
        }

        [Test]
        public void IndexerGetterSetterWrongIndexLengthThrows()
        {
            PropertyInfo prop = typeof(IndexerClass).GetProperty("Item");
            Func<object, object[], object> getter = ReflectionHelpers.GetIndexerGetter(prop);
            Action<object, object, object[]> setter = ReflectionHelpers.GetIndexerSetter(prop);
            IndexerClass obj = new();
            Assert.Throws<IndexOutOfRangeException>(() => getter(obj, new object[] { }));
            Assert.Throws<IndexOutOfRangeException>(() => setter(obj, 1, new object[] { }));
        }

        private sealed class NoParameterlessCtor
        {
            public int V { get; }

            public NoParameterlessCtor(int v)
            {
                V = v;
            }
        }
    }
}
