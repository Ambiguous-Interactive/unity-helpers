// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.DurableFile.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Helper;
    using RealFile = System.IO.File;
    using RuntimeInformation = WallstopStudios.UnityHelpers.Core.Helper.RuntimeInformation;
    using SimulatedFile = WallstopStudios.UnityHelpers.Core.Helper.File;

    /// <summary>Checks source-linked publication with explicitly simulated capability failures.</summary>
    /// <remarks>Only DurableFile, SemaphoreLease, and DisposalLease are linked unchanged; the integer math dependency is mirrored to avoid Unity dependencies. This is not Unity platform qualification.</remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class DurableFileReplacementTests
    {
        private const int UnableToMoveReplacement = unchecked((int)0x80070498);
        private const int UnableToMoveReplacementAfterBackup = unchecked((int)0x80070499);
        private const string ReplacementText = "replacement player data";
        private const string SliceWriteOperation = "WriteBytesSlice";

        private static readonly string[] Operations =
        {
            nameof(DurableFile.TryWriteAllText),
            nameof(Encoding.Unicode),
            nameof(DurableFile.TryWriteAllBytes),
            SliceWriteOperation,
            nameof(DurableFile.TryCopy),
            nameof(DurableFile.WriteAllTextAsync),
            nameof(DurableFile.WriteAllBytesAsync),
            nameof(DurableFile.CopyAsync),
        };

        private static readonly byte[] OriginalBytes = { 0, 255, 18, 42, 0 };
        private static readonly byte[] ReplacementBytes = { 42, 0, 255, 16 };

        private string _directory;
        private string _destination;
        private string _source;

        private static IEnumerable<TestCaseData> UnsupportedCases()
        {
            foreach (string operation in Operations)
            {
                foreach (bool platformFailure in new[] { false, true })
                {
                    foreach (bool rejectMove in new[] { false, true })
                    {
                        yield return new TestCaseData(
                            operation,
                            platformFailure,
                            rejectMove
                        ).SetName(
                            $"Unsupported.{operation}.PlatformFailure{platformFailure}.RejectMove{rejectMove}"
                        );
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> SupportedCases()
        {
            foreach (string operation in Operations)
            {
                foreach (bool existingDestination in new[] { false, true })
                {
                    yield return new TestCaseData(operation, existingDestination).SetName(
                        $"Supported.{operation}.ExistingDestination{existingDestination}"
                    );
                }
            }
        }

        private static IEnumerable<TestCaseData> UnsupportedCreationCases()
        {
            foreach (string operation in Operations)
            {
                foreach (bool platformFailure in new[] { false, true })
                {
                    yield return new TestCaseData(operation, platformFailure).SetName(
                        $"UnsupportedCreation.{operation}.PlatformFailure{platformFailure}"
                    );
                }
            }
        }

        private static IEnumerable<TestCaseData> RetryCases()
        {
            foreach (string operation in Operations)
            {
                foreach (bool platformFailure in new[] { false, true })
                {
                    yield return new TestCaseData(operation, platformFailure).SetName(
                        $"Retry.{operation}.PlatformFailure{platformFailure}"
                    );
                }
            }
        }

        private static IEnumerable<TestCaseData> SharingFailureCases()
        {
            foreach (string operation in Operations)
            {
                foreach (int errorCode in new[] { 32, 1175, 29 })
                {
                    foreach (int failures in new[] { 1, 2, 3, 4 })
                    {
                        yield return new TestCaseData(operation, errorCode, failures).SetName(
                            $"Replacement.{operation}.Error{errorCode}.Failures{failures}"
                        );
                    }
                }
            }
        }

        private static Exception UnsupportedFailure(bool platformFailure)
        {
            return platformFailure
                ? new PlatformNotSupportedException("Simulated unsupported atomic replacement.")
                : new NotSupportedException("Simulated unsupported atomic replacement.");
        }

        private static IEnumerable<TestCaseData> WindowsPublicationFailureCases()
        {
            foreach (string operation in Operations)
            {
                yield return new TestCaseData(operation, UnableToMoveReplacement, false).SetName(
                    $"WindowsReplacement.{operation}.KeepOriginal"
                );
                yield return new TestCaseData(
                    operation,
                    UnableToMoveReplacementAfterBackup,
                    false
                ).SetName($"WindowsReplacement.{operation}.RestoreBackup");
                yield return new TestCaseData(
                    operation,
                    UnableToMoveReplacementAfterBackup,
                    true
                ).SetName($"WindowsReplacement.{operation}.PreserveBackupOnConflict");
            }
        }

        private static byte[] ExpectedBytes(string operation)
        {
            if (string.Equals(operation, nameof(Encoding.Unicode), StringComparison.Ordinal))
            {
                byte[] preamble = Encoding.Unicode.GetPreamble();
                byte[] text = Encoding.Unicode.GetBytes(ReplacementText);
                byte[] encoded = new byte[preamble.Length + text.Length];
                Buffer.BlockCopy(preamble, 0, encoded, 0, preamble.Length);
                Buffer.BlockCopy(text, 0, encoded, preamble.Length, text.Length);
                return encoded;
            }

            return
                string.Equals(
                    operation,
                    nameof(DurableFile.TryWriteAllText),
                    StringComparison.Ordinal
                )
                || string.Equals(
                    operation,
                    nameof(DurableFile.WriteAllTextAsync),
                    StringComparison.Ordinal
                )
                ? Encoding.UTF8.GetBytes(ReplacementText)
                : ReplacementBytes;
        }

        private static string FindMount(string path)
        {
            string best = null;
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                string mount = drive.Name;
                if (
                    (best == null || best.Length < mount.Length)
                    && (
                        string.Equals(path, mount, StringComparison.Ordinal)
                        || path.StartsWith(
                            Path.EndsInDirectorySeparator(mount) ? mount : mount + "/",
                            StringComparison.Ordinal
                        )
                    )
                )
                {
                    best = mount;
                }
            }
            return best;
        }

        [SetUp]
        public void SetUp()
        {
            SimulatedFile.Reset();
            _directory = Path.Combine(
                DurableFileSidecarMoveTests.GetTemporaryRoot(),
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_directory);
            _destination = Path.Combine(_directory, "player.save");
            _source = Path.Combine(_directory, "source.save");
            RealFile.WriteAllBytes(_source, ReplacementBytes);
            SimulatedFile.ObservedDestination = _destination;
            TestContext.WriteLine(
                "Qualified .NET source-linked test: File replacement capability failures are simulated; no Unity or native unsupported-platform result is claimed."
            );
        }

        [TearDown]
        public void TearDown()
        {
            SimulatedFile.Reset();
            RuntimeInformation.SimulatedPlatform = null;
            Directory.Delete(_directory, recursive: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompareMissingTextPreservesFileCreatedAfterRead(bool simulateWindows)
        {
            if (simulateWindows)
            {
                RuntimeInformation.SimulatedPlatform = OSPlatform.Windows;
            }

            const string intervening = "suppression created by another writer";
            SimulatedFile.AfterMissingTextRead = path => RealFile.WriteAllText(path, intervening);

            bool completed = DurableFile.TryCompareThenReplaceAllText(
                _destination,
                expectedExists: false,
                expectedContents: string.Empty,
                replacementContents: ReplacementText,
                out bool exchanged,
                out Exception error
            );

            Assert.IsTrue(completed, error?.ToString());
            Assert.AreEqual(intervening, RealFile.ReadAllText(_destination));
            Assert.IsFalse(exchanged);
            Assert.AreEqual(0, SimulatedFile.ReplacementCalls);
            CollectionAssert.AreEquivalent(
                new[] { _source, _destination },
                Directory.GetFiles(_directory)
            );
        }

        [TestCase(false, 12)]
        [TestCase(true, 12)]
        [TestCase(false, 220)]
        [TestCase(true, 220)]
        public void CompareMissingTextPublishesWhenDestinationStaysAbsent(
            bool simulateWindows,
            int basenameLength
        )
        {
            if (simulateWindows)
            {
                RuntimeInformation.SimulatedPlatform = OSPlatform.Windows;
            }

            _destination = Path.Combine(_directory, new string('s', basenameLength));

            bool completed = DurableFile.TryCompareThenReplaceAllText(
                _destination,
                expectedExists: false,
                expectedContents: string.Empty,
                replacementContents: ReplacementText,
                out bool exchanged,
                out Exception error
            );

            Assert.IsTrue(completed, error?.ToString());
            Assert.IsTrue(exchanged);
            Assert.AreEqual(ReplacementText, RealFile.ReadAllText(_destination));
            CollectionAssert.AreEquivalent(
                new[] { _source, _destination },
                Directory.GetFiles(_directory)
            );
        }

        [Test]
        public void CompareMissingTextReportsUnsupportedPlatformAsPublicationFailure()
        {
            RuntimeInformation.SimulatedPlatform = OSPlatform.Create("Unsupported");

            bool completed = DurableFile.TryCompareThenReplaceAllText(
                _destination,
                expectedExists: false,
                expectedContents: string.Empty,
                replacementContents: ReplacementText,
                out bool exchanged,
                out Exception error
            );

            Assert.IsFalse(completed);
            Assert.IsFalse(exchanged);
            Assert.That(error, Is.TypeOf<PlatformNotSupportedException>());
            CollectionAssert.AreEquivalent(new[] { _source }, Directory.GetFiles(_directory));
        }

        [TestCase(nameof(DurableFile.TryCopy))]
        [TestCase(nameof(DurableFile.CopyAsync))]
        public async Task CopiesRefuseAnExclusivelyOpenSource(string operation)
        {
            RealFile.WriteAllBytes(_destination, OriginalBytes);
            using (
                FileStream writer = new(_source, FileMode.Open, FileAccess.Write, FileShare.None)
            )
            {
                Assert.Throws<IOException>(() =>
                {
                    using FileStream reader = RealFile.OpenRead(_source);
                });
                Exception error = await Execute(operation);
                Assert.That(error, Is.InstanceOf<IOException>());
                CollectionAssert.AreEqual(OriginalBytes, RealFile.ReadAllBytes(_destination));
            }

            Assert.IsNull(await Execute(operation));
            CollectionAssert.AreEqual(ReplacementBytes, RealFile.ReadAllBytes(_destination));
            AssertPublicationCleanup();
        }

        [TestCase(Program.CompareText)]
        [TestCase(Program.CompareBytes)]
        public void CompareThenReplaceRefusesAnotherProcessUntilOwnershipIsReleased(string command)
        {
            RealFile.WriteAllText(_destination, Program.PreviousFileContents);
            string stagePath = _destination + DurableFile.TemporarySuffix;
            string lockPath = stagePath + DurableFile.OwnershipSuffix;
            Assert.IsTrue(
                DurableFile.TryOpenStagingOwnership(
                    stagePath,
                    out FileStream ownership,
                    out Exception error
                ),
                error?.ToString()
            );
            using (ownership)
            {
                (int refused, string message) = RunContender();
                Assert.AreEqual(Program.OwnershipRefused, refused, message);
                Assert.AreEqual(Program.PreviousFileContents, RealFile.ReadAllText(_destination));
                Assert.IsTrue(RealFile.Exists(lockPath));
                Assert.IsFalse(RealFile.Exists(stagePath));
            }

            Assert.IsFalse(RealFile.Exists(lockPath));
            (int accepted, string failure) = RunContender();
            Assert.AreEqual(0, accepted, failure);
            Assert.AreEqual(Program.ReplacementFileContents, RealFile.ReadAllText(_destination));
            Assert.IsFalse(RealFile.Exists(lockPath));
            Assert.IsFalse(RealFile.Exists(stagePath));

            (int, string) RunContender()
            {
                ProcessStartInfo start = new()
                {
                    FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                };
                start.ArgumentList.Add(typeof(Program).Assembly.Location);
                start.ArgumentList.Add(command);
                start.ArgumentList.Add(_destination);
                using Process child = Process.Start(start);
                Assert.IsNotNull(child);
                bool exited = child.WaitForExit(10_000);
                if (!exited)
                {
                    child.Kill(entireProcessTree: true);
                    Assert.IsTrue(child.WaitForExit(5_000));
                }

                Assert.IsTrue(exited, "The contender did not exit.");
                return (child.ExitCode, child.StandardError.ReadToEnd());
            }
        }

        [Test]
        public void SidecarMoveReportsAllRestoreFailuresInReverseOrder()
        {
            string walSource = _source + "-wal";
            string sharedMemorySource = _source + "-shm";
            string journalSource = _source + "-journal";
            string walDestination = _destination + "-wal";
            string sharedMemoryDestination = _destination + "-shm";
            string journalDestination = _destination + "-journal";
            RealFile.WriteAllText(walSource, "original wal");
            RealFile.WriteAllText(sharedMemorySource, "original shared memory");
            RealFile.WriteAllText(journalSource, "original journal");
            IOException firstFailure = new IOException("The final move was denied.");
            List<(string Source, string Destination)> attempts = new();
            void MoveFile(string source, string destination)
            {
                attempts.Add((source, destination));
                if (string.Equals(source, journalSource, StringComparison.Ordinal))
                {
                    RealFile.WriteAllText(walSource, "replacement wal");
                    RealFile.WriteAllText(sharedMemorySource, "replacement shared memory");
                    throw firstFailure;
                }

                RealFile.Move(source, destination);
            }

            Assert.IsFalse(
                DurableFile.TryMoveWithSidecars(
                    _source,
                    _destination,
                    new[] { "-wal", "-shm", "-journal" },
                    MoveFile,
                    out Exception error
                )
            );

            Assert.IsInstanceOf<AggregateException>(error);
            AggregateException aggregate = (AggregateException)error;
            Assert.AreEqual(3, aggregate.InnerExceptions.Count);
            Assert.AreSame(firstFailure, aggregate.InnerExceptions[0]);
            StringAssert.Contains(sharedMemorySource, aggregate.InnerExceptions[1].Message);
            StringAssert.Contains(walSource, aggregate.InnerExceptions[2].Message);
            CollectionAssert.AreEqual(
                new[]
                {
                    (_source, _destination),
                    (walSource, walDestination),
                    (sharedMemorySource, sharedMemoryDestination),
                    (journalSource, journalDestination),
                    (sharedMemoryDestination, sharedMemorySource),
                    (walDestination, walSource),
                    (_destination, _source),
                },
                attempts
            );
            CollectionAssert.AreEqual(ReplacementBytes, RealFile.ReadAllBytes(_source));
            Assert.AreEqual("replacement wal", RealFile.ReadAllText(walSource));
            Assert.AreEqual("original wal", RealFile.ReadAllText(walDestination));
            Assert.AreEqual("replacement shared memory", RealFile.ReadAllText(sharedMemorySource));
            Assert.AreEqual(
                "original shared memory",
                RealFile.ReadAllText(sharedMemoryDestination)
            );
            Assert.AreEqual("original journal", RealFile.ReadAllText(journalSource));
            Assert.IsFalse(RealFile.Exists(_destination));
            Assert.IsFalse(RealFile.Exists(journalDestination));
        }

        [Platform("Linux")]
        [TestCase(false)]
        [TestCase(true)]
        public void RefusesSymbolicWriterLockWithoutTouchingItsTarget(bool targetExists)
        {
            string linkedPath = Path.Combine(_directory, "unrelated");
            if (targetExists)
            {
                RealFile.WriteAllText(linkedPath, "leave this alone");
            }

            string lockPath =
                _destination + DurableFile.TemporarySuffix + DurableFile.OwnershipSuffix;
            RealFile.CreateSymbolicLink(lockPath, linkedPath);
            Assert.IsFalse(
                DurableFile.TryWriteAllText(_destination, "replacement", out Exception error)
            );
            Assert.IsInstanceOf<IOException>(error);
            Assert.IsFalse(RealFile.Exists(_destination));
            Assert.AreEqual(linkedPath, new FileInfo(lockPath).LinkTarget);
            if (targetExists)
            {
                Assert.AreEqual("leave this alone", RealFile.ReadAllText(linkedPath));
            }
            else
            {
                Assert.IsFalse(RealFile.Exists(linkedPath));
            }
        }

        [Test]
        [Platform("Linux")]
        public void SidecarMoveRefusesCrossFilesystemCopyDelete()
        {
            const string sharedMemory = "/dev/shm";
            if (!Directory.Exists(sharedMemory))
            {
                Assert.Ignore("A separate writable filesystem at /dev/shm is required.");
            }

            string sourceMount = FindMount(_source);
            string destinationMount = FindMount(sharedMemory);
            if (
                sourceMount == null
                || destinationMount == null
                || string.Equals(sourceMount, destinationMount, StringComparison.Ordinal)
            )
            {
                Assert.Ignore("The source and destination must use separate mounts.");
            }

            string parent = Path.Combine(sharedMemory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            try
            {
                string destination = Path.Combine(parent, "player.save");
                Assert.IsFalse(
                    DurableFile.TryMoveWithSidecars(
                        _source,
                        destination,
                        Array.Empty<string>(),
                        out Exception error
                    )
                );
                StringAssert.Contains("OS error 18", error.Message);
                CollectionAssert.AreEqual(ReplacementBytes, RealFile.ReadAllBytes(_source));
                Assert.IsEmpty(Directory.GetFiles(parent));
            }
            finally
            {
                Directory.Delete(parent, recursive: true);
            }
        }

        [Test]
        public async Task RelativeCopyKeepsItsOriginalDestinationIfWorkingDirectoryChanges()
        {
            string original = Environment.CurrentDirectory;
            string other = Path.Combine(_directory, "other");
            Directory.CreateDirectory(other);
            string source = Path.GetFileName(_source);
            string destination = Path.GetFileName(_destination);
            try
            {
                Environment.CurrentDirectory = _directory;
                SimulatedFile.BeforeExists = path =>
                {
                    if (
                        string.Equals(Path.GetFileName(path), destination, StringComparison.Ordinal)
                    )
                    {
                        Environment.CurrentDirectory = other;
                    }
                };
                Exception failure = await DurableFile.CopyAsync(source, destination);
                Assert.IsTrue(failure == null, failure?.ToString());
                Assert.AreEqual(other, Environment.CurrentDirectory);
                CollectionAssert.AreEqual(ReplacementBytes, RealFile.ReadAllBytes(_destination));
                Assert.IsEmpty(Directory.GetFiles(other));
            }
            finally
            {
                Environment.CurrentDirectory = original;
            }
        }

        [Test]
        [Platform("Linux")]
        public void LongParentStillAllowsAnExclusiveStageWhenTheLockFits()
        {
            string parent = _directory;
            while (parent.Length + 1 < 4_070)
            {
                int next = Math.Min(200, 4_070 - parent.Length - 1);
                parent = Path.Combine(parent, new string('d', next));
            }
            Directory.CreateDirectory(parent);
            string path = Path.Combine(parent, "save");
            byte[] original = { 1 };
            byte[] replacement = { 2 };
            RealFile.WriteAllBytes(path, original);
            Assert.IsTrue(
                DurableFile.TryWriteAllBytes(path, replacement, out Exception failure),
                failure?.ToString()
            );
            CollectionAssert.AreEqual(replacement, RealFile.ReadAllBytes(path));
            CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(parent));
        }

        [Test]
        [Platform("Linux")]
        public void RejectsANamedPipeWithoutWaitingForAWriter()
        {
            string pipe = Path.Combine(_directory, "named-pipe");
            using Process create = Process.Start(
                new ProcessStartInfo("mkfifo", pipe) { UseShellExecute = false }
            );
            Assert.IsTrue(create.WaitForExit(5_000));
            Assert.AreEqual(0, create.ExitCode);
            ProcessStartInfo start = new()
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                UseShellExecute = false,
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add(Program.RefuseNamedPipe);
            start.ArgumentList.Add(pipe);
            using Process child = Process.Start(start);
            bool exited = child.WaitForExit(5_000);
            if (!exited)
            {
                child.Kill(entireProcessTree: true);
                Assert.IsTrue(child.WaitForExit(5_000));
            }

            Assert.IsTrue(exited, "The operation waited for a writer on the named pipe.");
            Assert.AreEqual(0, child.ExitCode);
        }

        [Test]
        [Platform("Linux")]
        public void ReleasingDirectorySynchronizationTwiceKeepsItClosed()
        {
            using DurableFileSync directory = DurableFileSync.Open(_directory);
            directory.Flush();
            directory.Dispose();
            directory.Dispose();
            Assert.Throws<ObjectDisposedException>(directory.Flush);
        }

        [Platform("Linux")]
        [TestCase(false)]
        [TestCase(true)]
        public void SidecarMoveRejectsSymbolicLinkInParentPath(bool aliasSource)
        {
            string realRoot = Path.Combine(_directory, "real");
            string aliasRoot = Path.Combine(_directory, "alias");
            string realParent = Path.Combine(realRoot, "nested");
            Directory.CreateDirectory(realParent);
            Directory.CreateSymbolicLink(aliasRoot, realRoot);
            string aliasParent = Path.Combine(aliasRoot, "nested");
            string realSource = Path.Combine(realParent, "save.db");
            string realDestination = realSource + "-wal";
            string source = Path.Combine(aliasSource ? aliasParent : realParent, "save.db");
            string destination = Path.Combine(
                aliasSource ? realParent : aliasParent,
                "save.db-wal"
            );
            RealFile.WriteAllBytes(realSource, ReplacementBytes);

            bool moved = DurableFile.TryMoveWithSidecars(
                source,
                destination,
                new[] { "-wal" },
                out Exception error
            );

            bool requestedDestinationExists = RealFile.Exists(realDestination);
            bool movedIntoOwnSidecar = RealFile.Exists(realDestination + "-wal");
            Assert.IsFalse(
                moved,
                $"Destination present: {requestedDestinationExists}. Moved into its own sidecar: {movedIntoOwnSidecar}."
            );
            Assert.IsInstanceOf<IOException>(error);
            StringAssert.Contains(aliasRoot, error.Message);
            Assert.AreEqual(0, SimulatedFile.MoveCalls);
            CollectionAssert.AreEqual(ReplacementBytes, RealFile.ReadAllBytes(realSource));
            Assert.IsFalse(requestedDestinationExists);
            Assert.IsFalse(movedIntoOwnSidecar);
        }

        [TestCaseSource(nameof(UnsupportedCases))]
        public async Task UnsupportedReplacementPreservesPreviousBytes(
            string operation,
            bool platformFailure,
            bool rejectMove
        )
        {
            RealFile.WriteAllBytes(_destination, OriginalBytes);
            Exception failure = UnsupportedFailure(platformFailure);
            SimulatedFile.ReplacementFailure = failure;
            SimulatedFile.RejectMove = rejectMove;

            Exception error = await Execute(operation);

            Assert.Multiple(() =>
            {
                Assert.AreSame(failure, error);
                Assert.IsTrue(RealFile.Exists(_destination), "Previous save must remain present.");
                Assert.AreEqual(0, SimulatedFile.DestinationDeleteCalls);
                Assert.AreEqual(0, SimulatedFile.MoveCalls);
                Assert.AreEqual(1, SimulatedFile.ReplacementCalls);
            });
            CollectionAssert.AreEqual(OriginalBytes, RealFile.ReadAllBytes(_destination));
            AssertPublicationCleanup();
        }

        [TestCaseSource(nameof(SupportedCases))]
        public async Task SupportedPublicationWritesCompleteContents(
            string operation,
            bool existingDestination
        )
        {
            if (existingDestination)
            {
                RealFile.WriteAllBytes(_destination, OriginalBytes);
            }

            Exception error = await Execute(operation);

            Assert.IsTrue(error == null, error?.ToString());
            CollectionAssert.AreEqual(
                ExpectedBytes(operation),
                RealFile.ReadAllBytes(_destination)
            );
            Assert.AreEqual(existingDestination ? 1 : 0, SimulatedFile.ReplacementCalls);
            Assert.AreEqual(existingDestination ? 0 : 1, SimulatedFile.MoveCalls);
            Assert.AreEqual(0, SimulatedFile.DestinationDeleteCalls);
            AssertPublicationCleanup();
            CollectionAssert.AreEquivalent(
                new[] { _source, _destination },
                Directory.GetFiles(_directory)
            );
        }

        [TestCaseSource(nameof(UnsupportedCreationCases))]
        public async Task AbsentDestinationCanBeCreatedWithoutReplacementCapability(
            string operation,
            bool platformFailure
        )
        {
            SimulatedFile.ReplacementFailure = UnsupportedFailure(platformFailure);

            Exception error = await Execute(operation);

            Assert.IsTrue(error == null, error?.ToString());
            CollectionAssert.AreEqual(
                ExpectedBytes(operation),
                RealFile.ReadAllBytes(_destination)
            );
            Assert.AreEqual(0, SimulatedFile.ReplacementCalls);
            Assert.AreEqual(1, SimulatedFile.MoveCalls);
            Assert.AreEqual(0, SimulatedFile.DestinationDeleteCalls);
            AssertPublicationCleanup();
        }

        [TestCaseSource(nameof(RetryCases))]
        public async Task SupportedRetryCanReplaceAfterRejectedPublication(
            string operation,
            bool platformFailure
        )
        {
            RealFile.WriteAllBytes(_destination, OriginalBytes);
            Exception failure = UnsupportedFailure(platformFailure);
            SimulatedFile.ReplacementFailure = failure;
            SimulatedFile.RejectMove = true;

            Exception error = await Execute(operation);

            Assert.AreSame(failure, error);
            CollectionAssert.AreEqual(OriginalBytes, RealFile.ReadAllBytes(_destination));
            AssertPublicationCleanup();
            SimulatedFile.Reset();
            SimulatedFile.ObservedDestination = _destination;

            Exception retryError = await Execute(operation);

            Assert.IsTrue(retryError == null, retryError?.ToString());
            CollectionAssert.AreEqual(
                ExpectedBytes(operation),
                RealFile.ReadAllBytes(_destination)
            );
            Assert.AreEqual(1, SimulatedFile.ReplacementCalls);
            Assert.AreEqual(0, SimulatedFile.DestinationDeleteCalls);
            AssertPublicationCleanup();
        }

        [TestCaseSource(nameof(SharingFailureCases))]
        public async Task ReplacementContentionIsBoundedAndPreservesPublication(
            string operation,
            int errorCode,
            int failures
        )
        {
            RealFile.WriteAllBytes(_destination, OriginalBytes);
            IOException failure = new IOException(
                "Simulated Windows replacement failure.",
                unchecked((int)0x80070000) | errorCode
            );
            SimulatedFile.ReplacementFailure = failure;
            SimulatedFile.ReplacementFailuresRemaining = failures;

            Exception error = await Execute(operation);

            bool retryable = errorCode == 32 || errorCode == 1175;
            bool recovered = retryable && failures < 3;
            if (recovered)
            {
                Assert.IsTrue(error == null, error?.ToString());
            }
            else
            {
                Assert.AreSame(failure, error);
            }
            CollectionAssert.AreEqual(
                recovered ? ExpectedBytes(operation) : OriginalBytes,
                RealFile.ReadAllBytes(_destination)
            );
            Assert.AreEqual(
                retryable ? Math.Min(failures + 1, 3) : 1,
                SimulatedFile.ReplacementCalls
            );
            Assert.AreEqual(0, SimulatedFile.DestinationDeleteCalls);
            Assert.AreEqual(0, SimulatedFile.MoveCalls);
            AssertPublicationCleanup();
        }

        [TestCaseSource(nameof(WindowsPublicationFailureCases))]
        public async Task FailedWindowsReplacementPreservesPreviousBytes(
            string operation,
            int errorCode,
            bool conflictingDestination
        )
        {
            RuntimeInformation.SimulatedPlatform = OSPlatform.Windows;
            RealFile.WriteAllBytes(_destination, OriginalBytes);
            IOException failure = new IOException("Simulated Windows rename refusal.", errorCode);
            SimulatedFile.ReplacementFailure = failure;
            string backup = null;
            SimulatedFile.BeforeFailedReplacement = (destination, backupPath) =>
            {
                backup = backupPath;
                if (errorCode == UnableToMoveReplacementAfterBackup)
                {
                    string movedOriginal = backupPath ?? Path.Combine(_directory, "native-backup");
                    RealFile.Move(destination, movedOriginal);
                }
                else if (backupPath == null)
                {
                    RealFile.Delete(destination);
                }

                if (conflictingDestination)
                {
                    RealFile.WriteAllBytes(destination, ReplacementBytes);
                }
            };

            Exception error = await Execute(operation);

            Assert.Multiple(() =>
            {
                Assert.IsTrue(
                    RealFile.Exists(_destination),
                    "The previous save lost its destination."
                );
                Assert.IsNotNull(backup, "Replacement must request a Windows backup.");
                Assert.AreEqual(1, SimulatedFile.ReplacementCalls);
                Assert.AreEqual(0, SimulatedFile.DestinationDeleteCalls);
                Assert.AreEqual(
                    errorCode == UnableToMoveReplacement ? 0 : 1,
                    SimulatedFile.MoveCalls
                );
                AssertPublicationCleanup();
            });

            if (conflictingDestination)
            {
                Assert.IsInstanceOf<IOException>(error);
                StringAssert.Contains(backup, error.Message);
                Assert.IsInstanceOf<AggregateException>(error.InnerException);
                AggregateException details = (AggregateException)error.InnerException;
                Assert.AreSame(failure, details.InnerExceptions[0]);
                Assert.IsInstanceOf<IOException>(details.InnerExceptions[1]);
                CollectionAssert.AreEqual(ReplacementBytes, RealFile.ReadAllBytes(_destination));
                CollectionAssert.AreEqual(OriginalBytes, RealFile.ReadAllBytes(backup));
                SimulatedFile.Reset();
                Assert.IsNull(await Execute(operation));
                CollectionAssert.AreEqual(OriginalBytes, RealFile.ReadAllBytes(backup));
            }
            else
            {
                Assert.AreSame(failure, error);
                CollectionAssert.AreEqual(OriginalBytes, RealFile.ReadAllBytes(_destination));
                Assert.IsFalse(RealFile.Exists(backup));
            }
        }

        private async ValueTask<Exception> Execute(string operation)
        {
            Exception error;
            bool success;
            switch (operation)
            {
                case nameof(DurableFile.TryWriteAllText):
                    success = DurableFile.TryWriteAllText(_destination, ReplacementText, out error);
                    break;
                case nameof(Encoding.Unicode):
                    success = DurableFile.TryWriteAllText(
                        _destination,
                        ReplacementText,
                        Encoding.Unicode,
                        out error
                    );
                    break;
                case nameof(DurableFile.TryWriteAllBytes):
                    success = DurableFile.TryWriteAllBytes(
                        _destination,
                        ReplacementBytes,
                        out error
                    );
                    break;
                case SliceWriteOperation:
                    byte[] largerBuffer = { 91, 42, 0, 255, 16, 91 };
                    success = DurableFile.TryWriteAllBytes(
                        _destination,
                        largerBuffer,
                        1,
                        ReplacementBytes.Length,
                        out error
                    );
                    break;
                case nameof(DurableFile.TryCopy):
                    success = DurableFile.TryCopy(_source, _destination, out error);
                    break;
                case nameof(DurableFile.WriteAllTextAsync):
                    return await DurableFile.WriteAllTextAsync(_destination, ReplacementText);
                case nameof(DurableFile.WriteAllBytesAsync):
                    return await DurableFile.WriteAllBytesAsync(_destination, ReplacementBytes);
                case nameof(DurableFile.CopyAsync):
                    return await DurableFile.CopyAsync(_source, _destination);
                default:
                    throw new ArgumentException(
                        "Unknown publication operation.",
                        nameof(operation)
                    );
            }

            Assert.AreEqual(error == null, success);
            return error;
        }

        private void AssertPublicationCleanup()
        {
            Assert.IsFalse(RealFile.Exists(_destination + DurableFile.TemporarySuffix));
            Assert.IsFalse(
                RealFile.Exists(
                    _destination + DurableFile.TemporarySuffix + DurableFile.OwnershipSuffix
                )
            );
        }
    }
}
