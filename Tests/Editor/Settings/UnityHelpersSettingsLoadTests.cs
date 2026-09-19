// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Settings
{
#if UNITY_EDITOR
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    public sealed class UnityHelpersSettingsLoadTests : CommonTestBase
    {
        [Test]
        public void OnEnableUsesLoadingObjectForDirectoryAndPoolSettings()
        {
            UnityHelpersSettings registered = UnityHelpersSettings.instance;
            string originalDirectory = registered._failedTestsOutputDirectory;
            float originalIdleTimeout = registered._poolIdleTimeoutSeconds;
            LogAssert.Expect(
                LogType.Error,
                new Regex("ScriptableSingleton.*already exists", RegexOptions.CultureInvariant)
            );
            UnityHelpersSettings loading = Track(
                ScriptableObject.CreateInstance<UnityHelpersSettings>()
            );

            try
            {
                registered._failedTestsOutputDirectory = string.Empty;
                registered._poolIdleTimeoutSeconds = 55f;
                loading._failedTestsOutputDirectory = "ProjectSettings";
                loading._poolIdleTimeoutSeconds = 123f;

                loading.OnEnable();

                Assert.That(loading._failedTestsOutputDirectory, Is.EqualTo("ProjectSettings"));
                Assert.That(PoolPurgeSettings.DefaultGlobalIdleTimeoutSeconds, Is.EqualTo(123f));
            }
            finally
            {
                registered._failedTestsOutputDirectory = originalDirectory;
                registered._poolIdleTimeoutSeconds = originalIdleTimeout;
                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();
            }
        }
    }
#endif
}
