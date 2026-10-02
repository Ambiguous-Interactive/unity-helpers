// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PrefabCheckerReportTests
    {
        private string _temporaryDirectory;

        private static int CountProperties(JsonElement element)
        {
            int count = 0;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                ++count;
            }
            return count;
        }

        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_temporaryDirectory, true);
        }

        [Test]
        public void NativeLegacyJsonOmitsReadonlyReportFields()
        {
            PrefabChecker.ScanReport report = new(new[] { "Assets/Prefabs" });
            report.Add("Assets/Prefabs/Player.prefab", new List<string> { "Missing reference" });
            string json = JsonUtility.ToJson(report, true);
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.That(document.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(CountProperties(document.RootElement), Is.Zero);
        }

        [Test]
        public void ExportJsonPreservesReadonlyFoldersAndEveryFindingField()
        {
            PrefabChecker.ScanReport report = new(new[] { "Assets/Prefabs", "Assets/UI" });
            string path = "Assets/Prefabs/Player.prefab";
            string message = "Missing \"target\"\n<child> & \u96ea";
            report.Add(path, new List<string> { message, string.Empty });
            report.Add("Assets/UI/Menu.prefab", null);
            string destination = Path.Combine(_temporaryDirectory, "report.json");
            File.WriteAllText(destination, "previous report");

            Assert.That(
                PrefabChecker.TryExportReportJson(report, destination, out Exception error),
                Is.True
            );
            Assert.That(error, Is.Null);
            string saved = File.ReadAllText(destination);
            Assert.That(saved, Does.Contain("\n"));
            using JsonDocument document = JsonDocument.Parse(saved);
            JsonElement root = document.RootElement;
            Assert.That(CountProperties(root), Is.EqualTo(2));
            JsonElement folders = root.GetProperty(nameof(PrefabChecker.ScanReport.folders));
            Assert.That(folders.GetArrayLength(), Is.EqualTo(2));
            Assert.That(folders[0].GetString(), Is.EqualTo(report.folders[0]));
            Assert.That(folders[1].GetString(), Is.EqualTo(report.folders[1]));
            JsonElement items = root.GetProperty(nameof(PrefabChecker.ScanReport.items));
            Assert.That(items.GetArrayLength(), Is.EqualTo(2));
            Assert.That(CountProperties(items[0]), Is.EqualTo(2));
            Assert.That(
                items[0].GetProperty(nameof(PrefabChecker.ScanReport.Item.path)).GetString(),
                Is.EqualTo(path)
            );
            JsonElement messages = items[0]
                .GetProperty(nameof(PrefabChecker.ScanReport.Item.messages));
            Assert.That(messages.GetArrayLength(), Is.EqualTo(2));
            Assert.That(messages[0].GetString(), Is.EqualTo(message));
            Assert.That(messages[1].GetString(), Is.Empty);
            Assert.That(
                items[1]
                    .GetProperty(nameof(PrefabChecker.ScanReport.Item.messages))
                    .GetArrayLength(),
                Is.Zero
            );
            Assert.That(Directory.GetFiles(_temporaryDirectory), Has.Length.EqualTo(1));
        }

        [Test]
        public void ExportJsonPreservesNativeLegacyFindingSchema()
        {
            PrefabChecker.ScanReport report = new(new[] { "Assets/Prefabs" });
            report.Add("Assets/Prefabs/Player.prefab", new List<string> { "Missing reference" });
            using JsonDocument legacy = JsonDocument.Parse(JsonUtility.ToJson(report.items[0]));
            string destination = Path.Combine(_temporaryDirectory, "report.json");
            Assert.That(
                PrefabChecker.TryExportReportJson(report, destination, out Exception error),
                Is.True
            );
            Assert.That(error, Is.Null);
            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(destination));
            JsonElement item = saved.RootElement.GetProperty(
                nameof(PrefabChecker.ScanReport.items)
            )[0];
            Assert.That(CountProperties(item), Is.EqualTo(CountProperties(legacy.RootElement)));
            Assert.That(
                item.GetProperty(nameof(PrefabChecker.ScanReport.Item.path)).GetString(),
                Is.EqualTo(
                    legacy
                        .RootElement.GetProperty(nameof(PrefabChecker.ScanReport.Item.path))
                        .GetString()
                )
            );
            Assert.That(
                item.GetProperty(nameof(PrefabChecker.ScanReport.Item.messages))[0].GetString(),
                Is.EqualTo(
                    legacy
                        .RootElement.GetProperty(nameof(PrefabChecker.ScanReport.Item.messages))[0]
                        .GetString()
                )
            );
        }

        [Test]
        public void ExportJsonPreservesTenThousandFindings()
        {
            PrefabChecker.ScanReport report = new(new[] { "Assets/Prefabs" });
            List<string> messages = new() { "Missing reference" };
            for (int index = 0; index < 10000; ++index)
            {
                report.Add($"Assets/Prefabs/Player{index}.prefab", messages);
            }
            string destination = Path.Combine(_temporaryDirectory, "large.json");
            Assert.That(
                PrefabChecker.TryExportReportJson(report, destination, out Exception error),
                Is.True
            );
            Assert.That(error, Is.Null);
            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(destination));
            JsonElement items = saved.RootElement.GetProperty(
                nameof(PrefabChecker.ScanReport.items)
            );
            Assert.That(items.GetArrayLength(), Is.EqualTo(10000));
            Assert.That(
                items[9999].GetProperty(nameof(PrefabChecker.ScanReport.Item.path)).GetString(),
                Is.EqualTo("Assets/Prefabs/Player9999.prefab")
            );
        }

        [Test]
        public void ExportJsonIncludesEmptyCollections()
        {
            PrefabChecker.ScanReport report = new(null);
            string destination = Path.Combine(_temporaryDirectory, "empty.json");
            Assert.That(
                PrefabChecker.TryExportReportJson(report, destination, out Exception error),
                Is.True
            );
            Assert.That(error, Is.Null);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(destination));
            Assert.That(
                document
                    .RootElement.GetProperty(nameof(PrefabChecker.ScanReport.folders))
                    .GetArrayLength(),
                Is.Zero
            );
            Assert.That(
                document
                    .RootElement.GetProperty(nameof(PrefabChecker.ScanReport.items))
                    .GetArrayLength(),
                Is.Zero
            );
        }

        [Test]
        public void ExportJsonRejectsNullReportWithoutReplacingExistingFile()
        {
            string destination = Path.Combine(_temporaryDirectory, "report.json");
            File.WriteAllText(destination, "previous report");
            Assert.That(
                PrefabChecker.TryExportReportJson(null, destination, out Exception error),
                Is.False
            );
            Assert.That(error, Is.TypeOf<ArgumentNullException>());
            Assert.That(File.ReadAllText(destination), Is.EqualTo("previous report"));
        }

        [TestCase(null, TestName = "ExportJson.NullPath.ReturnsFailure")]
        [TestCase("", TestName = "ExportJson.EmptyPath.ReturnsFailure")]
        [TestCase(" ", TestName = "ExportJson.WhitespacePath.ReturnsFailure")]
        public void ExportJsonRejectsInvalidDestination(string destination)
        {
            PrefabChecker.ScanReport report = new(Array.Empty<string>());
            Assert.That(
                PrefabChecker.TryExportReportJson(report, destination, out Exception error),
                Is.False
            );
            Assert.That(error, Is.TypeOf<ArgumentException>());
        }

        [Test]
        public void ExportJsonReturnsWriteFailureWithoutLeavingStagedFile()
        {
            PrefabChecker.ScanReport report = new(Array.Empty<string>());
            Assert.That(
                PrefabChecker.TryExportReportJson(report, _temporaryDirectory, out Exception error),
                Is.False
            );
            Assert.That(error, Is.Not.Null);
            Assert.That(Directory.GetFiles(_temporaryDirectory), Is.Empty);
            Assert.That(File.Exists(_temporaryDirectory + ".tmp"), Is.False);
        }

        [Test]
        public void ScanReportConstructorCopiesFolders()
        {
            PrefabChecker.ScanReport report = new(new[] { "A", "B" });
            string[] folders = report.folders;
            CollectionAssert.AreEqual(new[] { "A", "B" }, folders);
        }

        [Test]
        public void ScanReportAddCopiesMessages()
        {
            PrefabChecker.ScanReport report = new(Array.Empty<string>());
            report.Add("path.prefab", new List<string> { "m1", "m2" });
            Assert.AreEqual(1, report.items.Count);
            PrefabChecker.ScanReport.Item first = report.items[0];
            Assert.AreEqual("path.prefab", first.path);
            string[] messages = first.messages;
            CollectionAssert.AreEqual(new[] { "m1", "m2" }, messages);
        }
    }
#endif
}
