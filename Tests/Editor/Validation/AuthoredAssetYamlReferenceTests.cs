// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Validation;

    [TestFixture]
    public sealed class AuthoredAssetYamlReferenceTests
    {
        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase("not a reference", "")]
        [TestCase("{}", "")]
        [TestCase("{guid: external}", "")]
        [TestCase("{fileID: 0}", "0")]
        [TestCase("{fileID: -1}", "-1")]
        [TestCase("{fileID: 9223372036854775807}", "9223372036854775807")]
        [TestCase("{fileID: -9223372036854775808}", "-9223372036854775808")]
        [TestCase("{fileID: 9223372036854775808}", "")]
        [TestCase("{fileID: 1, fileID: 2}", "")]
        [TestCase("{fileID: 12, guid: external, type: 3}", "12")]
        public void FileIdsPreserveSignedAnchorsAndRefuseMalformedMappings(
            string reference,
            string expected
        )
        {
            Assert.AreEqual(expected, AuthoredAssetYaml.FileId(reference));
        }

        [TestCase("{fileID: 12}", "owner", "owner")]
        [TestCase("{fileID: 12}", null, "")]
        [TestCase("{fileID: 12}", " ", "")]
        [TestCase("{fileID: 12, guid: external}", "owner", "external")]
        [TestCase("{fileID: 12, guid: external}", null, "external")]
        [TestCase("{fileID: 0}", "owner", "")]
        [TestCase("{fileID: 0, guid: external}", "owner", "")]
        [TestCase("broken", "owner", "")]
        [TestCase(null, "owner", "")]
        [TestCase("{guid: external}", "owner", "")]
        [TestCase("{fileID: 1, guid: first, guid: second}", "owner", "")]
        public void ReferencedGuidsDistinguishLocalExternalNullAndMalformedReferences(
            string reference,
            string owner,
            string expected
        )
        {
            Assert.AreEqual(expected, AuthoredAssetYaml.ReferencedGuid(reference, owner));
        }

        [TestCase("{fileID: 7}", "{fileID: 7}")]
        [TestCase("  field: {fileID: 7}", "{fileID: 7}")]
        [TestCase("  - {fileID: 7}", "{fileID: 7}")]
        [TestCase("  - field: {fileID: 7}", "{fileID: 7}")]
        [TestCase(
            "  field: {fileID: 7, guid: external, type: 3} # trailing",
            "{fileID: 7, guid: external, type: 3}"
        )]
        [TestCase("  'field': {fileID: 0}\r", "{fileID: 0}")]
        public void InlineReferenceReadsSupportFieldsSequencesAndComments(
            string line,
            string expected
        )
        {
            Assert.IsTrue(AuthoredAssetYaml.TryGetInlineReference(line, out string reference));
            Assert.AreEqual(expected, reference);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("# {fileID: 7}")]
        [TestCase("field: \"{fileID: 7}\"")]
        [TestCase("field: 'prefix: {fileID: 7} # comment")]
        [TestCase("field: {fileID: 7} {fileID: 8}")]
        [TestCase("field: {fileID: 7} trailing")]
        [TestCase("field: {guid: external}")]
        [TestCase("field: {fileID: 7, fileID: 8}")]
        [TestCase("field: {fileID: 7")]
        [TestCase("field: fileID: 7}")]
        public void InlineReferenceReadsRefuseScalarTextAndAmbiguity(string line)
        {
            Assert.IsFalse(AuthoredAssetYaml.TryGetInlineReference(line, out string reference));
            Assert.IsTrue(reference == null);
        }

        [TestCase(null, "")]
        [TestCase(" ", "")]
        [TestCase("ffffffffffffffffffffffffffffffff", "ffffffffffffffffffffffffffffffff")]
        public void AssetNamesRetainUnknownIdentities(string guid, string expected)
        {
            Assert.AreEqual(expected, AuthoredAssetYaml.AssetName(guid));
        }

        [Test]
        public void AssetNamesReadKnownMetadataWithoutLoadingTheAsset()
        {
            const string path =
                "Packages/com.wallstop-studios.unity-helpers/Editor/Validation/AuthoredAssetYaml.cs";
            string guid = AssetDatabase.AssetPathToGUID(path);
            Assert.IsFalse(string.IsNullOrWhiteSpace(guid));
            Assert.AreEqual(nameof(AuthoredAssetYaml), AuthoredAssetYaml.AssetName(guid));
        }

        [Test]
        public void AuthoredExtensionGroupsExposeTheConsumerScope()
        {
            Assert.AreEqual("Assets", AuthoredAssetYaml.AssetsRoot);
            Assert.AreEqual("MonoBehaviour", AuthoredAssetYaml.MonoBehaviourType);
            Assert.AreEqual("Transform", AuthoredAssetYaml.TransformType);
            Assert.AreEqual("RectTransform", AuthoredAssetYaml.RectTransformType);
            CollectionAssert.AreEqual(
                new[] { ".unity", ".prefab" },
                AuthoredAssetYaml.SceneExtensions
            );
            CollectionAssert.AreEqual(new[] { ".asset" }, AuthoredAssetYaml.AssetExtensions);
            CollectionAssert.AreEqual(
                AuthoredAssetYaml.AuthoredExtensions,
                AuthoredAssetYaml.AllExtensions
            );
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)AuthoredAssetYaml.SceneExtensions)[0] = ".other"
            );
        }
    }
}
