// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Slow")]
    [Category("CompilationIsolation")]
    public sealed class OdinMigrationDictionaryCompilationTests : CommonTestBase
    {
        private static IReadOnlyDictionary<string, OdinMigrationSourceProposal> Sources()
        {
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    typeof(OdinMigrationBindingAsset),
                    out OdinMigrationCompiledBinding binding
                )
            );
            OdinMigrationDictionaryField[] fields =
            {
                OdinMigrationDictionaryField.Create<SpriteAlignment, Texture2D>(
                    nameof(OdinMigrationBindingAsset.Variations)
                ),
                OdinMigrationDictionaryField.Create<HideFlags, Texture2D>(
                    nameof(OdinMigrationBindingAsset.Other)
                ),
            };
            Assert.IsTrue(
                OdinMigrationDictionarySourceRewriter.TryPreview(
                    binding,
                    fields,
                    out OdinMigrationDictionarySourcePreview preview,
                    out string failure
                ),
                failure
            );
            return preview.Sources;
        }

        private static IEnumerator Await(OdinMigrationDictionaryCompilation compilation)
        {
            double deadline = UnityEditor.EditorApplication.timeSinceStartup + 600;
            while (
                !compilation.Completed && UnityEditor.EditorApplication.timeSinceStartup < deadline
            )
            {
                yield return null;
            }
            Assert.IsTrue(
                compilation.Completed,
                "Isolated compilation did not finish within the fixture deadline."
            );
        }

        [UnitySetUp]
        public IEnumerator WaitForEditorReady()
        {
            yield return null;
            double deadline = UnityEditor.EditorApplication.timeSinceStartup + 60;
            while (
                (
                    UnityEditor.EditorApplication.isCompiling
                    || UnityEditor.EditorApplication.isUpdating
                )
                && UnityEditor.EditorApplication.timeSinceStartup < deadline
            )
            {
                yield return null;
            }
            Assert.IsFalse(
                UnityEditor.EditorApplication.isCompiling,
                "Editor compilation did not finish."
            );
            Assert.IsFalse(
                UnityEditor.EditorApplication.isUpdating,
                "Editor import did not finish."
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void InvalidSourcesNeverStartACompiler(int invalid)
        {
            IReadOnlyDictionary<string, OdinMigrationSourceProposal> sources =
                invalid == 0 ? null
                : invalid == 1 ? new Dictionary<string, OdinMigrationSourceProposal>()
                : new Dictionary<string, OdinMigrationSourceProposal>
                {
                    {
                        "missing",
                        new OdinMigrationSourceProposal(
                            "Assets/MissingMigrationSource.cs",
                            "original",
                            "candidate"
                        )
                    },
                };
            Assert.IsFalse(
                OdinMigrationDictionaryCompilation.TryStart(
                    sources,
                    out OdinMigrationDictionaryCompilation compilation,
                    out string failure
                )
            );
            Assert.IsTrue(compilation == null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
        }

        [UnityTest]
        public IEnumerator ProposedSourcesAndTransitiveCallersCompileWithoutChangingOriginals()
        {
            IReadOnlyDictionary<string, OdinMigrationSourceProposal> sources = Sources();
            Dictionary<string, byte[]> originals = new Dictionary<string, byte[]>();
            foreach (KeyValuePair<string, OdinMigrationSourceProposal> source in sources)
            {
                originals.Add(
                    source.Key,
                    File.ReadAllBytes(OdinMigrationSourceBinder.FullPath(source.Key))
                );
            }
            Assert.IsTrue(
                OdinMigrationDictionaryCompilation.TryStart(
                    sources,
                    out OdinMigrationDictionaryCompilation compilation,
                    out string failure
                ),
                failure
            );
            using (compilation)
            {
                yield return Await(compilation);
                Assert.IsTrue(
                    compilation.Succeeded,
                    compilation.Failure + "\n" + string.Join("\n", compilation.Diagnostics)
                );
                Assert.IsTrue(compilation.TryValidateCurrent(out failure), failure);
                CollectionAssert.Contains(
                    compilation.Assemblies,
                    typeof(OdinMigrationBindingAsset).Assembly.GetName().Name
                );
                CollectionAssert.Contains(
                    compilation.Assemblies,
                    typeof(OdinMigrationDictionaryCompilationTests).Assembly.GetName().Name
                );
                CollectionAssert.AreEqual(compilation.Assemblies, compilation.CompiledAssemblies);
                foreach (KeyValuePair<string, byte[]> original in originals)
                {
                    CollectionAssert.AreEqual(
                        original.Value,
                        File.ReadAllBytes(OdinMigrationSourceBinder.FullPath(original.Key))
                    );
                }
                Assert.IsFalse(compilation.Cancelled);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StaleOrOversizedProposalsNeverStartACompiler(bool oversized)
        {
            Dictionary<string, OdinMigrationSourceProposal> sources =
                new Dictionary<string, OdinMigrationSourceProposal>();
            foreach (KeyValuePair<string, OdinMigrationSourceProposal> source in Sources())
            {
                sources.Add(
                    source.Key,
                    new OdinMigrationSourceProposal(
                        source.Key,
                        oversized ? source.Value.OriginalSource : source.Value.OriginalSource + " ",
                        oversized
                            ? new string(' ', OdinMigrationSourceBinder.MaximumSourceCharacters + 1)
                            : source.Value.ProposedSource
                    )
                );
            }
            Assert.IsFalse(
                OdinMigrationDictionaryCompilation.TryStart(
                    sources,
                    out OdinMigrationDictionaryCompilation compilation,
                    out string failure
                )
            );
            Assert.IsTrue(compilation == null);
            StringAssert.Contains(oversized ? "size limit" : "changed", failure);
        }

        [UnityTest]
        public IEnumerator InvalidCandidateReturnsCompilerErrors()
        {
            Dictionary<string, OdinMigrationSourceProposal> sources =
                new Dictionary<string, OdinMigrationSourceProposal>();
            foreach (KeyValuePair<string, OdinMigrationSourceProposal> source in Sources())
            {
                sources.Add(
                    source.Key,
                    new OdinMigrationSourceProposal(
                        source.Key,
                        source.Value.OriginalSource,
                        source.Value.ProposedSource + "\n invalid migration syntax"
                    )
                );
            }
            Assert.IsTrue(
                OdinMigrationDictionaryCompilation.TryStart(
                    sources,
                    out OdinMigrationDictionaryCompilation compilation,
                    out string failure
                ),
                failure
            );
            using (compilation)
            {
                yield return Await(compilation);
                Assert.IsFalse(compilation.Succeeded);
                Assert.IsFalse(compilation.TryValidateCurrent(out failure));
                Assert.IsFalse(string.IsNullOrWhiteSpace(compilation.Failure));
                Assert.IsNotEmpty(compilation.Diagnostics);
            }
        }

        [UnityTest]
        public IEnumerator AChangedFieldThatBreaksAnEditorCallerIsRejected()
        {
            Dictionary<string, OdinMigrationSourceProposal> sources =
                new Dictionary<string, OdinMigrationSourceProposal>();
            foreach (KeyValuePair<string, OdinMigrationSourceProposal> source in Sources())
            {
                sources.Add(
                    source.Key,
                    new OdinMigrationSourceProposal(
                        source.Key,
                        source.Value.OriginalSource,
                        source.Value.ProposedSource.Replace(
                            nameof(OdinMigrationBindingAsset.Variations),
                            "ChangedVariations"
                        )
                    )
                );
            }
            Assert.IsTrue(
                OdinMigrationDictionaryCompilation.TryStart(
                    sources,
                    out OdinMigrationDictionaryCompilation compilation,
                    out string failure
                ),
                failure
            );
            using (compilation)
            {
                yield return Await(compilation);
                Assert.IsFalse(compilation.Succeeded);
                StringAssert.Contains("dependent caller", compilation.Failure);
                CollectionAssert.Contains(
                    compilation.CompiledAssemblies,
                    typeof(OdinMigrationBindingAsset).Assembly.GetName().Name
                );
                StringAssert.Contains(
                    nameof(OdinMigrationBindingAsset.Variations),
                    string.Join("\n", compilation.Diagnostics)
                );
            }
        }

        [Test]
        public void CancellationBeforeTheFirstBuildFinishesAndReleasesTheCompiler()
        {
            Assert.IsTrue(
                OdinMigrationDictionaryCompilation.TryStart(
                    Sources(),
                    out OdinMigrationDictionaryCompilation compilation,
                    out string failure
                ),
                failure
            );
            Assert.IsFalse(
                OdinMigrationDictionaryCompilation.TryStart(
                    Sources(),
                    out OdinMigrationDictionaryCompilation second,
                    out failure
                )
            );
            Assert.IsTrue(second == null);
            compilation.Dispose();
            Assert.IsTrue(compilation.Completed);
            Assert.IsTrue(compilation.Cancelled);
            Assert.IsFalse(compilation.Succeeded);
            Assert.IsFalse(compilation.TryValidateCurrent(out failure));
            compilation.Dispose();
            Assert.IsTrue(
                OdinMigrationDictionaryCompilation.TryStart(Sources(), out second, out failure),
                failure
            );
            second.Dispose();
        }
    }
}
