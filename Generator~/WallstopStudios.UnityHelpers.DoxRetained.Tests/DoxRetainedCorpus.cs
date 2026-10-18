// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.DoxRetained.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text.Json.Nodes;
    using NUnit.Framework;

    internal static class DoxRetainedCorpus
    {
        internal static IEnumerable<string> Ids
        {
            get
            {
                for (int index = 0; index < 10; ++index)
                {
                    yield return "dox-progress-row-" + index.ToString(CultureInfo.InvariantCulture);
                }
                yield return "dox-settings-retained";
            }
        }

        internal static JsonObject ReadInventory()
        {
            return JsonNode.Parse(Read("DoxRetainedInventory.v1.json")).AsObject();
        }

        internal static byte[] Read(string relative)
        {
            return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, relative));
        }

        internal static string Hash(byte[] bytes)
        {
            using SHA256 algorithm = SHA256.Create();
            return Convert.ToHexString(algorithm.ComputeHash(bytes)).ToLowerInvariant();
        }

        internal static string FileFor(string id)
        {
            int index = 0;
            foreach (string known in Ids)
            {
                if (string.Equals(id, known, StringComparison.Ordinal))
                {
                    return index == 10
                        ? "Data/settings-wire-oracle.bin"
                        : "Data/progress-wire-"
                            + index.ToString(CultureInfo.InvariantCulture)
                            + ".bin";
                }
                ++index;
            }
            Assert.Fail("Unexpected retained identity: " + id);
            return string.Empty;
        }

        internal static void Validate(JsonObject inventory, Func<string, byte[]> read)
        {
            Assert.AreEqual(1, inventory["schemaVersion"].GetValue<int>());
            Assert.AreEqual("dox-retained-v1", inventory["panel"].GetValue<string>());
            ValidateFiles(
                inventory["sources"].AsArray(),
                new[]
                {
                    "Captured/LevelProgressModel.cs.txt",
                    "Captured/SettingsModel.cs.txt",
                    "Captured/ProgressOracle.cs.txt",
                    "Captured/SettingsOracle.cs.txt",
                },
                read
            );
            ValidateFiles(
                inventory["values"].AsArray(),
                new[] { "Data/level-progress-oracle.json.txt", "Data/settings.json.txt" },
                read
            );
            HashSet<string> remaining = new HashSet<string>(Ids, StringComparer.Ordinal);
            foreach (JsonNode row in inventory["inputs"].AsArray())
            {
                string id = row["id"].GetValue<string>();
                Assert.IsTrue(
                    remaining.Remove(id),
                    "Unexpected or duplicate retained identity: " + id
                );
                Assert.AreEqual(
                    FileFor(id),
                    row["file"].GetValue<string>(),
                    "Retained row association changed."
                );
                ValidateBytes(row, read);
            }
            Assert.AreEqual(0, remaining.Count, "A retained input is missing.");
            Assert.AreEqual(
                10,
                JsonNode.Parse(read("Data/level-progress-oracle.json.txt")).AsArray().Count
            );
        }

        internal static void ValidateFiles(
            JsonArray rows,
            IEnumerable<string> expected,
            Func<string, byte[]> read
        )
        {
            HashSet<string> remaining = new HashSet<string>(expected, StringComparer.Ordinal);
            foreach (JsonNode row in rows)
            {
                Assert.IsTrue(
                    remaining.Remove(row["file"].GetValue<string>()),
                    "Unexpected or duplicate retained file."
                );
                ValidateBytes(row, read);
            }
            Assert.AreEqual(0, remaining.Count, "A retained file is missing.");
        }

        private static void ValidateBytes(JsonNode row, Func<string, byte[]> read)
        {
            byte[] bytes = read(row["file"].GetValue<string>());
            Assert.AreEqual(row["bytes"].GetValue<int>(), bytes.Length, "Retained length changed.");
            Assert.AreEqual(
                row["sha256"].GetValue<string>(),
                Hash(bytes),
                "Retained bytes changed."
            );
        }
    }
}
