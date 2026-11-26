// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || !UNITY_5_3_OR_NEWER
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using Microsoft.Win32.SafeHandles;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using File = System.IO.File;
    using RuntimeInformation = System.Runtime.InteropServices.RuntimeInformation;

    [TestFixture]
    [Category("Integration")]
    public sealed class DurableFileWindowsBackupTests
    {
        private const uint ImageMappingProtection = 0x01000002;

        private string _directory;
        private string _destination;
        private byte[] _priorBytes;

        private static SafeFileHandle MapImage(SafeFileHandle source)
        {
            SafeFileHandle image = CreateFileMapping(
                source,
                IntPtr.Zero,
                ImageMappingProtection,
                0,
                0,
                null
            );
            if (image.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                image.Dispose();
                throw new IOException($"Could not map the Windows fixture (OS error {error}).");
            }
            return image;
        }

        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateFileMappingW",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern SafeFileHandle CreateFileMapping(
            SafeFileHandle file,
            IntPtr attributes,
            uint protection,
            uint maximumSizeHigh,
            uint maximumSizeLow,
            string name
        );

        [SetUp]
        public void SetUp()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Windows file replacement and image mappings are required.");
            }

            _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _destination = Path.Combine(_directory, "player.save");
            _priorBytes = File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "where.exe"));
            File.WriteAllBytes(_destination, _priorBytes);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public void OrdinaryReplacementRemovesItsBackupImmediately()
        {
            Publish("published");
            CollectionAssert.AreEqual(new[] { _destination }, Directory.GetFiles(_directory));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void LaterPublicationReclaimsOnlyItsUnlockedBackup(
            bool reuseBackupPath,
            bool blockDeleteSharing
        )
        {
            string backup;
            using (
                FileStream source = File.Open(
                    _destination,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                )
            )
            using (SafeFileHandle image = MapImage(source.SafeFileHandle))
            {
                Publish("first");
                string[] backups = Directory.GetFiles(_directory, "*.bak");
                Assert.That(backups.Length, Is.EqualTo(1));
                backup = backups[0];
                CollectionAssert.AreEqual(_priorBytes, File.ReadAllBytes(backup));
                Publish("second");
                CollectionAssert.AreEqual(backups, Directory.GetFiles(_directory, "*.bak"));
                CollectionAssert.AreEqual(_priorBytes, File.ReadAllBytes(backup));
            }

            if (reuseBackupPath)
            {
                File.Move(backup, Path.Combine(_directory, "moved"));
                File.WriteAllText(backup, "another writer");
            }

            if (blockDeleteSharing)
            {
                using FileStream reader = File.Open(
                    backup,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                );
                Publish("sharing refused");
                CollectionAssert.AreEqual(_priorBytes, File.ReadAllBytes(backup));
            }

            Publish("third");
            if (reuseBackupPath)
            {
                Assert.That(File.ReadAllText(backup), Is.EqualTo("another writer"));
                CollectionAssert.AreEqual(
                    _priorBytes,
                    File.ReadAllBytes(Path.Combine(_directory, "moved"))
                );
            }
            else
            {
                Assert.That(Directory.GetFiles(_directory, "*.bak"), Is.Empty);
            }
        }

        private void Publish(string contents)
        {
            Assert.That(
                DurableFile.TryWriteAllText(_destination, contents, out Exception error),
                Is.True,
                error?.ToString()
            );
            Assert.That(error, Is.Null);
            Assert.That(File.ReadAllText(_destination), Is.EqualTo(contents));
        }
    }
#endif
}
