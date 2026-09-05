// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests
{
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Persistence;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class MultiFileSelectorPersistenceManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            EditorPrefs.DeleteKey("WallstopStudios.MultiFileSelector.scopes");
            EditorPrefs.DeleteKey("WallstopStudios.MultiFileSelector.lastUsed.OldScope");
            EditorPrefs.DeleteKey("WallstopStudios.MultiFileSelector.lastSearch.OldScope");
            EditorPrefs.DeleteKey("WallstopStudios.MultiFileSelector.lastDirectory.OldScope");
            EditorPrefs.DeleteKey("WallstopStudios.MultiFileSelector.lastUsed.NewScope");
            EditorPrefs.DeleteKey("WallstopStudios.MultiFileSelector.lastSearch.NewScope");
            EditorPrefs.DeleteKey("WallstopStudios.MultiFileSelector.lastDirectory.NewScope");
            EditorPrefs.DeleteKey("WallstopStudios.MultiFileSelector.cleanup.autoEnabled");
            EditorPrefs.DeleteKey("WallstopStudios.MultiFileSelector.cleanup.maxAgeDays");
        }

        [TearDown]
        public void TearDown()
        {
            SetUp();
        }

        [Test]
        public void ManagerSettingsRoundTrip()
        {
            MultiFileSelectorPersistenceManager.SetAutoCleanupEnabled(false);
            Assert.False(
                MultiFileSelectorPersistenceManager.IsAutoCleanupEnabled(),
                "Auto cleanup should be false after setting false."
            );

            MultiFileSelectorPersistenceManager.SetAutoCleanupEnabled(true);
            Assert.True(
                MultiFileSelectorPersistenceManager.IsAutoCleanupEnabled(),
                "Auto cleanup should be true after setting true."
            );

            MultiFileSelectorPersistenceManager.SetMaxAgeDays(7);
            Assert.That(
                MultiFileSelectorPersistenceManager.GetMaxAgeDays(),
                Is.EqualTo(7),
                "Max age days should be stored and retrieved."
            );

            MultiFileSelectorPersistenceManager.SetMaxAgeDays(-5);
            Assert.That(
                MultiFileSelectorPersistenceManager.GetMaxAgeDays(),
                Is.EqualTo(30),
                "Invalid days should coerce to default 30."
            );
        }

        [Test]
        public void ManagerRunsCleanupUsingSettings()
        {
            EditorPrefs.SetString("WallstopStudios.MultiFileSelector.scopes", "OldScope;NewScope");
            long oldTicks = DateTime.UtcNow.AddDays(-60).Ticks;
            long newTicks = DateTime.UtcNow.Ticks;
            EditorPrefs.SetString(
                "WallstopStudios.MultiFileSelector.lastUsed.OldScope",
                oldTicks.ToString()
            );
            EditorPrefs.SetString("WallstopStudios.MultiFileSelector.lastSearch.OldScope", "x");
            EditorPrefs.SetString(
                "WallstopStudios.MultiFileSelector.lastDirectory.OldScope",
                "Assets"
            );

            EditorPrefs.SetString(
                "WallstopStudios.MultiFileSelector.lastUsed.NewScope",
                newTicks.ToString()
            );
            EditorPrefs.SetString("WallstopStudios.MultiFileSelector.lastSearch.NewScope", "y");
            EditorPrefs.SetString(
                "WallstopStudios.MultiFileSelector.lastDirectory.NewScope",
                "Assets"
            );

            MultiFileSelectorPersistenceManager.SetMaxAgeDays(30);
            MultiFileSelectorPersistenceManager.RunCleanupNow();

            Assert.False(
                EditorPrefs.HasKey("WallstopStudios.MultiFileSelector.lastUsed.OldScope"),
                "Old scope should be removed by manager cleanup."
            );
            Assert.True(
                EditorPrefs.HasKey("WallstopStudios.MultiFileSelector.lastUsed.NewScope"),
                "New scope should be retained by manager cleanup."
            );
        }
    }
}
