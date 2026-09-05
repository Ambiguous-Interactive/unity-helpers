// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Core.Helper
{
#if UNITY_EDITOR

    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;

    /// <summary>
    /// Covers <see cref="EditorCacheHelper.GetEnumDisplayName"/>, which previously located a member
    /// with <c>Array.IndexOf(Enum.GetValues(type), value)</c> — a fresh array allocation on every
    /// call, sized by the member count. It is now a cached bit-pattern lookup, so these pin the
    /// observable behavior that must survive that change.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EditorCacheHelperEnumDisplayNameTests
    {
        [Test]
        public void UsesTheInspectorNameAttribute()
        {
            Assert.AreEqual(
                "Custom Label",
                EditorCacheHelper.GetEnumDisplayName(DisplayNameSample.Decorated)
            );
        }

        [Test]
        public void NicifiesMembersWithoutAnAttribute()
        {
            Assert.AreEqual(
                "Undecorated Value",
                EditorCacheHelper.GetEnumDisplayName(DisplayNameSample.UndecoratedValue)
            );
        }

        /// <summary>
        /// Asserts that aliased members agree on one declared name, without pinning which one.
        /// </summary>
        /// <remarks>
        /// Enum.GetValues does not guarantee an order among members sharing a value, so asserting
        /// "Original" would pin a runtime detail rather than this method's contract. What must hold
        /// is that the two agree and that the answer is a declared name -- the old Array.IndexOf
        /// lookup and the new map read the same array in the same order, so whatever that order is,
        /// both produce the same result.
        /// </remarks>
        [Test]
        public void AliasedMembersResolveToOneStableName()
        {
            string original = EditorCacheHelper.GetEnumDisplayName(AliasSample.Original);
            string alias = EditorCacheHelper.GetEnumDisplayName(AliasSample.Alias);

            Assert.AreEqual(original, alias);
            // Is.AnyOf is NUnit 3.13+; Unity bundles an older NUnit, so use the Or constraint.
            Assert.That(original, Is.EqualTo("Original").Or.EqualTo("Alias"));
        }

        /// <summary>
        /// Pins negative members of a signed enum against the ToString fallback.
        /// </summary>
        /// <remarks>
        /// The lookup key is the member's 64-bit pattern, and a signed member sign-extends.
        /// </remarks>
        [Test]
        public void ResolvesNegativeMembersOfASignedEnum()
        {
            Assert.AreEqual(
                "Below Zero",
                EditorCacheHelper.GetEnumDisplayName(SignedSample.Negative)
            );
            Assert.AreEqual("Zero", EditorCacheHelper.GetEnumDisplayName(SignedSample.Zero));
        }

        [Test]
        public void NullValueReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, EditorCacheHelper.GetEnumDisplayName(null));
        }

        [Test]
        public void UndefinedValueFallsBackToToString()
        {
            DisplayNameSample undefined = (DisplayNameSample)999;
            Assert.AreEqual(undefined.ToString(), EditorCacheHelper.GetEnumDisplayName(undefined));
        }

        [Test]
        public void RepeatedLookupsReturnTheSameCachedString()
        {
            string first = EditorCacheHelper.GetEnumDisplayName(DisplayNameSample.Decorated);
            string second = EditorCacheHelper.GetEnumDisplayName(DisplayNameSample.Decorated);
            Assert.AreSame(
                first,
                second,
                "A per-call rebuild would hand back a different instance."
            );
        }

        [Test]
        public void SurvivesACacheClear()
        {
            Assert.AreEqual(
                "Custom Label",
                EditorCacheHelper.GetEnumDisplayName(DisplayNameSample.Decorated)
            );
            EditorCacheHelper.ClearAllCaches();
            Assert.AreEqual(
                "Custom Label",
                EditorCacheHelper.GetEnumDisplayName(DisplayNameSample.Decorated),
                "Clearing the name cache must clear the value map with it, or the two disagree."
            );
        }

        private enum DisplayNameSample
        {
            [InspectorName("Custom Label")]
            Decorated = 1,
            UndecoratedValue = 2,
        }

        // Enum aliases must preserve the label chosen by the former Array.IndexOf lookup.
        private enum AliasSample
        {
            Original = 7,
            Alias = 7,
        }

        private enum SignedSample : sbyte
        {
            [InspectorName("Below Zero")]
            Negative = -128,
            Zero = 0,
        }
    }
#endif
}
