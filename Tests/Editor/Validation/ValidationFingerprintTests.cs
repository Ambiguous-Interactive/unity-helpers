// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes;

    [TestFixture]
    public sealed class ValidationFingerprintTests : CommonTestBase
    {
        private string _folder;
        private bool _previousAutoRun;

        [SetUp]
        public void SetUp()
        {
            _previousAutoRun = ValidationAutoRun.Enabled;
            ValidationAutoRun.Enabled = false;
            _folder = "Assets/SentinelFingerprint" + Guid.NewGuid().ToString("N");
            TrackFolder(_folder);
            Assert.IsNotEmpty(
                AssetDatabase.CreateFolder("Assets", _folder.Substring("Assets/".Length))
            );
        }

        [TearDown]
        public void RestoreAutomaticValidation()
        {
            ValidationAutoRun.Enabled = _previousAutoRun;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ScalarNamedInstanceIdChangesFingerprint(bool nested)
        {
            ValidationFingerprintAsset subject =
                CreateScriptableObject<ValidationFingerprintAsset>();
            subject.instanceID = 11;
            subject.nested.instanceID = 11;
            string path = _folder + "/Transient.asset";
            string before = ValidationProjectRule.Fingerprint(subject, path);
            if (nested)
                subject.nested.instanceID = 17;
            else
                subject.instanceID = 17;
            Assert.AreNotEqual(before, ValidationProjectRule.Fingerprint(subject, path));
        }

        [Test]
        public void NativeNestedAndArrayPropertyPathsIdentifyTheirJsonReferenceNodes()
        {
            ValidationFingerprintAsset subject =
                CreateScriptableObject<ValidationFingerprintAsset>();
            ValidationFingerprintAsset reference =
                CreateScriptableObject<ValidationFingerprintAsset>();
            subject.instanceID = 11;
            subject.nested.instanceID = 17;
            subject.nested.reference = reference;
            subject.slots = new[]
            {
                new ValidationFingerprintAsset.ReferenceSlot
                {
                    instanceID = 23,
                    reference = reference,
                },
                new ValidationFingerprintAsset.ReferenceSlot
                {
                    instanceID = 31,
                    reference = reference,
                },
            };
            Dictionary<string, string> paths = new Dictionary<string, string>(
                StringComparer.Ordinal
            )
            {
                [nameof(subject.nested) + "." + nameof(subject.nested.reference)] = "stable-inner",
                [nameof(subject.slots) + ".Array.data[0]." + nameof(subject.nested.reference)] =
                    "stable-first",
                [nameof(subject.slots) + ".Array.data[1]." + nameof(subject.nested.reference)] =
                    "stable-second",
            };
            using (SerializedObject serialized = new SerializedObject(subject))
            {
                foreach (KeyValuePair<string, string> entry in paths)
                {
                    SerializedProperty property = serialized.FindProperty(entry.Key);
                    Assert.IsTrue(property != null, entry.Key);
                    Assert.AreEqual(SerializedPropertyType.ObjectReference, property.propertyType);
                    Assert.AreSame(reference, property.objectReferenceValue);
                }
            }
            string normalized = ValidationProjectRule.NormalizeReferences(
                EditorJsonUtility.ToJson(subject),
                paths,
                hasEditorRoot: true
            );
            StringAssert.Contains("\"reference\":\"stable-inner\"", normalized);
            StringAssert.Contains("\"reference\":\"stable-first\"", normalized);
            StringAssert.Contains("\"reference\":\"stable-second\"", normalized);
            foreach (int scalar in new[] { 11, 17, 23, 31 })
                StringAssert.Contains("\"instanceID\":" + scalar, normalized);
        }

        [Test]
        public void SwitchingTransientReferencesChangesFingerprint()
        {
            ValidationFingerprintAsset subject =
                CreateScriptableObject<ValidationFingerprintAsset>();
            ValidationFingerprintAsset first = CreateScriptableObject<ValidationFingerprintAsset>();
            ValidationFingerprintAsset second =
                CreateScriptableObject<ValidationFingerprintAsset>();
            first.name = "Same";
            second.name = "Same";
            string path = _folder + "/Transient.asset";
            subject.nested.reference = first;
            string before = ValidationProjectRule.Fingerprint(subject, path);
            subject.nested.reference = second;
            Assert.AreNotEqual(before, ValidationProjectRule.Fingerprint(subject, path));
        }

        [Test]
        public void PersistentReferencesKeepFingerprintAfterAssetUnloadAndReload()
        {
            ValidationFingerprintAsset reference =
                CreateScriptableObject<ValidationFingerprintAsset>();
            string referencePath = _folder + "/Reference.asset";
            TrackAssetPath(referencePath);
            AssetDatabase.CreateAsset(reference, referencePath);
            ValidationFingerprintAsset subject =
                CreateScriptableObject<ValidationFingerprintAsset>();
            subject.nested.reference = reference;
            subject.slots = new[]
            {
                new ValidationFingerprintAsset.ReferenceSlot { reference = reference },
            };
            string subjectPath = _folder + "/Subject.asset";
            TrackAssetPath(subjectPath);
            AssetDatabase.CreateAsset(subject, subjectPath);
            AssetDatabase.SaveAssets();
            string referenceIdentity = GlobalObjectId.GetGlobalObjectIdSlow(reference).ToString();
            string before = ValidationProjectRule.Fingerprint(subject, subjectPath);
            string originalJson = EditorJsonUtility.ToJson(subject);
            Resources.UnloadAsset(subject);
            Resources.UnloadAsset(reference);
            Assert.IsTrue(subject == null);
            Assert.IsTrue(reference == null);
            ValidationFingerprintAsset reloaded = Track(
                AssetDatabase.LoadAssetAtPath<ValidationFingerprintAsset>(subjectPath)
            );
            Assert.IsTrue(reloaded != null);
            Assert.IsTrue(reloaded.nested.reference != null);
            string reloadedJson = EditorJsonUtility.ToJson(reloaded);
            if (string.Equals(originalJson, reloadedJson, StringComparison.Ordinal))
            {
                Assert.Ignore(
                    "Unity reused native reference identifiers after reload; raw JSON did not change, so reference normalization was not exercised."
                );
            }
            Assert.AreEqual(
                referenceIdentity,
                GlobalObjectId.GetGlobalObjectIdSlow(reloaded.nested.reference).ToString()
            );
            Assert.AreEqual(before, ValidationProjectRule.Fingerprint(reloaded, subjectPath));
        }

        [Test]
        public void CyclicManagedReferencesFinishFingerprinting()
        {
            ValidationFingerprintAsset subject =
                CreateScriptableObject<ValidationFingerprintAsset>();
            subject.managed = new ValidationFingerprintAsset.ManagedNode { instanceID = 7 };
            subject.managed.next = subject.managed;
            string fingerprint = ValidationProjectRule.Fingerprint(
                subject,
                _folder + "/Transient.asset"
            );
            Assert.IsNotEmpty(fingerprint);
            Assert.AreEqual(
                fingerprint,
                ValidationProjectRule.Fingerprint(subject, _folder + "/Transient.asset")
            );
        }

        [Test]
        public void ScalarEditsInsideManagedCyclesChangeFingerprint()
        {
            ValidationFingerprintAsset subject =
                CreateScriptableObject<ValidationFingerprintAsset>();
            subject.managed = new ValidationFingerprintAsset.ManagedNode { instanceID = 7 };
            subject.managed.next = subject.managed;
            string path = _folder + "/Transient.asset";
            string before = ValidationProjectRule.Fingerprint(subject, path);
            subject.managed.instanceID = 19;
            Assert.AreNotEqual(before, ValidationProjectRule.Fingerprint(subject, path));
        }
    }
}
