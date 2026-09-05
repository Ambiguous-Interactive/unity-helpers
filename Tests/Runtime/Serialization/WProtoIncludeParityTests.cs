// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Every subtype protobuf-net can write, WallstopProto writes under the same field number, and
    /// the reverse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two are declared in different places and, since <c>[WProtoSubtype]</c>, from different
    /// ends: protobuf-net resolves a subtype only from the base's own <c>[ProtoInclude]</c>, while
    /// WallstopProto accepts <c>[WProtoInclude]</c> on the base or <c>[WProtoSubtype]</c> on the
    /// subtype. Nothing but a comparison stops one gaining a subtype the other does not have, and a
    /// payload names its subtype by field number alone -- so a tag present on one side and absent on
    /// the other is a value that reads back as its base, losing a level of type identity, on
    /// whichever path serves it.
    /// </para>
    /// <para>
    /// Assembly-wide rather than a list of types. A list is the thing that goes stale: the defect
    /// this exists to catch is a subtype added to one side and forgotten on the other, and a fixture
    /// enumerating the pairs by hand would have to be edited by the same person who forgot.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoIncludeParityTests
    {
        [Test]
        public void EveryProtoIncludeHasAWallstopProtoDeclarationUnderTheSameTag()
        {
            List<string> mismatches = new List<string>();
            Assembly assembly = typeof(AbstractRandom).Assembly;
            Dictionary<Type, SortedDictionary<int, Type>> wallstopByBase = WallstopIncludes(
                assembly
            );

            int compared = 0;
            foreach (Type contract in assembly.GetTypes())
            {
                if (contract.GetCustomAttribute<WProtoContractAttribute>() == null)
                {
                    continue;
                }

                SortedDictionary<int, Type> protobuf = new SortedDictionary<int, Type>();
                foreach (
                    ProtoIncludeAttribute include in contract.GetCustomAttributes<ProtoIncludeAttribute>(
                        false
                    )
                )
                {
                    protobuf[include.Tag] = include.KnownType;
                }

                SortedDictionary<int, Type> wallstop = wallstopByBase.TryGetValue(
                    contract,
                    out SortedDictionary<int, Type> declared
                )
                    ? declared
                    : new SortedDictionary<int, Type>();
                if (protobuf.Count == 0 && wallstop.Count == 0)
                {
                    continue;
                }

                compared++;
                if (!SameMap(protobuf, wallstop))
                {
                    mismatches.Add(
                        contract.Name
                            + ": protobuf-net has "
                            + Describe(protobuf)
                            + ", WallstopProto has "
                            + Describe(wallstop)
                    );
                }
            }

            Assert.IsEmpty(
                mismatches,
                "A subtype one encoder can write and the other cannot reads back as its base:\n"
                    + string.Join("\n", mismatches)
            );
            Assert.Less(0, compared, "The sweep found no polymorphic contract to compare.");
        }

        [Test]
        public void TheRandomHierarchyDeclaresItselfFromTheSubtypes()
        {
            /*
                The package generator hierarchy exercises subtype-owned declarations without a brittle parent
                include list.
            */
            Assembly assembly = typeof(AbstractRandom).Assembly;
            List<Type> generators = assembly
                .GetTypes()
                .Where(candidate =>
                    candidate.IsSubclassOf(typeof(AbstractRandom))
                    && candidate.GetCustomAttribute<WProtoContractAttribute>() != null
                )
                .ToList();

            Assert.Less(1, generators.Count, "The hierarchy has generators to check.");
            Assert.IsEmpty(
                typeof(AbstractRandom).GetCustomAttributes<WProtoIncludeAttribute>(false),
                "AbstractRandom declares its subtypes from the subtypes now."
            );

            List<string> undeclared = generators
                .Where(generator =>
                    !generator
                        .GetCustomAttributes<WProtoSubtypeAttribute>(false)
                        .Any(subtype => subtype.BaseType == typeof(AbstractRandom))
                )
                .Select(generator => generator.Name)
                .ToList();

            Assert.IsEmpty(
                undeclared,
                "These generators are not serializable through AbstractRandom: "
                    + string.Join(", ", undeclared)
            );
        }

        private static Dictionary<Type, SortedDictionary<int, Type>> WallstopIncludes(
            Assembly assembly
        )
        {
            Dictionary<Type, SortedDictionary<int, Type>> byBase =
                new Dictionary<Type, SortedDictionary<int, Type>>();

            foreach (Type candidate in assembly.GetTypes())
            {
                foreach (
                    WProtoIncludeAttribute include in candidate.GetCustomAttributes<WProtoIncludeAttribute>(
                        false
                    )
                )
                {
                    Record(byBase, candidate, include.Tag, include.KnownType);
                }

                foreach (
                    WProtoSubtypeAttribute subtype in candidate.GetCustomAttributes<WProtoSubtypeAttribute>(
                        false
                    )
                )
                {
                    Record(byBase, subtype.BaseType, subtype.Tag, candidate);
                }
            }

            return byBase;
        }

        private static void Record(
            Dictionary<Type, SortedDictionary<int, Type>> byBase,
            Type baseType,
            int tag,
            Type subtype
        )
        {
            if (baseType == null)
            {
                return;
            }

            if (!byBase.TryGetValue(baseType, out SortedDictionary<int, Type> declared))
            {
                declared = new SortedDictionary<int, Type>();
                byBase[baseType] = declared;
            }

            declared[tag] = subtype;
        }

        private static bool SameMap(
            SortedDictionary<int, Type> left,
            SortedDictionary<int, Type> right
        )
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (KeyValuePair<int, Type> entry in left)
            {
                if (!right.TryGetValue(entry.Key, out Type other) || other != entry.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private static string Describe(SortedDictionary<int, Type> map)
        {
            if (map.Count == 0)
            {
                return "nothing";
            }

            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<int, Type> entry in map)
            {
                if (0 < builder.Length)
                {
                    builder.Append(", ");
                }

                builder.Append(entry.Key).Append('=').Append(entry.Value.Name);
            }

            return builder.ToString();
        }
    }
}
