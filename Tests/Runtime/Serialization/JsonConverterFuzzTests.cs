// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.Json;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Feeds structure-aware malformed JSON to every Unity-aware converter and asserts what a
    /// shipped player needs from a save file it did not write (#437).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The existing <see cref="SerializerFuzzTests"/> fuzzes random <b>bytes</b>, which the
    /// System.Text.Json tokenizer rejects before a converter is ever entered, so the forty-odd
    /// converters in this package had no hostile-input coverage at all. The payloads here are
    /// derived from each converter's <b>own output</b> and then mutated, so every one of them is
    /// well-formed JSON that reaches <c>Read</c> and disagrees with it about a value.
    /// </para>
    /// <para>
    /// A converter's contract with System.Text.Json is that malformed input is reported as
    /// <see cref="JsonException"/>. The reader's own accessors already honour it -- <c>GetInt32</c>
    /// handed a string throws <see cref="InvalidOperationException"/>, but System.Text.Json tags and
    /// re-wraps that one, which is why every scalar converter here passes untouched. What escapes is
    /// what a converter throws on its <b>own</b> behalf: five defects were found this way, and every
    /// one of them is a converter handing payload-derived values to an API that validates them --
    /// a constructor, a Unity setter -- and letting that API's exception out.
    /// </para>
    /// <para>
    /// The re-encoding check is the other half, and it caught the worst of the five: a value a
    /// converter <b>writes</b> and then refuses to read back is a save file that cannot be loaded,
    /// with no hostile input involved at all.
    /// </para>
    /// <para>
    /// Every payload is reproducible: the corpus is generated from a fixed seed and a deterministic
    /// walk, and a failure prints the exact JSON, which is a <c>[TestCase]</c> for
    /// <see cref="AKnownHostilePayloadIsRefusedCleanly"/>.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [SkipUnderIL2CPP]
    public sealed class JsonConverterFuzzTests
    {
        /// <summary>
        /// Values substituted for one node of a valid payload at a time. Chosen for the accessor
        /// each one breaks: a wrong token kind breaks <c>GetInt32</c>/<c>GetString</c>, a fractional
        /// or out-of-range number breaks the integral accessors, and a container where a scalar
        /// belongs breaks any reader that assumes it can step over one token.
        /// </summary>
        private static readonly string[] HostileValues =
        {
            "null",
            "true",
            "\"\"",
            "\"not-a-number\"",
            // An all-zero identifier: the value a type's own "unset" writes, and the one a reader
            // that validates its input is most likely to refuse for being unset.
            "\"00000000-0000-0000-0000-000000000000\"",
            "1.5",
            "-1",
            "2147483648",
            "-2147483649",
            "99999999999999999999999999",
            "1e309",
            "[]",
            "[1,2,3]",
            "{}",
            "{\"nested\":1}",
        };

        private const int TruncationSamples = 24;

        private const int ByteMutationSamples = 48;

        /// <summary>
        /// How many times a repeated member's contents are repeated by <c>GrowArray</c>. Twelve
        /// clears the eight-key ceiling <see cref="Gradient"/> enforces and the sixteen-element
        /// shape <see cref="Matrix4x4"/> expects, which no single-node substitution reaches.
        /// </summary>
        private const int GrownArrayLength = 12;

        private static IReadOnlyList<FuzzTarget> _targets;

        /// <summary>
        /// The converters that deliberately write a value they cannot rebuild, each with the reason.
        /// This is the declaration <see cref="JsonConverterCoverageTests"/> checks the registered
        /// converters against, so a forty-eighth converter cannot be added the way these two were --
        /// with a <c>Read</c> that throws and nothing anywhere that says so.
        /// </summary>
        /// <remarks>
        /// Adding an entry is the escape hatch and is meant to cost something: a type listed here is
        /// asserted to refuse every read, so a converter that quietly gains a read path fails this
        /// list rather than silently outgrowing it.
        /// </remarks>
        public static readonly IReadOnlyDictionary<Type, string> WriteOnlyConverters =
            new Dictionary<Type, string>
            {
                [typeof(Touch)] =
                    "the platform owns every field, so there is nothing to restore a Touch into",
                [typeof(GameObject)] =
                    "the record written is a name/type/instance-id for diagnostics; a scene object cannot be rebuilt from it",
            };

        /// <summary>
        /// The <see cref="WriteOnlyConverters"/> keys, as a test case source.
        /// </summary>
        public static IEnumerable<Type> WriteOnlyTypes => WriteOnlyConverters.Keys;

        /// <summary>
        /// One entry per converter registered in <c>Serializer.CreateNormalJsonOptions</c>. The seed is
        /// a value the converter can write; the corpus is derived from its own output.
        /// </summary>
        public static IReadOnlyList<FuzzTarget> Targets
        {
            get
            {
                if (_targets == null)
                {
                    _targets = BuildTargets();
                }
                return _targets;
            }
        }

        /// <summary>
        /// A converter handed a value it cannot read must say so with <see cref="JsonException"/>.
        /// Anything else is a framework exception escaping through a public API, which a consumer
        /// calling <c>JsonSerializer.Deserialize</c> with these options cannot catch by contract.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(Targets))]
        public void MalformedPayloadsAreReportedAsJsonException(FuzzTarget target)
        {
            JsonSerializerOptions options = Serializer.CreateNormalJsonOptions();
            StringBuilder failures = new();
            int failureCount = 0;
            int accepted = 0;
            int examined = 0;

            foreach (string payload in Corpus(target))
            {
                examined++;
                try
                {
                    object decoded = JsonSerializer.Deserialize(payload, target.Type, options);
                    if (decoded != null)
                    {
                        // A null result for a reference type is System.Text.Json answering the
                        // `null` literal itself; the converter's Read was never entered, so it
                        // counts for neither the coverage gate nor the write-only one.
                        accepted++;
                    }
                }
                catch (JsonException)
                {
                    // The documented answer for input a converter cannot read.
                }
                catch (NotSupportedException)
                {
                    // System.Text.Json's own answer for a type it declines to construct.
                }
                catch (Exception unexpected)
                {
                    failureCount++;
                    if (failureCount <= 5)
                    {
                        failures
                            .Append("  ")
                            .Append(unexpected.GetType().FullName)
                            .Append(" from ")
                            .Append(Abbreviate(payload))
                            .Append(" -> ")
                            .Append(unexpected.Message)
                            .Append('\n');
                    }
                }
            }

            Assert.Zero(
                failureCount,
                $"{target.Name}: {failureCount} of {examined} malformed payloads left a "
                    + $"non-JsonException escape the converter.\n{failures}"
            );

            if (target.ReadSupported)
            {
                Assert.Positive(
                    accepted,
                    $"{target.Name}: no payload in a corpus of {examined} was accepted, so this "
                        + "target proves nothing about the read path. The seed or the mutator is "
                        + "broken."
                );
            }
            else
            {
                Assert.Zero(
                    accepted,
                    $"{target.Name} is declared write-only, but {accepted} payloads were read back. "
                        + "Either the converter gained a read path or the declaration is stale."
                );
            }
        }

        /// <summary>
        /// The package-level entry point converts every failure into
        /// <see cref="SerializationFailureException"/>, including one a converter reports oddly.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(Targets))]
        public void SerializerOnlyLeaksSerializationFailureException(FuzzTarget target)
        {
            foreach (string payload in Corpus(target))
            {
                try
                {
                    _ = Serializer.JsonDeserialize<object>(payload, target.Type);
                }
                catch (SerializationFailureException)
                {
                    // The documented answer.
                }
                catch (Exception unexpected)
                {
                    Assert.Fail(
                        $"{target.Name}: JsonDeserialize leaked "
                            + $"{unexpected.GetType().FullName} on {Abbreviate(payload)}: "
                            + unexpected.Message
                    );
                }
            }
        }

        /// <summary>
        /// <c>TryJsonDeserialize</c> promises a bool rather than an exception, for every input.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(Targets))]
        public void TryJsonDeserializeNeverThrows(FuzzTarget target)
        {
            foreach (string payload in Corpus(target))
            {
                try
                {
                    _ = Serializer.TryJsonDeserialize(payload, out object _, target.Type);
                }
                catch (Exception unexpected)
                {
                    Assert.Fail(
                        $"{target.Name}: TryJsonDeserialize threw "
                            + $"{unexpected.GetType().FullName} on {Abbreviate(payload)}: "
                            + unexpected.Message
                    );
                }
            }
        }

        /// <summary>
        /// A payload a converter accepts must survive its own re-encoding. A shape that reads back
        /// but cannot be written and read again is a converter whose <c>Write</c> and <c>Read</c>
        /// disagree, which corrupts the next save rather than the one being loaded.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(Targets))]
        public void AcceptedPayloadsSurviveTheirOwnReEncoding(FuzzTarget target)
        {
            JsonSerializerOptions options = Serializer.CreateNormalJsonOptions();

            foreach (string payload in Corpus(target))
            {
                object first;
                try
                {
                    first = JsonSerializer.Deserialize(payload, target.Type, options);
                }
                catch (Exception)
                {
                    continue;
                }

                string reEncoded;
                try
                {
                    reEncoded = JsonSerializer.Serialize(first, target.Type, options);
                }
                catch (Exception unexpected)
                {
                    Assert.Fail(
                        $"{target.Name}: a value read from {Abbreviate(payload)} could not be "
                            + $"written back: {unexpected.GetType().FullName}: {unexpected.Message}"
                    );
                    return;
                }

                try
                {
                    _ = JsonSerializer.Deserialize(reEncoded, target.Type, options);
                }
                catch (Exception unexpected)
                {
                    Assert.Fail(
                        $"{target.Name}: this converter wrote {Abbreviate(reEncoded)} and then "
                            + $"refused to read it: {unexpected.GetType().FullName}: "
                            + $"{unexpected.Message} (source payload {Abbreviate(payload)})"
                    );
                    return;
                }
            }
        }

        /// <summary>
        /// A <see cref="Type"/> written through an <see cref="object"/> reaches the package's
        /// <c>TypeConverter</c> whatever concrete <see cref="Type"/> subclass carries it.
        /// </summary>
        /// <remarks>
        /// <c>typeof(X)</c> hands back the internal <c>System.RuntimeType</c>, so a rule that
        /// exempted public runtime types from the base-converter walk looked correct and was not:
        /// <see cref="System.Reflection.TypeDelegator"/> is a <b>public</b> subclass of
        /// <see cref="Type"/>, and it reached the reflection-light writer instead, which threw
        /// <see cref="NullReferenceException"/> walking it.
        /// </remarks>
        [Test]
        public void ATypeSubclassIsWrittenThroughTheTypeConverter()
        {
            System.Reflection.TypeDelegator delegated = new(typeof(Vector3));

            string viaDelegator = Serializer.JsonStringify<object>(delegated);
            string viaType = Serializer.JsonStringify<object>(typeof(Vector3));

            Assert.AreEqual(
                viaType,
                viaDelegator,
                "A public Type subclass must be written by the same converter as the Type it carries."
            );
        }

        /// <summary>
        /// A converter that only writes must say so with <see cref="NotSupportedException"/>.
        /// </summary>
        /// <remarks>
        /// Both threw <see cref="NotImplementedException"/>, which reads as an unfinished converter
        /// rather than a deliberate one-way encoding, and which no caller would think to catch.
        /// <see cref="GameObject"/> is asserted here rather than as a fuzz target because reading
        /// one needs no instance, and allocating one would pull this whole fixture into Unity object
        /// lifecycle management for a converter with no read path to fuzz.
        /// </remarks>
        [Test]
        [TestCaseSource(nameof(WriteOnlyTypes))]
        public void AWriteOnlyConverterRefusesToRead(Type type)
        {
            JsonSerializerOptions options = Serializer.CreateNormalJsonOptions();

            Assert.Throws<NotSupportedException>(
                () => JsonSerializer.Deserialize("{}", type, options),
                $"{type.Name} is declared write-only ({WriteOnlyConverters.ValueFor(type)}) and must refuse a read with NotSupportedException"
            );
        }

        /// <summary>
        /// A converter that is not declared write-only must read back the value it just wrote.
        /// </summary>
        /// <remarks>
        /// <see cref="AcceptedPayloadsSurviveTheirOwnReEncoding"/> cannot make this assertion: a
        /// payload it fails to decode is skipped, because most of its corpus is deliberately
        /// hostile, so a converter whose <b>own seed</b> does not read back is silently passed over
        /// rather than reported. That is precisely the shape of the <see cref="WGuid"/> defect --
        /// a value the converter writes and then refuses -- and it is a save file that cannot be
        /// loaded rather than a rejected attack.
        /// </remarks>
        [Test]
        [TestCaseSource(nameof(Targets))]
        public void AReadableConverterReadsBackTheValueItWrote(FuzzTarget target)
        {
            JsonSerializerOptions options = Serializer.CreateNormalJsonOptions();
            string written = target.Seeds[0];

            if (!target.ReadSupported)
            {
                Assert.Throws<NotSupportedException>(
                    () => JsonSerializer.Deserialize(written, target.Type, options),
                    $"{target.Name} is declared write-only and must refuse its own output too"
                );
                return;
            }

            object restored = null;
            Assert.DoesNotThrow(
                () =>
                {
                    restored = JsonSerializer.Deserialize(written, target.Type, options);
                },
                $"{target.Name} wrote {Abbreviate(written)} and refused to read it back."
            );
            Assert.IsTrue(
                restored != null,
                $"{target.Name} wrote {Abbreviate(written)} and read it back as null."
            );

            // Re-encoding rather than comparing values, for two reasons. `restored` is a boxed struct
            // for most of this corpus, so a null check proves nothing there; and several reference
            // targets (AnimationCurve, Gradient, RectOffset) have reference equality only. The bytes
            // are the contract anyway -- a converter that reads its own output back as a *different*
            // value writes different bytes for it, which the null check above cannot see.
            string rewritten = JsonSerializer.Serialize(restored, target.Type, options);
            Assert.AreEqual(
                written,
                rewritten,
                $"{target.Name} wrote {Abbreviate(written)}, read it back, and then wrote {Abbreviate(rewritten)} -- the value did not survive."
            );
        }

        /// <summary>
        /// Reordering object properties, escaping their names and adding insignificant whitespace
        /// must not change the value a converter reads (#462).
        /// </summary>
        [Test]
        [TestCaseSource(nameof(Targets))]
        public void EquivalentJsonSpellingsPreserveConverterValue(FuzzTarget target)
        {
            if (!target.ReadSupported)
            {
                return;
            }

            JsonSerializerOptions options = Serializer.CreateNormalJsonOptions();
            int acceptedSeedCount = 0;

            foreach (string seed in target.Seeds)
            {
                object original = null;
                bool originalAccepted = true;
                try
                {
                    original = JsonSerializer.Deserialize(seed, target.Type, options);
                }
                catch (JsonException)
                {
                    originalAccepted = false;
                }

                using JsonDocument document = JsonDocument.Parse(seed);
                string equivalent = BuildEquivalentJson(document.RootElement);
                Assert.AreNotEqual(
                    seed,
                    equivalent,
                    $"{target.Name}: the equivalent-spelling transform did not change {Abbreviate(seed)}."
                );

                using JsonDocument transformedDocument = JsonDocument.Parse(equivalent);
                Assert.IsTrue(
                    EquivalentStructureIsReversed(
                        document.RootElement,
                        transformedDocument.RootElement,
                        out int propertyCount
                    ),
                    $"{target.Name}: property names must be case-insensitively unique and every object must be reversed in {Abbreviate(seed)}."
                );
                Assert.IsTrue(
                    PropertyTokensUseOnlyUnicodeEscapes(equivalent, propertyCount),
                    $"{target.Name}: at least one property name was not Unicode-escaped in {Abbreviate(equivalent)}."
                );

                if (!originalAccepted)
                {
                    Assert.Throws<JsonException>(
                        () => JsonSerializer.Deserialize(equivalent, target.Type, options),
                        $"{target.Name}: equivalent spelling changed a rejected payload into an accepted value. "
                            + $"Original {Abbreviate(seed)}; transformed {Abbreviate(equivalent)}."
                    );
                    continue;
                }

                acceptedSeedCount++;
                object transformed = JsonSerializer.Deserialize(equivalent, target.Type, options);
                string originalCanonical = JsonSerializer.Serialize(original, target.Type, options);
                string transformedCanonical = JsonSerializer.Serialize(
                    transformed,
                    target.Type,
                    options
                );

                Assert.AreEqual(
                    originalCanonical,
                    transformedCanonical,
                    $"{target.Name}: equivalent JSON changed the decoded value. Original "
                        + $"{Abbreviate(seed)}; transformed {Abbreviate(equivalent)}."
                );
            }

            Assert.Positive(
                acceptedSeedCount,
                $"{target.Name}: no accepted seed exercised the metamorphic equivalence oracle."
            );
        }

        /// <summary>
        /// An empty <see cref="WGuid"/> is what an unset id serializes to, and it must load again.
        /// </summary>
        /// <remarks>
        /// <see cref="WGuid"/> is version-4-only, so <c>WGuid.TryParse</c> refuses the all-zero GUID
        /// that <c>Write</c> emits for <see cref="WGuid.Empty"/> -- the converter rejected its own
        /// output, and every save holding an unset id was unreadable.
        /// </remarks>
        [Test]
        public void AnEmptyWGuidSurvivesAJsonRoundTrip()
        {
            string json = Serializer.JsonStringify(WGuid.Empty);
            WGuid restored = Serializer.JsonDeserialize<WGuid>(json);

            Assert.IsTrue(restored.IsEmpty, $"An empty WGuid wrote {json} and did not read back.");
        }

        /// <summary>
        /// The converter accepts a GUID's text in two shapes, and they must agree about the empty
        /// one. The first fix reached only the bare string, which left the object form refusing an
        /// unset id -- the same defect one branch over.
        /// </summary>
        [Test]
        [TestCase("\"00000000-0000-0000-0000-000000000000\"")]
        [TestCase("{\"Guid\":\"00000000-0000-0000-0000-000000000000\"}")]
        [TestCase("{\"_low\":0,\"_high\":0}")]
        [TestCase("\"\"")]
        [TestCase("null")]
        public void EveryEncodingOfAnEmptyWGuidReadsBack(string payload)
        {
            WGuid restored = Serializer.JsonDeserialize<WGuid>(payload);

            Assert.IsTrue(restored.IsEmpty, $"{payload} should read back as an empty WGuid.");
        }

        /// <summary>
        /// Every converter-backed value must be writable at the root of a graph, not only as a
        /// member of a POCO whose declared property type names it.
        /// </summary>
        /// <remarks>
        /// <see cref="Type"/> is the case that failed: it is abstract, so the write path replaced
        /// the declared type with <c>value.GetType()</c>, which is the internal
        /// <c>System.RuntimeType</c>. System.Text.Json refuses that outright, and the package's own
        /// <c>TypeConverter</c> never matched it.
        /// </remarks>
        [Test]
        [TestCaseSource(nameof(Targets))]
        public void ASeedValueCanBeWrittenAtTheRoot(FuzzTarget target)
        {
            Assert.DoesNotThrow(
                () =>
                {
                    _ = Serializer.JsonStringify(target.Seed);
                },
                $"{target.Name}: a value of this type could not be written at the root of a graph."
            );
        }

        /// <summary>
        /// The payloads the fuzzer has already found, pinned so a regression is a named failure
        /// rather than a corpus that happens to still contain the shape.
        /// </summary>
        [Test]
        [TestCase(typeof(Vector2Int), "{\"x\":\"not-a-number\",\"y\":0}")]
        [TestCase(typeof(Vector2Int), "{\"x\":1.5,\"y\":0}")]
        [TestCase(typeof(Vector2Int), "{\"x\":2147483648,\"y\":0}")]
        [TestCase(typeof(Vector3), "{\"x\":true,\"y\":0,\"z\":0}")]
        [TestCase(typeof(Color), "{\"r\":{},\"g\":0,\"b\":0,\"a\":1}")]
        [TestCase(typeof(Type), "12345")]
        [TestCase(typeof(BitSet), "{\"capacity\":\"huge\",\"indices\":[]}")]
        [TestCase(
            typeof(Range<int>),
            "{\"min\":1,\"max\":-1,\"startInclusive\":true,\"endInclusive\":false}"
        )]
        [TestCase(typeof(Range<int>), "{\"min\":1,\"startInclusive\":true,\"endInclusive\":false}")]
        [TestCase(
            typeof(Range<float>),
            "{\"min\":2.5,\"max\":-1.5,\"startInclusive\":true,\"endInclusive\":true}"
        )]
        [TestCase(typeof(WGuid), "{\"_low\":81985529216486895,\"_high\":-1147797409030816257}")]
        [TestCase(
            typeof(Gradient),
            "{\"mode\":\"Blend\",\"colorKeys\":[{\"color\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1},\"time\":0},"
                + "{\"color\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1},\"time\":0},"
                + "{\"color\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1},\"time\":0},"
                + "{\"color\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1},\"time\":0},"
                + "{\"color\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1},\"time\":0},"
                + "{\"color\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1},\"time\":0},"
                + "{\"color\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1},\"time\":0},"
                + "{\"color\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1},\"time\":0},"
                + "{\"color\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1},\"time\":0}],\"alphaKeys\":[]}"
        )]
        public void AKnownHostilePayloadIsRefusedCleanly(Type type, string payload)
        {
            JsonSerializerOptions options = Serializer.CreateNormalJsonOptions();

            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize(payload, type, options),
                $"{type.Name} must refuse {payload} with JsonException"
            );
        }

        private static IEnumerable<string> Corpus(FuzzTarget target)
        {
            foreach (string seed in target.Seeds)
            {
                foreach (string payload in CorpusFor(seed))
                {
                    yield return payload;
                }
            }
        }

        private static IEnumerable<string> CorpusFor(string seed)
        {
            yield return seed;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(seed);
            }
            catch (JsonException)
            {
                document = null;
            }

            if (document != null)
            {
                using (document)
                {
                    int nodeCount = CountNodes(document.RootElement);
                    for (int node = 0; node < nodeCount; node++)
                    {
                        foreach (string hostile in HostileValues)
                        {
                            yield return Rebuild(
                                document.RootElement,
                                new Mutation(node, MutationKind.ReplaceValue, hostile)
                            );
                        }

                        yield return Rebuild(
                            document.RootElement,
                            new Mutation(node, MutationKind.RenameProperty, null)
                        );
                        yield return Rebuild(
                            document.RootElement,
                            new Mutation(node, MutationKind.DropProperty, null)
                        );
                        yield return Rebuild(
                            document.RootElement,
                            new Mutation(node, MutationKind.DuplicateProperty, null)
                        );
                        yield return Rebuild(
                            document.RootElement,
                            new Mutation(node, MutationKind.DuplicateArrayElement, null)
                        );
                        yield return Rebuild(
                            document.RootElement,
                            new Mutation(node, MutationKind.GrowArray, null)
                        );
                    }
                }
            }

            for (int sample = 1; sample <= TruncationSamples && sample < seed.Length; sample++)
            {
                yield return seed.Substring(0, seed.Length * sample / (TruncationSamples + 1));
            }

            System.Random random = new(unchecked((int)0x5EED_F0DD));
            char[] scratch = seed.ToCharArray();
            for (int sample = 0; sample < ByteMutationSamples; sample++)
            {
                int index = random.Next(scratch.Length);
                char original = scratch[index];
                scratch[index] = (char)('!' + random.Next(90));
                yield return new string(scratch);
                scratch[index] = original;
            }
        }

        private static int CountNodes(JsonElement element)
        {
            int count = 1;
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        count += CountNodes(property.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        count += CountNodes(item);
                    }
                    break;
            }
            return count;
        }

        private static bool EquivalentStructureIsReversed(
            JsonElement original,
            JsonElement transformed,
            out int propertyCount
        )
        {
            propertyCount = 0;
            if (original.ValueKind != transformed.ValueKind)
            {
                return false;
            }

            switch (original.ValueKind)
            {
                case JsonValueKind.Object:
                    List<JsonProperty> originalProperties = new();
                    List<JsonProperty> transformedProperties = new();
                    foreach (JsonProperty property in original.EnumerateObject())
                    {
                        originalProperties.Add(property);
                    }
                    foreach (JsonProperty property in transformed.EnumerateObject())
                    {
                        transformedProperties.Add(property);
                    }
                    if (originalProperties.Count != transformedProperties.Count)
                    {
                        return false;
                    }
                    HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonProperty originalProperty in originalProperties)
                    {
                        if (!names.Add(originalProperty.Name))
                        {
                            return false;
                        }
                    }
                    propertyCount += originalProperties.Count;
                    for (int index = 0; index < originalProperties.Count; index++)
                    {
                        JsonProperty originalProperty = originalProperties[index];
                        JsonProperty transformedProperty = transformedProperties[
                            transformedProperties.Count - index - 1
                        ];
                        if (
                            !string.Equals(
                                originalProperty.Name,
                                transformedProperty.Name,
                                StringComparison.Ordinal
                            )
                            || !EquivalentStructureIsReversed(
                                originalProperty.Value,
                                transformedProperty.Value,
                                out int nestedPropertyCount
                            )
                        )
                        {
                            return false;
                        }
                        propertyCount += nestedPropertyCount;
                    }
                    break;
                case JsonValueKind.Array:
                    List<JsonElement> originalItems = new();
                    List<JsonElement> transformedItems = new();
                    foreach (JsonElement item in original.EnumerateArray())
                    {
                        originalItems.Add(item);
                    }
                    foreach (JsonElement item in transformed.EnumerateArray())
                    {
                        transformedItems.Add(item);
                    }
                    if (originalItems.Count != transformedItems.Count)
                    {
                        return false;
                    }
                    for (int index = 0; index < originalItems.Count; index++)
                    {
                        if (
                            !EquivalentStructureIsReversed(
                                originalItems[index],
                                transformedItems[index],
                                out int nestedPropertyCount
                            )
                        )
                        {
                            return false;
                        }
                        propertyCount += nestedPropertyCount;
                    }
                    break;
            }
            return true;
        }

        private static bool PropertyTokensUseOnlyUnicodeEscapes(
            string transformed,
            int expectedPropertyCount
        )
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(transformed);
            Utf8JsonReader reader = new(utf8);
            int propertyCount = 0;
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                propertyCount++;
                ReadOnlySpan<byte> rawName = reader.ValueSpan;
                if (rawName.Length == 0)
                {
                    continue;
                }
                if (rawName.Length % 6 != 0)
                {
                    return false;
                }
                for (int index = 0; index < rawName.Length; index += 6)
                {
                    if (rawName[index] != (byte)'\\' || rawName[index + 1] != (byte)'u')
                    {
                        return false;
                    }
                    for (int digit = 2; digit < 6; digit++)
                    {
                        byte value = rawName[index + digit];
                        bool isHex =
                            value is >= (byte)'0' and <= (byte)'9'
                            || value is >= (byte)'a' and <= (byte)'f'
                            || value is >= (byte)'A' and <= (byte)'F';
                        if (!isHex)
                        {
                            return false;
                        }
                    }
                }
            }
            return propertyCount == expectedPropertyCount;
        }

        private static string BuildEquivalentJson(JsonElement root)
        {
            StringBuilder builder = new();
            builder.Append('\n');
            WriteEquivalentJson(builder, root, 0);
            builder.Append('\n');
            return builder.ToString();
        }

        private static void WriteEquivalentJson(
            StringBuilder builder,
            JsonElement element,
            int depth
        )
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    List<JsonProperty> properties = new();
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        properties.Add(property);
                    }

                    builder.Append('{');
                    for (int index = properties.Count - 1; 0 <= index; index--)
                    {
                        JsonProperty property = properties[index];
                        builder.Append('\n').Append(' ', (depth + 1) * 2).Append('"');
                        AppendEscapedPropertyName(builder, property.Name);
                        builder.Append("\": ");
                        WriteEquivalentJson(builder, property.Value, depth + 1);
                        if (0 < index)
                        {
                            builder.Append(',');
                        }
                    }
                    if (0 < properties.Count)
                    {
                        builder.Append('\n').Append(' ', depth * 2);
                    }
                    builder.Append('}');
                    break;
                case JsonValueKind.Array:
                    builder.Append('[');
                    int itemIndex = 0;
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        if (0 < itemIndex)
                        {
                            builder.Append(',');
                        }
                        builder.Append('\n').Append(' ', (depth + 1) * 2);
                        WriteEquivalentJson(builder, item, depth + 1);
                        itemIndex++;
                    }
                    if (0 < itemIndex)
                    {
                        builder.Append('\n').Append(' ', depth * 2);
                    }
                    builder.Append(']');
                    break;
                default:
                    builder.Append(element.GetRawText());
                    break;
            }
        }

        private static void AppendEscapedPropertyName(StringBuilder builder, string name)
        {
            for (int index = 0; index < name.Length; index++)
            {
                builder
                    .Append("\\u")
                    .Append(((int)name[index]).ToString("x4", CultureInfo.InvariantCulture));
            }
        }

        private static string Rebuild(JsonElement root, Mutation mutation)
        {
            ArrayBufferWriter<byte> buffer = new();
            // Not a `using` statement: Utf8JsonWriter also implements IAsyncDisposable, whose
            // metadata this test assembly does not reference (overrideReferences).
            Utf8JsonWriter writer = new(buffer);
            try
            {
                int cursor = 0;
                WriteMutated(writer, root, null, false, ref cursor, mutation);
                writer.Flush();
            }
            finally
            {
                writer.Dispose();
            }
            return Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray());
        }

        private static void WriteMutated(
            Utf8JsonWriter writer,
            JsonElement element,
            string propertyName,
            bool inArray,
            ref int cursor,
            Mutation mutation
        )
        {
            int index = cursor;
            cursor++;

            if (index == mutation.NodeIndex && propertyName != null)
            {
                switch (mutation.Kind)
                {
                    case MutationKind.DropProperty:
                        SkipSubtree(element, ref cursor);
                        return;
                    case MutationKind.RenameProperty:
                        writer.WritePropertyName(propertyName + "§unknown");
                        WriteVerbatim(writer, element, ref cursor);
                        return;
                    case MutationKind.DuplicateProperty:
                        writer.WritePropertyName(propertyName);
                        int firstCopy = cursor;
                        WriteVerbatim(writer, element, ref firstCopy);
                        writer.WritePropertyName(propertyName);
                        WriteDiffering(writer, element, ref cursor);
                        return;
                }
            }

            if (
                index == mutation.NodeIndex
                && inArray
                && mutation.Kind == MutationKind.DuplicateArrayElement
            )
            {
                int firstCopy = cursor;
                WriteVerbatim(writer, element, ref firstCopy);
                WriteVerbatim(writer, element, ref cursor);
                return;
            }

            if (propertyName != null)
            {
                writer.WritePropertyName(propertyName);
            }

            if (index == mutation.NodeIndex && mutation.Kind == MutationKind.ReplaceValue)
            {
                using JsonDocument replacement = JsonDocument.Parse(mutation.Replacement);
                replacement.RootElement.WriteTo(writer);
                SkipSubtree(element, ref cursor);
                return;
            }

            if (
                index == mutation.NodeIndex
                && mutation.Kind == MutationKind.GrowArray
                && element.ValueKind == JsonValueKind.Array
            )
            {
                writer.WriteStartArray();
                for (int repeat = 0; repeat < GrownArrayLength; repeat++)
                {
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        item.WriteTo(writer);
                    }
                }
                writer.WriteEndArray();
                SkipSubtree(element, ref cursor);
                return;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        WriteMutated(
                            writer,
                            property.Value,
                            property.Name,
                            false,
                            ref cursor,
                            mutation
                        );
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        WriteMutated(writer, item, null, true, ref cursor, mutation);
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        private static void WriteVerbatim(
            Utf8JsonWriter writer,
            JsonElement element,
            ref int cursor
        )
        {
            element.WriteTo(writer);
            SkipSubtree(element, ref cursor);
        }

        /// <summary>
        /// Writes a value that is never equal to <paramref name="element"/>, consuming the same
        /// span of the node walk that writing it verbatim would.
        /// </summary>
        /// <remarks>
        /// The second copy of a duplicated property has to <b>differ</b> from the first, or the
        /// mutation cannot detect the thing it is named for: two identical copies decode to the same
        /// object whether the converter keeps the first, keeps the last, or merges them, so the only
        /// outcome it could ever distinguish is a throw. The kind is preserved where the kind has
        /// more than one value, so the payload stays the shape a converter expects and the divergence
        /// is in the value rather than in the token.
        /// </remarks>
        private static void WriteDiffering(
            Utf8JsonWriter writer,
            JsonElement element,
            ref int cursor
        )
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    if (!element.EnumerateObject().MoveNext())
                    {
                        writer.WriteNumber("§second", 1);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    if (!element.EnumerateArray().MoveNext())
                    {
                        writer.WriteNumberValue(1);
                    }
                    writer.WriteEndArray();
                    break;
                case JsonValueKind.String:
                    writer.WriteStringValue(element.GetString() + "§second");
                    break;
                case JsonValueKind.Number:
                    writer.WriteNumberValue(element.GetRawText() == "0" ? 1 : 0);
                    break;
                case JsonValueKind.True:
                    writer.WriteBooleanValue(false);
                    break;
                case JsonValueKind.False:
                    writer.WriteBooleanValue(true);
                    break;
                default:
                    // Null is the one kind with a single value, so the copies differ by kind here or
                    // they do not differ at all.
                    writer.WriteNumberValue(0);
                    break;
            }

            SkipSubtree(element, ref cursor);
        }

        private static void SkipSubtree(JsonElement element, ref int cursor)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        cursor++;
                        SkipSubtree(property.Value, ref cursor);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        cursor++;
                        SkipSubtree(item, ref cursor);
                    }
                    break;
            }
        }

        private static string Abbreviate(string payload)
        {
            if (payload == null)
            {
                return "<null>";
            }
            return payload.Length <= 120 ? payload : payload.Substring(0, 120) + "...";
        }

        private static IReadOnlyList<FuzzTarget> BuildTargets()
        {
            AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            Gradient gradient = new();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );

            BitSet bits = new(64);
            _ = bits.TrySet(3);
            _ = bits.TrySet(61);

            Deque<int> deque = new(8);
            deque.PushBack(1);
            deque.PushBack(2);

            CyclicBuffer<int> cyclic = new(4);
            cyclic.Add(7);
            cyclic.Add(9);

            return new List<FuzzTarget>
            {
                new(typeof(Vector2), new Vector2(1.5f, -2.25f)),
                new(typeof(Vector3), new Vector3(1.5f, -2.25f, 3f)),
                new(typeof(Vector4), new Vector4(1f, 2f, 3f, 4f)),
                new(typeof(Vector2Int), new Vector2Int(3, -4)),
                new(typeof(Vector3Int), new Vector3Int(3, -4, 5)),
                new(typeof(FastVector2Int), new FastVector2Int(3, -4)),
                new(typeof(FastVector3Int), new FastVector3Int(3, -4, 5)),
                new(typeof(Quaternion), new Quaternion(0.1f, 0.2f, 0.3f, 0.9f)),
                new(typeof(Matrix4x4), Matrix4x4.identity),
                new(typeof(Color), new Color(0.1f, 0.2f, 0.3f, 0.4f)),
                new(typeof(Color32), new Color32(10, 20, 30, 40)),
                new(typeof(Rect), new Rect(1f, 2f, 3f, 4f)),
                new(typeof(RectInt), new RectInt(1, 2, 3, 4)),
                new(typeof(RectOffset), new RectOffset(1, 2, 3, 4)),
                new(typeof(Bounds), new Bounds(Vector3.one, Vector3.one * 2f)),
                new(typeof(BoundsInt), new BoundsInt(Vector3Int.one, Vector3Int.one * 2)),
                new(typeof(BoundingSphere), new BoundingSphere(Vector3.one, 2f)),
                new(typeof(Plane), new Plane(Vector3.up, 3f)),
                new(typeof(Ray), new Ray(Vector3.zero, Vector3.forward)),
                new(typeof(Ray2D), new Ray2D(Vector2.zero, Vector2.right)),
                new(typeof(Pose), new Pose(Vector3.one, Quaternion.identity)),
                new(typeof(LayerMask), (LayerMask)5),
                new(typeof(RangeInt), new RangeInt(2, 7)),
                new(typeof(Hash128), Hash128.Compute("fuzz")),
                new(typeof(Resolution), new Resolution { width = 1920, height = 1080 }),
                new(typeof(RenderTextureDescriptor), new RenderTextureDescriptor(256, 128)),
                new(typeof(SphericalHarmonicsL2), default(SphericalHarmonicsL2)),
                new(typeof(AnimationCurve), curve),
                new(typeof(Gradient), gradient),
                new(typeof(ParticleSystem.MinMaxCurve), new ParticleSystem.MinMaxCurve(2f)),
                new(
                    typeof(ParticleSystem.MinMaxGradient),
                    new ParticleSystem.MinMaxGradient(Color.green)
                ),
                new(typeof(UnityEngine.UI.ColorBlock), UnityEngine.UI.ColorBlock.defaultColorBlock),
                new(typeof(RaycastHit), default(RaycastHit)),
                FuzzTarget.WriteOnly(typeof(Touch), default(Touch)),
                new(typeof(Scene), SceneManager.GetActiveScene()),
                new(typeof(Type), typeof(Vector3)),
                new(
                    typeof(WGuid),
                    WGuid.NewGuid(),
                    "{\"_low\":81985529216486895,\"_high\":-1147797409030816257}",
                    "{\"Guid\":\"2f3a9b4c-8d1f-4cba-8df7-2af00f5c6c1e\"}",
                    "\"\""
                ),
                new(typeof(Range<int>), new Range<int>(1, 10, true, false)),
                new(typeof(Range<float>), new Range<float>(-1.5f, 2.5f, false, true)),
                new(typeof(BitSet), bits),
                new(typeof(ImmutableBitSet), bits.ToImmutable()),
                new(typeof(Deque<int>), deque),
                new(typeof(CyclicBuffer<int>), cyclic),
                new(typeof(SerializableList<int>), new SerializableList<int> { 1, 2, 3 }),
                new(typeof(SerializableType), new SerializableType(typeof(Vector3))),
                new(typeof(SerializableNullable<int>), new SerializableNullable<int>(7)),
                new(typeof(SerializableHashSet<int>), new SerializableHashSet<int> { 1, 2, 3 }),
                new(
                    typeof(SerializableDictionary<string, int>),
                    new SerializableDictionary<string, int> { { "a", 1 }, { "b", 2 } }
                ),
                new(
                    typeof(SerializableSortedDictionary<string, int>),
                    new SerializableSortedDictionary<string, int> { { "a", 1 }, { "b", 2 } }
                ),
            };
        }

        private enum MutationKind
        {
            ReplaceValue = 1,
            RenameProperty = 2,
            DropProperty = 3,
            DuplicateProperty = 4,
            DuplicateArrayElement = 5,
            GrowArray = 6,
        }

        private readonly struct Mutation
        {
            public Mutation(int nodeIndex, MutationKind kind, string replacement)
            {
                NodeIndex = nodeIndex;
                Kind = kind;
                Replacement = replacement;
            }

            public int NodeIndex { get; }
            public MutationKind Kind { get; }
            public string Replacement { get; }
        }

        /// <summary>
        /// A converter-backed type, the value used to generate its corpus, and that value's JSON.
        /// </summary>
        public sealed class FuzzTarget
        {
            private readonly object _seed;
            private readonly string[] _alternateSeeds;
            private string[] _seeds;

            /// <param name="alternateSeeds">
            /// Shapes that reach branches a converter's own output does not -- accepted legacy
            /// encodings and well-formed probes that validation may reject. Mutating only the
            /// converter's own output leaves those branches unvisited.
            /// </param>
            public FuzzTarget(Type type, object seed, params string[] alternateSeeds)
            {
                Type = type;
                Name = type.Name;
                _seed = seed;
                _alternateSeeds = alternateSeeds ?? Array.Empty<string>();
                ReadSupported = true;
            }

            /// <summary>
            /// A converter that writes a value for diagnostics and cannot rebuild it. Every payload
            /// must be refused, and refused with <see cref="NotSupportedException"/> rather than
            /// with <see cref="NotImplementedException"/>, which reads as an unfinished converter.
            /// </summary>
            public static FuzzTarget WriteOnly(Type type, object seed)
            {
                return new FuzzTarget(type, seed) { ReadSupported = false };
            }

            public Type Type { get; }
            public string Name { get; }
            public object Seed => _seed;
            public bool ReadSupported { get; private set; }

            /// <summary>
            /// Built on first use rather than in the constructor: a seed that cannot be written is a
            /// finding about one converter, and building it eagerly would take the whole corpus
            /// down with it.
            /// </summary>
            public IReadOnlyList<string> Seeds
            {
                get
                {
                    if (_seeds == null)
                    {
                        string[] built = new string[_alternateSeeds.Length + 1];
                        built[0] = JsonSerializer.Serialize(
                            _seed,
                            Type,
                            Serializer.CreateNormalJsonOptions()
                        );
                        Array.Copy(_alternateSeeds, 0, built, 1, _alternateSeeds.Length);
                        _seeds = built;
                    }
                    return _seeds;
                }
            }

            public override string ToString()
            {
                return Name;
            }
        }
    }
}
