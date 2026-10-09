// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Tags.Helpers;

    [TestFixture]
    [Category("Fast")]
    public sealed class TagOwnershipSnapshotTests : TagsTestBase
    {
        private const string OwnedTag = "Owned";
        private const string OtherTag = "Other";

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void RemovalUsesAppliedTagsAfterAuthoringChanges(
            bool destroysAsset,
            bool removeByTag
        )
        {
            GameObject entity = CreateTrackedGameObject(
                nameof(TagOwnershipSnapshotTests),
                typeof(TagHandler)
            );
            TagHandler tags = entity.GetComponent<TagHandler>();
            tags.ApplyTag(OtherTag);
            AttributeEffect effect = CreateEffect(
                OwnedTag,
                authored =>
                {
                    authored.effectTags.Add(OwnedTag);
                    authored.effectTags.Add(OwnedTag);
                }
            );
            EffectHandle handle = EffectHandle.CreateInstance(effect);
            tags.ForceApplyTags(handle);
            if (destroysAsset)
            {
                Object.DestroyImmediate(effect); // UNH-SUPPRESS: destroyed asset is the input under test.
            }
            else
            {
                effect.effectTags.Clear();
                effect.effectTags.Add(OtherTag);
            }

            Assert.AreEqual(1, tags.GetHandlesWithTag(OwnedTag).Count);
            Assert.AreEqual(handle.id, tags.GetHandlesWithTag(OwnedTag)[0].id);
            List<EffectHandle> enumerated = new();
            foreach (EffectHandle matching in tags.EnumerateHandlesWithTag(OwnedTag))
            {
                enumerated.Add(matching);
            }
            Assert.AreEqual(1, enumerated.Count);
            Assert.AreEqual(handle.id, enumerated[0].id);
            Assert.IsEmpty(tags.GetHandlesWithTag(OtherTag));
            if (removeByTag)
            {
                Assert.AreEqual(1, tags.RemoveTag(OwnedTag).Count);
            }
            else
            {
                Assert.IsTrue(tags.ForceRemoveTags(handle));
            }
            Assert.IsFalse(tags.HasTag(OwnedTag));
            Assert.IsTrue(tags.TryGetTagCount(OtherTag, out int count));
            Assert.AreEqual(1, count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReapplyingSameHandleDuringApplicationRetainsOnlyNewOwnership(bool throws)
        {
            GameObject entity = CreateTrackedGameObject(
                nameof(TagOwnershipSnapshotTests),
                typeof(TagHandler)
            );
            TagHandler tags = entity.GetComponent<TagHandler>();
            AttributeEffect effect = CreateEffect(
                OwnedTag,
                authored =>
                {
                    authored.effectTags.Add(OwnedTag);
                    authored.effectTags.Add("Unapplied");
                }
            );
            EffectHandle handle = EffectHandle.CreateInstance(effect);
            bool replaced = false;
            tags.OnTagAdded += added =>
            {
                if (replaced || !string.Equals(added, OwnedTag, System.StringComparison.Ordinal))
                {
                    return;
                }
                replaced = true;
                Assert.IsTrue(tags.ForceRemoveTags(handle));
                effect.effectTags.Clear();
                effect.effectTags.Add(OtherTag);
                tags.ForceApplyTags(handle);
                if (throws)
                {
                    throw new System.InvalidOperationException("Original application failed");
                }
            };
            if (throws)
            {
                Assert.Throws<System.InvalidOperationException>(() => tags.ForceApplyTags(handle));
            }
            else
            {
                tags.ForceApplyTags(handle);
            }
            Assert.IsFalse(tags.HasTag(OwnedTag));
            Assert.IsFalse(tags.HasTag("Unapplied"));
            Assert.IsTrue(tags.TryGetTagCount(OtherTag, out int count));
            Assert.AreEqual(1, count);
            Assert.IsTrue(tags.ForceRemoveTags(handle));
            Assert.IsEmpty(tags.GetActiveTags());
        }

        [Test]
        public void FailedApplicationUnwindsOriginalTagsAfterCallbackChangesAsset()
        {
            GameObject entity = CreateTrackedGameObject(
                nameof(TagOwnershipSnapshotTests),
                typeof(TagHandler)
            );
            TagHandler tags = entity.GetComponent<TagHandler>();
            tags.ApplyTag(OtherTag);
            AttributeEffect effect = CreateEffect(
                OwnedTag,
                authored => authored.effectTags.Add(OwnedTag)
            );
            EffectHandle handle = EffectHandle.CreateInstance(effect);
            tags.OnTagAdded += added =>
            {
                effect.effectTags.Clear();
                effect.effectTags.Add(OtherTag);
                throw new System.InvalidOperationException("Application failed");
            };
            Assert.Throws<System.InvalidOperationException>(() => tags.ForceApplyTags(handle));
            Assert.IsFalse(tags.HasTag(OwnedTag));
            Assert.IsTrue(tags.TryGetTagCount(OtherTag, out int count));
            Assert.AreEqual(1, count);
            Assert.IsFalse(tags.ForceRemoveTags(handle));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DestructionDuringApplicationStopsRemainingNotifications(bool tracked)
        {
            GameObject entity = CreateTrackedGameObject(
                nameof(TagOwnershipSnapshotTests),
                typeof(TagHandler)
            );
            TagHandler tags = entity.GetComponent<TagHandler>();
            AttributeEffect effect = CreateEffect(
                OwnedTag,
                authored =>
                {
                    authored.effectTags.Add(OwnedTag);
                    authored.effectTags.Add(OtherTag);
                }
            );
            int notifications = 0;
            tags.OnTagAdded += added =>
            {
                ++notifications;
                Object.DestroyImmediate(tags); // UNH-SUPPRESS: exercise destruction during a callback.
            };
            if (tracked)
            {
                tags.ForceApplyTags(EffectHandle.CreateInstance(effect));
            }
            else
            {
                tags.ForceApplyEffect(effect);
            }
            Assert.AreEqual(1, notifications);
            Assert.IsEmpty(tags.GetActiveTags());
        }

        [Test]
        public void UntrackedApplicationSnapshotsTagsBeforeCallbacks()
        {
            GameObject entity = CreateTrackedGameObject(
                nameof(TagOwnershipSnapshotTests),
                typeof(TagHandler)
            );
            TagHandler tags = entity.GetComponent<TagHandler>();
            AttributeEffect effect = CreateEffect(
                OwnedTag,
                authored =>
                {
                    authored.effectTags.Add(OwnedTag);
                    authored.effectTags.Add(OtherTag);
                }
            );
            tags.OnTagAdded += added => effect.effectTags.Clear();
            Assert.DoesNotThrow(() => tags.ForceApplyEffect(effect));
            Assert.IsTrue(tags.HasTag(OwnedTag));
            Assert.IsTrue(tags.HasTag(OtherTag));
        }

        [TestCase(" ", 2)]
        [TestCase("\t", 2)]
        [TestCase("\u2003", 2)]
        [TestCase(" padded ", 2)]
        [TestCase(OwnedTag, 10_000)]
        public void SnapshotPreservesLiteralAndDuplicateTagsOwnedByAnotherSource(
            string tag,
            int duplicates
        )
        {
            GameObject entity = CreateTrackedGameObject(
                nameof(TagOwnershipSnapshotTests),
                typeof(TagHandler)
            );
            TagHandler tags = entity.GetComponent<TagHandler>();
            tags.ApplyTag(tag);
            AttributeEffect effect = CreateEffect(OwnedTag);
            for (int i = 0; i < duplicates; ++i)
            {
                effect.effectTags.Add(tag);
            }
            EffectHandle handle = EffectHandle.CreateInstance(effect);
            tags.ForceApplyTags(handle);
            Assert.IsTrue(tags.TryGetTagCount(tag, out int applied));
            Assert.AreEqual(duplicates + 1, applied);
            effect.effectTags.Clear();
            Assert.IsTrue(tags.ForceRemoveTags(handle));
            Assert.IsTrue(tags.TryGetTagCount(tag, out int remaining));
            Assert.AreEqual(1, remaining);
        }

        [Test]
        public void ApplicationCallbackCannotReplaceRemainingTagsOrExposeUnappliedOwnership()
        {
            GameObject entity = CreateTrackedGameObject(
                nameof(TagOwnershipSnapshotTests),
                typeof(TagHandler)
            );
            TagHandler tags = entity.GetComponent<TagHandler>();
            AttributeEffect effect = CreateEffect(
                OwnedTag,
                authored =>
                {
                    authored.effectTags.Add(OwnedTag);
                    authored.effectTags.Add(OtherTag);
                }
            );
            EffectHandle handle = EffectHandle.CreateInstance(effect);
            tags.OnTagAdded += added =>
            {
                if (!string.Equals(added, OwnedTag, System.StringComparison.Ordinal))
                {
                    return;
                }
                Assert.IsEmpty(tags.GetHandlesWithTag(OtherTag));
                foreach (EffectHandle matching in tags.EnumerateHandlesWithTag(OtherTag))
                {
                    Assert.Fail(
                        "An unapplied tag must have no contributing handle: " + matching.id
                    );
                }
                effect.effectTags.Clear();
            };
            Assert.DoesNotThrow(() => tags.ForceApplyTags(handle));
            Assert.IsTrue(tags.HasTag(OwnedTag));
            Assert.IsTrue(tags.HasTag(OtherTag));
            Assert.IsTrue(tags.ForceRemoveTags(handle));
            Assert.IsEmpty(tags.GetActiveTags());
        }
    }
}
