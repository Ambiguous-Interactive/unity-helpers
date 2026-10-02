// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PathHelperTests : CommonTestBase
    {
        [Test]
        public void SanitizePathWithNullReturnsNull()
        {
            string result = PathHelper.SanitizePath(null);
            Assert.IsTrue(result == null);
        }

        [Test]
        public void SanitizePathWithEmptyStringReturnsEmpty()
        {
            string result = string.Empty.SanitizePath();
            Assert.AreEqual(string.Empty, result);
        }

        [TestCase("", TestName = "PathSanitization.Empty.PreservesInput")]
        [TestCase("   ", TestName = "PathSanitization.Spaces.PreservesInput")]
        [TestCase("\t\r\n", TestName = "PathSanitization.ControlWhitespace.PreservesInput")]
        [TestCase("\u2003", TestName = "PathSanitization.UnicodeWhitespace.PreservesInput")]
        public void SanitizePreservesBlankText(string path)
        {
            Assert.AreSame(path, PathHelper.Sanitize(path));
        }

        [Test]
        public void SanitizePathWithForwardSlashesReturnsUnchanged()
        {
            string path = "Assets/Scripts/Test.cs";
            string result = path.SanitizePath();
            Assert.AreEqual(path, result);
        }

        [Test]
        public void SanitizePathConvertsBackslashesToForwardSlashes()
        {
            string path = "Assets\\Scripts\\Test.cs";
            string result = path.SanitizePath();
            Assert.AreEqual("Assets/Scripts/Test.cs", result);
        }

        [Test]
        public void SanitizePathHandlesMixedSlashes()
        {
            string path = "Assets\\Scripts/Test\\File.cs";
            string result = path.SanitizePath();
            Assert.AreEqual("Assets/Scripts/Test/File.cs", result);
        }

        [Test]
        public void SanitizePathWithMultipleBackslashesConvertsAll()
        {
            string path = "C:\\\\Users\\\\Test\\\\Documents\\\\File.txt";
            string result = path.SanitizePath();
            Assert.That(result, Does.Not.Contain("\\"));
            Assert.That(result, Does.Contain("/"));
        }

        [Test]
        public void SanitizePathWithOnlyBackslashesConvertsAll()
        {
            string path = "\\\\\\\\";
            string result = path.SanitizePath();
            Assert.AreEqual("////", result);
        }

        [Test]
        public void SanitizePathWithTrailingBackslashConverts()
        {
            string path = "Assets\\Scripts\\";
            string result = path.SanitizePath();
            Assert.AreEqual("Assets/Scripts/", result);
        }

        [Test]
        public void SanitizePathWithLeadingBackslashConverts()
        {
            string path = "\\Assets\\Scripts\\Test.cs";
            string result = path.SanitizePath();
            Assert.AreEqual("/Assets/Scripts/Test.cs", result);
        }

        [Test]
        public void SanitizePathWithSingleBackslashConverts()
        {
            string path = "\\";
            string result = path.SanitizePath();
            Assert.AreEqual("/", result);
        }

        [Test]
        public void SanitizePathWithNoSlashesReturnsUnchanged()
        {
            string path = "Test.cs";
            string result = path.SanitizePath();
            Assert.AreEqual(path, result);
        }

        [Test]
        public void SanitizePathWithWindowsDriveLetterConvertsSlashes()
        {
            string path = "C:\\Windows\\System32";
            string result = path.SanitizePath();
            Assert.AreEqual("C:/Windows/System32", result);
        }

        [Test]
        public void SanitizePathWithUncPathConvertsSlashes()
        {
            string path = "\\\\Server\\Share\\Folder\\File.txt";
            string result = path.SanitizePath();
            Assert.AreEqual("//Server/Share/Folder/File.txt", result);
        }

        [Test]
        public void SanitizePathWithSpecialCharactersPreservesThemButConvertsSlashes()
        {
            string path = "Assets\\Test (Copy)\\File [1].cs";
            string result = path.SanitizePath();
            Assert.AreEqual("Assets/Test (Copy)/File [1].cs", result);
        }

        [Test]
        public void SanitizePathWithWhitespacePreservesItButConvertsSlashes()
        {
            string path = "Assets\\Folder With Spaces\\File.cs";
            string result = path.SanitizePath();
            Assert.AreEqual("Assets/Folder With Spaces/File.cs", result);
        }

        [Test]
        public void SanitizePathWithUnicodeCharactersPreservesThemButConvertsSlashes()
        {
            string path = "Assets\\日本語\\ファイル.txt";
            string result = path.SanitizePath();
            Assert.AreEqual("Assets/日本語/ファイル.txt", result);
        }

        [Test]
        public void SanitizePathWithVeryLongPathConvertsAllBackslashes()
        {
            string longPath = "C:\\";
            for (int i = 0; i < 100; ++i)
            {
                longPath += $"Folder{i}\\";
            }
            longPath += "File.txt";

            string result = longPath.SanitizePath();
            Assert.That(result, Does.Not.Contain("\\"));
            Assert.That(result, Does.StartWith("C:/"));
        }

        [Test]
        public void SanitizePathWithRelativePathConvertsBackslashes()
        {
            string path = "..\\..\\Assets\\Test.cs";
            string result = path.SanitizePath();
            Assert.AreEqual("../../Assets/Test.cs", result);
        }

        [Test]
        public void SanitizePathWithCurrentDirectoryReferenceConvertsBackslashes()
        {
            string path = ".\\Assets\\Test.cs";
            string result = path.SanitizePath();
            Assert.AreEqual("./Assets/Test.cs", result);
        }

        [Test]
        public void SanitizePathIdempotent()
        {
            string path = "Assets\\Scripts\\Test.cs";
            string result1 = path.SanitizePath();
            string result2 = result1.SanitizePath();
            Assert.AreEqual(result1, result2);
        }

        [Test]
        public void SanitizePathWithOnlyForwardSlashesReturnsIdentical()
        {
            string path = "Assets/Scripts/Test.cs";
            string result = path.SanitizePath();
            Assert.AreSame(path, result);
        }
    }
}
