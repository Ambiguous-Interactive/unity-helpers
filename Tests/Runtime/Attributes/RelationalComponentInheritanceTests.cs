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

        [Test]
        public void PrivateFieldsSharingANameAcrossTheChainAreBothBound()
        {
            GameObject root = Track(new GameObject("RelationalPrivateName"));
            BoxCollider sibling = root.AddComponent<BoxCollider>();
            RelationalPrivateNameDerived tester = root.AddComponent<RelationalPrivateNameDerived>();

            tester.AssignRelationalComponents();

            // Private base fields are distinct from same-named derived fields; both must bind independently.
            Assert.AreSame(sibling, tester.DerivedCollider);
            Assert.AreSame(sibling, tester.BaseCollider);
        }

        [Test]
        public void ABaseRelationalFieldBindsWhenADerivedFieldReusesItsNameWithoutTheAttribute()
        {
            GameObject root = Track(new GameObject("RelationalPrivateNameUnrelated"));
            BoxCollider sibling = root.AddComponent<BoxCollider>();
            RelationalPrivateNameUnrelatedDerived tester =
                root.AddComponent<RelationalPrivateNameUnrelatedDerived>();

            tester.AssignRelationalComponents();

            /*
                The name-keyed cache cannot distinguish these declarations; only the live field attribute
                identifies which should bind.
            */
            Assert.AreSame(sibling, tester.BaseCollider);
            Assert.IsTrue(tester.DerivedCollider == null);
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
