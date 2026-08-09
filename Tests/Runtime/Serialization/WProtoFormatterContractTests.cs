// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins the <see cref="IWProtoFormatter{T}"/> lifecycle-hook contract against a hand-written
    /// formatter shaped exactly like the ones the generator will emit into consumer assemblies.
    /// </summary>
    /// <remarks>
    /// The formatter is a nested type of the contract it serializes. That is not decoration: it is
    /// how generated code reaches <b>private</b> hook methods and private backing fields without
    /// reflection, which is what makes the whole approach work under IL2CPP -- and it has to work
    /// for a game's own types, not just this package's, so the mechanism is exercised here through
    /// public API only.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed partial class WProtoFormatterContractTests
    {
        [Test]
        public void SerializationHooksRunInOrderAndExactlyOnce()
        {
            HookedMessage message = new(7, "alpha");
            byte[] buffer = new byte[64];

            HookedMessage.Formatter formatter = new();
            int measured = formatter.Measure(message);
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, message));

            Assert.AreEqual(
                measured,
                writer.Position,
                "Measure must predict Write exactly, or every enclosing message's length prefix lies."
            );
            CollectionAssert.AreEqual(
                new[] { "before-serialization", "after-serialization" },
                message.HookLog,
                "The before hook belongs to Measure and the after hook to Write, each once."
            );
        }

        [Test]
        public void DeserializationHooksRunInOrderAndRebuildIgnoredState()
        {
            HookedMessage source = new(7, "alpha");
            byte[] buffer = new byte[64];
            HookedMessage.Formatter formatter = new();
            int measured = formatter.Measure(source);
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, source));

            WProtoReader reader = new(buffer.AsSpan(0, measured));
            Assert.IsTrue(formatter.TryRead(ref reader, out HookedMessage restored));

            CollectionAssert.AreEqual(
                new[] { "before-deserialization", "after-deserialization" },
                restored.HookLog
            );
            Assert.AreEqual(7, restored.Health);
            Assert.AreEqual("alpha", restored.Label);
            Assert.AreEqual(
                "ALPHA/7",
                restored.DerivedLabel,
                "State excluded with WProtoIgnore is only ever restored by the after hook, so an "
                    + "unfired hook shows up here as a half-built object."
            );
        }

        [Test]
        public void AFailedReadDoesNotRunTheAfterDeserializationHook()
        {
            // A tag for field 1 with no varint body behind it.
            byte[] truncated = { 0x08 };
            WProtoReader reader = new(truncated);
            HookedMessage.Formatter formatter = new();

            Assert.IsFalse(formatter.TryRead(ref reader, out HookedMessage value));
            Assert.IsTrue(
                value == null,
                "A failed read must not hand back a partially populated object."
            );
            Assert.IsTrue(reader.Malformed);

            // Asserting only that the result is null is not enough: a formatter that runs the hook
            // and THEN returns null passes that. The abandoned instance is the only witness.
            Assert.IsTrue(formatter.LastAttempted != null);
            CollectionAssert.AreEqual(
                new[] { "before-deserialization" },
                formatter.LastAttempted.HookLog,
                "After-deserialization must not run on a failed read -- rebuilding derived state "
                    + "from half-written members yields a plausible wrong object."
            );
            Assert.IsTrue(formatter.LastAttempted.DerivedLabel == null);
        }

        [Test]
        public void AFieldSentWithTheWrongWireTypeIsNotDecodedAsTheDeclaredType()
        {
            // Field 1 is declared int32 (varint); this sends it as a Fixed32.
            byte[] payload = { 0x0D, 0x05, 0x08, 0x07, 0x00 };
            WProtoReader reader = new(payload);
            HookedMessage.Formatter formatter = new();

            Assert.IsTrue(formatter.TryRead(ref reader, out HookedMessage value));
            Assert.AreEqual(
                0,
                value.Health,
                "A mismatched wire type must be skipped like any unknown field, never read as the "
                    + "declared type -- otherwise a wrong-typed payload decodes to a plausible value."
            );
        }

        [Test]
        public void ExplicitMemberNamesAreCarriedOnTheContractRatherThanTheWire()
        {
            FieldInfo field = typeof(HookedMessage).GetField(
                HookedMessage.HealthFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.IsTrue(field != null);
            WProtoMemberAttribute health = (WProtoMemberAttribute)
                Attribute.GetCustomAttribute(field, typeof(WProtoMemberAttribute));

            Assert.IsTrue(health != null);
            Assert.AreEqual(1, health.Tag);
            Assert.AreEqual(
                "health",
                health.Name,
                "Name is what keeps a schema stable when the C# member is renamed."
            );

            WProtoContractAttribute contract = (WProtoContractAttribute)
                Attribute.GetCustomAttribute(
                    typeof(HookedMessage),
                    typeof(WProtoContractAttribute)
                );
            Assert.IsTrue(contract != null);
            Assert.AreEqual("player_state", contract.Name);
        }

        /// <summary>
        /// A consumer-shaped contract: private state, private hooks, and a nested formatter.
        /// </summary>
        /// <remarks>
        /// <c>partial</c> because <c>[WProtoContract]</c> means "generate a formatter for me", and
        /// the generator emits it nested so it can reach these private members. The hand-written
        /// <see cref="Formatter"/> below stays: it exposes <c>LastAttempted</c> so a test can inspect
        /// the instance an abandoned read built, which generated code has no reason to surface. The
        /// generated formatter is the one registered with <c>WProtoFormatterProvider</c>; nothing
        /// here resolves through it, and every test names the formatter it means.
        /// </remarks>
        [WProtoContract(Name = "player_state")]
        internal sealed partial class HookedMessage
        {
            /// <summary>The private member's name, so the test below carries no magic string.</summary>
            internal const string HealthFieldName = nameof(_health);

            [WProtoMember(1, Name = "health")]
            private int _health;

            [WProtoMember(2, Name = "label")]
            private string _label;

            [WProtoIgnore]
            private string _derivedLabel;

            private readonly List<string> _hookLog = new();

            internal HookedMessage() { }

            internal HookedMessage(int health, string label)
            {
                _health = health;
                _label = label;
            }

            internal int Health => _health;

            internal string Label => _label;

            internal string DerivedLabel => _derivedLabel;

            internal IReadOnlyList<string> HookLog => _hookLog;

            [WProtoBeforeSerialization]
            private void OnBeforeSerialization()
            {
                _hookLog.Add("before-serialization");
            }

            [WProtoAfterSerialization]
            private void OnAfterSerialization()
            {
                _hookLog.Add("after-serialization");
            }

            [WProtoBeforeDeserialization]
            private void OnBeforeDeserialization()
            {
                _hookLog.Add("before-deserialization");
                _derivedLabel = null;
            }

            [WProtoAfterDeserialization]
            private void OnAfterDeserialization()
            {
                _hookLog.Add("after-deserialization");
                _derivedLabel = $"{_label?.ToUpperInvariant()}/{_health}";
            }

            /// <summary>
            /// Hand-written stand-in for generated code, nested so it can reach private members.
            /// </summary>
            internal sealed class Formatter : IWProtoFormatter<HookedMessage>
            {
                /// <summary>
                /// The instance the last <see cref="TryRead"/> built, kept so a test can inspect an
                /// abandoned read. Generated formatters have no reason to expose this.
                /// </summary>
                internal HookedMessage LastAttempted { get; private set; }

                public int Measure(in HookedMessage value)
                {
                    if (value == null)
                    {
                        return 0;
                    }

                    value.OnBeforeSerialization();
                    return WProtoSizes.TagSize(1)
                        + WProtoSizes.Int32Size(value._health)
                        + WProtoSizes.TagSize(2)
                        + WProtoSizes.StringSize(value._label);
                }

                public bool Write(ref WProtoWriter writer, in HookedMessage value)
                {
                    if (value == null)
                    {
                        return true;
                    }

                    bool written =
                        writer.TryWriteTag(1, WProtoWireType.Varint)
                        && writer.TryWriteInt32(value._health)
                        && writer.TryWriteTag(2, WProtoWireType.LengthDelimited)
                        && writer.TryWriteString(value._label);

                    if (!written)
                    {
                        return false;
                    }

                    value.OnAfterSerialization();
                    return true;
                }

                public bool TryRead(ref WProtoReader reader, out HookedMessage value)
                {
                    HookedMessage result = new();
                    LastAttempted = result;
                    result.OnBeforeDeserialization();

                    while (reader.TryReadTag(out int fieldNumber, out int wireType))
                    {
                        // A field's wire type is checked before its number is trusted. Without this
                        // a payload that sends an int32 as a Fixed32 decodes to a plausible value
                        // instead of being stepped over like any other field this build cannot read.
                        switch (fieldNumber)
                        {
                            case 1 when wireType == WProtoWireType.Varint:
                            {
                                if (!reader.TryReadInt32(out result._health))
                                {
                                    value = null;
                                    return false;
                                }

                                break;
                            }
                            case 2 when wireType == WProtoWireType.LengthDelimited:
                            {
                                if (!reader.TryReadString(out result._label))
                                {
                                    value = null;
                                    return false;
                                }

                                break;
                            }
                            default:
                            {
                                if (!reader.TrySkipField(fieldNumber, wireType))
                                {
                                    value = null;
                                    return false;
                                }

                                break;
                            }
                        }
                    }

                    if (reader.Malformed)
                    {
                        value = null;
                        return false;
                    }

                    result.OnAfterDeserialization();
                    value = result;
                    return true;
                }
            }
        }
    }
}
