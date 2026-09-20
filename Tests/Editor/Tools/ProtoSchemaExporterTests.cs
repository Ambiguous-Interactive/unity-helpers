// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Editor.Tools;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ProtoSchemaExporterTests
    {
        private const string OutputDirectory = "proto-schema-api-tests";
        private const string OutputFileName = "exported.proto";

        private static Type[] SampleContracts =>
            new[]
            {
                typeof(ProtoSchemaExporterSampleContract),
                typeof(ProtoSchemaExporterSecondSampleContract),
            };

        private string _outputPath;

        private static void DeleteDirectory(string outputDirectory)
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }
        }

        private static IEnumerable<Type> ThrowingContracts()
        {
            yield return typeof(ProtoSchemaExporterSampleContract);
            throw new InvalidOperationException("Enumeration failed.");
        }

        [SetUp]
        public void SetUp()
        {
            _outputPath = Path.Combine(
                Application.temporaryCachePath,
                OutputDirectory,
                OutputFileName
            );
        }

        [TearDown]
        public void TearDown()
        {
            DeleteDirectory(Path.Combine(Application.temporaryCachePath, OutputDirectory));
        }

        [Test]
        public void DirectExporterWritesSelectedContractsWithoutAWindow()
        {
            ProtoSchemaExporter.ExportResult result = ProtoSchemaExporter.Export(
                new[] { typeof(ProtoSchemaExporterSampleContract) },
                _outputPath,
                ProtoSchemaExporter.ExportLayout.SingleFile,
                "mygame.save"
            );

            Assert.IsTrue(result.Success, result.Message);
            CollectionAssert.AreEquivalent(
                new[] { Path.GetFullPath(_outputPath) },
                result.WrittenPaths
            );
            StringAssert.Contains("package mygame.save;", File.ReadAllText(_outputPath));
            StringAssert.DoesNotContain(
                "message ProtoSchemaExporterSecondSampleContract",
                File.ReadAllText(_outputPath)
            );
        }

        [TestCase("1bad", "valid.proto")]
        [TestCase("mygame..save", "valid.proto")]
        [TestCase("mygame", "../outside.proto")]
        [TestCase("mygame", "invalid?.proto")]
        [TestCase("mygame", "invalid?/schema.proto")]
        public void DirectExporterRefusesInvalidInputsBeforeWriting(
            string packageName,
            string destination
        )
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string output = Path.Combine(projectRoot, destination);
            ProtoSchemaExporter.ExportResult result = ProtoSchemaExporter.Export(
                SampleContracts,
                destination,
                ProtoSchemaExporter.ExportLayout.SingleFile,
                packageName
            );

            Assert.IsFalse(result.Success);
            Assert.IsEmpty(result.WrittenPaths);
            Assert.IsFalse(File.Exists(output));
        }

        [Test]
        public void DirectExporterGroupsFilesAndReportsTheirPaths()
        {
            string directory = Path.Combine(
                Application.temporaryCachePath,
                OutputDirectory,
                "direct-groups"
            );
            DeleteDirectory(directory);
            try
            {
                ProtoSchemaExporter.ExportResult result = ProtoSchemaExporter.Export(
                    SampleContracts,
                    directory,
                    ProtoSchemaExporter.ExportLayout.OneFilePerContract
                );

                Assert.IsTrue(result.Success, result.Message);
                Assert.AreEqual(2, result.WrittenPaths.Count);
                foreach (string path in result.WrittenPaths)
                {
                    StringAssert.Contains("syntax = \"proto3\";", File.ReadAllText(path));
                }
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [Test]
        public void DirectProjectDiscoveryFindsContractsAndSurrogates()
        {
            Assert.IsTrue(
                ProtoSchemaExporter.TryDiscoverProjectContracts(
                    out IReadOnlyList<Type> contracts,
                    out string contractError
                ),
                contractError
            );
            CollectionAssert.Contains(contracts, typeof(ProtoSchemaExporterSampleContract));
            Assert.IsTrue(
                ProtoSchemaExporter.TryDiscoverProjectSurrogates(
                    out IReadOnlyDictionary<Type, Type> surrogates,
                    out string error
                ),
                error
            );
            Assert.IsTrue(surrogates.TryGetValue(typeof(Vector2), out Type surrogate));
            Assert.AreEqual(typeof(Vector2Surrogate), surrogate);
        }

        [Test]
        public void DirectExporterReportsFilesWrittenBeforeADirectoryWriteFails()
        {
            string directory = Path.Combine(
                Application.temporaryCachePath,
                OutputDirectory,
                "partial-groups"
            );
            DeleteDirectory(directory);
            Directory.CreateDirectory(directory);
            string blockedPath = Path.Combine(
                directory,
                typeof(ProtoSchemaExporterSecondSampleContract).FullName + ".proto"
            );
            Directory.CreateDirectory(blockedPath);
            try
            {
                ProtoSchemaExporter.ExportResult result = ProtoSchemaExporter.Export(
                    SampleContracts,
                    directory,
                    ProtoSchemaExporter.ExportLayout.OneFilePerContract
                );

                Assert.IsFalse(result.Success);
                Assert.AreEqual(1, result.WrittenPaths.Count);
                Assert.IsTrue(File.Exists(result.WrittenPaths[0]));
                StringAssert.Contains("Could not write schemas", result.Message);
            }
            finally
            {
                DeleteDirectory(directory);
            }
        }

        [Test]
        public void DirectExporterRefusesThrowingContractEnumeration()
        {
            ProtoSchemaExporter.ExportResult result = ProtoSchemaExporter.Export(
                ThrowingContracts(),
                _outputPath,
                ProtoSchemaExporter.ExportLayout.SingleFile
            );

            Assert.IsFalse(result.Success);
            Assert.IsEmpty(result.WrittenPaths);
            Assert.IsFalse(File.Exists(_outputPath));
            Assert.IsFalse(
                ProtoSchemaExporter.TryGetOutputPaths(
                    ThrowingContracts(),
                    _outputPath,
                    ProtoSchemaExporter.ExportLayout.SingleFile,
                    null,
                    out IReadOnlyList<string> paths,
                    out string error
                )
            );
            Assert.IsEmpty(paths);
            StringAssert.Contains("Enumeration failed", error);
        }

        [Test]
        public void DirectExporterUsesRegisteredSurrogatesByDefault()
        {
            ProtoSchemaExporter.ExportResult result = ProtoSchemaExporter.Export(
                new[] { typeof(ProtoSchemaExporterSurrogateContract) },
                _outputPath,
                ProtoSchemaExporter.ExportLayout.SingleFile
            );

            Assert.IsTrue(result.Success, result.Message);
            string discoveredSchema = File.ReadAllText(_outputPath);
            Assert.IsTrue(
                ProtoSchemaExporter.TryDiscoverProjectSurrogates(
                    out IReadOnlyDictionary<Type, Type> surrogates,
                    out string discoveryError
                ),
                discoveryError
            );
            ProtoSchemaExporter.ExportResult explicitResult = ProtoSchemaExporter.Export(
                new[] { typeof(ProtoSchemaExporterSurrogateContract) },
                _outputPath,
                ProtoSchemaExporter.ExportLayout.SingleFile,
                null,
                surrogates
            );
            Assert.IsTrue(explicitResult.Success, explicitResult.Message);
            Assert.AreEqual(discoveredSchema, File.ReadAllText(_outputPath));
            StringAssert.Contains("Vector2Surrogate", discoveredSchema);
        }

        [Test]
        public void SingleFileRenderFailureNamesTheDestination()
        {
            ProtoSchemaExporter.ExportResult result = ProtoSchemaExporter.Export(
                new[] { typeof(ProtoSchemaExporterSurrogateContract) },
                _outputPath,
                ProtoSchemaExporter.ExportLayout.SingleFile,
                null,
                new ThrowingSurrogateMap()
            );

            Assert.IsFalse(result.Success);
            StringAssert.Contains(_outputPath, result.Message);
            StringAssert.Contains("Surrogate lookup failed", result.Message);
            StringAssert.DoesNotContain("for :", result.Message);
            Assert.IsFalse(File.Exists(_outputPath));
        }

        private sealed class ThrowingSurrogateMap : IReadOnlyDictionary<Type, Type>
        {
            public Type this[Type key] => throw new InvalidOperationException();

            public IEnumerable<Type> Keys => Array.Empty<Type>();

            public IEnumerable<Type> Values => Array.Empty<Type>();

            public int Count => 0;

            public bool ContainsKey(Type key)
            {
                return false;
            }

            public bool TryGetValue(Type key, out Type value)
            {
                value = null;
                throw new InvalidOperationException("Surrogate lookup failed.");
            }

            public IEnumerator<KeyValuePair<Type, Type>> GetEnumerator()
            {
                return (
                    (IEnumerable<KeyValuePair<Type, Type>>)Array.Empty<KeyValuePair<Type, Type>>()
                ).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }

    [WProtoContract]
    public sealed partial class ProtoSchemaExporterSurrogateContract
    {
        [WProtoMember(1)]
        public Vector2 Position;
    }
}
