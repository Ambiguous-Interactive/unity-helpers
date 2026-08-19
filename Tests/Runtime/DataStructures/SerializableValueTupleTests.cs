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
            // Same field names and numbers, so a payload written by either reads back through the
            // other. That is what lets an existing save migrate to the stand-in without a rewrite.
            byte[] mine = Serializer.ProtoSerialize(
                new SerializableValueTuple<int, float>(7, 1.5f)
            );
            byte[] theirs = Serializer.ProtoSerialize((7, 1.5f));

            CollectionAssert.AreEqual(theirs, mine);

            SerializableValueTuple<int, float> fromTheirs = Serializer.ProtoDeserialize<
                SerializableValueTuple<int, float>
            >(theirs);
            ValueTuple<int, float> fromMine = Serializer.ProtoDeserialize<ValueTuple<int, float>>(
                mine
            );

            Assert.AreEqual(new SerializableValueTuple<int, float>(7, 1.5f), fromTheirs);
            Assert.AreEqual((7, 1.5f), fromMine);
        }

        [Test]
        public void JsonIsIdenticalToTheFrameworkTuple()
        {
            Assert.AreEqual(
                Serializer.JsonStringify((7, 1.5f)),
                Serializer.JsonStringify(new SerializableValueTuple<int, float>(7, 1.5f))
            );

            Assert.AreEqual(
                Serializer.JsonStringify((3, 0.25f, "a")),
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

        private SerializableValueTupleAsset CreateAsset()
        {
            return Track(ScriptableObject.CreateInstance<SerializableValueTupleAsset>());
        }
    }
}
