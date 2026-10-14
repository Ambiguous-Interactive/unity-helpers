// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    [TestFixture]
    [Category("Fast")]
    public sealed class OdinMigrationLiveDictionaryTests : CommonTestBase
    {
        private static OdinMigrationCompiledField FindField(
            OdinMigrationCompiledBinding binding,
            string name
        )
        {
            foreach (OdinMigrationCompiledField field in binding.Fields)
            {
                if (string.Equals(field.Name, name, StringComparison.Ordinal))
                {
                    return field;
                }
            }
            Assert.Fail("Missing original field: " + name);
            return null;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CaptureDistinguishesNullAndEmptyDictionaries(bool absent)
        {
            Dictionary<SpriteAlignment, List<Texture2D>> original = absent ? null : new();

            Assert.IsTrue(
                OdinMigrationLiveDictionary.TryCapture(
                    original,
                    key => (int)key,
                    out OdinMigrationLiveDictionary snapshot,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(absent, snapshot.IsNull);
            Assert.IsEmpty(snapshot.Keys);
            Assert.IsEmpty(snapshot.Values);
        }

        [Test]
        public void CapturePreservesOrderNullsSharingAndLiveReferenceIdentity()
        {
            Texture2D texture = Track(new Texture2D(1, 1));
            List<Texture2D> shared = new() { texture, null, texture };
            Dictionary<SpriteAlignment, List<Texture2D>> original = new()
            {
                [(SpriteAlignment)int.MinValue] = shared,
                [SpriteAlignment.Center] = null,
                [(SpriteAlignment)int.MaxValue] = shared,
                [SpriteAlignment.TopLeft] = new(),
            };

            Assert.IsTrue(
                OdinMigrationLiveDictionary.TryCapture(
                    original,
                    key => (int)key,
                    out OdinMigrationLiveDictionary snapshot,
                    out string failure
                ),
                failure
            );
            CollectionAssert.AreEqual(
                new[]
                {
                    int.MinValue,
                    (int)SpriteAlignment.Center,
                    int.MaxValue,
                    (int)SpriteAlignment.TopLeft,
                },
                snapshot.Keys
            );
            Assert.AreSame(snapshot.Values[0], snapshot.Values[2]);
            Assert.IsTrue(snapshot.Values[1] == null);
            Assert.IsEmpty(snapshot.Values[3]);
            Assert.AreSame(texture, snapshot.Values[0][0]);
            Assert.IsTrue(ReferenceEquals(snapshot.Values[0][1], null));
            Assert.AreSame(texture, snapshot.Values[0][2]);

            original.Clear();
            shared.Clear();
            Assert.AreEqual(4, snapshot.Keys.Count);
            Assert.AreEqual(3, snapshot.Values[0].Count);
            Assert.Throws<NotSupportedException>(() => ((IList<int>)snapshot.Keys).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<Object>)snapshot.Values[0]).Clear());
        }

        [Test]
        public void GeneratedReaderCapturesTheRealOriginalField()
        {
            OdinMigrationBindingAsset owner = CreateScriptableObject<OdinMigrationBindingAsset>();
            Texture2D texture = Track(new Texture2D(1, 1));
            owner.Variations.Add(SpriteAlignment.BottomRight, new List<Texture2D> { texture });
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    owner.GetType(),
                    out OdinMigrationCompiledBinding binding
                )
            );
            OdinMigrationCompiledField field = FindField(
                binding,
                nameof(OdinMigrationBindingAsset.Variations)
            );
            Assert.IsTrue(field.CanCapture);
            Assert.IsTrue(field.CanRestore);

            Assert.IsTrue(
                field.TryCapture(
                    owner,
                    out OdinMigrationLiveDictionary snapshot,
                    out string failure
                ),
                failure
            );
            CollectionAssert.AreEqual(new[] { (int)SpriteAlignment.BottomRight }, snapshot.Keys);
            Assert.AreSame(texture, snapshot.Values[0][0]);
            Assert.AreEqual(1, owner.Variations.Count);
        }

        [Test]
        public void GeneratedRecoveryWriterRebuildsOriginalValuesWithoutPartialMutation()
        {
            OdinMigrationBindingAsset owner = CreateScriptableObject<OdinMigrationBindingAsset>();
            Texture2D texture = Track(new Texture2D(1, 1));
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    owner.GetType(),
                    out OdinMigrationCompiledBinding binding
                )
            );
            OdinMigrationCompiledField field = FindField(
                binding,
                nameof(OdinMigrationBindingAsset.Variations)
            );

            Assert.IsTrue(
                field.TryRestore(
                    owner,
                    new[] { int.MinValue, int.MaxValue },
                    new IReadOnlyList<Object>[]
                    {
                        new Object[] { texture, null },
                        Array.Empty<Object>(),
                    },
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(2, owner.Variations.Count);
            Assert.IsTrue(
                owner.Variations.TryGetValue(
                    (SpriteAlignment)int.MinValue,
                    out List<Texture2D> restored
                )
            );
            Assert.AreSame(texture, restored[0]);
            Assert.IsTrue(ReferenceEquals(restored[1], null));
            Assert.IsTrue(
                owner.Variations.TryGetValue(
                    (SpriteAlignment)int.MaxValue,
                    out List<Texture2D> empty
                )
            );
            Assert.IsEmpty(empty);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void GeneratedRecoveryWriterRefusesIncompleteOrInvalidValuesBeforeWriting(int damage)
        {
            OdinMigrationBindingAsset owner = CreateScriptableObject<OdinMigrationBindingAsset>();
            Texture2D texture = Track(new Texture2D(1, 1));
            owner.Variations.Add(SpriteAlignment.Center, new List<Texture2D> { texture });
            Dictionary<SpriteAlignment, List<Texture2D>> original = owner.Variations;
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    owner.GetType(),
                    out OdinMigrationCompiledBinding binding
                )
            );
            OdinMigrationCompiledField field = FindField(
                binding,
                nameof(OdinMigrationBindingAsset.Variations)
            );
            IReadOnlyList<int> keys = new[] { (int)SpriteAlignment.BottomRight };
            IReadOnlyList<IReadOnlyList<Object>> values = new IReadOnlyList<Object>[]
            {
                new Object[] { texture },
            };
            switch (damage)
            {
                case 0:
                    keys = null;
                    break;
                case 1:
                    values = null;
                    break;
                case 2:
                    keys = Array.Empty<int>();
                    break;
                case 3:
                    keys = new[] { 1, 1 };
                    values = new IReadOnlyList<Object>[]
                    {
                        Array.Empty<Object>(),
                        Array.Empty<Object>(),
                    };
                    break;
                case 4:
                    values = new IReadOnlyList<Object>[]
                    {
                        new Object[] { Track(new GameObject("Wrong reference")) },
                    };
                    break;
                case 5:
                    Texture2D destroyed = Track(new Texture2D(1, 1));
                    Object.DestroyImmediate(destroyed); // UNH-SUPPRESS: destroyed recovery reference is the subject
                    values = new IReadOnlyList<Object>[] { new Object[] { destroyed } };
                    break;
                case 6:
                    values = new IReadOnlyList<Object>[] { null };
                    break;
                case 7:
                    keys = new int[OdinMigrationLiveDictionary.MaximumEntries + 1];
                    values = new IReadOnlyList<Object>[keys.Count];
                    break;
                case 8:
                    values = new IReadOnlyList<Object>[]
                    {
                        new Object[OdinMigrationLiveDictionary.MaximumReferences + 1],
                    };
                    break;
            }

            Assert.IsFalse(field.TryRestore(owner, keys, values, out string failure));
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
            Assert.AreSame(original, owner.Variations);
            Assert.AreEqual(1, original.Count);
            Assert.IsTrue(
                original.TryGetValue(SpriteAlignment.Center, out List<Texture2D> retained)
            );
            Assert.AreSame(texture, retained[0]);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedReaderRefusesWrongOrDestroyedOwners(bool destroyed)
        {
            OdinMigrationBindingAsset owner = CreateScriptableObject<OdinMigrationBindingAsset>();
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    owner.GetType(),
                    out OdinMigrationCompiledBinding binding
                )
            );
            OdinMigrationCompiledField field = FindField(
                binding,
                nameof(OdinMigrationBindingAsset.Variations)
            );
            Object invalid = destroyed ? owner : Track(new Texture2D(1, 1));
            if (destroyed)
            {
                Object.DestroyImmediate(owner); // UNH-SUPPRESS: destroyed owner is the subject
            }

            Assert.IsFalse(
                field.TryCapture(
                    invalid,
                    out OdinMigrationLiveDictionary snapshot,
                    out string failure
                )
            );
            Assert.IsTrue(snapshot == null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CaptureRequiresAValidAndInjectiveKeyEncoder(bool missing)
        {
            Dictionary<SpriteAlignment, List<Texture2D>> original = new()
            {
                [SpriteAlignment.TopLeft] = new(),
                [SpriteAlignment.BottomRight] = new(),
            };
            Func<SpriteAlignment, int> encode = missing ? null : _ => 1;

            Assert.IsFalse(
                OdinMigrationLiveDictionary.TryCapture(
                    original,
                    encode,
                    out OdinMigrationLiveDictionary snapshot,
                    out string failure
                )
            );
            Assert.IsTrue(snapshot == null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
        }

        [TestCase(OdinMigrationLiveDictionary.MaximumEntries, true)]
        [TestCase(OdinMigrationLiveDictionary.MaximumEntries + 1, false)]
        public void CaptureBoundsDeliveredEntries(int count, bool accepted)
        {
            Dictionary<SpriteAlignment, List<Texture2D>> original = new();
            for (int index = 0; index < count; ++index)
            {
                original.Add((SpriteAlignment)index, null);
            }

            Assert.AreEqual(
                accepted,
                OdinMigrationLiveDictionary.TryCapture(
                    original,
                    key => (int)key,
                    out OdinMigrationLiveDictionary snapshot,
                    out string failure
                )
            );
            if (accepted)
            {
                Assert.AreEqual(count, snapshot.Keys.Count);
                Assert.IsTrue(failure == null);
            }
            else
            {
                Assert.IsTrue(snapshot == null);
                StringAssert.Contains("entry limit", failure);
            }
        }

        [TestCase(OdinMigrationLiveDictionary.MaximumReferences, false, true)]
        [TestCase(OdinMigrationLiveDictionary.MaximumReferences + 1, false, false)]
        [TestCase(OdinMigrationLiveDictionary.MaximumReferences / 2, true, true)]
        [TestCase(OdinMigrationLiveDictionary.MaximumReferences / 2 + 1, true, false)]
        public void CaptureBoundsAllDeliveredSlotsIncludingSharedLists(
            int count,
            bool shared,
            bool accepted
        )
        {
            List<Texture2D> references = new();
            for (int index = 0; index < count; ++index)
            {
                references.Add(null);
            }
            Dictionary<SpriteAlignment, List<Texture2D>> original = new()
            {
                [SpriteAlignment.Center] = references,
            };
            if (shared)
            {
                original.Add(SpriteAlignment.BottomLeft, references);
            }

            Assert.AreEqual(
                accepted,
                OdinMigrationLiveDictionary.TryCapture(
                    original,
                    key => (int)key,
                    out OdinMigrationLiveDictionary snapshot,
                    out string failure
                )
            );
            if (accepted)
            {
                Assert.AreEqual(count, snapshot.Values[0].Count);
            }
            else
            {
                Assert.IsTrue(snapshot == null);
                StringAssert.Contains("reference limit", failure);
            }
        }

        [Test]
        public void CaptureReportsEncoderFailuresWithoutAPartialSnapshot()
        {
            Dictionary<SpriteAlignment, List<Texture2D>> original = new()
            {
                [SpriteAlignment.Center] = new(),
            };

            Assert.IsFalse(
                OdinMigrationLiveDictionary.TryCapture(
                    original,
                    _ => throw new InvalidOperationException("encoder failed"),
                    out OdinMigrationLiveDictionary snapshot,
                    out string failure
                )
            );
            Assert.IsTrue(snapshot == null);
            StringAssert.Contains("encoder failed", failure);
        }
    }
}
