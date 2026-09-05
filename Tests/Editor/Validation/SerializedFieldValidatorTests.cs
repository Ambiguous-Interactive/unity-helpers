// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    /// <summary>
    /// Pins which fields Unity drops, measured against a live editor rather than a rule table.
    /// </summary>
    /// <remarks>
    /// The fixture is a comparison on purpose. A check that only asserts the failing fields are
    /// reported would pass just as well if it reported every field, and a validator that fires on a
    /// correct declaration is a nuisance developers turn off rather than a safety net.
    /// </remarks>
    [TestFixture]
    public sealed class SerializedFieldValidatorTests
    {
        [Test]
        public void EveryFrameworkGenericAskedForIsReported()
        {
            List<DroppedSerializedField> findings = new();
            Assert.IsTrue(
                SerializedFieldValidator.TryValidate(typeof(DroppedSerializedFieldAsset), findings)
            );

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "lookup",
                    "tags",
                    "optionalCount",
                    "frameworkPair",
                    "_ordered",
                    "nestedLookup",
                    "nestedLookup",
                    "nestedLookup",
                },
                findings.Select(finding => finding.FieldName).ToArray(),
                string.Join(", ", findings.Select(finding => finding.ToString()))
            );
        }

        [Test]
        public void AFieldThatSurvivesIsNotReported()
        {
            List<DroppedSerializedField> findings = new();
            SerializedFieldValidator.TryValidate(typeof(DroppedSerializedFieldAsset), findings);
            string[] reported = findings.Select(finding => finding.FieldName).ToArray();

            CollectionAssert.DoesNotContain(reported, "count");
            CollectionAssert.DoesNotContain(reported, "serializedLookup");
            CollectionAssert.DoesNotContain(reported, "path");

            CollectionAssert.DoesNotContain(reported, "runtimeCache");
            CollectionAssert.DoesNotContain(reported, "_privateCache");
        }

        /// <summary>
        /// The parent field produces a property whatever the nested type holds, so a check that
        /// asks only about the asset's own fields reports nothing here -- and a serializable struct
        /// or class holding authored data is an ordinary Unity layout, not a corner.
        /// </summary>
        /// <remarks>
        /// The collection spellings matter separately: the fields of an element exist only under
        /// Array.data[0], so an empty list has nothing to ask about until one is materialized.
        /// </remarks>
        [Test]
        public void AFieldOnANestedSerializableTypeIsReachedToo()
        {
            List<DroppedSerializedField> findings = new();
            SerializedFieldValidator.TryValidate(typeof(DroppedSerializedFieldAsset), findings);

            List<DroppedSerializedField> nested = findings
                .Where(finding => finding.FieldName == "nestedLookup")
                .ToList();

            Assert.AreEqual(3, nested.Count, "direct, List<T> and T[] should each be reached");
            foreach (DroppedSerializedField finding in nested)
            {
                Assert.AreEqual(
                    typeof(DroppedSerializedFieldAsset.NestedBlock),
                    finding.Owner,
                    "the finding should name the type that declares the field"
                );
                Assert.AreEqual("SerializableDictionary<string, int>", finding.StandIn);
            }

            string[] reported = findings.Select(finding => finding.FieldName).ToArray();

            CollectionAssert.DoesNotContain(reported, "nestedCount");

            // A fresh SerializeReference has no children until an instance is assigned.
            CollectionAssert.DoesNotContain(reported, "payloadLookup");
            CollectionAssert.DoesNotContain(reported, "payload");
        }

        [Test]
        public void TheReportNamesTheStandInToUse()
        {
            List<DroppedSerializedField> findings = new();
            SerializedFieldValidator.TryValidate(typeof(DroppedSerializedFieldAsset), findings);

            Assert.AreEqual(
                "SerializableDictionary<string, int>",
                Reported(findings, "lookup").StandIn
            );
            Assert.AreEqual("SerializableHashSet<string>", Reported(findings, "tags").StandIn);
            Assert.AreEqual(
                "SerializableNullable<int>",
                Reported(findings, "optionalCount").StandIn
            );
            Assert.AreEqual(
                "SerializableValueTuple<int, float>",
                Reported(findings, "frameworkPair").StandIn
            );
            Assert.AreEqual(
                "SerializableSortedDictionary<string, int>",
                Reported(findings, "_ordered").StandIn
            );

            StringAssert.Contains(
                "SerializableDictionary<string, int>",
                Reported(findings, "lookup").ToString()
            );
        }

        /// <summary>
        /// The pair #289 shipped, checked from the outside: the same asset carries both, and only
        /// one of them survives. Whatever the two types have in common -- both are
        /// <c>[Serializable]</c>, both are structs of two values -- it is not what decides this.
        /// </summary>
        [Test]
        public void TheTupleStandInIsSerializedWhereTheFrameworkTupleIsNot()
        {
            List<DroppedSerializedField> findings = new();
            Assert.IsTrue(
                SerializedFieldValidator.TryValidate(typeof(SerializableValueTupleAsset), findings)
            );

            CollectionAssert.AreEqual(
                new[] { "frameworkPair" },
                findings.Select(finding => finding.FieldName).ToArray()
            );
        }

        /// <summary>
        /// A project scan reaches every type in every loaded assembly, so anything that refuses to
        /// be inspected has to be a skipped entry rather than the end of the scan.
        /// </summary>
        [Test]
        public void ATypeThatCannotBeConstructedIsDeclinedRatherThanThrown()
        {
            List<DroppedSerializedField> findings = new();

            Assert.IsFalse(SerializedFieldValidator.TryValidate(null, findings));
            Assert.IsFalse(SerializedFieldValidator.TryValidate(typeof(string), findings));
            Assert.IsFalse(SerializedFieldValidator.TryValidate(typeof(MonoBehaviour), null));

            Assert.IsFalse(SerializedFieldValidator.IsInspectable(typeof(List<>)));
            Assert.IsTrue(
                SerializedFieldValidator.IsInspectable(typeof(DroppedSerializedFieldAsset))
            );
        }

        /// <summary>
        /// Unity drops <c>List&lt;Dictionary&lt;K, V&gt;&gt;</c> for the inner type's sake, so
        /// naming the outer one would send the reader to the wrong half of the declaration.
        /// </summary>
        [Test]
        public void AStandInIsOfferedForTheElementOfACollectionToo()
        {
            Assert.IsTrue(
                UnitySerializationStandIns.TryGetStandIn(
                    typeof(List<Dictionary<string, int>>),
                    out string fromList
                )
            );
            Assert.AreEqual("SerializableDictionary<string, int>", fromList);

            Assert.IsTrue(
                UnitySerializationStandIns.TryGetStandIn(
                    typeof(Dictionary<string, int>[]),
                    out string fromArray
                )
            );
            Assert.AreEqual("SerializableDictionary<string, int>", fromArray);

            Assert.IsFalse(UnitySerializationStandIns.TryGetStandIn(typeof(Type), out _));
            Assert.IsFalse(UnitySerializationStandIns.TryGetStandIn(null, out _));
        }

        private static DroppedSerializedField Reported(
            List<DroppedSerializedField> findings,
            string fieldName
        )
        {
            return findings.Single(finding => finding.FieldName == fieldName);
        }
    }
}
