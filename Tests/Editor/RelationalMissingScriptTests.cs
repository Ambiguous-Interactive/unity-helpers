// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [Category("Integration")]
    public sealed class RelationalMissingScriptTests : BatchedEditorTestBase
    {
        private const string Root = "Assets/Temp/RelationalMissingScriptTests";

        private static void ClearCaches()
        {
            SiblingComponentExtensions.ClearCachedFieldMetadata();
            ParentComponentExtensions.ClearCachedFieldMetadata();
            ChildComponentExtensions.ClearCachedFieldMetadata();
            ReflectionHelpers.ClearFieldGetterCache();
            ReflectionHelpers.ClearFieldSetterCache();
        }

        private static List<Component> LiveComponents(Component[] queried, Transform exclude)
        {
            List<Component> expected = new();
            foreach (Component candidate in queried)
            {
                if (candidate != null && candidate.transform != exclude)
                {
                    expected.Add(candidate);
                }
            }
            return expected;
        }

        private static void AssertShapes(
            List<Component> expected,
            Component[] array,
            List<Component> list,
            HashSet<Component> set,
            string label
        )
        {
            Assert.IsTrue(array != null, label);
            Assert.IsTrue(list != null, label);
            Assert.IsTrue(set != null, label);
            CollectionAssert.AreEqual(expected, array, label + ".Array");
            CollectionAssert.AreEqual(expected, list, label + ".List");
            CollectionAssert.AreEquivalent(expected, set, label + ".Set");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void MissingScriptsAreExcludedBeforeCollectionLimits(int capability)
        {
            using IDisposable scope = new ReflectionCapabilityScope(
                capability == 0,
                capability == 1
            );
            ClearCaches();
            try
            {
                ExecuteWithImmediateImport(() =>
                {
                    EnsureFolder(Root);
                    GameObject root = CreateNode("Root", null);
                    GameObject owner = CreateNode("Owner", root.transform);
                    owner.AddComponent<RelationalMissingScriptTarget>();
                    CreateNode("Child", owner.transform);
                    string guid = AssetDatabase.AssetPathToGUID(
                        AssetDatabase.GetAssetPath(
                            MonoScript.FromMonoBehaviour(root.GetComponent<AssignmentComponent>())
                        )
                    );
                    Assert.IsNotEmpty(guid);
                    string path = Root + "/Missing.prefab";
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    TrackAssetPath(path);
                    string fullPath = Path.GetFullPath(
                        Path.Combine(Application.dataPath, "..", path)
                    );
                    string original = File.ReadAllText(fullPath);
                    string changed = original.Replace(
                        "guid: " + guid + ", type: 3",
                        "guid: ffffffffffffffffffffffffffffffff, type: 3"
                    );
                    Assert.AreNotEqual(original, changed);
                    File.WriteAllText(fullPath, changed);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    Assert.IsTrue(prefab != null);
                    RelationalMissingScriptTarget target = prefab
                        .transform.GetChild(0)
                        .GetComponent<RelationalMissingScriptTarget>();
                    Assert.IsTrue(target != null);
                    Component[] raw = target.GetComponents<Component>();
                    Assert.IsTrue(
                        Array.Exists(raw, static candidate => candidate == null),
                        "The fixture must include a real missing-script query entry."
                    );
                    List<Component> siblings = LiveComponents(raw, null);
                    List<Component> parents = LiveComponents(
                        target.transform.parent.GetComponentsInParent<Component>(true),
                        null
                    );
                    List<Component> children = LiveComponents(
                        target.GetComponentsInChildren<Component>(true),
                        target.transform
                    );
                    target.AssignRelationalComponents();
                    AssertShapes(
                        siblings,
                        target.siblingAllArray,
                        target.siblingAllList,
                        target.siblingAllSet,
                        "siblingAll"
                    );
                    AssertShapes(
                        siblings,
                        target.siblingFilteredArray,
                        target.siblingFilteredList,
                        target.siblingFilteredSet,
                        "siblingFiltered"
                    );
                    AssertShapes(
                        siblings.GetRange(0, Math.Min(2, siblings.Count)),
                        target.siblingBoundedArray,
                        target.siblingBoundedList,
                        target.siblingBoundedSet,
                        "siblingBounded"
                    );
                    AssertShapes(
                        siblings.GetRange(0, Math.Min(2, siblings.Count)),
                        target.siblingFilteredBoundedArray,
                        target.siblingFilteredBoundedList,
                        target.siblingFilteredBoundedSet,
                        "siblingFilteredBounded"
                    );
                    AssertShapes(
                        parents,
                        target.parentAllArray,
                        target.parentAllList,
                        target.parentAllSet,
                        "parentAll"
                    );
                    AssertShapes(
                        parents,
                        target.parentFilteredArray,
                        target.parentFilteredList,
                        target.parentFilteredSet,
                        "parentFiltered"
                    );
                    AssertShapes(
                        parents.GetRange(0, Math.Min(2, parents.Count)),
                        target.parentBoundedArray,
                        target.parentBoundedList,
                        target.parentBoundedSet,
                        "parentBounded"
                    );
                    AssertShapes(
                        parents.GetRange(0, Math.Min(2, parents.Count)),
                        target.parentFilteredBoundedArray,
                        target.parentFilteredBoundedList,
                        target.parentFilteredBoundedSet,
                        "parentFilteredBounded"
                    );
                    AssertShapes(
                        children,
                        target.childAllArray,
                        target.childAllList,
                        target.childAllSet,
                        "childAll"
                    );
                    AssertShapes(
                        children,
                        target.childFilteredArray,
                        target.childFilteredList,
                        target.childFilteredSet,
                        "childFiltered"
                    );
                    AssertShapes(
                        children.GetRange(0, Math.Min(2, children.Count)),
                        target.childBoundedArray,
                        target.childBoundedList,
                        target.childBoundedSet,
                        "childBounded"
                    );
                    AssertShapes(
                        children.GetRange(0, Math.Min(2, children.Count)),
                        target.childFilteredBoundedArray,
                        target.childFilteredBoundedList,
                        target.childFilteredBoundedSet,
                        "childFilteredBounded"
                    );
                });
            }
            finally
            {
                ClearCaches();
            }
        }

        private GameObject CreateNode(string name, Transform parent)
        {
            GameObject node = Track(new GameObject(name));
            node.transform.SetParent(parent);
            node.AddComponent<AssignmentComponent>();
            node.AddComponent<BoxCollider>();
            return node;
        }
    }
#endif
}
