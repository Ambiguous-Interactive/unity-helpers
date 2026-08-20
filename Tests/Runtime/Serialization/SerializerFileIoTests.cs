// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializerFileIoTests : CommonTestBase
    {
        private string _dir;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _dir = Path.Combine(Application.persistentDataPath, "SerializerFileIoTests");
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public override void TearDown()
        {
            try
            {
                if (Directory.Exists(_dir))
                {
                    Directory.Delete(_dir, recursive: true);
                }
            }
            catch { }
            base.TearDown();
        }

        private sealed class Sample
        {
            public int a;
            public string b;
        }

        /// <summary>
        /// Makes the allocation test's same-thread completion contract explicit and can cancel
        /// precisely after EOF, after at least one chunk has already been consumed.
        /// </summary>
        private sealed class InlineReadStream : MemoryStream
        {
            private readonly CancellationTokenSource _cancelWhenExhausted;

            public InlineReadStream(
                byte[] buffer,
                CancellationTokenSource cancelWhenExhausted = null
            )
                : base(buffer, writable: false)
            {
                _cancelWhenExhausted = cancelWhenExhausted;
            }

            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = Read(buffer, offset, count);
                if (read == 0)
                {
                    _cancelWhenExhausted?.Cancel();
                }
                return Task.FromResult(read);
            }
        }

        [Test]
        public void TryWriteAndTryReadRoundTrip()
        {
            string path = Path.Combine(_dir, "sample.json");
            Sample s = new() { a = 7, b = "test" };

            bool wrote = Serializer.TryWriteToJsonFile(s, path, pretty: true);
            Assert.IsTrue(wrote, "Expected TryWriteToJsonFile to succeed.");
            Assert.IsTrue(File.Exists(path), "Expected file to be created.");

            bool read = Serializer.TryReadFromJsonFile(path, out Sample loaded);
            Assert.IsTrue(read, "Expected TryReadFromJsonFile to succeed.");
            Assert.NotNull(loaded);
            Assert.AreEqual(7, loaded.a);
            Assert.AreEqual("test", loaded.b);
        }

        [Test]
        public void TryReadReturnsFalseWhenMissing()
        {
            string path = Path.Combine(_dir, "does_not_exist.json");
            bool read = Serializer.TryReadFromJsonFile(path, out Sample loaded);
            Assert.IsFalse(read);
            Assert.IsTrue(loaded == null, "Loaded object should be null when file is missing");
        }

        [Test]
        public void ReadAsyncHonorsCancellation()
        {
            string path = Path.Combine(_dir, "big.json");
            // Create a moderately large file
            File.WriteAllText(path, new string('x', 200_000));

            using CancellationTokenSource cts = new();
            cts.Cancel();
            Assert.Throws<TaskCanceledException>(() =>
                Serializer
                    .ReadFromJsonFileAsync<Sample>(path, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult()
            );
        }

        [UnityTest]
        public IEnumerator CancellationAwareReadHandlesPayloadLargerThanScratchBuffer()
        {
            string path = Path.Combine(_dir, "large-sample.json");
            string expected = new('x', 20_000);
            Serializer.WriteToJsonFile(new Sample { a = 17, b = expected }, path, pretty: false);

            Task<Sample> readerTask = Serializer.ReadFromJsonFileAsync<Sample>(
                path,
                CancellationToken.None
            );
            while (!readerTask.IsCompleted)
            {
                yield return null;
            }

            Sample loaded = readerTask.Result;

            Assert.NotNull(loaded);
            Assert.AreEqual(17, loaded.a);
            Assert.AreEqual(expected, loaded.b);
        }

        [Test]
        public void CancellationAwareReadDoesNotAllocatePayloadSizedCopy()
        {
            const int whitespaceBytes = 256 * 1024;
            const int measuredIterations = 3;
            byte[] compactJson = Encoding.UTF8.GetBytes("{\"a\":17}");
            byte[] paddedJson = new byte[whitespaceBytes + compactJson.Length];
            for (int index = 0; index < whitespaceBytes; index++)
            {
                paddedJson[index] = (byte)' ';
            }
            Buffer.BlockCopy(compactJson, 0, paddedJson, whitespaceBytes, compactJson.Length);
            using InlineReadStream compactStream = new(compactJson);
            using InlineReadStream paddedStream = new(paddedJson);
            Sample result = null;

            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            long calibrationBefore = GC.GetAllocatedBytesForCurrentThread();
            byte[] forcedPayloadSizedAllocation = new byte[whitespaceBytes];
            long calibrationBytes = GC.GetAllocatedBytesForCurrentThread() - calibrationBefore;
            GC.KeepAlive(forcedPayloadSizedAllocation);
            if (calibrationBytes < whitespaceBytes)
            {
                Assert.Inconclusive(
                    $"The runtime reported only {calibrationBytes} bytes for a known "
                        + $"{whitespaceBytes}-byte allocation, so its per-thread allocation "
                        + "counter cannot prove that the JSON read avoided a payload-sized copy."
                );
            }

            long compactAllocation = GCAssert.MeasureAllocatedBytes(
                () =>
                {
                    compactStream.Position = 0;
                    result = Serializer
                        .ReadJsonStreamAsync<Sample>(compactStream, CancellationToken.None)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                },
                measuredIterations: measuredIterations
            );
            long paddedAllocation = GCAssert.MeasureAllocatedBytes(
                () =>
                {
                    paddedStream.Position = 0;
                    result = Serializer
                        .ReadJsonStreamAsync<Sample>(paddedStream, CancellationToken.None)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                },
                measuredIterations: measuredIterations
            );

            Assert.NotNull(result);
            Assert.AreEqual(17, result.a);
            Assert.LessOrEqual(
                paddedAllocation,
                compactAllocation + 64 * 1024,
                $"A {paddedJson.Length}-byte JSON document allocated {paddedAllocation} bytes "
                    + $"versus {compactAllocation} for the same compact value. The file-read path "
                    + "must not materialize an exact payload-sized copy before decoding."
            );
        }

        [Test]
        public void CancellationAfterPartialReadBeforeDecodeIsObserved()
        {
            byte[] compactJson = Encoding.UTF8.GetBytes("{\"a\":17}");
            byte[] paddedJson = new byte[20_000 + compactJson.Length];
            for (int index = 0; index < 20_000; index++)
            {
                paddedJson[index] = (byte)' ';
            }
            Buffer.BlockCopy(compactJson, 0, paddedJson, 20_000, compactJson.Length);
            using CancellationTokenSource cts = new();
            using InlineReadStream stream = new(paddedJson, cts);

            Assert.Throws<OperationCanceledException>(() =>
                Serializer
                    .ReadJsonStreamAsync<Sample>(stream, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult()
            );
        }

        [UnityTest]
        public IEnumerator MalformedLargeReadWrapsFailureAndAllowsSubsequentRead()
        {
            string path = Path.Combine(_dir, "malformed-large.json");
            File.WriteAllText(path, new string(' ', 20_000) + "{\"a\":");

            Task<Sample> malformedTask = Serializer.ReadFromJsonFileAsync<Sample>(
                path,
                CancellationToken.None
            );
            while (!malformedTask.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(malformedTask.IsFaulted);
            Assert.IsInstanceOf<SerializationCorruptDataException>(
                malformedTask.Exception?.GetBaseException()
            );

            Serializer.WriteToJsonFile(new Sample { a = 23, b = "after-failure" }, path);
            Task<Sample> validTask = Serializer.ReadFromJsonFileAsync<Sample>(
                path,
                CancellationToken.None
            );
            while (!validTask.IsCompleted)
            {
                yield return null;
            }

            Sample loaded = validTask.Result;

            Assert.NotNull(loaded);
            Assert.AreEqual(23, loaded.a);
            Assert.AreEqual("after-failure", loaded.b);
        }

        [Test]
        public void WriteAsyncHonorsCancellation()
        {
            string path = Path.Combine(_dir, "out.json");
            using CancellationTokenSource cts = new();
            cts.Cancel();
            Assert.Throws<TaskCanceledException>(() =>
                Serializer
                    .WriteToJsonFileAsync(new Sample { a = 1, b = "x" }, path, true, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult()
            );
        }
    }
}
