// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using NUnit.Framework;

    [TestFixture]
    public sealed class WriterTests
    {
        [TestCase("", "\n")]
        [TestCase("alpha", "    alpha\n")]
        [TestCase("alpha\nbeta", "    alpha\n    beta\n")]
        [TestCase("alpha\n{", "    alpha\n    {\n")]
        [TestCase("\nalpha", "\n    alpha\n")]
        [TestCase("alpha\n", "    alpha\n\n")]
        [TestCase("alpha\n\nbeta", "    alpha\n\n    beta\n")]
        [TestCase("alpha\r\nbeta", "    alpha\r\n    beta\n")]
        public void LinePreservesSegmentAndLfSemantics(string input, string expected)
        {
            Writer writer = new Writer();
            writer.Indent();

            writer.Line(input);

            Assert.AreEqual(expected, writer.ToString());
        }
    }
}
