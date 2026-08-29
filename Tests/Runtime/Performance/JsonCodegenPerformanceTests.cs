// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// The measurement that
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/561">#561</see> is
    /// conditional on: how much a generated JSON converter can beat System.Text.Json's reflection
    /// path for a realistic save-game record.
    /// </summary>
    /// <remarks>
    /// <para>The issue asks for a Roslyn JSON generator "only if it was a measured win". The
    /// subject here is a hand-written <see cref="JsonConverter{T}"/> that reads and writes the
    /// record's members one at a time against cached UTF-8 property names, with the enum resolved
    /// by a switch rather than a lookup. That is exactly the code such a generator would emit, so
    /// it is the honest upper bound on what codegen could buy -- a real generator cannot beat it,
    /// and would pay more than it in generality.</para>
    /// <para>One deduction from that upper bound: the names are raw UTF-8 spans rather than
    /// pre-escaped <c>JsonEncodedText</c>, because building one needs <c>JavaScriptEncoder</c> and
    /// this assembly's <c>overrideReferences</c> list does not carry
    /// <c>System.Text.Encodings.Web</c>. The writer therefore re-scans each name for characters
    /// needing escapes on every call, which a generated converter would not. That cost lands on the
    /// subject, so every ratio below is a floor rather than a ceiling.</para>
    /// <para>The reference is the same record through the package's own shipped options, which is
    /// what a consumer gets today. Both configurations are measured, because the two answer
    /// different objections: <c>Normal</c> is the package default and carries
    /// <c>ReferenceHandler.IgnoreCycles</c>, case-insensitive name matching and string enums, none
    /// of which a custom converter pays; <c>Fast</c> turns all three off, so its row is
    /// codegen against plain reflective member walking and nothing else.</para>
    /// <para>Both payload sizes are measured because the answer depends on them. Per-member
    /// dispatch is the only cost codegen removes, so a record whose bytes are mostly one long
    /// array converges toward parity no matter how good the generated code is.</para>
    /// <para>The two sides are held to byte-identical output and to reading each other's payloads
    /// back into an equal graph before anything is timed, and only a workload whose spread is
    /// inside the protocol's limit reaches the table. This fixture reports; it does not gate.</para>
    /// </remarks>
    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class JsonCodegenPerformanceTests
    {
        private const int MeasurementBatches = 3;

        private const int SmallAbilityCount = 4;
        private const int LargeAbilityCount = 256;

        // Sized so every slot lasts tens of milliseconds on both sides. A slot short enough for the
        // clock floor to reach reports a speedup the code never had.
        private const int SmallIterations = 20_000;
        private const int LargeIterations = 4_000;

        private const int AbilityIdBound = 4096;

        private const ulong PayloadSeed = 0x6C8E9CF5709321D5UL;
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong Increment = 1442695040888963407UL;

        private static readonly int[] AbilityCounts = new int[]
        {
            SmallAbilityCount,
            LargeAbilityCount,
        };

        // false = the package's Normal options, true = its Fast options.
        private static readonly bool[] FastOptionChoices = new bool[] { false, true };

        // Written by every measured loop so neither side can be eliminated as dead code. It says
        // nothing about the two agreeing; AssertBothAgree is what checks that.
        private static int _sink;

        [Test]
        [Timeout(0)]
        public void GeneratedConverterShapeComparedAgainstReflection()
        {
            UnityEngine.Debug.Log("| Workload | Ratio | Reference Spread | Subject Spread |");
            UnityEngine.Debug.Log("| -------- | -----:| ----------------:| --------------:|");

            List<string> unstable = new List<string>();
            List<string> unmeasurable = new List<string>();
            int stableWorkloads = 0;

            ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
            // Not a `using` statement: Utf8JsonWriter also implements IAsyncDisposable, whose
            // metadata this test assembly does not reference (overrideReferences).
            Utf8JsonWriter writer = new Utf8JsonWriter(buffer);
            try
            {
                foreach (bool fastOptions in FastOptionChoices)
                {
                    string optionsLabel = fastOptions ? "Fast" : "Normal";
                    JsonSerializerOptions reference = CreateOptions(fastOptions);
                    JsonSerializerOptions subject = CreateOptions(fastOptions);
                    // Ahead of the package's own converters, so nothing else can claim the record.
                    // The fast configuration writes an enum as its underlying number and the normal
                    // one writes its name, so the converter is told which contract it is matching.
                    subject.Converters.Insert(
                        0,
                        new SaveSlotConverter(writeEnumNames: !fastOptions)
                    );

                    foreach (int abilityCount in AbilityCounts)
                    {
                        bool small = abilityCount <= SmallAbilityCount;
                        string sizeLabel = small ? "small" : "large";
                        int iterations = small ? SmallIterations : LargeIterations;
                        SaveSlot record = BuildRecord(abilityCount);
                        AssertBothAgree(record, reference, subject);

                        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(record, reference);

                        // The control runs FIRST and decides whether this platform can be measured.
                        // Asserting the subject on a platform whose clock cannot resolve the
                        // reference would be the absence of a measurement wearing a pass.
                        double controlWrite = MeasureSerialize(
                            record,
                            reference,
                            buffer,
                            writer,
                            iterations
                        );
                        double controlRead = MeasureDeserialize(payload, reference, iterations);
                        if (controlWrite <= 0 || controlRead <= 0)
                        {
                            unmeasurable.Add($"{optionsLabel} {sizeLabel}");
                            continue;
                        }

                        // Warm the subject so its first measured slot is not its first execution.
                        MeasureSerialize(record, subject, buffer, writer, iterations);
                        MeasureDeserialize(payload, subject, iterations);

                        PairedMeasurement write = BenchmarkProtocol.MeasurePaired(
                            () => MeasureSerialize(record, reference, buffer, writer, iterations),
                            () => MeasureSerialize(record, subject, buffer, writer, iterations),
                            MeasurementBatches
                        );
                        stableWorkloads += Publish(
                            $"{optionsLabel} serialize {sizeLabel}",
                            write,
                            unstable
                        )
                            ? 1
                            : 0;

                        PairedMeasurement read = BenchmarkProtocol.MeasurePaired(
                            () => MeasureDeserialize(payload, reference, iterations),
                            () => MeasureDeserialize(payload, subject, iterations),
                            MeasurementBatches
                        );
                        stableWorkloads += Publish(
                            $"{optionsLabel} deserialize {sizeLabel}",
                            read,
                            unstable
                        )
                            ? 1
                            : 0;
                    }
                }
            }
            finally
            {
                writer.Dispose();
            }

            foreach (string workload in unmeasurable)
            {
                UnityEngine.Debug.Log($"not measurable, the clock did not move: {workload}");
            }

            foreach (string workload in unstable)
            {
                UnityEngine.Debug.Log($"unstable, not published: {workload}");
            }

            if (stableWorkloads == 0)
            {
                Assert.Ignore(
                    "Every workload read the machine rather than the code: none came inside the "
                        + $"{BenchmarkProtocol.DefaultSpreadLimit:P0} spread limit on "
                        + $"{Application.platform}."
                );
            }
        }

        private static bool Publish(
            string workload,
            PairedMeasurement measurement,
            List<string> unstable
        )
        {
            if (!measurement.IsStable(BenchmarkProtocol.DefaultSpreadLimit))
            {
                unstable.Add($"{workload} ({measurement})");
                return false;
            }

            UnityEngine.Debug.Log(
                $"| {workload} | {measurement.Ratio:F2} | "
                    + $"{measurement.ReferenceSpread:F4} | {measurement.SubjectSpread:F4} |"
            );
            return true;
        }

        private static JsonSerializerOptions CreateOptions(bool fastOptions)
        {
            return fastOptions
                ? Serializer.CreateFastJsonOptions()
                : Serializer.CreateNormalJsonOptions();
        }

        /// <summary>
        /// Refuses to time two things that disagree. Byte equality is the strongest available
        /// statement that the converter is doing the reflection path's work rather than less of it,
        /// and each side then reads the other's payload back so a shared writing mistake cannot
        /// pass as agreement.
        /// </summary>
        private static void AssertBothAgree(
            SaveSlot record,
            JsonSerializerOptions reference,
            JsonSerializerOptions subject
        )
        {
            byte[] referenceBytes = JsonSerializer.SerializeToUtf8Bytes(record, reference);
            byte[] subjectBytes = JsonSerializer.SerializeToUtf8Bytes(record, subject);
            Assert.AreEqual(
                referenceBytes.Length,
                subjectBytes.Length,
                "The converter wrote a different number of bytes than the reflection path."
            );
            CollectionAssert.AreEqual(
                referenceBytes,
                subjectBytes,
                "The converter and the reflection path wrote different JSON."
            );

            AssertRecordsMatch(
                record,
                JsonSerializer.Deserialize<SaveSlot>(referenceBytes, subject),
                "the converter reading the reflection path's payload"
            );
            AssertRecordsMatch(
                record,
                JsonSerializer.Deserialize<SaveSlot>(subjectBytes, reference),
                "the reflection path reading the converter's payload"
            );
        }

        private static void AssertRecordsMatch(SaveSlot expected, SaveSlot actual, string because)
        {
            Assert.IsTrue(actual != null, because);
            Assert.AreEqual(expected.Level, actual.Level, because);
            Assert.AreEqual(expected.Health, actual.Health, 0f, because);
            Assert.AreEqual(expected.TutorialComplete, actual.TutorialComplete, because);
            Assert.AreEqual(expected.DisplayName, actual.DisplayName, because);
            Assert.AreEqual(expected.Difficulty, actual.Difficulty, because);
            Assert.AreEqual(expected.Position.x, actual.Position.x, 0f, because);
            Assert.AreEqual(expected.Position.y, actual.Position.y, 0f, because);
            Assert.AreEqual(expected.Position.z, actual.Position.z, 0f, because);
            CollectionAssert.AreEqual(
                expected.UnlockedAbilities,
                actual.UnlockedAbilities,
                because
            );
            Assert.AreEqual(expected.PlayTimeSeconds, actual.PlayTimeSeconds, because);
            Assert.AreEqual(expected.Currency, actual.Currency, because);
            Assert.IsTrue(actual.Equipped != null, because);
            Assert.AreEqual(expected.Equipped.ItemId, actual.Equipped.ItemId, because);
            Assert.AreEqual(expected.Equipped.Quantity, actual.Equipped.Quantity, because);
            Assert.AreEqual(expected.Equipped.Durability, actual.Equipped.Durability, 0f, because);
        }

        private static double MeasureSerialize(
            SaveSlot record,
            JsonSerializerOptions options,
            ArrayBufferWriter<byte> buffer,
            Utf8JsonWriter writer,
            int iterations
        )
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int written = 0;
            for (int index = 0; index < iterations; index++)
            {
                buffer.Clear();
                writer.Reset();
                JsonSerializer.Serialize(writer, record, options);
                written += buffer.WrittenCount;
            }

            stopwatch.Stop();
            _sink = written;
            return Throughput(iterations, stopwatch);
        }

        private static double MeasureDeserialize(
            byte[] payload,
            JsonSerializerOptions options,
            int iterations
        )
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int accumulated = 0;
            for (int index = 0; index < iterations; index++)
            {
                SaveSlot decoded = JsonSerializer.Deserialize<SaveSlot>(payload, options);
                accumulated += decoded.Level;
            }

            stopwatch.Stop();
            _sink = accumulated;
            return Throughput(iterations, stopwatch);
        }

        private static double Throughput(int operations, Stopwatch stopwatch)
        {
            double seconds = stopwatch.Elapsed.TotalSeconds;
            return seconds <= 0 ? 0 : operations / seconds;
        }

        private static SaveSlot BuildRecord(int abilityCount)
        {
            ulong state = PayloadSeed;
            List<int> abilities = new List<int>(abilityCount);
            for (int index = 0; index < abilityCount; index++)
            {
                abilities.Add(NextBounded(ref state, AbilityIdBound));
            }

            return new SaveSlot
            {
                Level = 47,
                Health = 82.5f,
                TutorialComplete = true,
                DisplayName = "Wanderer of the Ninth Vault",
                Difficulty = SaveDifficulty.Nightmare,
                Position = new Vector3(128.25f, -14.5f, 903.125f),
                UnlockedAbilities = abilities,
                PlayTimeSeconds = 372_845L,
                Currency = 15_320,
                Equipped = new EquippedItem
                {
                    ItemId = "weapon.spear.frost",
                    Quantity = 1,
                    Durability = 0.734f,
                },
            };
        }

        // The HIGH bits, always. An LCG's low bits have a short period -- bit 0 alternates every
        // draw -- so a `%` taken from the low bits pins the result to one parity and covers half the
        // range it claims to. Measured in #578's benchmark before it was fixed.
        private static int NextBounded(ref ulong state, int exclusiveUpperBound)
        {
            return (int)((Next(ref state) >> 32) % (ulong)exclusiveUpperBound);
        }

        // An LCG rather than one of the package generators: the payload has to be identical on every
        // runtime this runs on, and it must not be the thing being measured.
        private static ulong Next(ref ulong state)
        {
            state = (state * Multiplier) + Increment;
            return state;
        }

        private enum SaveDifficulty
        {
            Unset = 0,
            Story = 1,
            Normal = 2,
            Hard = 3,
            Nightmare = 4,
        }

        /// <summary>
        /// The nested object. A save record that is one flat struct is not the shape this question
        /// is about; the reflection path pays a second metadata dispatch for a member like this.
        /// </summary>
        private sealed class EquippedItem
        {
            [JsonPropertyOrder(1)]
            public string ItemId { get; set; }

            [JsonPropertyOrder(2)]
            public int Quantity { get; set; }

            [JsonPropertyOrder(3)]
            public float Durability { get; set; }
        }

        /// <summary>
        /// Ten members shaped like a real save-game record rather than a micro-type.
        /// </summary>
        /// <remarks>
        /// The order is pinned rather than left to reflection's member order, so the converter's
        /// output can be held to byte equality with the reflection path's on every runtime instead
        /// of on the ones whose <c>GetProperties</c> happens to return declaration order.
        /// </remarks>
        private sealed class SaveSlot
        {
            [JsonPropertyOrder(1)]
            public int Level { get; set; }

            [JsonPropertyOrder(2)]
            public float Health { get; set; }

            [JsonPropertyOrder(3)]
            public bool TutorialComplete { get; set; }

            [JsonPropertyOrder(4)]
            public string DisplayName { get; set; }

            [JsonPropertyOrder(5)]
            public SaveDifficulty Difficulty { get; set; }

            [JsonPropertyOrder(6)]
            public Vector3 Position { get; set; }

            [JsonPropertyOrder(7)]
            public List<int> UnlockedAbilities { get; set; }

            [JsonPropertyOrder(8)]
            public long PlayTimeSeconds { get; set; }

            [JsonPropertyOrder(9)]
            public int Currency { get; set; }

            [JsonPropertyOrder(10)]
            public EquippedItem Equipped { get; set; }
        }

        /// <summary>
        /// The code a JSON source generator emits: cached UTF-8 property names, one member written
        /// or read at a time, the enum resolved by a switch, and no metadata lookup anywhere.
        /// </summary>
        private sealed class SaveSlotConverter : JsonConverter<SaveSlot>
        {
            private static readonly byte[] LevelName = Encoding.UTF8.GetBytes(
                nameof(SaveSlot.Level)
            );
            private static readonly byte[] HealthName = Encoding.UTF8.GetBytes(
                nameof(SaveSlot.Health)
            );
            private static readonly byte[] TutorialCompleteName = Encoding.UTF8.GetBytes(
                nameof(SaveSlot.TutorialComplete)
            );
            private static readonly byte[] DisplayNameName = Encoding.UTF8.GetBytes(
                nameof(SaveSlot.DisplayName)
            );
            private static readonly byte[] DifficultyName = Encoding.UTF8.GetBytes(
                nameof(SaveSlot.Difficulty)
            );
            private static readonly byte[] PositionName = Encoding.UTF8.GetBytes(
                nameof(SaveSlot.Position)
            );
            private static readonly byte[] UnlockedAbilitiesName = Encoding.UTF8.GetBytes(
                nameof(SaveSlot.UnlockedAbilities)
            );
            private static readonly byte[] PlayTimeSecondsName = Encoding.UTF8.GetBytes(
                nameof(SaveSlot.PlayTimeSeconds)
            );
            private static readonly byte[] CurrencyName = Encoding.UTF8.GetBytes(
                nameof(SaveSlot.Currency)
            );
            private static readonly byte[] EquippedName = Encoding.UTF8.GetBytes(
                nameof(SaveSlot.Equipped)
            );

            private static readonly byte[] ItemIdName = Encoding.UTF8.GetBytes(
                nameof(EquippedItem.ItemId)
            );
            private static readonly byte[] QuantityName = Encoding.UTF8.GetBytes(
                nameof(EquippedItem.Quantity)
            );
            private static readonly byte[] DurabilityName = Encoding.UTF8.GetBytes(
                nameof(EquippedItem.Durability)
            );

            // The same three names the package's own Vector3Converter writes, so the two agree.
            private static readonly byte[] XName = Encoding.UTF8.GetBytes(nameof(Vector3.x));
            private static readonly byte[] YName = Encoding.UTF8.GetBytes(nameof(Vector3.y));
            private static readonly byte[] ZName = Encoding.UTF8.GetBytes(nameof(Vector3.z));

            private static readonly byte[] UnsetDifficulty = Encoding.UTF8.GetBytes(
                nameof(SaveDifficulty.Unset)
            );
            private static readonly byte[] StoryDifficulty = Encoding.UTF8.GetBytes(
                nameof(SaveDifficulty.Story)
            );
            private static readonly byte[] NormalDifficulty = Encoding.UTF8.GetBytes(
                nameof(SaveDifficulty.Normal)
            );
            private static readonly byte[] HardDifficulty = Encoding.UTF8.GetBytes(
                nameof(SaveDifficulty.Hard)
            );
            private static readonly byte[] NightmareDifficulty = Encoding.UTF8.GetBytes(
                nameof(SaveDifficulty.Nightmare)
            );

            private readonly bool _writeEnumNames;

            public SaveSlotConverter(bool writeEnumNames)
            {
                _writeEnumNames = writeEnumNames;
            }

            public override SaveSlot Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException($"Invalid token type {reader.TokenType}");
                }

                SaveSlot slot = new SaveSlot();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return slot;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    if (reader.ValueTextEquals(LevelName))
                    {
                        reader.Read();
                        slot.Level = reader.GetInt32();
                    }
                    else if (reader.ValueTextEquals(HealthName))
                    {
                        reader.Read();
                        slot.Health = reader.GetSingle();
                    }
                    else if (reader.ValueTextEquals(TutorialCompleteName))
                    {
                        reader.Read();
                        slot.TutorialComplete = reader.GetBoolean();
                    }
                    else if (reader.ValueTextEquals(DisplayNameName))
                    {
                        reader.Read();
                        slot.DisplayName =
                            reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    }
                    else if (reader.ValueTextEquals(DifficultyName))
                    {
                        reader.Read();
                        slot.Difficulty = _writeEnumNames
                            ? DecodeDifficulty(ref reader)
                            : (SaveDifficulty)reader.GetInt32();
                    }
                    else if (reader.ValueTextEquals(PositionName))
                    {
                        reader.Read();
                        slot.Position = ReadPosition(ref reader);
                    }
                    else if (reader.ValueTextEquals(UnlockedAbilitiesName))
                    {
                        reader.Read();
                        slot.UnlockedAbilities = ReadAbilities(ref reader);
                    }
                    else if (reader.ValueTextEquals(PlayTimeSecondsName))
                    {
                        reader.Read();
                        slot.PlayTimeSeconds = reader.GetInt64();
                    }
                    else if (reader.ValueTextEquals(CurrencyName))
                    {
                        reader.Read();
                        slot.Currency = reader.GetInt32();
                    }
                    else if (reader.ValueTextEquals(EquippedName))
                    {
                        reader.Read();
                        slot.Equipped = ReadEquipped(ref reader);
                    }
                    else
                    {
                        reader.Read();
                        reader.Skip();
                    }
                }

                throw new JsonException($"Incomplete JSON for {nameof(SaveSlot)}");
            }

            public override void Write(
                Utf8JsonWriter writer,
                SaveSlot value,
                JsonSerializerOptions options
            )
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                writer.WriteStartObject();
                writer.WriteNumber(LevelName, value.Level);
                writer.WriteNumber(HealthName, value.Health);
                writer.WriteBoolean(TutorialCompleteName, value.TutorialComplete);
                if (value.DisplayName == null)
                {
                    writer.WriteNull(DisplayNameName);
                }
                else
                {
                    writer.WriteString(DisplayNameName, value.DisplayName);
                }

                if (_writeEnumNames)
                {
                    writer.WriteString(DifficultyName, EncodeDifficulty(value.Difficulty));
                }
                else
                {
                    writer.WriteNumber(DifficultyName, (int)value.Difficulty);
                }

                writer.WriteStartObject(PositionName);
                writer.WriteNumber(XName, value.Position.x);
                writer.WriteNumber(YName, value.Position.y);
                writer.WriteNumber(ZName, value.Position.z);
                writer.WriteEndObject();

                List<int> abilities = value.UnlockedAbilities;
                if (abilities == null)
                {
                    writer.WriteNull(UnlockedAbilitiesName);
                }
                else
                {
                    writer.WriteStartArray(UnlockedAbilitiesName);
                    for (int index = 0; index < abilities.Count; index++)
                    {
                        writer.WriteNumberValue(abilities[index]);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteNumber(PlayTimeSecondsName, value.PlayTimeSeconds);
                writer.WriteNumber(CurrencyName, value.Currency);

                EquippedItem equipped = value.Equipped;
                if (equipped == null)
                {
                    writer.WriteNull(EquippedName);
                }
                else
                {
                    writer.WriteStartObject(EquippedName);
                    if (equipped.ItemId == null)
                    {
                        writer.WriteNull(ItemIdName);
                    }
                    else
                    {
                        writer.WriteString(ItemIdName, equipped.ItemId);
                    }

                    writer.WriteNumber(QuantityName, equipped.Quantity);
                    writer.WriteNumber(DurabilityName, equipped.Durability);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            private static byte[] EncodeDifficulty(SaveDifficulty value)
            {
                switch (value)
                {
                    case SaveDifficulty.Story:
                        return StoryDifficulty;
                    case SaveDifficulty.Normal:
                        return NormalDifficulty;
                    case SaveDifficulty.Hard:
                        return HardDifficulty;
                    case SaveDifficulty.Nightmare:
                        return NightmareDifficulty;
                    default:
                        return UnsetDifficulty;
                }
            }

            private static SaveDifficulty DecodeDifficulty(ref Utf8JsonReader reader)
            {
                if (reader.ValueTextEquals(StoryDifficulty))
                {
                    return SaveDifficulty.Story;
                }

                if (reader.ValueTextEquals(NormalDifficulty))
                {
                    return SaveDifficulty.Normal;
                }

                if (reader.ValueTextEquals(HardDifficulty))
                {
                    return SaveDifficulty.Hard;
                }

                if (reader.ValueTextEquals(NightmareDifficulty))
                {
                    return SaveDifficulty.Nightmare;
                }

                return SaveDifficulty.Unset;
            }

            private static Vector3 ReadPosition(ref Utf8JsonReader reader)
            {
                float x = 0;
                float y = 0;
                float z = 0;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return new Vector3(x, y, z);
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    if (reader.ValueTextEquals(XName))
                    {
                        reader.Read();
                        x = reader.GetSingle();
                    }
                    else if (reader.ValueTextEquals(YName))
                    {
                        reader.Read();
                        y = reader.GetSingle();
                    }
                    else if (reader.ValueTextEquals(ZName))
                    {
                        reader.Read();
                        z = reader.GetSingle();
                    }
                    else
                    {
                        reader.Read();
                        reader.Skip();
                    }
                }

                throw new JsonException($"Incomplete JSON for {nameof(Vector3)}");
            }

            private static List<int> ReadAbilities(ref Utf8JsonReader reader)
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                List<int> abilities = new List<int>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        return abilities;
                    }

                    abilities.Add(reader.GetInt32());
                }

                throw new JsonException(
                    $"Incomplete JSON for {nameof(SaveSlot.UnlockedAbilities)}"
                );
            }

            private static EquippedItem ReadEquipped(ref Utf8JsonReader reader)
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                EquippedItem equipped = new EquippedItem();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return equipped;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    if (reader.ValueTextEquals(ItemIdName))
                    {
                        reader.Read();
                        equipped.ItemId =
                            reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    }
                    else if (reader.ValueTextEquals(QuantityName))
                    {
                        reader.Read();
                        equipped.Quantity = reader.GetInt32();
                    }
                    else if (reader.ValueTextEquals(DurabilityName))
                    {
                        reader.Read();
                        equipped.Durability = reader.GetSingle();
                    }
                    else
                    {
                        reader.Read();
                        reader.Skip();
                    }
                }

                throw new JsonException($"Incomplete JSON for {nameof(EquippedItem)}");
            }
        }
    }
}
