// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;

    /// <summary>
    /// One row of the equality-law table driven by <c>EqualityContractTests</c>. Exists so the laws
    /// are written once and every type is fed through the same asserts, rather than each type
    /// growing its own near-duplicate fixture.
    /// </summary>
    public abstract class EqualityContractCase
    {
        /// <summary>
        /// Name shown by the test runner for this row.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Initializes a row with the name the runner shows for it.
        /// </summary>
        /// <param name="label">Name shown by the test runner. Must contain no underscores.</param>
        protected EqualityContractCase(string label)
        {
            Label = label;
        }

        /// <summary>
        /// Asserts every equality law against the values this row carries.
        /// </summary>
        public abstract void AssertLaws();

        /// <inheritdoc />
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// A typed row of the equality-law table: three values that must all be equal to one another, a
    /// fourth that must not be, and any number of foreign objects that <see cref="object.Equals(object)"/>
    /// must refuse.
    /// </summary>
    /// <typeparam name="T">The type under test.</typeparam>
    public sealed class EqualityContractCase<T> : EqualityContractCase
        where T : IEquatable<T>
    {
        private readonly T _first;
        private readonly T _second;
        private readonly T _third;
        private readonly T _different;
        private readonly object[] _foreign;
        private readonly Func<T, T, bool> _equalityOperator;
        private readonly Func<T, T, bool> _inequalityOperator;

        /// <summary>
        /// Initializes a typed row.
        /// </summary>
        /// <param name="label">Name shown by the test runner.</param>
        /// <param name="first">A value equal to <paramref name="second"/> and <paramref name="third"/>.</param>
        /// <param name="second">A separately constructed value equal to <paramref name="first"/>.</param>
        /// <param name="third">A third separately constructed equal value, so transitivity has something to chain through. A row whose type admits only one instance of the value under test says so where it is declared.</param>
        /// <param name="different">A value that must not be equal to the other three.</param>
        /// <param name="foreign">Objects of other types that <see cref="object.Equals(object)"/> must refuse. May be null.</param>
        /// <param name="equalityOperator">The type's <c>==</c>, when it declares one, so operator agreement can be checked.</param>
        /// <param name="inequalityOperator">The type's <c>!=</c>, when it declares one.</param>
        public EqualityContractCase(
            string label,
            T first,
            T second,
            T third,
            T different,
            object[] foreign = null,
            Func<T, T, bool> equalityOperator = null,
            Func<T, T, bool> inequalityOperator = null
        )
            : base(label)
        {
            _first = first;
            _second = second;
            _third = third;
            _different = different;
            _foreign = foreign ?? Array.Empty<object>();
            _equalityOperator = equalityOperator;
            _inequalityOperator = inequalityOperator;
        }

        /// <inheritdoc />
        public override void AssertLaws()
        {
            AssertReflexive();
            AssertSymmetric();
            AssertTransitive();
            AssertEqualValuesShareAHash();
            AssertRefusesNullAndForeignTypes();
            AssertOperatorsAgreeWithEquals();
            AssertHashCollectionRoundTrip();
        }

        private void AssertReflexive()
        {
            Assert.IsTrue(_first.Equals(_first), $"{Label}: a value must equal itself");
            Assert.IsTrue(
                _first.Equals((object)_first),
                $"{Label}: a value must equal itself through Equals(object)"
            );
        }

        private void AssertSymmetric()
        {
            Assert.IsTrue(_first.Equals(_second), $"{Label}: equal values must compare equal");
            Assert.IsTrue(
                _second.Equals(_first),
                $"{Label}: equality must hold in both directions"
            );
            Assert.IsTrue(
                _first.Equals((object)_second),
                $"{Label}: equal values must compare equal through Equals(object)"
            );
            Assert.IsTrue(
                _second.Equals((object)_first),
                $"{Label}: Equals(object) must hold in both directions"
            );

            Assert.IsFalse(
                _first.Equals(_different),
                $"{Label}: values that differ must not compare equal"
            );
            Assert.IsFalse(
                _different.Equals(_first),
                $"{Label}: inequality must hold in both directions"
            );
            Assert.IsFalse(
                _first.Equals((object)_different),
                $"{Label}: values that differ must not compare equal through Equals(object)"
            );
            Assert.IsFalse(
                _different.Equals((object)_first),
                $"{Label}: Equals(object) inequality must hold in both directions"
            );
        }

        private void AssertTransitive()
        {
            Assert.IsTrue(_second.Equals(_third), $"{Label}: the second and third must be equal");
            Assert.IsTrue(
                _first.Equals(_third),
                $"{Label}: equality must be transitive across all three equal values"
            );
        }

        private void AssertEqualValuesShareAHash()
        {
            int hash = _first.GetHashCode();
            Assert.AreEqual(
                hash,
                _first.GetHashCode(),
                $"{Label}: a hash code must not move between calls on an unchanged value"
            );
            Assert.AreEqual(
                hash,
                _second.GetHashCode(),
                $"{Label}: equal values must share a hash code"
            );
            Assert.AreEqual(
                hash,
                _third.GetHashCode(),
                $"{Label}: equal values must share a hash code"
            );
        }

        private void AssertRefusesNullAndForeignTypes()
        {
            Assert.IsFalse(
                _first.Equals((object)null),
                $"{Label}: nothing is equal to null through Equals(object)"
            );
            Assert.IsFalse(
                _first.Equals(new object()),
                $"{Label}: a bare object is not equal to this value"
            );
            Assert.IsFalse(
                _first.Equals("not a value of this type"),
                $"{Label}: a string is not equal to this value"
            );

            foreach (object candidate in _foreign)
            {
                Assert.IsFalse(
                    _first.Equals(candidate),
                    $"{Label}: Equals(object) must refuse {candidate.GetType().Name}, which cannot "
                        + "answer true for this type in return"
                );
            }
        }

        /*
            SerializableType uses == null to mean empty while Equals(null) is false; compare operators only
            against values of the same type.
        */
        private void AssertOperatorsAgreeWithEquals()
        {
            if (_equalityOperator != null)
            {
                Assert.IsTrue(
                    _equalityOperator(_first, _second),
                    $"{Label}: == must agree with Equals for equal values"
                );
                Assert.IsFalse(
                    _equalityOperator(_first, _different),
                    $"{Label}: == must agree with Equals for values that differ"
                );
            }

            if (_inequalityOperator == null)
            {
                return;
            }

            Assert.IsFalse(
                _inequalityOperator(_first, _second),
                $"{Label}: != must be the negation of Equals for equal values"
            );
            Assert.IsTrue(
                _inequalityOperator(_first, _different),
                $"{Label}: != must be the negation of Equals for values that differ"
            );
        }

        private void AssertHashCollectionRoundTrip()
        {
            HashSet<T> set = new() { _first };
            Assert.IsTrue(
                set.Contains(_second),
                $"{Label}: a set holding one value must find an equal value"
            );
            Assert.IsFalse(
                set.Add(_third),
                $"{Label}: a set must refuse a second copy of an equal value"
            );
            Assert.IsFalse(
                set.Contains(_different),
                $"{Label}: a set must not find a value that differs"
            );
            Assert.IsTrue(set.Add(_different), $"{Label}: a set must accept a value that differs");

            Dictionary<T, string> map = new() { [_first] = "stored" };
            Assert.IsTrue(
                map.TryGetValue(_second, out string stored),
                $"{Label}: a dictionary keyed on one value must be reachable through an equal key"
            );
            Assert.AreEqual("stored", stored, $"{Label}: the round-tripped value must survive");
            Assert.IsFalse(
                map.ContainsKey(_different),
                $"{Label}: a dictionary must not resolve a key that differs"
            );
        }
    }
}
