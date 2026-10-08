// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.DurableFile.Tests
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Runtime.InteropServices;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using RealFile = System.IO.File;
    using SimulatedFile = WallstopStudios.UnityHelpers.Core.Helper.File;

    /// <summary>Checks initialization against real operating system write and flush failures.</summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class FileHelperInitializationTests
    {
        private string _directory;
        private string _destination;

        [SetUp]
        public void SetUp()
        {
            SimulatedFile.Reset();
            _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _destination = Path.Combine(_directory, "player.save");
        }

        [TearDown]
        public void TearDown()
        {
            SimulatedFile.Reset();
            Directory.Delete(_directory, recursive: true);
        }

        [TestCase(128, 0)]
        [TestCase(128, 1)]
        [TestCase(128, 63)]
        [TestCase(65536, 0)]
        [TestCase(65536, 1)]
        [TestCase(65536, 63)]
        public void KernelFileSizeFailureLeavesDestinationAbsentAndRetryable(
            int contentsLength,
            int maximumBytes
        )
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || !Environment.Is64BitProcess)
            {
                Assert.Ignore("Requires the Linux 64-bit RLIMIT_FSIZE ABI.");
            }
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("--initialize-limit");
            start.ArgumentList.Add(_destination);
            start.ArgumentList.Add(contentsLength.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(maximumBytes.ToString(CultureInfo.InvariantCulture));
            using Process child = Process.Start(start);
            Assert.IsNotNull(child);
            bool exited = child.WaitForExit(20000);
            if (!exited)
            {
                child.Kill(entireProcessTree: true);
                Assert.IsTrue(
                    child.WaitForExit(5000),
                    "The isolated initializer could not be stopped."
                );
            }
            Assert.IsTrue(exited, "The isolated file-size-limited initializer did not exit.");
            string errors = child.StandardError.ReadToEnd();
            Assert.AreEqual(0, child.ExitCode, errors);
            Assert.IsFalse(RealFile.Exists(_destination));
            Assert.IsEmpty(Directory.GetFileSystemEntries(_directory));

            byte[] contents = Program.CreateContents(contentsLength);
            Assert.IsTrue(FileHelper.InitializePath(_destination, contents));
            CollectionAssert.AreEqual(contents, RealFile.ReadAllBytes(_destination));
            CollectionAssert.AreEqual(
                new[] { _destination },
                Directory.GetFileSystemEntries(_directory)
            );
        }

        [Test]
        public void NullContentsPublishAnEmptyFileAndExistingContentsRemainUntouched()
        {
            Assert.IsTrue(FileHelper.InitializePath(_destination));
            Assert.AreEqual(0, new FileInfo(_destination).Length);
            byte[] existing = Program.CreateContents(12);
            RealFile.WriteAllBytes(_destination, existing);
            Assert.IsFalse(FileHelper.InitializePath(_destination, Program.CreateContents(128)));
            CollectionAssert.AreEqual(existing, RealFile.ReadAllBytes(_destination));
            CollectionAssert.AreEqual(
                new[] { _destination },
                Directory.GetFileSystemEntries(_directory)
            );
        }

        [Test]
        public void DirectoryCollisionPreservesDirectoryAndAllowsRetryAfterItIsRemoved()
        {
            Directory.CreateDirectory(_destination);
            string foreign = Path.Combine(_destination, "foreign.save");
            byte[] contents = Program.CreateContents(16);
            RealFile.WriteAllBytes(foreign, contents);
            Assert.IsFalse(FileHelper.InitializePath(_destination, Program.CreateContents(128)));
            CollectionAssert.AreEqual(contents, RealFile.ReadAllBytes(foreign));
            CollectionAssert.AreEqual(
                new[] { _destination },
                Directory.GetFileSystemEntries(_directory)
            );
            Directory.Delete(_destination, recursive: true);
            Assert.IsTrue(FileHelper.InitializePath(_destination, contents));
            CollectionAssert.AreEqual(contents, RealFile.ReadAllBytes(_destination));
        }
    }
}
