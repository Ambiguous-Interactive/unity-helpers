// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers.Utils
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;

    [TestFixture]
    public sealed class DropDownSharedTests
    {
        [Test]
        [TestCase(0, "(Option 0)", TestName = "Index.Zero.ReturnsOption0")]
        [TestCase(1, "(Option 1)", TestName = "Index.One.ReturnsOption1")]
        [TestCase(99, "(Option 99)", TestName = "Index.NinetyNine.ReturnsOption99")]
        public void GetFallbackOptionLabelReturnsExpectedFormat(int index, string expected)
        {
            string result = DropDownShared.GetFallbackOptionLabel(index);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0, TestName = "Index.Zero")]
        [TestCase(int.MaxValue, TestName = "Index.MaxValue")]
        [TestCase(-1, TestName = "Index.NegativeOne")]
        [TestCase(-100, TestName = "Index.NegativeHundred")]
        public void GetFallbackOptionLabelReturnsNonEmptyForAllIndices(int index)
        {
            string result = DropDownShared.GetFallbackOptionLabel(index);
            Assert.That(result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        [TestCase(0, TestName = "CachedIndex.Zero")]
        [TestCase(5, TestName = "CachedIndex.Five")]
        [TestCase(42, TestName = "CachedIndex.FortyTwo")]
        public void GetFallbackOptionLabelReturnsCachedInstance(int index)
        {
            string first = DropDownShared.GetFallbackOptionLabel(index);
            string second = DropDownShared.GetFallbackOptionLabel(index);
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void GetFallbackOptionLabelStartsWithOpenParen()
        {
            for (int i = 0; i < 10; ++i)
            {
                string label = DropDownShared.GetFallbackOptionLabel(i);
                Assert.That(
                    label.StartsWith("(Option", System.StringComparison.OrdinalIgnoreCase),
                    Is.True,
                    $"Fallback label for index {i} should start with '(Option' to be searchable, but was '{label}'."
                );
            }
        }
    }
#endif
}
