// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentTagAndNameFilterEdgeTests : CommonTestBase
    {
        [Test]
        public void IncludeInactiveExcludesDisabledAndInactive()
        {
            GameObject root = Track(new GameObject("InactiveRoot", typeof(IncludeInactiveTester)));
            IncludeInactiveTester tester = root.GetComponent<IncludeInactiveTester>();

            GameObject activeChild = Track(new GameObject("ActiveChild", typeof(SpriteRenderer)));
            activeChild.tag = "Player";
            activeChild.transform.SetParent(root.transform);

            GameObject inactiveChild = Track(
                new GameObject("InactiveChild", typeof(SpriteRenderer))
            );
            inactiveChild.tag = "Player";
            inactiveChild.transform.SetParent(root.transform);
            inactiveChild.SetActive(false);

            GameObject disabledChild = Track(
                new GameObject("DisabledChild", typeof(SpriteRenderer))
            );
            disabledChild.tag = "Player";
            disabledChild.transform.SetParent(root.transform);
            disabledChild.GetComponent<SpriteRenderer>().enabled = false;

            tester.AssignChildComponents();

            Assert.AreEqual(1, tester.onlyActivePlayers.Count);
            Assert.AreSame(activeChild.GetComponent<SpriteRenderer>(), tester.onlyActivePlayers[0]);

            return;
        }

        [Test]
        public void CombinedTagAndNameFilterRequiresBoth()
        {
            GameObject root = Track(new GameObject("Root", typeof(CombinedFilterTester)));
            CombinedFilterTester tester = root.GetComponent<CombinedFilterTester>();

            GameObject playerWrongName = Track(new GameObject("EnemyOne", typeof(SpriteRenderer)));
            playerWrongName.tag = "Player";
            playerWrongName.transform.SetParent(root.transform);

            GameObject wrongTagRightName = Track(
                new GameObject("PlayerOne", typeof(SpriteRenderer))
            );
            wrongTagRightName.tag = "Untagged";
            wrongTagRightName.transform.SetParent(root.transform);

            GameObject correct = Track(new GameObject("PlayerAlpha", typeof(SpriteRenderer)));
            correct.tag = "Player";
            correct.transform.SetParent(root.transform);

            tester.AssignChildComponents();

            Assert.AreEqual(1, tester.matched.Count);
            Assert.AreSame(correct.GetComponent<SpriteRenderer>(), tester.matched[0]);

            return;
        }

        [Test]
        public void TagFilterMatchesUntagged()
        {
            GameObject root = Track(new GameObject("Root", typeof(UntaggedFilterTester)));
            UntaggedFilterTester tester = root.GetComponent<UntaggedFilterTester>();

            GameObject child1 = Track(new GameObject("Child1", typeof(SpriteRenderer)));
            child1.tag = "Untagged";
            child1.transform.SetParent(root.transform);

            GameObject child2 = Track(new GameObject("Child2", typeof(SpriteRenderer)));
            child2.tag = "Player";
            child2.transform.SetParent(root.transform);

            tester.AssignChildComponents();

            Assert.AreEqual(1, tester.untagged.Count);
            Assert.AreSame(child1.GetComponent<SpriteRenderer>(), tester.untagged[0]);

            return;
        }

        [Test]
        public void OnlyDescendantsIncludesSelfWhenFalse()
        {
            GameObject root = Track(
                new GameObject("SelfRoot", typeof(SelfInclusionTester), typeof(SpriteRenderer))
            );
            SelfInclusionTester tester = root.GetComponent<SelfInclusionTester>();

            tester.AssignChildComponents();

            Assert.IsTrue(tester.selfRenderer != null);
            Assert.AreSame(root.GetComponent<SpriteRenderer>(), tester.selfRenderer);

            return;
        }

        [Test]
        public void AllowInterfacesFalseDisablesInterfaceResolution()
        {
            GameObject root = Track(
                new GameObject("InterfaceRoot", typeof(InterfacesDisabledTester))
            );
            InterfacesDisabledTester tester = root.GetComponent<InterfacesDisabledTester>();

            GameObject child = Track(new GameObject("Child", typeof(TestInterfaceComponent)));
            child.transform.SetParent(root.transform);

            // Emitted via the package logger, which is compiled out in a non-development player.
            ExpectWallstopLog(
                LogType.Error,
                new Regex(@"Unable to find child component of type .* for field 'iface'")
            );

            tester.AssignChildComponents();

            Assert.IsTrue((Object)tester.iface == null);
            return;
        }

        [Test]
        public void OptionalSuppressesMissingErrors()
        {
            GameObject root = Track(new GameObject("OptionalRoot", typeof(OptionalTester)));
            OptionalTester tester = root.GetComponent<OptionalTester>();

            tester.AssignSiblingComponents();

            Assert.IsTrue(tester.missingOptional == null);
            LogAssert.NoUnexpectedReceived();
            return;
        }

        [Test]
        public void SiblingTagFilterNoMatchLogsError()
        {
            GameObject root = new("SiblingTagFilterRoot");
            Track(root);
            root.tag = "Untagged";
            root.AddComponent<BoxCollider>();
            SiblingNoMatchTagTester tester = root.AddComponent<SiblingNoMatchTagTester>();

            // Emitted via the package logger, which is compiled out in a non-development player.
            ExpectWallstopLog(
                LogType.Error,
                new Regex(
                    @"Unable to find sibling component of type .* for field 'siblingCollider'"
                )
            );

            tester.AssignSiblingComponents();
            Assert.IsTrue(tester.siblingCollider == null);

            return;
        }

        [Test]
        public void SkipIfAssignedDoesNotOverride()
        {
            GameObject root = new("SkipRoot", typeof(SkipIfAssignedTesterEdgeCase));
            Track(root);
            SkipIfAssignedTesterEdgeCase tester = root.GetComponent<SkipIfAssignedTesterEdgeCase>();

            SpriteRenderer preassigned = root.AddComponent<SpriteRenderer>();
            tester.alreadyAssigned = preassigned;

            tester.AssignSiblingComponents();

            Assert.AreSame(preassigned, tester.alreadyAssigned);
            return;
        }
    }
}
