// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using Components;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class RelationalSkipMissingTests : CommonTestBase
    {
        private static void ClearCaches()
        {
            ParentComponentExtensions.ClearCachedFieldMetadata();
            SiblingComponentExtensions.ClearCachedFieldMetadata();
            ChildComponentExtensions.ClearCachedFieldMetadata();
            ReflectionHelpers.ClearFieldGetterCache();
            ReflectionHelpers.ClearFieldSetterCache();
        }

        private static SpriteRenderer GetReference(
            RelationalSkipMissingProbe probe,
            int relationship
        )
        {
            return relationship switch
            {
                0 => probe.parent,
                1 => probe.sibling,
                _ => probe.child,
            };
        }

        private static void SetReference(
            RelationalSkipMissingProbe probe,
            int relationship,
            SpriteRenderer value
        )
        {
            switch (relationship)
            {
                case 0:
                    probe.parent = value;
                    break;
                case 1:
                    probe.sibling = value;
                    break;
                default:
                    probe.child = value;
                    break;
            }
        }

        [TearDown]
        public void ResetBindingCaches()
        {
            ClearCaches();
        }

        [Test]
        public void MissingMatchClearsDestroyedSkippedReference(
            [Values(0, 1, 2)] int relationship,
            [Values(0, 1, 2)] int capability,
            [Values(false, true)] bool hierarchyExists
        )
        {
            using IDisposable scope = new ReflectionCapabilityScope(
                capability == 0,
                capability == 1
            );
            ClearCaches();
            GameObject host = Track(
                new GameObject(nameof(MissingMatchClearsDestroyedSkippedReference))
            );
            RelationalSkipMissingProbe probe = host.AddComponent<RelationalSkipMissingProbe>();
            Assert.IsTrue(probe != null);
            GameObject donor = Track(new GameObject("Donor"));
            SpriteRenderer destroyed = donor.AddComponent<SpriteRenderer>();
            Assert.IsTrue(destroyed != null);
            SetReference(probe, relationship, destroyed);
            UnityEngine.Object.DestroyImmediate(destroyed); // UNH-SUPPRESS: Exercise rebinding after destruction before teardown.
            if (hierarchyExists)
            {
                GameObject parent = Track(new GameObject("EmptyParent"));
                host.transform.SetParent(parent.transform);
                GameObject child = Track(new GameObject("EmptyChild"));
                child.transform.SetParent(host.transform);
            }
            Assert.IsTrue(destroyed == null);
            Assert.IsFalse(ReferenceEquals(null, GetReference(probe, relationship)));

            probe.AssignRelationalComponents();

            Assert.IsTrue(ReferenceEquals(null, GetReference(probe, relationship)));
            probe.AssignRelationalComponents();
            Assert.IsTrue(ReferenceEquals(null, GetReference(probe, relationship)));
        }

        [Test]
        public void MissingMatchPreservesLiveSkippedReference(
            [Values(0, 1, 2)] int relationship,
            [Values(0, 1, 2)] int capability
        )
        {
            using IDisposable scope = new ReflectionCapabilityScope(
                capability == 0,
                capability == 1
            );
            ClearCaches();
            GameObject host = Track(
                new GameObject(nameof(MissingMatchPreservesLiveSkippedReference))
            );
            RelationalSkipMissingProbe probe = host.AddComponent<RelationalSkipMissingProbe>();
            Assert.IsTrue(probe != null);
            GameObject donor = Track(new GameObject("Donor"));
            SpriteRenderer live = donor.AddComponent<SpriteRenderer>();
            Assert.IsTrue(live != null);
            SetReference(probe, relationship, live);

            probe.AssignRelationalComponents();

            Assert.AreSame(live, GetReference(probe, relationship));
            probe.AssignRelationalComponents();
            Assert.AreSame(live, GetReference(probe, relationship));
        }

        [Test]
        public void DestroyedSkippedReferenceFindsReplacement(
            [Values(0, 1, 2)] int relationship,
            [Values(0, 1, 2)] int capability
        )
        {
            using IDisposable scope = new ReflectionCapabilityScope(
                capability == 0,
                capability == 1
            );
            ClearCaches();
            GameObject host = Track(
                new GameObject(nameof(DestroyedSkippedReferenceFindsReplacement))
            );
            RelationalSkipMissingProbe probe = host.AddComponent<RelationalSkipMissingProbe>();
            Assert.IsTrue(probe != null);
            GameObject donor = Track(new GameObject("Donor"));
            SpriteRenderer destroyed = donor.AddComponent<SpriteRenderer>();
            Assert.IsTrue(destroyed != null);
            SetReference(probe, relationship, destroyed);
            UnityEngine.Object.DestroyImmediate(destroyed); // UNH-SUPPRESS: Exercise rebinding after destruction before teardown.
            GameObject match = relationship == 1 ? host : Track(new GameObject("Match"));
            if (relationship == 0)
            {
                host.transform.SetParent(match.transform);
            }
            else if (relationship == 2)
            {
                match.transform.SetParent(host.transform);
            }
            SpriteRenderer replacement = match.AddComponent<SpriteRenderer>();
            Assert.IsTrue(replacement != null);

            probe.AssignRelationalComponents();

            Assert.AreSame(replacement, GetReference(probe, relationship));
        }
    }
}
