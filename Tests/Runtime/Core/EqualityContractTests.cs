// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Utils;
    using WallstopStudios.UnityHelpers.Visuals;
    using Attribute = WallstopStudios.UnityHelpers.Tags.Attribute;

    /// <summary>
    /// The equality laws every value type in this package owes its callers, written once and driven
    /// from a table. A dictionary and a set are entitled to assume all of them; a type that breaks
    /// one loses entries rather than reporting an error, which is why this is a shared fixture
    /// rather than an assertion each type's own tests remember to make.
    /// </summary>
    /// <remarks>
    /// Deliberately absent: any claim that values which differ must hash differently. Collisions are
    /// legal, and asserting otherwise pins an implementation detail rather than a contract.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EqualityContractTests
    {
        private static readonly Guid SampleGuid = new("6f9619ff-8b86-4d11-b42d-00c04fc964ff");
        private static readonly Guid OtherGuid = new("2f9619ff-8b86-4d11-b42d-00c04fc964ff");

        [TestCaseSource(nameof(Cases))]
        public void EveryTypeObeysTheEqualityLaws(EqualityContractCase equalityCase)
        {
            equalityCase.AssertLaws();
        }

        [Test]
        public void TheTableCoversEveryTypeThisFixtureIsResponsibleFor()
        {
            List<string> labels = new();
            foreach (EqualityContractCase equalityCase in Cases())
            {
                labels.Add(equalityCase.Label);
            }

            /*
                A table that quietly loses a row reads exactly like a clean run, so what it covers is
                asserted alongside what it proves.
            */
            string[] expected =
            {
                nameof(Circle),
                nameof(Sphere),
                nameof(Line2D),
                nameof(Line3D),
                nameof(PoolFrequencyStatistics),
                nameof(PoolStatistics),
                "PoolStatisticsNotANumberRates",
                nameof(SplitMix64),
                nameof(AnimatedSpriteLayer),
                "AnimatedSpriteLayerZeroInitialized",
                nameof(ImmutableBitSet),
                "ImmutableBitSetZeroInitialized",
                nameof(FastVector2Int),
                nameof(FastVector3Int),
                nameof(WGuid),
                nameof(SerializableType),
                "SerializableTypeEmpty",
                "SerializableNullableWithValue",
                "SerializableNullableWithoutValue",
                "SerializableValueTuplePair",
                "SerializableValueTupleTriple",
                nameof(RandomState),
                nameof(Attribute),
                "EffectStackKeyCustom",
                "EffectStackKeyNone",
            };
            foreach (string label in expected)
            {
                CollectionAssert.Contains(labels, label);
            }
        }

        private static IEnumerable<EqualityContractCase> Cases()
        {
            yield return new EqualityContractCase<Circle>(
                nameof(Circle),
                new Circle(new Vector2(5f, 10f), 3f),
                new Circle(new Vector2(5f, 10f), 3f),
                new Circle(new Vector2(5f, 10f), 3f),
                // A radius Mathf.Approximately called equal while Objects.HashCode did not.
                new Circle(new Vector2(5f, 10f), 3f + 1e-6f),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            yield return new EqualityContractCase<Sphere>(
                nameof(Sphere),
                new Sphere(new Vector3(5f, 10f, 15f), 3f),
                new Sphere(new Vector3(5f, 10f, 15f), 3f),
                new Sphere(new Vector3(5f, 10f, 15f), 3f),
                new Sphere(new Vector3(5f, 10f, 15f), 3f + 1e-6f),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            yield return new EqualityContractCase<Line2D>(
                nameof(Line2D),
                new Line2D(Vector2.zero, Vector2.one),
                new Line2D(Vector2.zero, Vector2.one),
                new Line2D(Vector2.zero, Vector2.one),
                // Unity's Vector2 == calls this endpoint equal to the origin; its hash does not.
                new Line2D(new Vector2(1e-6f, 0f), Vector2.one),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            yield return new EqualityContractCase<Line3D>(
                nameof(Line3D),
                new Line3D(Vector3.zero, Vector3.one),
                new Line3D(Vector3.zero, Vector3.one),
                new Line3D(Vector3.zero, Vector3.one),
                new Line3D(new Vector3(1e-6f, 0f, 0f), Vector3.one),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            yield return new EqualityContractCase<PoolFrequencyStatistics>(
                nameof(PoolFrequencyStatistics),
                SampleFrequencyStatistics(10f),
                SampleFrequencyStatistics(10f),
                SampleFrequencyStatistics(10f),
                // Inside the old FloatEqualityTolerance, so it used to compare equal and hash apart.
                SampleFrequencyStatistics(10.00005f),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            yield return new EqualityContractCase<PoolStatistics>(
                nameof(PoolStatistics),
                SampleStatistics(10f),
                SampleStatistics(10f),
                SampleStatistics(10f),
                // Inside the old FloatEqualityTolerance, so it used to compare equal and hash apart.
                SampleStatistics(10.0005f),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            /*
                Math.Abs(NaN - NaN) < tolerance is false, so this snapshot used to answer false for
                itself: a set could not find what it had just added and a dictionary threw on the
                key it had just stored.
            */
            yield return new EqualityContractCase<PoolStatistics>(
                "PoolStatisticsNotANumberRates",
                SampleStatistics(float.NaN),
                SampleStatistics(float.NaN),
                SampleStatistics(float.NaN),
                SampleStatistics(10f),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            /*
                A generator hashes its state, so Equals(object) has to read that state too;
                inheriting Object's reference equality loses a copy the moment it is boxed.
            */
            yield return new EqualityContractCase<SplitMix64>(
                nameof(SplitMix64),
                new SplitMix64(123UL),
                new SplitMix64(123UL),
                new SplitMix64(123UL),
                new SplitMix64(456UL)
            );

            yield return new EqualityContractCase<AnimatedSpriteLayer>(
                nameof(AnimatedSpriteLayer),
                new AnimatedSpriteLayer(new Sprite[] { null, null }),
                new AnimatedSpriteLayer(new Sprite[] { null, null }),
                new AnimatedSpriteLayer(new Sprite[] { null, null }),
                new AnimatedSpriteLayer(new Sprite[] { null }),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            /*
                Only the constructor fills the frame array, so every element of a freshly allocated
                AnimatedSpriteLayer[] is this value and there is exactly one of it. Equals used to
                read Length straight off that null array.
            */
            yield return new EqualityContractCase<AnimatedSpriteLayer>(
                "AnimatedSpriteLayerZeroInitialized",
                default,
                default,
                default,
                new AnimatedSpriteLayer(new Sprite[] { null }),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            yield return new EqualityContractCase<ImmutableBitSet>(
                nameof(ImmutableBitSet),
                SampleBitSet(0, 5),
                SampleBitSet(0, 5),
                SampleBitSet(0, 5),
                SampleBitSet(0, 6),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            /*
                The zero-initialized set is the only one carrying a null word array; the constructor
                normalizes, so its own serialized round trip arrives with an empty one. The two used
                to compare unequal while hashing identically and reporting the same capacity, count
                and bits.
            */
            yield return new EqualityContractCase<ImmutableBitSet>(
                "ImmutableBitSetZeroInitialized",
                default,
                new ImmutableBitSet(Array.Empty<ulong>(), 0),
                new ImmutableBitSet(null, 0),
                SampleBitSet(0, 5),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            yield return new EqualityContractCase<FastVector2Int>(
                nameof(FastVector2Int),
                new FastVector2Int(3, 5),
                new FastVector2Int(3, 5),
                new FastVector2Int(3, 5),
                new FastVector2Int(3, 6),
                new object[]
                {
                    new Vector2Int(3, 5),
                    new FastVector3Int(3, 5, 0),
                    new Vector3Int(3, 5, 0),
                },
                (left, right) => left == right,
                (left, right) => left != right
            );

            yield return new EqualityContractCase<FastVector3Int>(
                nameof(FastVector3Int),
                new FastVector3Int(3, 5, 7),
                new FastVector3Int(3, 5, 7),
                new FastVector3Int(3, 5, 7),
                new FastVector3Int(3, 5, 8),
                new object[]
                {
                    new Vector3Int(3, 5, 7),
                    new FastVector2Int(3, 5),
                    new Vector2Int(3, 5),
                },
                (left, right) => left == right,
                (left, right) => left != right
            );

            yield return new EqualityContractCase<WGuid>(
                nameof(WGuid),
                new WGuid(SampleGuid),
                new WGuid(SampleGuid),
                new WGuid(SampleGuid),
                new WGuid(OtherGuid),
                new object[] { SampleGuid },
                (left, right) => left == right,
                (left, right) => left != right
            );

            yield return new EqualityContractCase<SerializableType>(
                nameof(SerializableType),
                new SerializableType(typeof(int)),
                new SerializableType(typeof(int)),
                new SerializableType(typeof(int)),
                new SerializableType(typeof(float)),
                new object[] { typeof(int) },
                (left, right) => left == right,
                (left, right) => left != right
            );

            /*
                Three construction paths that all arrive at the empty type: zero-initialized,
                constructed from a null Type, and set back to null after holding one. The operators
                are checked only against values of this type; SerializableType's == deliberately
                answers true for a null operand as its documented "is empty" idiom, which
                Equals(null) does not and must not agree with.
            */
            yield return new EqualityContractCase<SerializableType>(
                "SerializableTypeEmpty",
                default,
                new SerializableType(null),
                ClearedSerializableType(),
                new SerializableType(typeof(int)),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            yield return new EqualityContractCase<SerializableNullable<int>>(
                "SerializableNullableWithValue",
                new SerializableNullable<int>(5),
                new SerializableNullable<int>(5),
                new SerializableNullable<int>(5),
                new SerializableNullable<int>(7),
                new object[] { 5 }
            );

            yield return new EqualityContractCase<SerializableNullable<int>>(
                "SerializableNullableWithoutValue",
                default,
                new SerializableNullable<int>((int?)null),
                ClearedSerializableNullable(),
                new SerializableNullable<int>(0)
            );

            yield return new EqualityContractCase<SerializableValueTuple<int, float>>(
                "SerializableValueTuplePair",
                new SerializableValueTuple<int, float>(7, 1.5f),
                new SerializableValueTuple<int, float>(7, 1.5f),
                new SerializableValueTuple<int, float>(7, 1.5f),
                new SerializableValueTuple<int, float>(7, 2.5f),
                new object[] { (7, 1.5f) },
                (left, right) => left == right,
                (left, right) => left != right
            );

            yield return new EqualityContractCase<SerializableValueTuple<int, float, string>>(
                "SerializableValueTupleTriple",
                new SerializableValueTuple<int, float, string>(7, 1.5f, "loot"),
                new SerializableValueTuple<int, float, string>(7, 1.5f, "loot"),
                new SerializableValueTuple<int, float, string>(7, 1.5f, "loot"),
                new SerializableValueTuple<int, float, string>(7, 1.5f, "scrap"),
                new object[] { (7, 1.5f, "loot") },
                (left, right) => left == right,
                (left, right) => left != right
            );

            yield return new EqualityContractCase<RandomState>(
                nameof(RandomState),
                /*
                    default(RandomState) is the value new RandomState(0) describes, and used to
                    carry a stored zero hash while the constructed one computed a mix.
                */
                default,
                new RandomState(0),
                new RandomState(0),
                new RandomState(1)
            );

            yield return new EqualityContractCase<Attribute>(
                nameof(Attribute),
                new Attribute(5f),
                new Attribute(5f),
                new Attribute(5f),
                new Attribute(6f),
                new object[] { 5f, 5.0, 5 }
            );

            yield return new EqualityContractCase<EffectStackKey>(
                "EffectStackKeyCustom",
                EffectStackKey.CreateCustom("DamageOverTime"),
                EffectStackKey.CreateCustom("DamageOverTime"),
                EffectStackKey.CreateCustom("DamageOverTime"),
                EffectStackKey.CreateCustom("Burning"),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );

            /*
                All three equal values have to be default: EffectStackGroup.None carries no payload
                and no factory produces it, so a zero-initialized key is the only instance of this
                arm there is. The row exists because that arm used to answer false for itself while
                its hash stayed put, which is a dictionary that collects keys it can never resolve.
            */
            yield return new EqualityContractCase<EffectStackKey>(
                "EffectStackKeyNone",
                default,
                default,
                default,
                EffectStackKey.CreateCustom("DamageOverTime"),
                equalityOperator: (left, right) => left == right,
                inequalityOperator: (left, right) => left != right
            );
        }

        private static SerializableType ClearedSerializableType()
        {
            SerializableType cleared = new(typeof(int));
            cleared.SetType(null);
            return cleared;
        }

        private static SerializableNullable<int> ClearedSerializableNullable()
        {
            SerializableNullable<int> cleared = new(5);
            cleared.Clear();
            return cleared;
        }

        private static ImmutableBitSet SampleBitSet(params int[] setBits)
        {
            BitSet bits = new(64);
            foreach (int index in setBits)
            {
                _ = bits.TrySet(index);
            }

            return bits.ToImmutable();
        }

        private static PoolStatistics SampleStatistics(float rentalsPerMinute)
        {
            return new PoolStatistics(
                currentSize: 4,
                peakSize: 9,
                rentCount: 100,
                returnCount: 98,
                purgeCount: 2,
                idleTimeoutPurges: 1,
                capacityPurges: 1,
                rentalsPerMinute: rentalsPerMinute
            );
        }

        private static PoolFrequencyStatistics SampleFrequencyStatistics(float rentalsPerMinute)
        {
            return new PoolFrequencyStatistics(
                rentalsPerMinute,
                averageInterRentalTimeSeconds: 1f,
                lastAccessTime: 100f,
                totalRentalCount: 50,
                isHighFrequency: true,
                isLowFrequency: false,
                isUnused: false
            );
        }
    }
}
