// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WButton
{
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;

    /// <summary>
    /// Comprehensive tests for WButton declaration order preservation.
    /// These tests verify that buttons are sorted by declaration order (source code order)
    /// within the same draw order, NOT by alphabetical order of display names or method names.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WButtonDeclarationOrderTests : CommonTestBase
    {
        [Test]
        public void AlphabeticalTrapPreservesDeclarationOrderNotAlphabetical()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonAlphabeticalTrapTarget)
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(4),
                $"Expected 4 buttons but found {metadata.Count}. Buttons: [{string.Join(", ", metadata.Select(m => m.DisplayName))}]"
            );

            Assert.That(
                metadata[0].DisplayName,
                Is.EqualTo("Delta"),
                "First button should be Delta (first declared), not Alpha (alphabetically first)"
            );
            Assert.That(
                metadata[1].DisplayName,
                Is.EqualTo("Charlie"),
                "Second button should be Charlie (second declared)"
            );
            Assert.That(
                metadata[2].DisplayName,
                Is.EqualTo("Beta"),
                "Third button should be Beta (third declared)"
            );
            Assert.That(
                metadata[3].DisplayName,
                Is.EqualTo("Alpha"),
                "Fourth button should be Alpha (last declared), not first"
            );
        }

        [Test]
        public void AlphabeticalTrapDeclarationOrderValuesAreAscending()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonAlphabeticalTrapTarget)
            );

            for (int i = 1; i < metadata.Count; i++)
            {
                Assert.That(
                    metadata[i].DeclarationOrder,
                    Is.GreaterThan(metadata[i - 1].DeclarationOrder),
                    $"Declaration order at index {i} should be greater than index {i - 1}"
                );
            }
        }

        [Test]
        public void MixedCaseNamesPreservesDeclarationOrder()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonMixedCaseNamesTarget)
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(4),
                $"Expected 4 buttons but found {metadata.Count}. Buttons: [{string.Join(", ", metadata.Select(m => m.DisplayName))}]"
            );

            Assert.That(metadata[0].DisplayName, Is.EqualTo("apple"));
            Assert.That(metadata[1].DisplayName, Is.EqualTo("BANANA"));
            Assert.That(metadata[2].DisplayName, Is.EqualTo("cherry"));
            Assert.That(metadata[3].DisplayName, Is.EqualTo("Date"));
        }

        [Test]
        public void NumericPrefixNamesPreservesDeclarationOrderNotNumericOrAlphabeticalSort()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonNumericPrefixNamesTarget)
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(4),
                $"Expected 4 buttons but found {metadata.Count}. Buttons: [{string.Join(", ", metadata.Select(m => m.DisplayName))}]"
            );

            // Declared 2, 10, 1, 100 -- neither alphabetical (1, 10, 100, 2) nor numeric (1, 2, 10, 100).
            Assert.That(
                metadata[0].DisplayName,
                Does.StartWith("2"),
                "First should be '2' (first declared)"
            );
            Assert.That(
                metadata[1].DisplayName,
                Does.StartWith("10 "),
                "Second should be '10' (second declared)"
            );
            Assert.That(
                metadata[2].DisplayName,
                Does.StartWith("1 "),
                "Third should be '1' (third declared)"
            );
            Assert.That(
                metadata[3].DisplayName,
                Does.StartWith("100"),
                "Fourth should be '100' (fourth declared)"
            );
        }

        [Test]
        public void SpecialCharactersPreservesDeclarationOrderNotAsciiSort()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonSpecialCharactersTarget)
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(4),
                $"Expected 4 buttons but found {metadata.Count}. Buttons: [{string.Join(", ", metadata.Select(m => m.DisplayName))}]"
            );

            // Declared _underscore, @at, #hash, !exclaim -- the reverse of ASCII order.
            Assert.That(metadata[0].DisplayName, Does.StartWith("_underscore"));
            Assert.That(metadata[1].DisplayName, Does.StartWith("@at"));
            Assert.That(metadata[2].DisplayName, Does.StartWith("#hash"));
            Assert.That(metadata[3].DisplayName, Does.StartWith("!exclaim"));
        }

        [Test]
        public void SameDisplayNamePreservesDeclarationOrderNotMethodNameSort()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonSameDisplayNameDifferentMethodsTarget)
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(3),
                $"Expected 3 buttons but found {metadata.Count}. Methods: [{string.Join(", ", metadata.Select(m => m.Method.Name))}]"
            );

            // All three display "Action"; the methods are declared ZZZ, AAA, MMM.
            Assert.That(
                metadata[0].Method.Name,
                Is.EqualTo("ZZZFirstDeclaration"),
                "First should be ZZZ method (first declared)"
            );
            Assert.That(
                metadata[1].Method.Name,
                Is.EqualTo("AAASecondDeclaration"),
                "Second should be AAA method (second declared)"
            );
            Assert.That(
                metadata[2].Method.Name,
                Is.EqualTo("MMMThirdDeclaration"),
                "Third should be MMM method (third declared)"
            );
        }

        [Test]
        public void EmptyDisplayNameFallsBackToMethodNameButPreservesDeclarationOrder()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonEmptyDisplayNameTarget)
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(3),
                $"Expected 3 buttons but found {metadata.Count}. Buttons: [{string.Join(", ", metadata.Select(m => m.DisplayName))}]"
            );

            Assert.That(
                metadata[0].DisplayName,
                Is.EqualTo("ZZZFirstMethod"),
                "First should be ZZZ (first declared)"
            );
            Assert.That(
                metadata[1].DisplayName,
                Is.EqualTo("AAASecondMethod"),
                "Second should be AAA (second declared)"
            );
            Assert.That(
                metadata[2].DisplayName,
                Is.EqualTo("MMMThirdMethod"),
                "Third should be MMM (third declared)"
            );
        }

        [Test]
        public void MultipleDrawOrdersEachPreservesDeclarationOrderWithinDrawOrder()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonMultipleDrawOrdersWithDeclarationOrderTarget)
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(9),
                $"Expected 9 buttons but found {metadata.Count}. Buttons: [{string.Join(", ", metadata.Select(m => m.DisplayName))}]"
            );

            List<WButtonMethodMetadata> order0 = metadata.Where(m => m.DrawOrder == 0).ToList();
            List<WButtonMethodMetadata> order1 = metadata.Where(m => m.DrawOrder == 1).ToList();
            List<WButtonMethodMetadata> orderMinus1 = metadata
                .Where(m => m.DrawOrder == -1)
                .ToList();

            Assert.That(order0, Has.Count.EqualTo(3));
            Assert.That(
                order0[0].Method.Name,
                Is.EqualTo("Order0Z"),
                "First in draw order 0 should be Z"
            );
            Assert.That(
                order0[1].Method.Name,
                Is.EqualTo("Order0Y"),
                "Second in draw order 0 should be Y"
            );
            Assert.That(
                order0[2].Method.Name,
                Is.EqualTo("Order0X"),
                "Third in draw order 0 should be X"
            );

            Assert.That(order1, Has.Count.EqualTo(3));
            Assert.That(
                order1[0].Method.Name,
                Is.EqualTo("Order1C"),
                "First in draw order 1 should be C"
            );
            Assert.That(
                order1[1].Method.Name,
                Is.EqualTo("Order1B"),
                "Second in draw order 1 should be B"
            );
            Assert.That(
                order1[2].Method.Name,
                Is.EqualTo("Order1A"),
                "Third in draw order 1 should be A"
            );

            Assert.That(orderMinus1, Has.Count.EqualTo(3));
            Assert.That(
                orderMinus1[0].Method.Name,
                Is.EqualTo("OrderMinus1Q"),
                "First in draw order -1 should be Q"
            );
            Assert.That(
                orderMinus1[1].Method.Name,
                Is.EqualTo("OrderMinus1P"),
                "Second in draw order -1 should be P"
            );
            Assert.That(
                orderMinus1[2].Method.Name,
                Is.EqualTo("OrderMinus1O"),
                "Third in draw order -1 should be O"
            );
        }

        [Test]
        public void MultipleDrawOrdersDrawOrderSortingIsCorrect()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonMultipleDrawOrdersWithDeclarationOrderTarget)
            );

            int lastDrawOrder = int.MinValue;
            foreach (WButtonMethodMetadata m in metadata)
            {
                Assert.That(
                    m.DrawOrder,
                    Is.GreaterThanOrEqualTo(lastDrawOrder),
                    $"Draw order should be non-decreasing. Found {m.DrawOrder} after {lastDrawOrder}"
                );
                lastDrawOrder = m.DrawOrder;
            }
        }

        [Test]
        public void GroupedButtonsPreservesDeclarationOrderWithinGroup()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonGroupedReverseAlphabeticalMethodsTarget)
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(6),
                $"Expected 6 buttons but found {metadata.Count}. Buttons: [{string.Join(", ", metadata.Select(m => $"{m.GroupName}:{m.Method.Name}"))}]"
            );

            List<WButtonMethodMetadata> groupA = metadata
                .Where(m => m.GroupName == "Group A")
                .ToList();
            Assert.That(groupA, Has.Count.EqualTo(3));
            Assert.That(
                groupA[0].Method.Name,
                Is.EqualTo("GroupAZMethod"),
                "First in Group A should be Z"
            );
            Assert.That(
                groupA[1].Method.Name,
                Is.EqualTo("GroupAMMethod"),
                "Second in Group A should be M"
            );
            Assert.That(
                groupA[2].Method.Name,
                Is.EqualTo("GroupAAMethod"),
                "Third in Group A should be A"
            );

            List<WButtonMethodMetadata> groupB = metadata
                .Where(m => m.GroupName == "Group B")
                .ToList();
            Assert.That(groupB, Has.Count.EqualTo(3));
            Assert.That(
                groupB[0].Method.Name,
                Is.EqualTo("GroupBThird"),
                "First in Group B should be Third"
            );
            Assert.That(
                groupB[1].Method.Name,
                Is.EqualTo("GroupBSecond"),
                "Second in Group B should be Second"
            );
            Assert.That(
                groupB[2].Method.Name,
                Is.EqualTo("GroupBFirst"),
                "Third in Group B should be First"
            );
        }

        [Test]
        public void SingleButtonHasValidDeclarationOrder()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonSingleButtonTarget)
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(1),
                $"Expected 1 button but found {metadata.Count}. Buttons: [{string.Join(", ", metadata.Select(m => m.DisplayName))}]"
            );
            Assert.That(metadata[0].DeclarationOrder, Is.EqualTo(0));
            Assert.That(metadata[0].DisplayName, Is.EqualTo("Only Button"));
        }

        [Test]
        public void LargeDeclarationOrderPreservesExactDeclarationOrder()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonLargeDeclarationOrderTarget)
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(10),
                $"Expected 10 buttons but found {metadata.Count}. Buttons: [{string.Join(", ", metadata.Select(m => m.DisplayName))}]"
            );

            string[] expectedOrder = new[]
            {
                "Ninth",
                "First",
                "Fifth",
                "Third",
                "Seventh",
                "Second",
                "Tenth",
                "Fourth",
                "Eighth",
                "Sixth",
            };

            for (int i = 0; i < expectedOrder.Length; i++)
            {
                Assert.That(
                    metadata[i].DisplayName,
                    Is.EqualTo(expectedOrder[i]),
                    $"Button at index {i} should be '{expectedOrder[i]}'"
                );
            }
        }

        [Test]
        public void LargeDeclarationOrderDeclarationOrderValuesAreSequential()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonLargeDeclarationOrderTarget)
            );

            for (int i = 0; i < metadata.Count; i++)
            {
                Assert.That(
                    metadata[i].DeclarationOrder,
                    Is.EqualTo(i),
                    $"Declaration order at index {i} should be {i}"
                );
            }
        }

        [Test]
        public void DeclarationOrderTargetPreservesOrderWithinGroup()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonDeclarationOrderTarget)
            );

            List<WButtonMethodMetadata> orderTestGroup = metadata
                .Where(m => m.GroupName == "Order Test")
                .ToList();

            Assert.That(orderTestGroup, Has.Count.EqualTo(4));
            Assert.That(orderTestGroup[0].DisplayName, Is.EqualTo("First"));
            Assert.That(orderTestGroup[1].DisplayName, Is.EqualTo("Second"));
            Assert.That(orderTestGroup[2].DisplayName, Is.EqualTo("Third"));
            Assert.That(orderTestGroup[3].DisplayName, Is.EqualTo("Fifth"));
        }

        [Test]
        public void DeclarationOrderTargetPreservesOrderAcrossGroups()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonDeclarationOrderTarget)
            );

            WButtonMethodMetadata firstOrderTest = metadata.FirstOrDefault(m =>
                m.GroupName == "Order Test"
            );
            WButtonMethodMetadata firstOtherGroup = metadata.FirstOrDefault(m =>
                m.GroupName == "Other Group"
            );

            Assert.That(firstOrderTest, Is.Not.Null);
            Assert.That(firstOtherGroup, Is.Not.Null);

            Assert.That(
                firstOrderTest.DeclarationOrder,
                Is.LessThan(firstOtherGroup.DeclarationOrder),
                "Order Test group should start before Other Group"
            );
        }

        [Test]
        public void InterleavedGroupsButtonsWithinGroupPreserveDeclarationOrder()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonInterleavedGroupsTarget)
            );

            List<WButtonMethodMetadata> alphaGroup = metadata
                .Where(m => m.GroupName == "Alpha")
                .ToList();

            Assert.That(alphaGroup, Has.Count.EqualTo(3));

            // Alpha1, Alpha2 and Alpha3 are interleaved with other groups in the source.
            Assert.That(alphaGroup[0].Method.Name, Is.EqualTo("Alpha1"));
            Assert.That(alphaGroup[1].Method.Name, Is.EqualTo("Alpha2"));
            Assert.That(alphaGroup[2].Method.Name, Is.EqualTo("Alpha3"));
        }

        [Test]
        public void InterleavedGroupsGroupDeclarationOrderReflectsFirstButtonInGroup()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonInterleavedGroupsTarget)
            );

            WButtonMethodMetadata alpha1 = metadata.First(m => m.GroupName == "Alpha");
            WButtonMethodMetadata beta1 = metadata.First(m => m.GroupName == "Beta");
            WButtonMethodMetadata gamma1 = metadata.First(m => m.GroupName == "Gamma");

            Assert.That(
                alpha1.DeclarationOrder,
                Is.LessThan(beta1.DeclarationOrder),
                "Alpha first appears before Beta"
            );
            Assert.That(
                beta1.DeclarationOrder,
                Is.LessThan(gamma1.DeclarationOrder),
                "Beta first appears before Gamma"
            );
        }

        [Test]
        public void ReverseAlphabeticalGroupsPreservesDeclarationOrderNotAlphabetical()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonReverseAlphabeticalGroupsTarget)
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(3),
                $"Expected 3 buttons but found {metadata.Count}. Groups: [{string.Join(", ", metadata.Select(m => m.GroupName))}]"
            );

            Assert.That(metadata[0].GroupName, Is.EqualTo("Zebra"), "First group should be Zebra");
            Assert.That(metadata[1].GroupName, Is.EqualTo("Yak"), "Second group should be Yak");
            Assert.That(metadata[2].GroupName, Is.EqualTo("Xenon"), "Third group should be Xenon");
        }

        [Test]
        public void DeclarationOrderIsConsistentAcrossMultipleCalls()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata1 = WButtonMetadataCache.GetMetadata(
                typeof(WButtonAlphabeticalTrapTarget)
            );
            IReadOnlyList<WButtonMethodMetadata> metadata2 = WButtonMetadataCache.GetMetadata(
                typeof(WButtonAlphabeticalTrapTarget)
            );
            IReadOnlyList<WButtonMethodMetadata> metadata3 = WButtonMetadataCache.GetMetadata(
                typeof(WButtonAlphabeticalTrapTarget)
            );

            Assert.That(
                metadata1.Count,
                Is.EqualTo(metadata2.Count),
                $"metadata1.Count ({metadata1.Count}) != metadata2.Count ({metadata2.Count})"
            );
            Assert.That(
                metadata2.Count,
                Is.EqualTo(metadata3.Count),
                $"metadata2.Count ({metadata2.Count}) != metadata3.Count ({metadata3.Count})"
            );

            for (int i = 0; i < metadata1.Count; i++)
            {
                Assert.That(
                    metadata1[i].Method.Name,
                    Is.EqualTo(metadata2[i].Method.Name),
                    $"Method at index {i} should be consistent between calls"
                );
                Assert.That(
                    metadata2[i].Method.Name,
                    Is.EqualTo(metadata3[i].Method.Name),
                    $"Method at index {i} should be consistent between calls"
                );
                Assert.That(
                    metadata1[i].DeclarationOrder,
                    Is.EqualTo(metadata2[i].DeclarationOrder),
                    $"Declaration order at index {i} should be consistent"
                );
            }
        }

        [Test]
        public void AllMetadataHaveNonNegativeDeclarationOrder()
        {
            System.Type[] targetTypes = new[]
            {
                typeof(WButtonAlphabeticalTrapTarget),
                typeof(WButtonMixedCaseNamesTarget),
                typeof(WButtonNumericPrefixNamesTarget),
                typeof(WButtonSpecialCharactersTarget),
                typeof(WButtonSameDisplayNameDifferentMethodsTarget),
                typeof(WButtonEmptyDisplayNameTarget),
                typeof(WButtonSingleButtonTarget),
                typeof(WButtonLargeDeclarationOrderTarget),
            };

            foreach (System.Type targetType in targetTypes)
            {
                IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                    targetType
                );
                foreach (WButtonMethodMetadata m in metadata)
                {
                    Assert.That(
                        m.DeclarationOrder,
                        Is.GreaterThanOrEqualTo(0),
                        $"Declaration order for {m.Method.Name} in {targetType.Name} should be non-negative"
                    );
                }
            }
        }

        [Test]
        public void AllMetadataHaveUniqueDeclarationOrderWithinType()
        {
            System.Type[] targetTypes = new[]
            {
                typeof(WButtonAlphabeticalTrapTarget),
                typeof(WButtonMixedCaseNamesTarget),
                typeof(WButtonNumericPrefixNamesTarget),
                typeof(WButtonLargeDeclarationOrderTarget),
            };

            foreach (System.Type targetType in targetTypes)
            {
                IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                    targetType
                );
                HashSet<int> seenOrders = new HashSet<int>();
                foreach (WButtonMethodMetadata m in metadata)
                {
                    Assert.That(
                        seenOrders.Add(m.DeclarationOrder),
                        Is.True,
                        $"Declaration order {m.DeclarationOrder} is duplicated in {targetType.Name}"
                    );
                }
            }
        }

        [TestCase(typeof(WButtonAlphabeticalTrapTarget), 4)]
        [TestCase(typeof(WButtonMixedCaseNamesTarget), 4)]
        [TestCase(typeof(WButtonNumericPrefixNamesTarget), 4)]
        [TestCase(typeof(WButtonSpecialCharactersTarget), 4)]
        [TestCase(typeof(WButtonSameDisplayNameDifferentMethodsTarget), 3)]
        [TestCase(typeof(WButtonEmptyDisplayNameTarget), 3)]
        [TestCase(typeof(WButtonSingleButtonTarget), 1)]
        [TestCase(typeof(WButtonLargeDeclarationOrderTarget), 10)]
        [TestCase(typeof(WButtonReverseAlphabeticalGroupsTarget), 3)]
        [TestCase(typeof(WButtonGroupedReverseAlphabeticalMethodsTarget), 6)]
        [TestCase(typeof(WButtonMultipleDrawOrdersWithDeclarationOrderTarget), 9)]
        public void MetadataCountMatchesExpected(System.Type targetType, int expectedCount)
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                targetType
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(expectedCount),
                $"Expected {expectedCount} buttons for {targetType.Name} but found {metadata.Count}.\n{FormatMetadataDiagnostics(metadata)}"
            );
        }

        [TestCase(typeof(WButtonAlphabeticalTrapTarget))]
        [TestCase(typeof(WButtonMixedCaseNamesTarget))]
        [TestCase(typeof(WButtonNumericPrefixNamesTarget))]
        [TestCase(typeof(WButtonSpecialCharactersTarget))]
        [TestCase(typeof(WButtonSameDisplayNameDifferentMethodsTarget))]
        [TestCase(typeof(WButtonEmptyDisplayNameTarget))]
        [TestCase(typeof(WButtonSingleButtonTarget))]
        [TestCase(typeof(WButtonLargeDeclarationOrderTarget))]
        [TestCase(typeof(WButtonReverseAlphabeticalGroupsTarget))]
        [TestCase(typeof(WButtonGroupedReverseAlphabeticalMethodsTarget))]
        [TestCase(typeof(WButtonMultipleDrawOrdersWithDeclarationOrderTarget))]
        public void MetadataDeclarationOrdersAreUniqueAndSequential(System.Type targetType)
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                targetType
            );

            HashSet<int> seenOrders = new HashSet<int>();
            for (int i = 0; i < metadata.Count; i++)
            {
                int order = metadata[i].DeclarationOrder;

                Assert.That(
                    order,
                    Is.GreaterThanOrEqualTo(0),
                    $"Declaration order at index {i} should be non-negative.\n{FormatMetadataDiagnostics(metadata)}"
                );

                Assert.That(
                    seenOrders.Add(order),
                    Is.True,
                    $"Declaration order {order} at index {i} is a duplicate.\n{FormatMetadataDiagnostics(metadata)}"
                );
            }
        }

        [TestCase(typeof(WButtonAlphabeticalTrapTarget))]
        [TestCase(typeof(WButtonMixedCaseNamesTarget))]
        [TestCase(typeof(WButtonNumericPrefixNamesTarget))]
        [TestCase(typeof(WButtonSpecialCharactersTarget))]
        [TestCase(typeof(WButtonSameDisplayNameDifferentMethodsTarget))]
        [TestCase(typeof(WButtonEmptyDisplayNameTarget))]
        [TestCase(typeof(WButtonSingleButtonTarget))]
        [TestCase(typeof(WButtonLargeDeclarationOrderTarget))]
        [TestCase(typeof(WButtonReverseAlphabeticalGroupsTarget))]
        [TestCase(typeof(WButtonGroupedReverseAlphabeticalMethodsTarget))]
        [TestCase(typeof(WButtonMultipleDrawOrdersWithDeclarationOrderTarget))]
        public void MetadataIsNotEmptyForValidTarget(System.Type targetType)
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                targetType
            );

            Assert.That(
                metadata.Count,
                Is.GreaterThan(0),
                $"Expected non-empty metadata for {targetType.Name}"
            );
        }

        [Test]
        public void GetMetadataThrowsForNullType()
        {
            Assert.That(
                () => WButtonMetadataCache.GetMetadata(null),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("inspectedType")
            );
        }

        [Test]
        public void GetMetadataReturnsEmptyForTypeWithNoWButtonMethods()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(object)
            );

            Assert.That(
                metadata.Count,
                Is.EqualTo(0),
                "Expected empty metadata for type with no WButton methods"
            );
        }

        [Test]
        public void GetMetadataCachesResultsCorrectly()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata1 = WButtonMetadataCache.GetMetadata(
                typeof(WButtonAlphabeticalTrapTarget)
            );

            IReadOnlyList<WButtonMethodMetadata> metadata2 = WButtonMetadataCache.GetMetadata(
                typeof(WButtonAlphabeticalTrapTarget)
            );

            Assert.That(
                ReferenceEquals(metadata1, metadata2),
                Is.True,
                "Subsequent calls should return cached instance"
            );

            Assert.That(metadata1.Count, Is.EqualTo(metadata2.Count));
            for (int i = 0; i < metadata1.Count; i++)
            {
                Assert.That(
                    ReferenceEquals(metadata1[i], metadata2[i]),
                    Is.True,
                    $"Metadata at index {i} should be same reference"
                );
            }
        }

        [Test]
        public void MetadataDrawOrderIsRespectedAcrossAllTestTargets()
        {
            System.Type[] targetTypes = new[]
            {
                typeof(WButtonAlphabeticalTrapTarget),
                typeof(WButtonMixedCaseNamesTarget),
                typeof(WButtonMultipleDrawOrdersWithDeclarationOrderTarget),
            };

            foreach (System.Type targetType in targetTypes)
            {
                IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                    targetType
                );

                int previousDrawOrder = int.MinValue;
                for (int i = 0; i < metadata.Count; i++)
                {
                    Assert.That(
                        metadata[i].DrawOrder,
                        Is.GreaterThanOrEqualTo(previousDrawOrder),
                        $"DrawOrder at index {i} in {targetType.Name} should be >= previous.\n{FormatMetadataDiagnostics(metadata)}"
                    );
                    previousDrawOrder = metadata[i].DrawOrder;
                }
            }
        }

        private static string FormatMetadataDiagnostics(
            IReadOnlyList<WButtonMethodMetadata> metadata
        )
        {
            if (metadata == null || metadata.Count == 0)
            {
                return "Metadata: <empty>";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.AppendLine($"Metadata ({metadata.Count} items):");
            for (int i = 0; i < metadata.Count; i++)
            {
                WButtonMethodMetadata m = metadata[i];
                builder.AppendLine(
                    $"  [{i}] Method={m.Method.Name}, Display=\"{m.DisplayName}\", DrawOrder={m.DrawOrder}, DeclOrder={m.DeclarationOrder}, Group=\"{m.GroupName ?? "<null>"}\""
                );
            }
            return builder.ToString();
        }
    }
}
#endif
