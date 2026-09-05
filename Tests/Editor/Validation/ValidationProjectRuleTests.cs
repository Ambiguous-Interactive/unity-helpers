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
    public sealed class ValidationProjectRuleTests : CommonTestBase
    {
        [TestCase(1.5, ">", "1", true)]
        [TestCase(1.5, "<", "1", false)]
        [TestCase(1.5, "==", "1.50", true)]
        [TestCase(1.5, "!=", "1.5", false)]
        [TestCase(1.5, ">", "invalid", false)]
        [TestCase("stereo", "contains", "ter", true)]
        [TestCase(null, "is null", "", true)]
        [TestCase(null, "is missing", "", true)]
        [TestCase(null, "!=", "null", false)]
        [TestCase(true, "==", "true", true)]
        public void ConditionsUseTypedInvariantValues(
            object actual,
            string comparison,
            string expected,
            bool matches
        )
        {
            Assert.AreEqual(matches, ValidationProjectRule.Matches(actual, comparison, expected));
        }

        [TestCase("Assets/Prefabs/A.prefab", true)]
        [TestCase("Assets/Prefabs/Sub/A.prefab", true)]
        [TestCase("Assets/PrefabsOther/A.prefab", false)]
        [TestCase("Assets/Prefabs/A.unity", false)]
        public void PathFilterRespectsFolderAndCategoryBoundaries(string path, bool matches)
        {
            ValidationWorkspaceSettings.RuleDefinition definition = Definition();
            definition.pathFilter = "Assets/Prefabs/";
            ValidationTarget target = new ValidationTarget("guid", path, typeof(GameObject));
            Assert.AreEqual(matches, new ValidationProjectRule(definition).AppliesTo(in target));
        }

        [Test]
        public void RunCapturesTheRuleBeforeFurtherBuilderEdits()
        {
            ValidationWorkspaceSettings.RuleDefinition definition = Definition();
            ValidationProjectRule rule = new ValidationProjectRule(definition);
            definition.message = "changed";
            definition.checks[0].value = "99";
            GameObject subject = Track(new GameObject("Subject"));
            subject.transform.localScale = new Vector3(1, 2, 1);
            List<ValidationFinding> findings = Validate(rule, subject);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual("Scale exceeds one", findings[0].Message);
        }

        [Test]
        public void EveryConditionMustMatchTheSameObject()
        {
            ValidationWorkspaceSettings.RuleDefinition definition = Definition();
            definition.checks.Add(
                new ValidationWorkspaceSettings.RuleCondition
                {
                    property = "Rigidbody.mass",
                    comparison = ">",
                    value = "10",
                }
            );
            GameObject subject = Track(new GameObject("Subject"));
            subject.transform.localScale = new Vector3(1, 2, 1);
            Rigidbody body = subject.AddComponent<Rigidbody>();
            body.mass = 1;
            Assert.IsEmpty(Validate(new ValidationProjectRule(definition), subject));
            body.mass = 11;
            Assert.AreEqual(1, Validate(new ValidationProjectRule(definition), subject).Count);
        }

        [Test]
        public void MissingComponentDoesNotMasqueradeAsItsNullProperty()
        {
            ValidationWorkspaceSettings.RuleDefinition definition = Definition();
            definition.checks[0] = new ValidationWorkspaceSettings.RuleCondition
            {
                property = "Renderer.sharedMaterial",
                comparison = "is null",
                value = string.Empty,
            };
            GameObject subject = Track(new GameObject("Subject"));
            Assert.IsEmpty(Validate(new ValidationProjectRule(definition), subject));
            subject.AddComponent<MeshRenderer>();
            Assert.AreEqual(1, Validate(new ValidationProjectRule(definition), subject).Count);
        }

        [Test]
        public void SameNamedSiblingsHaveDifferentSuppressionIdentities()
        {
            GameObject root = Track(new GameObject("Root"));
            foreach (int index in new[] { 0, 1 })
            {
                GameObject child = Track(new GameObject("Same"));
                child.transform.SetParent(root.transform);
                child.transform.localScale = new Vector3(1, 2, 1);
            }
            List<ValidationFinding> findings = Validate(
                new ValidationProjectRule(Definition()),
                root
            );
            Assert.AreEqual(2, findings.Count);
            Assert.AreNotEqual(findings[0].Id, findings[1].Id);
        }

        [Test]
        public void RequiredFieldsFindEmptyStringsAndCollectionElements()
        {
            AuthoredRequirementTestAsset subject = Track(
                ScriptableObject.CreateInstance<AuthoredRequirementTestAsset>()
            );
            Material material = Track(new Material(Shader.Find("Hidden/InternalErrorShader")));
            Texture2D texture = Track(new Texture2D(2, 2));
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
            subject.requiredMaterial = material;
            subject.requiredName = "present";
            subject.requiredMaterials = new Material[0];
            subject.icon = sprite;
            ValidationWorkspaceSettings.RuleDefinition definition = Definition();
            definition.checks[0] = new ValidationWorkspaceSettings.RuleCondition
            {
                property = "[Required] fields",
                comparison = "is null",
                value = string.Empty,
            };
            ValidationProjectRule rule = new ValidationProjectRule(definition);
            ValidationTarget target = new ValidationTarget(
                "guid",
                "Assets/Test.asset",
                typeof(AuthoredRequirementTestAsset)
            );
            List<ValidationFinding> findings = new List<ValidationFinding>();
            rule.Validate(in target, subject, findings);
            Assert.IsEmpty(findings);
            subject.requiredName = string.Empty;
            rule.Validate(in target, subject, findings);
            Assert.AreEqual(1, findings.Count);
            findings.Clear();
            subject.requiredName = "present";
            subject.requiredMaterials = new Material[] { null };
            rule.Validate(in target, subject, findings);
            Assert.AreEqual(1, findings.Count);
        }

        [Test]
        public void EveryAudioSourceIsCheckedAndFindingTargetsTheMatchingComponent()
        {
            GameObject subject = Track(new GameObject("Audio"));
            AudioClip stereo = Track(AudioClip.Create("Stereo", 64, 2, 44100, false));
            AudioSource first = subject.AddComponent<AudioSource>();
            first.spatialBlend = 0;
            first.clip = stereo;
            AudioSource second = subject.AddComponent<AudioSource>();
            second.spatialBlend = 1;
            second.clip = stereo;
            ValidationWorkspaceSettings.RuleDefinition definition =
                new ValidationWorkspaceSettings.RuleDefinition
                {
                    id = "project.audio",
                    pathFilter = string.Empty,
                };
            List<ValidationFinding> findings = Validate(
                new ValidationProjectRule(definition),
                subject
            );
            Assert.AreEqual(1, findings.Count);
            Assert.IsTrue(findings[0].TryGetTarget(out UnityEngine.Object target));
            Assert.AreSame(second, target);
        }

        [TestCase("instanceID", "-2147483648")]
        [TestCase("instanceID", "4294967296")]
        [TestCase("instanceID", "18446744073709551615")]
        public void FingerprintsNormalizeFullWidthReferenceIdentifiers(string field, string id)
        {
            Dictionary<string, string> references = new Dictionary<string, string>(
                StringComparer.Ordinal
            )
            {
                { "reference", "stable-reference" },
            };
            string json = "{\"reference\":{\"" + field + "\":" + id + "}}";
            Assert.AreEqual(
                "{\"reference\":\"stable-reference\"}",
                ValidationProjectRule.NormalizeReferences(json, references)
            );
        }

        [TestCase("0")]
        [TestCase("18446744073709551615")]
        public void FingerprintsPreserveUnmappedReferences(string id)
        {
            string json = "{\"reference\":{\"instanceID\":" + id + "}}";
            Assert.AreEqual(
                json,
                ValidationProjectRule.NormalizeReferences(
                    json,
                    new Dictionary<string, string> { { "anotherReference", "stable" } }
                )
            );
        }

        [TestCase("{\"instanceID\":1}")]
        [TestCase("{\"instanceID\":18446744073709551615}")]
        [TestCase("{\"entityId\":18446744073709551615}")]
        [TestCase("{\"text\":\"\\\"instanceID\\\":123\"}")]
        [TestCase("{\"ordinary\":{\"instanceID\":123}}")]
        public void FingerprintsPreserveOrdinaryIdentifierFieldsAndQuotedText(string json)
        {
            Assert.AreEqual(
                json,
                ValidationProjectRule.NormalizeReferences(
                    json,
                    new Dictionary<string, string> { { "anotherReference", "stable" } }
                )
            );
        }

        [TestCase("{\"instanceID\":1}", "", "\"stable\"")]
        [TestCase(
            "{\"nested\":{\"reference\":{\"instanceID\":1},\"instanceID\":2}}",
            "nested.reference",
            "{\"nested\":{\"reference\":\"stable\",\"instanceID\":2}}"
        )]
        [TestCase(
            "{\"values\":[{\"instanceID\":1},{\"instanceID\":2}]}",
            "values.Array.data[0]",
            "{\"values\":[\"stable\",{\"instanceID\":2}]}"
        )]
        [TestCase(
            "{\"values\":[{\"instanceID\":1},{\"instanceID\":2}]}",
            "values.Array.data[1]",
            "{\"values\":[{\"instanceID\":1},\"stable\"]}"
        )]
        [TestCase(
            "{\"rows\":[{\"values\":[{\"reference\":{\"instanceID\":1}},{\"reference\":{\"instanceID\":2}}]}]}",
            "rows.Array.data[0].values.Array.data[1].reference",
            "{\"rows\":[{\"values\":[{\"reference\":{\"instanceID\":1}},{\"reference\":\"stable\"}]}]}"
        )]
        public void FingerprintsNormalizeOnlyExactReferencePaths(
            string json,
            string path,
            string expected
        )
        {
            Assert.AreEqual(
                expected,
                ValidationProjectRule.NormalizeReferences(
                    json,
                    new Dictionary<string, string> { { path, "stable" } }
                )
            );
        }

        [Test]
        public void FingerprintsPreserveReferenceIdentityAcrossNumericIdentifierChanges()
        {
            string first = "{\"reference\":{\"instanceID\":1}}";
            string second = "{\"reference\":{\"instanceID\":18446744073709551615}}";
            Dictionary<string, string> references = new Dictionary<string, string>
            {
                { "reference", "stable-first" },
            };
            string normalized = ValidationProjectRule.NormalizeReferences(first, references);
            Assert.AreEqual(
                normalized,
                ValidationProjectRule.NormalizeReferences(second, references)
            );
            references["reference"] = "stable-second";
            Assert.AreNotEqual(
                normalized,
                ValidationProjectRule.NormalizeReferences(second, references)
            );
            Assert.AreNotEqual(
                normalized,
                ValidationProjectRule.NormalizeReferences(second, new Dictionary<string, string>())
            );
        }

        [TestCase("{\"reference\":{\"instanceID\":NaN}}")]
        [TestCase("{\"reference\":")]
        public void FingerprintsPreserveUnparseableJson(string json)
        {
            Assert.AreEqual(
                json,
                ValidationProjectRule.NormalizeReferences(
                    json,
                    new Dictionary<string, string> { { "reference", "stable" } }
                )
            );
        }

        [Test]
        public void FingerprintsPreserveJsonBeyondParserDepth()
        {
            string json = new string('[', 70) + "1" + new string(']', 70);
            Assert.AreEqual(
                json,
                ValidationProjectRule.NormalizeReferences(
                    json,
                    new Dictionary<string, string> { { "reference", "stable" } }
                )
            );
        }

        [Test]
        public void FingerprintReferencePathsMatchNativeEditorJson()
        {
            Material material = Track(new Material(Shader.Find("Hidden/InternalErrorShader")));
            Dictionary<string, string> references = new Dictionary<string, string>(
                StringComparer.Ordinal
            );
            using (SerializedObject serialized = new SerializedObject(material))
            {
                SerializedProperty shader = serialized.FindProperty("m_Shader");
                Assert.IsTrue(shader != null);
                Assert.AreEqual(SerializedPropertyType.ObjectReference, shader.propertyType);
                references.Add(shader.propertyPath, "stable-shader");
            }
            string normalized = ValidationProjectRule.NormalizeReferences(
                EditorJsonUtility.ToJson(material),
                references
            );
            Assert.That(normalized, Does.Contain("\"m_Shader\":\"stable-shader\""));
        }

        private static ValidationWorkspaceSettings.RuleDefinition Definition()
        {
            return new ValidationWorkspaceSettings.RuleDefinition
            {
                id = "project.test",
                pathFilter = string.Empty,
                message = "Scale exceeds one",
                checks = new List<ValidationWorkspaceSettings.RuleCondition>
                {
                    new ValidationWorkspaceSettings.RuleCondition
                    {
                        property = "Transform.localScale.y",
                        comparison = ">",
                        value = "1",
                    },
                },
            };
        }

        private static List<ValidationFinding> Validate(
            ValidationProjectRule rule,
            GameObject subject
        )
        {
            ValidationTarget target = new ValidationTarget(
                "guid",
                "Assets/Test.prefab",
                typeof(GameObject)
            );
            List<ValidationFinding> findings = new List<ValidationFinding>();
            rule.Validate(in target, subject, findings);
            return findings;
        }
    }
}
