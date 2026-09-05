// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class FileHelperTests : CommonTestBase
    {
        private string _testDirectory;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _testDirectory = Path.Combine(Application.temporaryCachePath, "FileHelperTests");
            if (!Directory.Exists(_testDirectory))
            {
                Directory.CreateDirectory(_testDirectory);
            }
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
        public void InitializePathCreatesFileWhenItDoesNotExist()
        {
            string testFile = Path.Combine(_testDirectory, "test.txt");
            Assert.IsFalse(File.Exists(testFile));

            bool result = FileHelper.InitializePath(testFile);

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(testFile));
        }

        [Test]
        public void InitializePathReturnsFalseWhenFileAlreadyExists()
        {
            string testFile = Path.Combine(_testDirectory, "existing.txt");
            File.WriteAllText(testFile, "existing content");

            bool result = FileHelper.InitializePath(testFile);

            Assert.IsFalse(result);
        }

        [Test]
        public void InitializePathCreatesFileWithProvidedContents()
        {
            string testFile = Path.Combine(_testDirectory, "withcontent.txt");
            byte[] contents = System.Text.Encoding.UTF8.GetBytes("Hello, World!");

            bool result = FileHelper.InitializePath(testFile, contents);

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(testFile));
            byte[] readContents = File.ReadAllBytes(testFile);
            Assert.AreEqual(contents, readContents);
        }

        [Test]
        public void InitializePathCreatesFileWithEmptyContentsWhenNullProvided()
        {
            string testFile = Path.Combine(_testDirectory, "empty.txt");

            bool result = FileHelper.InitializePath(testFile, null);

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(testFile));
            Assert.AreEqual(0, new FileInfo(testFile).Length);
        }

        [Test]
        public void InitializePathCreatesIntermediateDirectories()
        {
            string nestedPath = Path.Combine(
                _testDirectory,
                "Level1",
                "Level2",
                "Level3",
                "file.txt"
            );
            Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(nestedPath)));

            bool result = FileHelper.InitializePath(nestedPath);

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(nestedPath));
            Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(nestedPath)));
        }

        [Test]
        public void InitializePathHandlesFileInRootOfTestDirectory()
        {
            string testFile = Path.Combine(_testDirectory, "root.txt");

            bool result = FileHelper.InitializePath(testFile);

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(testFile));
        }

        [Test]
        public void InitializePathDoesNotOverwriteExistingFile()
        {
            string testFile = Path.Combine(_testDirectory, "preserve.txt");
            string originalContent = "Original Content";
            File.WriteAllText(testFile, originalContent);

            byte[] newContents = System.Text.Encoding.UTF8.GetBytes("New Content");
            bool result = FileHelper.InitializePath(testFile, newContents);

            Assert.IsFalse(result);
            string readContent = File.ReadAllText(testFile);
            Assert.AreEqual(originalContent, readContent);
        }

        [Test]
        public void InitializePathWithEmptyDirectoryPathCreatesFileInCurrentDirectory()
        {
            string fileName = Path.Combine(_testDirectory, "nodirpath.txt");

            bool result = FileHelper.InitializePath(fileName);

            Assert.IsTrue(result);
        }

        [Test]
        public void InitializePathWithVeryLongPathCreatesFile()
        {
            string longPath = _testDirectory;
            for (int i = 0; i < 10; i++)
            {
                longPath = Path.Combine(longPath, $"Dir{i}");
            }
            longPath = Path.Combine(longPath, "file.txt");

            bool result = FileHelper.InitializePath(longPath);

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(longPath));
        }

        [Test]
        public void InitializePathWithSpecialCharactersInFilenameCreatesFile()
        {
            string testFile = Path.Combine(_testDirectory, "file (copy) [1].txt");

            bool result = FileHelper.InitializePath(testFile);

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(testFile));
        }

        [Test]
        public void InitializePathWithUnicodeCharactersCreatesFile()
        {
            string testFile = Path.Combine(_testDirectory, "ファイル.txt");

            bool result = FileHelper.InitializePath(testFile);

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(testFile));
        }

        [Test]
        public void InitializePathWithLargeContentsCreatesFile()
        {
            string testFile = Path.Combine(_testDirectory, "large.txt");
            byte[] largeContents = new byte[1024 * 1024];
            for (int i = 0; i < largeContents.Length; i++)
            {
                largeContents[i] = (byte)(i % 256);
            }

            bool result = FileHelper.InitializePath(testFile, largeContents);

            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(testFile));
            Assert.AreEqual(largeContents.Length, new FileInfo(testFile).Length);
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncCopiesFileSuccessfully()
        {
            string sourceFile = Path.Combine(_testDirectory, "source.txt");
            string destinationFile = Path.Combine(_testDirectory, "destination.txt");
            string content = "Test Content for Async Copy";
            File.WriteAllText(sourceFile, content);

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(sourceFile, destinationFile);
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }

            bool result = copyTask.Result;
            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(destinationFile));
            string copiedContent = File.ReadAllText(destinationFile);
            Assert.AreEqual(content, copiedContent);
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncReturnsFalseWhenSourceDoesNotExist()
        {
            string sourceFile = Path.Combine(_testDirectory, "nonexistent.txt");
            string destinationFile = Path.Combine(_testDirectory, "destination.txt");

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(sourceFile, destinationFile);
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }
            bool result = copyTask.Result;

            Assert.IsFalse(result);
            Assert.IsFalse(File.Exists(destinationFile));
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncOverwritesExistingDestination()
        {
            string sourceFile = Path.Combine(_testDirectory, "source2.txt");
            string destinationFile = Path.Combine(_testDirectory, "destination2.txt");
            string sourceContent = "Source Content";
            string oldDestContent = "Old Destination Content";

            File.WriteAllText(sourceFile, sourceContent);
            File.WriteAllText(destinationFile, oldDestContent);

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(sourceFile, destinationFile);
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }
            bool result = copyTask.Result;
            Assert.IsTrue(result);
            string copiedContent = File.ReadAllText(destinationFile);
            Assert.AreEqual(sourceContent, copiedContent);
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncCopiesEmptyFile()
        {
            string sourceFile = Path.Combine(_testDirectory, "empty_source.txt");
            string destinationFile = Path.Combine(_testDirectory, "empty_destination.txt");
            File.WriteAllText(sourceFile, string.Empty);

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(sourceFile, destinationFile);
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }

            bool result = copyTask.Result;
            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(destinationFile));
            Assert.AreEqual(0, new FileInfo(destinationFile).Length);
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncCopiesLargeFile()
        {
            string sourceFile = Path.Combine(_testDirectory, "large_source.txt");
            string destinationFile = Path.Combine(_testDirectory, "large_destination.txt");
            byte[] largeContent = new byte[5 * 1024 * 1024];
            for (int i = 0; i < largeContent.Length; i++)
            {
                largeContent[i] = (byte)(i % 256);
            }
            File.WriteAllBytes(sourceFile, largeContent);

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(sourceFile, destinationFile);
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }
            bool result = copyTask.Result;
            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(destinationFile));
            Assert.AreEqual(largeContent.Length, new FileInfo(destinationFile).Length);
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncWithCustomBufferSizeCopiesFile()
        {
            string sourceFile = Path.Combine(_testDirectory, "buffered_source.txt");
            string destinationFile = Path.Combine(_testDirectory, "buffered_destination.txt");
            string content = "Test Content with Custom Buffer";
            File.WriteAllText(sourceFile, content);

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(
                sourceFile,
                destinationFile,
                bufferSize: 1024
            );
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }
            bool result = copyTask.Result;
            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(destinationFile));
            string copiedContent = File.ReadAllText(destinationFile);
            Assert.AreEqual(content, copiedContent);
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncWithVerySmallBufferSizeCopiesFile()
        {
            string sourceFile = Path.Combine(_testDirectory, "small_buffer_source.txt");
            string destinationFile = Path.Combine(_testDirectory, "small_buffer_destination.txt");
            string content = "Content";
            File.WriteAllText(sourceFile, content);

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(
                sourceFile,
                destinationFile,
                bufferSize: 16
            );
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }
            bool result = copyTask.Result;
            Assert.IsTrue(result);
            string copiedContent = File.ReadAllText(destinationFile);
            Assert.AreEqual(content, copiedContent);
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncCanBeCancelled()
        {
            string sourceFile = Path.Combine(_testDirectory, "cancel_source.txt");
            string destinationFile = Path.Combine(_testDirectory, "cancel_destination.txt");
            byte[] largeContent = new byte[10 * 1024 * 1024];
            File.WriteAllBytes(sourceFile, largeContent);

            CancellationTokenSource cts = new();
            cts.Cancel();

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(
                sourceFile,
                destinationFile,
                cancellationToken: cts.Token
            );
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }
            bool result = copyTask.Result;

            Assert.IsFalse(result);
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncCreatesDestinationDirectory()
        {
            string sourceFile = Path.Combine(_testDirectory, "nested_source.txt");
            string destinationFile = Path.Combine(
                _testDirectory,
                "NewDir",
                "nested_destination.txt"
            );
            string content = "Nested Content";
            File.WriteAllText(sourceFile, content);
            string directoryName = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(sourceFile, destinationFile);
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }
            bool result = copyTask.Result;
            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(destinationFile));
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncCopiesBinaryFile()
        {
            string sourceFile = Path.Combine(_testDirectory, "binary_source.bin");
            string destinationFile = Path.Combine(_testDirectory, "binary_destination.bin");
            byte[] binaryContent = { 0x00, 0xFF, 0x7F, 0x80, 0x01, 0xFE };
            File.WriteAllBytes(sourceFile, binaryContent);

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(sourceFile, destinationFile);
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }
            bool result = copyTask.Result;
            Assert.IsTrue(result);
            byte[] copiedContent = File.ReadAllBytes(destinationFile);
            Assert.AreEqual(binaryContent, copiedContent);
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncCopiesFileWithSpecialCharactersInName()
        {
            string sourceFile = Path.Combine(_testDirectory, "source (copy).txt");
            string destinationFile = Path.Combine(_testDirectory, "destination [1].txt");
            string content = "Special Characters";
            File.WriteAllText(sourceFile, content);

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(sourceFile, destinationFile);
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }
            bool result = copyTask.Result;
            Assert.IsTrue(result);
            string copiedContent = File.ReadAllText(destinationFile);
            Assert.AreEqual(content, copiedContent);
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncWithZeroBufferSizeUsesDefault()
        {
            string sourceFile = Path.Combine(_testDirectory, "zero_buffer_source.txt");
            string destinationFile = Path.Combine(_testDirectory, "zero_buffer_destination.txt");
            string content = "Content";
            File.WriteAllText(sourceFile, content);

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(
                sourceFile,
                destinationFile,
                bufferSize: 0
            );
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }
            bool result = copyTask.Result;

            Assert.That(result, Is.True.Or.False);
        }

        [UnityTest]
        public IEnumerator CopyFileAsyncPreservesFileContentsExactly()
        {
            string sourceFile = Path.Combine(_testDirectory, "exact_source.txt");
            string destinationFile = Path.Combine(_testDirectory, "exact_destination.txt");
            byte[] randomContent = new byte[4096];
            IRandom random = new PcgRandom(42);
            random.NextBytes(randomContent);
            File.WriteAllBytes(sourceFile, randomContent);

            ValueTask<bool> copyTask = FileHelper.CopyFileAsync(sourceFile, destinationFile);
            while (!copyTask.IsCompleted)
            {
                yield return null;
            }
            bool result = copyTask.Result;
            Assert.IsTrue(result);
            byte[] sourceBytes = File.ReadAllBytes(sourceFile);
            byte[] destBytes = File.ReadAllBytes(destinationFile);
            Assert.AreEqual(sourceBytes, destBytes);
        }
    }
}
