// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    [TestFixture]
    public sealed class SceneCanonicalizerTests
    {
        private const string Header = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n";
        private const string LocalRefPrefix = "{fileID: ";
        private static readonly string BaseScene = Scene(
            "--- !u!29 &100",
            "OcclusionCullingSettings:",
            "  m_ObjectHideFlags: 0",
            "  serializedVersion: 2",
            "  m_OcclusionBakeSettings:",
            "    smallestOccluder: 5",
            "  m_SceneGUID: 00000000000000000000000000000000",
            "  m_OcclusionCullingData: {fileID: 0}",
            "--- !u!104 &110",
            "RenderSettings:",
            "  m_ObjectHideFlags: 0",
            "  serializedVersion: 10",
            "  m_Fog: 0",
            "  m_Sun: {fileID: 0}",
            "--- !u!1 &120",
            "GameObject:",
            "  m_ObjectHideFlags: 0",
            "  m_CorrespondingSourceObject: {fileID: 0}",
            "  m_PrefabInstance: {fileID: 0}",
            "  m_PrefabAsset: {fileID: 0}",
            "  serializedVersion: 6",
            "  m_Component:",
            "  - component: {fileID: 130}",
            "  - component: {fileID: 140}",
            "  - component: {fileID: 150}",
            "  m_Layer: 0",
            "  m_Name: Level",
            "  m_TagString: Untagged",
            "  m_IsActive: 1",
            "--- !u!4 &130",
            "Transform:",
            "  m_ObjectHideFlags: 0",
            "  m_CorrespondingSourceObject: {fileID: 0}",
            "  m_GameObject: {fileID: 120}",
            "  serializedVersion: 2",
            "  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}",
            "  m_LocalPosition: {x: 0, y: 0, z: 0}",
            "  m_LocalScale: {x: 1, y: 1, z: 1}",
            "  m_Children:",
            "  - {fileID: 170}",
            "  - {fileID: 200}",
            "  m_Father: {fileID: 0}",
            "--- !u!114 &140",
            "MonoBehaviour:",
            "  m_ObjectHideFlags: 0",
            "  m_GameObject: {fileID: 120}",
            "  m_Enabled: 1",
            "  m_Script: {fileID: 11500000, guid: cc33cc33cc33cc33cc33cc33cc33cc33, type: 3}",
            "  m_Name: ",
            "  _value: 1",
            "--- !u!114 &150",
            "MonoBehaviour:",
            "  m_ObjectHideFlags: 0",
            "  m_GameObject: {fileID: 120}",
            "  m_Enabled: 1",
            "  m_Script: {fileID: 11500000, guid: cc33cc33cc33cc33cc33cc33cc33cc33, type: 3}",
            "  m_Name: ",
            "  _value: 2",
            "--- !u!1 &160",
            "GameObject:",
            "  m_ObjectHideFlags: 0",
            "  m_CorrespondingSourceObject: {fileID: 0}",
            "  m_PrefabInstance: {fileID: 0}",
            "  m_PrefabAsset: {fileID: 0}",
            "  serializedVersion: 6",
            "  m_Component:",
            "  - component: {fileID: 170}",
            "  - component: {fileID: 180}",
            "  m_Layer: 0",
            "  m_Name: Tile",
            "  m_IsActive: 1",
            "--- !u!4 &170",
            "Transform:",
            "  m_ObjectHideFlags: 0",
            "  m_GameObject: {fileID: 160}",
            "  serializedVersion: 2",
            "  m_LocalPosition: {x: 0, y: 0, z: 0}",
            "  m_Children: []",
            "  m_Father: {fileID: 130}",
            "--- !u!212 &180",
            "SpriteRenderer:",
            "  m_ObjectHideFlags: 0",
            "  m_GameObject: {fileID: 160}",
            "  m_Enabled: 1",
            "  m_Sprite: {fileID: 21300000, guid: a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1, type: 3}",
            "--- !u!1 &190",
            "GameObject:",
            "  m_ObjectHideFlags: 0",
            "  m_CorrespondingSourceObject: {fileID: 0}",
            "  m_PrefabInstance: {fileID: 0}",
            "  m_PrefabAsset: {fileID: 0}",
            "  serializedVersion: 6",
            "  m_Component:",
            "  - component: {fileID: 200}",
            "  - component: {fileID: 210}",
            "  m_Layer: 0",
            "  m_Name: Tile",
            "  m_IsActive: 1",
            "--- !u!4 &200",
            "Transform:",
            "  m_ObjectHideFlags: 0",
            "  m_GameObject: {fileID: 190}",
            "  serializedVersion: 2",
            "  m_LocalPosition: {x: 0, y: 0, z: 0}",
            "  m_Children: []",
            "  m_Father: {fileID: 130}",
            "--- !u!212 &210",
            "SpriteRenderer:",
            "  m_ObjectHideFlags: 0",
            "  m_GameObject: {fileID: 190}",
            "  m_Enabled: 1",
            "  m_Sprite: {fileID: 21300000, guid: b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2, type: 3}",
            "--- !u!1001 &220",
            "PrefabInstance:",
            "  m_ObjectHideFlags: 0",
            "  serializedVersion: 2",
            "  m_Modification:",
            "    serializedVersion: 3",
            "    m_TransformParent: {fileID: 0}",
            "    m_Modifications:",
            "    - target: {fileID: 5000, guid: dd44dd44dd44dd44dd44dd44dd44dd44, type: 3}",
            "      propertyPath: m_Name",
            "      value: Hero",
            "      objectReference: {fileID: 0}",
            "    m_RemovedComponents: []",
            "  m_SourcePrefab: {fileID: 100100000, guid: dd44dd44dd44dd44dd44dd44dd44dd44, type: 3}",
            "--- !u!1660057539 &230",
            "SceneRoots:",
            "  m_ObjectHideFlags: 0",
            "  m_Roots:",
            "  - {fileID: 130}",
            "  - {fileID: 220}"
        );
        private static readonly string TwinScene = Scene(
            "--- !u!1 &900063",
            "GameObject:",
            "  m_ObjectHideFlags: 0",
            "  m_CorrespondingSourceObject: {fileID: 0}",
            "  m_PrefabInstance: {fileID: 0}",
            "  m_PrefabAsset: {fileID: 0}",
            "  serializedVersion: 6",
            "  m_Component:",
            "  - component: {fileID: 900070}",
            "  - component: {fileID: 900077}",
            "  m_Layer: 0",
            "  m_Name: Tile",
            "  m_IsActive: 1",
            "--- !u!1660057539 &900091",
            "SceneRoots:",
            "  m_ObjectHideFlags: 0",
            "  m_Roots:",
            "  - {fileID: 900021}",
            "  - {fileID: 900084}",
            "--- !u!1001 &900084",
            "PrefabInstance:",
            "  m_ObjectHideFlags: 0",
            "  serializedVersion: 2",
            "  m_Modification:",
            "    serializedVersion: 3",
            "    m_TransformParent: {fileID: 0}",
            "    m_Modifications:",
            "    - target: {fileID: 5000, guid: dd44dd44dd44dd44dd44dd44dd44dd44, type: 3}",
            "      propertyPath: m_Name",
            "      value: Hero",
            "      objectReference: {fileID: 0}",
            "    m_RemovedComponents: []",
            "  m_SourcePrefab: {fileID: 100100000, guid: dd44dd44dd44dd44dd44dd44dd44dd44, type: 3}",
            "--- !u!1 &900014",
            "GameObject:",
            "  m_ObjectHideFlags: 0",
            "  m_CorrespondingSourceObject: {fileID: 0}",
            "  m_PrefabInstance: {fileID: 0}",
            "  m_PrefabAsset: {fileID: 0}",
            "  serializedVersion: 6",
            "  m_Component:",
            "  - component: {fileID: 900021}",
            "  - component: {fileID: 900028}",
            "  - component: {fileID: 900035}",
            "  m_Layer: 0",
            "  m_Name: Level",
            "  m_TagString: Untagged",
            "  m_IsActive: 1",
            "--- !u!104 &900007",
            "RenderSettings:",
            "  m_ObjectHideFlags: 0",
            "  serializedVersion: 10",
            "  m_Fog: 0",
            "  m_Sun: {fileID: 0}",
            "--- !u!212 &900056",
            "SpriteRenderer:",
            "  m_ObjectHideFlags: 0",
            "  m_GameObject: {fileID: 900042}",
            "  m_Enabled: 1",
            "  m_Sprite: {fileID: 21300000, guid: a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1, type: 3}",
            "--- !u!4 &900049",
            "Transform:",
            "  m_ObjectHideFlags: 0",
            "  m_GameObject: {fileID: 900042}",
            "  serializedVersion: 2",
            "  m_LocalPosition: {x: 0, y: 0, z: 0}",
            "  m_Children: []",
            "  m_Father: {fileID: 900021}",
            "--- !u!4 &900070",
            "Transform:",
            "  m_ObjectHideFlags: 0",
            "  m_GameObject: {fileID: 900063}",
            "  serializedVersion: 2",
            "  m_LocalPosition: {x: 0, y: 0, z: 0}",
            "  m_Children: []",
            "  m_Father: {fileID: 900021}",
            "--- !u!4 &900021",
            "Transform:",
            "  m_ObjectHideFlags: 0",
            "  m_CorrespondingSourceObject: {fileID: 0}",
            "  m_GameObject: {fileID: 900014}",
            "  serializedVersion: 2",
            "  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}",
            "  m_LocalPosition: {x: 0, y: 0, z: 0}",
            "  m_LocalScale: {x: 1, y: 1, z: 1}",
            "  m_Children:",
            "  - {fileID: 900049}",
            "  - {fileID: 900070}",
            "  m_Father: {fileID: 0}",
            "--- !u!114 &900035",
            "MonoBehaviour:",
            "  m_ObjectHideFlags: 0",
            "  m_GameObject: {fileID: 900014}",
            "  m_Enabled: 1",
            "  m_Script: {fileID: 11500000, guid: cc33cc33cc33cc33cc33cc33cc33cc33, type: 3}",
            "  m_Name: ",
            "  _value: 2",
            "--- !u!114 &900028",
            "MonoBehaviour:",
            "  m_ObjectHideFlags: 0",
            "  m_GameObject: {fileID: 900014}",
            "  m_Enabled: 1",
            "  m_Script: {fileID: 11500000, guid: cc33cc33cc33cc33cc33cc33cc33cc33, type: 3}",
            "  m_Name: ",
            "  _value: 1",
            "--- !u!29 &900000",
            "OcclusionCullingSettings:",
            "  m_ObjectHideFlags: 0",
            "  serializedVersion: 2",
            "  m_OcclusionBakeSettings:",
            "    smallestOccluder: 5",
            "  m_SceneGUID: 00000000000000000000000000000000",
            "  m_OcclusionCullingData: {fileID: 0}",
            "--- !u!212 &900077",
            "SpriteRenderer:",
            "  m_ObjectHideFlags: 0",
            "  m_GameObject: {fileID: 900063}",
            "  m_Enabled: 1",
            "  m_Sprite: {fileID: 21300000, guid: b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2, type: 3}",
            "--- !u!1 &900042",
            "GameObject:",
            "  m_ObjectHideFlags: 0",
            "  m_CorrespondingSourceObject: {fileID: 0}",
            "  m_PrefabInstance: {fileID: 0}",
            "  m_PrefabAsset: {fileID: 0}",
            "  serializedVersion: 6",
            "  m_Component:",
            "  - component: {fileID: 900049}",
            "  - component: {fileID: 900056}",
            "  m_Layer: 0",
            "  m_Name: Tile",
            "  m_IsActive: 1"
        );

        private static readonly string SingletonScene = Scene(
            "--- !u!29 &1",
            "OcclusionCullingSettings:",
            "  m_ObjectHideFlags: 0",
            "  m_OcclusionCullingData: {fileID: 0}",
            "--- !u!104 &2",
            "RenderSettings:",
            "  m_ObjectHideFlags: 0",
            "  m_Sun: {fileID: 0}",
            "--- !u!157 &3",
            "LightmapSettings:",
            "  m_ObjectHideFlags: 0",
            "  m_LightingSettings: {fileID: 0}"
        );

        private static IEnumerable<TestCaseData> Scenes()
        {
            yield return new TestCaseData(BaseScene).SetName("{m}.BaseScene");
            yield return new TestCaseData(TwinScene).SetName("{m}.TwinScene");
            yield return new TestCaseData(SingletonScene).SetName("{m}.SingletonScene");
        }

        private static string Scene(params string[] lines)
        {
            return Header + string.Join("\n", lines) + "\n";
        }

        private static List<string> Documents(string yaml)
        {
            List<int> starts = new List<int>();
            if (yaml.StartsWith("--- !u!", StringComparison.Ordinal))
            {
                starts.Add(0);
            }

            int index = 0;
            while (true)
            {
                int found = yaml.IndexOf("\n--- !u!", index, StringComparison.Ordinal);
                if (found < 0)
                {
                    break;
                }

                starts.Add(found + 1);
                index = found + 1;
            }

            List<string> documents = new List<string>(starts.Count);
            for (int i = 0; i < starts.Count; ++i)
            {
                int start = starts[i];
                int end = i + 1 < starts.Count ? starts[i + 1] : yaml.Length;
                documents.Add(yaml.Substring(start, end - start));
            }

            return documents;
        }

        private static List<string> NormalizedDocuments(string yaml)
        {
            List<string> normalized = new List<string>();
            foreach (string document in Documents(yaml))
            {
                int ampersand = document.IndexOf('&');
                int start = ampersand + 1;
                int end = start;
                while (end < document.Length && IsDigit(document[end]))
                {
                    ++end;
                }

                string withAnchor = document.Substring(0, start) + "A" + document.Substring(end);
                normalized.Add(TokenizeLocalReferences(withAnchor));
            }

            normalized.Sort(StringComparer.Ordinal);
            return normalized;
        }

        private static string TokenizeLocalReferences(string text)
        {
            List<long> ignored = new List<long>();
            return ScanLocalReferences(text, ignored, true);
        }

        private static List<long> LocalReferences(string yaml)
        {
            List<long> references = new List<long>();
            ScanLocalReferences(yaml, references, false);
            return references;
        }

        private static string ScanLocalReferences(string text, List<long> sink, bool tokenize)
        {
            System.Text.StringBuilder builder = tokenize
                ? new System.Text.StringBuilder(text.Length)
                : null;
            int cursor = 0;
            int index = text.IndexOf(LocalRefPrefix, StringComparison.Ordinal);
            while (0 <= index)
            {
                int numberStart = index + LocalRefPrefix.Length;
                int pointer = numberStart;
                bool negative = pointer < text.Length && text[pointer] == '-';
                if (negative)
                {
                    ++pointer;
                }

                int digitsStart = pointer;
                while (pointer < text.Length && IsDigit(text[pointer]))
                {
                    ++pointer;
                }

                bool isLocal =
                    digitsStart < pointer && pointer < text.Length && text[pointer] == '}';
                if (isLocal)
                {
                    long value = long.Parse(text.Substring(digitsStart, pointer - digitsStart));
                    long signed = negative ? -value : value;
                    if (sink != null)
                    {
                        sink.Add(signed);
                    }

                    if (tokenize)
                    {
                        builder.Append(text, cursor, index - cursor).Append("{L}");
                        cursor = pointer + 1;
                    }
                }

                index = text.IndexOf(LocalRefPrefix, numberStart, StringComparison.Ordinal);
            }

            if (!tokenize)
            {
                return text;
            }

            builder.Append(text, cursor, text.Length - cursor);
            return builder.ToString();
        }

        private static List<string> ExternalReferences(string yaml)
        {
            List<string> references = new List<string>();
            int index = yaml.IndexOf(LocalRefPrefix, StringComparison.Ordinal);
            while (0 <= index)
            {
                int end = yaml.IndexOf('}', index);
                if (0 <= end)
                {
                    string token = yaml.Substring(index, end - index + 1);
                    if (0 <= token.IndexOf("guid:", StringComparison.Ordinal))
                    {
                        references.Add(token);
                    }
                }

                index = yaml.IndexOf(
                    LocalRefPrefix,
                    index + LocalRefPrefix.Length,
                    StringComparison.Ordinal
                );
            }

            references.Sort(StringComparer.Ordinal);
            return references;
        }

        private static long AnchorId(string document)
        {
            int ampersand = document.IndexOf('&');
            int pointer = ampersand + 1;
            int start = pointer;
            while (pointer < document.Length && IsDigit(document[pointer]))
            {
                ++pointer;
            }

            return start < pointer ? long.Parse(document.Substring(start, pointer - start)) : 0;
        }

        private static bool IsDigit(char value)
        {
            return '0' <= value && value <= '9';
        }

        [Test]
        public void ShuffledTwinsCanonicalizeIdentically()
        {
            Assert.AreEqual(
                SceneCanonicalizer.CanonicalizeYaml(BaseScene),
                SceneCanonicalizer.CanonicalizeYaml(TwinScene)
            );
        }

        [Test]
        public void ShuffledTwinDiffersFromBaseBeforeCanonicalization()
        {
            Assert.AreNotEqual(BaseScene, TwinScene);
        }

        [TestCaseSource(nameof(Scenes))]
        public void CanonicalizationIsIdempotent(string scene)
        {
            string once = SceneCanonicalizer.CanonicalizeYaml(scene);
            string twice = SceneCanonicalizer.CanonicalizeYaml(once);
            Assert.AreEqual(once, twice);
        }

        [TestCaseSource(nameof(Scenes))]
        public void CanonicalizationPreservesContent(string scene)
        {
            string canonical = SceneCanonicalizer.CanonicalizeYaml(scene);
            List<string> before = NormalizedDocuments(scene);
            List<string> after = NormalizedDocuments(canonical);
            CollectionAssert.AreEqual(before, after);
        }

        [TestCaseSource(nameof(Scenes))]
        public void CanonicalizationPreservesReferences(string scene)
        {
            string canonical = SceneCanonicalizer.CanonicalizeYaml(scene);

            HashSet<long> anchors = new HashSet<long>();
            foreach (string document in Documents(canonical))
            {
                anchors.Add(AnchorId(document));
            }

            List<long> localReferences = LocalReferences(canonical);
            foreach (long reference in localReferences)
            {
                if (reference != 0)
                {
                    Assert.IsTrue(
                        anchors.Contains(reference),
                        "Dangling local reference {0}",
                        reference
                    );
                }
            }

            Assert.AreEqual(LocalReferences(scene).Count, localReferences.Count);
            CollectionAssert.AreEqual(ExternalReferences(scene), ExternalReferences(canonical));
        }

        [Test]
        public void EmptyInputIsReturnedUnchanged()
        {
            Assert.AreEqual(string.Empty, SceneCanonicalizer.CanonicalizeYaml(string.Empty));
        }
    }
}
