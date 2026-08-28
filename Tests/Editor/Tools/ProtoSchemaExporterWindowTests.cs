// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System;
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
}
