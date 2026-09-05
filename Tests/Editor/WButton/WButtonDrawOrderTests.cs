// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WButton
{
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;

    /// <summary>
    /// Comprehensive tests for WButton draw order and grouping functionality.
    /// These tests verify that:
    /// - Arbitrary integer draw orders work correctly
    /// - Draw order >= -1 renders at top, < -1 renders at bottom
    /// - Different group names at same draw order render separately
    /// - Declaration order is preserved within groups
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WButtonDrawOrderTests : CommonTestBase
    {
        [Test]
        public void MetadataSortedByDrawOrderAscending()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonArbitraryDrawOrderTarget)
            );

            Assert.That(metadata, Is.Not.Empty);

            int previousOrder = int.MinValue;
            for (int index = 0; index < metadata.Count; index++)
            {
                int currentOrder = metadata[index].DrawOrder;
                Assert.That(
                    currentOrder,
                    Is.GreaterThanOrEqualTo(previousOrder),
                    $"Metadata at index {index} has draw order {currentOrder} which is less than previous {previousOrder}"
                );
                previousOrder = currentOrder;
            }
        }

        [Test]
        public void ArbitraryPositiveDrawOrdersAreRecognized()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonArbitraryDrawOrderTarget)
            );

            WButtonMethodMetadata intMax = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonArbitraryDrawOrderTarget.OrderIntMax)
            );
            WButtonMethodMetadata order1000 = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonArbitraryDrawOrderTarget.Order1000)
            );

            Assert.That(intMax, Is.Not.Null);
            Assert.That(intMax.DrawOrder, Is.EqualTo(int.MaxValue));

            Assert.That(order1000, Is.Not.Null);
            Assert.That(order1000.DrawOrder, Is.EqualTo(1000));
        }

        [Test]
        public void ArbitraryNegativeDrawOrdersAreRecognized()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonArbitraryDrawOrderTarget)
            );

            WButtonMethodMetadata intMin = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonArbitraryDrawOrderTarget.OrderIntMin)
            );
            WButtonMethodMetadata orderMinus1000 = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonArbitraryDrawOrderTarget.OrderMinus1000)
            );

            Assert.That(intMin, Is.Not.Null);
            Assert.That(intMin.DrawOrder, Is.EqualTo(int.MinValue));

            Assert.That(orderMinus1000, Is.Not.Null);
            Assert.That(orderMinus1000.DrawOrder, Is.EqualTo(-1000));
        }

        [Test]
        public void GroupNamesCapturedCorrectly()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonDrawOrderTestTarget)
            );

            WButtonMethodMetadata topAction1 = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonDrawOrderTestTarget.TopAction1)
            );
            WButtonMethodMetadata topDebug1 = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonDrawOrderTestTarget.TopDebug1)
            );
            WButtonMethodMetadata topUtility = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonDrawOrderTestTarget.TopUtility)
            );

            Assert.That(topAction1, Is.Not.Null);
            Assert.That(topAction1.GroupName, Is.EqualTo("Actions"));

            Assert.That(topDebug1, Is.Not.Null);
            Assert.That(topDebug1.GroupName, Is.EqualTo("Debug"));

            Assert.That(topUtility, Is.Not.Null);
            Assert.That(topUtility.GroupName, Is.Null.Or.Empty);
        }

        [Test]
        public void DifferentGroupNamesAtSameDrawOrderAreDistinct()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonSameDrawOrderDifferentGroupsTarget)
            );

            Assert.That(metadata, Has.All.Property("DrawOrder").EqualTo(0));

            HashSet<string> groupNames = new();
            foreach (WButtonMethodMetadata m in metadata)
            {
                groupNames.Add(m.GroupName ?? string.Empty);
            }

            Assert.That(groupNames, Has.Count.EqualTo(4));
            Assert.That(groupNames, Contains.Item("Setup"));
            Assert.That(groupNames, Contains.Item("Configuration"));
            Assert.That(groupNames, Contains.Item("Validation"));
            Assert.That(groupNames, Contains.Item(string.Empty));
        }

        [Test]
        public void GroupKeyComparison()
        {
            WButtonGroupKey key1 = new(0, 0, "Actions", 0, WButtonGroupPlacement.UseGlobalSetting);
            WButtonGroupKey key2 = new(0, 0, "Actions", 0, WButtonGroupPlacement.UseGlobalSetting);
            WButtonGroupKey key3 = new(0, 0, "Debug", 1, WButtonGroupPlacement.UseGlobalSetting);
            WButtonGroupKey key4 = new(0, 1, "Actions", 2, WButtonGroupPlacement.UseGlobalSetting);

            Assert.That(key1.Equals(key2), Is.True);
            Assert.That(key1.GetHashCode(), Is.EqualTo(key2.GetHashCode()));

            Assert.That(key1.Equals(key3), Is.False);

            Assert.That(key1.Equals(key4), Is.False);
        }

        [Test]
        public void GroupKeySorting()
        {
            List<WButtonGroupKey> keys = new()
            {
                new WButtonGroupKey(0, 5, "B", 5, WButtonGroupPlacement.UseGlobalSetting),
                new WButtonGroupKey(0, 0, "A", 0, WButtonGroupPlacement.UseGlobalSetting),
                new WButtonGroupKey(0, 0, "B", 1, WButtonGroupPlacement.UseGlobalSetting),
                new WButtonGroupKey(0, -1, "C", 2, WButtonGroupPlacement.UseGlobalSetting),
                new WButtonGroupKey(0, -10, "D", 3, WButtonGroupPlacement.UseGlobalSetting),
                new WButtonGroupKey(0, 0, "C", 4, WButtonGroupPlacement.UseGlobalSetting),
            };

            keys.Sort();

            Assert.That(keys[0]._drawOrder, Is.EqualTo(-10));
            Assert.That(keys[1]._drawOrder, Is.EqualTo(-1));
            Assert.That(keys[2]._drawOrder, Is.EqualTo(0));
            Assert.That(keys[3]._drawOrder, Is.EqualTo(0));
            Assert.That(keys[4]._drawOrder, Is.EqualTo(0));
            Assert.That(keys[5]._drawOrder, Is.EqualTo(5));

            int firstZeroIndex = keys.FindIndex(k => k._drawOrder == 0);
            Assert.That(keys[firstZeroIndex]._declarationOrder, Is.EqualTo(0));
            Assert.That(keys[firstZeroIndex + 1]._declarationOrder, Is.EqualTo(1));
            Assert.That(keys[firstZeroIndex + 2]._declarationOrder, Is.EqualTo(4));
        }

        [Test]
        public void DrawOrderZeroIsTopPlacement()
        {
            WButtonGroupKey key = new(0, 0, null, 0, WButtonGroupPlacement.UseGlobalSetting);
            GUIContent header = WButtonGUI.BuildGroupHeader(key);

            Assert.That(header, Is.Not.Null);
        }

        [Test]
        public void DrawOrderMinusOneIsTopPlacement()
        {
            WButtonGroupKey key = new(0, -1, null, 0, WButtonGroupPlacement.UseGlobalSetting);

            WButtonGUI.ClearGroupDataForTesting();
            Dictionary<int, int> counts = new() { { -1, 1 } };
            WButtonGUI.SetGroupCountsForTesting(counts);

            GUIContent header = WButtonGUI.BuildGroupHeader(key);

            Assert.That(header, Is.Not.Null);
            Assert.That(header.text, Is.EqualTo(WButtonStyles.TopGroupLabel.text));
        }

        [Test]
        public void ExplicitBottomPlacementUsesBottomLabel()
        {
            WButtonGroupKey key = new(0, -2, null, 0, WButtonGroupPlacement.Bottom);

            WButtonGUI.ClearGroupDataForTesting();
            Dictionary<int, int> counts = new() { { -2, 1 } };
            WButtonGUI.SetGroupCountsForTesting(counts);

            GUIContent header = WButtonGUI.BuildGroupHeader(key);

            Assert.That(header, Is.Not.Null, "Header should not be null");
            Assert.That(
                header.text,
                Is.EqualTo(WButtonStyles.BottomGroupLabel.text),
                $"GroupPlacement.Bottom should use BottomGroupLabel. Got: {header.text}"
            );
        }

        [Test]
        public void UseGlobalSettingWithAnyDrawOrderUsesTopLabel()
        {
            WButtonGroupKey key = new(0, -2, null, 0, WButtonGroupPlacement.UseGlobalSetting);

            WButtonGUI.ClearGroupDataForTesting();
            Dictionary<int, int> counts = new() { { -2, 1 } };
            WButtonGUI.SetGroupCountsForTesting(counts);

            GUIContent header = WButtonGUI.BuildGroupHeader(key);

            Assert.That(header, Is.Not.Null, "Header should not be null");
            Assert.That(
                header.text,
                Is.EqualTo(WButtonStyles.TopGroupLabel.text),
                $"UseGlobalSetting should use TopGroupLabel in BuildGroupHeader (placement is resolved at render time). Got: {header.text}"
            );
        }

        [Test]
        public void MetadataFromDeclarationOrderTargetPreservesMethodOrder()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonDeclarationOrderTarget)
            );

            Assert.That(metadata, Has.All.Property("DrawOrder").EqualTo(0));

            List<WButtonMethodMetadata> orderTestGroup = metadata
                .Where(m => m.GroupName == "Order Test")
                .ToList();

            Assert.That(orderTestGroup, Has.Count.EqualTo(4));

            // Fifth follows Third in declaration order, even though Fourth sits between them.
            Assert.That(
                orderTestGroup[0].DisplayName,
                Is.EqualTo("First"),
                "First method in Order Test group should be 'First'"
            );
            Assert.That(
                orderTestGroup[1].DisplayName,
                Is.EqualTo("Second"),
                "Second method in Order Test group should be 'Second'"
            );
            Assert.That(
                orderTestGroup[2].DisplayName,
                Is.EqualTo("Third"),
                "Third method in Order Test group should be 'Third'"
            );
            Assert.That(
                orderTestGroup[3].DisplayName,
                Is.EqualTo("Fifth"),
                "Fourth method in Order Test group should be 'Fifth'"
            );
        }

        [Test]
        public void ButtonsWithSameDrawOrderDifferentGroupsHaveDifferentMetadata()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonSameDrawOrderDifferentGroupsTarget)
            );

            Dictionary<string, List<WButtonMethodMetadata>> byGroup = new();
            foreach (WButtonMethodMetadata m in metadata)
            {
                string key = m.GroupName ?? string.Empty;
                byGroup.GetOrAdd(key).Add(m);
            }

            Assert.That(byGroup.ValueFor("Setup"), Has.Count.EqualTo(2));

            Assert.That(byGroup.ValueFor("Configuration"), Has.Count.EqualTo(2));

            Assert.That(byGroup.ValueFor("Validation"), Has.Count.EqualTo(1));

            Assert.That(byGroup.ValueFor(string.Empty), Has.Count.EqualTo(2));
        }

        [Test]
        public void MetadataHasDeclarationOrderProperty()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonGroupDeclarationOrderTarget)
            );

            Assert.That(metadata, Has.All.Property("DeclarationOrder").GreaterThanOrEqualTo(0));

            HashSet<int> orders = new();
            foreach (WButtonMethodMetadata m in metadata)
            {
                Assert.That(
                    orders.Add(m.DeclarationOrder),
                    Is.True,
                    $"Duplicate declaration order {m.DeclarationOrder} found"
                );
            }
        }

        [Test]
        public void SetupGroupDeclaredBeforeDebugGroupHasLowerDeclarationOrder()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonGroupDeclarationOrderTarget)
            );

            WButtonMethodMetadata setupMethod = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonGroupDeclarationOrderTarget.Initialize)
            );
            WButtonMethodMetadata debugMethod = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonGroupDeclarationOrderTarget.RollDice)
            );

            Assert.That(setupMethod, Is.Not.Null, "Setup method should exist");
            Assert.That(debugMethod, Is.Not.Null, "Debug method should exist");

            Assert.That(
                setupMethod.DeclarationOrder,
                Is.LessThan(debugMethod.DeclarationOrder),
                "Setup group (declared first) should have lower declaration order than Debug group (declared second)"
            );
        }

        [Test]
        public void ReverseAlphabeticalGroupsPreserveDeclarationOrder()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonReverseAlphabeticalGroupsTarget)
            );

            WButtonMethodMetadata zebra = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonReverseAlphabeticalGroupsTarget.ZebraAction)
            );
            WButtonMethodMetadata yak = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonReverseAlphabeticalGroupsTarget.YakAction)
            );
            WButtonMethodMetadata xenon = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonReverseAlphabeticalGroupsTarget.XenonAction)
            );

            Assert.That(zebra, Is.Not.Null);
            Assert.That(yak, Is.Not.Null);
            Assert.That(xenon, Is.Not.Null);

            Assert.That(
                zebra.DeclarationOrder,
                Is.LessThan(yak.DeclarationOrder),
                "Zebra should have lower declaration order than Yak"
            );
            Assert.That(
                yak.DeclarationOrder,
                Is.LessThan(xenon.DeclarationOrder),
                "Yak should have lower declaration order than Xenon"
            );
        }

        [Test]
        public void InterleavedGroupsUseFirstDeclarationOrder()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonInterleavedGroupsTarget)
            );

            WButtonMethodMetadata alpha1 = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonInterleavedGroupsTarget.Alpha1)
            );
            WButtonMethodMetadata beta1 = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonInterleavedGroupsTarget.Beta1)
            );
            WButtonMethodMetadata gamma1 = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonInterleavedGroupsTarget.Gamma1)
            );

            Assert.That(alpha1, Is.Not.Null);
            Assert.That(beta1, Is.Not.Null);
            Assert.That(gamma1, Is.Not.Null);

            Assert.That(
                alpha1.DeclarationOrder,
                Is.LessThan(beta1.DeclarationOrder),
                "Alpha (first declared) should have lower declaration order than Beta"
            );
            Assert.That(
                beta1.DeclarationOrder,
                Is.LessThan(gamma1.DeclarationOrder),
                "Beta (second declared) should have lower declaration order than Gamma"
            );
        }

        [Test]
        public void AllMethodsInSameGroupHaveSameGroupName()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonInterleavedGroupsTarget)
            );

            List<WButtonMethodMetadata> alphaMethods = metadata
                .Where(m => m.GroupName == "Alpha")
                .ToList();

            Assert.That(alphaMethods, Has.Count.EqualTo(3));
            Assert.That(alphaMethods, Has.All.Property("GroupName").EqualTo("Alpha"));
        }

        [Test]
        public void MixedDrawOrdersAndGroupsAreProperlyOrdered()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonMixedDrawOrderAndGroupsTarget)
            );

            Dictionary<int, List<WButtonMethodMetadata>> byDrawOrder = new();
            foreach (WButtonMethodMetadata m in metadata)
            {
                byDrawOrder.GetOrAdd(m.DrawOrder).Add(m);
            }

            Assert.That(byDrawOrder.Keys, Is.EquivalentTo(new[] { 0, -1, -2 }));

            Assert.That(byDrawOrder.ValueFor(0), Has.Count.EqualTo(2));
            Assert.That(byDrawOrder.ValueFor(-1), Has.Count.EqualTo(2));
            Assert.That(byDrawOrder.ValueFor(-2), Has.Count.EqualTo(2));

            WButtonMethodMetadata zeroFirst = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonMixedDrawOrderAndGroupsTarget.ZeroFirst)
            );
            WButtonMethodMetadata zeroSecond = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonMixedDrawOrderAndGroupsTarget.ZeroSecond)
            );

            Assert.That(zeroFirst, Is.Not.Null);
            Assert.That(zeroSecond, Is.Not.Null);
            Assert.That(
                zeroFirst.DeclarationOrder,
                Is.LessThan(zeroSecond.DeclarationOrder),
                "First Group should be declared before Second Group"
            );
        }

        [Test]
        public void GroupKeyUsesMetadataDeclarationOrderForSorting()
        {
            WButtonGroupKey setupKey = new(
                0,
                -1,
                "Setup",
                0,
                WButtonGroupPlacement.UseGlobalSetting
            );
            WButtonGroupKey debugKey = new(
                0,
                -1,
                "Debug",
                2,
                WButtonGroupPlacement.UseGlobalSetting
            );

            int comparison = setupKey.CompareTo(debugKey);
            Assert.That(
                comparison,
                Is.LessThan(0),
                "Setup group (declaration order 0) should sort before Debug group (declaration order 2)"
            );
        }

        [Test]
        public void SortedDictionaryOfGroupKeysPreservesDeclarationOrder()
        {
            SortedDictionary<WButtonGroupKey, string> groups = new();

            WButtonGroupKey zebraKey = new(
                0,
                0,
                "Zebra",
                0,
                WButtonGroupPlacement.UseGlobalSetting
            );
            WButtonGroupKey yakKey = new(0, 0, "Yak", 1, WButtonGroupPlacement.UseGlobalSetting);
            WButtonGroupKey xenonKey = new(
                0,
                0,
                "Xenon",
                2,
                WButtonGroupPlacement.UseGlobalSetting
            );

            groups[zebraKey] = "Zebra content";
            groups[yakKey] = "Yak content";
            groups[xenonKey] = "Xenon content";

            List<string> groupOrder = groups.Keys.Select(k => k._groupName).ToList();

            Assert.That(
                groupOrder[0],
                Is.EqualTo("Zebra"),
                "First group should be Zebra (declaration order 0)"
            );
            Assert.That(
                groupOrder[1],
                Is.EqualTo("Yak"),
                "Second group should be Yak (declaration order 1)"
            );
            Assert.That(
                groupOrder[2],
                Is.EqualTo("Xenon"),
                "Third group should be Xenon (declaration order 2)"
            );
        }
    }
}
#endif
