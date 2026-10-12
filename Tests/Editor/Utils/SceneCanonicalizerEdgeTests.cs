// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class SceneCanonicalizerEdgeTests
    {
        private const string Scene =
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1 &11\nGameObject:\n  m_Component:\n  - component: {fileID: 12}\n  m_Name: Root\n--- !u!4 &12\nTransform:\n  m_GameObject: {fileID: 11}\n  m_Father: {fileID: 0}\n  m_Children: []\n--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_Roots:\n  - {fileID: 12}\n";

        [TestCase(null)]
        [TestCase("")]
        public void EmptyInputIsPreserved(string yaml)
        {
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(yaml), Is.EqualTo(yaml));
        }

        [TestCase("no YAML")]
        [TestCase("--- !u!x &1\nInvalid:\n")]
        [TestCase("--- !u!1 missing\nGameObject:\n")]
        [TestCase("--- !u!1 &0\nGameObject:\n")]
        [TestCase("--- !u!1 &1\nGameObject:\n--- !u!4 &1\nTransform:\n")]
        [TestCase("--- !u!1 &99999999999999999999999999\nGameObject:\n")]
        [TestCase("--- !u!1 &1 unknown\nGameObject:\n")]
        public void MalformedInputReportsFailureAndRemainsUnchanged(string yaml)
        {
            Assert.That(
                SceneCanonicalizer.TryCanonicalizeYaml(
                    yaml,
                    out string canonical,
                    out Exception error
                ),
                Is.False
            );
            Assert.That(canonical, Is.EqualTo(yaml));
            Assert.That(error, Is.Not.Null);
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(yaml), Is.EqualTo(yaml));
        }

        [TestCase("\n", true)]
        [TestCase("\r\n", true)]
        [TestCase("\n", false)]
        [TestCase("\r\n", false)]
        public void PreservesLineEndingsAndFinalNewline(string newline, bool trailing)
        {
            string yaml = Scene.Replace("\n", newline);
            if (!trailing)
            {
                yaml = yaml.Substring(0, yaml.Length - newline.Length);
            }
            string canonical = SceneCanonicalizer.CanonicalizeYaml(yaml);
            Assert.That(
                canonical.EndsWith(newline, StringComparison.Ordinal),
                Is.EqualTo(trailing)
            );
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(canonical), Is.EqualTo(canonical));
            if (string.Equals(newline, "\r\n", StringComparison.Ordinal))
            {
                Assert.That(canonical.Replace("\r\n", ""), Does.Not.Contain("\n"));
            }
        }

        [Test]
        public void NegativeAnchorsAndReferencesCanonicalizeLikePositiveOnes()
        {
            string negative = Scene
                .Replace("&11", "&-11")
                .Replace("&12", "&-12")
                .Replace("{fileID: 11}", "{fileID: -11}")
                .Replace("{fileID: 12}", "{fileID: -12}");
            Assert.That(
                SceneCanonicalizer.CanonicalizeYaml(negative),
                Is.EqualTo(SceneCanonicalizer.CanonicalizeYaml(Scene))
            );
        }

        [Test]
        public void PreservesSentinelExternalReferencesAndScalarText()
        {
            const string fields =
                "  external: {fileID: 11, guid: 0123456789abcdef0123456789abcdef, type: 3}\n  text: \"{fileID: 11}\"\n  single: '{fileID: 12}'\n  escaped: \"\\\"{fileID: 11}\\\"\"\n  block: |-\n    {fileID: 11}\n    {fileID: 12}\n  comment: 0 # {fileID: 11}\n";
            string canonical = SceneCanonicalizer.CanonicalizeYaml(
                Scene.Replace("  m_Name: Root\n", "  m_Name: Root\n" + fields)
            );
            Assert.That(canonical, Does.Contain("&9223372036854775807\nSceneRoots:"));
            Assert.That(canonical, Does.Contain(fields));
            Assert.That(canonical, Does.Not.Contain("m_GameObject: {fileID: 11}"));
        }

        [TestCase("Toss 'em")]
        [TestCase("say \"hello")]
        [TestCase("hello | world")]
        [TestCase("hello > world")]
        [TestCase("foo:'bar")]
        [TestCase("Hello, 'em")]
        [TestCase("hello, | world")]
        [TestCase("hello, > world")]
        public void PlainScalarPunctuationDoesNotHideLaterReferences(string value)
        {
            string yaml = Scene.Replace(
                "  m_Name: Root\n",
                "  m_Name: Root\n  rows:\n  - text: " + value + "\n    target: {fileID: 11}\n"
            );
            string canonical = SceneCanonicalizer.CanonicalizeYaml(yaml);
            string target = Regex
                .Match(canonical, @"m_GameObject: {fileID: ([0-9]+)}")
                .Groups[1]
                .Value;
            Assert.That(target, Is.Not.Empty);
            Assert.That(canonical, Does.Contain("text: " + value + "\n"));
            Assert.That(canonical, Does.Contain("target: {fileID: " + target + "}"));
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(canonical), Is.EqualTo(canonical));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void RepeatedPrefabInstancesKeepStrippedReferencesStable(
            bool legacyRoots,
            bool nested
        )
        {
            string yaml =
                "--- !u!1001 &1\nPrefabInstance:\n  m_Modification:\n    m_TransformParent: {fileID: 0}\n  m_SourcePrefab: {fileID: 100100000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n--- !u!1001 &2\nPrefabInstance:\n  m_Modification:\n    m_TransformParent: {fileID: 0}\n  m_SourcePrefab: {fileID: 100100000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n--- !u!4 &3 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: 4000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n  m_PrefabInstance: {fileID: 1}\n--- !u!4 &4 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: 4000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n  m_PrefabInstance: {fileID: 2}\n--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_Roots:\n  - {fileID: 3}\n  - {fileID: 4}\n";
            if (legacyRoots)
            {
                yaml = yaml.Substring(
                    0,
                    yaml.IndexOf("--- !u!1660057539", StringComparison.Ordinal)
                );
                yaml = yaml.Replace(
                        "m_PrefabInstance: {fileID: 1}\n",
                        "m_PrefabInstance: {fileID: 1}\n  m_RootOrder: 0\n"
                    )
                    .Replace(
                        "m_PrefabInstance: {fileID: 2}\n",
                        "m_PrefabInstance: {fileID: 2}\n  m_RootOrder: 1\n"
                    );
            }
            if (nested)
            {
                yaml +=
                    "--- !u!1001 &5\nPrefabInstance:\n  m_Modification:\n    m_TransformParent: {fileID: 3}\n  m_SourcePrefab: {fileID: 100100000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}\n--- !u!1001 &6\nPrefabInstance:\n  m_Modification:\n    m_TransformParent: {fileID: 4}\n  m_SourcePrefab: {fileID: 100100000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}\n--- !u!4 &7 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: 5000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}\n  m_PrefabInstance: {fileID: 5}\n--- !u!4 &8 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: 5000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}\n  m_PrefabInstance: {fileID: 6}\n";
            }
            string[] documents = yaml.Split(
                new[] { "--- !u!" },
                StringSplitOptions.RemoveEmptyEntries
            );
            Array.Reverse(documents);
            string twin = "--- !u!" + string.Join("--- !u!", documents);
            twin = Regex.Replace(
                twin,
                @"(?<=&)[1-8](?=\s)|(?<=fileID: )[1-8](?=})",
                match =>
                    (60 - int.Parse(match.Value)).ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    )
            );
            string canonical = SceneCanonicalizer.CanonicalizeYaml(yaml);
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(twin), Is.EqualTo(canonical));
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(canonical), Is.EqualTo(canonical));
        }

        [Test]
        public void AddedNestedPrefabSiblingsKeepDistinctStableIdentities()
        {
            const string yaml =
                "--- !u!1001 &1\nPrefabInstance:\n  m_Modification:\n    m_TransformParent: {fileID: 0}\n    m_AddedGameObjects:\n    - targetCorrespondingSourceObject: {fileID: 4000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n      insertIndex: -1\n      addedObject: {fileID: 5}\n    - targetCorrespondingSourceObject: {fileID: 4000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n      insertIndex: -1\n      addedObject: {fileID: 6}\n    m_AddedComponents: []\n  m_SourcePrefab: {fileID: 100100000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n--- !u!4 &4 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: 4000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n  m_PrefabInstance: {fileID: 1}\n--- !u!1001 &2\nPrefabInstance:\n  m_Modification:\n    m_TransformParent: {fileID: 4}\n  m_SourcePrefab: {fileID: 100100000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}\n--- !u!1001 &3\nPrefabInstance:\n  m_Modification:\n    m_TransformParent: {fileID: 4}\n  m_SourcePrefab: {fileID: 100100000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}\n--- !u!4 &5 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: 5000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}\n  m_PrefabInstance: {fileID: 2}\n--- !u!4 &6 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: 5000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}\n  m_PrefabInstance: {fileID: 3}\n--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_Roots:\n  - {fileID: 1}\n";
            string[] documents = yaml.Split(
                new[] { "--- !u!" },
                StringSplitOptions.RemoveEmptyEntries
            );
            Array.Reverse(documents);
            string twin = "--- !u!" + string.Join("--- !u!", documents);
            twin = Regex.Replace(
                twin,
                @"(?<=&)[1-6](?=\s)|(?<=fileID: )[1-6](?=})",
                match =>
                    (60 - int.Parse(match.Value)).ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    )
            );
            string canonical = SceneCanonicalizer.CanonicalizeYaml(yaml);
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(twin), Is.EqualTo(canonical));
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(canonical), Is.EqualTo(canonical));
        }

        [TestCase("--- !u!4 &1\nTransform:\n  m_Father: {fileID: 1}\n")]
        [TestCase(
            "--- !u!1001 &1\nPrefabInstance:\n  m_Modification:\n    m_TransformParent: {fileID: 2}\n--- !u!4 &2 stripped\nTransform:\n  m_PrefabInstance: {fileID: 1}\n"
        )]
        public void CyclicHierarchyIsRefusedBeforeKeyConstruction(string yaml)
        {
            Assert.That(
                SceneCanonicalizer.TryCanonicalizeYaml(
                    yaml,
                    out string canonical,
                    out Exception error
                ),
                Is.False
            );
            Assert.That(canonical, Is.EqualTo(yaml));
            Assert.That(error, Is.TypeOf<InvalidDataException>());
        }

        [TestCase(
            "--- !u!4 &2\nTransform:\n  m_GameObject: {fileID: 1}\n--- !u!224 &3\nRectTransform:\n  m_GameObject: {fileID: 1}\n"
        )]
        [TestCase(
            "--- !u!4 &3\nTransform:\n  m_GameObject: {fileID: 1}\n  m_Father: {fileID: 0}\n--- !u!4 &2\nTransform:\n  m_GameObject: {fileID: 1}\n  m_Father: {fileID: 5}\n--- !u!1001 &4\nPrefabInstance:\n  m_Modification:\n    m_TransformParent: {fileID: 3}\n--- !u!4 &5 stripped\nTransform:\n  m_PrefabInstance: {fileID: 4}\n"
        )]
        public void DuplicateTransformOwnershipIsRefusedBeforeKeyConstruction(string yaml)
        {
            Assert.That(
                SceneCanonicalizer.TryCanonicalizeYaml(
                    yaml,
                    out string canonical,
                    out Exception error
                ),
                Is.False
            );
            Assert.That(canonical, Is.EqualTo(yaml));
            Assert.That(error, Is.TypeOf<InvalidDataException>());
        }

        [Test]
        public void UnresolvedReferencesCannotBecomeAccidentalLiveReferences()
        {
            const long unresolved = 1768227884;
            string yaml = Scene.Replace(
                "  m_Name: Root\n",
                "  m_Name: Root\n  missing: {fileID: " + unresolved + "}\n"
            );
            string canonical = SceneCanonicalizer.CanonicalizeYaml(yaml);
            Assert.That(canonical, Does.Contain("missing: {fileID: " + unresolved + "}"));
            Assert.That(canonical, Does.Not.Contain("&" + unresolved + "\n"));
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(canonical), Is.EqualTo(canonical));
        }

        [Test]
        public void ExcessiveHierarchyDepthIsRefusedWithoutPartialOutput()
        {
            StringBuilder builder = new();
            const int count = 1026;
            for (int i = 1; i <= count; ++i)
            {
                int parent = i == count ? 0 : i + 1;
                builder
                    .Append("--- !u!4 &")
                    .Append(i)
                    .Append("\nTransform:\n  m_Father: {fileID: ")
                    .Append(parent)
                    .Append("}\n");
            }
            string yaml = builder.ToString();
            Assert.That(
                SceneCanonicalizer.TryCanonicalizeYaml(
                    yaml,
                    out string canonical,
                    out Exception error
                ),
                Is.False
            );
            Assert.That(canonical, Is.EqualTo(yaml));
            Assert.That(error, Is.TypeOf<InvalidDataException>());
        }

        [Test]
        public void HierarchySeparatorsInNamesCannotAliasOtherPaths()
        {
            const string yaml =
                "--- !u!1 &1\nGameObject:\n  m_Name: A#0/B\n  m_Component:\n  - component: {fileID: 2}\n  - component: {fileID: 7}\n--- !u!4 &2\nTransform:\n  m_GameObject: {fileID: 1}\n  m_Father: {fileID: 0}\n--- !u!1 &3\nGameObject:\n  m_Name: A\n  m_Component:\n  - component: {fileID: 4}\n--- !u!4 &4\nTransform:\n  m_GameObject: {fileID: 3}\n  m_Father: {fileID: 0}\n  m_Children:\n  - {fileID: 6}\n--- !u!1 &5\nGameObject:\n  m_Name: B\n  m_Component:\n  - component: {fileID: 6}\n  - component: {fileID: 8}\n--- !u!4 &6\nTransform:\n  m_GameObject: {fileID: 5}\n  m_Father: {fileID: 4}\n--- !u!54 &7\nRigidbody:\n  m_GameObject: {fileID: 1}\n--- !u!54 &8\nRigidbody:\n  m_GameObject: {fileID: 5}\n";
            string[] documents = yaml.Split(
                new[] { "--- !u!" },
                StringSplitOptions.RemoveEmptyEntries
            );
            Array.Reverse(documents);
            string twin = "--- !u!" + string.Join("--- !u!", documents);
            twin = Regex.Replace(
                twin,
                @"(?<=&)[1-8](?=\s)|(?<=fileID: )[1-8](?=})",
                match =>
                    (60 - int.Parse(match.Value)).ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    )
            );
            string canonical = SceneCanonicalizer.CanonicalizeYaml(yaml);
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(twin), Is.EqualTo(canonical));
            Assert.That(canonical, Does.Contain("m_Name: A#0/B"));
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(canonical), Is.EqualTo(canonical));
        }

        [TestCase(".unity", false)]
        [TestCase(".prefab", true)]
        public void FileReplacementPreservesEncodingAndNoOpBytes(string extension, bool preamble)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "UnityYaml-" + Guid.NewGuid().ToString("N") + extension
            );
            try
            {
                UTF8Encoding encoding = new(preamble);
                File.WriteAllText(path, Scene, encoding);
                Assert.That(
                    SceneCanonicalizer.TryCanonicalizeFile(
                        path,
                        out bool changed,
                        out Exception error
                    ),
                    Is.True,
                    error?.ToString()
                );
                Assert.That(changed, Is.True);
                byte[] canonical = File.ReadAllBytes(path);
                Assert.That(canonical[0] == 0xef, Is.EqualTo(preamble));
                Assert.That(
                    SceneCanonicalizer.TryCanonicalizeFile(path, out changed, out error),
                    Is.True,
                    error?.ToString()
                );
                Assert.That(changed, Is.False);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(canonical));
                Assert.That(SceneCanonicalizer.CanonicalizeSceneFile(path), Is.False);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("missing-scene.unity")]
        public void FileFailureReportsErrorWithoutCreatingFiles(string path)
        {
            Assert.That(
                SceneCanonicalizer.TryCanonicalizeFile(path, out bool changed, out Exception error),
                Is.False
            );
            Assert.That(changed, Is.False);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void MalformedFileIsNeverReplaced()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "UnityYaml-" + Guid.NewGuid().ToString("N") + ".prefab"
            );
            try
            {
                byte[] bytes = { 0xff, 0xfe, 0, 1 };
                File.WriteAllBytes(path, bytes);
                Assert.That(
                    SceneCanonicalizer.TryCanonicalizeFile(
                        path,
                        out bool changed,
                        out Exception error
                    ),
                    Is.False
                );
                Assert.That(changed, Is.False);
                Assert.That(error, Is.Not.Null);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void HashCollisionsReceiveDistinctStableIdentifiers()
        {
            const string firstName = "Collision99754";
            const string secondName = "Collision111100";
            string first =
                "--- !u!1 &1\nGameObject:\n  m_Component:\n  - component: {fileID: 3}\n  m_Name: "
                + firstName
                + "\n--- !u!4 &3\nTransform:\n  m_GameObject: {fileID: 1}\n  m_Father: {fileID: 0}\n--- !u!1 &2\nGameObject:\n  m_Component:\n  - component: {fileID: 4}\n  m_Name: "
                + secondName
                + "\n--- !u!4 &4\nTransform:\n  m_GameObject: {fileID: 2}\n  m_Father: {fileID: 0}\n";
            string[] documents = first.Split(
                new[] { "--- !u!" },
                StringSplitOptions.RemoveEmptyEntries
            );
            Array.Reverse(documents);
            string twin = "--- !u!" + string.Join("--- !u!", documents);
            twin = twin.Replace("&1\n", "&51\n")
                .Replace("&2\n", "&52\n")
                .Replace("&3\n", "&53\n")
                .Replace("&4\n", "&54\n")
                .Replace("{fileID: 1}", "{fileID: 51}")
                .Replace("{fileID: 2}", "{fileID: 52}")
                .Replace("{fileID: 3}", "{fileID: 53}")
                .Replace("{fileID: 4}", "{fileID: 54}");
            string canonical = SceneCanonicalizer.CanonicalizeYaml(first);
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(twin), Is.EqualTo(canonical));
            MatchCollection anchors = Regex.Matches(canonical, @"&([0-9]+)");
            Assert.That(anchors.Count, Is.EqualTo(4));
            Assert.That(canonical, Does.Contain("&639389634\nGameObject:"));
            Assert.That(canonical, Does.Contain("&639389635\nGameObject:"));
            Assert.That(anchors[0].Value, Is.Not.EqualTo(anchors[1].Value));
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(canonical), Is.EqualTo(canonical));
        }
    }
}
