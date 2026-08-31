// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    /// <summary>
    /// A dropdown matches an AUTHORED option against a SERIALIZED value, and those are allowed to be
    /// different-but-convertible types. Narrowing <c>Equals(object)</c> to the declaring type took
    /// that away and left a blank dropdown over a perfectly valid authored value, so every pair the
    /// drawer is expected to match is pinned here.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WValueDropDownConvertibleOptionTests : CommonTestBase
    {
        private static readonly Guid SampleGuid = new("6f9619ff-8b86-4d11-b42d-00c04fc964ff");
        private static readonly Guid OtherGuid = new("2f9619ff-8b86-4d11-b42d-00c04fc964ff");

        [Test]
        public void ResolveSelectedIndexMatchesAGuidOptionAgainstAWGuidField()
        {
            WValueDropDownConvertibleOptionAsset asset =
                CreateScriptableObject<WValueDropDownConvertibleOptionAsset>();
            asset.selectedGuid = new WGuid(WValueDropDownConvertibleOptionSource.SecondGuid);

            Assert.AreEqual(
                1,
                ResolveSelectedIndex(
                    asset,
                    nameof(WValueDropDownConvertibleOptionAsset.selectedGuid)
                ),
                "A WGuid field must select the Guid option it holds"
            );
        }

        [Test]
        public void ResolveSelectedIndexMatchesAnIntOptionAgainstASerializableNullableField()
        {
            WValueDropDownConvertibleOptionAsset asset =
                CreateScriptableObject<WValueDropDownConvertibleOptionAsset>();
            asset.selectedNullable = new SerializableNullable<int>(7);

            Assert.AreEqual(
                2,
                ResolveSelectedIndex(
                    asset,
                    nameof(WValueDropDownConvertibleOptionAsset.selectedNullable)
                ),
                "A SerializableNullable<int> field must select the int option it holds"
            );
        }

        [Test]
        public void ResolveSelectedIndexFindsNothingForAnEmptySerializableNullableField()
        {
            WValueDropDownConvertibleOptionAsset asset =
                CreateScriptableObject<WValueDropDownConvertibleOptionAsset>();
            asset.selectedNullable = default;

            Assert.AreEqual(
                -1,
                ResolveSelectedIndex(
                    asset,
                    nameof(WValueDropDownConvertibleOptionAsset.selectedNullable)
                ),
                "A field holding no value must select no option"
            );
        }

        [Test]
        public void ResolveSelectedIndexMatchesAValueTupleOptionAgainstASerializablePairField()
        {
            WValueDropDownConvertibleOptionAsset asset =
                CreateScriptableObject<WValueDropDownConvertibleOptionAsset>();
            asset.selectedPair = new SerializableValueTuple<int, float>(7, 1.5f);

            Assert.AreEqual(
                1,
                ResolveSelectedIndex(
                    asset,
                    nameof(WValueDropDownConvertibleOptionAsset.selectedPair)
                ),
                "A SerializableValueTuple<int, float> field must select the ValueTuple option it holds"
            );
        }

        [Test]
        public void ResolveSelectedIndexMatchesAValueTupleOptionAgainstASerializableTripleField()
        {
            WValueDropDownConvertibleOptionAsset asset =
                CreateScriptableObject<WValueDropDownConvertibleOptionAsset>();
            asset.selectedTriple = new SerializableValueTuple<int, float, string>(7, 1.5f, "loot");

            Assert.AreEqual(
                1,
                ResolveSelectedIndex(
                    asset,
                    nameof(WValueDropDownConvertibleOptionAsset.selectedTriple)
                ),
                "A SerializableValueTuple<int, float, string> field must select the ValueTuple option it holds"
            );
        }

        [Test]
        [TestCaseSource(nameof(ConvertiblePairs))]
        public void MatchesAuthoredOptionAcceptsAConvertibleOption(
            object serializedValue,
            object option
        )
        {
            Assert.IsTrue(
                WValueDropDownDrawer.TestHooks.MatchesAuthoredOption(serializedValue, option),
                $"{serializedValue.GetType().Name} holding {serializedValue} must match the authored "
                    + $"{option.GetType().Name} {option}"
            );
        }

        [Test]
        [TestCaseSource(nameof(NonConvertiblePairs))]
        public void MatchesAuthoredOptionRefusesAnOptionThatDenotesAnotherValue(
            object serializedValue,
            object option
        )
        {
            Assert.IsFalse(
                WValueDropDownDrawer.TestHooks.MatchesAuthoredOption(serializedValue, option),
                $"{serializedValue.GetType().Name} holding {serializedValue} must not match the "
                    + $"authored {option.GetType().Name} {option}"
            );
        }

        private static int ResolveSelectedIndex(
            WValueDropDownConvertibleOptionAsset asset,
            string propertyPath
        )
        {
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            Assert.IsTrue(property != null, $"Failed to locate the {propertyPath} property.");
            Assert.That(
                property.propertyType,
                Is.EqualTo(SerializedPropertyType.Generic),
                $"{propertyPath} must reach the drawer's generic path."
            );

            WValueDropDownAttribute attribute =
                PropertyDrawerTestHelper.GetAttributeFromProperty<WValueDropDownAttribute>(
                    property
                );
            Assert.IsTrue(attribute != null, $"Failed to read the {propertyPath} attribute.");

            object[] options = attribute.GetOptions(asset);
            Assert.IsTrue(options != null, $"{propertyPath} produced no options.");

            return WValueDropDownDrawer.TestHooks.ResolveSelectedIndex(
                property,
                attribute.ValueType,
                options
            );
        }

        private static IEnumerable<TestCaseData> ConvertiblePairs()
        {
            yield return new TestCaseData(new FastVector2Int(3, 5), new Vector2Int(3, 5)).SetName(
                "Convertible.FastVector2IntToVector2Int"
            );

            yield return new TestCaseData(
                new FastVector2Int(3, 5),
                new FastVector3Int(3, 5, 9)
            ).SetName("Convertible.FastVector2IntToFastVector3Int");

            yield return new TestCaseData(
                new FastVector2Int(3, 5),
                new Vector3Int(3, 5, 9)
            ).SetName("Convertible.FastVector2IntToVector3Int");

            yield return new TestCaseData(
                new FastVector3Int(3, 5, 9),
                new Vector3Int(3, 5, 9)
            ).SetName("Convertible.FastVector3IntToVector3Int");

            yield return new TestCaseData(
                new FastVector3Int(3, 5, 9),
                new Vector2Int(3, 5)
            ).SetName("Convertible.FastVector3IntToVector2Int");

            yield return new TestCaseData(
                new FastVector3Int(3, 5, 9),
                new FastVector2Int(3, 5)
            ).SetName("Convertible.FastVector3IntToFastVector2Int");

            yield return new TestCaseData(new Vector2Int(3, 5), new FastVector2Int(3, 5)).SetName(
                "Convertible.Vector2IntToFastVector2Int"
            );

            yield return new TestCaseData(
                new Vector3Int(3, 5, 9),
                new FastVector3Int(3, 5, 9)
            ).SetName("Convertible.Vector3IntToFastVector3Int");

            yield return new TestCaseData(new WGuid(SampleGuid), SampleGuid).SetName(
                "Convertible.WGuidToGuid"
            );

            yield return new TestCaseData(new SerializableNullable<int>(5), 5).SetName(
                "Convertible.SerializableNullableToValue"
            );

            yield return new TestCaseData(
                new SerializableValueTuple<int, float>(7, 1.5f),
                (7, 1.5f)
            ).SetName("Convertible.SerializablePairToValueTuple");

            yield return new TestCaseData(
                new SerializableValueTuple<int, float, string>(7, 1.5f, "loot"),
                (7, 1.5f, "loot")
            ).SetName("Convertible.SerializableTripleToValueTuple");
        }

        private static IEnumerable<TestCaseData> NonConvertiblePairs()
        {
            yield return new TestCaseData(new FastVector2Int(3, 5), new Vector2Int(3, 6)).SetName(
                "NonConvertible.FastVector2IntToDifferentVector2Int"
            );

            yield return new TestCaseData(
                new FastVector2Int(3, 5),
                new Vector3Int(3, 6, 0)
            ).SetName("NonConvertible.FastVector2IntToDifferentVector3Int");

            yield return new TestCaseData(
                new FastVector3Int(3, 5, 9),
                new Vector3Int(3, 5, 8)
            ).SetName("NonConvertible.FastVector3IntToDifferentVector3Int");

            yield return new TestCaseData(new WGuid(SampleGuid), OtherGuid).SetName(
                "NonConvertible.WGuidToDifferentGuid"
            );

            yield return new TestCaseData(default(SerializableNullable<int>), 5).SetName(
                "NonConvertible.EmptySerializableNullableToValue"
            );

            yield return new TestCaseData(
                new SerializableValueTuple<int, float>(7, 1.5f),
                (7, 2.5f)
            ).SetName("NonConvertible.SerializablePairToDifferentValueTuple");

            yield return new TestCaseData(new FastVector2Int(3, 5), "not a vector").SetName(
                "NonConvertible.FastVector2IntToString"
            );
        }
    }
#endif
}
