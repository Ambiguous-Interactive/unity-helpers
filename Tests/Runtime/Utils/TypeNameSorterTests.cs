// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TypeNameSorterTests
    {
        [Test]
        public void CompareOrdersByTypeNameIgnoringCase()
        {
            List<Type> types = new() { typeof(Beta), typeof(Alpha) };

            types.Sort(TypeNameSorter.Instance);

            Assert.That(types, Is.EqualTo(new[] { typeof(Alpha), typeof(Beta) }));
        }

        [Test]
        public void CompareHandlesNullTypes()
        {
            int compareNullAgainstType = TypeNameSorter.Instance.Compare(null, typeof(Alpha));
            int compareTypeAgainstNull = TypeNameSorter.Instance.Compare(typeof(Alpha), null);

            Assert.Less(compareNullAgainstType, 0);
            Assert.Greater(compareTypeAgainstNull, 0);
        }

        private sealed class Alpha { }

        private sealed class Beta { }
    }
}
