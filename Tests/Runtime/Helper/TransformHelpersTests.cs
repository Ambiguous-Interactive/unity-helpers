// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TransformHelpersTests : CommonTestBase
    {
        [TestCase(true, "Leaf,Middle,Root")]
        [TestCase(false, "Middle,Root")]
        public void IterateOverAllParentComponentsEnumeratorRespectsIncludeSelf(
            bool includeSelf,
            string expectedSequence
        )
        {
            (TransformProbe root, TransformProbe middle, TransformProbe leaf) = BuildHierarchy();
            TransformProbe start = leaf;

            List<string> ids = start
                .transform.IterateOverAllParentComponentsRecursively<TransformProbe>(includeSelf)
                .Select(component => component.Id)
                .ToList();

            string[] expected = expectedSequence.Split(',');
            CollectionAssert.AreEqual(expected, ids);
        }

        [TestCase(true, "Root,Middle,Leaf")]
        [TestCase(false, "Middle,Leaf")]
        public void IterateOverAllChildComponentsRecursivelyEnumeratorRespectsIncludeSelf(
            bool includeSelf,
            string expectedSequence
        )
        {
            (TransformProbe root, TransformProbe middle, TransformProbe leaf) = BuildHierarchy();

            List<string> ids = root
                .transform.IterateOverAllChildComponentsRecursively<TransformProbe>(includeSelf)
                .Select(component => component.Id)
                .ToList();

            string[] expected = expectedSequence.Split(',');
            CollectionAssert.AreEqual(expected, ids);
        }

        [Test]
        public void IterateOverAllChildrenRecursivelyBreadthFirstRespectsDepthLimit()
        {
            GameObject root = Track(new GameObject("Root"));
            GameObject child = Track(new GameObject("Child"));
            GameObject grandChild = Track(new GameObject("GrandChild"));

            child.transform.SetParent(root.transform);
            grandChild.transform.SetParent(child.transform);

            List<Transform> buffer = new();
            root.transform.IterateOverAllChildrenRecursivelyBreadthFirst(
                buffer,
                includeSelf: false,
                maxDepth: 1
            );

            CollectionAssert.AreEqual(new[] { child.transform }, buffer);
        }

        [Test]
        public void IterateOverAllChildrenRecursivelyBreadthFirstIncludesSelf()
        {
            GameObject root = Track(new GameObject("Root"));
            List<Transform> buffer = new();
            root.transform.IterateOverAllChildrenRecursivelyBreadthFirst(buffer, includeSelf: true);

            Assert.AreEqual(1, buffer.Count);
            Assert.AreSame(root.transform, buffer[0]);
        }

        [Test]
        public void IterateOverAllParentComponentsHandlesNullComponent()
        {
            List<Transform> buffer = new() { null };
            Transform result = null;

            IEnumerable<Transform> enumerable = result.IterateOverAllParents(includeSelf: true);
            Assert.IsFalse(enumerable.GetEnumerator().MoveNext());

            List<Transform> listResult = result.IterateOverAllParents(buffer, includeSelf: true);
            Assert.IsEmpty(listResult);
        }

        private (TransformProbe root, TransformProbe middle, TransformProbe leaf) BuildHierarchy()
        {
            TransformProbe root = Track(new GameObject("RootProbe", typeof(TransformProbe)))
                .GetComponent<TransformProbe>();
            root.Id = "Root";

            TransformProbe middle = Track(new GameObject("MiddleProbe", typeof(TransformProbe)))
                .GetComponent<TransformProbe>();
            middle.Id = "Middle";

            TransformProbe leaf = Track(new GameObject("LeafProbe", typeof(TransformProbe)))
                .GetComponent<TransformProbe>();
            leaf.Id = "Leaf";

            middle.transform.SetParent(root.transform);
            leaf.transform.SetParent(middle.transform);

            return (root, middle, leaf);
        }
    }
}
