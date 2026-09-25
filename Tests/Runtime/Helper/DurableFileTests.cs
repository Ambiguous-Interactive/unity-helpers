// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
#if UNITY_EDITOR
    using System.Diagnostics;
#endif

    [TestFixture]
    [NUnit.Framework.Category("Integration")]
    public sealed class DurableFileTests : CommonTestBase
    {
        private static readonly string[] BlankPaths = { null, string.Empty, "   " };

        private string _testDirectory;

        // A directory at the staging path forces the interrupted-write branch without killing the process.
        private static void BlockStaging(string path)
        {
            Directory.CreateDirectory(path + DurableFile.TemporarySuffix);
        }

#if UNITY_EDITOR
        private static int TryOverwriteStagingInAnotherProcess(
            string scriptPath,
            string stagingPath
        )
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "pwsh",
                Arguments = $"-NoProfile -NonInteractive -File \"{scriptPath}\" \"{stagingPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using Process process = Process.Start(startInfo);
            if (process == null || !process.WaitForExit(10_000))
            {
                process?.Kill();
                return -1;
            }

            return process.ExitCode;
        }

        private static void WriteStagingOwnershipProbe(string scriptPath)
        {
            File.WriteAllText(
                scriptPath,
                "param([string]$Path)\n"
                    + "try {\n"
                    + "  $stream = [System.IO.FileStream]::new(($Path + '.lock'), [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::Read)\n"
                    + "  $stream.WriteByte(88)\n"
                    + "  $stream.Dispose()\n"
                    + "  exit 0\n"
                    + "} catch {\n"
                    + "  exit 23\n"
                    + "}\n"
            );
        }
#endif

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
        public void CompareThenReplaceBytesWritesOnMatchAndRejectsObservedMismatch()
        {
            string path = WriteDirectly("compare.bin", "original");
            byte[] original = File.ReadAllBytes(path);
            byte[] replacement = { 0, 255, 0, 42 };

            Assert.IsTrue(
                DurableFile.TryCompareThenReplaceBytes(
                    path,
                    original,
                    replacement,
                    out Exception successError
                ),
                successError?.ToString()
            );
            Assert.IsTrue(successError == null);
            CollectionAssert.AreEqual(replacement, File.ReadAllBytes(path));

            Assert.IsFalse(
                DurableFile.TryCompareThenReplaceBytes(
                    path,
                    original,
                    new byte[] { 1 },
                    out Exception mismatchError
                )
            );
            Assert.IsInstanceOf<InvalidOperationException>(mismatchError);
            CollectionAssert.AreEqual(replacement, File.ReadAllBytes(path));
        }

        [Test]
        public void CompareThenReplaceBytesRejectsMissingAndInvalidInputsWithoutCreatingAFile()
        {
            string path = Path.Combine(_testDirectory, "missing.bin");
            Assert.IsFalse(
                DurableFile.TryCompareThenReplaceBytes(
                    path,
                    Array.Empty<byte>(),
                    new byte[] { 1 },
                    out Exception missingError
                )
            );
            Assert.IsInstanceOf<FileNotFoundException>(missingError);
            Assert.IsFalse(File.Exists(path));

            Assert.IsFalse(
                DurableFile.TryCompareThenReplaceBytes(
                    path,
                    null,
                    new byte[] { 1 },
                    out Exception invalidError
                )
            );
            Assert.IsInstanceOf<ArgumentNullException>(invalidError);
            Assert.IsFalse(File.Exists(path));
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

        [TestCase(true)]
        [TestCase(false)]
        public void EncodedWritePreservesPreambleAndPreviousBytesOnStagingFailure(bool emitBom)
        {
            string path = Path.Combine(_testDirectory, "encoded.json");
            Encoding encoding = new UTF8Encoding(emitBom);
            const string contents = "{\"name\":\"ファイル\"}";

            Assert.IsTrue(
                DurableFile.TryWriteAllText(path, contents, encoding, out Exception writeError)
            );
            Assert.IsTrue(writeError == null);
            byte[] expectedPreamble = encoding.GetPreamble();
            byte[] expectedContents = encoding.GetBytes(contents);
            byte[] actual = File.ReadAllBytes(path);
            Assert.AreEqual(expectedPreamble.Length + expectedContents.Length, actual.Length);
            for (int i = 0; i < expectedPreamble.Length; ++i)
            {
                Assert.AreEqual(expectedPreamble[i], actual[i]);
            }
            for (int i = 0; i < expectedContents.Length; ++i)
            {
                Assert.AreEqual(expectedContents[i], actual[expectedPreamble.Length + i]);
            }

            BlockStaging(path);
            Assert.IsFalse(
                DurableFile.TryWriteAllText(path, "replacement", encoding, out Exception error)
            );
            Assert.IsTrue(error != null);
            CollectionAssert.AreEqual(actual, File.ReadAllBytes(path));

            Directory.Delete(path + DurableFile.TemporarySuffix);
            Assert.IsTrue(DurableFile.TryWriteAllText(path, null, encoding, out error));
            Assert.IsTrue(error == null);
            CollectionAssert.AreEqual(expectedPreamble, File.ReadAllBytes(path));
        }

        [Test]
        public void EncodedWriteRejectsNullEncodingWithoutChangingTheFile()
        {
            string path = WriteDirectly("encoded.json", "previous");

            Assert.IsFalse(DurableFile.TryWriteAllText(path, "new", null, out Exception error));

            Assert.IsInstanceOf<ArgumentNullException>(error);
            Assert.AreEqual("previous", File.ReadAllText(path));
        }

        [Test]
        public void WriteTreatsNullContentsAsEmpty()
        {
            string path = WriteDirectly("save.json", "previous");

            Assert.IsTrue(DurableFile.TryWriteAllText(path, null, out Exception error));

            Assert.IsTrue(error == null);
            Assert.AreEqual(0, new FileInfo(path).Length);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(256)]
        [TestCase(16384)]
        public void ByteWriteCreatesThenReplacesWithExactContents(int length)
        {
            string path = Path.Combine(_testDirectory, "binary", "save.bin");
            byte[] contents = new byte[length];
            for (int i = 0; i < contents.Length; ++i)
            {
                contents[i] = (byte)(i % 256);
            }

            Assert.IsTrue(DurableFile.TryWriteAllBytes(path, contents, out Exception createError));
            Assert.IsTrue(createError == null);
            CollectionAssert.AreEqual(contents, File.ReadAllBytes(path));
            Assert.IsFalse(File.Exists(path + DurableFile.TemporarySuffix));

            byte[] replacement = { 255, 0, 128 };
            Assert.IsTrue(
                DurableFile.TryWriteAllBytes(path, replacement, out Exception replaceError)
            );
            Assert.IsTrue(replaceError == null);
            CollectionAssert.AreEqual(replacement, File.ReadAllBytes(path));
            Assert.IsFalse(File.Exists(path + DurableFile.TemporarySuffix));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ByteWriteTreatsNullAndEmptyContentsAsEmpty(bool useNull)
        {
            string path = WriteDirectly("save.bin", "previous");
            byte[] contents = useNull ? null : Array.Empty<byte>();

            Assert.IsTrue(DurableFile.TryWriteAllBytes(path, contents, out Exception error));

            Assert.IsTrue(error == null);
            Assert.AreEqual(0, new FileInfo(path).Length);
            Assert.IsFalse(File.Exists(path + DurableFile.TemporarySuffix));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("invalid\0path")]
        public void ByteWriteRejectsInvalidPaths(string path)
        {
            Assert.IsFalse(
                DurableFile.TryWriteAllBytes(path, new byte[] { 255 }, out Exception error)
            );
            Assert.IsTrue(error is ArgumentException);
        }

        [Test]
        public void FailedByteWriteLeavesPreviousContentsAndBlockedStagingIntact()
        {
            string path = WriteDirectly("save.bin", "previous");
            BlockStaging(path);

            Assert.IsFalse(
                DurableFile.TryWriteAllBytes(path, new byte[] { 255 }, out Exception error)
            );

            Assert.IsTrue(error != null);
            Assert.AreEqual("previous", File.ReadAllText(path));
            Assert.IsTrue(Directory.Exists(path + DurableFile.TemporarySuffix));
        }

        [Test]
        public void FailedByteWriteSwapRemovesItsStagedFile()
        {
            string path = Path.Combine(_testDirectory, "save.bin");
            Directory.CreateDirectory(path);

            Assert.IsFalse(
                DurableFile.TryWriteAllBytes(path, new byte[] { 255 }, out Exception error)
            );

            Assert.IsTrue(error != null);
            Assert.IsTrue(Directory.Exists(path));
            Assert.IsFalse(File.Exists(path + DurableFile.TemporarySuffix));
        }

        [Test]
        public void ConcurrentByteAndTextWritesLeaveOneCompletePayload()
        {
            string path = Path.Combine(_testDirectory, "save.bin");
            string text = new string('a', 16384);
            byte[] bytes = new byte[16384];
            using Barrier start = new(2);
            Task<bool> textWrite = Task.Run(() =>
            {
                Assert.IsTrue(start.SignalAndWait(TimeSpan.FromSeconds(10)));
                return DurableFile.TryWriteAllText(path, text, out _);
            });
            Task<bool> byteWrite = Task.Run(() =>
            {
                Assert.IsTrue(start.SignalAndWait(TimeSpan.FromSeconds(10)));
                return DurableFile.TryWriteAllBytes(path, bytes, out _);
            });

            Task.WaitAll(textWrite, byteWrite);

            Assert.IsTrue(textWrite.Result);
            Assert.IsTrue(byteWrite.Result);
            byte[] actual = File.ReadAllBytes(path);
            Assert.AreEqual(bytes.Length, actual.Length);
            byte expected = actual[0];
            Assert.IsTrue(expected == 0 || expected == (byte)'a');
            CollectionAssert.AreEqual(expected == 0 ? bytes : Encoding.UTF8.GetBytes(text), actual);
            Assert.IsFalse(File.Exists(path + DurableFile.TemporarySuffix));
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
        public void AFailureBeforeStagingLeavesAnExistingStagedFileAlone()
        {
            // A preexisting staging file belongs to another operation until this call opens it.
            string source = Path.Combine(_testDirectory, "missing.json");
            string destination = WriteDirectly("destination.json", "the previous document");
            string staged = destination + DurableFile.TemporarySuffix;
            File.WriteAllText(staged, "another writer's staged document");

            Assert.IsFalse(DurableFile.TryCopy(source, destination, out Exception error));

            Assert.IsTrue(error != null);
            Assert.AreEqual("another writer's staged document", File.ReadAllText(staged));
            Assert.AreEqual("the previous document", File.ReadAllText(destination));
        }

        [Test]
        public void AFailureAfterStagingRemovesTheStagedFile()
        {
            string source = WriteDirectly("source.json", "source contents");
            // Fail the final swap after staging ownership has been acquired.
            string destination = Path.Combine(_testDirectory, "destination.json");
            Directory.CreateDirectory(destination);

            Assert.IsFalse(DurableFile.TryCopy(source, destination, out Exception error));

            Assert.IsTrue(error != null);
            Assert.IsFalse(File.Exists(destination + DurableFile.TemporarySuffix));
        }

        [Test]
        public void AFailedWriteAfterStagingRemovesTheStagedFile()
        {
            string path = Path.Combine(_testDirectory, "save.json");
            Directory.CreateDirectory(path);
#if UNITY_EDITOR
            string scriptPath = Path.Combine(_testDirectory, "probe-failed-write-staging.ps1");
            WriteStagingOwnershipProbe(scriptPath);
            int childExitCode = -1;
            DurableFile.BeforeStagedCleanupForTests = temporaryPath =>
            {
                childExitCode = TryOverwriteStagingInAnotherProcess(scriptPath, temporaryPath);
            };
#endif

            try
            {
                Assert.IsFalse(
                    DurableFile.TryWriteAllText(path, "the new document", out Exception error)
                );

                Assert.IsTrue(error != null);
            }
            finally
            {
#if UNITY_EDITOR
                DurableFile.BeforeStagedCleanupForTests = null;
#endif
            }

#if UNITY_EDITOR
            Assert.AreEqual(23, childExitCode, "Cleanup must retain staging ownership.");
#endif
            Assert.IsFalse(File.Exists(path + DurableFile.TemporarySuffix));
        }

#if UNITY_EDITOR
        [Test]
        public void AnotherProcessCannotReplaceStagingBeforeItIsPublished()
        {
            string path = WriteDirectly("cross-process.json", "previous document");
            string scriptPath = Path.Combine(_testDirectory, "overwrite-staging.ps1");
            WriteStagingOwnershipProbe(scriptPath);
            int childExitCode = -1;
            DurableFile.BeforeStagedSwapForTests = temporaryPath =>
            {
                childExitCode = TryOverwriteStagingInAnotherProcess(scriptPath, temporaryPath);
            };

            try
            {
                Assert.IsTrue(
                    DurableFile.TryWriteAllText(path, "writer A document", out Exception error),
                    error?.ToString()
                );
            }
            finally
            {
                DurableFile.BeforeStagedSwapForTests = null;
            }

            Assert.AreEqual(23, childExitCode, "The second process must not acquire staging.");
            Assert.AreEqual("writer A document", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + DurableFile.TemporarySuffix));
        }

        [Test]
        public void CompareThenReplaceOwnsStagingFromBeforeComparisonThroughSwap()
        {
            string path = WriteDirectly("compare-cross-process.bin", "expected");
            string scriptPath = Path.Combine(_testDirectory, "compare-staging-probe.ps1");
            WriteStagingOwnershipProbe(scriptPath);
            int beforeReadExitCode = -1;
            int beforeSwapExitCode = -1;
            DurableFile.BeforeCompareReadForTests = temporaryPath =>
            {
                beforeReadExitCode = TryOverwriteStagingInAnotherProcess(scriptPath, temporaryPath);
            };
            DurableFile.BeforeStagedSwapForTests = temporaryPath =>
            {
                beforeSwapExitCode = TryOverwriteStagingInAnotherProcess(scriptPath, temporaryPath);
            };

            try
            {
                Assert.IsTrue(
                    DurableFile.TryCompareThenReplaceBytes(
                        path,
                        Encoding.UTF8.GetBytes("expected"),
                        Encoding.UTF8.GetBytes("replacement"),
                        out Exception error
                    ),
                    error?.ToString()
                );
            }
            finally
            {
                DurableFile.BeforeCompareReadForTests = null;
                DurableFile.BeforeStagedSwapForTests = null;
            }

            Assert.AreEqual(
                23,
                beforeReadExitCode,
                "A second writer acquired staging before compare."
            );
            Assert.AreEqual(
                23,
                beforeSwapExitCode,
                "A second writer acquired staging before swap."
            );
            Assert.AreEqual("replacement", File.ReadAllText(path));
        }
#endif

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
        public void CopyFromAMissingSourceCreatesNothing()
        {
            // A missing source must not leave newly created destination directories behind.
            string source = Path.Combine(_testDirectory, "missing.json");
            string destination = Path.Combine(_testDirectory, "a", "b", "c", "destination.json");

            Assert.IsFalse(DurableFile.TryCopy(source, destination, out Exception error));

            Assert.IsTrue(error != null);
            Assert.IsFalse(Directory.Exists(Path.Combine(_testDirectory, "a")));
        }

        [Test]
        public void CopyClosesTheSourceWhenDestinationDirectoryCreationFails()
        {
            string source = WriteDirectly("source.json", "source contents");
            string blockedDirectory = WriteDirectly("not-a-directory", "existing file");
            string destination = Path.Combine(blockedDirectory, "destination.json");

            Assert.IsFalse(DurableFile.TryCopy(source, destination, out Exception error));

            Assert.IsTrue(error != null);
            Assert.AreEqual("source contents", File.ReadAllText(source));
            using FileStream exclusiveSource = new(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None
            );
            Assert.AreEqual("source contents".Length, exclusiveSource.Length);
        }

        [Test]
        public void DeleteReportsSuccessWhenNothingIsThere()
        {
            Assert.IsTrue(DurableFile.TryDelete(Path.Combine(_testDirectory, "absent.json")));
        }

        [Test]
        public void DeleteReportsFailureForDirectoryPath()
        {
            Assert.IsFalse(DurableFile.TryDelete(_testDirectory));
            Assert.IsTrue(Directory.Exists(_testDirectory));
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
        public IEnumerator WriteBytesAsyncReplacesExistingContentAndAcceptsNull()
        {
            string path = WriteDirectly("save.bin", "a much longer previous document");
            byte[] contents = { 0, 1, 255, 2 };

            Task<Exception> write = DurableFile.WriteAllBytesAsync(path, contents).AsTask();
            while (!write.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(write.Result == null);
            CollectionAssert.AreEqual(contents, File.ReadAllBytes(path));
            Assert.IsFalse(File.Exists(path + DurableFile.TemporarySuffix));

            Task<Exception> clear = DurableFile.WriteAllBytesAsync(path, null).AsTask();
            while (!clear.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(clear.Result == null);
            Assert.AreEqual(0, new FileInfo(path).Length);
        }

        [UnityTest]
        public IEnumerator WriteBytesAsyncPreservesDestinationWhenStagingFails()
        {
            string path = WriteDirectly("save.bin", "previous document");
            BlockStaging(path);

            Task<Exception> write = DurableFile
                .WriteAllBytesAsync(path, new byte[] { 1, 2 })
                .AsTask();
            while (!write.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(write.Result != null);
            Assert.AreEqual("previous document", File.ReadAllText(path));
        }

        [UnityTest]
        public IEnumerator WriteBytesAsyncReportsCancellationWithoutChangingDestination()
        {
            string path = WriteDirectly("save.bin", "previous document");
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Task<Exception> write = DurableFile
                .WriteAllBytesAsync(path, new byte[] { 1, 2 }, cancellation.Token)
                .AsTask();
            while (!write.IsCompleted)
            {
                yield return null;
            }

            Assert.IsInstanceOf<OperationCanceledException>(write.Result);
            Assert.AreEqual("previous document", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + DurableFile.TemporarySuffix));
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

        [UnityTest]
        public IEnumerator CopyAsyncClosesTheSourceWhenDestinationDirectoryCreationFails()
        {
            string source = WriteDirectly("source.json", "source contents");
            string blockedDirectory = WriteDirectly("not-a-directory", "existing file");
            string destination = Path.Combine(blockedDirectory, "destination.json");

            Task<Exception> copy = DurableFile.CopyAsync(source, destination).AsTask();
            while (!copy.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(copy.Result != null);
            Assert.AreEqual("source contents", File.ReadAllText(source));
            using FileStream exclusiveSource = new(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None
            );
            Assert.AreEqual("source contents".Length, exclusiveSource.Length);
        }

        private string WriteDirectly(string fileName, string contents)
        {
            string path = Path.Combine(_testDirectory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }
    }
}
