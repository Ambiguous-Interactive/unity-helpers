// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// UNH-SUPPRESS UNH003: EffectStackKeyTests inherits from TagsTestBase which inherits from CommonTestBase
namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Tags.Helpers;

    /// <summary>
    /// A reference-grouped stack key is keyed on a managed reference, and a managed reference
    /// outlives the engine object it points at. Equality has always known that; hashing had to learn
    /// it, or every handle bucketed under an effect became unreachable the moment the effect was
    /// destroyed.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EffectStackKeyTests : TagsTestBase
    {
        [Test]
        public void ReferenceKeyStaysReachableAfterTheEffectIsDestroyed()
        {
            AttributeEffect effect = CreateEffect("Poison");
            EffectStackKey key = EffectStackKey.CreateReference(effect);

            Dictionary<EffectStackKey, int> stacks = new() { [key] = 1 };
            Assert.IsTrue(stacks.ContainsKey(key));

            Object.DestroyImmediate(effect); // UNH-SUPPRESS UNH001: the destroyed asset IS the subject

            /*
                Equals compares the managed reference, which destruction does not touch. The hash
                used to route through Unity's own GetHashCode and collapse onto the null sentinel, so
                the bucket moved out from under an entry that was still perfectly findable by value.
            */
            EffectStackKey rebuilt = EffectStackKey.CreateReference(effect);
            Assert.IsTrue(key.Equals(rebuilt));
            Assert.AreEqual(key.GetHashCode(), rebuilt.GetHashCode());
            Assert.IsTrue(stacks.ContainsKey(rebuilt));
        }

        [Test]
        public void ReferenceKeysForDifferentEffectsDoNotShareABucket()
        {
            AttributeEffect first = CreateEffect("Poison");
            AttributeEffect second = CreateEffect("Poison");

            EffectStackKey firstKey = EffectStackKey.CreateReference(first);
            EffectStackKey secondKey = EffectStackKey.CreateReference(second);

            Assert.IsFalse(firstKey.Equals(secondKey));

            HashSet<EffectStackKey> keys = new() { firstKey };
            Assert.IsTrue(keys.Add(secondKey));
        }

        [Test]
        public void CustomKeysGroupByOrdinalString()
        {
            EffectStackKey first = EffectStackKey.CreateCustom("DamageOverTime");
            EffectStackKey second = EffectStackKey.CreateCustom("DamageOverTime");
            EffectStackKey different = EffectStackKey.CreateCustom("damageovertime");

            Assert.IsTrue(first.Equals(second));
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
            Assert.IsFalse(first.Equals(different));
        }
    }
}
