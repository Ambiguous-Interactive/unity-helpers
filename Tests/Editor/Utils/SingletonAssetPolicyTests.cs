// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class SingletonAssetPolicyTests
    {
        [Test]
        public void AllTestSingletonTypesWithExclusionHaveTheAttribute()
        {
            List<string> missingAttribute = new();
            foreach (Type type in SingletonTestTypes.WithExclusion)
            {
                if (
                    !ReflectionHelpers.TryGetAttributeSafe<ExcludeFromSingletonCreationAttribute>(
                        type,
                        out _,
                        inherit: false
                    )
                )
                {
                    missingAttribute.Add(type.FullName);
                }
            }

            Assert.IsEmpty(
                missingAttribute,
                $"The following test singleton types are missing [ExcludeFromSingletonCreation] attribute:\n{string.Join("\n", missingAttribute)}"
            );
        }

        [Test]
        public void CreatorTestTypesDoNotHaveExclusionAttribute()
        {
            List<string> hasAttribute = new();
            foreach (Type type in SingletonTestTypes.WithoutExclusion)
            {
                if (
                    ReflectionHelpers.TryGetAttributeSafe<ExcludeFromSingletonCreationAttribute>(
                        type,
                        out _,
                        inherit: false
                    )
                )
                {
                    hasAttribute.Add(type.FullName);
                }
            }

            Assert.IsEmpty(
                hasAttribute,
                $"The following creator test types should NOT have [ExcludeFromSingletonCreation] attribute as they are used to test creation logic:\n{string.Join("\n", hasAttribute)}"
            );
        }

        [Test]
        public void DuplicatePollutionFoldersAreNotProtected()
        {
            string[] duplicatePaths = new[]
            {
                "Assets/Resources/Wallstop Studios/Unity Helpers 1",
                "Assets/Resources/Wallstop Studios/Unity Helpers 2",
                "Assets/Resources/Wallstop Studios/Unity Helpers 10",
                "Assets/Resources/Wallstop Studios/Unity Helpers 99",
                "Assets/Resources/Wallstop Studios 1",
                "Assets/Resources/Wallstop Studios 2",
            };

            List<string> incorrectlyProtected = new();
            foreach (string path in duplicatePaths)
            {
                if (CommonTestBase.ProtectionTestHooks.TestIsProtectedPath(path))
                {
                    incorrectlyProtected.Add(path);
                }
            }

            Assert.IsEmpty(
                incorrectlyProtected,
                $"The following duplicate pollution folders are incorrectly marked as protected:\n"
                    + $"{string.Join("\n", incorrectlyProtected)}\n"
                    + "These should be deletable during test cleanup."
            );
        }

        [Test]
        public void MainProductionFoldersAreProtected()
        {
            string[] mainFolders = new[]
            {
                "Assets/Resources/Wallstop Studios",
                "Assets/Resources/Wallstop Studios/Unity Helpers",
                "Assets/Plugins",
                "Assets/Editor Default Resources",
                "Assets/StreamingAssets",
            };

            List<string> unprotected = new();
            foreach (string path in mainFolders)
            {
                if (!CommonTestBase.ProtectionTestHooks.TestIsProtectedPath(path))
                {
                    unprotected.Add(path);
                }
            }

            Assert.IsEmpty(
                unprotected,
                $"The following main production folders are NOT protected:\n"
                    + $"{string.Join("\n", unprotected)}\n"
                    + "These should be protected from test cleanup."
            );
        }

        [Test]
        public void ExcludeFromSingletonCreationAttributeIsDefinedCorrectly()
        {
            Type attributeType = typeof(ExcludeFromSingletonCreationAttribute);
            Assert.IsTrue(
                attributeType.IsSealed,
                "ExcludeFromSingletonCreationAttribute should be sealed"
            );

            AttributeUsageAttribute usage =
                attributeType.GetCustomAttribute<AttributeUsageAttribute>();
            Assert.IsTrue(
                usage != null,
                "ExcludeFromSingletonCreationAttribute should have AttributeUsage"
            );
            Assert.AreEqual(
                AttributeTargets.Class,
                usage.ValidOn,
                "ExcludeFromSingletonCreationAttribute should target classes"
            );
            Assert.IsFalse(
                usage.Inherited,
                "ExcludeFromSingletonCreationAttribute should not be inherited"
            );
            Assert.IsFalse(
                usage.AllowMultiple,
                "ExcludeFromSingletonCreationAttribute should not allow multiple"
            );
        }

        [Test]
        public void AllTestTypesAreDetectedByIsTestTypeHelper()
        {
            List<string> notDetected = new();
            foreach (Type type in SingletonTestTypes.All)
            {
                if (!TestAssemblyHelper.IsTestType(type))
                {
                    notDetected.Add(type.FullName);
                }
            }

            Assert.IsEmpty(
                notDetected,
                $"The following test singleton types are not detected by TestAssemblyHelper.IsTestType:\n{string.Join("\n", notDetected)}"
            );
        }
    }
}
