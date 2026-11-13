// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
#if !SINGLE_THREADED
    using System.Threading;
    using System.Threading.Tasks;
#endif

    [TestFixture]
    [Category("Integration")]
    public sealed class LatestWriteFileTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "latest-writes-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_directory, true);
        }

        [Test]
        public void ManualBatchKeepsOnlyTheLatestNormalizedPathPayload()
        {
            string path = Path.Combine(_directory, "state.txt");
            string alias = Path.Combine(_directory, "unused", "..", "state.txt");
            using LatestWriteFile writer = new(writeInBackground: false);
            Assert.IsTrue(writer.TryEnqueueText(path, "first", out Exception firstError));
            Assert.IsTrue(firstError == null);
            Assert.IsTrue(writer.TryEnqueueText(alias, "second", out Exception secondError));
            Assert.IsTrue(secondError == null);
            Assert.IsTrue(writer.TryEnqueueText(path, "last", out Exception lastError));
            Assert.IsTrue(lastError == null);
            Assert.That(writer.PendingPathCount, Is.EqualTo(1));
            Assert.IsFalse(File.Exists(path));
            Assert.IsTrue(writer.Flush(out Exception error));
            Assert.IsTrue(error == null);
            Assert.That(File.ReadAllText(path), Is.EqualTo("last"));
            Assert.That(writer.PendingPathCount, Is.Zero);
            Assert.IsTrue(writer.Failure == null);
        }

        [Test]
        public void CaseAliasesFollowTheHostPathComparison()
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            bool ignoreCase = isWindows || isMac;
            string lower = Path.Combine(_directory, "case.txt");
            string upper = Path.Combine(_directory, "CASE.txt");
            using LatestWriteFile writer = new(writeInBackground: false);
            Assert.IsTrue(writer.TryEnqueueText(lower, "old", out Exception error));
            Assert.IsTrue(error == null);
            Assert.IsTrue(writer.TryEnqueueText(upper, "new", out error));
            Assert.IsTrue(error == null);
            Assert.That(writer.PendingPathCount, Is.EqualTo(ignoreCase ? 1 : 2));
            Assert.IsTrue(writer.Flush(out error));
            Assert.IsTrue(error == null);
            Assert.That(File.ReadAllText(lower), Is.EqualTo(ignoreCase ? "new" : "old"));
            if (!ignoreCase)
            {
                Assert.That(File.ReadAllText(upper), Is.EqualTo("new"));
            }
        }

        [Test]
        public void CaseAliasRecoveryClearsOnlyTheMatchingDestinationFailure()
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            bool ignoreCase = isWindows || isMac;
            string lower = Path.Combine(_directory, "recovery.txt");
            string upper = Path.Combine(_directory, "RECOVERY.txt");
            Directory.CreateDirectory(lower);
            using LatestWriteFile writer = new(writeInBackground: false);
            Assert.IsTrue(writer.TryEnqueueText(lower, "blocked", out Exception error));
            Assert.IsTrue(error == null);
            Assert.IsFalse(writer.Flush(out error));
            Assert.IsTrue(error != null);
            Directory.Delete(lower);
            Assert.IsTrue(writer.TryEnqueueText(upper, "recovered", out error));
            Assert.IsTrue(error == null);
            Assert.That(writer.Flush(out error), Is.EqualTo(ignoreCase));
            Assert.That(writer.Failure == null, Is.EqualTo(ignoreCase));
            Assert.That(File.ReadAllText(upper), Is.EqualTo("recovered"));
            if (!ignoreCase)
            {
                Assert.IsTrue(writer.TryEnqueueText(lower, "separate", out error));
                Assert.IsTrue(error == null);
                Assert.IsTrue(writer.Flush(out error));
                Assert.IsTrue(error == null);
                Assert.IsTrue(writer.Failure == null);
                Assert.That(File.ReadAllText(upper), Is.EqualTo("recovered"));
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("é漢字\r\nlast\n")]
        public void TextPreservesWhitespaceAndUtf8WithoutABom(string contents)
        {
            string path = Path.Combine(_directory, "text.txt");
            using LatestWriteFile writer = new(writeInBackground: false);
            Assert.IsTrue(writer.TryEnqueueText(path, contents, out Exception error));
            Assert.IsTrue(error == null);
            Assert.IsTrue(writer.Flush(out error));
            Assert.IsTrue(error == null);
            byte[] expected = Encoding.UTF8.GetBytes(contents ?? string.Empty);
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(path));
        }

        [Test]
        public void ByteSubmissionOwnsItsCopyAndCanReplaceText()
        {
            string path = Path.Combine(_directory, "bytes.bin");
            byte[] payload = { 0, 255, 3, 0 };
            using LatestWriteFile writer = new(writeInBackground: false);
            Assert.IsTrue(writer.TryEnqueueText(path, "discarded", out Exception textError));
            Assert.IsTrue(textError == null);
            Assert.IsTrue(writer.TryEnqueueBytes(path, payload, out Exception bytesError));
            Assert.IsTrue(bytesError == null);
            payload[0] = 42;
            Assert.That(writer.PendingPathCount, Is.EqualTo(1));
            Assert.IsTrue(writer.Flush(out Exception error));
            Assert.IsTrue(error == null);
            CollectionAssert.AreEqual(new byte[] { 0, 255, 3, 0 }, File.ReadAllBytes(path));
            Assert.IsTrue(writer.TryEnqueueBytes(path, null, out error));
            Assert.IsTrue(error == null);
            Assert.IsTrue(writer.Flush(out error));
            Assert.IsTrue(error == null);
            Assert.That(File.ReadAllBytes(path), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisposalFinishesEveryAcceptedPathAndRejectsLaterSubmissions(bool background)
        {
            LatestWriteFile writer = new(writeInBackground: background);
            string first = Path.Combine(_directory, "one.txt");
            string second = Path.Combine(_directory, "nested", "two.txt");
            try
            {
                Assert.IsTrue(writer.TryEnqueueText(first, "old", out Exception error));
                Assert.IsTrue(error == null);
                Assert.IsTrue(writer.TryEnqueueText(second, "other", out error));
                Assert.IsTrue(error == null);
                Assert.IsTrue(writer.TryEnqueueText(first, "new", out error));
                Assert.IsTrue(error == null);
                writer.Dispose();
                Assert.That(File.ReadAllText(first), Is.EqualTo("new"));
                Assert.That(File.ReadAllText(second), Is.EqualTo("other"));
                Assert.That(writer.PendingPathCount, Is.Zero);
                Assert.IsFalse(writer.TryEnqueueText(first, "late", out error));
                Assert.That(error, Is.InstanceOf<ObjectDisposedException>());
                Assert.IsFalse(writer.TryEnqueueBytes(second, new byte[] { 1 }, out error));
                Assert.That(error, Is.InstanceOf<ObjectDisposedException>());
                Assert.IsTrue(writer.Flush(out error));
                Assert.IsTrue(error == null);
            }
            finally
            {
                writer.Dispose();
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        [TestCase("bad\0path")]
        public void InvalidPathsAreRejectedWithoutChangingThePendingBatch(string path)
        {
            using LatestWriteFile writer = new(writeInBackground: false);
            Assert.IsFalse(writer.TryEnqueueText(path, "text", out Exception textError));
            Assert.IsTrue(textError != null);
            Assert.IsFalse(
                writer.TryEnqueueBytes(path, new byte[] { 1 }, out Exception bytesError)
            );
            Assert.IsTrue(bytesError != null);
            Assert.That(writer.PendingPathCount, Is.Zero);
            Assert.IsTrue(writer.Flush(out Exception flushError));
            Assert.IsTrue(flushError == null);
        }

        [Test]
        public void FailedPathStaysObservableUntilThatPathSucceeds()
        {
            string blocked = Path.Combine(_directory, "blocked");
            Directory.CreateDirectory(blocked);
            List<string> failedPaths = new();
            List<Exception> failures = new();
            using LatestWriteFile writer = new(
                writeInBackground: false,
                onFailure: (path, failure) =>
                {
                    failedPaths.Add(path);
                    failures.Add(failure);
                }
            );
            Assert.IsTrue(writer.TryEnqueueText(blocked, "first", out Exception error));
            Assert.IsTrue(error == null);
            Assert.IsFalse(writer.Flush(out error));
            Assert.That(error, Is.SameAs(writer.Failure));
            Assert.That(failedPaths, Is.EqualTo(new[] { blocked }));
            Assert.That(failures, Is.EqualTo(new[] { error }));
            string other = Path.Combine(_directory, "other.txt");
            Assert.IsTrue(writer.TryEnqueueText(other, "other", out Exception acceptedError));
            Assert.IsTrue(acceptedError == null);
            Assert.IsFalse(writer.Flush(out Exception retainedError));
            Assert.That(retainedError, Is.SameAs(error));
            Assert.That(File.ReadAllText(other), Is.EqualTo("other"));
            Directory.Delete(blocked);
            Assert.IsTrue(writer.TryEnqueueText(blocked, "recovered", out acceptedError));
            Assert.IsTrue(acceptedError == null);
            Assert.IsTrue(writer.Flush(out Exception recoveredError));
            Assert.IsTrue(recoveredError == null);
            Assert.IsTrue(writer.Failure == null);
            Assert.That(File.ReadAllText(blocked), Is.EqualTo("recovered"));
        }

        [Test]
        public void ThrowingFailureCallbackDoesNotDiscardOtherPaths()
        {
            string blocked = Path.Combine(_directory, "blocked");
            string valid = Path.Combine(_directory, "valid.txt");
            Directory.CreateDirectory(blocked);
            Exception callbackError = new InvalidOperationException(
                nameof(ThrowingFailureCallbackDoesNotDiscardOtherPaths)
            );
            using LatestWriteFile writer = new(
                writeInBackground: false,
                onFailure: (_, _) => throw callbackError
            );
            Assert.IsTrue(writer.TryEnqueueText(blocked, "bad", out Exception firstError));
            Assert.IsTrue(firstError == null);
            Assert.IsTrue(writer.TryEnqueueText(valid, "good", out Exception secondError));
            Assert.IsTrue(secondError == null);
            Assert.IsFalse(writer.Flush(out Exception error));
            Assert.That(error, Is.InstanceOf<AggregateException>());
            AggregateException combined = (AggregateException)error;
            Assert.That(combined.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(combined.InnerExceptions[1], Is.SameAs(callbackError));
            Assert.That(File.ReadAllText(valid), Is.EqualTo("good"));
        }

        [Test]
        public void FailureCallbackCanRecoverAndFlushReentrantly()
        {
            string path = Path.Combine(_directory, "blocked");
            Directory.CreateDirectory(path);
            LatestWriteFile writer = null;
            int callbacks = 0;
            bool accepted = false;
            bool recovered = false;
            writer = new LatestWriteFile(
                writeInBackground: false,
                onFailure: (failedPath, _) =>
                {
                    if (++callbacks != 1)
                    {
                        return;
                    }
                    Directory.Delete(failedPath);
                    accepted = writer.TryEnqueueText(failedPath, "recovered", out _);
                    recovered = writer.Flush(out _);
                }
            );
            using (writer)
            {
                Assert.IsTrue(writer.TryEnqueueText(path, "first", out Exception error));
                Assert.IsTrue(error == null);
                Assert.IsTrue(writer.Flush(out error));
                Assert.IsTrue(error == null);
                Assert.IsTrue(accepted);
                Assert.IsTrue(recovered);
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(File.ReadAllText(path), Is.EqualTo("recovered"));
            }
        }

#if !SINGLE_THREADED
        [Test]
        public void BackgroundFailureCallbackDoesNotHoldTheWriteLockOrResurrectAnOlderPayload()
        {
            string path = Path.Combine(_directory, "blocked");
            Directory.CreateDirectory(path);
            using ManualResetEventSlim callbackStarted = new();
            using ManualResetEventSlim releaseCallback = new();
            using LatestWriteFile writer = new(
                onFailure: (_, _) =>
                {
                    callbackStarted.Set();
                    releaseCallback.Wait(TimeSpan.FromSeconds(10));
                }
            );
            Task<bool> flush = null;
            try
            {
                Assert.IsTrue(writer.TryEnqueueText(path, "old", out Exception error));
                Assert.IsTrue(error == null);
                Assert.IsTrue(callbackStarted.Wait(TimeSpan.FromSeconds(10)));
                Directory.Delete(path);
                Assert.IsTrue(writer.TryEnqueueText(path, "new", out error));
                Assert.IsTrue(error == null);
                flush = Task.Run(() => writer.Flush(out _));
                Assert.IsTrue(flush.Wait(TimeSpan.FromSeconds(5)));
                Assert.IsTrue(flush.Result);
                Assert.That(File.ReadAllText(path), Is.EqualTo("new"));
            }
            finally
            {
                releaseCallback.Set();
                if (flush != null)
                {
                    Assert.IsTrue(flush.Wait(TimeSpan.FromSeconds(10)));
                }
            }
        }
#endif
    }
}
