// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes;

    /// <summary>
    /// Holds the package's own shipped trees to the two rules that keep a type authorable.
    /// </summary>
    /// <remarks>
    /// A type with no <c>MonoScript</c> still compiles and <c>AddComponent</c> still constructs it,
    /// so every behavioral test passes and the gap shows up only when somebody tries to author the
    /// thing -- where it reads as "the component will not drag onto the prefab", which looks like a
    /// Unity glitch rather than a defect in this package.
    /// </remarks>
    [TestFixture]
    public sealed class MonoScriptBindingValidatorTests
    {
        [SetUp]
        public void ResolvePackageRoot()
        {
            MonoScriptIndex.ClearCaches();
            Assert.IsTrue(
                MonoScriptIndex.TryGetScriptPath(
                    typeof(MonoScriptBindingValidator),
                    out string scriptPath
                ),
                "The validator cannot find its own script, so every 'except its own file' exclusion "
                    + "would match nothing and the scan would pass for every type at once."
            );

            int marker = scriptPath.IndexOf(EditorFolder, StringComparison.Ordinal);
            Assert.IsTrue(0 <= marker, scriptPath);
            _packageRoot = scriptPath.Substring(0, marker + 1);
        }

        [TearDown]
        public void ForgetPackageRoot()
        {
            MonoScriptIndex.ClearCaches();
            _packageRoot = null;
        }

        [Test]
        public void EveryShippedTypeCanBeAuthoredOntoSomething()
        {
            List<MonoScriptBindingFinding> findings = ScanShippedTrees(
                out int typesConsidered,
                out int scriptsConsidered
            );

            Assert.IsTrue(0 < typesConsidered, "The type scope stopped matching the package.");
            Assert.IsTrue(0 < scriptsConsidered, "The script scope stopped matching the package.");
            CollectionAssert.IsEmpty(
                findings.Select(finding => finding.ToString()).ToArray(),
                string.Join(Environment.NewLine, findings.Select(finding => finding.ToString()))
            );
        }

        [Test]
        public void EachShippedTreeIsInScopeOnItsOwn()
        {
            List<MonoScriptBindingFinding> runtimeFindings = new();
            Assert.IsTrue(
                MonoScriptBindingValidator.TryScan(
                    new[] { $"{_packageRoot}Runtime/" },
                    runtimeFindings,
                    out int runtimeTypes,
                    out int runtimeScripts
                )
            );

            List<MonoScriptBindingFinding> editorFindings = new();
            Assert.IsTrue(
                MonoScriptBindingValidator.TryScan(
                    new[] { $"{_packageRoot}Editor/" },
                    editorFindings,
                    out int editorTypes,
                    out int editorScripts
                )
            );

            /*
                Per tree rather than over both, because a scope that silently loses one reports the
                same zero findings a clean scan does, and the combined count stays non-zero on the
                strength of the tree that survived.
            */
            Assert.IsTrue(0 < runtimeTypes, "Runtime/ fell out of the assembly scope.");
            Assert.IsTrue(0 < runtimeScripts, "Runtime/ fell out of the script scope.");
            Assert.IsTrue(0 < editorTypes, "Editor/ fell out of the assembly scope.");
            Assert.IsTrue(0 < editorScripts, "Editor/ fell out of the script scope.");
        }

        [Test]
        public void DiscoveryIsUnitysOwnIndexAndExcludesOnlyWhatCannotBeInstantiated()
        {
            Type[] discovered = MonoScriptBindingValidator.ConcreteAuthorableTypes().ToArray();

            CollectionAssert.Contains(discovered, typeof(AuthoredRequirementTestAsset));
            Assert.IsFalse(
                discovered.Any(type => type.IsAbstract),
                "Nothing can be an instance of an abstract type, so it has nothing to author."
            );
            Assert.IsFalse(
                discovered.Any(type => typeof(UnityEditor.Editor).IsAssignableFrom(type)),
                "An inspector is bound by CustomEditor, not by being authored onto anything."
            );
        }

        [Test]
        public void AScanWithNoScopeIsRefusedRatherThanReportedClean()
        {
            List<MonoScriptBindingFinding> findings = new();

            Assert.IsFalse(
                MonoScriptBindingValidator.TryScan(null, findings, out int _, out int _)
            );
            Assert.IsFalse(
                MonoScriptBindingValidator.TryScan(
                    Array.Empty<string>(),
                    findings,
                    out int _,
                    out int _
                )
            );
            Assert.IsFalse(
                MonoScriptBindingValidator.TryScan(
                    new[] { _packageRoot },
                    null,
                    out int _,
                    out int _
                )
            );
        }

        private List<MonoScriptBindingFinding> ScanShippedTrees(
            out int typesConsidered,
            out int scriptsConsidered
        )
        {
            List<MonoScriptBindingFinding> findings = new();
            Assert.IsTrue(
                MonoScriptBindingValidator.TryScan(
                    new[] { $"{_packageRoot}Runtime/", $"{_packageRoot}Editor/" },
                    findings,
                    out typesConsidered,
                    out scriptsConsidered
                )
            );

            return findings;
        }

        private const string EditorFolder = "/Editor/";

        private string _packageRoot;
    }
}
