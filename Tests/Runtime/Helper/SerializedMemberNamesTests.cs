// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Pins the one name translation Unity's serializer forces on a caller.
    /// </summary>
    /// <remarks>
    /// The mangled spelling is a C# compiler detail rather than a documented contract, so the
    /// decisive test is not the string literal -- it is <see cref="TheShapeMatchesWhatTheCompilerEmits"/>,
    /// which reads a real auto-property's backing field and fails if the convention ever moves.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializedMemberNamesTests
    {
        [Test]
        public void TheShapeMatchesWhatTheCompilerEmits()
        {
            FieldInfo emitted = null;
            foreach (
                FieldInfo candidate in typeof(Subject).GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                )
            )
            {
                if (candidate.Name.Contains(nameof(Subject.Speed)))
                {
                    emitted = candidate;
                }
            }

            Assert.IsTrue(
                emitted != null,
                "the compiler emits a backing field for an auto-property"
            );
            Assert.AreEqual(
                emitted.Name,
                SerializedMemberNames.BackingFieldFor(nameof(Subject.Speed)),
                "the translation must produce the name the compiler actually emitted"
            );
            Assert.IsTrue(SerializedMemberNames.IsBackingField(emitted.Name));
            Assert.IsTrue(
                SerializedMemberNames.TryGetPropertyName(emitted.Name, out string recovered)
            );
            Assert.AreEqual(nameof(Subject.Speed), recovered);
        }

        [TestCase(null, TestName = "NullName")]
        [TestCase("", TestName = "EmptyName")]
        [TestCase("speed", TestName = "OrdinaryField")]
        [TestCase("<>", TestName = "AngleBracketsOnly")]
        [TestCase(">k__BackingField", TestName = "MissingOpeningBracket")]
        [TestCase("<Speed>", TestName = "MissingSuffix")]
        public void AnOrdinaryNameIsNotMistakenForABackingField(string name)
        {
            Assert.IsFalse(SerializedMemberNames.IsBackingField(name));
            Assert.IsFalse(SerializedMemberNames.TryGetPropertyName(name, out string recovered));
            Assert.AreEqual(name, recovered, "a name that is not a backing field comes back as-is");
        }

        /// <summary>
        /// No input reaches the substring with an out-of-range offset or length.
        /// </summary>
        /// <remarks>
        /// The recovery does arithmetic on two lengths, so the answer to "are those indices
        /// absolutely valid" has to be a test rather than a reading. The names below are the ones
        /// that get the arithmetic closest to the edge: the empty property name, a name one
        /// character shorter than the affixes it would need, and the affixes overlapping.
        /// </remarks>
        [TestCase("<>k__BackingField", TestName = "EmptyPropertyName")]
        [TestCase("<k__BackingField", TestName = "PrefixRunsIntoSuffix")]
        [TestCase(">k__BackingField", TestName = "SuffixAlone")]
        [TestCase("<", TestName = "PrefixAlone")]
        [TestCase("<>", TestName = "AffixesOnly")]
        [TestCase("k__BackingField", TestName = "SuffixWithoutBrackets")]
        [TestCase("<<A>k__BackingField>k__BackingField", TestName = "DoublyWrapped")]
        [TestCase("<A>k__BackingField<B>k__BackingField", TestName = "TwoConcatenated")]
        public void NoNameMakesTheRecoveryThrow(string name)
        {
            string recovered = null;
            bool recoveredAName = false;
            Assert.DoesNotThrow(() =>
                recoveredAName = SerializedMemberNames.TryGetPropertyName(name, out recovered)
            );
            Assert.DoesNotThrow(() => SerializedMemberNames.IsBackingField(name));
            Assert.DoesNotThrow(() => SerializedMemberNames.BackingFieldFor(name));

            if (SerializedMemberNames.IsBackingField(name))
            {
                Assert.IsTrue(
                    recoveredAName && !string.IsNullOrEmpty(recovered),
                    "a name accepted as a backing field must yield a non-empty property name"
                );
            }
        }

        /// <summary>
        /// A name one character too short is refused, which is what keeps the arithmetic in range.
        /// </summary>
        [Test]
        public void TheShortestAcceptedNameIsExactlyOneCharacterOfProperty()
        {
            const string tooShort = "<>k__BackingField";
            string shortest = SerializedMemberNames.BackingFieldFor("A");

            Assert.AreEqual(
                tooShort.Length + 1,
                shortest.Length,
                "one character of property name is one character longer than the affixes alone"
            );
            Assert.IsFalse(
                SerializedMemberNames.IsBackingField(tooShort),
                "a zero-length property name is not a backing field"
            );
            Assert.IsTrue(SerializedMemberNames.IsBackingField(shortest));
            Assert.IsTrue(SerializedMemberNames.TryGetPropertyName(shortest, out string recovered));
            Assert.AreEqual("A", recovered);
        }

        [Test]
        public void TranslatingIsIdempotentAndRoundTrips()
        {
            string once = SerializedMemberNames.BackingFieldFor("Speed");

            Assert.AreEqual(
                once,
                SerializedMemberNames.BackingFieldFor(once),
                "translating an already-translated name must not wrap it twice"
            );
            Assert.AreEqual(
                once,
                SerializedMemberNames.BackingFieldFor("Speed"),
                "the cached second call must answer the same as the first"
            );

            Assert.IsTrue(SerializedMemberNames.TryGetPropertyName(once, out string recovered));
            Assert.AreEqual("Speed", recovered);
        }

        [Test]
        public void AnAbsentNameIsReturnedUnchanged()
        {
            Assert.IsTrue(SerializedMemberNames.BackingFieldFor(null) == null);
            Assert.AreEqual(string.Empty, SerializedMemberNames.BackingFieldFor(string.Empty));
        }

        private sealed class Subject
        {
            [field: SerializeField]
            public int Speed { get; set; }
        }
    }
}
