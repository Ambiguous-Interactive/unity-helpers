// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ProtoSchemaExporterWindowTests : CommonTestBase
    {
        private const string OutputDirectory = "proto-schema-tests";
        private const string OutputFileName = "exported.proto";

        private ProtoSchemaExporterWindow _window;
        private string _outputPath;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            ProtoSchemaExporterWindow.SuppressUserPrompts = true;
            _window = Track(ScriptableObject.CreateInstance<ProtoSchemaExporterWindow>());
            _outputPath = Path.Combine(
                Path.Combine(Application.temporaryCachePath, OutputDirectory),
                OutputFileName
            );
        }

        [TearDown]
        public override void TearDown()
        {
            if (File.Exists(_outputPath))
            {
                File.Delete(_outputPath);
            }

            ProtoSchemaExporterWindow.SuppressUserPrompts = false;
            base.TearDown();
        }

        [Test]
        public void ExportWritesAProto3SchemaForTheSelectedAssembly()
        {
            _window.RefreshInventory();
            Assert.IsTrue(
                _window.ExportSchemaToPath(_outputPath),
                "The export should render the project's contracts."
            );

            string schema = File.ReadAllText(_outputPath);
            StringAssert.Contains("syntax = \"proto3\";", schema);
            StringAssert.Contains("message ProtoSchemaExporterSampleContract {", schema);
            StringAssert.Contains("int32 Health = 1;", schema);
            StringAssert.Contains("string Label = 2;", schema);
        }

        [Test]
        public void ExportWithoutContractsReportsInsteadOfWriting()
        {
            _window.SetSelectedAssembliesForTest(Array.Empty<string>());

            Assert.IsFalse(_window.ExportSchemaToPath(_outputPath));
            Assert.IsFalse(File.Exists(_outputPath), "Nothing selected must not write a file.");
        }

        [Test]
        public void IndividualContractsCanBeSelectedWithinAnAssembly()
        {
            _window.SetSelectedContractsForTest(
                new[] { typeof(ProtoSchemaExporterSampleContract) }
            );

            Assert.IsTrue(_window.ExportSchemaToPath(_outputPath));

            string schema = File.ReadAllText(_outputPath);
            StringAssert.Contains("message ProtoSchemaExporterSampleContract {", schema);
            StringAssert.DoesNotContain("ProtoSchemaExporterSecondSampleContract", schema);
        }

        [Test]
        public void PerAssemblyExportWritesOneNamedSchemaForTheSelectedAssembly()
        {
            string assemblyName = typeof(ProtoSchemaExporterSampleContract).Assembly.GetName().Name;
            _window.ExportLayoutForTest = ProtoSchemaExporterWindow.ExportLayout.OneFilePerAssembly;
            _window.SetSelectedAssembliesForTest(new[] { assemblyName });

            string outputDirectory = ExportToDirectory("per-assembly");
            try
            {
                string schema = File.ReadAllText(
                    Path.Combine(outputDirectory, assemblyName + ".proto")
                );
                StringAssert.Contains("message ProtoSchemaExporterSampleContract {", schema);
                StringAssert.Contains("message ProtoSchemaExporterSecondSampleContract {", schema);
            }
            finally
            {
                DeleteDirectory(outputDirectory);
            }
        }

        [Test]
        public void PerNamespaceExportGroupsEveryContractOfOneNamespaceIntoOneFile()
        {
            _window.ExportLayoutForTest = ProtoSchemaExporterWindow
                .ExportLayout
                .OneFilePerNamespace;
            _window.SetSelectedContractsForTest(SampleContracts);

            string outputDirectory = ExportToDirectory("per-namespace");
            try
            {
                string[] files = Directory.GetFiles(outputDirectory, "*.proto");
                Assert.AreEqual(1, files.Length, "Both samples share one namespace.");
                Assert.AreEqual(
                    typeof(ProtoSchemaExporterSampleContract).Namespace + ".proto",
                    Path.GetFileName(files[0])
                );
                string schema = File.ReadAllText(files[0]);
                StringAssert.Contains("message ProtoSchemaExporterSampleContract {", schema);
                StringAssert.Contains("message ProtoSchemaExporterSecondSampleContract {", schema);
            }
            finally
            {
                DeleteDirectory(outputDirectory);
            }
        }

        [Test]
        public void PerContractExportWritesOneSelfContainedFilePerType()
        {
            _window.ExportLayoutForTest = ProtoSchemaExporterWindow.ExportLayout.OneFilePerContract;
            _window.SetSelectedContractsForTest(SampleContracts);

            string outputDirectory = ExportToDirectory("per-contract");
            try
            {
                foreach (Type contract in SampleContracts)
                {
                    string schemaPath = Path.Combine(outputDirectory, contract.FullName + ".proto");
                    Assert.IsTrue(File.Exists(schemaPath), schemaPath + " should exist.");
                    string schema = File.ReadAllText(schemaPath);
                    StringAssert.Contains("message " + contract.Name + " {", schema);
                }

                Assert.AreEqual(2, Directory.GetFiles(outputDirectory, "*.proto").Length);
            }
            finally
            {
                DeleteDirectory(outputDirectory);
            }
        }

        [Test]
        public void AProtoPackageIsWrittenIntoEverySchema()
        {
            _window.PackageNameForTest = "mygame.save";
            _window.SetSelectedContractsForTest(SampleContracts);

            Assert.IsTrue(_window.ExportSchemaToPath(_outputPath));
            StringAssert.Contains("package mygame.save;", File.ReadAllText(_outputPath));
        }

        [TestCase("1bad")]
        [TestCase("has space")]
        [TestCase("trailing.")]
        [TestCase("double..dot")]
        [TestCase("has-hyphen")]
        public void AMalformedProtoPackageRefusesInsteadOfWriting(string packageName)
        {
            _window.PackageNameForTest = packageName;
            _window.SetSelectedContractsForTest(SampleContracts);

            Assert.IsFalse(_window.HasUsablePackageNameForTest);
            Assert.IsFalse(_window.ExportSchemaToPath(_outputPath));
            Assert.IsFalse(File.Exists(_outputPath), "A refused package must not write a file.");
            StringAssert.Contains("not a proto3 package", _window.LastStatusForTest);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("mygame")]
        [TestCase("my_game.save2")]
        [TestCase("_leading.Underscore")]
        public void AnOmittedOrWellFormedProtoPackageIsAccepted(string packageName)
        {
            _window.PackageNameForTest = packageName;

            Assert.IsTrue(_window.HasUsablePackageNameForTest);
        }

        [Test]
        public void TheSearchFilterMatchesTypeAndAssemblyNames()
        {
            _window.SearchFilterForTest = nameof(ProtoSchemaExporterSecondSampleContract);

            CollectionAssert.AreEquivalent(
                new[] { typeof(ProtoSchemaExporterSecondSampleContract) },
                _window.VisibleContractsForTest
            );

            _window.SearchFilterForTest = typeof(ProtoSchemaExporterSampleContract)
                .Assembly.GetName()
                .Name;

            CollectionAssert.IsSupersetOf(_window.VisibleContractsForTest, SampleContracts);
        }

        [Test]
        public void EveryDeselectionReachesTheSerializedFieldImmediately()
        {
            // Not a capture/restore round trip: the window records each mutation as it happens, so
            // there is no checkpoint to roll back to and nothing depends on OnDisable running
            // first. What has to hold is that the serialized field already carries the exclusion.
            string secondKey = ProtoSchemaExporterWindow.ContractKeyForTest(
                typeof(ProtoSchemaExporterSecondSampleContract)
            );
            string firstKey = ProtoSchemaExporterWindow.ContractKeyForTest(
                typeof(ProtoSchemaExporterSampleContract)
            );

            _window.SetSelectedContractsForTest(
                new[] { typeof(ProtoSchemaExporterSampleContract) }
            );

            CollectionAssert.Contains(_window.PersistedExclusionsForTest, secondKey);
            CollectionAssert.DoesNotContain(_window.PersistedExclusionsForTest, firstKey);
        }

        [Test]
        public void RestoringTheSerializedStateRebuildsTheLiveSelection()
        {
            _window.SetSelectedContractsForTest(
                new[] { typeof(ProtoSchemaExporterSampleContract) }
            );

            // What a domain reload does: the serialized list survives, the runtime set is rebuilt
            // from it by OnEnable.
            _window.RestoreSelectionState();

            CollectionAssert.DoesNotContain(
                _window.SelectedContractsForTest,
                typeof(ProtoSchemaExporterSecondSampleContract)
            );
            CollectionAssert.Contains(
                _window.SelectedContractsForTest,
                typeof(ProtoSchemaExporterSampleContract)
            );
        }

        [Test]
        public void ARefreshKeepsDeselectionsAndAdmitsEverythingElse()
        {
            _window.SetSelectedContractsForTest(
                new[] { typeof(ProtoSchemaExporterSampleContract) }
            );

            _window.RefreshInventory();

            CollectionAssert.Contains(
                _window.SelectedContractsForTest,
                typeof(ProtoSchemaExporterSampleContract)
            );
            CollectionAssert.DoesNotContain(
                _window.SelectedContractsForTest,
                typeof(ProtoSchemaExporterSecondSampleContract)
            );
        }

        private static Type[] SampleContracts =>
            new[]
            {
                typeof(ProtoSchemaExporterSampleContract),
                typeof(ProtoSchemaExporterSecondSampleContract),
            };

        private string ExportToDirectory(string leafName)
        {
            string outputDirectory = Path.Combine(
                Path.Combine(Application.temporaryCachePath, OutputDirectory),
                leafName
            );
            DeleteDirectory(outputDirectory);
            Assert.IsTrue(
                _window.ExportSchemasToDirectory(outputDirectory),
                _window.LastStatusForTest
            );
            return outputDirectory;
        }

        private static void DeleteDirectory(string outputDirectory)
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }
        }

        [Test]
        public void EveryFileLayoutIsLabelledAndSelectable()
        {
            // The popup is constructed with an index into one array and a label list from the
            // other; a length mismatch throws where a user can only see a broken window.
            Assert.AreEqual(
                ProtoSchemaExporterWindow.SelectableLayoutsForTest.Count,
                ProtoSchemaExporterWindow.LayoutLabelsForTest.Count,
                "Every selectable layout needs exactly one label."
            );
            CollectionAssert.AllItemsAreUnique(ProtoSchemaExporterWindow.SelectableLayoutsForTest);
            CollectionAssert.DoesNotContain(
                ProtoSchemaExporterWindow.SelectableLayoutsForTest,
                default(ProtoSchemaExporterWindow.ExportLayout),
                "The obsolete zero value must never be offered."
            );
        }

        [Test]
        public void GroupKeysThatSanitizeToOneNameGetSeparateFiles()
        {
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Assert.AreEqual(
                "Game_Save.proto",
                ProtoSchemaExporterWindow.UniqueFileNameForTest("Game:Save", used)
            );
            Assert.AreEqual(
                "Game_Save-2.proto",
                ProtoSchemaExporterWindow.UniqueFileNameForTest("Game/Save", used)
            );
            Assert.AreEqual(
                "Global.proto",
                ProtoSchemaExporterWindow.UniqueFileNameForTest(string.Empty, used)
            );
        }

        [Test]
        public void AFailedExportIsReportedAsAFailure()
        {
            _window.PackageNameForTest = "1bad";

            Assert.IsFalse(_window.ExportSchemaToPath(_outputPath));
            Assert.IsTrue(
                _window.LastStatusIsFailureForTest,
                "A refusal must not read as an informational status."
            );

            _window.PackageNameForTest = string.Empty;
            Assert.IsTrue(_window.ExportSchemaToPath(_outputPath));
            Assert.IsFalse(_window.LastStatusIsFailureForTest);
        }

        [Test]
        public void AnUnwritablePathReportsInsteadOfThrowing()
        {
            // A directory where the file belongs is the deterministic way to make the write fail;
            // the Try contract answers false with the reason instead of escaping.
            string directoryPath = Path.Combine(
                Application.temporaryCachePath,
                OutputDirectory,
                "written-as-directory.proto"
            );
            Directory.CreateDirectory(directoryPath);

            try
            {
                Assert.IsFalse(_window.ExportSchemaToPath(directoryPath));
                StringAssert.Contains("Could not write", _window.LastStatusForTest);
            }
            finally
            {
                Directory.Delete(directoryPath);
            }
        }

        [Test]
        public void AnOutsideProjectPathWritesWithoutImporting()
        {
            string outsidePath = Path.Combine(Path.GetTempPath(), "proto-schema-outside.proto");
            try
            {
                Assert.IsTrue(
                    _window.ExportSchemaToPath(outsidePath),
                    "A writable path outside the project still exports."
                );
                StringAssert.Contains("syntax = \"proto3\";", File.ReadAllText(outsidePath));
            }
            finally
            {
                if (File.Exists(outsidePath))
                {
                    File.Delete(outsidePath);
                }
            }
        }
    }

    [WProtoContract]
    public sealed partial class ProtoSchemaExporterSampleContract
    {
        [WProtoMember(1)]
        public int Health;

        [WProtoMember(2)]
        public string Label;
    }

    [WProtoContract]
    public sealed partial class ProtoSchemaExporterSecondSampleContract
    {
        [WProtoMember(1)]
        public int Score;
    }
}
