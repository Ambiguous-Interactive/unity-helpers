// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class ValidationPreferencesTests : CommonTestBase
    {
        private bool _enabled;
        private bool _automatic;

        [SetUp]
        public void PreservePreferences()
        {
            Assert.IsFalse(ValidationScheduler.IsRunning);
            _enabled = ValidationPreferences.Enabled;
            _automatic = ValidationAutoRun.Enabled;
            ValidationPreferences.Enabled = true;
        }

        [TearDown]
        public void RestorePreferences()
        {
            ValidationScheduler.Stop();
            ValidationAutoRunTestAccess.ClearPending();
            ValidationAutoRun.Enabled = _automatic;
            ValidationPreferences.Enabled = _enabled;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisablingDropsPendingWorkAndPreservesAutomaticPreference(bool automatic)
        {
            ValidationRun blocker = new ValidationRun(
                null,
                new[]
                {
                    new ValidationTarget(
                        "pending",
                        "Assets/Pending.asset",
                        typeof(ScriptableObject)
                    ),
                }
            );
            Assert.IsTrue(ValidationScheduler.TryStart(blocker));
            ValidationAutoRun.Enabled = automatic;
            ValidationAutoRun.Queue(new[] { "first", "second" }, true);
            Assert.AreEqual(automatic ? 2 : 0, ValidationAutoRun.PendingCount);

            ValidationPreferences.Enabled = false;
            Assert.AreEqual(0, ValidationAutoRun.PendingCount);
            Assert.AreEqual(automatic, ValidationAutoRun.Enabled);
            Assert.IsTrue(blocker.IsCancelled);
            ValidationAutoRun.Queue(new[] { "third" }, true);
            Assert.AreEqual(0, ValidationAutoRun.PendingCount);

            ValidationPreferences.Enabled = true;
            Assert.AreEqual(automatic, ValidationAutoRun.Enabled);
            Assert.AreEqual(0, ValidationAutoRun.PendingCount);
            Assert.IsFalse(ValidationScheduler.IsRunning);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisablingClosesTheWindowAndSuppressesNewStatusContent(bool show)
        {
            ValidationWindow window = Track(ScriptableObject.CreateInstance<ValidationWindow>());
            Assert.IsTrue(window != null);
            if (
                show
                && !Application.isBatchMode
                && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null
            )
            {
                window.Show();
                Assert.IsTrue(window.rootVisualElement.panel != null);
            }
            ValidationPreferences.Enabled = false;
            Assert.IsTrue(window == null);
            Assert.IsFalse(new ValidationSceneOverlay().visible);
            Assert.IsFalse(new ValidationToolbarOverlay().visible);
            Assert.AreEqual(
                DisplayStyle.None,
                ValidationStatusSurfaces.CreatePanel().style.display.value
            );
            Assert.AreEqual(DisplayStyle.None, new ValidationToolbarButton().style.display.value);

            ValidationPreferences.Enabled = true;
            Assert.IsTrue(new ValidationSceneOverlay().visible);
            Assert.IsTrue(new ValidationToolbarOverlay().visible);
            Assert.AreEqual(
                DisplayStyle.Flex,
                ValidationStatusSurfaces.CreatePanel().style.display.value
            );
        }

        [Test]
        public void ReenablingBeforeDeferredCleanupRestoresTheWindow()
        {
            ValidationPreferences.Enabled = false;
            ValidationWindow window = Track(ScriptableObject.CreateInstance<ValidationWindow>());
            ValidationPreferences.Enabled = true;
            Assert.IsTrue(window != null);
            Assert.IsTrue(window.rootVisualElement.Q<Button>("validate-project") != null);
        }

        [TestCase(false, ValidationSeverity.Info)]
        [TestCase(true, ValidationSeverity.Info)]
        [TestCase(false, ValidationSeverity.Warning)]
        [TestCase(true, ValidationSeverity.Warning)]
        [TestCase(false, ValidationSeverity.Error)]
        [TestCase(true, ValidationSeverity.Error)]
        public void BuildGateCommandLinePreservesOptionalSuppressions(
            bool hasSuppressions,
            ValidationSeverity severity
        )
        {
            string[] arguments = ValidationBuildGate.CreateCommandLine(severity, hasSuppressions);
            Assert.AreEqual(
                severity,
                ValidationBatch.ParseSeverity(
                    Helpers.GetCommandLineArgument(arguments, ValidationBatch.FailOnArgument),
                    (ValidationSeverity)(-1)
                )
            );
            string path = Helpers.GetCommandLineArgument(
                arguments,
                ValidationBatch.SuppressionsArgument
            );
            Assert.AreEqual(hasSuppressions ? ValidationBuildGate.SuppressionsFile : null, path);
            Assert.That(arguments, Has.None.EqualTo(string.Empty));
        }

        [Test]
        public void DisabledBuildHookDoesNotInspectInvalidProfiles()
        {
            ValidationWorkspaceSettings settings = ValidationWorkspaceSettings.instance;
            System.Collections.Generic.List<ValidationWorkspaceSettings.Profile> profiles =
                settings.profiles;
            try
            {
                settings.profiles = null;
                ValidationPreferences.Enabled = false;
                Assert.DoesNotThrow(() => new ValidationBuildGate().OnPreprocessBuild(null));
            }
            finally
            {
                settings.profiles = profiles;
            }
        }
    }
}
