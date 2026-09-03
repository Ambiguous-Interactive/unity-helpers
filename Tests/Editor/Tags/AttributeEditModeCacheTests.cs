// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using Attribute = WallstopStudios.UnityHelpers.Tags.Attribute;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    /// <summary>
    /// Pins <see cref="Attribute.CurrentValue"/> outside play mode.
    /// </summary>
    /// <remarks>
    /// The equivalent play-mode assertions live in
    /// <c>Tests/Runtime/Tags/AttributeSerializationTests.cs</c>, and Unity's EditMode runner takes
    /// editor-only assemblies -- so nothing in CI read this property with
    /// <c>Application.isPlaying</c> false until this fixture existed. The getter used to discard
    /// the cache entirely on that branch, which silently dropped every deserialized buff
    /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/569">#569</see>).
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AttributeEditModeCacheTests
    {
        [Test]
        public void JsonRoundTripKeepsTheModifiedValueOutsidePlayMode()
        {
            Attribute attribute = new(100f);
            attribute.Add(25f);

            string json = Serializer.JsonStringify(attribute);
            Attribute deserialized = Serializer.JsonDeserialize<Attribute>(json);

            Assert.IsTrue(deserialized != null);
            Assert.AreEqual(100f, deserialized.BaseValue);
            Assert.AreEqual(125f, deserialized.CurrentValue);
        }

        [Test]
        public void ClearCacheAfterJsonRoundTripFallsBackToTheBaseValueOutsidePlayMode()
        {
            Attribute attribute = new(100f);
            attribute.Add(25f);

            Attribute deserialized = Serializer.JsonDeserialize<Attribute>(
                Serializer.JsonStringify(attribute)
            );

            Assert.IsTrue(deserialized != null);
            Assert.AreEqual(125f, deserialized.CurrentValue);

            deserialized.ClearCache();
            Assert.AreEqual(100f, deserialized.CurrentValue);
        }

        [Test]
        public void WritingTheBaseValueBehindTheCacheInvalidatesItOutsidePlayMode()
        {
            Attribute attribute = new(100f);
            attribute.Add(25f);
            Assert.AreEqual(125f, attribute.CurrentValue);

            /*
                Exactly what Unity's serializer does to a [SerializeField] on an Inspector edit, a
                prefab apply or an undo: assign the field, run nothing that could clear the cache.
            */
            attribute._baseValue = 200f;

            Assert.AreEqual(225f, attribute.CurrentValue);
        }
    }
#endif
}
