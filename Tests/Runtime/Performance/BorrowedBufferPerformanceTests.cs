// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tests.Extensions;

    [TestFixture]
    [Category("Performance")]
    public sealed class BorrowedBufferPerformanceTests
    {
        [TestCase("ascii", 1)]
        [TestCase("ascii", 2)]
        [TestCase("ascii", 3)]
        [TestCase("ascii", 8192)]
        [TestCase("multibyte", 1)]
        [TestCase("multibyte", 8192)]
        [TestCase("invalidUtf8", 1)]
        [TestCase("invalidUtf8", 8192)]
        [TestCase("invalidPadding", 1)]
        [TestCase("invalidAlphabet", 1)]
        [TestCase("empty", 0)]
        [TestCase("null", 0)]
        [Timeout(600_000)]
        public void Base64WholeCallComparedWithBorrowedBuffer(string shape, int length)
        {
            new BorrowedBase64Tests().CompareWholeCall(shape, length);
        }
    }
}
