// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class DropDownAttributeTests
    {
        [UnityTearDown]
        public IEnumerator VerifyNoUnexpectedLogs()
        {
            // These synchronous [Test]s emit [Error] logs (WValueDropDown provider errors) that
            // they LogAssert.Expect. Without spanning a frame, an error can flush at the NEXT frame
            // boundary -- bleeding into a later PlayMode fixture's teardown (the cross-test pollution
            // that failed ~20 RelationalComponentsZenjectTests). Pump a frame so the log flushes
            // within THIS fixture, then reconcile here where the expectations were registered.
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void StringInListInlineOptionsReturnSameReferences()
        {
            string[] values = { "Alpha", "Beta", "Gamma" };
            StringInListAttribute attribute = new(values);
            CollectionAssert.AreEqual(values, attribute.List);
        }

        [Test]
        public void StringInListMethodProviderReturnsValues()
        {
            StringInListAttribute attribute = new(
                typeof(StringProviders),
                nameof(StringProviders.GetStringValues)
            );
            CollectionAssert.AreEqual(new[] { "One", "Two", "Three" }, attribute.List);
        }

        [Test]
        public void StringInListEnumerableProviderSupported()
        {
            StringInListAttribute attribute = new(
                typeof(StringProviders),
                nameof(StringProviders.GetEnumerableStrings)
            );
            CollectionAssert.AreEqual(new[] { "Red", "Green" }, attribute.List);
        }

        [Test]
        public void StringInListMissingMethodLogsErrorAndReturnsEmpty()
        {
            Regex pattern = new("WValueDropDownAttribute.*Could not locate.*Missing");
            LogAssert.Expect(LogType.Error, pattern);
            StringInListAttribute attribute = new(typeof(StringProviders), "Missing");
            string[] result = attribute.List;
            CollectionAssert.IsEmpty(
                result,
                "Expected empty list when provider method is missing, but got: [{0}]",
                string.Join(", ", result ?? Array.Empty<string>())
            );
        }

        [Test]
        public void StringInListMethodThrowingExceptionLogsErrorAndReturnsEmpty()
        {
            Regex pattern = new(
                "WValueDropDownAttribute.*ThrowingProvider.*InvalidOperationException"
            );
            LogAssert.Expect(LogType.Error, pattern);
            StringInListAttribute attribute = new(
                typeof(StringProviders),
                nameof(StringProviders.ThrowingProvider)
            );
            string[] result = attribute.List;
            CollectionAssert.IsEmpty(
                result,
                "Expected empty list when provider throws, but got: [{0}]",
                string.Join(", ", result ?? Array.Empty<string>())
            );
        }

        [Test]
        public void StringInListInstanceMethodUsesContext()
        {
            InstanceStringProvider provider = new() { Prefix = "Ctx" };
            StringInListAttribute attribute = new(nameof(InstanceStringProvider.BuildStates));
            CollectionAssert.AreEqual(new[] { "Ctx_A", "Ctx_B" }, attribute.GetOptions(provider));
        }

        [Test]
        public void StringInListInstanceMethodFallsBackToStatic()
        {
            InstanceStringProvider provider = new();
            StringInListAttribute attribute = new(nameof(InstanceStringProvider.StaticStates));
            CollectionAssert.AreEqual(
                new[] { "Static_X", "Static_Y" },
                attribute.GetOptions(provider)
            );
        }

        [Test]
        public void StringInListTypeAndMethodConstructorWithInstanceMethodUsesContext()
        {
            InstanceStringProvider provider = new() { Prefix = "TypeArg" };
            StringInListAttribute attribute = new(
                typeof(InstanceStringProvider),
                nameof(InstanceStringProvider.BuildStates)
            );
            CollectionAssert.AreEqual(
                new[] { "TypeArg_A", "TypeArg_B" },
                attribute.GetOptions(provider)
            );
        }

        [Test]
        public void StringInListTypeAndMethodConstructorPrefersStaticMethod()
        {
            InstanceStringProvider provider = new();
            StringInListAttribute attribute = new(
                typeof(InstanceStringProvider),
                nameof(InstanceStringProvider.StaticStates)
            );
            // Static method should work without context
            CollectionAssert.AreEqual(new[] { "Static_X", "Static_Y" }, attribute.List);
        }

        [Test]
        public void IntDropDownTypeAndMethodConstructorWithInstanceMethodUsesContext()
        {
            InstanceIntProvider provider = new() { Multiplier = 2 };
            IntDropDownAttribute attribute = new(
                typeof(InstanceIntProvider),
                nameof(InstanceIntProvider.GetDynamicValues)
            );
            CollectionAssert.AreEqual(new[] { 20, 40, 60 }, attribute.GetOptions(provider));
        }

        [Test]
        public void IntDropDownTypeAndMethodConstructorWithEnumerableInstanceMethod()
        {
            InstanceIntProvider provider = new() { Multiplier = 3 };
            IntDropDownAttribute attribute = new(
                typeof(InstanceIntProvider),
                nameof(InstanceIntProvider.GetEnumerableValues)
            );
            CollectionAssert.AreEqual(new[] { 15, 45 }, attribute.GetOptions(provider));
        }

        [Test]
        public void IntDropDownTypeAndMethodConstructorPrefersStaticMethod()
        {
            InstanceIntProvider provider = new() { Multiplier = 5 };
            IntDropDownAttribute attribute = new(
                typeof(InstanceIntProvider),
                nameof(InstanceIntProvider.GetStaticValues)
            );
            // Static method should work without context - multiplier is ignored
            CollectionAssert.AreEqual(new[] { 100, 200, 300 }, attribute.Options);
        }

        [Test]
        public void WValueDropDownTypeAndMethodConstructorWithInstanceMethodUsesContext()
        {
            InstanceValueProvider provider = new() { Suffix = "_Test" };
            WValueDropDownAttribute attribute = new(
                typeof(InstanceValueProvider),
                nameof(InstanceValueProvider.GetItems)
            );
            object[] options = attribute.GetOptions(provider);
            Assert.AreEqual(2, options.Length);
            Assert.IsInstanceOf<DropDownItem>(options[0]);
            Assert.AreEqual("Item1_Test", ((DropDownItem)options[0]).Name);
            Assert.IsInstanceOf<DropDownItem>(options[1]);
            Assert.AreEqual("Item2_Test", ((DropDownItem)options[1]).Name);
        }

        [Test]
        public void WValueDropDownTypeMethodAndValueTypeConstructorWithInstanceMethod()
        {
            InstanceValueProvider provider = new();
            WValueDropDownAttribute attribute = new(
                typeof(InstanceValueProvider),
                nameof(InstanceValueProvider.GetFloatValues),
                typeof(float)
            );
            object[] options = attribute.GetOptions(provider);
            Assert.AreEqual(3, options.Length);
            Assert.AreEqual(1.5f, options[0]);
            Assert.AreEqual(2.5f, options[1]);
            Assert.AreEqual(3.5f, options[2]);
        }

        [Test]
        public void IntDropDownInstanceMethodWithNullContextReturnsEmpty()
        {
            IntDropDownAttribute attribute = new(
                typeof(InstanceIntProvider),
                nameof(InstanceIntProvider.GetDynamicValues)
            );
            // Without context, instance method should return empty
            int[] result = attribute.Options;
            CollectionAssert.IsEmpty(
                result,
                "Expected empty options when instance method is used without context"
            );
        }

        [Test]
        public void StringInListInstanceMethodWithNullContextReturnsEmpty()
        {
            StringInListAttribute attribute = new(
                typeof(InstanceStringProvider),
                nameof(InstanceStringProvider.BuildStates)
            );
            // Without context, instance method should return empty
            string[] result = attribute.List;
            CollectionAssert.IsEmpty(
                result,
                "Expected empty list when instance method is used without context"
            );
        }

        [Test]
        public void IntDropDownInlineOptionsReturnSameReferences()
        {
            int[] values = { 2, 4, 6 };
            IntDropDownAttribute attribute = new(values);
            CollectionAssert.AreEqual(values, attribute.Options);
        }

        [Test]
        public void IntDropDownMethodProviderReturnsValues()
        {
            IntDropDownAttribute attribute = new(
                typeof(IntProviders),
                nameof(IntProviders.GetValues)
            );
            CollectionAssert.AreEqual(new[] { 1, 5, 9 }, attribute.Options);
        }

        [Test]
        public void IntDropDownEnumerableProviderSupported()
        {
            IntDropDownAttribute attribute = new(
                typeof(IntProviders),
                nameof(IntProviders.GetEnumerableValues)
            );
            CollectionAssert.AreEqual(new[] { 10, 20 }, attribute.Options);
        }

        [Test]
        public void IntDropDownMissingMethodLogsErrorAndReturnsEmpty()
        {
            Regex pattern = new("WValueDropDownAttribute.*Could not locate.*Missing");
            LogAssert.Expect(LogType.Error, pattern);
            IntDropDownAttribute attribute = new(typeof(IntProviders), "Missing");
            int[] result = attribute.Options;
            CollectionAssert.IsEmpty(
                result,
                "Expected empty options when provider method is missing, but got: [{0}]",
                string.Join(", ", result ?? Array.Empty<int>())
            );
        }

        [Test]
        public void IntDropDownMethodThrowingExceptionLogsErrorAndReturnsEmpty()
        {
            Regex pattern = new(
                "WValueDropDownAttribute.*ThrowingProvider.*InvalidOperationException"
            );
            LogAssert.Expect(LogType.Error, pattern);
            IntDropDownAttribute attribute = new(
                typeof(IntProviders),
                nameof(IntProviders.ThrowingProvider)
            );
            int[] result = attribute.Options;
            CollectionAssert.IsEmpty(
                result,
                "Expected empty options when provider throws, but got: [{0}]",
                string.Join(", ", result ?? Array.Empty<int>())
            );
        }

        [TestCase(typeof(StringProviders), "NonExistent", "NonExistent")]
        [TestCase(typeof(StringProviders), "Missing", "Missing")]
        [TestCase(typeof(StringProviders), "GetStringValues_Typo", "GetStringValues_Typo")]
        public void StringInListMissingMethodLogsErrorWithMethodName(
            Type providerType,
            string methodName,
            string expectedMethodInError
        )
        {
            Regex pattern = new(
                $"WValueDropDownAttribute.*Could not locate.*{expectedMethodInError}"
            );
            LogAssert.Expect(LogType.Error, pattern);
            StringInListAttribute attribute = new(providerType, methodName);
            CollectionAssert.IsEmpty(
                attribute.List,
                "Expected empty list for missing method '{0}' on type '{1}'",
                methodName,
                providerType.Name
            );
        }

        [TestCase(typeof(IntProviders), "NonExistent", "NonExistent")]
        [TestCase(typeof(IntProviders), "Missing", "Missing")]
        [TestCase(typeof(IntProviders), "GetValues_Typo", "GetValues_Typo")]
        public void IntDropDownMissingMethodLogsErrorWithMethodName(
            Type providerType,
            string methodName,
            string expectedMethodInError
        )
        {
            Regex pattern = new(
                $"WValueDropDownAttribute.*Could not locate.*{expectedMethodInError}"
            );
            LogAssert.Expect(LogType.Error, pattern);
            IntDropDownAttribute attribute = new(providerType, methodName);
            CollectionAssert.IsEmpty(
                attribute.Options,
                "Expected empty options for missing method '{0}' on type '{1}'",
                methodName,
                providerType.Name
            );
        }

        [TestCase(1, new[] { 10, 20, 30 }, Description = "Multiplier 1 returns base values")]
        [TestCase(2, new[] { 20, 40, 60 }, Description = "Multiplier 2 doubles values")]
        [TestCase(5, new[] { 50, 100, 150 }, Description = "Multiplier 5 quintuples values")]
        [TestCase(0, new[] { 0, 0, 0 }, Description = "Multiplier 0 returns zeros")]
        public void IntDropDownInstanceMethodWithTypeConstructorDataDriven(
            int multiplier,
            int[] expectedValues
        )
        {
            InstanceIntProvider provider = new() { Multiplier = multiplier };
            IntDropDownAttribute attribute = new(
                typeof(InstanceIntProvider),
                nameof(InstanceIntProvider.GetDynamicValues)
            );
            CollectionAssert.AreEqual(
                expectedValues,
                attribute.GetOptions(provider),
                "Instance method should return dynamically computed values based on context state"
            );
        }

        [TestCase("A", new[] { "A_A", "A_B" }, Description = "Prefix A")]
        [TestCase("Test", new[] { "Test_A", "Test_B" }, Description = "Prefix Test")]
        [TestCase("", new[] { "_A", "_B" }, Description = "Empty prefix")]
        public void StringInListInstanceMethodWithTypeConstructorDataDriven(
            string prefix,
            string[] expectedValues
        )
        {
            InstanceStringProvider provider = new() { Prefix = prefix };
            StringInListAttribute attribute = new(
                typeof(InstanceStringProvider),
                nameof(InstanceStringProvider.BuildStates)
            );
            CollectionAssert.AreEqual(
                expectedValues,
                attribute.GetOptions(provider),
                "Instance method should return dynamically computed values based on context state"
            );
        }

        [TestCase("_Suffix1", "Item1_Suffix1", "Item2_Suffix1")]
        [TestCase("", "Item1", "Item2")]
        [TestCase("_X", "Item1_X", "Item2_X")]
        public void WValueDropDownInstanceMethodWithTypeConstructorDataDriven(
            string suffix,
            string expectedItem1,
            string expectedItem2
        )
        {
            InstanceValueProvider provider = new() { Suffix = suffix };
            WValueDropDownAttribute attribute = new(
                typeof(InstanceValueProvider),
                nameof(InstanceValueProvider.GetItems)
            );
            object[] options = attribute.GetOptions(provider);
            Assert.AreEqual(2, options.Length);
            Assert.IsInstanceOf<DropDownItem>(options[0]);
            Assert.AreEqual(expectedItem1, ((DropDownItem)options[0]).Name);
            Assert.IsInstanceOf<DropDownItem>(options[1]);
            Assert.AreEqual(expectedItem2, ((DropDownItem)options[1]).Name);
        }

        [Test]
        public void StringInListNullProviderTypeLogsErrorAndReturnsEmpty()
        {
            Regex pattern = new("WValueDropDownAttribute.*Provider type cannot be null");
            LogAssert.Expect(LogType.Error, pattern);
            StringInListAttribute attribute = new((Type)null, "SomeMethod");
            CollectionAssert.IsEmpty(
                attribute.List,
                "Expected empty list when provider type is null"
            );
        }

        [Test]
        public void IntDropDownNullProviderTypeLogsErrorAndReturnsEmpty()
        {
            Regex pattern = new("WValueDropDownAttribute.*Provider type cannot be null");
            LogAssert.Expect(LogType.Error, pattern);
            IntDropDownAttribute attribute = new(null, "SomeMethod");
            CollectionAssert.IsEmpty(
                attribute.Options,
                "Expected empty options when provider type is null"
            );
        }

        [TestCase(null)]
        [TestCase("")]
        public void StringInListNullOrEmptyStaticMethodNameLogsErrorAndReturnsEmpty(
            string methodName
        )
        {
            Regex pattern = new("WValueDropDownAttribute.*Method name cannot be null or empty");
            LogAssert.Expect(LogType.Error, pattern);
            StringInListAttribute attribute = new(typeof(StringProviders), methodName);
            CollectionAssert.IsEmpty(
                attribute.List,
                "Expected empty list when static method name is '{0}'",
                methodName ?? "<null>"
            );
        }

        [TestCase(null)]
        [TestCase("")]
        public void IntDropDownNullOrEmptyStaticMethodNameLogsErrorAndReturnsEmpty(
            string methodName
        )
        {
            Regex pattern = new("WValueDropDownAttribute.*Method name cannot be null or empty");
            LogAssert.Expect(LogType.Error, pattern);
            IntDropDownAttribute attribute = new(typeof(IntProviders), methodName);
            CollectionAssert.IsEmpty(
                attribute.Options,
                "Expected empty options when static method name is '{0}'",
                methodName ?? "<null>"
            );
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void StringInListNullOrEmptyInstanceMethodNameLogsErrorAndReturnsEmpty(
            string methodName
        )
        {
            Regex pattern = new("WValueDropDownAttribute.*Method name cannot be null or empty");
            LogAssert.Expect(LogType.Error, pattern);
            StringInListAttribute attribute = new(methodName);
            CollectionAssert.IsEmpty(
                attribute.List,
                "Expected empty list when instance method name is '{0}'",
                methodName ?? "<null>"
            );
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void IntDropDownNullOrEmptyInstanceMethodNameLogsErrorAndReturnsEmpty(
            string methodName
        )
        {
            Regex pattern = new("WValueDropDownAttribute.*Method name cannot be null or empty");
            LogAssert.Expect(LogType.Error, pattern);
            IntDropDownAttribute attribute = new(methodName);
            CollectionAssert.IsEmpty(
                attribute.Options,
                "Expected empty options when instance method name is '{0}'",
                methodName ?? "<null>"
            );
        }

        [Test]
        public void StringInListProviderReturningEmptyArrayReturnsEmpty()
        {
            StringInListAttribute attribute = new(
                typeof(EmptyProviders),
                nameof(EmptyProviders.GetEmptyStrings)
            );
            CollectionAssert.IsEmpty(
                attribute.List,
                "Expected empty list when provider returns empty array"
            );
        }

        [Test]
        public void IntDropDownProviderReturningEmptyArrayReturnsEmpty()
        {
            IntDropDownAttribute attribute = new(
                typeof(EmptyProviders),
                nameof(EmptyProviders.GetEmptyInts)
            );
            CollectionAssert.IsEmpty(
                attribute.Options,
                "Expected empty options when provider returns empty array"
            );
        }

        [Test]
        public void StringInListProviderReturningNullReturnsEmpty()
        {
            StringInListAttribute attribute = new(
                typeof(NullProviders),
                nameof(NullProviders.GetNullStrings)
            );
            CollectionAssert.IsEmpty(
                attribute.List,
                "Expected empty list when provider returns null"
            );
        }

        [Test]
        public void IntDropDownProviderReturningNullReturnsEmpty()
        {
            IntDropDownAttribute attribute = new(
                typeof(NullProviders),
                nameof(NullProviders.GetNullInts)
            );
            CollectionAssert.IsEmpty(
                attribute.Options,
                "Expected empty options when provider returns null"
            );
        }

        [Test]
        public void StringInListThrowingArgumentExceptionLogsErrorAndReturnsEmpty()
        {
            Regex pattern = new(
                "WValueDropDownAttribute.*ArgumentThrowingProvider.*ArgumentException"
            );
            LogAssert.Expect(LogType.Error, pattern);
            StringInListAttribute attribute = new(
                typeof(ExceptionProviders),
                nameof(ExceptionProviders.ArgumentThrowingProvider)
            );
            CollectionAssert.IsEmpty(
                attribute.List,
                "Expected empty list when provider throws ArgumentException"
            );
        }

        [Test]
        public void StringInListThrowingNullReferenceExceptionLogsErrorAndReturnsEmpty()
        {
            Regex pattern = new(
                "WValueDropDownAttribute.*NullReferenceThrowingProvider.*NullReferenceException"
            );
            LogAssert.Expect(LogType.Error, pattern);
            StringInListAttribute attribute = new(
                typeof(ExceptionProviders),
                nameof(ExceptionProviders.NullReferenceThrowingProvider)
            );
            CollectionAssert.IsEmpty(
                attribute.List,
                "Expected empty list when provider throws NullReferenceException"
            );
        }

        [Test]
        public void WValueDropDownInlineFloatsConvertSuccessfully()
        {
            WValueDropDownAttribute attribute = new(typeof(float), 1, 2.5f, "3.75");
            object[] options = attribute.Options;
            Assert.AreEqual(3, options.Length);
            Assert.AreEqual(1f, options[0]);
            Assert.AreEqual(2.5f, options[1]);
            Assert.AreEqual(3.75f, options[2]);
        }

        [Test]
        public void WValueDropDownMethodProviderHandlesDoubles()
        {
            WValueDropDownAttribute attribute = new(
                typeof(ValueProviders),
                nameof(ValueProviders.GetDoubles),
                typeof(double)
            );
            object[] options = attribute.Options;
            Assert.AreEqual(2, options.Length);
            Assert.AreEqual(12.5d, options[0]);
            Assert.AreEqual(42.25d, options[1]);
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsFloats()
        {
            WValueDropDownAttribute attribute = new(1f, 2.5f);
            Assert.AreEqual(typeof(float), attribute.ValueType);
            object[] options = attribute.Options;
            Assert.AreEqual(2, options.Length);
            Assert.AreEqual(1f, options[0]);
            Assert.AreEqual(2.5f, options[1]);
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsBooleans()
        {
            WValueDropDownAttribute attribute = new(true, false, true);
            AssertOptions(attribute, typeof(bool), new[] { true, false, true });
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsCharacters()
        {
            WValueDropDownAttribute attribute = new('A', 'B');
            AssertOptions(attribute, typeof(char), new[] { 'A', 'B' });
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsStrings()
        {
            WValueDropDownAttribute attribute = new("Alpha", "Beta");
            AssertOptions(attribute, typeof(string), new[] { "Alpha", "Beta" });
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsSignedBytes()
        {
            WValueDropDownAttribute attribute = new((sbyte)1, (sbyte)-2);
            AssertOptions(attribute, typeof(sbyte), new[] { (sbyte)1, (sbyte)-2 });
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsUnsignedBytes()
        {
            WValueDropDownAttribute attribute = new((byte)1, (byte)200);
            AssertOptions(attribute, typeof(byte), new[] { (byte)1, (byte)200 });
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsShorts()
        {
            WValueDropDownAttribute attribute = new((short)10, (short)-20);
            AssertOptions(attribute, typeof(short), new[] { (short)10, (short)-20 });
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsUnsignedShorts()
        {
            WValueDropDownAttribute attribute = new((ushort)10, (ushort)20);
            AssertOptions(attribute, typeof(ushort), new[] { (ushort)10, (ushort)20 });
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsIntegers()
        {
            WValueDropDownAttribute attribute = new(10, -30, 50);
            AssertOptions(attribute, typeof(int), new[] { 10, -30, 50 });
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsUnsignedIntegers()
        {
            WValueDropDownAttribute attribute = new(10u, 30u);
            AssertOptions(attribute, typeof(uint), new[] { 10u, 30u });
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsLongs()
        {
            WValueDropDownAttribute attribute = new(10L, -20L);
            AssertOptions(attribute, typeof(long), new[] { 10L, -20L });
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsUnsignedLongs()
        {
            WValueDropDownAttribute attribute = new(10UL, 20UL);
            AssertOptions(attribute, typeof(ulong), new[] { 10UL, 20UL });
        }

        [Test]
        public void WValueDropDownTypedConstructorSupportsDoubles()
        {
            WValueDropDownAttribute attribute = new(1.25d, 2.5d);
            AssertOptions(attribute, typeof(double), new[] { 1.25d, 2.5d });
        }

        [Test]
        public void WValueDropDownEnumConversionSupportsNames()
        {
            WValueDropDownAttribute attribute = new(
                typeof(ValueProviders),
                nameof(ValueProviders.GetEnumNames),
                typeof(TestEnum)
            );
            object[] options = attribute.Options;
            Assert.AreEqual(2, options.Length);
            Assert.AreEqual(TestEnum.First, options[0]);
            Assert.AreEqual(TestEnum.Second, options[1]);
        }

        [Test]
        public void WValueDropDownInvalidConversionLogsErrorAndSkips()
        {
            Regex pattern = new("WValueDropDownAttribute");
            LogAssert.Expect(LogType.Error, pattern);
            WValueDropDownAttribute attribute = new(typeof(byte), 1, 512);
            object[] options = attribute.Options;
            Assert.AreEqual(1, options.Length);
            Assert.AreEqual((byte)1, options[0]);
        }

        [Test]
        public void WValueDropDownMissingProviderLogsErrorAndReturnsEmpty()
        {
            Regex pattern = new("WValueDropDownAttribute");
            LogAssert.Expect(LogType.Error, pattern);
            WValueDropDownAttribute attribute = new(
                typeof(ValueProviders),
                "Missing",
                typeof(float)
            );
            CollectionAssert.IsEmpty(attribute.Options);
        }

        [Test]
        public void WValueDropDownProviderInfersElementType()
        {
            WValueDropDownAttribute attribute = new(
                typeof(ValueProviders),
                nameof(ValueProviders.GetShorts)
            );
            Assert.AreEqual(typeof(short), attribute.ValueType);
            object[] options = attribute.Options;
            Assert.AreEqual(3, options.Length);
            Assert.AreEqual((short)1, options[0]);
            Assert.AreEqual((short)2, options[1]);
            Assert.AreEqual((short)3, options[2]);
        }

        [Test]
        public void WValueDropDownProviderSupportsCustomStructs()
        {
            WValueDropDownAttribute attribute = new(
                typeof(ValueProviders),
                nameof(ValueProviders.GetDropdownItems)
            );

            Assert.AreEqual(typeof(DropDownItem), attribute.ValueType);
            object[] options = attribute.Options;
            Assert.AreEqual(2, options.Length);
            Assert.IsInstanceOf<DropDownItem>(options[0]);
            Assert.AreEqual("Alpha", ((DropDownItem)options[0]).Name);
            Assert.IsInstanceOf<DropDownItem>(options[1]);
            Assert.AreEqual("Beta", ((DropDownItem)options[1]).Name);
        }

        [Test]
        public void WValueDropDownProviderSupportsCustomReferenceTypes()
        {
            WValueDropDownAttribute attribute = new(
                typeof(ValueProviders),
                nameof(ValueProviders.GetCustomReferences)
            );

            Assert.AreEqual(typeof(CustomReference), attribute.ValueType);
            object[] options = attribute.Options;
            Assert.AreEqual(2, options.Length);
            Assert.IsInstanceOf<CustomReference>(options[0]);
            Assert.AreEqual("First", ((CustomReference)options[0]).Identifier);
            Assert.IsInstanceOf<CustomReference>(options[1]);
            Assert.AreEqual("Second", ((CustomReference)options[1]).Identifier);
        }

        [Test]
        public void WValueDropDownProviderSupportsArrays()
        {
            WValueDropDownAttribute attribute = new(
                typeof(ValueProviders),
                nameof(ValueProviders.GetDropdownItemArray)
            );

            Assert.AreEqual(typeof(DropDownItem), attribute.ValueType);
            object[] options = attribute.Options;
            Assert.AreEqual(2, options.Length);
            Assert.IsInstanceOf<DropDownItem>(options[0]);
            Assert.AreEqual("ArrayAlpha", ((DropDownItem)options[0]).Name);
            Assert.IsInstanceOf<DropDownItem>(options[1]);
            Assert.AreEqual("ArrayBeta", ((DropDownItem)options[1]).Name);
        }

        [Test]
        public void WValueDropDownProviderReturningNullCollectionReturnsEmpty()
        {
            WValueDropDownAttribute attribute = new(
                typeof(ValueProviders),
                nameof(ValueProviders.GetNullCollection)
            );

            Assert.AreEqual(typeof(int), attribute.ValueType);
            CollectionAssert.IsEmpty(attribute.Options);
        }

        [Test]
        public void WValueDropDownProviderThrowingLogsErrorAndReturnsEmpty()
        {
            Regex pattern = new("WValueDropDownAttribute");
            LogAssert.Expect(LogType.Error, pattern);
            WValueDropDownAttribute attribute = new(
                typeof(ValueProviders),
                nameof(ValueProviders.ThrowingProvider)
            );
            Assert.AreEqual(typeof(int), attribute.ValueType);
            CollectionAssert.IsEmpty(attribute.Options);
        }

        [Test]
        public void WValueDropDownExplicitTypeConvertsEntries()
        {
            WValueDropDownAttribute attribute = new(
                typeof(ValueProviders),
                nameof(ValueProviders.GetIntList),
                typeof(long)
            );

            Assert.AreEqual(typeof(long), attribute.ValueType);
            object[] options = attribute.Options;
            Assert.AreEqual(3, options.Length);
            Assert.AreEqual(10L, options[0]);
            Assert.AreEqual(20L, options[1]);
            Assert.AreEqual(30L, options[2]);
        }

        [Test]
        public void WValueDropDownProviderSupportsEnumerableOfObject()
        {
            WValueDropDownAttribute attribute = new(
                typeof(ValueProviders),
                nameof(ValueProviders.GetObjectEnumerable)
            );

            Assert.AreEqual(typeof(object), attribute.ValueType);
            object[] options = attribute.Options;
            Assert.AreEqual(2, options.Length);
            Assert.IsInstanceOf<DropDownItem>(options[0]);
            Assert.AreEqual("Gamma", ((DropDownItem)options[0]).Name);
            Assert.AreEqual("Literal", options[1]);
        }

        [Test]
        public void WValueDropDownProviderWithInvalidReturnLogsError()
        {
            Regex pattern = new("WValueDropDownAttribute");
            LogAssert.Expect(LogType.Error, pattern);
            WValueDropDownAttribute attribute = new(
                typeof(ValueProviders),
                nameof(ValueProviders.GetInvalidProvider)
            );
            Assert.AreEqual(typeof(object), attribute.ValueType);
            CollectionAssert.IsEmpty(attribute.Options);
        }

        [TestCase(
            typeof(InvalidReturnTypeProviders),
            nameof(InvalidReturnTypeProviders.ReturnVoid)
        )]
        [TestCase(typeof(InvalidReturnTypeProviders), nameof(InvalidReturnTypeProviders.ReturnInt))]
        [TestCase(
            typeof(InvalidReturnTypeProviders),
            nameof(InvalidReturnTypeProviders.ReturnString)
        )]
        [TestCase(
            typeof(InvalidReturnTypeProviders),
            nameof(InvalidReturnTypeProviders.ReturnBool)
        )]
        public void WValueDropDownProviderWithNonEnumerableReturnLogsError(
            Type providerType,
            string methodName
        )
        {
            Regex pattern = new("WValueDropDownAttribute.*(must return|Could not locate)");
            LogAssert.Expect(LogType.Error, pattern);
            WValueDropDownAttribute attribute = new(providerType, methodName);
            CollectionAssert.IsEmpty(
                attribute.Options,
                "Expected empty options for provider method '{0}' with invalid return type on '{1}'",
                methodName,
                providerType.Name
            );
        }

        [TestCase(typeof(ValueProviders), "CompletelyMissing")]
        [TestCase(typeof(ValueProviders), "NonExistentMethod")]
        [TestCase(typeof(ValueProviders), "GetDropDownItems.Typo")]
        public void WValueDropDownMissingMethodLogsErrorWithMethodName(
            Type providerType,
            string methodName
        )
        {
            Regex pattern = new($"WValueDropDownAttribute.*Could not locate.*{methodName}");
            LogAssert.Expect(LogType.Error, pattern);
            WValueDropDownAttribute attribute = new(providerType, methodName);
            CollectionAssert.IsEmpty(
                attribute.Options,
                "Expected empty options for missing method '{0}' on type '{1}'",
                methodName,
                providerType.Name
            );
        }

        [TestCase(typeof(ValueProviders), "CompletelyMissing", typeof(int))]
        [TestCase(typeof(StringProviders), "MissingMethod", typeof(string))]
        public void WValueDropDownThreeArgConstructorMissingMethodLogsError(
            Type providerType,
            string methodName,
            Type valueType
        )
        {
            Regex pattern = new($"WValueDropDownAttribute.*Could not locate.*{methodName}");
            LogAssert.Expect(LogType.Error, pattern);
            WValueDropDownAttribute attribute = new(providerType, methodName, valueType);
            CollectionAssert.IsEmpty(
                attribute.Options,
                "Expected empty options for missing method '{0}' with valueType '{1}' on type '{2}'",
                methodName,
                valueType.Name,
                providerType.Name
            );
        }

        [Test]
        public void MethodWithParametersNotMatchedAsProvider()
        {
            Regex pattern = new("WValueDropDownAttribute.*Could not locate.*GetValuesWithParam");
            LogAssert.Expect(LogType.Error, pattern);
            IntDropDownAttribute attribute = new(
                typeof(MethodWithParametersProvider),
                nameof(MethodWithParametersProvider.GetValuesWithParam)
            );
            CollectionAssert.IsEmpty(
                attribute.Options,
                "Expected empty options when provider method requires parameters"
            );
        }

        private static void AssertOptions<T>(
            WValueDropDownAttribute attribute,
            Type expectedType,
            T[] expectedValues
        )
        {
            Assert.AreEqual(expectedType, attribute.ValueType);
            object[] options = attribute.Options;
            Assert.AreEqual(expectedValues.Length, options.Length);

            for (int index = 0; index < expectedValues.Length; index += 1)
            {
                Assert.IsInstanceOf<T>(options[index]);
                Assert.AreEqual(expectedValues[index], (T)options[index]);
            }
        }

        private static class StringProviders
        {
            public static string[] GetStringValues()
            {
                return new[] { "One", "Two", "Three" };
            }

            public static IEnumerable<string> GetEnumerableStrings()
            {
                return new List<string> { "Red", "Green" };
            }

            public static string[] ThrowingProvider()
            {
                throw new InvalidOperationException("Example");
            }
        }

        private static class IntProviders
        {
            public static int[] GetValues()
            {
                return new[] { 1, 5, 9 };
            }

            public static IEnumerable<int> GetEnumerableValues()
            {
                return new List<int> { 10, 20 };
            }

            public static int[] ThrowingProvider()
            {
                throw new InvalidOperationException("Example");
            }
        }

        private static class ValueProviders
        {
            public static IEnumerable<double> GetDoubles()
            {
                return new List<double> { 12.5d, 42.25d };
            }

            public static IEnumerable<string> GetEnumNames()
            {
                return new List<string> { nameof(TestEnum.First), nameof(TestEnum.Second) };
            }

            public static short[] GetShorts()
            {
                return new[] { (short)1, (short)2, (short)3 };
            }

            public static IEnumerable<DropDownItem> GetDropdownItems()
            {
                return new List<DropDownItem> { new("Alpha"), new("Beta") };
            }

            public static DropDownItem[] GetDropdownItemArray()
            {
                return new[] { new DropDownItem("ArrayAlpha"), new DropDownItem("ArrayBeta") };
            }

            public static List<CustomReference> GetCustomReferences()
            {
                return new List<CustomReference> { new("First"), new("Second") };
            }

            public static IEnumerable<int> GetNullCollection()
            {
                return null;
            }

            public static IEnumerable<int> ThrowingProvider()
            {
                throw new InvalidOperationException("Example");
            }

            public static IEnumerable<int> GetIntList()
            {
                return new List<int> { 10, 20, 30 };
            }

            public static IEnumerable<object> GetObjectEnumerable()
            {
                return new List<object> { new DropDownItem("Gamma"), "Literal" };
            }

            public static int GetInvalidProvider()
            {
                return 1;
            }
        }

        private enum TestEnum
        {
            First,
            Second,
        }

        private readonly struct DropDownItem
        {
            public DropDownItem(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }

        private sealed class CustomReference
        {
            public CustomReference(string identifier)
            {
                Identifier = identifier;
            }

            public string Identifier { get; }
        }

        private sealed class InstanceStringProvider
        {
            public string Prefix { get; set; } = "Default";

            public string[] BuildStates()
            {
                return new[] { $"{Prefix}_A", $"{Prefix}_B" };
            }

            public static IEnumerable<string> StaticStates()
            {
                return new[] { "Static_X", "Static_Y" };
            }
        }

        private sealed class InstanceIntProvider
        {
            public int Multiplier { get; set; } = 1;

            public int[] GetDynamicValues()
            {
                return new[] { 10 * Multiplier, 20 * Multiplier, 30 * Multiplier };
            }

            public IEnumerable<int> GetEnumerableValues()
            {
                return new List<int> { 5 * Multiplier, 15 * Multiplier };
            }

            public static int[] GetStaticValues()
            {
                return new[] { 100, 200, 300 };
            }
        }

        private sealed class InstanceValueProvider
        {
            public string Suffix { get; set; } = string.Empty;

            public IEnumerable<DropDownItem> GetItems()
            {
                return new List<DropDownItem> { new($"Item1{Suffix}"), new($"Item2{Suffix}") };
            }

            public float[] GetFloatValues()
            {
                return new[] { 1.5f, 2.5f, 3.5f };
            }
        }

        private static class EmptyProviders
        {
            public static string[] GetEmptyStrings()
            {
                return Array.Empty<string>();
            }

            public static int[] GetEmptyInts()
            {
                return Array.Empty<int>();
            }
        }

        private static class NullProviders
        {
            public static string[] GetNullStrings()
            {
                return null;
            }

            public static int[] GetNullInts()
            {
                return null;
            }
        }

        private static class ExceptionProviders
        {
            public static string[] ArgumentThrowingProvider()
            {
                throw new ArgumentException("Argument error");
            }

            public static string[] NullReferenceThrowingProvider()
            {
                throw new NullReferenceException("Null reference error");
            }
        }

        private static class InvalidReturnTypeProviders
        {
            public static void ReturnVoid() { }

            public static int ReturnInt()
            {
                return 42;
            }

            public static string ReturnString()
            {
                return "Hello";
            }

            public static bool ReturnBool()
            {
                return true;
            }
        }

        private static class MethodWithParametersProvider
        {
            public static int[] GetValuesWithParam(int count)
            {
                return new int[count];
            }

            public static int[] GetValuesNoParams()
            {
                return new[] { 1, 2, 3 };
            }
        }
    }
}
