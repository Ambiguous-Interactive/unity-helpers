// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentExtensionsTests : RelationalExactBindingTestBase
    {
        [Test]
        public void ExactTypeAssignmentMatchesDirectQueriesAcrossShapesAndCacheStates(
            [Values(1, 4)] int depth,
            [Values(false, true)] bool inactive,
            [Values(0, 1, 2)] int capability
        )
        {
            VerifyExactTypeAssignment(depth, inactive, capability);
        }

        [Test]
        public void AssignmentMatchesDirectQueriesAcrossCacheAndHierarchyChanges(
            [Values(1, 4)] int depth,
            [Values(false, true)] bool inactive,
            [Values(0, 1, 2)] int capability
        )
        {
            using IDisposable scope = ReflectionHelpers.OverrideReflectionCapabilities(
                capability == 0,
                capability == 1
            );
            SiblingComponentExtensions.ClearCachedFieldMetadata();
            ParentComponentExtensions.ClearCachedFieldMetadata();
            ChildComponentExtensions.ClearCachedFieldMetadata();
            ReflectionHelpers.ClearFieldSetterCache();

            GameObject root = Track(new GameObject("OracleRoot", typeof(Rigidbody)));
            root.GetComponent<Rigidbody>().isKinematic = true;
            GameObject ancestor = root;
            for (int level = 1; level < depth; level++)
            {
                GameObject next = Track(new GameObject("OracleAncestor"));
                next.transform.SetParent(ancestor.transform);
                ancestor = next;
            }

            GameObject owner = Track(
                new GameObject(
                    "OracleOwner",
                    typeof(RelationalComponentTester),
                    typeof(BoxCollider),
                    typeof(CapsuleCollider),
                    typeof(Rigidbody)
                )
            );
            owner.GetComponent<Rigidbody>().isKinematic = true;
            owner.transform.SetParent(ancestor.transform);
            GameObject child = Track(new GameObject("OracleFirstChild", typeof(CapsuleCollider)));
            child.transform.SetParent(owner.transform);
            GameObject otherChild = Track(
                new GameObject("OracleSecondChild", typeof(CapsuleCollider))
            );
            otherChild.transform.SetParent(owner.transform);
            root.SetActive(!inactive);
            RelationalComponentTester tester = owner.GetComponent<RelationalComponentTester>();

            AssertAssignmentMatchesDirectQueries(tester);
            AssertAssignmentMatchesDirectQueries(tester);

            GameObject unrelated = Track(
                new GameObject("OracleUnrelated", typeof(BoxCollider), typeof(CapsuleCollider))
            );
            unrelated.SetActive(!inactive);
            AssertAssignmentMatchesDirectQueries(tester);

            otherChild.transform.SetAsFirstSibling();
            AssertAssignmentMatchesDirectQueries(tester);
            Assert.AreSame(otherChild.GetComponent<CapsuleCollider>(), tester.childCollider);
        }

        private static void AssertAssignmentMatchesDirectQueries(RelationalComponentTester tester)
        {
            Rigidbody parent = tester.transform.parent.GetComponentInParent<Rigidbody>(true);
            BoxCollider sibling = tester.GetComponent<BoxCollider>();
            CapsuleCollider child = null;
            foreach (
                CapsuleCollider candidate in tester.GetComponentsInChildren<CapsuleCollider>(true)
            )
            {
                if (candidate.transform != tester.transform)
                {
                    child = candidate;
                    break;
                }
            }

            tester.AssignRelationalComponents();

            Assert.AreSame(parent, tester.parentBody);
            Assert.AreSame(sibling, tester.siblingCollider);
            Assert.AreSame(child, tester.childCollider);
        }

        [Test]
        public void AssignRelationalComponentsResolvesParentSiblingAndChild()
        {
            GameObject parent = Track(new GameObject("RelationalParent", typeof(Rigidbody)));
            Rigidbody parentBody = parent.GetComponent<Rigidbody>();

            GameObject middle = new(
                "RelationalMiddle",
                typeof(BoxCollider),
                typeof(RelationalComponentTester)
            );
            middle = Track(middle);
            middle.transform.SetParent(parent.transform);
            BoxCollider siblingCollider = middle.GetComponent<BoxCollider>();

            GameObject child = Track(new GameObject("RelationalChild", typeof(CapsuleCollider)));
            child.transform.SetParent(middle.transform);
            CapsuleCollider childCollider = child.GetComponent<CapsuleCollider>();

            RelationalComponentTester tester = middle.GetComponent<RelationalComponentTester>();
            tester.AssignRelationalComponents();

            Assert.AreSame(parentBody, tester.parentBody);
            Assert.AreSame(siblingCollider, tester.siblingCollider);
            Assert.AreSame(childCollider, tester.childCollider);

            return;
        }
    }
}
