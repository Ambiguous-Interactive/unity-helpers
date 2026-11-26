// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using File = System.IO.File;
    using NativePlatform = System.Runtime.InteropServices.RuntimeInformation;
    using OSPlatform = System.Runtime.InteropServices.OSPlatform;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class DurableFileCompareExchangeTests
    {
        private string _directory;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(
                DurableFileSidecarMoveTests.GetTemporaryRoot(),
                $"durable-compare-{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "save.dat");
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void DistinguishesEmptyFromAbsent()
        {
            byte[] empty = Array.Empty<byte>();
            AssertExchange(empty, new byte[] { 1 }, exchanged: false);
            AssertExchange(null, null, exchanged: true);
            AssertExchange(null, empty, exchanged: true);
            AssertExchange(null, null, exchanged: false);
            Assert.AreEqual(0, new FileInfo(_path).Length);
            AssertExchange(empty, null, exchanged: true);
            Assert.IsFalse(File.Exists(_path));
            Assert.IsEmpty(Directory.GetFiles(_directory));
        }

        [Test]
        public void RestoresSnapshotOnlyWhileThePublishedBytesStillMatch()
        {
            byte[] snapshot = { 1, 2 };
            byte[] published = { 2, 3 };
            byte[] later = { 3, 4 };
            File.WriteAllBytes(_path, snapshot);
            AssertExchange(snapshot, published, exchanged: true);
            CollectionAssert.AreEqual(published, File.ReadAllBytes(_path));
            AssertExchange(published, snapshot, exchanged: true);
            CollectionAssert.AreEqual(snapshot, File.ReadAllBytes(_path));
            AssertExchange(snapshot, published, exchanged: true);
            AssertExchange(published, later, exchanged: true);
            AssertExchange(published, snapshot, exchanged: false);
            CollectionAssert.AreEqual(later, File.ReadAllBytes(_path));
            CollectionAssert.AreEqual(new[] { _path }, Directory.GetFiles(_directory));
        }

        [Test]
        public void ComparesTheTailOfAFileLargerThanTheComparisonBuffer()
        {
            byte[] current = new byte[14_003];
            current[current.Length - 1] = 42;
            File.WriteAllBytes(_path, current);
            AssertExchange(new byte[current.Length], null, exchanged: false);
            CollectionAssert.AreEqual(current, File.ReadAllBytes(_path));
            AssertExchange(current, null, exchanged: true);
            Assert.IsFalse(File.Exists(_path));
        }

        [Test]
        public void KeepsPredictableTemporarySiblingIntact()
        {
            string sibling = _path + DurableFile.TemporarySuffix;
            File.WriteAllText(sibling, "unrelated file");
            byte[] previous = { 1, 2 };
            byte[] replacement = { 3, 4 };
            Assert.IsTrue(
                DurableFile.TryWriteAllBytes(_path, previous, out Exception writeError),
                writeError?.ToString()
            );
            AssertExchange(previous, replacement, exchanged: true);
            string copy = Path.Combine(_directory, "copy.dat");
            string copySibling = copy + DurableFile.TemporarySuffix;
            File.WriteAllText(copySibling, "another unrelated file");
            Assert.IsTrue(
                DurableFile.TryCopy(_path, copy, out Exception copyError),
                copyError?.ToString()
            );

            Assert.AreEqual("unrelated file", File.ReadAllText(sibling));
            Assert.AreEqual("another unrelated file", File.ReadAllText(copySibling));
            CollectionAssert.AreEqual(replacement, File.ReadAllBytes(copy));
            CollectionAssert.AreEquivalent(
                new[] { _path, sibling, copy, copySibling },
                Directory.GetFiles(_directory)
            );
        }

        [Test]
        public void RefusesToDeleteOrTruncateAnOccupiedWriterLock()
        {
            string lockPath = _path + DurableFile.TemporarySuffix + DurableFile.OwnershipSuffix;
            File.WriteAllText(lockPath, "unrelated data in the lock slot");
            File.WriteAllText(_path, "previous contents");

            Assert.IsFalse(
                DurableFile.TryWriteAllText(_path, "replacement", out Exception writeError)
            );
            Assert.IsInstanceOf<IOException>(writeError);
            Assert.IsFalse(DurableFile.TryDelete(_path));
            Assert.AreEqual("previous contents", File.ReadAllText(_path));
            Assert.AreEqual("unrelated data in the lock slot", File.ReadAllText(lockPath));
        }

        [Test]
        public void SynchronizesCreationReplacementAndDeletion()
        {
            if (
                !NativePlatform.IsOSPlatform(OSPlatform.Linux)
                && !NativePlatform.IsOSPlatform(OSPlatform.Windows)
            )
            {
                Assert.Ignore(
                    "Parent directory synchronization requires Linux or local Windows NTFS."
                );
            }

            byte[] original = { 1, 2 };
            byte[] updated = { 3, 4 };
            AssertExchange(null, original, exchanged: true, syncParentDirectory: true);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(_path));
            AssertExchange(original, updated, exchanged: true, syncParentDirectory: true);
            CollectionAssert.AreEqual(updated, File.ReadAllBytes(_path));
            AssertExchange(updated, null, exchanged: true, syncParentDirectory: true);
            Assert.IsEmpty(Directory.GetFiles(_directory));
        }

        [Test]
        public void SynchronizedCreationRequiresAnExistingParent()
        {
            string missingParent = Path.Combine(_directory, "missing");
            _path = Path.Combine(missingParent, "save.dat");

            Assert.IsFalse(
                DurableFile.TryCompareExchangeBytes(
                    _path,
                    null,
                    new byte[] { 1 },
                    syncParentDirectory: true,
                    out bool exchanged,
                    out Exception error
                )
            );
            Assert.IsFalse(exchanged);
            Assert.IsInstanceOf<DirectoryNotFoundException>(error);
            Assert.IsFalse(Directory.Exists(missingParent));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SynchronizedWindowsDeletionRespectsReaderSharing(bool shareDelete)
        {
            if (!NativePlatform.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Tests Windows deletion behavior on NTFS.");
            }

            byte[] content = { 7 };
            File.WriteAllBytes(_path, content);
            FileShare sharing = shareDelete ? FileShare.Read | FileShare.Delete : FileShare.Read;
            using FileStream reader = new(_path, FileMode.Open, FileAccess.Read, sharing);
            bool completed = DurableFile.TryCompareExchangeBytes(
                _path,
                content,
                null,
                syncParentDirectory: true,
                out bool exchanged,
                out Exception error
            );

            if (shareDelete)
            {
                Assert.IsTrue(completed, error?.ToString());
                Assert.IsTrue(exchanged);
                Assert.IsFalse(File.Exists(_path));
                using (FileStream reopened = new(_path, FileMode.CreateNew, FileAccess.Write))
                {
                    reopened.WriteByte(8);
                }
            }
            else
            {
                Assert.IsFalse(completed);
                Assert.IsFalse(exchanged);
                Assert.IsInstanceOf<IOException>(error);
                CollectionAssert.AreEqual(content, File.ReadAllBytes(_path));
            }

            Assert.AreEqual(7, reader.ReadByte());
        }

        private void AssertExchange(
            byte[] expected,
            byte[] replacement,
            bool exchanged,
            bool syncParentDirectory = false
        )
        {
            Assert.IsTrue(
                DurableFile.TryCompareExchangeBytes(
                    _path,
                    expected,
                    replacement,
                    syncParentDirectory,
                    out bool actual,
                    out Exception error
                ),
                error?.ToString()
            );
            Assert.IsTrue(error == null);
            Assert.AreEqual(exchanged, actual);
        }
    }
}
