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
        /*
            JSON converter factories construct generic closures reflectively, which IL2CPP may not generate.
            JSON equivalence is therefore outside this fixture contract.
        */

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

            // The framework tuple is a negative control: Unity drops it.
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
            Assert.AreEqual(
                new SerializableValueTuple<int, float>(4, 0.5f),
                restored.loot.ValueFor("boss")
            );
        }

        [Test]
        public void ProtoBytesAreIdenticalToTheFrameworkTuple()
        {
            /*
                Raw ValueTuple serialization requires an AOT formatter in IL2CPP; an editor-only pass would miss
                a broken root-marshal registration.
            */
            byte[] theirs = Serializer.ProtoSerialize((7, 1.5f));
            byte[] mine = Serializer.ProtoSerialize(
                new SerializableValueTuple<int, float>(7, 1.5f)
            );

            CollectionAssert.AreEqual(theirs, mine);

            /*
                Literal protobuf-net bytes prevent two mutually compatible but incorrect serializers from
                passing together.
            */
            Assert.AreEqual("0807150000C03F", ToHex(mine));

            Assert.AreEqual(
                new SerializableValueTuple<int, float>(7, 1.5f),
                Serializer.ProtoDeserialize<SerializableValueTuple<int, float>>(theirs)
            );
            Assert.AreEqual((7, 1.5f), Serializer.ProtoDeserialize<ValueTuple<int, float>>(mine));

            Assert.AreEqual(
                "0803150000803E1A0161",
                ToHex(Serializer.ProtoSerialize((3, 0.25f, "a")))
            );

            /*
                The framework tuple closure appears only as a literal; TypeSyntax-only discovery previously
                missed its AOT formatter.
            */
            Assert.AreEqual(
                ToHex(Serializer.ProtoSerialize(new SerializableValueTuple<short, ulong>(1, 2))),
                ToHex(Serializer.ProtoSerialize(((short)1, (ulong)2)))
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
        public void EqualsObjectAcceptsOnlyAnotherSerializableValueTuple()
        {
            SerializableValueTuple<int, float> pair = new(7, 1.5f);
            SerializableValueTuple<int, float, string> triple = new(3, 0.25f, "a");

            Assert.IsTrue(pair.Equals((object)new SerializableValueTuple<int, float>(7, 1.5f)));
            Assert.IsTrue(
                triple.Equals((object)new SerializableValueTuple<int, float, string>(3, 0.25f, "a"))
            );

            /*
                Framework tuples reject the boxed wrapper and hash differently; cross-type comparison belongs to
                strongly typed Equals.
            */
            Assert.IsFalse(pair.Equals((object)(7, 1.5f)));
            Assert.IsFalse(triple.Equals((object)(3, 0.25f, "a")));
            Assert.IsFalse(pair.Equals((object)null));
        }

        [Test]
        public void EqualityAndHashingAgreeIncludingOnNullComponents()
        {
            // A dropped Unity field leaves default tuple components; null reference components must remain safe.
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
