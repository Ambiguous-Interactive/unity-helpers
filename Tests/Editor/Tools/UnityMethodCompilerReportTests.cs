// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
#if UNITY_EDITOR
    using System.Text.Json;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer;

    [TestFixture]
    public sealed class UnityMethodCompilerReportTests
    {
        private static int CountProperties(JsonElement element)
        {
            int count = 0;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                ++count;
            }
            return count;
        }

        private static ReportData CreateReport()
        {
            return new ReportData
            {
                assemblies = new()
                {
                    new AssemblyReport
                    {
                        name = "Game.Editor",
                        hasErrors = true,
                        messages = new()
                        {
                            new MessageData
                            {
                                file = "Assets/雪/Player.cs",
                                line = 27,
                                column = 4,
                                message =
                                    "error CS0115: \"Player.Update\"\nNo override <found> & checked.",
                                error = true,
                            },
                            new MessageData
                            {
                                file = "Assets/Player.cs",
                                line = 28,
                                column = 8,
                                message = "warning WUH015: invalid callback",
                                error = false,
                            },
                        },
                    },
                    new AssemblyReport { name = "Game", messages = new() },
                },
            };
        }

        [Test]
        public void LegacyUnityJsonRetainsEveryCompilerMessageField()
        {
            ReportData original = CreateReport();
            string legacy = JsonUtility.ToJson(original);

            ReportData loaded = UnityMethodCompilerReport.ReadSessionData(legacy);

            Assert.That(JsonUtility.ToJson(loaded), Is.EqualTo(legacy));
            Assert.That(loaded.assemblies.Count, Is.EqualTo(2));
            Assert.That(loaded.assemblies[0].messages.Count, Is.EqualTo(2));
            Assert.That(loaded.assemblies[1].messages, Is.Empty);
        }

        [Test]
        public void SessionJsonPreservesExactLegacySchemaAndNativeRoundTrip()
        {
            ReportData original = CreateReport();
            string legacy = JsonUtility.ToJson(original);

            string saved = UnityMethodCompilerReport.WriteSessionData(original);
            using JsonDocument document = JsonDocument.Parse(saved);
            JsonElement root = document.RootElement;
            Assert.That(CountProperties(root), Is.EqualTo(1));
            JsonElement assemblies = root.GetProperty(nameof(ReportData.assemblies));
            Assert.That(assemblies.GetArrayLength(), Is.EqualTo(2));
            JsonElement assembly = assemblies[0];
            Assert.That(CountProperties(assembly), Is.EqualTo(3));
            Assert.That(
                assembly.GetProperty(nameof(AssemblyReport.name)).GetString(),
                Is.EqualTo("Game.Editor")
            );
            Assert.That(
                assembly.GetProperty(nameof(AssemblyReport.hasErrors)).ValueKind,
                Is.EqualTo(JsonValueKind.True)
            );
            JsonElement messages = assembly.GetProperty(nameof(AssemblyReport.messages));
            Assert.That(messages.GetArrayLength(), Is.EqualTo(2));
            JsonElement message = messages[0];
            Assert.That(CountProperties(message), Is.EqualTo(5));
            Assert.That(
                message.GetProperty(nameof(MessageData.file)).GetString(),
                Is.EqualTo(original.assemblies[0].messages[0].file)
            );
            Assert.That(message.GetProperty(nameof(MessageData.line)).GetInt32(), Is.EqualTo(27));
            Assert.That(message.GetProperty(nameof(MessageData.column)).GetInt32(), Is.EqualTo(4));
            Assert.That(
                message.GetProperty(nameof(MessageData.message)).GetString(),
                Is.EqualTo(original.assemblies[0].messages[0].message)
            );
            Assert.That(
                message.GetProperty(nameof(MessageData.error)).ValueKind,
                Is.EqualTo(JsonValueKind.True)
            );
            Assert.That(
                messages[1].GetProperty(nameof(MessageData.error)).ValueKind,
                Is.EqualTo(JsonValueKind.False)
            );
            ReportData native = JsonUtility.FromJson<ReportData>(saved);
            Assert.That(JsonUtility.ToJson(native), Is.EqualTo(legacy));
            ReportData reloaded = UnityMethodCompilerReport.ReadSessionData(saved);
            Assert.That(JsonUtility.ToJson(reloaded), Is.EqualTo(legacy));
        }

        [TestCase(null, TestName = "EmptySession.Null.StartsWithNoCoverage")]
        [TestCase("", TestName = "EmptySession.Empty.StartsWithNoCoverage")]
        [TestCase(" ", TestName = "EmptySession.Whitespace.StartsWithNoCoverage")]
        [TestCase("null", TestName = "InvalidSession.NullRoot.StartsWithNoCoverage")]
        [TestCase("[]", TestName = "InvalidSession.ArrayRoot.StartsWithNoCoverage")]
        [TestCase("42", TestName = "InvalidSession.NumberRoot.StartsWithNoCoverage")]
        [TestCase("true", TestName = "InvalidSession.BooleanRoot.StartsWithNoCoverage")]
        [TestCase("\"report\"", TestName = "InvalidSession.StringRoot.StartsWithNoCoverage")]
        [TestCase("{", TestName = "InvalidSession.Truncated.StartsWithNoCoverage")]
        [TestCase(
            "{\"assemblies\":null}",
            TestName = "InvalidSession.NullAssemblies.StartsWithNoCoverage"
        )]
        [TestCase(
            "{\"assemblies\":[null]}",
            TestName = "InvalidSession.NullAssembly.StartsWithNoCoverage"
        )]
        [TestCase(
            "{\"assemblies\":[{}]}",
            TestName = "InvalidSession.MissingAssemblyName.StartsWithNoCoverage"
        )]
        [TestCase(
            "{\"assemblies\":[{\"name\":\" \"}]}",
            TestName = "InvalidSession.BlankAssemblyName.StartsWithNoCoverage"
        )]
        [TestCase(
            "{\"assemblies\":[{\"name\":\"Game\",\"messages\":null}]}",
            TestName = "InvalidSession.NullMessages.StartsWithNoCoverage"
        )]
        [TestCase(
            "{\"assemblies\":[{\"name\":\"Game\",\"messages\":[null]}]}",
            TestName = "InvalidSession.NullMessage.StartsWithNoCoverage"
        )]
        [TestCase(
            "{\"assemblies\":[{\"name\":\"Game\",\"messages\":[{\"line\":-1}]}]}",
            TestName = "InvalidSession.NegativeLine.StartsWithNoCoverage"
        )]
        [TestCase(
            "{\"assemblies\":[{\"name\":\"Game\",\"messages\":[{\"column\":-1}]}]}",
            TestName = "InvalidSession.NegativeColumn.StartsWithNoCoverage"
        )]
        [TestCase(
            "{\"assemblies\":[{\"name\":\"Game\"},{\"name\":\"Game\"}]}",
            TestName = "InvalidSession.DuplicateAssembly.StartsWithNoCoverage"
        )]
        [TestCase(
            "{\"assemblies\":[{\"name\":\"Game\"},null]}",
            TestName = "InvalidSession.ValidThenInvalidAssembly.StartsWithNoCoverage"
        )]
        public void InvalidSessionStartsWithNoCoverage(string saved)
        {
            ReportData loaded = UnityMethodCompilerReport.ReadSessionData(saved);

            Assert.That(loaded != null, Is.True);
            Assert.That(loaded.assemblies != null, Is.True);
            Assert.That(loaded.assemblies, Is.Empty);
            Assert.That(
                UnityMethodCompilerReport.DescribeCoverage(
                    loaded.assemblies.Count,
                    1,
                    false,
                    false
                ),
                Does.StartWith("No compiler coverage captured.")
            );
        }

        [TestCase("{}", TestName = "MissingSessionFields.RootDefaultsToEmpty")]
        [TestCase("{\"assemblies\":[]}", TestName = "MissingSessionFields.EmptyListStaysEmpty")]
        public void EmptyObjectAndEmptyListRemainValid(string saved)
        {
            ReportData loaded = UnityMethodCompilerReport.ReadSessionData(saved);

            Assert.That(loaded.assemblies, Is.Empty);
        }

        [Test]
        public void MissingOptionalFieldsAndNullStringsAreNormalized()
        {
            const string saved =
                "{\"assemblies\":[{\"name\":\"Game\"},{\"name\":\"Game.Editor\",\"messages\":[{\"file\":null,\"message\":null}]}]}";

            ReportData loaded = UnityMethodCompilerReport.ReadSessionData(saved);

            Assert.That(loaded.assemblies.Count, Is.EqualTo(2));
            Assert.That(loaded.assemblies[0].messages, Is.Empty);
            MessageData message = loaded.assemblies[1].messages[0];
            Assert.That(message.file, Is.EqualTo(string.Empty));
            Assert.That(message.message, Is.EqualTo(string.Empty));
            Assert.That(message.line, Is.Zero);
            Assert.That(message.column, Is.Zero);
            Assert.That(message.error, Is.False);
        }

        [Test]
        public void ErrorMessagesRepairTheAssemblyErrorFlag()
        {
            const string saved =
                "{\"assemblies\":[{\"name\":\"Game\",\"hasErrors\":false,\"messages\":[{\"error\":true}]}]}";

            ReportData loaded = UnityMethodCompilerReport.ReadSessionData(saved);

            Assert.That(loaded.assemblies.Count, Is.EqualTo(1));
            Assert.That(loaded.assemblies[0].hasErrors, Is.True);
            Assert.That(
                UnityMethodCompilerReport.DescribeCoverage(
                    1,
                    1,
                    false,
                    loaded.assemblies[0].hasErrors
                ),
                Does.Contain("Compilation errors")
            );
        }

        [Test]
        public void NullOptionalStringsWriteAsNativeEmptyStrings()
        {
            ReportData original = new()
            {
                assemblies = new()
                {
                    new AssemblyReport
                    {
                        name = "Game",
                        messages = new() { new MessageData() },
                    },
                },
            };
            string legacy = JsonUtility.ToJson(original);

            string saved = UnityMethodCompilerReport.WriteSessionData(original);

            using JsonDocument document = JsonDocument.Parse(saved);
            JsonElement message = document
                .RootElement.GetProperty(nameof(ReportData.assemblies))[0]
                .GetProperty(nameof(AssemblyReport.messages))[0];
            Assert.That(
                message.GetProperty(nameof(MessageData.file)).GetString(),
                Is.EqualTo(string.Empty)
            );
            Assert.That(
                message.GetProperty(nameof(MessageData.message)).GetString(),
                Is.EqualTo(string.Empty)
            );
            Assert.That(
                JsonUtility.ToJson(JsonUtility.FromJson<ReportData>(saved)),
                Is.EqualTo(legacy)
            );
        }

        [Test]
        public void InvalidInMemorySnapshotWritesNoCoverage()
        {
            ReportData original = CreateReport();
            original.assemblies.Add(null);

            ReportData loaded = UnityMethodCompilerReport.ReadSessionData(
                UnityMethodCompilerReport.WriteSessionData(original)
            );

            Assert.That(loaded.assemblies, Is.Empty);
            Assert.That(
                UnityMethodCompilerReport.WriteSessionData(null),
                Is.EqualTo("{\"assemblies\":[]}")
            );
        }

        [Test]
        public void UnknownFieldsAndMaximumSourcePositionsPreserveKnownData()
        {
            const string saved =
                "{\"future\":true,\"assemblies\":[{\"name\":\"Game\",\"future\":[],\"messages\":[{\"line\":2147483647,\"column\":2147483647,\"future\":42}]}]}";

            ReportData loaded = UnityMethodCompilerReport.ReadSessionData(saved);

            Assert.That(loaded.assemblies.Count, Is.EqualTo(1));
            Assert.That(loaded.assemblies[0].messages.Count, Is.EqualTo(1));
            Assert.That(loaded.assemblies[0].messages[0].line, Is.EqualTo(int.MaxValue));
            Assert.That(loaded.assemblies[0].messages[0].column, Is.EqualTo(int.MaxValue));
        }
    }
#endif
}
