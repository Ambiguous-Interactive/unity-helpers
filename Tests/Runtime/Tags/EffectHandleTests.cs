// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System.Text.Json;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tags;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EffectHandleTests : AttributeTagsTestBase
    {
        [SetUp]
        public void SetUp()
        {
            ResetEffectHandleId();
        }

        [Test]
        public void CreateInstanceProducesSequentialIdentifiers()
        {
            AttributeEffect effect = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            effect.name = "HandleTest";

            EffectHandle first = EffectHandle.CreateInstance(effect);
            EffectHandle second = EffectHandle.CreateInstance(effect);

            Assert.AreEqual(first.id + 1, second.id);
            Assert.IsTrue(second.CompareTo(first) > 0);
            Assert.IsTrue(second.CompareTo((object)first) > 0);
        }

        [Test]
        public void EqualsOnlyDependsOnIdentifier()
        {
            AttributeEffect firstEffect = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            AttributeEffect secondEffect = Track(
                ScriptableObject.CreateInstance<AttributeEffect>()
            );
            firstEffect.name = "First";
            secondEffect.name = "Second";

            EffectHandle firstHandle = EffectHandle.CreateInstance(firstEffect);
            ResetEffectHandleId(firstHandle.id - 1);
            EffectHandle secondHandle = EffectHandle.CreateInstance(secondEffect);

            Assert.AreEqual(firstHandle.id, secondHandle.id);
            Assert.IsTrue(firstHandle.Equals(secondHandle));
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ToStringIncludesIdentifier()
        {
            AttributeEffect effect = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            effect.name = "JsonHandle";
            EffectHandle handle = EffectHandle.CreateInstance(effect);

            using JsonDocument document = JsonDocument.Parse(handle.ToString());
            JsonElement root = document.RootElement;
            Assert.AreEqual(handle.id, root.GetProperty("id").GetInt64());
        }
    }
}
