// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    /// <summary>
    /// Pins that the stand-in is serialized where <see cref="ValueTuple{T1, T2}"/> is dropped, and
    /// that it is interchangeable with it everywhere else.
    /// </summary>
    /// <remarks>
    /// Measured before this type existed: Unity produces <b>no</b> <c>SerializedProperty</c> for a
    /// framework tuple field -- not an empty one, not an error -- so a tuple in a serialized
    /// collection loses its authored contents in silence. That asymmetry is what the first test
    /// asserts, because a test that only checked the stand-in would pass just as well if Unity had
    /// supported tuples all along.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializableValueTupleTests : CommonTestBase
    {
        [Test]
        public void UnitySerializesTheStandInAndNotTheFrameworkTuple()
        {
            SerializableValueTupleAsset asset = CreateAsset();
            asset.pair = (7, 1.5f);
            asset.frameworkPair = (7, 1.5f);

            string json = JsonUtility.ToJson(asset);

            Assert.IsTrue(json.Contains(nameof(SerializableValueTupleAsset.pair)), json);
            Assert.IsTrue(json.Contains(nameof(SerializableValueTupleAsset.triple)), json);
            Assert.IsTrue(json.Contains(nameof(SerializableValueTupleAsset.pairs)), json);

            // The comparison that makes the assertions above mean something: Unity drops this one.
            Assert.IsFalse(json.Contains(nameof(SerializableValueTupleAsset.frameworkPair)), json);
        }

        [Test]
        public void UnityRoundTripsEveryDeclaredShape()
        {
            SerializableValueTupleAsset asset = CreateAsset();
            asset.pair = (7, 1.5f);
            asset.triple = (3, 0.25f, "a");
            asset.pairs.Add((1, 2f));
            asset.loot["boss"] = (4, 0.5f);

            SerializableValueTupleAsset restored = CreateAsset();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(asset), restored);

            Assert.AreEqual(asset.pair, restored.pair);
            Assert.AreEqual(asset.triple, restored.triple);
            CollectionAssert.AreEqual(asset.pairs, restored.pairs);
            Assert.AreEqual(1, restored.loot.Count);
            Assert.AreEqual(new SerializableValueTuple<int, float>(4, 0.5f), restored.loot["boss"]);
        }

        [Test]
        public void ProtoBytesAreIdenticalToTheFrameworkTuple()
        {
            // The framework tuple is serialized here ON PURPOSE, and it is the assertion that
            // matters most on the gated IL2CPP leg. protobuf-net reaches a raw ValueTuple through
            // StructValueChecker<ValueTuple<int, float>>, instantiated reflectively, so this line
            // threw ExecutionEngineException on a 2021.3 standalone player until
            // [assembly: WProtoRootMarshal(typeof(ValueTuple<,>), ...)] gave it an AOT formatter.
            // If that registration regresses, this fails in a player and passes in the editor --
            // which is exactly the trap the marshal exists to close.
            byte[] theirs = Serializer.ProtoSerialize((7, 1.5f));
            byte[] mine = Serializer.ProtoSerialize(
                new SerializableValueTuple<int, float>(7, 1.5f)
            );

            CollectionAssert.AreEqual(theirs, mine);

            // Pinned against a literal as well, so the pair agreeing with each other cannot pass by
            // both being wrong. The bytes came from protobuf-net, and PackageContractShapeTests
            // keeps them honest against 2.4.9 and 3.2.56 on every generator build.
            Assert.AreEqual("0807150000C03F", ToHex(mine));

            // Each reads what the other wrote.
            Assert.AreEqual(
                new SerializableValueTuple<int, float>(7, 1.5f),
                Serializer.ProtoDeserialize<SerializableValueTuple<int, float>>(theirs)
            );
            Assert.AreEqual((7, 1.5f), Serializer.ProtoDeserialize<ValueTuple<int, float>>(mine));

            // The three-component form travels the same road.
            Assert.AreEqual(
                "0803150000803E1A0161",
                ToHex(Serializer.ProtoSerialize((3, 0.25f, "a")))
            );
        }

        [Test]
        public void JsonIsIdenticalToTheFrameworkTuple()
        {
            // Constants, unlike the protobuf case above: a raw ValueTuple now has an AOT protobuf
            // formatter, but System.Text.Json still reaches ObjectDefaultConverter<ValueTuple<..>>,
            // which is instantiated reflectively and has no AOT code under IL2CPP. Serializing the
            // framework tuple here would therefore still fail in a player. These strings are what
            // JsonStringify produces for it on mono.
            Assert.AreEqual(
                "{\"Item1\":7,\"Item2\":1.5}",
                Serializer.JsonStringify(new SerializableValueTuple<int, float>(7, 1.5f))
            );

            Assert.AreEqual(
                "{\"Item1\":3,\"Item2\":0.25,\"Item3\":\"a\"}",
                Serializer.JsonStringify(
                    new SerializableValueTuple<int, float, string>(3, 0.25f, "a")
                )
            );
        }

        [Test]
        public void ConversionsAndDeconstructionMatchTheFrameworkTuple()
        {
            SerializableValueTuple<int, float> pair = (7, 1.5f);
            (int count, float weight) = pair;

            Assert.AreEqual(7, count);
            Assert.AreEqual(1.5f, weight);

            ValueTuple<int, float> back = pair;
            Assert.AreEqual((7, 1.5f), back);
            Assert.IsTrue(pair.Equals((7, 1.5f)));
            Assert.IsTrue(pair == new SerializableValueTuple<int, float>(7, 1.5f));
            Assert.IsFalse(pair != new SerializableValueTuple<int, float>(7, 1.5f));

            SerializableValueTuple<int, float, string> triple = (3, 0.25f, "a");
            (int first, float second, string third) = triple;
            Assert.AreEqual(3, first);
            Assert.AreEqual(0.25f, second);
            Assert.AreEqual("a", third);
            Assert.AreEqual((3, 0.25f, "a"), (ValueTuple<int, float, string>)triple);
        }

        [Test]
        public void EqualityAndHashingAgreeIncludingOnNullComponents()
        {
            // A null component must not throw from Equals or GetHashCode -- a tuple holding a
            // reference type is the ordinary case, and `default` is the value a dropped Unity field
            // would leave behind.
            SerializableValueTuple<string, string> empty = default;
            SerializableValueTuple<string, string> alsoEmpty = new(null, null);

            Assert.IsTrue(empty.Equals(alsoEmpty));
            Assert.AreEqual(empty.GetHashCode(), alsoEmpty.GetHashCode());
            Assert.AreEqual("(, )", empty.ToString());

            SerializableValueTuple<int, float> pair = new(7, 1.5f);
            Assert.AreEqual(
                pair.GetHashCode(),
                new SerializableValueTuple<int, float>(7, 1.5f).GetHashCode()
            );
            Assert.AreNotEqual(
                pair.GetHashCode(),
                new SerializableValueTuple<int, float>(8, 1.5f).GetHashCode()
            );
            Assert.AreEqual("(7, 1.5)", pair.ToString());
        }

        private static string ToHex(byte[] bytes)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
            {
                builder.Append(value.ToString("X2"));
            }

            return builder.ToString();
        }

        private SerializableValueTupleAsset CreateAsset()
        {
            return Track(ScriptableObject.CreateInstance<SerializableValueTupleAsset>());
        }
    }
}
