// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Comprehensive tests for CI environment detection functionality.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CiEnvironmentDetectionTests : CommonTestBase
    {
        private Dictionary<string, string> _originalValues;

        [SetUp]
        public void SetUp()
        {
            // Store original values for all CI environment variables
            _originalValues = new Dictionary<string, string>();
            foreach (string envVar in Helpers.CiEnvironmentVariables.All)
            {
                _originalValues[envVar] = Environment.GetEnvironmentVariable(envVar);
            }
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            // Restore all original values
            foreach (KeyValuePair<string, string> kvp in _originalValues)
            {
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }
        }

        private static void ClearAllCiEnvironmentVariables()
        {
            foreach (string envVar in Helpers.CiEnvironmentVariables.All)
            {
                Environment.SetEnvironmentVariable(envVar, null);
            }
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsFalseWhenNoVariablesSet()
        {
            ClearAllCiEnvironmentVariables();
            Assert.IsFalse(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForCiVariable()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "true");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForGitHubActions()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(
                Helpers.CiEnvironmentVariables.GitHubActions,
                "true"
            );
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForGitLabCi()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.GitLabCi, "true");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForJenkinsUrl()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(
                Helpers.CiEnvironmentVariables.JenkinsUrl,
                "http://jenkins.example.com"
            );
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForTravisCi()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.TravisCi, "true");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForCircleCi()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.CircleCi, "true");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForAzurePipelines()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(
                Helpers.CiEnvironmentVariables.AzurePipelines,
                "True"
            );
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForTeamCity()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(
                Helpers.CiEnvironmentVariables.TeamCity,
                "2023.05.1"
            );
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForBuildkite()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Buildkite, "true");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForAwsCodeBuild()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(
                Helpers.CiEnvironmentVariables.AwsCodeBuild,
                "build-id-12345"
            );
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForBitbucketPipelines()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(
                Helpers.CiEnvironmentVariables.BitbucketPipelines,
                "12345"
            );
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForAppVeyor()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.AppVeyor, "True");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForDroneCi()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.DroneCi, "true");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForUnityCi()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.UnityCi, "1");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForUnityTests()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.UnityTests, "1");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsFalseForEmptyStringValue()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "");
            Assert.IsFalse(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsFalseForWhitespaceOnlyValue()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "   ");
            Assert.IsFalse(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForAnyNonWhitespaceValue()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "1");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForFalseStringValue()
        {
            // Even "false" as a string value should be detected as CI running
            // because the env var is set (just poorly configured)
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "false");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInContinuousIntegrationReturnsTrueForZeroValue()
        {
            // Even "0" as a value should be detected as CI running
            // because the env var is set (detection is based on presence, not truthiness)
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "0");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void DetectionIsDynamicAndReflectsCurrentEnvironment()
        {
            // Verify that detection responds immediately to environment changes
            // (no caching - environment variables are checked on each access)
            ClearAllCiEnvironmentVariables();
            Assert.IsFalse(Helpers.IsRunningInContinuousIntegration);

            // Set a CI variable - detection should immediately return true
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "true");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);

            // Clear it - detection should immediately return false
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, null);
            Assert.IsFalse(Helpers.IsRunningInContinuousIntegration);

            // Set a different CI variable
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.GitHubActions, "1");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);

            // Clear it
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.GitHubActions, null);
            Assert.IsFalse(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void MultipleCiVariablesSetStillReturnsTrue()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "true");
            Environment.SetEnvironmentVariable(
                Helpers.CiEnvironmentVariables.GitHubActions,
                "true"
            );
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.GitLabCi, "true");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsEnvironmentVariableSetReturnsTrueForSetVariable()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "true");
            Assert.IsTrue(Helpers.IsEnvironmentVariableSet(Helpers.CiEnvironmentVariables.Ci));
        }

        [Test]
        public void IsEnvironmentVariableSetReturnsFalseForUnsetVariable()
        {
            ClearAllCiEnvironmentVariables();
            Assert.IsFalse(Helpers.IsEnvironmentVariableSet(Helpers.CiEnvironmentVariables.Ci));
        }

        [Test]
        public void IsEnvironmentVariableSetReturnsFalseForEmptyValue()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "");
            Assert.IsFalse(Helpers.IsEnvironmentVariableSet(Helpers.CiEnvironmentVariables.Ci));
        }

        [Test]
        public void IsEnvironmentVariableSetReturnsFalseForWhitespaceValue()
        {
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "   ");
            Assert.IsFalse(Helpers.IsEnvironmentVariableSet(Helpers.CiEnvironmentVariables.Ci));
        }

        [Test]
        public void IsEnvironmentVariableSetWorksForArbitraryVariables()
        {
            // Test that it works with arbitrary environment variable names
            string testVar = "TEST_VAR_" + Guid.NewGuid().ToString("N");
            try
            {
                Assert.IsFalse(Helpers.IsEnvironmentVariableSet(testVar));

                Environment.SetEnvironmentVariable(testVar, "value");
                Assert.IsTrue(Helpers.IsEnvironmentVariableSet(testVar));

                Environment.SetEnvironmentVariable(testVar, "");
                Assert.IsFalse(Helpers.IsEnvironmentVariableSet(testVar));
            }
            finally
            {
                Environment.SetEnvironmentVariable(testVar, null);
            }
        }

        [Test]
        public void CiEnvironmentVariablesAllContainsExpectedVariables()
        {
            string[] all = Helpers.CiEnvironmentVariables.All;
            Assert.IsTrue(15 <= all.Length);

            Assert.Contains(Helpers.CiEnvironmentVariables.Ci, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.GitHubActions, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.GitLabCi, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.JenkinsUrl, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.TravisCi, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.CircleCi, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.AzurePipelines, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.TeamCity, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.Buildkite, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.AwsCodeBuild, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.BitbucketPipelines, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.AppVeyor, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.DroneCi, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.UnityCi, all);
            Assert.Contains(Helpers.CiEnvironmentVariables.UnityTests, all);
        }

        [Test]
        public void CiEnvironmentVariablesConstantsAreNotEmpty()
        {
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.Ci));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.GitHubActions));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.GitLabCi));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.JenkinsUrl));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.TravisCi));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.CircleCi));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.AzurePipelines));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.TeamCity));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.Buildkite));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.AwsCodeBuild));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.BitbucketPipelines));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.AppVeyor));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.DroneCi));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.UnityCi));
            Assert.IsFalse(string.IsNullOrEmpty(Helpers.CiEnvironmentVariables.UnityTests));
        }

        [Test]
        public void AllDefinedCiVariablesAreTested()
        {
            // This test ensures that every environment variable in the All array
            // is individually tested by verifying it triggers CI detection
            ClearAllCiEnvironmentVariables();

            foreach (string envVar in Helpers.CiEnvironmentVariables.All)
            {
                Environment.SetEnvironmentVariable(envVar, "test_value");
                Assert.IsTrue(
                    Helpers.IsRunningInContinuousIntegration,
                    $"Environment variable '{envVar}' should trigger CI detection"
                );
                Environment.SetEnvironmentVariable(envVar, null);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        public void IsEnvironmentVariableSetHandlesNullAndEmptyVariableName(string variableName)
        {
            // Should not throw, just return false for invalid input
            Assert.IsFalse(Helpers.IsEnvironmentVariableSet(variableName));
        }

        [Test]
        public void IsRunningInContinuousIntegrationIsCaseInsensitiveForVariableValues()
        {
            // Environment variable values should work regardless of case
            ClearAllCiEnvironmentVariables();

            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "TRUE");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);

            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "True");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);

            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "tRuE");
            Assert.IsTrue(Helpers.IsRunningInContinuousIntegration);
        }

        [Test]
        public void IsRunningInBatchModeReflectsApplicationIsBatchMode()
        {
            // This is a pass-through property
            Assert.AreEqual(UnityEngine.Application.isBatchMode, Helpers.IsRunningInBatchMode);
        }

        [Test]
        public void RepeatedAccessDoesNotThrow()
        {
            // Verify repeated access works correctly
            ClearAllCiEnvironmentVariables();
            Environment.SetEnvironmentVariable(Helpers.CiEnvironmentVariables.Ci, "true");

            // Access multiple times rapidly
            for (int i = 0; i < 1000; i++)
            {
                bool result = Helpers.IsRunningInContinuousIntegration;
                Assert.IsTrue(result);
            }
        }
    }
}
