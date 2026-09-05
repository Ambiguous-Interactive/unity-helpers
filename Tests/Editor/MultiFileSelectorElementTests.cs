// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Visuals.UIToolkit;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class MultiFileSelectorElementTests
    {
        private string _baseRel;
        private string _baseAbs;
        private string _baseMeta;
        private static readonly string[] PrefKeys =
        {
            "WallstopStudios.MultiFileSelector.lastSearch.TestScope1",
            "WallstopStudios.MultiFileSelector.lastSearch.KeyA",
            "WallstopStudios.MultiFileSelector.lastSearch.KeyB",
            "WallstopStudios.MultiFileSelector.lastSearch.SelScope",
            "WallstopStudios.MultiFileSelector.lastDirectory.DirKey",
            "WallstopStudios.MultiFileSelector.lastDirectory.DirTestA",
            "WallstopStudios.MultiFileSelector.lastUsed.KeyA",
            "WallstopStudios.MultiFileSelector.lastUsed.KeyB",
            "WallstopStudios.MultiFileSelector.scopes",
        };

        [SetUp]
        public void SetUp()
        {
            _baseRel = "Assets/TempMultiFileSelectorTests";
            _baseAbs = Path.Combine(Application.dataPath, "TempMultiFileSelectorTests");
            _baseMeta = _baseAbs + ".meta";
            if (Directory.Exists(_baseAbs))
            {
                Directory.Delete(_baseAbs, recursive: true);
            }
            if (File.Exists(_baseMeta))
            {
                File.Delete(_baseMeta);
            }

            Directory.CreateDirectory(_baseAbs);
            Directory.CreateDirectory(Path.Combine(_baseAbs, "DirA"));
            Directory.CreateDirectory(Path.Combine(_baseAbs, "DirB"));

            File.WriteAllText(Path.Combine(_baseAbs, "a.txt"), "a");
            File.WriteAllText(Path.Combine(_baseAbs, "image.bytes"), "img");
            File.WriteAllText(Path.Combine(_baseAbs, "foo.txt"), "foo");
            File.WriteAllText(Path.Combine(_baseAbs, "bar.txt"), "bar");
            File.WriteAllText(Path.Combine(_baseAbs, "DirA/nested.txt"), "nested");

            TestContext.WriteLine($"[MultiFileSelectorElementTests] Test root: {_baseAbs}");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string key in PrefKeys)
            {
                EditorPrefs.DeleteKey(key);
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            try
            {
                if (Directory.Exists(_baseAbs))
                {
                    Directory.Delete(_baseAbs, recursive: true);
                }
                if (File.Exists(_baseMeta))
                {
                    File.Delete(_baseMeta);
                }
                TestContext.WriteLine(
                    "[MultiFileSelectorElementTests] Cleaned test root (best effort)."
                );
            }
            catch (IOException io)
            {
                Debug.LogWarning(
                    $"[MultiFileSelectorElementTests] Unable to delete '{_baseAbs}' due to IO exception: {io.Message}. Manual cleanup may be required."
                );
            }
        }

        [Test]
        public void FolderFirstAndSorting()
        {
            MultiFileSelectorElement selector = new(_baseRel, null, "TestScope1");
            IReadOnlyList<string> names = selector.DebugGetVisibleEntryNames();

            Assert.That(
                names.Count,
                Is.GreaterThanOrEqualTo(5),
                "Expected directories and files to be listed."
            );
            Assert.That(
                names[0],
                Is.EqualTo("DirA"),
                "Folders should be sorted and appear before files."
            );
            Assert.That(names[1], Is.EqualTo("DirB"), "Second folder should be DirB.");
        }

        [Test]
        public void ExtensionFilterNormalizationWorks()
        {
            MultiFileSelectorElement selector = new(_baseRel, new[] { "txt" }, "TestScopeExt");
            IReadOnlyList<string> names = selector.DebugGetVisibleEntryNames();

            CollectionAssert.Contains(names, "a.txt");
            CollectionAssert.Contains(names, "foo.txt");
            CollectionAssert.Contains(names, "bar.txt");
            CollectionAssert.DoesNotContain(names, "image.bytes");
        }

        [Test]
        public void SearchPersistenceIsScoped()
        {
            EditorPrefs.SetString("WallstopStudios.MultiFileSelector.lastSearch.KeyA", "foo");
            EditorPrefs.SetString("WallstopStudios.MultiFileSelector.lastSearch.KeyB", "bar");

            MultiFileSelectorElement selA = new(_baseRel, null, "KeyA");
            MultiFileSelectorElement selB = new(_baseRel, null, "KeyB");

            IReadOnlyList<string> namesA = selA.DebugGetVisibleEntryNames();
            IReadOnlyList<string> namesB = selB.DebugGetVisibleEntryNames();

            Assert.That(
                namesA.Any(n => n.Equals("foo.txt", StringComparison.OrdinalIgnoreCase)),
                "Selector A should include foo.txt by search."
            );
            Assert.False(
                namesA.Any(n => n.Equals("bar.txt", StringComparison.OrdinalIgnoreCase)),
                "Selector A should not include bar.txt by search."
            );

            Assert.That(
                namesB.Any(n => n.Equals("bar.txt", StringComparison.OrdinalIgnoreCase)),
                "Selector B should include bar.txt by search."
            );
            Assert.False(
                namesB.Any(n => n.Equals("foo.txt", StringComparison.OrdinalIgnoreCase)),
                "Selector B should not include foo.txt by search."
            );
        }

        [Test]
        public void NoPersistenceWhenNoKey()
        {
            EditorPrefs.SetString("WallstopStudios.MultiFileSelector.lastSearch", "foo");
            MultiFileSelectorElement selector = new(_baseRel, null);
            IReadOnlyList<string> names = selector.DebugGetVisibleEntryNames();

            Assert.That(
                names.Any(n => n.Equals("foo.txt", StringComparison.OrdinalIgnoreCase)),
                "Expected foo.txt to be visible without scoped persistence."
            );
            Assert.That(
                names.Any(n => n.Equals("bar.txt", StringComparison.OrdinalIgnoreCase)),
                "Expected bar.txt to be visible without scoped persistence."
            );
        }

        [Test]
        public void SelectionHelpersAffectSelection()
        {
            MultiFileSelectorElement selector = new(_baseRel, new[] { ".txt" }, "SelScope");

            selector.SelectAllInView();
            IReadOnlyCollection<string> selected = selector.DebugGetSelectedFilePaths();
            Assert.That(
                selected.Count,
                Is.EqualTo(3),
                "Select All should select all visible .txt files."
            );

            selector.InvertSelectionInView();
            selected = selector.DebugGetSelectedFilePaths();
            Assert.That(
                selected.Count,
                Is.EqualTo(0),
                "Invert on fully selected should deselect all in view."
            );

            selector.ClearSelectionInView();
            selected = selector.DebugGetSelectedFilePaths();
            Assert.That(selected.Count, Is.EqualTo(0), "Clear should result in zero selection.");

            selector.InvertSelectionInView();
            selected = selector.DebugGetSelectedFilePaths();
            Assert.That(
                selected.Count,
                Is.EqualTo(3),
                "Invert on empty should select all in view."
            );
        }

        [Test]
        public void DirectoryPersistenceIsScoped()
        {
            MultiFileSelectorElement selector = new(_baseRel, null, "DirKey");

            selector.NavigateTo(_baseRel + "/DirA");

            MultiFileSelectorElement selector2 = new(_baseRel, null, "DirKey");
            IReadOnlyList<string> names = selector2.DebugGetVisibleEntryNames();
            CollectionAssert.Contains(
                names,
                "nested.txt",
                "Expected to be inside DirA due to scoped directory persistence."
            );
        }

        [Test]
        public void DisabledPersistenceDoesNotLoadOrSave()
        {
            EditorPrefs.SetString("WallstopStudios.MultiFileSelector.lastSearch", "foo");
            MultiFileSelectorElement selector = new(_baseRel, null);
            IReadOnlyList<string> names = selector.DebugGetVisibleEntryNames();
            Assert.That(
                names.Any(n => n.Equals("bar.txt", StringComparison.OrdinalIgnoreCase)),
                "No key means no scoped loading; do not filter by previous value."
            );
        }

        [Test]
        public void CleanupRemovesStaleScopes()
        {
            EditorPrefs.SetString("WallstopStudios.MultiFileSelector.scopes", "OldScope;NewScope");
            long oldTicks = DateTime.UtcNow.AddDays(-90).Ticks;
            long newTicks = DateTime.UtcNow.Ticks;
            EditorPrefs.SetString(
                "WallstopStudios.MultiFileSelector.lastUsed.OldScope",
                oldTicks.ToString()
            );
            EditorPrefs.SetString("WallstopStudios.MultiFileSelector.lastSearch.OldScope", "foo");
            EditorPrefs.SetString(
                "WallstopStudios.MultiFileSelector.lastDirectory.OldScope",
                "Assets"
            );

            EditorPrefs.SetString(
                "WallstopStudios.MultiFileSelector.lastUsed.NewScope",
                newTicks.ToString()
            );
            EditorPrefs.SetString("WallstopStudios.MultiFileSelector.lastSearch.NewScope", "bar");
            EditorPrefs.SetString(
                "WallstopStudios.MultiFileSelector.lastDirectory.NewScope",
                "Assets"
            );

            MultiFileSelectorElement.CleanupStalePersistenceEntries(TimeSpan.FromDays(30));

            Assert.False(
                EditorPrefs.HasKey("WallstopStudios.MultiFileSelector.lastUsed.OldScope"),
                "Old scope lastUsed should be removed."
            );
            Assert.False(
                EditorPrefs.HasKey("WallstopStudios.MultiFileSelector.lastSearch.OldScope"),
                "Old scope search should be removed."
            );
            Assert.False(
                EditorPrefs.HasKey("WallstopStudios.MultiFileSelector.lastDirectory.OldScope"),
                "Old scope directory should be removed."
            );

            Assert.True(
                EditorPrefs.HasKey("WallstopStudios.MultiFileSelector.lastUsed.NewScope"),
                "New scope lastUsed should be retained."
            );
            Assert.True(
                EditorPrefs.HasKey("WallstopStudios.MultiFileSelector.lastSearch.NewScope"),
                "New scope search should be retained."
            );
            Assert.True(
                EditorPrefs.HasKey("WallstopStudios.MultiFileSelector.lastDirectory.NewScope"),
                "New scope directory should be retained."
            );

            string scopes = EditorPrefs.GetString("WallstopStudios.MultiFileSelector.scopes", "");
            Assert.That(
                scopes,
                Is.EqualTo("NewScope"),
                "Scopes index should be pruned to survivors."
            );
        }
    }
}
