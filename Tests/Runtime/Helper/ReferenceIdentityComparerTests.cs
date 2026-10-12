// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [Category("Fast")]
    public sealed class ReferenceIdentityComparerTests
    {
        [Test]
        public void EqualValuesKeepDistinctIdentities()
        {
            string first = new(new[] { 'a', 'b' });
            string second = new(new[] { 'a', 'b' });
            ReferenceIdentityComparer<string> comparer = ReferenceIdentityComparer<string>.Instance;
            Assert.That(first, Is.EqualTo(second));
            Assert.That(comparer.Equals(first, second), Is.False);
            Assert.That(comparer.Equals(first, first), Is.True);
            HashSet<string> values = new(comparer) { first, second };
            Assert.That(values.Count, Is.EqualTo(2));
        }

        [Test]
        public void NullComparisonAndHashHaveExplicitContracts()
        {
            ReferenceIdentityComparer<object> comparer = ReferenceIdentityComparer<object>.Instance;
            object value = new();
            Assert.That(comparer.Equals(null, null), Is.True);
            Assert.That(comparer.Equals(null, value), Is.False);
            Assert.That(comparer.Equals(value, null), Is.False);
            Assert.That(comparer.GetHashCode(null), Is.Zero);
            Assert.That(comparer.GetHashCode(value), Is.EqualTo(comparer.GetHashCode(value)));
            Assert.That(comparer, Is.SameAs(ReferenceIdentityComparer<object>.Instance));
        }
    }
}
