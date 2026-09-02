// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    /// <summary>
    /// Relational fields declared privately on a base class must bind on every subclass. Reflection
    /// keyed on the most derived type never returns an inherited private field, so such a field
    /// stayed null with no log at all -- the discovery that never happened is also the reason the
    /// missing-component error never fired.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentInheritanceTests : CommonTestBase
    {
        [Test]
        public void AssignRelationalComponentsBindsPrivateFieldsDeclaredOnBaseClass()
        {
            RelationalInheritanceDerived tester = BuildInheritanceHierarchy(
                out BoxCollider sibling,
                out SpriteRenderer childRenderer,
                out Rigidbody parentBody
            );

            tester.AssignRelationalComponents();

            Assert.AreSame(sibling, tester.Sibling);
            Assert.AreSame(childRenderer, tester.Child);
            Assert.AreSame(parentBody, tester.Parent);
        }

        [Test]
        public void RelationalComponentAssignerBindsWhatItReportsItHas()
        {
            RelationalComponentAssigner assigner = new();

            Assert.IsTrue(assigner.HasRelationalAssignments(typeof(RelationalInheritanceDerived)));

            RelationalInheritanceDerived tester = BuildInheritanceHierarchy(
                out BoxCollider sibling,
                out SpriteRenderer childRenderer,
                out Rigidbody parentBody
            );

            assigner.Assign(tester);

            Assert.AreSame(sibling, tester.Sibling);
            Assert.AreSame(childRenderer, tester.Child);
            Assert.AreSame(parentBody, tester.Parent);
        }

        [Test]
        public void AssignRelationalComponentsLogsMissingComponentForBaseDeclaredField()
        {
            GameObject root = Track(
                new GameObject(
                    "RelationalInheritanceMissing",
                    typeof(RelationalInheritanceMissingDerived)
                )
            );
            RelationalInheritanceMissingDerived tester =
                root.GetComponent<RelationalInheritanceMissingDerived>();

            ExpectMissingRelationalComponentError(
                "RelationalInheritanceMissing",
                nameof(RelationalInheritanceMissingDerived),
                "sibling",
                "UnityEngine.Rigidbody2D",
                "_required"
            );

            tester.AssignRelationalComponents();

            Assert.IsTrue(tester.Required == null);
        }

        [Test]
        public void ShadowedRelationalFieldNameBindsOnlyTheMostDerivedDeclaration()
        {
            GameObject root = Track(new GameObject("RelationalShadow"));
            BoxCollider sibling = root.AddComponent<BoxCollider>();
            RelationalShadowDerived tester = root.AddComponent<RelationalShadowDerived>();

            tester.AssignRelationalComponents();

            Assert.AreSame(sibling, tester.DerivedCollider);
            Assert.IsTrue(tester.BaseCollider == null);
        }

        private RelationalInheritanceDerived BuildInheritanceHierarchy(
            out BoxCollider sibling,
            out SpriteRenderer childRenderer,
            out Rigidbody parentBody
        )
        {
            GameObject parent = Track(new GameObject("RelationalInheritanceParent"));
            parentBody = parent.AddComponent<Rigidbody>();

            GameObject root = Track(new GameObject("RelationalInheritance"));
            root.transform.SetParent(parent.transform);
            sibling = root.AddComponent<BoxCollider>();

            GameObject child = Track(new GameObject("RelationalInheritanceChild"));
            child.transform.SetParent(root.transform);
            childRenderer = child.AddComponent<SpriteRenderer>();

            return root.AddComponent<RelationalInheritanceDerived>();
        }
    }
}
