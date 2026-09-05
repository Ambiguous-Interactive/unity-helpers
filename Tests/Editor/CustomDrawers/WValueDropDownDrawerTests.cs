// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Base;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using PropertyAttribute = UnityEngine.PropertyAttribute;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WValueDropDownDrawerTests : CommonTestBase
    {
        [Test]
        public void ApplyOptionUpdatesFloatSerializedProperty()
        {
            WValueDropDownFloatAsset asset = CreateScriptableObject<WValueDropDownFloatAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownFloatAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate float selection property.");

            InvokeApplyOption(property, 2.5f);
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.selection, Is.EqualTo(2.5f).Within(0.0001f));
        }

        [Test]
        public void ApplyOptionUpdatesDoubleSerializedProperty()
        {
            WValueDropDownFloatAsset asset = CreateScriptableObject<WValueDropDownFloatAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownFloatAsset.preciseSelection)
            );
            Assert.IsTrue(property != null, "Failed to locate double selection property.");

            InvokeApplyOption(property, 5.25);
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.preciseSelection, Is.EqualTo(5.25d).Within(0.000001d));
        }

        [Test]
        public void CreatePropertyGUIWithoutOptionsReturnsHelpBox()
        {
            WValueDropDownNoOptionsAsset asset =
                CreateScriptableObject<WValueDropDownNoOptionsAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownNoOptionsAsset.unspecified)
            );
            Assert.IsTrue(property != null, "Failed to locate int property.");

            WValueDropDownDrawer drawer = new();
            AssignAttribute(
                drawer,
                new WValueDropDownAttribute(
                    typeof(WValueDropDownEmptySource),
                    nameof(WValueDropDownEmptySource.GetEmptyOptions)
                )
            );
            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<HelpBox>(element);
        }

        [Test]
        public void SelectorUpdatesIntSerializedProperty()
        {
            WValueDropDownIntOptionsAsset asset =
                CreateScriptableObject<WValueDropDownIntOptionsAsset>();
            asset.selection = 10;
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownIntOptionsAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate selection property.");

            WValueDropDownDrawer drawer = new();
            AssignAttribute(drawer, new WValueDropDownAttribute(10, 20, 30));
            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<BaseField<string>>(element);

            BaseField<string> selector = (BaseField<string>)element;
            DropdownField dropdown = selector.Q<DropdownField>();
            Assert.IsTrue(dropdown != null, "DropDown field was not created.");
            Assert.That(dropdown.value, Is.EqualTo("10"));

            InvokeApplySelection(selector, 2);
            serializedObject.Update();
            Assert.That(asset.selection, Is.EqualTo(30));
        }

        [Test]
        [Description(
            "Verifies selector updates string serialized property correctly (tests string property support fix)"
        )]
        public void SelectorUpdatesStringSerializedProperty()
        {
            WValueDropDownStringOptionsAsset asset =
                CreateScriptableObject<WValueDropDownStringOptionsAsset>();
            asset.selection = "Alpha";
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownStringOptionsAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate string selection property.");
            Assert.That(
                property.propertyType,
                Is.EqualTo(SerializedPropertyType.String),
                $"Property should be String type but was {property.propertyType}"
            );

            WValueDropDownDrawer drawer = new();
            WValueDropDownAttribute attribute = new("Alpha", "Beta", "Gamma");
            AssignAttribute(drawer, attribute);

            Assert.That(
                attribute.ValueType,
                Is.EqualTo(typeof(string)),
                $"Attribute ValueType should be string but was {attribute.ValueType}"
            );

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<BaseField<string>>(
                element,
                $"String property with string options should create BaseField<string>. "
                    + $"isArray: {property.isArray}, propertyType: {property.propertyType}, "
                    + $"Actual element type: {element?.GetType().Name ?? "null"}"
            );

            BaseField<string> selector = (BaseField<string>)element;
            DropdownField dropdown = selector.Q<DropdownField>();
            Assert.IsTrue(dropdown != null, "Dropdown field was not created.");
            Assert.That(dropdown.value, Is.EqualTo("Alpha"));

            InvokeApplySelection(selector, 1);
            serializedObject.Update();
            Assert.That(asset.selection, Is.EqualTo("Beta"));
        }

        [Test]
        public void InstanceMethodProviderReturnsValues()
        {
            WValueDropDownInstanceMethodAsset asset =
                CreateScriptableObject<WValueDropDownInstanceMethodAsset>();
            asset.dynamicValues.AddRange(new[] { 100, 200, 300 });
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownInstanceMethodAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate instance method backed property.");

            WValueDropDownAttribute attribute =
                PropertyDrawerTestHelper.GetAttributeFromProperty<WValueDropDownAttribute>(
                    property
                );
            Assert.IsTrue(attribute != null, "Failed to retrieve attribute.");

            object[] options = attribute.GetOptions(asset);
            Assert.That(options.Length, Is.EqualTo(3));
            Assert.That(options[0], Is.EqualTo(100));
            Assert.That(options[1], Is.EqualTo(200));
            Assert.That(options[2], Is.EqualTo(300));
        }

        [Test]
        public void InstanceMethodProviderWithNoContextReturnsEmpty()
        {
            WValueDropDownAttribute attribute = new(
                nameof(WValueDropDownInstanceMethodAsset.GetDynamicValues),
                typeof(int)
            );
            object[] options = attribute.GetOptions(null);
            Assert.That(options.Length, Is.EqualTo(0));
        }

        [Test]
        public void StaticMethodProviderReturnsValues()
        {
            WValueDropDownFloatAsset asset = CreateScriptableObject<WValueDropDownFloatAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownFloatAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate float selection property.");

            WValueDropDownAttribute attribute =
                PropertyDrawerTestHelper.GetAttributeFromProperty<WValueDropDownAttribute>(
                    property
                );
            Assert.IsTrue(attribute != null, "Failed to retrieve attribute.");

            object[] options = attribute.GetOptions(asset);
            Assert.That(options.Length, Is.EqualTo(3));
            Assert.That(options[0], Is.EqualTo(1f));
            Assert.That(options[1], Is.EqualTo(2.5f));
            Assert.That(options[2], Is.EqualTo(5f));
        }

        [Test]
        public void PopupChromeIncludesFooterPadding()
        {
            float chrome = WValueDropDownDrawer.TestHooks.CalculatePopupChromeHeight(
                includePagination: true
            );
            float searchHeight = EditorGUIUtility.singleLineHeight;
            float paginationHeight = WValueDropDownDrawer.TestHooks.PaginationButtonHeight;
            float footerHeight = chrome - (searchHeight + paginationHeight);
            float expectedFooterHeight =
                EditorGUIUtility.standardVerticalSpacing
                + WValueDropDownDrawer.TestHooks.OptionFooterPadding;
            Assert.That(footerHeight, Is.EqualTo(expectedFooterHeight).Within(0.001f));
        }

        [Test]
        public void PopupChromeAddsPaginationHeightWhenRequired()
        {
            float withPagination = WValueDropDownDrawer.TestHooks.CalculatePopupChromeHeight(
                includePagination: true
            );
            float withoutPagination = WValueDropDownDrawer.TestHooks.CalculatePopupChromeHeight(
                includePagination: false
            );
            float difference = withPagination - withoutPagination;
            float expectedDifference =
                WValueDropDownDrawer.TestHooks.PaginationButtonHeight
                - EditorGUIUtility.standardVerticalSpacing;
            Assert.That(difference, Is.EqualTo(expectedDifference).Within(0.001f));
        }

        [Test]
        public void PopupTargetHeightAggregatesChromeAndRows()
        {
            const int pageSize = 10;
            float chrome = WValueDropDownDrawer.TestHooks.CalculatePopupChromeHeight(
                includePagination: false
            );
            float rowHeight = WValueDropDownDrawer.TestHooks.GetOptionRowHeight();
            float expected = chrome + (pageSize * rowHeight);
            float actual = WValueDropDownDrawer.TestHooks.CalculatePopupTargetHeight(
                pageSize,
                includePagination: false
            );
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void PopupTargetHeightFitsSingleRowWithoutMinimumClamp()
        {
            float chrome = WValueDropDownDrawer.TestHooks.CalculatePopupChromeHeight(
                includePagination: false
            );
            float rowHeight = WValueDropDownDrawer.TestHooks.GetOptionRowHeight();
            float expected = chrome + rowHeight;
            float actual = WValueDropDownDrawer.TestHooks.CalculatePopupTargetHeight(
                1,
                includePagination: false
            );
            Assert.That(actual, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void PopupTargetHeightScalesWithLargePageSizes()
        {
            float twentyFiveRows = WValueDropDownDrawer.TestHooks.CalculatePopupTargetHeight(
                25,
                includePagination: true
            );
            float fiftyRows = WValueDropDownDrawer.TestHooks.CalculatePopupTargetHeight(
                50,
                includePagination: true
            );
            float rowHeight = WValueDropDownDrawer.TestHooks.GetOptionRowHeight();
            Assert.That(fiftyRows - twentyFiveRows, Is.EqualTo(25 * rowHeight).Within(0.001f));
        }

        [Test]
        public void PopupTargetHeightTreatsNonPositivePageSizesAsSingleRow()
        {
            float baseline = WValueDropDownDrawer.TestHooks.CalculatePopupTargetHeight(
                1,
                includePagination: false
            );
            float zero = WValueDropDownDrawer.TestHooks.CalculatePopupTargetHeight(
                0,
                includePagination: false
            );
            float negative = WValueDropDownDrawer.TestHooks.CalculatePopupTargetHeight(
                -5,
                includePagination: false
            );
            Assert.That(zero, Is.EqualTo(baseline));
            Assert.That(negative, Is.EqualTo(baseline));
        }

        [Test]
        public void OptionRowHeightMatchesControlPlusEffectiveMargin()
        {
            float control = WValueDropDownDrawer.TestHooks.GetOptionControlHeight();
            int marginVertical = WValueDropDownDrawer.TestHooks.OptionButtonMarginVertical;
            float expected =
                control + Mathf.Max(0f, marginVertical - EditorGUIUtility.standardVerticalSpacing);
            float actual = WValueDropDownDrawer.TestHooks.GetOptionRowHeight();
            Assert.That(actual, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void EmptySearchHeightLeavesRoomForHelpBox()
        {
            float emptyHeight = WValueDropDownDrawer.TestHooks.CalculateEmptySearchHeight();
            GUIStyle helpStyle = EditorStyles.helpBox;
            int helpMargin = helpStyle.margin?.horizontal ?? 0;
            float helpWidth =
                WValueDropDownDrawer.TestHooks.PopupWidthValue
                - WValueDropDownDrawer.TestHooks.EmptySearchHorizontalPaddingValue
                - helpMargin;
            helpWidth = Mathf.Max(32f, helpWidth);
            float helpHeight =
                helpStyle.CalcHeight(
                    new GUIContent(WValueDropDownDrawer.TestHooks.EmptyResultsMessageValue),
                    helpWidth
                ) + (helpStyle.margin?.vertical ?? 0);
            float expected =
                EditorGUIUtility.singleLineHeight
                + (EditorGUIUtility.standardVerticalSpacing * 4f)
                + helpHeight
                + WValueDropDownDrawer.TestHooks.OptionFooterPadding
                + WValueDropDownDrawer.TestHooks.EmptySearchExtraPaddingValue;
            Assert.That(emptyHeight, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void EmptySearchHeightPrefersMeasuredHelpBoxHeight()
        {
            const float measuredHelpHeight = 42f;
            float emptyHeight =
                WValueDropDownDrawer.TestHooks.CalculateEmptySearchHeightWithMeasurement(
                    measuredHelpHeight
                );

            float expected =
                EditorGUIUtility.singleLineHeight
                + (EditorGUIUtility.standardVerticalSpacing * 4f)
                + measuredHelpHeight
                + WValueDropDownDrawer.TestHooks.OptionFooterPadding
                + WValueDropDownDrawer.TestHooks.EmptySearchExtraPaddingValue;

            Assert.That(emptyHeight, Is.EqualTo(expected).Within(0.001f));
        }

        [TestCase(12, 5, 5, 2, TestName = "CalculateRowsOnPage.LastPage.Returns2")]
        [TestCase(0, 5, 0, 1, TestName = "CalculateRowsOnPage.EmptyList.Returns1")]
        [TestCase(6, 0, 0, 1, TestName = "CalculateRowsOnPage.ZeroPageSize.Returns1")]
        [TestCase(3, 2, -2, 2, TestName = "CalculateRowsOnPage.NegativePage.ClampsToFirst")]
        [TestCase(10, 5, 0, 5, TestName = "CalculateRowsOnPage.FirstFullPage.ReturnsPageSize")]
        [TestCase(10, 5, 1, 5, TestName = "CalculateRowsOnPage.SecondFullPage.ReturnsPageSize")]
        [TestCase(7, 5, 1, 2, TestName = "CalculateRowsOnPage.PartialLastPage.ReturnsRemaining")]
        [TestCase(5, 5, 0, 5, TestName = "CalculateRowsOnPage.ExactlyOnePage.ReturnsPageSize")]
        [TestCase(1, 10, 0, 1, TestName = "CalculateRowsOnPage.SingleItem.Returns1")]
        [TestCase(
            100,
            10,
            9,
            10,
            TestName = "CalculateRowsOnPage.LastPageExactFit.ReturnsPageSize"
        )]
        public void CalculateRowsOnPageDataDrivenScenarios(
            int filteredCount,
            int pageSize,
            int currentPage,
            int expectedRows
        )
        {
            int rows = WValueDropDownDrawer.TestHooks.CalculateRowsOnPage(
                filteredCount: filteredCount,
                pageSize: pageSize,
                currentPage: currentPage
            );
            Assert.That(
                rows,
                Is.EqualTo(expectedRows),
                $"CalculateRowsOnPage({filteredCount}, {pageSize}, {currentPage}) should return {expectedRows} but got {rows}"
            );
        }

        [Test]
        public void TypedInlineConstructorsSetsValueType()
        {
            WValueDropDownAttribute boolAttr = new(true, false);
            Assert.That(boolAttr.ValueType, Is.EqualTo(typeof(bool)));

            WValueDropDownAttribute charAttr = new('a', 'b', 'c');
            Assert.That(charAttr.ValueType, Is.EqualTo(typeof(char)));

            WValueDropDownAttribute sbyteAttr = new((sbyte)1, (sbyte)2);
            Assert.That(sbyteAttr.ValueType, Is.EqualTo(typeof(sbyte)));

            WValueDropDownAttribute byteAttr = new((byte)1, (byte)2);
            Assert.That(byteAttr.ValueType, Is.EqualTo(typeof(byte)));

            WValueDropDownAttribute shortAttr = new((short)1, (short)2);
            Assert.That(shortAttr.ValueType, Is.EqualTo(typeof(short)));

            WValueDropDownAttribute ushortAttr = new((ushort)1, (ushort)2);
            Assert.That(ushortAttr.ValueType, Is.EqualTo(typeof(ushort)));

            WValueDropDownAttribute intAttr = new(1, 2, 3);
            Assert.That(intAttr.ValueType, Is.EqualTo(typeof(int)));

            WValueDropDownAttribute uintAttr = new(1u, 2u);
            Assert.That(uintAttr.ValueType, Is.EqualTo(typeof(uint)));

            WValueDropDownAttribute longAttr = new(1L, 2L);
            Assert.That(longAttr.ValueType, Is.EqualTo(typeof(long)));

            WValueDropDownAttribute ulongAttr = new(1UL, 2UL);
            Assert.That(ulongAttr.ValueType, Is.EqualTo(typeof(ulong)));

            WValueDropDownAttribute floatAttr = new(1.5f, 2.5f);
            Assert.That(floatAttr.ValueType, Is.EqualTo(typeof(float)));

            WValueDropDownAttribute doubleAttr = new(1.5d, 2.5d);
            Assert.That(doubleAttr.ValueType, Is.EqualTo(typeof(double)));
        }

        [Test]
        public void ProviderTypeAndMethodNameAreStoredCorrectly()
        {
            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownSource),
                nameof(WValueDropDownSource.GetFloatValues)
            );
            Assert.That(attribute.ProviderType, Is.EqualTo(typeof(WValueDropDownSource)));
            Assert.That(
                attribute.ProviderMethodName,
                Is.EqualTo(nameof(WValueDropDownSource.GetFloatValues))
            );
        }

        [Test]
        public void InstanceMethodSetsProviderMethodNameButNotType()
        {
            WValueDropDownAttribute attribute = new(
                nameof(WValueDropDownInstanceMethodAsset.GetDynamicValues),
                typeof(int)
            );
            Assert.That(attribute.ProviderType, Is.Null);
            Assert.That(
                attribute.ProviderMethodName,
                Is.EqualTo(nameof(WValueDropDownInstanceMethodAsset.GetDynamicValues))
            );
            Assert.That(attribute.RequiresInstanceContext, Is.True);
        }

        [Test]
        [Description(
            "Verifies that string properties with string options are supported (fixed production bug)"
        )]
        public void StringPropertyWithStringOptionsIsSupported()
        {
            // Unity reports isArray true for a string property, which it stores as a char array.
            WValueDropDownStringOptionsAsset asset =
                CreateScriptableObject<WValueDropDownStringOptionsAsset>();
            asset.selection = "Alpha";
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownStringOptionsAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate string selection property.");
            Assert.That(
                property.propertyType,
                Is.EqualTo(SerializedPropertyType.String),
                $"Property type should be String but was {property.propertyType}"
            );

            WValueDropDownDrawer drawer = new();
            WValueDropDownAttribute attribute = new("Alpha", "Beta", "Gamma");
            AssignAttribute(drawer, attribute);

            Assert.That(
                attribute.ValueType,
                Is.EqualTo(typeof(string)),
                $"Attribute ValueType should be string but was {attribute.ValueType}"
            );

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.That(
                element,
                Is.Not.InstanceOf<HelpBox>(),
                $"String property with string options should NOT show HelpBox. "
                    + $"Element type: {element?.GetType().Name ?? "null"}"
            );
        }

        [Test]
        [TestCase(
            SerializedPropertyType.String,
            "Alpha",
            "Beta",
            TestName = "StringPropertyWithStringOptions"
        )]
        [Description("Data-driven test for property type compatibility with dropdown options")]
        public void PropertyTypeCompatibilityDataDriven(
            SerializedPropertyType expectedPropertyType,
            params string[] options
        )
        {
            WValueDropDownStringOptionsAsset asset =
                CreateScriptableObject<WValueDropDownStringOptionsAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownStringOptionsAsset.selection)
            );

            Assert.That(
                property.propertyType,
                Is.EqualTo(expectedPropertyType),
                $"Property type mismatch: expected {expectedPropertyType}, got {property.propertyType}"
            );

            WValueDropDownDrawer drawer = new();
            AssignAttribute(drawer, new WValueDropDownAttribute(options));

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.That(
                element,
                Is.Not.InstanceOf<HelpBox>(),
                $"Compatible property type {expectedPropertyType} with matching options should NOT show HelpBox"
            );
        }

        [Test]
        public void ResolveSelectedIndexReturnsNegativeOneForUnmatchedValue()
        {
            WValueDropDownIntOptionsAsset asset =
                CreateScriptableObject<WValueDropDownIntOptionsAsset>();
            asset.selection = 99;
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownIntOptionsAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate selection property.");

            int index = WValueDropDownDrawer.TestHooks.ResolveSelectedIndex(
                property,
                typeof(int),
                new object[] { 10, 20, 30 }
            );
            Assert.That(index, Is.EqualTo(-1));
        }

        [Test]
        public void ResolveSelectedIndexReturnsCorrectIndexForMatchedValue()
        {
            WValueDropDownIntOptionsAsset asset =
                CreateScriptableObject<WValueDropDownIntOptionsAsset>();
            asset.selection = 20;
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownIntOptionsAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate selection property.");

            int index = WValueDropDownDrawer.TestHooks.ResolveSelectedIndex(
                property,
                typeof(int),
                new object[] { 10, 20, 30 }
            );
            Assert.That(index, Is.EqualTo(1));
        }

        [Test]
        public void FormatOptionCachedReturnsTypeNameForEmptyToString()
        {
            EmptyToStringHelper instance = new();
            string result = WValueDropDownDrawer.TestHooks.FormatOptionCached(instance);
            Assert.That(result, Is.Not.Null.And.Not.Empty);
            Assert.That(result, Does.Contain(nameof(EmptyToStringHelper)));
        }

        [Test]
        public void FormatOptionCachedReturnsNonEmptyForNullToString()
        {
            NullToStringHelper instance = new();
            string result = WValueDropDownDrawer.TestHooks.FormatOptionCached(instance);
            Assert.That(result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void FormatOptionCachedHandlesNullOption()
        {
            string result = WValueDropDownDrawer.TestHooks.FormatOptionCached(null);
            Assert.That(result, Is.EqualTo("(null)"));
        }

        [Test]
        [TestCaseSource(nameof(FormatOptionCachedNeverReturnsEmptyStringData))]
        public void FormatOptionCachedNeverReturnsEmptyString(object input)
        {
            string result = WValueDropDownDrawer.TestHooks.FormatOptionCached(input);
            Assert.That(result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void BuildDisplayLabelsProducesNoEmptyStrings()
        {
            object[] options = new object[] { 42, null };
            string[] labels = WValueDropDownDrawer.TestHooks.BuildDisplayLabelsUncached(options);
            Assert.That(labels.Length, Is.EqualTo(options.Length));
            foreach (string label in labels)
            {
                Assert.That(label, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        public void BuildDisplayLabelsHandlesEmptyToStringWithFallback()
        {
            object[] options = new object[]
            {
                new EmptyToStringHelper(),
                new NullToStringHelper(),
                42,
                null,
            };
            string[] labels = WValueDropDownDrawer.TestHooks.BuildDisplayLabelsUncached(options);
            Assert.That(labels.Length, Is.EqualTo(options.Length));
            foreach (string label in labels)
            {
                Assert.That(label, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        public void SelectorWithUnmatchedValueDefaultsToFirstOption()
        {
            WValueDropDownStringOptionsAsset asset =
                CreateScriptableObject<WValueDropDownStringOptionsAsset>();
            asset.selection = "NonExistent";
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownStringOptionsAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate string selection property.");

            int rawIndex = WValueDropDownDrawer.TestHooks.ResolveSelectedIndex(
                property,
                typeof(string),
                new object[] { "Alpha", "Beta", "Gamma" }
            );
            Assert.That(rawIndex, Is.EqualTo(-1));
            Assert.That(Mathf.Max(0, rawIndex), Is.EqualTo(0));
        }

        private static IEnumerable<TestCaseData> FormatOptionCachedNeverReturnsEmptyStringData()
        {
            yield return new TestCaseData(null).SetName("Option.Null");
            yield return new TestCaseData(new EmptyToStringHelper()).SetName(
                "Option.EmptyToString"
            );
            yield return new TestCaseData(new NullToStringHelper()).SetName("Option.NullToString");
            yield return new TestCaseData(42).SetName("Option.Integer");
            yield return new TestCaseData("hello").SetName("Option.String");
        }

        private static void InvokeApplyOption(SerializedProperty property, object value)
        {
            WValueDropDownDrawer.ApplyOption(property, value);
        }

        private static void AssignAttribute(PropertyDrawer drawer, PropertyAttribute attribute)
        {
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);
        }

        private static void InvokeApplySelection(BaseField<string> selector, int optionIndex)
        {
            WDropDownSelectorBase<string> dropDownSelector =
                selector as WDropDownSelectorBase<string>;
            Assert.IsTrue(
                dropDownSelector != null,
                $"Expected selector to derive from WDropDownSelectorBase<string>, but was {selector?.GetType().FullName ?? "null"}."
            );
            dropDownSelector.ApplySelection(optionIndex);
        }

        private sealed class EmptyToStringHelper
        {
            public override string ToString()
            {
                return "";
            }
        }

        private sealed class NullToStringHelper
        {
            public override string ToString()
            {
                return null;
            }
        }
    }
#endif
}
