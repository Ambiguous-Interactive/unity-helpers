// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WButton
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class WButtonGuiHeaderTests
    {
        [SetUp]
        public void SetUp()
        {
            WButtonGUI.ClearGroupDataForTesting();
        }

        [TearDown]
        public void TearDown()
        {
            WButtonGUI.ClearGroupDataForTesting();
        }

        [Test]
        public void BuildGroupHeaderNoGroupingSuffixWhenSingleGroup()
        {
            WButtonGUI.ClearGroupDataForTesting();

            // Draw order >= -1 is top placement
            GUIContent header = WButtonGUI.BuildGroupHeader(-1);
            Assert.That(header.text, Is.EqualTo(WButtonStyles.TopGroupLabel.text));
        }

        [Test]
        public void BuildGroupHeaderAppendsDrawOrderWhenMultipleGroups()
        {
            /*
                The legacy int-based API uses UseGlobalSetting, which defaults to TopGroupLabel.
                DrawOrder does NOT decide the label style -- only GroupPlacement does. See
                UseGlobalSettingWithAnyDrawOrderUsesTopLabel in WButtonDrawOrderTests.
            */
            Dictionary<int, int> counts = new() { { -1, 3 }, { -5, 2 } };
            WButtonGUI.SetGroupCountsForTesting(counts);

            GUIContent topHeader = WButtonGUI.BuildGroupHeader(-1);
            GUIContent bottomHeader = WButtonGUI.BuildGroupHeader(-5);

            Assert.That(
                topHeader.text,
                Is.EqualTo($"{WButtonStyles.TopGroupLabel.text} (-1)"),
                $"Top header should use TopGroupLabel. Actual: '{topHeader.text}'"
            );
            // Legacy API creates keys with UseGlobalSetting, which defaults to TopGroupLabel.
            Assert.That(
                bottomHeader.text,
                Is.EqualTo($"{WButtonStyles.TopGroupLabel.text} (-5)"),
                $"Legacy API with UseGlobalSetting defaults to TopGroupLabel regardless of drawOrder. Actual: '{bottomHeader.text}'"
            );
        }

        [Test]
        public void BuildGroupHeaderUsesCustomGroupNameWhenProvided()
        {
            Dictionary<int, int> counts = new() { { -1, 2 }, { -4, 1 } };
            WButtonGUI.SetGroupCountsForTesting(counts);

            Dictionary<int, string> names = new() { { -4, "Networking" } };
            WButtonGUI.SetGroupNamesForTesting(names);

            GUIContent custom = WButtonGUI.BuildGroupHeader(-4);
            GUIContent defaultHeader = WButtonGUI.BuildGroupHeader(-1);

            Assert.That(custom.text, Is.EqualTo("Networking"));
            Assert.That(defaultHeader.text, Is.EqualTo($"{WButtonStyles.TopGroupLabel.text} (-1)"));
        }

        [Test]
        public void BuildGroupHeaderExplicitBottomPlacementUsesBottomLabel()
        {
            // When using explicit GroupPlacement.Bottom, should use BottomGroupLabel
            WButtonGroupKey topKey = new(0, -1, null, 0, WButtonGroupPlacement.Top);
            WButtonGroupKey bottomKey = new(0, -2, null, 0, WButtonGroupPlacement.Bottom);

            Dictionary<WButtonGroupKey, int> counts = WButtonGUI.GetGroupCountsForTesting();
            counts[topKey] = 2;
            counts[bottomKey] = 1;

            GUIContent topHeader = WButtonGUI.BuildGroupHeader(topKey);
            GUIContent bottomHeader = WButtonGUI.BuildGroupHeader(bottomKey);

            Assert.That(
                topHeader.text,
                Does.StartWith(WButtonStyles.TopGroupLabel.text),
                $"Explicit Top placement should use TopGroupLabel. Actual: '{topHeader.text}'"
            );
            Assert.That(
                bottomHeader.text,
                Does.StartWith(WButtonStyles.BottomGroupLabel.text),
                $"Explicit Bottom placement should use BottomGroupLabel. Actual: '{bottomHeader.text}'"
            );
        }

        [TestCase(
            -1,
            "Actions",
            Description = "DrawOrder -1 with UseGlobalSetting uses TopGroupLabel"
        )]
        [TestCase(
            -5,
            "Actions",
            Description = "DrawOrder -5 with UseGlobalSetting uses TopGroupLabel"
        )]
        [TestCase(
            0,
            "Actions",
            Description = "DrawOrder 0 with UseGlobalSetting uses TopGroupLabel"
        )]
        public void BuildGroupHeaderLegacyApiAlwaysUsesTopLabel(
            int drawOrder,
            string expectedLabelPrefix
        )
        {
            // Legacy int-based API always creates UseGlobalSetting keys, which default to TopGroupLabel
            Dictionary<int, int> counts = new() { { drawOrder, 1 }, { drawOrder - 10, 1 } };
            WButtonGUI.SetGroupCountsForTesting(counts);

            GUIContent header = WButtonGUI.BuildGroupHeader(drawOrder);

            Assert.That(
                header.text,
                Does.StartWith(expectedLabelPrefix),
                $"Legacy API with drawOrder {drawOrder} should use '{expectedLabelPrefix}' prefix. Actual: '{header.text}'"
            );
        }
    }
}
#endif
