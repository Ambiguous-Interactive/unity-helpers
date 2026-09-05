// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Editor")]
    public sealed class FailedTestsExporterTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
        }

        [Test]
        public void FailedTestInfoConstructorSetsAllFields()
        {
            FailedTestsExporter.FailedTestInfo info = new(
                "TestNamespace.TestClass.TestMethod",
                "Expected true but was false",
                "at TestClass.TestMethod() in file.cs:line 10"
            );

            Assert.AreEqual("TestNamespace.TestClass.TestMethod", info.name);
            Assert.AreEqual("Expected true but was false", info.message);
            Assert.AreEqual("at TestClass.TestMethod() in file.cs:line 10", info.stackTrace);
        }

        [Test]
        public void FailedTestInfoConstructorWithEmptyStrings()
        {
            FailedTestsExporter.FailedTestInfo info = new(string.Empty, string.Empty, string.Empty);

            Assert.AreEqual(string.Empty, info.name);
            Assert.AreEqual(string.Empty, info.message);
            Assert.AreEqual(string.Empty, info.stackTrace);
        }

        [Test]
        public void FailedTestInfoConstructorWithNullValues()
        {
            FailedTestsExporter.FailedTestInfo info = new(null, null, null);

            Assert.IsTrue(info.name == null);
            Assert.IsTrue(info.message == null);
            Assert.IsTrue(info.stackTrace == null);
        }

        [Test]
        public void IsEnabledReturnsFalseByDefault()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool originalValue = settings._failedTestsExporterEnabled;
            try
            {
                settings._failedTestsExporterEnabled =
                    UnityHelpersSettings.DefaultFailedTestsExporterEnabled;
                Assert.IsFalse(FailedTestsExporter.IsEnabled());
            }
            finally
            {
                settings._failedTestsExporterEnabled = originalValue;
                settings.SaveSettings();
            }
        }

        [Test]
        public void IsEnabledReflectsSettingsValue()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool originalValue = settings._failedTestsExporterEnabled;
            try
            {
                settings._failedTestsExporterEnabled = true;
                Assert.IsTrue(FailedTestsExporter.IsEnabled());

                settings._failedTestsExporterEnabled = false;
                Assert.IsFalse(FailedTestsExporter.IsEnabled());
            }
            finally
            {
                settings._failedTestsExporterEnabled = originalValue;
                settings.SaveSettings();
            }
        }

        [Test]
        public void InstanceIsNullWhenDisabled()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool originalValue = settings._failedTestsExporterEnabled;
            try
            {
                settings._failedTestsExporterEnabled = false;
                FailedTestsExporter.Reinitialize();

                FailedTestsExporter instance = FailedTestsExporter.Instance;
                Assert.IsTrue(instance == null);
            }
            finally
            {
                settings._failedTestsExporterEnabled = originalValue;
                settings.SaveSettings();
            }
        }

        [Test]
        public void FailuresPropertyReturnsEmptyListByDefault()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool originalValue = settings._failedTestsExporterEnabled;
            try
            {
                settings._failedTestsExporterEnabled = true;
                FailedTestsExporter.Reinitialize();

                FailedTestsExporter instance = FailedTestsExporter.Instance;
                if (instance != null)
                {
                    IReadOnlyList<FailedTestsExporter.FailedTestInfo> failures = instance.Failures;
                    Assert.IsTrue(failures != null);
                    Assert.AreEqual(0, failures.Count);
                }
            }
            finally
            {
                settings._failedTestsExporterEnabled = originalValue;
                FailedTestsExporter.Reinitialize();
                settings.SaveSettings();
            }
        }

        /// <summary>
        /// Pins that registration does not wait for an editor tick.
        /// </summary>
        /// <remarks>
        /// The exporter used to register only on <c>EditorApplication.delayCall</c>, and an editor
        /// nothing is interacting with -- a CI editor driven over a socket, which is exactly the one
        /// that wants failed tests exported -- does not necessarily pump that tick. This asserts the
        /// instance exists the moment initialization returns, with nothing pumped in between, so
        /// putting the registration back behind a tick turns it red
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/684">#684</see>).
        /// </remarks>
        [Test]
        public void RegistrationDoesNotWaitForAnEditorTick()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool originalValue = settings._failedTestsExporterEnabled;
            try
            {
                settings._failedTestsExporterEnabled = true;
                FailedTestsExporter.Reinitialize();

                Assert.IsTrue(
                    FailedTestsExporter.Instance != null,
                    "Initialization returned without a registered exporter, so it is waiting for a "
                        + "tick an unattended editor may never pump."
                );
            }
            finally
            {
                settings._failedTestsExporterEnabled = originalValue;
                FailedTestsExporter.Reinitialize();
                settings.SaveSettings();
            }
        }

        /// <summary>
        /// Pins that a readable setting of <c>false</c> is reported as read, not as unreadable.
        /// </summary>
        /// <remarks>
        /// Registration retries only while the settings object cannot be read. Collapsing "switched
        /// off" into that outcome would retry a disabled feature on every reload forever, which is
        /// the failure the fix introduces if the two are ever merged back together. The unavailable
        /// branch itself is a stated omission: settings are loadable in a running editor, so nothing
        /// a test can author makes <c>ScriptableSingleton.instance</c> fail on demand.
        /// </remarks>
        [Test]
        [TestCase(false, TestName = "TryIsEnabledReportsAReadableFalseAsRead")]
        [TestCase(true, TestName = "TryIsEnabledReportsAReadableTrueAsRead")]
        public void TryIsEnabledSeparatesDisabledFromUnavailable(bool configured)
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool originalValue = settings._failedTestsExporterEnabled;
            try
            {
                settings._failedTestsExporterEnabled = configured;

                Assert.IsTrue(
                    FailedTestsExporter.TryIsEnabled(out bool enabled),
                    "A settings object that read fine was reported as unavailable."
                );
                Assert.AreEqual(configured, enabled);
                Assert.AreEqual(configured, FailedTestsExporter.IsEnabled());
            }
            finally
            {
                settings._failedTestsExporterEnabled = originalValue;
                FailedTestsExporter.Reinitialize();
                settings.SaveSettings();
            }
        }

        [Test]
        public void SettingsDefaultIsFalse()
        {
            Assert.IsFalse(UnityHelpersSettings.DefaultFailedTestsExporterEnabled);
        }

        [Test]
        public void ReinitializeDoesNotThrowWhenDisabled()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool originalValue = settings._failedTestsExporterEnabled;
            try
            {
                settings._failedTestsExporterEnabled = false;
                Assert.DoesNotThrow(() => FailedTestsExporter.Reinitialize());
            }
            finally
            {
                settings._failedTestsExporterEnabled = originalValue;
                settings.SaveSettings();
            }
        }

        [Test]
        public void ReinitializeDoesNotThrowWhenEnabled()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool originalValue = settings._failedTestsExporterEnabled;
            try
            {
                settings._failedTestsExporterEnabled = true;
                Assert.DoesNotThrow(() => FailedTestsExporter.Reinitialize());
            }
            finally
            {
                settings._failedTestsExporterEnabled = originalValue;
                FailedTestsExporter.Reinitialize();
                settings.SaveSettings();
            }
        }

        [TestCase("", TestName = "GetFailedTestsOutputDirectoryReturnsDefaultWhenEmpty")]
        [TestCase(null, TestName = "GetFailedTestsOutputDirectoryReturnsDefaultWhenNull")]
        [TestCase("   ", TestName = "GetFailedTestsOutputDirectoryReturnsDefaultWhenWhitespace")]
        [TestCase("/etc/passwd", TestName = "GetFailedTestsOutputDirectoryRejectsAbsolutePath")]
        [TestCase(
            "C:\\Users\\test",
            TestName = "GetFailedTestsOutputDirectoryRejectsWindowsAbsolutePath"
        )]
        [TestCase(
            "subdir/../../etc",
            TestName = "GetFailedTestsOutputDirectoryRejectsPathTraversal"
        )]
        [TestCase("..", TestName = "GetFailedTestsOutputDirectoryRejectsDoubleDotOnly")]
        [TestCase(
            "this_directory_definitely_does_not_exist_12345",
            TestName = "GetFailedTestsOutputDirectoryRejectsNonExistentDirectory"
        )]
        public void GetFailedTestsOutputDirectoryReturnsDefaultForInvalidInput(string input)
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            string original = settings._failedTestsOutputDirectory;
            try
            {
                settings._failedTestsOutputDirectory = input;
                Assert.AreEqual(
                    UnityHelpersSettings.DefaultFailedTestsOutputDirectory,
                    UnityHelpersSettings.GetFailedTestsOutputDirectory()
                );
            }
            finally
            {
                settings._failedTestsOutputDirectory = original;
                settings.SaveSettings();
            }
        }

        [TestCase(
            "Temp",
            "Temp",
            TestName = "GetFailedTestsOutputDirectoryAcceptsValidExistingDirectory"
        )]
        [TestCase(
            "Temp\\",
            "Temp",
            TestName = "GetFailedTestsOutputDirectoryNormalizesBackslashes"
        )]
        [TestCase("Temp/", "Temp", TestName = "GetFailedTestsOutputDirectoryTrimsTrailingSlash")]
        public void GetFailedTestsOutputDirectoryValidDirectoryInput(string input, string expected)
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            string original = settings._failedTestsOutputDirectory;
            string projectRoot = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(UnityEngine.Application.dataPath, "..")
            );
            string fullTestPath = System.IO.Path.Combine(projectRoot, "Temp");
            bool createdDirectory = false;

            try
            {
                if (!System.IO.Directory.Exists(fullTestPath))
                {
                    System.IO.Directory.CreateDirectory(fullTestPath);
                    createdDirectory = true;
                }

                settings._failedTestsOutputDirectory = input;
                string result = UnityHelpersSettings.GetFailedTestsOutputDirectory();
                Assert.AreEqual(expected, result);
            }
            finally
            {
                settings._failedTestsOutputDirectory = original;
                settings.SaveSettings();
                if (createdDirectory && System.IO.Directory.Exists(fullTestPath))
                {
                    try
                    {
                        System.IO.Directory.Delete(fullTestPath);
                    }
                    catch { }
                }
            }
        }

        [Test]
        public void DefaultFailedTestsOutputDirectoryIsEmptyString()
        {
            Assert.AreEqual(string.Empty, UnityHelpersSettings.DefaultFailedTestsOutputDirectory);
        }
    }
#endif
}
