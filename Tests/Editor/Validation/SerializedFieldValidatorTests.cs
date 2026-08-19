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
                new[] { "lookup", "tags", "optionalCount", "frameworkPair", "_ordered" },
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

            // A serialized primitive, the package stand-in for the dictionary beside it, and a user
            // generic -- which Unity has serialized since 2020 and which a rules table would be
            // most likely to report by mistake.
            CollectionAssert.DoesNotContain(reported, "count");
            CollectionAssert.DoesNotContain(reported, "serializedLookup");
            CollectionAssert.DoesNotContain(reported, "path");

            // A field that says it is runtime-only, and one that never asked to be serialized.
            CollectionAssert.DoesNotContain(reported, "runtimeCache");
            CollectionAssert.DoesNotContain(reported, "_privateCache");
        }

        [Test]
        public void TheReportNamesTheStandInToUse()
        {
            List<DroppedSerializedField> findings = new();
            SerializedFieldValidator.TryValidate(typeof(DroppedSerializedFieldAsset), findings);

            // Naming the fix is the useful half. A message that only says something is wrong sends
            // the reader to a search engine.
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

        [Test]
        public void TheTupleStandInIsSerializedWhereTheFrameworkTupleIsNot()
        {
            // The pair #289 shipped, checked from the outside: the same asset carries both, and only
            // one of them survives. Whatever the two types have in common -- both are
            // [Serializable], both are structs of two values -- it is not what decides this.
            List<DroppedSerializedField> findings = new();
            Assert.IsTrue(
                SerializedFieldValidator.TryValidate(typeof(SerializableValueTupleAsset), findings)
            );

            CollectionAssert.AreEqual(
                new[] { "frameworkPair" },
                findings.Select(finding => finding.FieldName).ToArray()
            );
        }

        [Test]
        public void ATypeThatCannotBeConstructedIsDeclinedRatherThanThrown()
        {
            // A project scan reaches every type in every loaded assembly, so anything that refuses
            // to be inspected has to be a skipped entry rather than the end of the scan.
            List<DroppedSerializedField> findings = new();

            Assert.IsFalse(SerializedFieldValidator.TryValidate(null, findings));
            Assert.IsFalse(SerializedFieldValidator.TryValidate(typeof(string), findings));
            Assert.IsFalse(SerializedFieldValidator.TryValidate(typeof(MonoBehaviour), null));

            Assert.IsFalse(SerializedFieldValidator.IsInspectable(typeof(List<>)));
            Assert.IsTrue(
                SerializedFieldValidator.IsInspectable(typeof(DroppedSerializedFieldAsset))
            );
        }

        [Test]
        public void AStandInIsOfferedForTheElementOfACollectionToo()
        {
            // Unity drops `List<Dictionary<K, V>>` for the inner type's sake, so naming the outer
            // one would send the reader to the wrong half of the declaration.
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

            // And nothing is invented for a type the package has no answer for.
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
