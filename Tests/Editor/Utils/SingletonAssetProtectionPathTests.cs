// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class SingletonAssetProtectionPathTests
    {
        private static IEnumerable<ProtectedPathTestCase> ProtectedPathTestCases()
        {
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios",
                true,
                false,
                "Main Wallstop Studios folder should be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios/Unity Helpers",
                true,
                false,
                "Main Unity Helpers folder should be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios/Unity Helpers/SomeAsset.asset",
                true,
                false,
                "Assets inside Unity Helpers should be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios 1",
                false,
                true,
                "Wallstop Studios 1 is a duplicate and should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios 2",
                false,
                true,
                "Wallstop Studios 2 is a duplicate and should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios/Unity Helpers 1",
                false,
                true,
                "Unity Helpers 1 is a duplicate and should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios/Unity Helpers 15",
                false,
                true,
                "Unity Helpers 15 is a duplicate and should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios/Unity Helpers 1/SomeAsset.asset",
                false,
                true,
                "Assets inside duplicate Unity Helpers should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios/Unity Helpers 99/Nested/File.asset",
                false,
                true,
                "Nested assets inside duplicate Unity Helpers should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Plugins",
                true,
                false,
                "Plugins folder should be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Plugins/SomePlugin/Code.cs",
                true,
                false,
                "Assets inside Plugins should be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/TempTestFolder",
                false,
                false,
                "Temp test folders should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Tests",
                false,
                false,
                "Test folders in Resources should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "",
                false,
                false,
                "Empty path should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                null,
                false,
                false,
                "Null path should NOT be protected"
            );
        }

        [Test]
        public void IsProtectedPathReturnsExpectedValue(
            [ValueSource(nameof(ProtectedPathTestCases))] ProtectedPathTestCase testCase
        )
        {
            bool actual = CommonTestBase.ProtectionTestHooks.TestIsProtectedPath(testCase.Path);
            Assert.That(
                actual,
                Is.EqualTo(testCase.ExpectedIsProtected),
                $"IsProtectedPath(\"{testCase.Path ?? "(null)"}\") returned {actual}, expected {testCase.ExpectedIsProtected}. "
                    + $"Description: {testCase.Description}"
            );
        }

        [Test]
        public void IsKnownDuplicatePollutionReturnsExpectedValue(
            [ValueSource(nameof(ProtectedPathTestCases))] ProtectedPathTestCase testCase
        )
        {
            bool actual = CommonTestBase.ProtectionTestHooks.TestIsKnownDuplicatePollution(
                testCase.Path
            );
            Assert.That(
                actual,
                Is.EqualTo(testCase.ExpectedIsDuplicatePollution),
                $"IsKnownDuplicatePollution(\"{testCase.Path ?? "(null)"}\") returned {actual}, expected {testCase.ExpectedIsDuplicatePollution}. "
                    + $"Description: {testCase.Description}"
            );
        }
    }
#endif
}
