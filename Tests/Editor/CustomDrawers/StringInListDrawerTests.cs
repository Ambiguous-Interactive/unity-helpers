// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
#if UNITY_EDITOR
    using System;
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
    public sealed class StringInListDrawerTests : CommonTestBase
    {
        [Test]
        public void CreatePropertyGUIWithoutOptionsReturnsHelpBox()
        {
            StringInListNoOptionsAsset asset = CreateScriptableObject<StringInListNoOptionsAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(StringInListNoOptionsAsset.unspecified)
            );
            Assert.IsTrue(property != null, "Failed to locate string property.");

            StringInListDrawer drawer = new();
            AssignAttribute(drawer, new StringInListAttribute());
            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<HelpBox>(element);
        }

        [Test]
        public void SelectorUpdatesStringSerializedProperty()
        {
            StringInListStringOptionsAsset asset =
                CreateScriptableObject<StringInListStringOptionsAsset>();
            asset.state = "Run";
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(StringInListStringOptionsAsset.state)
            );
            Assert.IsTrue(property != null, "Failed to locate state property.");

            StringInListDrawer drawer = new();
            AssignAttribute(drawer, new StringInListAttribute("Idle", "Run", "Jump"));
            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<BaseField<string>>(element);

            BaseField<string> selector = (BaseField<string>)element;
            DropdownField dropdown = selector.Q<DropdownField>();
            Assert.IsTrue(dropdown != null, "DropDown field was not created.");
            Assert.That(dropdown.value, Is.EqualTo("Run"));

            InvokeApplySelection(selector, 2);
            serializedObject.Update();
            Assert.That(asset.state, Is.EqualTo("Jump"));
        }

        [Test]
        public void SelectorWritesSelectedIndexToIntegerProperty()
        {
            StringInListIntegerOptionsAsset asset =
                CreateScriptableObject<StringInListIntegerOptionsAsset>();
            asset.selection = 0;
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(StringInListIntegerOptionsAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate integer-backed dropdown.");

            StringInListDrawer drawer = new();
            AssignAttribute(drawer, new StringInListAttribute("Low", "Medium", "High"));
            VisualElement element = drawer.CreatePropertyGUI(property);
            DropdownField dropdown = element.Q<DropdownField>();
            Assert.IsTrue(dropdown != null, "DropDown field was not created.");

            InvokeApplySelection((BaseField<string>)element, 2);
            serializedObject.Update();
            Assert.That(asset.selection, Is.EqualTo(2));
        }

        [Test]
        public void PopupChromeIncludesFooterPadding()
        {
            float chrome = StringInListDrawer.TestHooks.CalculatePopupChromeHeight(
                includePagination: true
            );
            float searchHeight = EditorGUIUtility.singleLineHeight;
            float paginationHeight = StringInListDrawer.TestHooks.PaginationButtonHeight;
            float footerHeight = chrome - (searchHeight + paginationHeight);
            float expectedFooterHeight =
                EditorGUIUtility.standardVerticalSpacing
                + StringInListDrawer.TestHooks.OptionFooterPadding;
            Assert.That(footerHeight, Is.EqualTo(expectedFooterHeight).Within(0.001f));
        }

        [Test]
        public void PopupChromeAddsPaginationHeightWhenRequired()
        {
            float withPagination = StringInListDrawer.TestHooks.CalculatePopupChromeHeight(
                includePagination: true
            );
            float withoutPagination = StringInListDrawer.TestHooks.CalculatePopupChromeHeight(
                includePagination: false
            );
            float difference = withPagination - withoutPagination;
            float expectedDifference =
                StringInListDrawer.TestHooks.PaginationButtonHeight
                - EditorGUIUtility.standardVerticalSpacing;
            Assert.That(difference, Is.EqualTo(expectedDifference).Within(0.001f));
        }

        [Test]
        public void PopupTargetHeightAggregatesChromeAndRows()
        {
            const int pageSize = 10;
            float chrome = StringInListDrawer.TestHooks.CalculatePopupChromeHeight(
                includePagination: false
            );
            float rowHeight = StringInListDrawer.TestHooks.GetOptionRowHeight();
            float expected = chrome + (pageSize * rowHeight);
            float actual = StringInListDrawer.TestHooks.CalculatePopupTargetHeight(
                pageSize,
                includePagination: false
            );
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void PopupTargetHeightFitsSingleRowWithoutMinimumClamp()
        {
            float chrome = StringInListDrawer.TestHooks.CalculatePopupChromeHeight(
                includePagination: false
            );
            float rowHeight = StringInListDrawer.TestHooks.GetOptionRowHeight();
            float expected = chrome + rowHeight;
            float actual = StringInListDrawer.TestHooks.CalculatePopupTargetHeight(
                1,
                includePagination: false
            );
            Assert.That(actual, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void PopupTargetHeightScalesWithLargePageSizes()
        {
            float twentyFiveRows = StringInListDrawer.TestHooks.CalculatePopupTargetHeight(
                25,
                includePagination: true
            );
            float fiftyRows = StringInListDrawer.TestHooks.CalculatePopupTargetHeight(
                50,
                includePagination: true
            );
            float rowHeight = StringInListDrawer.TestHooks.GetOptionRowHeight();
            Assert.That(fiftyRows - twentyFiveRows, Is.EqualTo(25 * rowHeight).Within(0.001f));
        }

        [Test]
        public void PopupTargetHeightTreatsNonPositivePageSizesAsSingleRow()
        {
            float baseline = StringInListDrawer.TestHooks.CalculatePopupTargetHeight(
                1,
                includePagination: false
            );
            float zero = StringInListDrawer.TestHooks.CalculatePopupTargetHeight(
                0,
                includePagination: false
            );
            float negative = StringInListDrawer.TestHooks.CalculatePopupTargetHeight(
                -5,
                includePagination: false
            );
            Assert.That(zero, Is.EqualTo(baseline));
            Assert.That(negative, Is.EqualTo(baseline));
        }

        [Test]
        public void OptionRowHeightMatchesControlPlusEffectiveMargin()
        {
            float control = StringInListDrawer.TestHooks.GetOptionControlHeight();
            int marginVertical = StringInListDrawer.TestHooks.OptionButtonMarginVertical;
            float expected =
                control + Mathf.Max(0f, marginVertical - EditorGUIUtility.standardVerticalSpacing);
            float actual = StringInListDrawer.TestHooks.GetOptionRowHeight();
            Assert.That(actual, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void EmptySearchHeightLeavesRoomForHelpBox()
        {
            float emptyHeight = StringInListDrawer.TestHooks.CalculateEmptySearchHeight();
            GUIStyle helpStyle = EditorStyles.helpBox;
            int helpMargin = helpStyle.margin?.horizontal ?? 0;
            float helpWidth =
                StringInListDrawer.TestHooks.PopupWidthValue
                - StringInListDrawer.TestHooks.EmptySearchHorizontalPaddingValue
                - helpMargin;
            helpWidth = Mathf.Max(32f, helpWidth);
            float helpHeight =
                helpStyle.CalcHeight(
                    new GUIContent(StringInListDrawer.TestHooks.EmptyResultsMessageValue),
                    helpWidth
                ) + (helpStyle.margin?.vertical ?? 0);
            /*
                Production code sums: searchRow (singleLineHeight + standardVerticalSpacing) +
                topSpacer + helpBoxHeight + bottomSpacer + footer (standardVerticalSpacing +
                OptionBottomPadding + EmptySearchExtraPadding), which is where the four vertical
                spacings below come from.
            */
            float expected =
                EditorGUIUtility.singleLineHeight
                + (EditorGUIUtility.standardVerticalSpacing * 4f)
                + helpHeight
                + StringInListDrawer.TestHooks.OptionFooterPadding
                + StringInListDrawer.TestHooks.EmptySearchExtraPaddingValue;
            Assert.That(emptyHeight, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void EmptySearchHeightPrefersMeasuredHelpBoxHeight()
        {
            const float measuredHelpHeight = 42f;
            float emptyHeight =
                StringInListDrawer.TestHooks.CalculateEmptySearchHeightWithMeasurement(
                    measuredHelpHeight
                );

            // Same formula as CalculateEmptySearchHeight but with measured height
            float expected =
                EditorGUIUtility.singleLineHeight
                + (EditorGUIUtility.standardVerticalSpacing * 4f)
                + measuredHelpHeight
                + StringInListDrawer.TestHooks.OptionFooterPadding
                + StringInListDrawer.TestHooks.EmptySearchExtraPaddingValue;

            Assert.That(emptyHeight, Is.EqualTo(expected).Within(0.001f));
        }

        // Data-driven test for CalculateRowsOnPage covering various scenarios
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
            int rows = StringInListDrawer.TestHooks.CalculateRowsOnPage(
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
        public void BackingAttributeIsWValueDropDown()
        {
            StringInListAttribute attribute = new("Alpha", "Beta", "Gamma");
            WValueDropDownAttribute backingAttribute = attribute.BackingAttribute;
            Assert.IsTrue(backingAttribute != null);
            Assert.That(backingAttribute.ValueType, Is.EqualTo(typeof(string)));
        }

        [Test]
        public void InlineListReturnsCorrectOptions()
        {
            StringInListAttribute attribute = new("Option1", "Option2", "Option3");
            string[] options = attribute.List;
            Assert.That(options.Length, Is.EqualTo(3));
            Assert.That(options[0], Is.EqualTo("Option1"));
            Assert.That(options[1], Is.EqualTo("Option2"));
            Assert.That(options[2], Is.EqualTo("Option3"));
        }

        [Test]
        public void StaticProviderReturnsCorrectOptions()
        {
            StringInListAttribute attribute = new(
                typeof(StringOptionsProvider),
                nameof(StringOptionsProvider.GetOptions)
            );
            string[] options = attribute.List;
            Assert.That(options.Length, Is.EqualTo(3));
            Assert.That(options[0], Is.EqualTo("Static1"));
            Assert.That(options[1], Is.EqualTo("Static2"));
            Assert.That(options[2], Is.EqualTo("Static3"));
        }

        [Test]
        public void InstanceMethodProviderReturnsValues()
        {
            StringInListInstanceMethodAsset asset =
                CreateScriptableObject<StringInListInstanceMethodAsset>();
            asset.dynamicValues.AddRange(new[] { "Dynamic1", "Dynamic2" });
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(StringInListInstanceMethodAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate instance method backed property.");

            StringInListAttribute attribute = GetAttributeFromProperty<StringInListAttribute>(
                property
            );
            Assert.IsTrue(attribute != null, "Failed to retrieve attribute.");

            string[] options = attribute.GetOptions(asset);
            Assert.That(options.Length, Is.EqualTo(2));
            Assert.That(options[0], Is.EqualTo("Dynamic1"));
            Assert.That(options[1], Is.EqualTo("Dynamic2"));
        }

        [Test]
        public void InstanceMethodProviderWithNoContextReturnsEmpty()
        {
            StringInListAttribute attribute = new(
                nameof(StringInListInstanceMethodAsset.GetDynamicValues)
            );
            string[] options = attribute.GetOptions(null);
            Assert.That(options.Length, Is.EqualTo(0));
        }

        [Test]
        public void RequiresInstanceContextIsTrueForInstanceProvider()
        {
            StringInListAttribute attribute = new(
                nameof(StringInListInstanceMethodAsset.GetDynamicValues)
            );
            Assert.That(attribute.RequiresInstanceContext, Is.True);
        }

        [Test]
        public void RequiresInstanceContextIsFalseForInlineList()
        {
            StringInListAttribute attribute = new("A", "B", "C");
            Assert.That(attribute.RequiresInstanceContext, Is.False);
        }

        [Test]
        public void RequiresInstanceContextIsFalseForStaticProvider()
        {
            StringInListAttribute attribute = new(
                typeof(StringOptionsProvider),
                nameof(StringOptionsProvider.GetOptions)
            );
            Assert.That(attribute.RequiresInstanceContext, Is.False);
        }

        [Test]
        public void ProviderTypeAndMethodNameAreStoredCorrectly()
        {
            StringInListAttribute attribute = new(
                typeof(StringOptionsProvider),
                nameof(StringOptionsProvider.GetOptions)
            );
            Assert.That(attribute.ProviderType, Is.EqualTo(typeof(StringOptionsProvider)));
            Assert.That(
                attribute.ProviderMethodName,
                Is.EqualTo(nameof(StringOptionsProvider.GetOptions))
            );
        }

        [Test]
        public void InstanceMethodSetsProviderMethodNameButNotType()
        {
            StringInListAttribute attribute = new(
                nameof(StringInListInstanceMethodAsset.GetDynamicValues)
            );
            Assert.That(attribute.ProviderType, Is.Null);
            Assert.That(
                attribute.ProviderMethodName,
                Is.EqualTo(nameof(StringInListInstanceMethodAsset.GetDynamicValues))
            );
        }

        [Test]
        public void EmptyListReturnsEmptyArray()
        {
            StringInListAttribute attribute = new();
            string[] options = attribute.List;
            Assert.That(options.Length, Is.EqualTo(0));
        }

        [Test]
        public void SelectorWithUnmatchedStringDefaultsToFirstOption()
        {
            StringInListStringOptionsAsset asset =
                CreateScriptableObject<StringInListStringOptionsAsset>();
            asset.state = "NonExistent";
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(StringInListStringOptionsAsset.state)
            );
            Assert.IsTrue(property != null, "Failed to locate state property.");

            StringInListDrawer drawer = new();
            AssignAttribute(drawer, new StringInListAttribute("Idle", "Run", "Jump"));
            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<BaseField<string>>(element);

            BaseField<string> selector = (BaseField<string>)element;
            DropdownField dropdown = selector.Q<DropdownField>();
            Assert.IsTrue(dropdown != null, "DropDown field was not created.");
            Assert.That(dropdown.value, Is.EqualTo("Idle"));
        }

        [Test]
        public void SelectorWithOutOfRangeIntegerDefaultsToFirstOption()
        {
            StringInListIntegerOptionsAsset asset =
                CreateScriptableObject<StringInListIntegerOptionsAsset>();
            asset.selection = 999;
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(StringInListIntegerOptionsAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate integer-backed dropdown.");

            StringInListDrawer drawer = new();
            AssignAttribute(drawer, new StringInListAttribute("Low", "Medium", "High"));
            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<BaseField<string>>(element);

            BaseField<string> selector = (BaseField<string>)element;
            DropdownField dropdown = selector.Q<DropdownField>();
            Assert.IsTrue(dropdown != null, "DropDown field was not created.");
            Assert.That(dropdown.value, Is.EqualTo("Low"));
        }

        private static T GetAttributeFromProperty<T>(SerializedProperty property)
            where T : Attribute
        {
            return PropertyDrawerTestHelper.GetAttributeFromProperty<T>(property);
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
    }
#endif
}
