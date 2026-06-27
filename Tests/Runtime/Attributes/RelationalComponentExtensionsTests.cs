// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentExtensionsTests : CommonTestBase
    {
        // Tracking handled by CommonTestBase

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
