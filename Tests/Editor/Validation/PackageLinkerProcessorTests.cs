// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Xml.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.Build;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using PackageInfo = UnityEditor.PackageManager.PackageInfo;

    [TestFixture]
    [Category("Fast")]
    [Category("Validation")]
    public sealed class PackageLinkerProcessorTests
    {
        private static IEnumerable<TestCaseData> AssemblyPreservationCases()
        {
            yield return new TestCaseData("protobuf-net", true).SetName(
                "PackageLinker.Preserve.Protobuf.Optional"
            );
            yield return new TestCaseData("protobuf-net.Core", true).SetName(
                "PackageLinker.Preserve.ProtobufCore.Optional"
            );
            yield return new TestCaseData("System.Text.Json", false).SetName(
                "PackageLinker.Preserve.Json.Required"
            );
            yield return new TestCaseData("System.Collections.Immutable", false).SetName(
                "PackageLinker.Preserve.Immutable.Required"
            );
            yield return new TestCaseData(
                typeof(Serializer).Assembly.GetName().Name,
                false
            ).SetName("PackageLinker.Preserve.Runtime.Required");
        }

        [Test]
        public void UnityDiscoversPackageLinkerProcessor()
        {
            bool found = false;
            foreach (Type callback in TypeCache.GetTypesDerivedFrom<IUnityLinkerProcessor>())
            {
                if (callback == typeof(PackageLinkerProcessor))
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found);
        }

        [Test]
        public void InstalledCallbackSuppliesExistingPackageDescriptorWithoutChanges()
        {
            PackageInfo package = PackageInfo.FindForAssembly(
                typeof(PackageLinkerProcessor).Assembly
            );
            PackageLinkerProcessor callback = new PackageLinkerProcessor();
            string root = TestPackageRoot.Resolve(typeof(PackageLinkerProcessorTests).Assembly);
            Assert.IsFalse(string.IsNullOrWhiteSpace(root));
            string descriptor = Path.Combine(root, PackageLinkerProcessor.LinkXmlFileName);
            byte[] before = File.ReadAllBytes(descriptor);

            string supplied = callback.GenerateAdditionalLinkXmlFile(null, null);

            Assert.AreEqual(0, callback.callbackOrder);
            if (package == null)
            {
                Assert.IsTrue(supplied == null);
            }
            else
            {
                Assert.AreEqual(
                    PathHelper.Sanitize(
                        Path.Combine(package.resolvedPath, PackageLinkerProcessor.LinkXmlFileName)
                    ),
                    supplied
                );
                Assert.IsTrue(Path.IsPathRooted(supplied));
                Assert.IsTrue(File.Exists(supplied));
                CollectionAssert.AreEqual(before, File.ReadAllBytes(supplied));
            }

            CollectionAssert.AreEqual(before, File.ReadAllBytes(descriptor));
        }

        [TestCaseSource(nameof(AssemblyPreservationCases))]
        public void DescriptorRetainsAssemblyPreservationPolicy(string assemblyName, bool optional)
        {
            string root = TestPackageRoot.Resolve(typeof(PackageLinkerProcessorTests).Assembly);
            Assert.IsFalse(string.IsNullOrWhiteSpace(root));
            XDocument descriptor = XDocument.Load(
                Path.Combine(root, PackageLinkerProcessor.LinkXmlFileName)
            );
            Assert.IsTrue(descriptor.Root != null);
            Assert.AreEqual("linker", descriptor.Root.Name.LocalName);
            int count = 0;
            XElement selected = null;
            foreach (XElement assembly in descriptor.Root.Elements("assembly"))
            {
                ++count;
                if (
                    string.Equals(
                        (string)assembly.Attribute("fullname"),
                        assemblyName,
                        StringComparison.Ordinal
                    )
                )
                {
                    Assert.IsTrue(
                        selected == null,
                        "Assembly preservation must have no duplicate entries."
                    );
                    selected = assembly;
                }
            }

            Assert.AreEqual(5, count);
            Assert.IsTrue(selected != null);
            Assert.AreEqual("all", (string)selected.Attribute("preserve"));
            Assert.AreEqual(
                optional ? "true" : null,
                (string)selected.Attribute("ignoreIfMissing")
            );
        }

        [TestCase(null, TestName = "PackageLinker.InvalidRoot.Null")]
        [TestCase("", TestName = "PackageLinker.InvalidRoot.Empty")]
        [TestCase(" \t\r\n", TestName = "PackageLinker.InvalidRoot.Whitespace")]
        [TestCase("relative directory", TestName = "PackageLinker.InvalidRoot.Relative")]
        [TestCase("\0", TestName = "PackageLinker.InvalidRoot.InvalidCharacter")]
        public void InvalidPackageRootStopsBuild(string root)
        {
            Assert.Throws<BuildFailedException>(() =>
                PackageLinkerProcessor.RequireLinkXmlPath(root)
            );
        }

        [Test]
        public void InvalidCharactersInAbsoluteRootStopBuild()
        {
            string root = Path.GetTempPath() + "\0";
            Assert.Throws<BuildFailedException>(() =>
                PackageLinkerProcessor.RequireLinkXmlPath(root)
            );
        }

        [Test]
        public void ExistingDriveRelativeDescriptorStopsBuild()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Ignore("Drive-relative paths apply to Windows.");
            }

            string temporaryRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string rootPrefix = Path.GetPathRoot(temporaryRoot);
            if (rootPrefix.Length < 2 || rootPrefix[1] != ':')
            {
                Assert.Ignore("No drive-letter temporary location is available.");
            }

            string drive = rootPrefix.Substring(0, 2);
            string driveBase = Path.GetFullPath(drive + ".");
            string driveRelative = drive + Path.GetRelativePath(driveBase, temporaryRoot);
            Directory.CreateDirectory(temporaryRoot);
            try
            {
                string descriptor = Path.Combine(
                    temporaryRoot,
                    PackageLinkerProcessor.LinkXmlFileName
                );
                File.WriteAllText(descriptor, "<linker />");
                Assert.IsTrue(
                    File.Exists(Path.Combine(driveRelative, PackageLinkerProcessor.LinkXmlFileName))
                );
                Assert.IsTrue(Path.IsPathRooted(driveRelative));
                Assert.IsFalse(Path.IsPathFullyQualified(driveRelative));

                Assert.Throws<BuildFailedException>(() =>
                    PackageLinkerProcessor.RequireLinkXmlPath(driveRelative)
                );
            }
            finally
            {
                Directory.Delete(temporaryRoot, true);
            }
        }

        [TestCase(false, TestName = "PackageLinker.MissingDescriptor.FileAbsent")]
        [TestCase(true, TestName = "PackageLinker.MissingDescriptor.DirectoryAtFilePath")]
        public void MissingDescriptorStopsBuild(bool occupiedByDirectory)
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                if (occupiedByDirectory)
                {
                    Directory.CreateDirectory(
                        Path.Combine(root, PackageLinkerProcessor.LinkXmlFileName)
                    );
                }

                BuildFailedException failure = Assert.Throws<BuildFailedException>(() =>
                    PackageLinkerProcessor.RequireLinkXmlPath(root)
                );
                StringAssert.Contains(nameof(PackageLinkerProcessor), failure.Message);
                StringAssert.Contains(root, failure.Message);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestCase("Packages/local package", TestName = "PackageLinker.Location.Embedded")]
        [TestCase(
            "Library/PackageCache/local-package@1.0.0",
            TestName = "PackageLinker.Location.Cache"
        )]
        [TestCase("external packages/local package", TestName = "PackageLinker.Location.External")]
        public void ExistingDescriptorUsesResolvedLocationWithoutChanges(string relativeLocation)
        {
            string temporaryRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string root = Path.Combine(temporaryRoot, relativeLocation);
            Directory.CreateDirectory(root);
            try
            {
                string descriptor = Path.Combine(root, PackageLinkerProcessor.LinkXmlFileName);
                byte[] before = System.Text.Encoding.UTF8.GetBytes("<linker />");
                File.WriteAllBytes(descriptor, before);

                Assert.AreEqual(
                    PathHelper.Sanitize(descriptor),
                    PackageLinkerProcessor.RequireLinkXmlPath(root)
                );
                CollectionAssert.AreEqual(before, File.ReadAllBytes(descriptor));
                CollectionAssert.AreEquivalent(new[] { descriptor }, Directory.GetFiles(root));
            }
            finally
            {
                Directory.Delete(temporaryRoot, true);
            }
        }
    }
}
