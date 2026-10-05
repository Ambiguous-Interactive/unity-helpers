// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.DurableFile.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using RealFile = System.IO.File;
    using SimulatedFile = WallstopStudios.UnityHelpers.Core.Helper.File;

    /// <summary>Checks source-linked publication with explicitly simulated capability failures.</summary>
    /// <remarks>Only DurableFile, SemaphoreLease, and DisposalLease are linked unchanged; the integer math dependency is mirrored to avoid Unity dependencies. This is not Unity platform qualification.</remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class DurableFileReplacementTests
    {
        private const string ReplacementText = "replacement player data";

        private static readonly string[] Operations =
        {
            nameof(DurableFile.TryWriteAllText),
            nameof(Encoding.Unicode),
            nameof(DurableFile.TryWriteAllBytes),
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

        private static Exception UnsupportedFailure(bool platformFailure)
        {
            return platformFailure
                ? new PlatformNotSupportedException("Simulated unsupported atomic replacement.")
                : new NotSupportedException("Simulated unsupported atomic replacement.");
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

        [SetUp]
        public void SetUp()
        {
            SimulatedFile.Reset();
            _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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
            Directory.Delete(_directory, recursive: true);
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
