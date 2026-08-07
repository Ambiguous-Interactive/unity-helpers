// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Integration")]
    public sealed class DurableFileTests : CommonTestBase
    {
        private static readonly string[] BlankPaths = { null, string.Empty, "   " };

        private string _testDirectory;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _testDirectory = Path.Combine(Application.temporaryCachePath, "DurableFileTests");
            Directory.CreateDirectory(_testDirectory);
        }

        [TearDown]
        public override void TearDown()
        {
            try
            {
                if (!string.IsNullOrEmpty(_testDirectory) && Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup, fall through to base teardown.
            }
            finally
            {
                base.TearDown();
            }
        }

        [Test]
        public void BlankPathsFailWithoutThrowing(
            [ValueSource(nameof(BlankPaths))] string blankPath
        )
        {
            Assert.IsFalse(
                DurableFile.TryWriteAllText(blankPath, "data", out Exception writeError)
            );
            Assert.IsTrue(writeError is ArgumentException);

            Assert.IsFalse(
                DurableFile.TryAppendAllText(blankPath, "data", out Exception appendError)
            );
            Assert.IsTrue(appendError is ArgumentException);

            string existing = WriteDirectly("blank-source.txt", "source");
            Assert.IsFalse(DurableFile.TryCopy(blankPath, existing, out Exception copySourceError));
            Assert.IsTrue(copySourceError is ArgumentException);

            Assert.IsFalse(
                DurableFile.TryCopy(existing, blankPath, out Exception copyDestinationError)
            );
            Assert.IsTrue(copyDestinationError is ArgumentException);

            Assert.IsFalse(DurableFile.TryDelete(blankPath));
        }

        [Test]
        public void WriteCreatesMissingDirectories()
        {
            string path = Path.Combine(_testDirectory, "a", "b", "c", "save.json");

            Assert.IsTrue(DurableFile.TryWriteAllText(path, "{}", out Exception error));

            Assert.IsTrue(error == null);
            Assert.AreEqual("{}", File.ReadAllText(path));
        }

        [Test]
        public void WriteReplacesExistingContentEntirely()
        {
            string path = WriteDirectly("save.json", "a much longer previous document");

            Assert.IsTrue(DurableFile.TryWriteAllText(path, "short", out Exception error));

            Assert.IsTrue(error == null);
            Assert.AreEqual("short", File.ReadAllText(path));
        }

        [Test]
        public void WriteLeavesNoStagedFileBehind()
        {
            string path = Path.Combine(_testDirectory, "save.json");

            Assert.IsTrue(DurableFile.TryWriteAllText(path, "payload", out Exception error));

            Assert.IsTrue(error == null);
            Assert.IsFalse(File.Exists(path + DurableFile.TemporarySuffix));
        }

        [Test]
        public void FailedWriteLeavesThePreviousContentsIntact()
        {
            string path = WriteDirectly("save.json", "the previous document");
            BlockStaging(path);

            Assert.IsFalse(
                DurableFile.TryWriteAllText(path, "the new document", out Exception error)
            );

            Assert.IsTrue(error != null);
            Assert.AreEqual("the previous document", File.ReadAllText(path));
        }

        [Test]
        public void WriteRoundTripsUnicodeWithoutAByteOrderMark()
        {
            string path = Path.Combine(_testDirectory, "unicode.json");
            const string contents = "{\"name\":\"ファイル — 🎮\"}";

            Assert.IsTrue(DurableFile.TryWriteAllText(path, contents, out Exception error));

            Assert.IsTrue(error == null);
            byte[] bytes = File.ReadAllBytes(path);
            Assert.AreEqual(new UTF8Encoding(false).GetBytes(contents), bytes);
            Assert.AreEqual(contents, File.ReadAllText(path));
        }

        [Test]
        public void WriteTreatsNullContentsAsEmpty()
        {
            string path = WriteDirectly("save.json", "previous");

            Assert.IsTrue(DurableFile.TryWriteAllText(path, null, out Exception error));

            Assert.IsTrue(error == null);
            Assert.AreEqual(0, new FileInfo(path).Length);
        }

        [Test]
        public void AppendCreatesThenAccumulates()
        {
            string path = Path.Combine(_testDirectory, "ledger.log");

            Assert.IsTrue(DurableFile.TryAppendAllText(path, "first\n", out Exception firstError));
            Assert.IsTrue(
                DurableFile.TryAppendAllText(path, "second\n", out Exception secondError)
            );

            Assert.IsTrue(firstError == null);
            Assert.IsTrue(secondError == null);
            Assert.AreEqual("first\nsecond\n", File.ReadAllText(path));
        }

        [Test]
        public void AppendingNothingSucceedsWithoutCreatingAFile()
        {
            string path = Path.Combine(_testDirectory, "ledger.log");

            Assert.IsTrue(DurableFile.TryAppendAllText(path, string.Empty, out Exception error));

            Assert.IsTrue(error == null);
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void ConcurrentAppendsInterleaveWholeRecords()
        {
            string path = Path.Combine(_testDirectory, "ledger.log");
            const int writerCount = 4;
            const int recordsPerWriter = 50;

            Task[] writers = new Task[writerCount];
            for (int writer = 0; writer < writerCount; ++writer)
            {
                int writerId = writer;
                writers[writer] = Task.Run(() =>
                {
                    for (int record = 0; record < recordsPerWriter; ++record)
                    {
                        Assert.IsTrue(
                            DurableFile.TryAppendAllText(path, $"{writerId}:{record}\n", out _)
                        );
                    }
                });
            }

            Task.WaitAll(writers);

            HashSet<string> records = new(File.ReadAllLines(path));
            records.Remove(string.Empty);
            Assert.AreEqual(writerCount * recordsPerWriter, records.Count);
        }

        [Test]
        public void CopyReplacesTheDestination()
        {
            string source = WriteDirectly("source.json", "source contents");
            string destination = WriteDirectly("destination.json", "a much longer destination");

            Assert.IsTrue(DurableFile.TryCopy(source, destination, out Exception error));

            Assert.IsTrue(error == null);
            Assert.AreEqual("source contents", File.ReadAllText(destination));
            Assert.IsFalse(File.Exists(destination + DurableFile.TemporarySuffix));
        }

        [Test]
        public void AFailedCopyLeavesTheDestinationIntact()
        {
            string source = WriteDirectly("source.json", "source contents");
            string destination = WriteDirectly("destination.json", "the previous document");
            BlockStaging(destination);

            Assert.IsFalse(DurableFile.TryCopy(source, destination, out Exception error));

            Assert.IsTrue(error != null);
            Assert.AreEqual("the previous document", File.ReadAllText(destination));
        }

        [Test]
        public void CopyFromAMissingSourceLeavesTheDestinationIntact()
        {
            string source = Path.Combine(_testDirectory, "missing.json");
            string destination = WriteDirectly("destination.json", "the previous document");

            Assert.IsFalse(DurableFile.TryCopy(source, destination, out Exception error));

            Assert.IsTrue(error != null);
            Assert.AreEqual("the previous document", File.ReadAllText(destination));
        }

        [Test]
        public void DeleteReportsSuccessWhenNothingIsThere()
        {
            Assert.IsTrue(DurableFile.TryDelete(Path.Combine(_testDirectory, "absent.json")));
        }

        [UnityTest]
        public IEnumerator WriteAsyncReplacesExistingContentEntirely()
        {
            string path = WriteDirectly("save.json", "a much longer previous document");

            Task<Exception> write = DurableFile.WriteAllTextAsync(path, "short").AsTask();
            while (!write.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(write.Result == null);
            Assert.AreEqual("short", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + DurableFile.TemporarySuffix));
        }

        [UnityTest]
        public IEnumerator WriteAsyncLeavesThePreviousContentsIntactWhenStagingFails()
        {
            string path = WriteDirectly("save.json", "the previous document");
            BlockStaging(path);

            Task<Exception> write = DurableFile
                .WriteAllTextAsync(path, "the new document")
                .AsTask();
            while (!write.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(write.Result != null);
            Assert.AreEqual("the previous document", File.ReadAllText(path));
        }

        [UnityTest]
        public IEnumerator AppendAsyncCreatesThenAccumulates()
        {
            string path = Path.Combine(_testDirectory, "ledger.log");

            Task<Exception> first = DurableFile.AppendAllTextAsync(path, "first\n").AsTask();
            while (!first.IsCompleted)
            {
                yield return null;
            }

            Task<Exception> second = DurableFile.AppendAllTextAsync(path, "second\n").AsTask();
            while (!second.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(first.Result == null);
            Assert.IsTrue(second.Result == null);
            Assert.AreEqual("first\nsecond\n", File.ReadAllText(path));
        }

        [UnityTest]
        public IEnumerator CopyAsyncReplacesTheDestination()
        {
            string source = WriteDirectly("source.json", "source contents");
            string destination = WriteDirectly("destination.json", "a much longer destination");

            Task<Exception> copy = DurableFile.CopyAsync(source, destination).AsTask();
            while (!copy.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(copy.Result == null);
            Assert.AreEqual("source contents", File.ReadAllText(destination));
            Assert.IsFalse(File.Exists(destination + DurableFile.TemporarySuffix));
        }

        [UnityTest]
        public IEnumerator CopyAsyncLeavesTheDestinationIntactWhenTheStagedCopyFails()
        {
            string source = WriteDirectly("source.json", "source contents");
            string destination = WriteDirectly("destination.json", "the previous document");
            BlockStaging(destination);

            Task<Exception> copy = DurableFile.CopyAsync(source, destination).AsTask();
            while (!copy.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(copy.Result != null);
            Assert.AreEqual("the previous document", File.ReadAllText(destination));
        }

        [UnityTest]
        public IEnumerator CopyAsyncRejectsBlankPathsWithoutTouchingTheDisk()
        {
            string destination = Path.Combine(_testDirectory, "destination.json");

            Task<Exception> copy = DurableFile.CopyAsync("   ", destination).AsTask();
            while (!copy.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(copy.Result is ArgumentException);
            Assert.IsFalse(File.Exists(destination));
            Assert.IsFalse(File.Exists(destination + DurableFile.TemporarySuffix));
        }

        private string WriteDirectly(string fileName, string contents)
        {
            string path = Path.Combine(_testDirectory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        // Occupying the staged path with a directory makes the staging step fail without needing to
        // kill the process, which is the only other way to reach the interrupted-write branch.
        private static void BlockStaging(string path)
        {
            Directory.CreateDirectory(path + DurableFile.TemporarySuffix);
        }
    }
}
