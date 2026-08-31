// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class StringWrapperTests
    {
        private const int NumTries = 100;

        [TearDown]
        public void Cleanup()
        {
            // Clean up cache between tests
            StringWrapper.Clear();
        }

        [Test]
        public void GetReturnsNonNullWrapper()
        {
            StringWrapper wrapper = StringWrapper.Get("test");
            Assert.IsTrue(wrapper != null, "StringWrapper should not be null for valid string");
            Assert.AreEqual("test", wrapper.value);
        }

        [Test]
        public void GetWithEmptyStringReturnsValidWrapper()
        {
            StringWrapper wrapper = StringWrapper.Get("");
            Assert.IsTrue(wrapper != null, "StringWrapper should not be null for empty string");
            Assert.AreEqual("", wrapper.value);
        }

        [Test]
        public void GetReturnsSameInstanceForSameString()
        {
            StringWrapper wrapper1 = StringWrapper.Get("test");
            StringWrapper wrapper2 = StringWrapper.Get("test");
            Assert.AreSame(wrapper1, wrapper2);
        }

        [Test]
        public void GetReturnsDifferentInstancesForDifferentStrings()
        {
            StringWrapper wrapper1 = StringWrapper.Get("hello");
            StringWrapper wrapper2 = StringWrapper.Get("world");
            Assert.AreNotSame(wrapper1, wrapper2);
        }

        [Test]
        public void GetCachesMultipleStrings()
        {
            for (int i = 0; i < NumTries; i++)
            {
                string value = $"test_{i}";
                StringWrapper wrapper1 = StringWrapper.Get(value);
                StringWrapper wrapper2 = StringWrapper.Get(value);
                Assert.AreSame(wrapper1, wrapper2);
                StringWrapper.Remove(value);
            }
        }

        [Test]
        public void RemoveReturnsTrueForExistingString()
        {
            StringWrapper.Get("test");
            bool removed = StringWrapper.Remove("test");
            Assert.IsTrue(removed);
        }

        [Test]
        public void RemoveReturnsFalseForNonExistingString()
        {
            bool removed = StringWrapper.Remove("nonexistent");
            Assert.IsFalse(removed);
        }

        [Test]
        public void RemoveAllowsNewInstanceAfterRemoval()
        {
            StringWrapper wrapper1 = StringWrapper.Get("test");
            StringWrapper.Remove("test");
            StringWrapper wrapper2 = StringWrapper.Get("test");
            Assert.AreNotSame(wrapper1, wrapper2);
        }

        [Test]
        public void EqualsReturnsTrueForSameInstance()
        {
            StringWrapper wrapper = StringWrapper.Get("test");
            Assert.IsTrue(wrapper.Equals(wrapper));
        }

        [Test]
        public void EqualsReturnsTrueForSameValue()
        {
            StringWrapper wrapper1 = StringWrapper.Get("test");
            StringWrapper wrapper2 = StringWrapper.Get("test");
            Assert.IsTrue(wrapper1.Equals(wrapper2));
        }

        [Test]
        public void EqualsReturnsFalseForDifferentValues()
        {
            StringWrapper wrapper1 = StringWrapper.Get("hello");
            StringWrapper wrapper2 = StringWrapper.Get("world");
            Assert.IsFalse(wrapper1.Equals(wrapper2));
            StringWrapper.Remove("hello");
            StringWrapper.Remove("world");
        }

        [Test]
        public void EqualsReturnsFalseForNull()
        {
            StringWrapper wrapper = StringWrapper.Get("test");
            Assert.IsFalse(wrapper.Equals(null));
        }

        [Test]
        public void EqualsObjectReturnsTrueForSameInstance()
        {
            StringWrapper wrapper = StringWrapper.Get("test");
            object obj = wrapper;
            Assert.IsTrue(wrapper.Equals(obj));
        }

        [Test]
        public void EqualsObjectReturnsFalseForNonStringWrapper()
        {
            StringWrapper wrapper = StringWrapper.Get("test");
            object obj = "test";
            Assert.IsFalse(wrapper.Equals(obj));
        }

        [Test]
        public void GetHashCodeConsistentForSameValue()
        {
            StringWrapper wrapper1 = StringWrapper.Get("test");
            StringWrapper wrapper2 = StringWrapper.Get("test");
            Assert.AreEqual(wrapper1.GetHashCode(), wrapper2.GetHashCode());
        }

        [Test]
        public void GetHashCodeMatchesStringHashCode()
        {
            string value = "test";
            StringWrapper wrapper = StringWrapper.Get(value);
            Assert.AreEqual(value.GetHashCode(), wrapper.GetHashCode());
        }

        [Test]
        public void GetHashCodeDifferentForDifferentValues()
        {
            StringWrapper wrapper1 = StringWrapper.Get("hello");
            StringWrapper wrapper2 = StringWrapper.Get("world");
            // Hash codes can collide, but these specific strings should be different
            Assert.AreNotEqual(wrapper1.GetHashCode(), wrapper2.GetHashCode());
            StringWrapper.Remove("hello");
            StringWrapper.Remove("world");
        }

        [Test]
        public void CompareToReturnsZeroForSameInstance()
        {
            StringWrapper wrapper = StringWrapper.Get("test");
            Assert.AreEqual(0, wrapper.CompareTo(wrapper));
        }

        [Test]
        public void CompareToReturnsZeroForSameValue()
        {
            StringWrapper wrapper1 = StringWrapper.Get("test");
            StringWrapper wrapper2 = StringWrapper.Get("test");
            Assert.AreEqual(0, wrapper1.CompareTo(wrapper2));
            Assert.AreEqual(0, wrapper2.CompareTo(wrapper1));
        }

        [Test]
        public void CompareToReturnsDifferentForDifferentValue()
        {
            StringWrapper wrapper1 = StringWrapper.Get("test");
            StringWrapper wrapper2 = StringWrapper.Get("test2");
            Assert.AreNotEqual(0, wrapper1.CompareTo(wrapper2));
            Assert.AreNotEqual(0, wrapper2.CompareTo(wrapper1));
        }

        // Any object compares greater than null, the way every IComparable in the framework does.
        [Test]
        public void CompareToOrdersNullFirst()
        {
            StringWrapper wrapper = StringWrapper.Get("test");
            Assert.AreEqual(1, wrapper.CompareTo(null));
        }

        [Test]
        public void CompareToOrdersByValueRatherThanHash()
        {
            StringWrapper alpha = StringWrapper.Get("alpha");
            StringWrapper beta = StringWrapper.Get("beta");

            Assert.Less(alpha.CompareTo(beta), 0);
            Assert.Greater(beta.CompareTo(alpha), 0);
            Assert.AreEqual(
                Math.Sign(string.CompareOrdinal("alpha", "beta")),
                Math.Sign(alpha.CompareTo(beta))
            );
        }

        [Test]
        public void GetAndRemoveHandleNullWithoutThrowing()
        {
            Assert.IsTrue(StringWrapper.Get(null) == null);
            Assert.IsFalse(StringWrapper.Remove(null));
        }

        [Test]
        public void ToStringReturnsOriginalValue()
        {
            string value = "test";
            StringWrapper wrapper = StringWrapper.Get(value);
            Assert.AreEqual(value, wrapper.ToString());
        }

        [Test]
        public void ToStringHandlesEmptyString()
        {
            StringWrapper wrapper = StringWrapper.Get("");
            Assert.AreEqual("", wrapper.ToString());
        }

        [Test]
        public void ToStringHandlesSpecialCharacters()
        {
            string value = "test\n\t\r!@#$%^&*()";
            StringWrapper wrapper = StringWrapper.Get(value);
            Assert.AreEqual(value, wrapper.ToString());
            StringWrapper.Remove(value);
        }

        /// <summary>
        /// The wrapper is shared with every other holder of the same string, so one borrower's
        /// <c>using</c> block must not evict it. <c>Dispose</c> leaves the cache alone, and
        /// <see cref="StringWrapper.Remove"/> stays the way to administer it
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/646">#646</see>).
        /// </summary>
        [Test]
#pragma warning disable CS0618
        public void DisposeLeavesTheSharedWrapperInTheCache()
        {
            StringWrapper wrapper = StringWrapper.Get("test");
            wrapper.Dispose();

            Assert.AreSame(wrapper, StringWrapper.Get("test"));
            Assert.DoesNotThrow(() => wrapper.Dispose());
            Assert.AreSame(wrapper, StringWrapper.Get("test"));
            Assert.IsTrue(StringWrapper.Remove("test"));
            Assert.IsFalse(StringWrapper.Remove("test"));
        }
#pragma warning restore CS0618

        [Test]
        public void ConcurrentGetReturnsSameInstance()
        {
            // Simulate concurrent access patterns
            HashSet<StringWrapper> wrappers = new();
            for (int i = 0; i < NumTries; i++)
            {
                wrappers.Add(StringWrapper.Get("concurrent"));
            }
            Assert.AreEqual(1, wrappers.Count);
            StringWrapper.Remove("concurrent");
        }

        [Test]
        public void LargeStringHandling()
        {
            string largeString = new('a', 10000);
            StringWrapper wrapper = StringWrapper.Get(largeString);
            Assert.AreEqual(largeString, wrapper.value);
            Assert.AreSame(wrapper, StringWrapper.Get(largeString));
            StringWrapper.Remove(largeString);
        }

        [Test]
        public void UnicodeStringHandling()
        {
            string unicodeString = "Hello 世界 🌍";
            StringWrapper wrapper = StringWrapper.Get(unicodeString);
            Assert.AreEqual(unicodeString, wrapper.value);
            Assert.AreEqual(unicodeString, wrapper.ToString());
            StringWrapper.Remove(unicodeString);
        }

        [Test]
        public void WhitespaceOnlyStringHandling()
        {
            string whitespace = "   \t\n\r   ";
            StringWrapper wrapper = StringWrapper.Get(whitespace);
            Assert.AreEqual(whitespace, wrapper.value);
            StringWrapper.Remove(whitespace);
        }

        [Test]
        public void CaseSensitiveComparison()
        {
            StringWrapper lower = StringWrapper.Get("test");
            StringWrapper upper = StringWrapper.Get("TEST");
            StringWrapper mixed = StringWrapper.Get("Test");

            Assert.AreNotSame(lower, upper);
            Assert.AreNotSame(lower, mixed);
            Assert.AreNotSame(upper, mixed);
            Assert.IsFalse(lower.Equals(upper));
            Assert.IsFalse(lower.Equals(mixed));
            Assert.IsFalse(upper.Equals(mixed));

            StringWrapper.Remove("TEST");
            StringWrapper.Remove("Test");
        }

        [Test]
        public void UsableAsHashSetKey()
        {
            HashSet<StringWrapper> set = new();
            StringWrapper wrapper1 = StringWrapper.Get("test1");
            StringWrapper wrapper2 = StringWrapper.Get("test2");
            StringWrapper wrapper3 = StringWrapper.Get("test1");

            Assert.IsTrue(set.Add(wrapper1));
            Assert.IsTrue(set.Add(wrapper2));
            Assert.IsFalse(set.Add(wrapper3)); // Should not add duplicate
            Assert.AreEqual(2, set.Count);

            StringWrapper.Remove("test1");
            StringWrapper.Remove("test2");
        }

        [Test]
        public void UsableAsDictionaryKey()
        {
            Dictionary<StringWrapper, int> dict = new();
            StringWrapper wrapper1 = StringWrapper.Get("key1");
            StringWrapper wrapper2 = StringWrapper.Get("key2");
            StringWrapper wrapper3 = StringWrapper.Get("key1");

            dict[wrapper1] = 100;
            dict[wrapper2] = 200;
            dict[wrapper3] = 300; // Should overwrite wrapper1's value

            Assert.AreEqual(2, dict.Count);
            Assert.AreEqual(300, dict[wrapper1]);
            Assert.AreEqual(200, dict[wrapper2]);

            StringWrapper.Remove("key1");
            StringWrapper.Remove("key2");
        }
    }
}
