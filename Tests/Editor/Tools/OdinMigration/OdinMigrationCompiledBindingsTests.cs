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

    [TestFixture]
    [Category("Fast")]
    public sealed class OdinMigrationCompiledBindingsTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ReferencedChainsPreserveAllMetadataAndDeduplicateSources(int depth)
        {
            Type[] types =
            {
                typeof(OdinMigrationExternalDerivedBindingAsset),
                typeof(OdinMigrationBindingAsset),
                typeof(ScriptableObject),
                typeof(UnityEngine.Object),
            };
            Dictionary<Type, OdinMigrationCompiledBinding> registrations =
                new Dictionary<Type, OdinMigrationCompiledBinding>();
            List<OdinMigrationCompiledField> fields = new List<OdinMigrationCompiledField>();
            List<OdinMigrationCompiledSourceEdit> edits =
                new List<OdinMigrationCompiledSourceEdit>();
            for (int index = 0; index <= depth; ++index)
            {
                string path = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                OdinMigrationCompiledField field = new OdinMigrationCompiledField(
                    path,
                    null,
                    null,
                    null,
                    null,
                    path,
                    0,
                    1
                );
                OdinMigrationCompiledSourceEdit edit = new OdinMigrationCompiledSourceEdit(
                    path,
                    0,
                    1,
                    "replacement"
                );
                fields.Add(field);
                edits.Add(edit);
                registrations.Add(
                    types[index],
                    new OdinMigrationCompiledBinding(
                        types[index],
                        new[]
                        {
                            new OdinMigrationCompiledSource("shared", "same"),
                            new OdinMigrationCompiledSource(path, "checksum"),
                        },
                        new[] { field },
                        new[] { edit },
                        new[] { types[index], types[0] },
                        index < depth ? types[index + 1] : null
                    )
                );
            }
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryResolve(
                    types[0],
                    registrations,
                    out OdinMigrationCompiledBinding combined
                )
            );
            Assert.AreEqual(types[0], combined.OriginalType);
            Assert.AreEqual(depth + 2, combined.Sources.Count);
            Assert.AreEqual(depth + 1, combined.AffectedTypes.Count);
            CollectionAssert.AreEqual(fields, combined.Fields);
            CollectionAssert.AreEqual(edits, combined.BaseEdits);
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
        [TestCase(9)]
        public void InvalidReferencedMetadataReturnsNoPartialBinding(int invalid)
        {
            Type owner = typeof(OdinMigrationExternalDerivedBindingAsset);
            Type parent = typeof(OdinMigrationBindingAsset);
            OdinMigrationCompiledSource source = new OdinMigrationCompiledSource(
                "shared",
                "checksum"
            );
            Dictionary<Type, OdinMigrationCompiledBinding> registrations = new Dictionary<
                Type,
                OdinMigrationCompiledBinding
            >
            {
                [owner] = new OdinMigrationCompiledBinding(
                    owner,
                    new[] { source },
                    null,
                    baseEdits: null,
                    affectedTypes: null,
                    inheritedBindingType: parent
                ),
                [parent] = new OdinMigrationCompiledBinding(parent, new[] { source }, null),
            };
            switch (invalid)
            {
                case 0:
                    registrations.Remove(parent);
                    break;
                case 1:
                    registrations[parent] = null;
                    break;
                case 2:
                    registrations[parent] = new OdinMigrationCompiledBinding(
                        typeof(Texture2D),
                        null,
                        null
                    );
                    break;
                case 3:
                    registrations[owner] = new OdinMigrationCompiledBinding(
                        owner,
                        null,
                        null,
                        baseEdits: null,
                        affectedTypes: null,
                        inheritedBindingType: owner
                    );
                    break;
                case 4:
                    registrations[owner] = new OdinMigrationCompiledBinding(
                        owner,
                        null,
                        null,
                        baseEdits: null,
                        affectedTypes: null,
                        inheritedBindingType: typeof(Texture2D)
                    );
                    break;
                case 5:
                    registrations[parent] = new OdinMigrationCompiledBinding(
                        parent,
                        new OdinMigrationCompiledSource[] { null },
                        null
                    );
                    break;
                case 6:
                    registrations[parent] = new OdinMigrationCompiledBinding(
                        parent,
                        new[] { new OdinMigrationCompiledSource(" ", "checksum") },
                        null
                    );
                    break;
                case 7:
                    registrations[parent] = new OdinMigrationCompiledBinding(
                        parent,
                        new[] { new OdinMigrationCompiledSource("shared", " ") },
                        null
                    );
                    break;
                case 8:
                    registrations[parent] = new OdinMigrationCompiledBinding(
                        parent,
                        new[] { new OdinMigrationCompiledSource("shared", "different") },
                        null
                    );
                    break;
                case 9:
                    registrations[owner] = new OdinMigrationCompiledBinding(parent, null, null);
                    break;
            }
            Assert.IsFalse(
                OdinMigrationCompiledBindings.TryResolve(
                    owner,
                    registrations,
                    out OdinMigrationCompiledBinding binding
                )
            );
            Assert.IsTrue(binding == null);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void MissingResolutionInputsReturnNoBinding(int invalid)
        {
            Assert.IsFalse(
                OdinMigrationCompiledBindings.TryResolve(
                    invalid == 0 ? null : typeof(OdinMigrationExternalDerivedBindingAsset),
                    invalid == 1 ? null : new Dictionary<Type, OdinMigrationCompiledBinding>(),
                    out OdinMigrationCompiledBinding binding
                )
            );
            Assert.IsTrue(binding == null);
        }

        [Test]
        public void LateBaseRegistrationAllowsResolutionAfterAMissingDependency()
        {
            Type owner = typeof(OdinMigrationExternalDerivedBindingAsset);
            Type parent = typeof(OdinMigrationBindingAsset);
            Dictionary<Type, OdinMigrationCompiledBinding> registrations = new Dictionary<
                Type,
                OdinMigrationCompiledBinding
            >
            {
                [owner] = new OdinMigrationCompiledBinding(
                    owner,
                    null,
                    null,
                    baseEdits: null,
                    affectedTypes: null,
                    inheritedBindingType: parent
                ),
            };
            Assert.IsFalse(
                OdinMigrationCompiledBindings.TryResolve(
                    owner,
                    registrations,
                    out OdinMigrationCompiledBinding missing
                )
            );
            Assert.IsTrue(missing == null);
            registrations.Add(parent, new OdinMigrationCompiledBinding(parent, null, null));
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryResolve(
                    owner,
                    registrations,
                    out OdinMigrationCompiledBinding binding
                )
            );
            Assert.AreEqual(owner, binding.OriginalType);
        }

        [Test]
        public void RegisteringANewBaseSnapshotInvalidatesPreviouslyResolvedChildren()
        {
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    typeof(OdinMigrationBindingAsset),
                    out OdinMigrationCompiledBinding original
                )
            );
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    typeof(OdinMigrationExternalDerivedBindingAsset),
                    out OdinMigrationCompiledBinding before
                )
            );
            Assert.AreEqual(2, before.Fields.Count);
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    typeof(OdinMigrationExternalDerivedBindingAsset),
                    out OdinMigrationCompiledBinding cached
                )
            );
            Assert.AreSame(before, cached);
            try
            {
                Assert.IsTrue(
                    OdinMigrationCompiledBindings.Register(
                        new OdinMigrationCompiledBinding(original.OriginalType, null, null)
                    )
                );
                Assert.IsTrue(
                    OdinMigrationCompiledBindings.TryGet(
                        typeof(OdinMigrationExternalDerivedBindingAsset),
                        out OdinMigrationCompiledBinding after
                    )
                );
                Assert.AreEqual(0, after.Fields.Count);
                Assert.AreNotSame(before, after);
            }
            finally
            {
                Assert.IsTrue(OdinMigrationCompiledBindings.Register(original));
            }
        }
    }
}
