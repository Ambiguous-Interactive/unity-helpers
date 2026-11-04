// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System.Collections.Generic;
    using System.Globalization;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class RelationalInvalidTagTests : CommonTestBase
    {
        [TestCase("en-US")]
        [TestCase("fr-FR")]
        public void UndefinedTagsDoNotAbortBindingOrBroadenMatchesAcrossAnyRelation(
            string cultureName
        )
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                GameObject parent = Track(new GameObject(nameof(RelationalInvalidTagTests)));
                GameObject owner = Track(
                    new GameObject(
                        nameof(RelationalInvalidTagProbe),
                        typeof(RelationalInvalidTagProbe)
                    )
                );
                GameObject child = Track(new GameObject(nameof(RelationalInvalidTagProbe.child)));
                owner.transform.SetParent(parent.transform);
                child.transform.SetParent(owner.transform);
                RelationalInvalidTagProbe probe = owner.GetComponent<RelationalInvalidTagProbe>();
                Assert.AreEqual("Untagged", owner.tag);
                ExpectMissingRelationalComponentError(
                    owner.name,
                    nameof(RelationalInvalidTagProbe),
                    "parent",
                    typeof(Transform).FullName,
                    nameof(probe.requiredParent)
                );
                ExpectMissingRelationalComponentError(
                    owner.name,
                    nameof(RelationalInvalidTagProbe),
                    "sibling",
                    typeof(Transform).FullName,
                    nameof(probe.requiredSibling)
                );
                ExpectMissingRelationalComponentError(
                    owner.name,
                    nameof(RelationalInvalidTagProbe),
                    "child",
                    typeof(Transform).FullName,
                    nameof(probe.requiredChild)
                );
                for (int invocation = 0; invocation < 2; ++invocation)
                {
                    probe.invalidParent = parent.transform;
                    probe.invalidSibling = owner.transform;
                    probe.invalidChild = child.transform;
                    probe.invalidParents = new[] { parent.transform };
                    probe.invalidSiblings = new[] { owner.transform };
                    probe.invalidChildren = new[] { child.transform };
                    probe.invalidParentList = new List<Transform> { parent.transform };
                    probe.invalidSiblingList = new List<Transform> { owner.transform };
                    probe.invalidChildList = new List<Transform> { child.transform };
                    probe.invalidParentSet = new HashSet<Transform> { parent.transform };
                    probe.invalidSiblingSet = new HashSet<Transform> { owner.transform };
                    probe.invalidChildSet = new HashSet<Transform> { child.transform };
                    Assert.DoesNotThrow(() => probe.AssignRelationalComponents());
                    Assert.IsTrue(probe.invalidParent == null);
                    Assert.IsTrue(probe.invalidSibling == null);
                    Assert.IsTrue(probe.invalidChild == null);
                    Assert.IsTrue(probe.emptyTag == null);
                    Assert.IsTrue(probe.whitespaceTag == null);
                    Assert.IsTrue(probe.requiredParent == null);
                    Assert.IsTrue(probe.requiredSibling == null);
                    Assert.IsTrue(probe.requiredChild == null);
                    Assert.That(probe.invalidParents, Is.Empty);
                    Assert.That(probe.invalidSiblings, Is.Empty);
                    Assert.That(probe.invalidChildren, Is.Empty);
                    Assert.That(probe.invalidParentList, Is.Empty);
                    Assert.That(probe.invalidSiblingList, Is.Empty);
                    Assert.That(probe.invalidChildList, Is.Empty);
                    Assert.That(probe.invalidParentSet, Is.Empty);
                    Assert.That(probe.invalidSiblingSet, Is.Empty);
                    Assert.That(probe.invalidChildSet, Is.Empty);
                    Assert.AreSame(parent.transform, probe.parent);
                    Assert.AreSame(owner.transform, probe.sibling);
                    Assert.AreSame(child.transform, probe.child);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }
    }
}
