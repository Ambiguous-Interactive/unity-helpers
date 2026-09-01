// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes;

    /// <summary>Pins the helpers the authored-asset checks are assembled from, one at a time.</summary>
    [TestFixture]
    public sealed class AuthoredAssetHelperTests
    {
        [TestCase("m_Name: Sample", "m_Name", "Sample")]
        [TestCase("m_EditorClassIdentifier:", "m_EditorClassIdentifier", "")]
        [TestCase("_values: []", "_values", "[]")]
        [TestCase("<Name>k__BackingField: 3", "<Name>k__BackingField", "3")]
        [TestCase("m_Name: 'foo: bar'", "m_Name", "'foo: bar'")]
        public void AMappingLineSplitsAtItsFirstTerminatedColon(
            string content,
            string expectedKey,
            string expectedValue
        )
        {
            Assert.IsTrue(
                AuthoredAssetYaml.TrySplitEntry(content, out string key, out string inlineValue)
            );
            Assert.AreEqual(expectedKey, key);
            Assert.AreEqual(expectedValue, inlineValue);
        }

        [TestCase("{fileID: 0}")]
        [TestCase("key:value")]
        [TestCase("- plain scalar")]
        [TestCase("")]
        [TestCase(": leading colon")]
        public void ALineThatIsNotAMappingEntryIsRefused(string content)
        {
            Assert.IsFalse(
                AuthoredAssetYaml.TrySplitEntry(content, out string _, out string _),
                content
            );
        }

        [TestCase("", 0)]
        [TestCase("x", 0)]
        [TestCase("  x", 2)]
        [TestCase("    ", 4)]
        [TestCase("\tx", 0)]
        public void OnlySpacesCountAsIndentation(string line, int expected)
        {
            Assert.AreEqual(expected, AuthoredAssetYaml.LeadingSpaces(line));
        }

        [TestCase("Assets/Art/Hero.prefab", true)]
        [TestCase("assets/art/Hero.prefab", true)]
        [TestCase("Assets\\Art\\Hero.prefab", true)]
        [TestCase("Packages/other/Hero.prefab", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void APathIsInScopeOnlyUnderOneOfThePrefixes(string path, bool expected)
        {
            Assert.AreEqual(
                expected,
                AuthoredAssetYaml.IsUnderAnyPrefix(path, new[] { "Assets/" })
            );
        }

        [Test]
        public void APathWithNoPrefixesAtAllIsOutOfScope()
        {
            Assert.IsFalse(AuthoredAssetYaml.IsUnderAnyPrefix("Assets/A.prefab", null));
            Assert.IsFalse(
                AuthoredAssetYaml.IsUnderAnyPrefix("Assets/A.prefab", Array.Empty<string>())
            );
            Assert.IsFalse(AuthoredAssetYaml.IsUnderAnyPrefix(null, new[] { "Assets/" }));
            Assert.IsFalse(AuthoredAssetYaml.IsUnderAnyPrefix(string.Empty, new[] { "Assets/" }));
        }

        [Test]
        public void AnAssetPathResolvesToSomethingTheFilesystemCanBeAskedAbout()
        {
            string resolved = AuthoredAssetPaths.ToFileSystemPath("Assets/Anything.asset");

            Assert.IsTrue(
                Path.IsPathRooted(resolved),
                "A project-relative path read through System.IO depends on the working directory."
            );
            Assert.IsTrue(
                Directory.Exists(Path.GetDirectoryName(resolved)),
                $"{resolved} does not name a directory in this project."
            );
        }

        [Test]
        public void ResolvingAPathTwiceChangesNothing()
        {
            string once = AuthoredAssetPaths.ToFileSystemPath("Assets/Anything.asset");

            Assert.AreEqual(once, AuthoredAssetPaths.ToFileSystemPath(once));
        }

        [Test]
        public void AResolvedPathRoundTripsBackToTheAssetPathAReaderCanClick()
        {
            const string AssetPath = "Assets/Nested/Anything.asset";

            Assert.AreEqual(
                AssetPath,
                AuthoredAssetPaths.ToAssetPath(AuthoredAssetPaths.ToFileSystemPath(AssetPath))
            );
        }

        [TestCase(null)]
        [TestCase("")]
        public void AnAbsentPathResolvesToItself(string assetPath)
        {
            Assert.AreEqual(assetPath, AuthoredAssetPaths.ToFileSystemPath(assetPath));
            Assert.AreEqual(assetPath, AuthoredAssetPaths.ToAssetPath(assetPath));
        }

        [Test]
        public void APathOutsideTheProjectIsLeftAlone()
        {
            string outside = Path.Combine(Path.GetTempPath(), "Elsewhere.asset").Replace('\\', '/');

            Assert.AreEqual(outside, AuthoredAssetPaths.ToAssetPath(outside));
        }

        [Test]
        public void AGenericTypesArityIsStrippedFromTheNameAFileWouldCarry()
        {
            Assert.AreEqual("List", MonoScriptBindingValidator.SimpleNameOf(typeof(List<string>)));
            Assert.AreEqual(
                nameof(AuthoredRequirementTestAsset),
                MonoScriptBindingValidator.SimpleNameOf(typeof(AuthoredRequirementTestAsset))
            );
        }

        [Test]
        public void OnlyAComponentOrAssetTypeCarriesABindingWorthNaming()
        {
            Assert.IsTrue(
                MonoScriptBindingValidator.IsAuthorable(typeof(AuthoredRequirementTestAsset))
            );
            Assert.IsTrue(MonoScriptBindingValidator.IsAuthorable(typeof(MonoBehaviour)));
            Assert.IsFalse(MonoScriptBindingValidator.IsAuthorable(typeof(string)));
            Assert.IsFalse(MonoScriptBindingValidator.IsAuthorable(null));
            Assert.IsFalse(
                MonoScriptBindingValidator.IsAuthorable(typeof(UnityEditor.Editor)),
                "An inspector is found through [CustomEditor], not through a saved reference."
            );
        }

        [Test]
        public void AnInspectableClassificationSurvivesTheShapesTheTextReaderCanJudge()
        {
            Assert.IsTrue(
                AuthoredRequirementValidator.TryClassify(
                    typeof(AuthoredRequirementTestAsset).GetField(
                        nameof(AuthoredRequirementTestAsset.requiredMaterial)
                    ),
                    out AuthoredRequirementField reference
                )
            );
            Assert.IsTrue(reference.IsObjectReference);
            Assert.IsFalse(reference.IsCollection);
            Assert.IsTrue(AuthoredRequirementValidator.IsEmptyValue(reference, "{fileID: 0}"));
            Assert.IsFalse(
                AuthoredRequirementValidator.IsEmptyValue(
                    reference,
                    "{fileID: 1, guid: a, type: 3}"
                )
            );

            Assert.IsTrue(
                AuthoredRequirementValidator.TryClassify(
                    typeof(AuthoredRequirementTestAsset).GetField(
                        nameof(AuthoredRequirementTestAsset.requiredMaterials)
                    ),
                    out AuthoredRequirementField collection
                )
            );
            Assert.IsTrue(collection.IsCollection);

            Assert.IsTrue(
                AuthoredRequirementValidator.TryClassify(
                    typeof(AuthoredRequirementTestAsset).GetField(
                        nameof(AuthoredRequirementTestAsset.requiredName)
                    ),
                    out AuthoredRequirementField text
                )
            );
            Assert.IsFalse(text.IsObjectReference);
            Assert.IsTrue(AuthoredRequirementValidator.IsEmptyValue(text, string.Empty));
            Assert.IsFalse(AuthoredRequirementValidator.IsEmptyValue(text, "filled"));
        }

        [Test]
        public void AValueWithNoTextFormTheInspectorJudgesIsRefused()
        {
            Assert.IsFalse(
                AuthoredRequirementValidator.TryClassify(
                    typeof(AuthoredRequirementTestAsset).GetField(
                        nameof(AuthoredRequirementTestAsset.weight)
                    ),
                    out AuthoredRequirementField _
                ),
                "A number has no empty state the inspector reports."
            );
        }
    }
}
