// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Counts constructions, so a test can say whether one happened.</summary>
    public static class ConstructorWitness
    {
        /// <summary>How many author-written constructors have run since the last reset.</summary>
        public static int Constructions;
    }

    /// <summary>
    /// The base a skipped constructor still initializes, standing in for <c>AbstractRandom</c>'s
    /// scratch buffer.
    /// </summary>
    public abstract class WitnessBase
    {
        /// <summary>Allocated by a field initializer, which a generated constructor still runs.</summary>
        public byte[] Scratch = new byte[16];
    }

    /// <summary>
    /// The shape of a generator that must not be constructed on read: an expensive constructor that
    /// produces derived state, and a hook that rebuilds it only when it is missing.
    /// </summary>
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class SkippingContract : WitnessBase
    {
        /// <summary>The saved seed.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Seed;

        /// <summary>
        /// A repeated field whose initializer must not be treated as protobuf payload state when
        /// the field is present.
        /// </summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public int[] Values = { 99 };

        /// <summary>Proves a field initializer ran.</summary>
        public string FromInitializer = "initialized";

        /// <summary>State derived from the seed, never written to the wire.</summary>
        [ProtoIgnore]
        [WProtoIgnore]
        public string Derived;

        /// <summary>Builds an instance the way a caller would, deriving state up front.</summary>
        public SkippingContract()
        {
            ConstructorWitness.Constructions++;
            Seed = -1;
            Derived = "constructed";
        }

        [ProtoAfterDeserialization]
        [WProtoAfterDeserialization]
        private void Rebuild()
        {
            // Exactly DotNetRandom's shape: rebuilding is skipped when something already did it,
            // which is what turns a constructor call into a silently wrong generator.
            if (Derived != null)
            {
                return;
            }

            Derived = "rebuilt-from-" + Seed;
        }
    }

    /// <summary>
    /// The flag on a type that writes no constructor of its own, which must stay constructible.
    /// </summary>
    /// <remarks>
    /// Emitting a constructor here would remove the implicit parameterless one and break
    /// <c>new NoConstructorContract()</c> in the consumer's own source -- an attribute silently
    /// breaking unrelated code. There is also nothing to skip: the implicit constructor runs field
    /// initializers and nothing else.
    /// </remarks>
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class NoConstructorContract
    {
        /// <summary>The saved seed.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Seed;
    }

    /// <summary>The same shape without the flag, as the control.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class ConstructingContract : WitnessBase
    {
        /// <summary>The saved seed.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Seed;

        /// <summary>State derived from the seed, never written to the wire.</summary>
        [ProtoIgnore]
        [WProtoIgnore]
        public string Derived;

        /// <summary>Builds an instance the way a caller would, deriving state up front.</summary>
        public ConstructingContract()
        {
            ConstructorWitness.Constructions++;
            Seed = -1;
            Derived = "constructed";
        }

        [ProtoAfterDeserialization]
        [WProtoAfterDeserialization]
        private void Rebuild()
        {
            if (Derived != null)
            {
                return;
            }

            Derived = "rebuilt-from-" + Seed;
        }
    }
}
