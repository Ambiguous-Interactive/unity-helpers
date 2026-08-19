// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Holds the shipped JSON converter registrations and the fuzz suite that covers them to each
    /// other, so a converter cannot be added to one and missed by the other (#459).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every converter is a save-file entry point, and the cost of missing one is a save that cannot
    /// be loaded rather than a wrong pixel. <c>GameObjectConverter</c> and <c>TouchConverter</c> were
    /// each added with a <c>Read</c> that throws, and nothing outside their own source said so; the
    /// fuzz suite could not have noticed, because a target is only fuzzed once someone adds it to a
    /// list by hand.
    /// </para>
    /// <para>
    /// This is the <c>ContractMirrorTests</c> shape applied to JSON: enumerate what ships, compare it
    /// to what is covered, and require anything left over to be declared with a reason. The
    /// enumeration asks each converter what it claims through <see cref="JsonConverter.CanConvert"/>
    /// rather than reading its generic argument, because six of the registrations are factories whose
    /// converted type is decided per closure.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [SkipUnderIL2CPP]
    public sealed class JsonConverterCoverageTests
    {
        /// <summary>
        /// How many converters the normal configuration registers, pinned against history so a
        /// reorder or a silent addition to the one shared list is visible.
        /// </summary>
        private const int ShippedConverterCount = 48;

        /// <summary>
        /// Where <see cref="JsonStringEnumConverter"/> sits in that list. Registration order is
        /// precedence, and this is the only entry claiming a category rather than a concrete type.
        /// </summary>
        private const int StringEnumConverterIndex = 4;

        /// <summary>
        /// Registered converters this package does not own and does not fuzz, each with the reason.
        /// </summary>
        private static readonly IReadOnlyDictionary<Type, string> NotCovered = new Dictionary<
            Type,
            string
        >
        {
            [typeof(JsonStringEnumConverter)] =
                "System.Text.Json's own enum converter; it writes a name for any enum, so no fixed type covers it and its behaviour is not this package's contract",
        };

        /// <summary>
        /// Every converter in the shipped options must be reachable from the fuzz corpus, declared
        /// write-only, or declared as not ours.
        /// </summary>
        [Test]
        public void EveryRegisteredConverterIsCoveredDeclaredOrExplicitlyNotOurs()
        {
            List<Type> covered = new();
            foreach (JsonConverterFuzzTests.FuzzTarget target in JsonConverterFuzzTests.Targets)
            {
                covered.Add(target.Type);
            }

            foreach (Type writeOnly in JsonConverterFuzzTests.WriteOnlyConverters.Keys)
            {
                covered.Add(writeOnly);
            }

            List<string> failures = new();
            foreach (JsonConverter converter in Serializer.CreateNormalJsonOptions().Converters)
            {
                if (converter == null || NotCovered.ContainsKey(converter.GetType()))
                {
                    continue;
                }

                bool claimsSomething = false;
                for (int i = 0; i < covered.Count && !claimsSomething; ++i)
                {
                    claimsSomething = converter.CanConvert(covered[i]);
                }

                if (!claimsSomething)
                {
                    failures.Add(
                        $"{converter.GetType().Name} is registered in the shipped JSON options and claims no type that "
                            + $"{nameof(JsonConverterFuzzTests)} covers. Add a seed value to its target list, or declare it "
                            + $"in {nameof(JsonConverterFuzzTests)}.{nameof(JsonConverterFuzzTests.WriteOnlyConverters)} with the reason it cannot read what it writes."
                    );
                }
            }

            Assert.That(failures, Is.Empty, Join(failures));
        }

        /// <summary>
        /// The second registration channel: a type carrying <see cref="JsonConverterAttribute"/> is
        /// served by that converter wherever it appears, with or without the package's options.
        /// </summary>
        /// <remarks>
        /// Enumerating <c>JsonSerializerOptions.Converters</c> alone misses these entirely -- three
        /// of this package's own adapters are registered this way and nothing else in the shipped
        /// options names them, so a converter added here is invisible to every check above.
        /// </remarks>
        [Test]
        public void EveryTypeLevelConverterIsCoveredByAFuzzTarget()
        {
            List<Type> runtimeTypes = new();
            try
            {
                runtimeTypes.AddRange(typeof(Serializer).Assembly.GetTypes());
            }
            catch (System.Reflection.ReflectionTypeLoadException loadFailure)
            {
                foreach (Type loaded in loadFailure.Types)
                {
                    if (loaded != null)
                    {
                        runtimeTypes.Add(loaded);
                    }
                }
            }

            List<string> failures = new();
            foreach (Type type in runtimeTypes)
            {
                if (type == null || !type.IsDefined(typeof(JsonConverterAttribute), inherit: false))
                {
                    continue;
                }

                bool covered = false;
                foreach (JsonConverterFuzzTests.FuzzTarget target in JsonConverterFuzzTests.Targets)
                {
                    Type candidate = target.Type.IsGenericType
                        ? target.Type.GetGenericTypeDefinition()
                        : target.Type;
                    if (candidate == type)
                    {
                        covered = true;
                        break;
                    }
                }

                if (!covered)
                {
                    failures.Add(
                        $"{type.Name} carries [{nameof(JsonConverterAttribute)}], so its converter serves it everywhere, but "
                            + $"{nameof(JsonConverterFuzzTests)} has no target for it. A converter reached this way is never fuzzed and never round-trip checked."
                    );
                }
            }

            Assert.That(failures, Is.Empty, Join(failures));
        }

        /// <summary>
        /// The reverse: a declared exception must still name something that ships. A list naming a
        /// converter nobody registers is a claim about a file that no longer exists.
        /// </summary>
        [Test]
        public void EveryDeclaredExceptionStillNamesARegisteredConverter()
        {
            List<JsonConverter> registered = new();
            foreach (JsonConverter converter in Serializer.CreateNormalJsonOptions().Converters)
            {
                if (converter != null)
                {
                    registered.Add(converter);
                }
            }

            List<string> failures = new();
            foreach (
                KeyValuePair<Type, string> writeOnly in JsonConverterFuzzTests.WriteOnlyConverters
            )
            {
                bool claimed = false;
                for (int i = 0; i < registered.Count && !claimed; ++i)
                {
                    claimed = registered[i].CanConvert(writeOnly.Key);
                }

                if (!claimed)
                {
                    failures.Add(
                        $"{writeOnly.Key.Name} is declared write-only ({writeOnly.Value}) but no converter in the shipped options claims it."
                    );
                }
            }

            foreach (KeyValuePair<Type, string> notOurs in NotCovered)
            {
                bool present = false;
                for (int i = 0; i < registered.Count && !present; ++i)
                {
                    present = registered[i].GetType() == notOurs.Key;
                }

                if (!present)
                {
                    failures.Add(
                        $"{notOurs.Key.Name} is declared as not ours ({notOurs.Value}) but is not registered."
                    );
                }
            }

            Assert.That(failures, Is.Empty, Join(failures));
        }

        /// <summary>
        /// A target's <c>ReadSupported</c> flag and the write-only declaration are two statements of
        /// the same fact, so they are asserted against each other rather than maintained in parallel.
        /// </summary>
        [Test]
        public void TheWriteOnlyDeclarationAgreesWithTheFuzzTargets()
        {
            List<string> failures = new();
            foreach (JsonConverterFuzzTests.FuzzTarget target in JsonConverterFuzzTests.Targets)
            {
                bool declaredWriteOnly = JsonConverterFuzzTests.WriteOnlyConverters.ContainsKey(
                    target.Type
                );
                if (declaredWriteOnly == target.ReadSupported)
                {
                    failures.Add(
                        $"{target.Name}: declared write-only is {declaredWriteOnly} but the fuzz target says ReadSupported is {target.ReadSupported}."
                    );
                }
            }

            Assert.That(failures, Is.Empty, Join(failures));
        }

        /// <summary>
        /// The three shipped configurations must register the same converters in the same order, and
        /// the fast one must differ only by <see cref="JsonStringEnumConverter"/>.
        /// </summary>
        /// <remarks>
        /// They used to be three verbatim copies of one forty-six entry list, so a converter added to
        /// one and missed on another silently changed what a value round-trips through depending only
        /// on which options the caller reached for. They are built from one list now; this pins that
        /// they still are, and states the one deliberate difference -- the fast configuration writes
        /// an enum as its number rather than its name -- as a contract rather than as an omission
        /// nobody can tell from a mistake.
        /// </remarks>
        [Test]
        public void EveryShippedConfigurationRegistersTheSameConvertersInTheSameOrder()
        {
            List<Type> normal = RegisteredTypes(Serializer.CreateNormalJsonOptions());
            List<Type> pretty = RegisteredTypes(Serializer.CreatePrettyJsonOptions());
            List<Type> fast = RegisteredTypes(Serializer.CreateFastJsonOptions());

            CollectionAssert.AreEqual(
                normal,
                pretty,
                "The pretty configuration must register exactly what the normal one does."
            );

            List<Type> normalWithoutEnums = new();
            foreach (Type type in normal)
            {
                if (type != typeof(JsonStringEnumConverter))
                {
                    normalWithoutEnums.Add(type);
                }
            }

            CollectionAssert.AreEqual(
                normalWithoutEnums,
                fast,
                "The fast configuration must differ from the normal one by JsonStringEnumConverter and nothing else."
            );
            CollectionAssert.DoesNotContain(
                fast,
                typeof(JsonStringEnumConverter),
                "The fast configuration writes an enum as its underlying number; adding the string converter changes the wire format."
            );

            // Comparing the three lists to each other cannot see a change that reorders the shared
            // list, because it reorders all three identically -- and that is the one risk collapsing
            // three copies into one introduces. The count and the enum converter's position are
            // pinned against history instead: order decides precedence, and JsonStringEnumConverter
            // is the only registration that claims a whole category of types rather than one.
            Assert.AreEqual(
                ShippedConverterCount,
                normal.Count,
                "A converter was added or removed. If that is intended, update ShippedConverterCount and add a fuzz target."
            );
            Assert.AreEqual(
                StringEnumConverterIndex,
                normal.IndexOf(typeof(JsonStringEnumConverter)),
                "JsonStringEnumConverter must stay at the index it has always occupied; moving it changes which converter claims an enum-shaped type first."
            );
        }

        private static List<Type> RegisteredTypes(JsonSerializerOptions options)
        {
            List<Type> types = new();
            foreach (JsonConverter converter in options.Converters)
            {
                if (converter != null)
                {
                    types.Add(converter.GetType());
                }
            }

            return types;
        }

        private static string Join(IReadOnlyList<string> failures)
        {
            StringBuilder builder = new();
            for (int i = 0; i < failures.Count; ++i)
            {
                builder.AppendLine(failures[i]);
            }

            return builder.ToString();
        }
    }
}
