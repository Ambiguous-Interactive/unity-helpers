// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Visuals
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Visuals;

    [TestFixture]
    [Category("Fast")]
    public sealed class MatchSpriteMaskToSpriteRendererTests : CommonTestBase
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitSynchronizationResolvesSiblingsBeforeAnInactiveOwnerIsEnabled(
            bool disabled
        )
        {
            GameObject owner = Track(new GameObject(nameof(MatchSpriteMaskToSpriteRendererTests)));
            owner.SetActive(false);
            MatchSpriteMaskToSpriteRenderer matcher =
                owner.AddComponent<MatchSpriteMaskToSpriteRenderer>();
            matcher.enabled = !disabled;
            SpriteRenderer renderer = owner.GetComponent<SpriteRenderer>();
            SpriteMask mask = owner.GetComponent<SpriteMask>();
            renderer.sprite = CreateSprite();
            matcher.Synchronize();
            Assert.That(mask.sprite, Is.SameAs(renderer.sprite));
            Assert.That(owner.activeSelf, Is.False);
            Assert.That(matcher.enabled, Is.EqualTo(!disabled));
        }

        [Test]
        public void AddingMatcherRequiresBothSiblingsAndCopiesTheAssignedSprite()
        {
            GameObject owner = Track(
                new GameObject(nameof(MatchSpriteMaskToSpriteRendererTests), typeof(SpriteRenderer))
            );
            SpriteRenderer renderer = owner.GetComponent<SpriteRenderer>();
            renderer.sprite = CreateSprite();
            MatchSpriteMaskToSpriteRenderer matcher =
                owner.AddComponent<MatchSpriteMaskToSpriteRenderer>();
            SpriteMask mask = owner.GetComponent<SpriteMask>();
            Assert.That(mask != null, Is.True);
            Assert.That(mask.sprite, Is.SameAs(renderer.sprite));
            Assert.That(matcher.enabled, Is.True);
        }

        [Test]
        public void SynchronizeClearsOldFramesAndPreservesMaskAndRendererSettings()
        {
            GameObject owner = Track(new GameObject(nameof(MatchSpriteMaskToSpriteRendererTests)));
            MatchSpriteMaskToSpriteRenderer matcher =
                owner.AddComponent<MatchSpriteMaskToSpriteRenderer>();
            SpriteRenderer renderer = owner.GetComponent<SpriteRenderer>();
            SpriteMask mask = owner.GetComponent<SpriteMask>();
            mask.RangeToItsOwnLayer();
            mask.alphaCutoff = 0.75f;
            renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            renderer.flipX = true;
            renderer.flipY = true;
            renderer.sprite = CreateSprite();
            matcher.Synchronize();
            Assert.That(mask.sprite, Is.SameAs(renderer.sprite));
            renderer.sprite = null;
            matcher.Synchronize();
            Assert.That(mask.sprite == null, Is.True);
            Assert.That(mask.isCustomRangeActive, Is.True);
            Assert.That(mask.alphaCutoff, Is.EqualTo(0.75f));
            Assert.That(mask.frontSortingOrder, Is.EqualTo(short.MaxValue));
            Assert.That(mask.backSortingOrder, Is.EqualTo(short.MinValue));
            Assert.That(
                renderer.maskInteraction,
                Is.EqualTo(SpriteMaskInteraction.VisibleInsideMask)
            );
            Assert.That(renderer.flipX && renderer.flipY, Is.True);
        }

        [UnityTest]
        public IEnumerator LateUpdateTracksFrameChangesAndReenableSynchronizesImmediately()
        {
            GameObject owner = Track(new GameObject(nameof(MatchSpriteMaskToSpriteRendererTests)));
            MatchSpriteMaskToSpriteRenderer matcher =
                owner.AddComponent<MatchSpriteMaskToSpriteRenderer>();
            SpriteRenderer renderer = owner.GetComponent<SpriteRenderer>();
            SpriteMask mask = owner.GetComponent<SpriteMask>();
            Sprite first = CreateSprite();
            Sprite second = CreateSprite();
            renderer.sprite = first;
            yield return null;
            yield return null;
            Assert.That(mask.sprite, Is.SameAs(first));
            matcher.enabled = false;
            renderer.sprite = second;
            yield return null;
            yield return null;
            Assert.That(mask.sprite, Is.SameAs(first));
            matcher.enabled = true;
            Assert.That(mask.sprite, Is.SameAs(second));
            renderer.sprite = null;
            yield return null;
            yield return null;
            Assert.That(mask.sprite == null, Is.True);
        }

        private Sprite CreateSprite()
        {
            Texture2D texture = Track(new Texture2D(2, 2, TextureFormat.RGBA32, false));
            return Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
        }
    }
}
