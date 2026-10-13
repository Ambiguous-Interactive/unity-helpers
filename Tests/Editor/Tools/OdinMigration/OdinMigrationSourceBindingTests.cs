// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class OdinMigrationSourceBindingTests : CommonTestBase
    {
        private static OdinMigrationDictionaryField[] Fields()
        {
            return new[]
            {
                OdinMigrationDictionaryField.Create<SpriteAlignment, Texture2D>(
                    nameof(OdinMigrationBindingAsset.Variations)
                ),
                OdinMigrationDictionaryField.Create<HideFlags, Texture2D>(
                    nameof(OdinMigrationBindingAsset.Other)
                ),
            };
        }

        [Test]
        public void GeneratedBindingMatchesTheOriginalScriptAndFieldTypes()
        {
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    typeof(OdinMigrationBindingAsset),
                    out OdinMigrationCompiledBinding compiled
                )
            );
            Assert.AreEqual(2, compiled.Fields.Count);
            Assert.Greater(compiled.Sources.Count, 0);
            Assert.IsTrue(
                OdinMigrationTool.TryBindSerializedDictionaryFields(
                    Payload(),
                    Fields(),
                    out OdinMigrationCompiledBinding binding,
                    out string failure
                ),
                failure
            );
            Assert.AreSame(compiled, binding);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WrongCompiledFieldShapeReturnsNoBinding(bool wrongKey)
        {
            OdinMigrationDictionaryField[] fields = wrongKey
                ? new[]
                {
                    OdinMigrationDictionaryField.Create<HideFlags, Texture2D>(
                        nameof(OdinMigrationBindingAsset.Variations)
                    ),
                }
                : new[]
                {
                    OdinMigrationDictionaryField.Create<SpriteAlignment, Sprite>(
                        nameof(OdinMigrationBindingAsset.Variations)
                    ),
                };
            Assert.IsFalse(
                OdinMigrationTool.TryBindSerializedDictionaryFields(
                    Payload(),
                    fields,
                    out OdinMigrationCompiledBinding binding,
                    out string failure
                )
            );
            Assert.IsTrue(binding == null);
            StringAssert.Contains("compiled original declaration", failure);
        }

        [Test]
        public void ChangedCompilerSnapshotReturnsNoBinding()
        {
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    typeof(OdinMigrationBindingAsset),
                    out OdinMigrationCompiledBinding original
                )
            );
            OdinMigrationCompiledSource source = original.Sources[0];
            OdinMigrationCompiledField[] fields = new OdinMigrationCompiledField[
                original.Fields.Count
            ];
            for (int index = 0; index < fields.Length; ++index)
            {
                fields[index] = original.Fields[index];
            }
            try
            {
                Assert.IsTrue(
                    OdinMigrationCompiledBindings.Register(
                        new OdinMigrationCompiledBinding(
                            typeof(OdinMigrationBindingAsset),
                            new[]
                            {
                                new OdinMigrationCompiledSource(source.Path, "stale compilation"),
                            },
                            fields
                        )
                    )
                );
                Assert.IsFalse(
                    OdinMigrationTool.TryBindSerializedDictionaryFields(
                        Payload(),
                        Fields(),
                        out OdinMigrationCompiledBinding binding,
                        out string failure
                    )
                );
                Assert.IsTrue(binding == null);
                StringAssert.Contains("changed after", failure);
            }
            finally
            {
                Assert.IsTrue(OdinMigrationCompiledBindings.Register(original));
            }
        }

        [Test]
        public void BoundConversionChecksTheCompilerAndLiveReferencesTogether()
        {
            string folder = "Assets/OdinBoundConversion-" + Guid.NewGuid().ToString("N");
            Assert.IsTrue(
                !string.IsNullOrWhiteSpace(
                    AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder))
                )
            );
            try
            {
                Texture2D texture = Track(new Texture2D(1, 1));
                string path = folder + "/Reference.asset";
                AssetDatabase.CreateAsset(texture, path);
                AssetDatabase.SaveAssets();
                Assert.IsTrue(
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        texture,
                        out string guid,
                        out long fileId
                    )
                );
                OdinMigrationSerializedData original = Payload();
                string reference =
                    "{fileID: "
                    + fileId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ", guid: "
                    + guid
                    + ", type: 2}";
                string source = OdinMigrationDictionaryConverterTests
                    .SourceWithTwoDictionaries()
                    .Replace(
                        "MonoBehaviour:\n",
                        "MonoBehaviour:\n  m_Script: {fileID: 11500000, guid: "
                            + original.ScriptGuid
                            + ", type: 3}\n"
                    )
                    .Replace("UnityEngine.Sprite,", "UnityEngine.Texture2D,")
                    .Replace(
                        "{fileID: 21300000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}",
                        reference
                    );
                Assert.IsTrue(
                    OdinMigrationTool.TryReadSerializedNodes(
                        source,
                        out IReadOnlyList<OdinMigrationSerializedData> payloads,
                        out string failure
                    ),
                    failure
                );
                Assert.IsTrue(
                    OdinMigrationTool.TryConvertBoundSerializedDictionaries(
                        payloads[0],
                        Fields(),
                        path,
                        new UnityEngine.Object[] { texture },
                        out OdinMigrationCompiledBinding binding,
                        out string yaml,
                        out failure
                    ),
                    failure
                );
                Assert.AreEqual(typeof(OdinMigrationBindingAsset), binding.OriginalType);
                StringAssert.Contains(reference, yaml);
                Assert.IsFalse(
                    OdinMigrationTool.TryConvertBoundSerializedDictionaries(
                        payloads[0],
                        Fields(),
                        path,
                        new UnityEngine.Object[] { null },
                        out binding,
                        out yaml,
                        out failure
                    )
                );
                Assert.IsTrue(binding == null);
                Assert.IsTrue(yaml == null);
                StringAssert.Contains("missing or destroyed", failure);
            }
            finally
            {
                Assert.IsTrue(AssetDatabase.DeleteAsset(folder));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void InvalidBindingInputsReturnNoBinding(int invalid)
        {
            OdinMigrationDictionaryField[] fields =
                invalid == 1 ? null
                : invalid == 2 ? Array.Empty<OdinMigrationDictionaryField>()
                : Fields();
            Assert.IsFalse(
                OdinMigrationTool.TryBindSerializedDictionaryFields(
                    invalid == 0 ? null : Payload(),
                    fields,
                    out OdinMigrationCompiledBinding binding,
                    out string failure
                )
            );
            Assert.IsTrue(binding == null);
            StringAssert.Contains("required", failure);
        }

        [Test]
        public void DuplicateRequestedFieldNamesReturnNoBinding()
        {
            OdinMigrationDictionaryField field = Fields()[0];
            Assert.IsFalse(
                OdinMigrationTool.TryBindSerializedDictionaryFields(
                    Payload(),
                    new[] { field, field },
                    out OdinMigrationCompiledBinding binding,
                    out string failure
                )
            );
            Assert.IsTrue(binding == null);
            StringAssert.Contains("unique", failure);
        }

        [Test]
        public void UnknownFieldReturnsNoBinding()
        {
            Assert.IsFalse(
                OdinMigrationTool.TryBindSerializedDictionaryFields(
                    Payload(),
                    new[]
                    {
                        OdinMigrationDictionaryField.Create<SpriteAlignment, Texture2D>(
                            nameof(UnknownFieldReturnsNoBinding)
                        ),
                    },
                    out OdinMigrationCompiledBinding binding,
                    out string failure
                )
            );
            Assert.IsTrue(binding == null);
            StringAssert.Contains("compiled original declaration", failure);
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
        public void IncompleteOrAmbiguousCompilerMetadataReturnsNoBinding(int invalid)
        {
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    typeof(OdinMigrationBindingAsset),
                    out OdinMigrationCompiledBinding original
                )
            );
            OdinMigrationCompiledSource[] sources = new OdinMigrationCompiledSource[
                original.Sources.Count
            ];
            for (int index = 0; index < sources.Length; ++index)
            {
                sources[index] = original.Sources[index];
            }
            OdinMigrationCompiledField field = original.Fields[0];
            OdinMigrationCompiledField changed = new OdinMigrationCompiledField(
                field.Name,
                field.KeyTypeName,
                field.ElementTypeName,
                invalid == 0 ? "wrong enum assembly" : field.KeyAssemblyName,
                invalid == 1 ? "wrong element assembly" : field.ElementAssemblyName,
                invalid == 6 ? "missing source" : field.SourcePath,
                invalid == 2 ? -1
                    : invalid == 4 ? int.MaxValue
                    : field.TypeStart,
                invalid == 3 ? 0 : field.TypeLength
            );
            if (invalid == 7)
            {
                sources[0] = null;
            }
            if (invalid == 8)
            {
                sources[0] = new OdinMigrationCompiledSource(sources[0].Path, null);
            }
            try
            {
                Assert.IsTrue(
                    OdinMigrationCompiledBindings.Register(
                        new OdinMigrationCompiledBinding(
                            original.OriginalType,
                            sources,
                            invalid == 5 ? new[] { changed, changed } : new[] { changed }
                        )
                    )
                );
                Assert.IsFalse(
                    OdinMigrationTool.TryBindSerializedDictionaryFields(
                        Payload(),
                        Fields(),
                        out OdinMigrationCompiledBinding binding,
                        out string failure
                    )
                );
                Assert.IsTrue(binding == null);
                Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
            }
            finally
            {
                Assert.IsTrue(OdinMigrationCompiledBindings.Register(original));
            }
        }

        [Test]
        public void CompilerSnapshotsDoNotExposeCallerOwnedArrays()
        {
            OdinMigrationCompiledSource source = new OdinMigrationCompiledSource(
                "original",
                "checksum"
            );
            OdinMigrationCompiledSource[] sources = { source };
            OdinMigrationCompiledField[] fields = { null };
            OdinMigrationCompiledBinding binding = new OdinMigrationCompiledBinding(
                typeof(OdinMigrationBindingAsset),
                sources,
                fields
            );
            sources[0] = null;
            fields[0] = new OdinMigrationCompiledField(
                "changed",
                null,
                null,
                null,
                null,
                null,
                0,
                0
            );
            Assert.AreSame(source, binding.Sources[0]);
            Assert.IsTrue(binding.Fields[0] == null);
            Assert.IsFalse(binding.Sources is OdinMigrationCompiledSource[]);
            Assert.IsFalse(binding.Fields is OdinMigrationCompiledField[]);
            Assert.IsFalse(OdinMigrationCompiledBindings.Register(null));
            Assert.IsFalse(
                OdinMigrationCompiledBindings.Register(
                    new OdinMigrationCompiledBinding(null, null, null)
                )
            );
            Assert.IsFalse(
                OdinMigrationCompiledBindings.TryGet(null, out OdinMigrationCompiledBinding missing)
            );
            Assert.IsTrue(missing == null);
        }

        [Test]
        public void SourcePreviewRewritesBothDeclarationsAndInitializersWithoutWriting()
        {
            Assert.IsTrue(
                OdinMigrationTool.TryPreviewDictionarySources(
                    Payload(),
                    Fields(),
                    out OdinMigrationDictionarySourcePreview preview,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(1, preview.Sources.Count);
            foreach (KeyValuePair<string, OdinMigrationSourceProposal> pair in preview.Sources)
            {
                OdinMigrationSourceProposal source = pair.Value;
                Assert.AreEqual(pair.Key, source.SourcePath);
                StringAssert.Contains(
                    "SerializableDictionary<global::UnityEngine.SpriteAlignment",
                    source.ProposedSource
                );
                StringAssert.Contains(
                    "SerializableDictionary<global::UnityEngine.HideFlags",
                    source.ProposedSource
                );
                StringAssert.Contains(
                    "new global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableDictionary<",
                    source.ProposedSource
                );
                StringAssert.Contains("global::UnityEngine.SerializeField", source.ProposedSource);
                Assert.AreEqual(
                    source.OriginalSource,
                    OdinMigrationSourceBinder.ReadCompiledSource(preview.Binding.Sources[0])
                );
                StringAssert.DoesNotContain("SerializableDictionary<", source.OriginalSource);
            }
        }

        [Test]
        public void NestedGenericEnumBindingMatchesItsRealRuntimeFullName()
        {
            OdinMigrationGenericBindingAsset asset = Track(
                ScriptableObject.CreateInstance<OdinMigrationGenericBindingAsset>()
            );
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    typeof(OdinMigrationGenericBindingAsset),
                    out OdinMigrationCompiledBinding compiled
                )
            );
            Assert.AreEqual(
                typeof(OdinMigrationGenericKeys<List<int[]>>.Key).FullName,
                compiled.Fields[0].KeyTypeName
            );
            Assert.AreEqual(
                typeof(AttributeTargets).Assembly.FullName,
                compiled.Fields[1].KeyAssemblyName
            );
            OdinMigrationDictionaryField[] fields =
            {
                OdinMigrationDictionaryField.Create<
                    OdinMigrationGenericKeys<List<int[]>>.Key,
                    Texture2D
                >(nameof(OdinMigrationGenericBindingAsset.Variations)),
                OdinMigrationDictionaryField.Create<AttributeTargets, Texture2D>(
                    nameof(OdinMigrationGenericBindingAsset.Other)
                ),
            };
            Assert.IsTrue(
                OdinMigrationTool.TryBindSerializedDictionaryFields(
                    Payload(asset),
                    fields,
                    out OdinMigrationCompiledBinding binding,
                    out string failure
                ),
                failure
            );
            Assert.AreSame(compiled, binding);
            Assert.IsTrue(
                OdinMigrationTool.TryPreviewDictionarySources(
                    Payload(asset),
                    fields,
                    out OdinMigrationDictionarySourcePreview preview,
                    out failure
                ),
                failure
            );
            Assert.Greater(preview.Sources.Count, 0);
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
        public void IncompleteOrConflictingSourceReplacementsReturnNoPreview(int invalid)
        {
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    typeof(OdinMigrationBindingAsset),
                    out OdinMigrationCompiledBinding original
                )
            );
            OdinMigrationCompiledField field = original.Fields[0];
            OdinMigrationCompiledSourceEdit changed = new OdinMigrationCompiledSourceEdit(
                invalid == 1 ? "missing source" : field.SourcePath,
                invalid == 2 ? -1
                    : invalid == 4 ? int.MaxValue
                    : field.TypeStart,
                invalid == 3 ? -1 : field.TypeLength,
                invalid == 5 ? null : "different replacement"
            );
            OdinMigrationCompiledSourceEdit[] edits =
                invalid == 0 ? new OdinMigrationCompiledSourceEdit[] { null }
                : invalid == 6 ? new[] { changed, field.SourceEdits[0] }
                : invalid == 7 ? Array.Empty<OdinMigrationCompiledSourceEdit>()
                : new[] { changed };
            OdinMigrationCompiledField[] fields = new OdinMigrationCompiledField[
                original.Fields.Count
            ];
            for (int index = 0; index < fields.Length; ++index)
            {
                fields[index] = original.Fields[index];
            }
            fields[0] = new OdinMigrationCompiledField(
                field.Name,
                field.KeyTypeName,
                field.ElementTypeName,
                field.KeyAssemblyName,
                field.ElementAssemblyName,
                field.SourcePath,
                field.TypeStart,
                field.TypeLength,
                edits,
                invalid == 8 ? "source requires review" : null
            );
            OdinMigrationCompiledSource[] sources = new OdinMigrationCompiledSource[
                original.Sources.Count
            ];
            for (int index = 0; index < sources.Length; ++index)
            {
                sources[index] = original.Sources[index];
            }
            try
            {
                Assert.IsTrue(
                    OdinMigrationCompiledBindings.Register(
                        new OdinMigrationCompiledBinding(original.OriginalType, sources, fields)
                    )
                );
                Assert.IsFalse(
                    OdinMigrationTool.TryPreviewDictionarySources(
                        Payload(),
                        Fields(),
                        out OdinMigrationDictionarySourcePreview preview,
                        out string failure
                    )
                );
                Assert.IsTrue(preview == null);
                Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
            }
            finally
            {
                Assert.IsTrue(OdinMigrationCompiledBindings.Register(original));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void InvalidSourcePreviewInputsReturnNoPreview(int invalid)
        {
            Assert.IsFalse(
                OdinMigrationTool.TryPreviewDictionarySources(
                    invalid == 0 ? null : Payload(),
                    invalid == 1 ? null
                        : invalid == 2 ? Array.Empty<OdinMigrationDictionaryField>()
                        : Fields(),
                    out OdinMigrationDictionarySourcePreview preview,
                    out string failure
                )
            );
            Assert.IsTrue(preview == null);
            StringAssert.Contains("required", failure);
        }

        private OdinMigrationSerializedData Payload()
        {
            OdinMigrationBindingAsset asset = Track(
                ScriptableObject.CreateInstance<OdinMigrationBindingAsset>()
            );
            return Payload(asset);
        }

        private OdinMigrationSerializedData Payload(ScriptableObject asset)
        {
            MonoScript script = MonoScript.FromScriptableObject(asset);
            Assert.IsTrue(script != null);
            Assert.IsTrue(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out string guid, out long _)
            );
            string source = OdinMigrationDictionaryConverterTests
                .SourceWithTwoDictionaries()
                .Replace(
                    "MonoBehaviour:\n",
                    "MonoBehaviour:\n  m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}\n"
                )
                .Replace("UnityEngine.Sprite,", "UnityEngine.Texture2D,");
            Assert.IsTrue(
                OdinMigrationTool.TryReadSerializedNodes(
                    source,
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(guid, payloads[0].ScriptGuid);
            return payloads[0];
        }
    }
}
