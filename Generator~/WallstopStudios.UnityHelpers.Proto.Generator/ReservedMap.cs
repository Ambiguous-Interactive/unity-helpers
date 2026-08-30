// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System;
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// The field numbers and member names one contract's <c>[WProtoReserved]</c> declarations hold
    /// against every member of that contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the contract itself and from nothing else. A reservation is a statement about one
    /// type's own field-number space, so inheriting one from a base -- whose numbers live in a
    /// different space entirely -- would refuse a member for a collision that cannot happen.
    /// </para>
    /// <para>
    /// The record exists because the declaration that spends a number is deleted along with the
    /// member it sits on, so <c>WPROTO002</c> -- which fires on two LIVE claims -- cannot see a
    /// number a deletion freed
    /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/608">#608</see>).
    /// </para>
    /// </remarks>
    internal sealed class ReservedMap
    {
        internal const string ReservedAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoReservedAttribute";

        private static readonly ReservedMap EmptyMap = new ReservedMap(
            new HashSet<int>(),
            new HashSet<string>(StringComparer.Ordinal)
        );

        private readonly HashSet<int> _numbers;
        private readonly HashSet<string> _names;

        private ReservedMap(HashSet<int> numbers, HashSet<string> names)
        {
            _numbers = numbers;
            _names = names;
        }

        /// <summary>A map for a contract that reserves nothing.</summary>
        internal static ReservedMap Empty => EmptyMap;

        /// <summary>Whether this contract reserves anything at all.</summary>
        internal bool IsEmpty => _numbers.Count == 0 && _names.Count == 0;

        /// <summary>The reserved field numbers, ascending.</summary>
        internal IEnumerable<int> Numbers
        {
            get
            {
                List<int> ordered = new List<int>(_numbers);
                ordered.Sort();
                return ordered;
            }
        }

        /// <summary>The reserved member names, in ordinal order.</summary>
        internal IEnumerable<string> Names
        {
            get
            {
                List<string> ordered = new List<string>(_names);
                ordered.Sort(StringComparer.Ordinal);
                return ordered;
            }
        }

        /// <summary>
        /// Indexes one contract's reservations.
        /// </summary>
        /// <param name="contract">The contract to read.</param>
        /// <returns>The map; empty when the contract reserves nothing.</returns>
        internal static ReservedMap Build(INamedTypeSymbol contract)
        {
            if (contract == null)
            {
                return EmptyMap;
            }

            HashSet<int> numbers = new HashSet<int>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (AttributeData attribute in contract.GetAttributes())
            {
                if (
                    attribute.AttributeClass == null
                    || attribute.AttributeClass.ToDisplayString() != ReservedAttribute
                )
                {
                    continue;
                }

                foreach (TypedConstant value in Arguments(attribute))
                {
                    if (value.Value is int number)
                    {
                        numbers.Add(number);
                    }
                    else if (value.Value is string name && !string.IsNullOrEmpty(name))
                    {
                        names.Add(name);
                    }
                }
            }

            return numbers.Count == 0 && names.Count == 0
                ? EmptyMap
                : new ReservedMap(numbers, names);
        }

        /// <summary>Whether a field number may not be used.</summary>
        /// <param name="fieldNumber">The number a member is claiming.</param>
        /// <returns><c>true</c> when the contract reserves it.</returns>
        internal bool ReservesNumber(int fieldNumber)
        {
            return _numbers.Contains(fieldNumber);
        }

        /// <summary>
        /// Explains why a reserved field number cannot be taken by a subtype declaration.
        /// </summary>
        /// <param name="fieldNumber">The number being claimed.</param>
        /// <param name="contractName">The contract that reserves it.</param>
        /// <returns>The clause an include or subtype diagnostic appends.</returns>
        /// <remarks>
        /// Members and subtype discriminators share ONE field-number space -- a base's includes are
        /// numbered against its members -- so a rule that bound only <c>[WProtoMember]</c> would be
        /// one an author steps around by writing the number on an include instead.
        /// </remarks>
        internal static string ReservedProblem(int fieldNumber, string contractName)
        {
            return "field number "
                + fieldNumber
                + " is reserved on '"
                + contractName
                + "' with [WProtoReserved]. A subtype's number and a member's number are the same "
                + "space, so a reservation binds both. Every payload written before the removal "
                + "still carries that field, and a discriminator sharing it reads those saves back "
                + "as the wrong type. Use a free number, or delete the matching [WProtoReserved] if "
                + "this really is the removed declaration coming back";
        }

        /// <summary>Whether a member name may not be used.</summary>
        /// <param name="memberName">The name a member is declared under.</param>
        /// <returns><c>true</c> when the contract reserves it.</returns>
        internal bool ReservesName(string memberName)
        {
            return !string.IsNullOrEmpty(memberName) && _names.Contains(memberName);
        }

        /// <summary>
        /// Flattens one declaration's arguments, whichever overload wrote them.
        /// </summary>
        /// <param name="attribute">The reservation.</param>
        /// <returns>Every value it names, arrays expanded.</returns>
        /// <remarks>
        /// Both constructors are <c>(first, params rest[])</c>, so a declaration arrives as a scalar
        /// followed by an array. Written to expand any array it finds rather than to assume that
        /// shape, so a later overload cannot silently drop its values.
        /// </remarks>
        private static IEnumerable<TypedConstant> Arguments(AttributeData attribute)
        {
            foreach (TypedConstant argument in attribute.ConstructorArguments)
            {
                if (argument.Kind == TypedConstantKind.Array)
                {
                    foreach (TypedConstant element in argument.Values)
                    {
                        yield return element;
                    }

                    continue;
                }

                yield return argument;
            }
        }
    }
}
