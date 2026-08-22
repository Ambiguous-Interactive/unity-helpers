// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System.Collections.Generic;
    using Components;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// The child and parent collection queries run through a generic closed over the field's element
    /// type at run time, and fall back to the non-generic query when a runtime refuses that. Without
    /// these the fallback is only ever reached on a platform no test runs on, so a divergence between
    /// the two would ship.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentCollectorTests : CommonTestBase
    {
        [TearDown]
        public void ResetCollectorMode()
        {
            RelationalComponentCollector.FallbackOnly = false;
        }

        [Test]
        [TestCase(1, 1)]
        [TestCase(1, 3)]
        [TestCase(3, 1)]
        [TestCase(4, 3)]
        public void ChildAssignmentMatchesNonGenericFallback(int depth, int breadth)
        {
            ExpectChildSpriteRenderers subject = BuildChildHierarchy(depth, breadth);

            RelationalComponentCollector.FallbackOnly = false;
            subject.AssignChildComponents();
            SpriteRenderer[] typedInclusiveArray = subject.inclusiveChildrenArray;
            SpriteRenderer[] typedExclusiveArray = subject.exclusiveChildrenArray;
            List<SpriteRenderer> typedInclusiveList = new(subject.inclusiveChildrenList);
            List<SpriteRenderer> typedExclusiveList = new(subject.exclusiveChildrenList);
            SpriteRenderer typedInclusiveSingle = subject.inclusiveChild;

            ClearChildFields(subject);

            RelationalComponentCollector.FallbackOnly = true;
            subject.AssignChildComponents();

            Assert.IsTrue(typedInclusiveArray != null);
            Assert.Less(0, typedExclusiveArray.Length);
            CollectionAssert.AreEqual(typedInclusiveArray, subject.inclusiveChildrenArray);
            CollectionAssert.AreEqual(typedExclusiveArray, subject.exclusiveChildrenArray);
            CollectionAssert.AreEqual(typedInclusiveList, subject.inclusiveChildrenList);
            CollectionAssert.AreEqual(typedExclusiveList, subject.exclusiveChildrenList);
            Assert.AreEqual(typedInclusiveSingle, subject.inclusiveChild);
        }

        [Test]
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        public void ParentAssignmentMatchesNonGenericFallback(int depth)
        {
            ExpectParentSpriteRenderers subject = BuildParentHierarchy(depth);

            RelationalComponentCollector.FallbackOnly = false;
            subject.AssignParentComponents();
            SpriteRenderer[] typedInclusiveArray = subject.inclusiveParentArray;
            SpriteRenderer[] typedExclusiveArray = subject.exclusiveParentArray;
            List<SpriteRenderer> typedInclusiveList = new(subject.inclusiveParentList);
            List<SpriteRenderer> typedExclusiveList = new(subject.exclusiveParentList);

            ClearParentFields(subject);

            RelationalComponentCollector.FallbackOnly = true;
            subject.AssignParentComponents();

            Assert.IsTrue(typedInclusiveArray != null);
            Assert.Less(0, typedInclusiveArray.Length);
            CollectionAssert.AreEqual(typedInclusiveArray, subject.inclusiveParentArray);
            CollectionAssert.AreEqual(typedExclusiveArray, subject.exclusiveParentArray);
            CollectionAssert.AreEqual(typedInclusiveList, subject.inclusiveParentList);
            CollectionAssert.AreEqual(typedExclusiveList, subject.exclusiveParentList);
        }

        /// <summary>
        /// A refusal is cached, so the very first use of an element type has to be the one that both
        /// builds the collector and proves it against a live component.
        /// </summary>
        [Test]
        public void CollectorRefusesElementTypesItCannotServe()
        {
            GameObject holder = Track(new GameObject("CollectorRefusal", typeof(SpriteRenderer)));
            Component probe = holder.GetComponent<SpriteRenderer>();

            Assert.IsTrue(RelationalComponentCollector.For(null, probe) == null);
            Assert.IsTrue(
                RelationalComponentCollector.For(typeof(System.IDisposable), probe) == null
            );
            Assert.IsTrue(RelationalComponentCollector.For(typeof(string), probe) == null);
            Assert.IsTrue(RelationalComponentCollector.For(typeof(SpriteRenderer), probe) != null);

            RelationalComponentCollector.FallbackOnly = true;
            Assert.IsTrue(RelationalComponentCollector.For(typeof(SpriteRenderer), probe) == null);
        }

        private ExpectChildSpriteRenderers BuildChildHierarchy(int depth, int breadth)
        {
            GameObject root = Track(
                new GameObject(
                    "CollectorRoot",
                    typeof(SpriteRenderer),
                    typeof(ExpectChildSpriteRenderers)
                )
            );
            Transform frontier = root.transform;
            for (int level = 0; level < depth; ++level)
            {
                Transform next = null;
                for (int sibling = 0; sibling < breadth; ++sibling)
                {
                    GameObject child = Track(
                        new GameObject($"CollectorChild{level}x{sibling}", typeof(SpriteRenderer))
                    );
                    child.transform.SetParent(frontier);
                    if (next == null)
                    {
                        next = child.transform;
                    }
                }

                frontier = next;
            }

            // These attributes default to IncludeInactive, and IncludeInactive=false forces the slow
            // path, so the fast path only ever runs with inactive objects INCLUDED. Both senses of
            // inactive are here -- a deactivated GameObject and a disabled behaviour -- because that
            // is where the two query overloads would diverge if they were going to.
            GameObject inactiveChild = Track(
                new GameObject("CollectorInactiveChild", typeof(SpriteRenderer))
            );
            inactiveChild.transform.SetParent(frontier);
            inactiveChild.SetActive(false);

            GameObject disabledChild = Track(
                new GameObject("CollectorDisabledChild", typeof(SpriteRenderer))
            );
            disabledChild.transform.SetParent(frontier);
            disabledChild.GetComponent<SpriteRenderer>().enabled = false;

            return root.GetComponent<ExpectChildSpriteRenderers>();
        }

        private ExpectParentSpriteRenderers BuildParentHierarchy(int depth)
        {
            GameObject root = Track(new GameObject("CollectorAncestor0", typeof(SpriteRenderer)));
            Transform frontier = root.transform;
            for (int level = 1; level < depth; ++level)
            {
                GameObject ancestor = Track(
                    new GameObject($"CollectorAncestor{level}", typeof(SpriteRenderer))
                );
                ancestor.transform.SetParent(frontier);
                frontier = ancestor.transform;
            }

            // One ancestor carries a disabled renderer, for the same reason the child hierarchy has
            // one: the fast path only ever runs with inactive components included.
            GameObject disabledAncestor = Track(
                new GameObject("CollectorDisabledAncestor", typeof(SpriteRenderer))
            );
            disabledAncestor.transform.SetParent(frontier);
            disabledAncestor.GetComponent<SpriteRenderer>().enabled = false;

            GameObject leaf = Track(
                new GameObject(
                    "CollectorLeaf",
                    typeof(SpriteRenderer),
                    typeof(ExpectParentSpriteRenderers)
                )
            );
            leaf.transform.SetParent(disabledAncestor.transform);
            return leaf.GetComponent<ExpectParentSpriteRenderers>();
        }

        private static void ClearChildFields(ExpectChildSpriteRenderers subject)
        {
            subject.inclusiveChildrenArray = null;
            subject.exclusiveChildrenArray = null;
            subject.inclusiveChildrenList = null;
            subject.exclusiveChildrenList = null;
            subject.inclusiveChild = null;
            subject.exclusiveChild = null;
        }

        private static void ClearParentFields(ExpectParentSpriteRenderers subject)
        {
            subject.inclusiveParentArray = null;
            subject.exclusiveParentArray = null;
            subject.inclusiveParentList = null;
            subject.exclusiveParentList = null;
        }
    }
}
