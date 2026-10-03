// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class ManualRecompileTests : CommonTestBase
    {
        [Test]
        public void PendingCompilationSkipsTheRequest()
        {
            bool previouslyCompiling = EditorApplication.isCompiling;
            LogAssert.Expect(
                LogType.Log,
                "[Unity Helpers] Script compilation already in progress; manual request skipped."
            );
            Assert.DoesNotThrow(() => ManualRecompile.Request(compilationPending: true));
            Assert.AreEqual(previouslyCompiling, EditorApplication.isCompiling);
        }
    }
}
