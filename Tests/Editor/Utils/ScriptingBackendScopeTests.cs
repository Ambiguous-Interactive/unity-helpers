// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils
{
#if !WALLSTOP_EDITOR_CHECK
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.Build;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class ScriptingBackendScopeTests
    {
        private static readonly NamedBuildTarget Target = NamedBuildTarget.Standalone;

        private ScriptingImplementation _original;

        private static ScriptingImplementation Other(ScriptingImplementation value)
        {
            return value == ScriptingImplementation.Mono2x
                ? ScriptingImplementation.IL2CPP
                : ScriptingImplementation.Mono2x;
        }

        [SetUp]
        public void CaptureBackend()
        {
            _original = PlayerSettings.GetScriptingBackend(Target);
        }

        [TearDown]
        public void RestoreBackend()
        {
            PlayerSettings.SetScriptingBackend(Target, _original);
        }

        [Test]
        public void ScopeAppliesAndRestoresTheBackend()
        {
            ScriptingImplementation changed = Other(_original);

            using (new ScriptingBackendScope(Target, changed))
            {
                Assert.AreEqual(changed, PlayerSettings.GetScriptingBackend(Target));
            }

            Assert.AreEqual(_original, PlayerSettings.GetScriptingBackend(Target));
        }

        [Test]
        public void NestedScopesRestoreInOrder()
        {
            ScriptingImplementation first = Other(_original);

            using (new ScriptingBackendScope(Target, first))
            {
                Assert.AreEqual(first, PlayerSettings.GetScriptingBackend(Target));
                using (new ScriptingBackendScope(Target, _original))
                {
                    Assert.AreEqual(_original, PlayerSettings.GetScriptingBackend(Target));
                }
                Assert.AreEqual(first, PlayerSettings.GetScriptingBackend(Target));
            }

            Assert.AreEqual(_original, PlayerSettings.GetScriptingBackend(Target));
        }

        [Test]
        public void CopiesShareOneRestorationClaim()
        {
            ScriptingBackendScope scope = new(Target, Other(_original));
            ScriptingBackendScope copy = scope;

            copy.Dispose();
            Assert.AreEqual(_original, PlayerSettings.GetScriptingBackend(Target));

            scope.Dispose();
            Assert.AreEqual(_original, PlayerSettings.GetScriptingBackend(Target));
        }

        [Test]
        public void DoubleDisposeIsSafe()
        {
            ScriptingBackendScope scope = new(Target, Other(_original));

            scope.Dispose();
            scope.Dispose();

            Assert.AreEqual(_original, PlayerSettings.GetScriptingBackend(Target));
        }
    }
#endif
}
