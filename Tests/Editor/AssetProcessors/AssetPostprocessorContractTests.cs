// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Source-scan contract that every <see cref="AssetPostprocessor"/> subclass in this
    /// package MUST avoid synchronous asset-database / component / reflection operations
    /// inside Unity's asset-import phase. The canonical way to satisfy the contract is
    /// to route such work through
    /// <see cref="WallstopStudios.UnityHelpers.Editor.AssetProcessors.AssetPostprocessorDeferral.Schedule(System.Action)"/>.
    ///
    /// Reflection alone cannot introspect a method body; this test reads the declaring
    /// <c>.cs</c> source file and scans each override's body for forbidden tokens. If a
    /// processor needs to call one of these APIs, wrap the call in a drain lambda
    /// scheduled via <c>AssetPostprocessorDeferral.Schedule</c>.
    ///
    /// <b>This scan covers the callback's own body only, and that is not the whole rule.</b> A
    /// forbidden call reached through a helper is invisible here, and one was: #439 found
    /// <c>OnPostprocessAllAssets</c> reaching <c>AssetDatabase.LoadAssetAtPath</c> four frames
    /// down through <c>EnsureInitialized</c> while this test passed. The transitive contract lives
    /// in <c>scripts/tests/test-asset-postprocessor-reachability.js</c>
    /// (<c>npm run test:asset-postprocessor-reachability</c>), which walks the call graph within
    /// the declaring type and runs on every pull request in seconds. Treat this test as its
    /// shallow subset rather than as the guarantee.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [Category("Contract")]
    public sealed class AssetPostprocessorContractTests
    {
        private static readonly string[] EditorAssemblyNames =
        {
            "WallstopStudios.UnityHelpers.Editor",
        };

        private static readonly HashSet<string> InspectedCallbackNames = new(StringComparer.Ordinal)
        {
            "OnPostprocessAllAssets",
            "OnPreprocessAsset",
            "OnPreprocessTexture",
            "OnPostprocessTexture",
            "OnPostprocessCubemap",
            "OnPreprocessModel",
            "OnPostprocessModel",
            "OnPreprocessAudio",
            "OnPostprocessAudio",
            "OnPreprocessSpeedTree",
            "OnPostprocessSpeedTree",
            "OnPreprocessAnimation",
            "OnPostprocessAnimation",
            "OnPreprocessMaterialDescription",
            "OnPreprocessCameraDescription",
            "OnPreprocessLightDescription",
            "OnPostprocessPrefab",
            "OnPostprocessMeshHierarchy",
            "OnPostprocessMaterial",
            "OnPostprocessGameObjectWithUserProperties",
            "OnPostprocessSprites",
            "OnPostprocessAssetbundleNameChanged",
        };

        // Cover generic and non-generic overloads; either can invoke components during asset import.
        private static readonly string[] ForbiddenTokens =
        {
            "AssetDatabase.LoadAssetAtPath",
            "AssetDatabase.LoadAllAssetsAtPath",
            "AssetDatabase.LoadMainAssetAtPath",
            ".GetComponentsInChildren<",
            ".GetComponentsInChildren(",
            ".GetComponents<",
            ".GetComponents(",
            ".AddComponent<",
            ".AddComponent(",
            "Object.Instantiate",
            "GameObject.Instantiate",
            "UnityEngine.Object.Instantiate",
            "DestroyImmediate",
            "MethodInfo.Invoke",
        };

        // A word boundary avoids treating names such as preInstantiate as Unity Instantiate calls.
        private static readonly Regex BareInstantiatePattern = new(
            "\\bInstantiate\\s*\\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        private const string DeferralCallExpression = "AssetPostprocessorDeferral.Schedule(";

        /*
            CommonOneTime lifecycle overrides use package names instead of NUnit names and must obey the same
            drain discipline.
        */
        private static readonly HashSet<string> TeardownMethodNames = new(StringComparer.Ordinal)
        {
            "ClearTestState",
            "TearDown",
            "BaseSetUp",
            "SetUp",
            "OneTimeTearDown",
            "OneTimeSetUp",
            "CommonOneTimeSetUp",
            "CommonOneTimeTearDown",
        };

        /*
            Fixture-level asset mutation can leave drains for the next fixture even when it never clears handler
            statics.
        */
        private static readonly HashSet<string> OneTimeLifecycleMethodNames = new(
            StringComparer.Ordinal
        )
        {
            "OneTimeTearDown",
            "OneTimeSetUp",
            "CommonOneTimeSetUp",
            "CommonOneTimeTearDown",
        };

        /*
            Limit this source check to direct mutation calls; helper-only fixtures rely on their base-chain
            cleanup discipline.
        */
        private static readonly string[] AssetMutationTokens =
        {
            "AssetDatabase.CreateAsset",
            "AssetDatabase.DeleteAsset",
            "AssetDatabase.Refresh",
            "AssetDatabase.ImportAsset",
            "AssetDatabase.CreateFolder",
            "SaveAndRefreshIfNotBatching",
            "RefreshIfNotBatching",
        };

        /*
            Only processor-aware fixtures can pollute these handler statics; unrelated texture fixtures need no
            such drain contract.
        */
        private static readonly string[] AssetContextTokens =
        {
            "AssetPostprocessorDeferral",
            "DetectAssetChangeProcessor",
            "LlmArtifactCleaner",
            "SpriteLabelProcessor",
        };

        /*
            Restrict Clear matching to handler doubles so unrelated collection clears do not trigger the
            contract.
        */
        private static readonly Regex HandlerClearPattern = new(
            "\\bTest\\w*Handler\\.Clear\\s*\\(\\s*\\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        // Shared clear helpers mutate the same handler state and need the same drain guarantee.
        private static readonly string[] HandlerClearEquivalentCalls =
        {
            "AssetPostprocessorTestHandlers.FlushAndClearAll(",
            "AssetPostprocessorTestHandlers.AssertCleanAndClearAll(",
            "ClearTestState(",
        };

        private const string FlushCallExpression = "AssetPostprocessorDeferral.FlushForTesting(";

        // These helpers drain internally, so an additional explicit flush would be redundant.
        private static readonly string[] FlushEquivalentExpressions =
        {
            FlushCallExpression,
            "AssetPostprocessorTestHandlers.FlushAndClearAll(",
            "AssetPostprocessorTestHandlers.AssertCleanAndClearAll(",
            "ClearTestState(",
        };

        [Test]
        public void AllAssetPostprocessorCallbacksAvoidForbiddenSynchronousApis()
        {
            Type[] processorTypes = DiscoverEditorAssetPostprocessorTypes();
            Assert.IsNotEmpty(
                processorTypes,
                "Expected at least one AssetPostprocessor in the editor assembly."
            );

            List<string> failures = new();
            foreach (Type processorType in processorTypes)
            {
                IReadOnlyList<string> sourcePaths = ResolveSourcePaths(processorType);
                if (sourcePaths.Count == 0)
                {
                    failures.Add(
                        $"[{processorType.FullName}] could not locate source (.cs) for method-body scan. "
                            + "Verify the file lives under Editor/AssetProcessors/ or the package path."
                    );
                    continue;
                }

                IReadOnlyList<MethodInfo> inspected = FindInspectedCallbacks(processorType);
                if (inspected.Count == 0)
                {
                    continue;
                }

                foreach (MethodInfo method in inspected)
                {
                    MethodBodySearchResult result = TryExtractBodyAcrossFiles(
                        sourcePaths,
                        method.Name
                    );
                    if (result.Status == BodySearchStatus.NotFound)
                    {
                        failures.Add(
                            $"[{processorType.FullName}.{method.Name}] body not found across {sourcePaths.Count} candidate file(s): "
                                + string.Join(", ", EnumerableSelect(sourcePaths, Path.GetFileName))
                                + ". Place the override directly in a file whose name matches the type, "
                                + "or add the filename to the contract test's search roots."
                        );
                        continue;
                    }

                    if (result.Status == BodySearchStatus.ReadError)
                    {
                        failures.Add(
                            $"[{processorType.FullName}.{method.Name}] failed to read candidate source: {result.Detail}"
                        );
                        continue;
                    }

                    string stripped = StripDeferralSchedules(result.Body);
                    List<string> firedTokens = FindForbiddenTokens(stripped);
                    if (0 < firedTokens.Count)
                    {
                        StringBuilder message = new();
                        message
                            .Append('[')
                            .Append(processorType.FullName)
                            .Append('.')
                            .Append(method.Name)
                            .Append("] contains forbidden tokens: ")
                            .AppendLine(string.Join(", ", firedTokens));
                        message.AppendLine(
                            "Route these calls through AssetPostprocessorDeferral.Schedule(...) "
                                + "so they run outside Unity's asset-import phase."
                        );
                        message.Append("Source file: ").Append(result.SourcePath);
                        failures.Add(message.ToString());
                    }
                }
            }

            if (0 < failures.Count)
            {
                Assert.Fail(string.Join("\n\n", failures));
            }
        }

        /// <summary>
        /// Source-scan contract: any test method that clears a handler test double
        /// must also drain pending <see cref="AssetPostprocessor"/> deferrals in the
        /// same body. Accepted as flush-equivalent (see
        /// <c>FlushEquivalentExpressions</c>):
        /// <list type="bullet">
        /// <item><description>A direct
        /// <c>AssetPostprocessorDeferral.FlushForTesting()</c> call.</description></item>
        /// <item><description>A call to one of the centralized helpers known to
        /// flush internally before clearing:
        /// <c>AssetPostprocessorTestHandlers.FlushAndClearAll</c>,
        /// <c>AssetPostprocessorTestHandlers.AssertCleanAndClearAll</c>, or
        /// <c>ClearTestState</c> (whether called as <c>ClearTestState()</c> or
        /// <c>base.ClearTestState()</c> — both resolve to the single definition
        /// on <c>DetectAssetChangeTestBase</c>, which itself satisfies the
        /// contract).</description></item>
        /// </list>
        /// Chaining to <c>base.*</c> without naming one of these is NOT
        /// sufficient, because an arbitrary base method may or may not flush and
        /// the scanner cannot follow the indirection. The whitelist above is
        /// maintained in tandem with the helpers' implementations — if a helper
        /// stops flushing internally, it must be removed from the whitelist (and
        /// the tree-wide audit test <see cref="CentralizedClearHelpersActuallyFlush"/>
        /// fails-loudly if that invariant is broken).
        ///
        /// Without the flush, a deferred drain scheduled by a prior asset operation
        /// fires between tests and re-populates the statics we just cleared,
        /// producing flaky "test pollution detected" failures in the next test's
        /// setup.
        /// </summary>
        [Test]
        public void TestTeardownsThatClearHandlerStateFlushDeferralsFirst()
        {
            string testsRoot = ResolveEditorTestsRoot();
            if (string.IsNullOrEmpty(testsRoot))
            {
                Assert.Inconclusive(
                    "Could not locate Tests/Editor/ on disk; skipping the teardown-flush contract."
                );
                return;
            }

            string[] testFiles;
            try
            {
                testFiles = Directory.GetFiles(testsRoot, "*.cs", SearchOption.AllDirectories);
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Assert.Inconclusive(
                    $"Failed to enumerate {testsRoot}: {ex.Message}. Skipping the teardown-flush contract."
                );
                return;
            }

            List<string> failures = new();
            foreach (string path in testFiles)
            {
                string fileName = Path.GetFileName(path);
                if (IsContractTestSelf(fileName))
                {
                    continue;
                }

                if (!TryReadSource(path, fileName, failures, out string source))
                {
                    continue;
                }

                foreach (string methodName in TeardownMethodNames)
                {
                    string body = ExtractMethodBody(source, methodName);
                    if (body == null)
                    {
                        continue;
                    }

                    if (!ContainsHandlerClear(body))
                    {
                        continue;
                    }

                    if (ContainsDirectFlush(body))
                    {
                        continue;
                    }

                    failures.Add(
                        $"[{fileName}:{methodName}] clears handler state (via Test*Handler.Clear(), "
                            + "AssetPostprocessorTestHandlers.FlushAndClearAll(), "
                            + "AssetPostprocessorTestHandlers.AssertCleanAndClearAll(), or ClearTestState()) "
                            + "without either (a) a direct "
                            + $"{FlushCallExpression}) call, or (b) a call to one of the flush-equivalent "
                            + "helpers (FlushAndClearAll / AssertCleanAndClearAll / ClearTestState), in the "
                            + "same method body. A call through 'base.' is accepted only when the called "
                            + "name matches one of the whitelisted helpers — so 'base.ClearTestState()' IS "
                            + "accepted (ClearTestState(' is in the whitelist) but 'base.SomeOtherHelper()' "
                            + "is NOT, because the scanner cannot follow the indirection to confirm the "
                            + "base method flushes. Either call one of the whitelisted helpers directly, "
                            + $"or add an explicit {FlushCallExpression}) call right before the clear."
                    );
                }
            }

            if (0 < failures.Count)
            {
                Assert.Fail(string.Join("\n\n", failures));
            }
        }

        /// <summary>
        /// Source-scan contract: any <c>OneTimeSetUp</c> / <c>OneTimeTearDown</c>
        /// that performs an asset mutation (CreateAsset, DeleteAsset, Refresh,
        /// ImportAsset, CreateFolder, SaveAndRefreshIfNotBatching,
        /// RefreshIfNotBatching) must end with a DIRECT
        /// <c>AssetPostprocessorDeferral.FlushForTesting()</c> call so drains
        /// scheduled by those mutations do not leak into the next fixture.
        /// Chaining to <c>base.&lt;method&gt;(</c> is NOT sufficient — the base
        /// may or may not flush, and the indirection produces false negatives
        /// identical to the teardown-flush contract's reasoning. Every author
        /// whose OneTime* method mutates assets must pay the one-line cost of
        /// an explicit flush, matching the stricter rule already enforced for
        /// per-test teardowns that clear handler state.
        ///
        /// Scoped to test files that already interact with asset postprocessors
        /// (the gate tokens in <see cref="AssetContextTokens"/>) so non-asset
        /// fixtures aren't forced to flush.
        /// </summary>
        [Test]
        public void OneTimeLifecycleMethodsWithAssetMutationsFlushDeferrals()
        {
            string testsRoot = ResolveEditorTestsRoot();
            if (string.IsNullOrEmpty(testsRoot))
            {
                Assert.Inconclusive(
                    "Could not locate Tests/Editor/ on disk; skipping the OneTime flush contract."
                );
                return;
            }

            string[] testFiles;
            try
            {
                testFiles = Directory.GetFiles(testsRoot, "*.cs", SearchOption.AllDirectories);
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Assert.Inconclusive(
                    $"Failed to enumerate {testsRoot}: {ex.Message}. Skipping the OneTime flush contract."
                );
                return;
            }

            List<string> failures = new();
            foreach (string path in testFiles)
            {
                string fileName = Path.GetFileName(path);
                if (IsContractTestSelf(fileName))
                {
                    continue;
                }

                if (!TryReadSource(path, fileName, failures, out string source))
                {
                    continue;
                }

                if (!FileIsInAssetContext(source))
                {
                    continue;
                }

                foreach (string methodName in OneTimeLifecycleMethodNames)
                {
                    string body = ExtractMethodBody(source, methodName);
                    if (body == null)
                    {
                        continue;
                    }

                    if (!ContainsAssetMutation(body))
                    {
                        continue;
                    }

                    if (ContainsDirectFlush(body))
                    {
                        continue;
                    }

                    failures.Add(
                        $"[{fileName}:{methodName}] performs asset mutations but does not contain a direct "
                            + $"{FlushCallExpression}) call in the same method body. "
                            + "Chaining to base.* is not accepted — the base may or may not flush, "
                            + "and the indirection hides regressions that leak drains into the next fixture. "
                            + $"Add an explicit {FlushCallExpression}) call after the asset operations."
                    );
                }
            }

            if (0 < failures.Count)
            {
                Assert.Fail(string.Join("\n\n", failures));
            }
        }

        /// <summary>
        /// Source-scan audit: the centralized helpers whitelisted as
        /// flush-equivalents in <see cref="FlushEquivalentExpressions"/> must
        /// themselves actually flush. If a future author refactors one of these
        /// helpers so its body no longer reaches
        /// <c>AssetPostprocessorDeferral.FlushForTesting()</c> (directly or via
        /// another audited helper), the whole teardown-flush contract silently
        /// degrades to a no-op for every caller of that helper.
        ///
        /// The audit distinguishes two tiers to avoid a vacuous pass through
        /// mutual delegation:
        /// <list type="bullet">
        /// <item><description><b>Terminal helpers</b>
        /// (<c>FlushAndClearAll</c>, <c>AssertCleanAndClearAll</c>) must
        /// contain a DIRECT <c>FlushForTesting()</c> call — they may not
        /// delegate. If these are mutated to delegate elsewhere, the contract
        /// root is lost.</description></item>
        /// <item><description><b>Delegating helpers</b>
        /// (<c>ClearTestState</c>) may satisfy the contract by calling a
        /// terminal helper. If the delegation target changes, the audit fails
        /// loudly.</description></item>
        /// </list>
        /// </summary>
        /*
            Match the owning file as well as the method name so an unrelated implementation cannot satisfy the
            contract.
        */
        private static readonly (string FileName, string MethodName)[] CentralizedTerminalTargets =
        {
            ("AssetPostprocessorTestHandlers.cs", "FlushAndClearAll"),
            ("AssetPostprocessorTestHandlers.cs", "AssertCleanAndClearAll"),
        };

        private static readonly (
            string FileName,
            string MethodName,
            string[] AcceptedDelegates
        )[] CentralizedDelegatingTargets =
        {
            (
                "DetectAssetChangeTestBase.cs",
                "ClearTestState",
                new[]
                {
                    "AssetPostprocessorTestHandlers.FlushAndClearAll(",
                    "AssetPostprocessorTestHandlers.AssertCleanAndClearAll(",
                    FlushCallExpression,
                }
            ),
        };

        [Test]
        public void CentralizedClearHelpersActuallyFlush()
        {
            (string FileName, string MethodName)[] terminalTargets = CentralizedTerminalTargets;
            (string FileName, string MethodName, string[] AcceptedDelegates)[] delegatingTargets =
                CentralizedDelegatingTargets;

            string testsRoot = ResolveEditorTestsRoot();
            if (string.IsNullOrEmpty(testsRoot))
            {
                Assert.Inconclusive(
                    "Could not locate Tests/Editor/ on disk; skipping the centralized-helper flush audit."
                );
                return;
            }

            List<string> failures = new();
            foreach ((string FileName, string MethodName) terminalTargetsElement in terminalTargets)
            {
                (string fileName, string methodName) = terminalTargetsElement;
                string body = LoadSingleMethodBody(testsRoot, fileName, methodName, failures);
                if (body == null)
                {
                    continue;
                }

                if (IndexOfOutsideLiteral(body, FlushCallExpression, 0) < 0)
                {
                    failures.Add(
                        $"[{fileName}:{methodName}] is a TERMINAL centralized helper but its body does "
                            + $"not contain a direct {FlushCallExpression}) call. The teardown-flush "
                            + "contract relies on this helper being the flush root; remove it from "
                            + "FlushEquivalentExpressions in AssetPostprocessorContractTests if the "
                            + "helper is no longer intended to flush."
                    );
                }
            }

            foreach (
                (
                    string FileName,
                    string MethodName,
                    string[] AcceptedDelegates
                ) delegatingTargetsElement in delegatingTargets
            )
            {
                (string fileName, string methodName, string[] accepted) = delegatingTargetsElement;
                string body = LoadSingleMethodBody(testsRoot, fileName, methodName, failures);
                if (body == null)
                {
                    continue;
                }

                bool satisfied = false;
                foreach (string acceptedElement in accepted)
                {
                    if (0 <= IndexOfOutsideLiteral(body, acceptedElement, 0))
                    {
                        satisfied = true;
                        break;
                    }
                }

                if (!satisfied)
                {
                    failures.Add(
                        $"[{fileName}:{methodName}] is a DELEGATING centralized helper but its body "
                            + "does not call any accepted flush root ("
                            + string.Join(", ", accepted)
                            + "). Route the method through one of these, or remove it from "
                            + "FlushEquivalentExpressions in AssetPostprocessorContractTests."
                    );
                }
            }

            if (0 < failures.Count)
            {
                Assert.Fail(string.Join("\n\n", failures));
            }
        }

        private static string LoadSingleMethodBody(
            string testsRoot,
            string fileName,
            string methodName,
            List<string> failures
        )
        {
            string[] matches;
            try
            {
                matches = Directory.GetFiles(testsRoot, fileName, SearchOption.AllDirectories);
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                failures.Add(
                    $"[{fileName}:{methodName}] failed to locate file under {testsRoot}: {ex.Message}"
                );
                return null;
            }

            if (matches.Length == 0)
            {
                failures.Add(
                    $"[{fileName}:{methodName}] owning file not found under {testsRoot} — "
                        + "the helper may have been renamed or moved. Update this audit's "
                        + "targets table to match."
                );
                return null;
            }

            if (1 < matches.Length)
            {
                failures.Add(
                    $"[{fileName}:{methodName}] ambiguous: multiple files named {fileName} found under "
                        + $"{testsRoot} ({string.Join(", ", matches)}). Consolidate or rename."
                );
                return null;
            }

            if (!TryReadSource(matches[0], fileName, failures, out string source))
            {
                return null;
            }

            string body = ExtractMethodBody(source, methodName);
            if (body == null)
            {
                failures.Add(
                    $"[{fileName}:{methodName}] method body not found. The helper may have been "
                        + "renamed; update this audit's targets table."
                );
            }
            return body;
        }

        /// <summary>
        /// Consistency audit: every helper whitelisted in
        /// <see cref="FlushEquivalentExpressions"/> (other than the direct flush
        /// expression itself) must appear in exactly one of
        /// <see cref="CentralizedTerminalTargets"/> or
        /// <see cref="CentralizedDelegatingTargets"/>, so the audit test
        /// <c>CentralizedClearHelpersActuallyFlush</c> actually guards every
        /// accepted helper. Without this cross-check, a future author can add a
        /// new accepted helper to <c>FlushEquivalentExpressions</c> and forget
        /// to register it with the audit — and the new helper's flush behavior
        /// then has no regression test.
        /// </summary>
        [Test]
        public void FlushEquivalentExpressionsAreFullyCoveredByCentralizedAudit()
        {
            List<string> failures = new();
            foreach (string expression in FlushEquivalentExpressions)
            {
                if (string.Equals(expression, FlushCallExpression, StringComparison.Ordinal))
                {
                    // A direct flush has no helper implementation to audit.
                    continue;
                }

                string methodName = ExtractMethodNameFromExpression(expression);
                if (methodName == null)
                {
                    failures.Add(
                        $"FlushEquivalentExpressions contains '{expression}' which does not "
                            + "look like a method-call token (expected 'Name(' or 'Type.Name(')."
                    );
                    continue;
                }

                int terminalMatches = 0;
                foreach (
                    (
                        string FileName,
                        string MethodName
                    ) centralizedTerminalTargetsElement in CentralizedTerminalTargets
                )
                {
                    if (
                        string.Equals(
                            centralizedTerminalTargetsElement.MethodName,
                            methodName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        terminalMatches++;
                    }
                }

                int delegatingMatches = 0;
                foreach (
                    (
                        string FileName,
                        string MethodName,
                        string[] AcceptedDelegates
                    ) centralizedDelegatingTargetsElement in CentralizedDelegatingTargets
                )
                {
                    if (
                        string.Equals(
                            centralizedDelegatingTargetsElement.MethodName,
                            methodName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        delegatingMatches++;
                    }
                }

                int total = terminalMatches + delegatingMatches;
                if (total == 0)
                {
                    failures.Add(
                        $"FlushEquivalentExpressions contains '{expression}' but no entry in "
                            + "CentralizedTerminalTargets / CentralizedDelegatingTargets audits "
                            + "it. Add the helper to the appropriate tier so the audit test "
                            + "CentralizedClearHelpersActuallyFlush verifies its flush behavior."
                    );
                }
                else if (1 < total)
                {
                    failures.Add(
                        $"FlushEquivalentExpressions contains '{expression}' and it matches "
                            + $"{total} entries across the terminal + delegating audit tables "
                            + "(expected exactly 1). Consolidate the audit entry so each helper "
                            + "is classified as either a terminal or a delegating flush root, "
                            + "not both."
                    );
                }
            }

            if (0 < failures.Count)
            {
                Assert.Fail(string.Join("\n\n", failures));
            }
        }

        private static string ExtractMethodNameFromExpression(string expression)
        {
            if (
                string.IsNullOrEmpty(expression)
                || !expression.EndsWith("(", StringComparison.Ordinal)
            )
            {
                return null;
            }
            string withoutParen = expression.Substring(0, expression.Length - 1);
            int lastDot = withoutParen.LastIndexOf('.');
            return lastDot < 0 ? withoutParen : withoutParen.Substring(lastDot + 1);
        }

        /// <summary>
        /// Tripwire-coverage contract: every test fixture in
        /// <c>Tests/Editor/AssetProcessors/</c> that registers a <c>[SetUp]</c>
        /// method (or overrides <c>BaseSetUp</c>) and passes the
        /// <see cref="FileIsInAssetContext"/> gate must call
        /// <c>AssetPostprocessorTestHandlers.AssertCleanAndClearAll(</c> in that
        /// body. The call pins cross-fixture handler-static pollution to its
        /// true source rather than rolling it forward invisibly — see the
        /// canonical rationale on <c>AssertCleanAndClearAll</c>'s XML doc.
        ///
        /// Without this contract, future asset-processor fixtures can silently
        /// omit the tripwire (as happened with <c>LlmArtifactCleanerTests</c>
        /// before round 9 of the #234 review caught it). Scoped to asset-context
        /// files so unrelated fixtures aren't forced to call the helper.
        /// </summary>
        [Test]
        public void AssetContextFixturesCallCrossFixturePollutionTripwire()
        {
            string testsRoot = ResolveEditorTestsRoot();
            if (string.IsNullOrEmpty(testsRoot))
            {
                Assert.Inconclusive(
                    "Could not locate Tests/Editor/ on disk; skipping the tripwire-coverage contract."
                );
                return;
            }

            string[] testFiles;
            try
            {
                testFiles = Directory.GetFiles(testsRoot, "*.cs", SearchOption.AllDirectories);
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Assert.Inconclusive(
                    $"Failed to enumerate {testsRoot}: {ex.Message}. Skipping the tripwire-coverage contract."
                );
                return;
            }

            const string TripwireCall = "AssetPostprocessorTestHandlers.AssertCleanAndClearAll(";
            const string BaseSetUpCall = "base.BaseSetUp(";
            const string TestAttributeToken = "[Test]";
            /*
                Deferral-internal tests do not touch handler statics; processor fixtures whose assets reach
                handlers still need the pollution check.
            */
            string[] handlerInvolvementTokens =
            {
                "AssetPostprocessorTestHandlers",
                "DetectAssetChanged",
                "DetectAssetChangeProcessor",
                "LlmArtifactCleaner",
                "SpriteLabelProcessor",
            };

            List<string> failures = new();
            foreach (string path in testFiles)
            {
                string fileName = Path.GetFileName(path);
                if (IsContractTestSelf(fileName))
                {
                    continue;
                }

                if (!TryReadSource(path, fileName, failures, out string source))
                {
                    continue;
                }

                if (!FileIsInAssetContext(source))
                {
                    continue;
                }

                if (source.IndexOf(TestAttributeToken, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                // Deferral-only fixtures have no handler-static pollution for this check to observe.
                bool handlerInvolved = false;
                foreach (string handlerInvolvementTokensElement in handlerInvolvementTokens)
                {
                    if (
                        0
                        <= source.IndexOf(handlerInvolvementTokensElement, StringComparison.Ordinal)
                    )
                    {
                        handlerInvolved = true;
                        break;
                    }
                }

                if (!handlerInvolved)
                {
                    continue;
                }

                // Accept both per-test SetUp and inherited BaseSetUp conventions.
                int tripwireIndex = IndexOfOutsideLiteral(source, TripwireCall, 0);
                if (tripwireIndex < 0)
                {
                    failures.Add(
                        $"[{fileName}] is an asset-context test fixture that interacts with "
                            + "handler statics but does not call "
                            + "AssetPostprocessorTestHandlers.AssertCleanAndClearAll() anywhere. "
                            + "Add the call so it precedes base.BaseSetUp( — or anywhere in the "
                            + "fixture's [SetUp] / BaseSetUp body if no base chain exists — so "
                            + "cross-fixture handler-state pollution is pinned to its true source. "
                            + "See AssertCleanAndClearAll's XML doc for the canonical rationale."
                    );
                    continue;
                }

                /*
                    Check pollution before base setup can change attribution, ignoring base-call text inside
                    literals and comments.
                */
                int baseSetUpIndex = IndexOfOutsideLiteral(source, BaseSetUpCall, 0);
                if (0 <= baseSetUpIndex && baseSetUpIndex < tripwireIndex)
                {
                    failures.Add(
                        $"[{fileName}] calls AssetPostprocessorTestHandlers.AssertCleanAndClearAll() "
                            + "AFTER base.BaseSetUp(). The tripwire must precede base.BaseSetUp( so "
                            + "prior-fixture pollution is snapshotted before the base class performs "
                            + "any configuration that could shift attribution. Move the "
                            + "AssetPostprocessorTestHandlers.AssertCleanAndClearAll() call to "
                            + "precede base.BaseSetUp()."
                    );
                }
            }

            if (0 < failures.Count)
            {
                Assert.Fail(string.Join("\n\n", failures));
            }
        }

        /// <summary>
        /// Reflection contract: every type in the test assemblies that declares at
        /// least one method with <see cref="DetectAssetChangedAttribute"/> must also
        /// expose a <c>public static void Clear()</c> method so the centralized
        /// helper can clear its state. Without this, a future author who adds a new
        /// handler without a Clear() would silently create a new cross-fixture
        /// pollution vector.
        /// </summary>
        [Test]
        public void AllTestHandlerDoublesExposeClearMethod()
        {
            TypeCache.MethodCollection methods =
                TypeCache.GetMethodsWithAttribute<DetectAssetChangedAttribute>();

            HashSet<Type> candidateTypes = new();
            foreach (MethodInfo method in methods)
            {
                if (method == null)
                {
                    continue;
                }

                Type declaringType = method.DeclaringType;
                if (declaringType == null)
                {
                    continue;
                }

                Assembly assembly = declaringType.Assembly;
                if (assembly == null)
                {
                    continue;
                }

                string assemblyName = assembly.GetName().Name;
                if (
                    string.IsNullOrEmpty(assemblyName)
                    || !assemblyName.StartsWith(
                        "WallstopStudios.UnityHelpers.Tests",
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                candidateTypes.Add(declaringType);
            }

            List<string> offenders = new();
            foreach (Type type in candidateTypes)
            {
                MethodInfo clearMethod = type.GetMethod(
                    "Clear",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null
                );
                if (clearMethod == null || clearMethod.ReturnType != typeof(void))
                {
                    offenders.Add(type.FullName);
                }
            }

            if (0 < offenders.Count)
            {
                Assert.Fail(
                    "The following test handler types declare [DetectAssetChanged] methods but "
                        + "do not expose a `public static void Clear()` method:\n"
                        + string.Join("\n", offenders.Select(o => $"  - {o}"))
                        + "\nAdd a Clear() method so the centralized AssetPostprocessorTestHandlers "
                        + "helper can clear their state and prevent cross-fixture pollution."
                );
            }
        }

        /// <summary>
        /// Cross-check that the centralized discovery logic (in
        /// <c>AssetPostprocessorTestHandlers</c>) has not silently dropped any of the
        /// known-good handler types — e.g. due to an assembly-name filter that is too
        /// narrow. The expected set below is derived from the current test assemblies;
        /// if a future change legitimately renames or removes one of these, update
        /// this test in the same commit.
        /// </summary>
        [Test]
        public void AssetPostprocessorTestHandlersCoversAllDiscoveredTypes()
        {
            IReadOnlyList<Type> discovered = AssetPostprocessorTestHandlers.DiscoveredHandlerTypes;
            Assert.IsNotEmpty(
                discovered,
                "AssetPostprocessorTestHandlers.DiscoveredHandlerTypes returned empty. "
                    + "TypeCache may not have found any [DetectAssetChanged] methods in the test assemblies."
            );

            string[] expectedTypeNames =
            {
                "TestPrefabAssetChangeHandler",
                "TestSceneAssetChangeHandler",
                "TestNestedPrefabHandler",
                "TestCombinedSearchHandler",
                "TestDetectAssetChangeHandler",
                "TestDetailedSignatureHandler",
                "TestStaticAssetChangeHandler",
                "TestMultiAttributeHandler",
                "TestReentrantHandler",
                "TestLoopingHandler",
                "TestAssignableAssetChangeHandler",
                "TestExceptionThrowingHandler",
            };

            HashSet<string> discoveredNames = new(StringComparer.Ordinal);
            for (int i = 0; i < discovered.Count; i++)
            {
                discoveredNames.Add(discovered[i].Name);
            }

            List<string> missing = new();
            foreach (string expectedTypeNamesElement in expectedTypeNames)
            {
                if (!discoveredNames.Contains(expectedTypeNamesElement))
                {
                    missing.Add(expectedTypeNamesElement);
                }
            }

            if (0 < missing.Count)
            {
                Assert.Fail(
                    "AssetPostprocessorTestHandlers.DiscoveredHandlerTypes is missing expected handlers:\n"
                        + string.Join("\n", missing.Select(m => $"  - {m}"))
                        + "\nCheck the discovery filter (e.g. assembly-name prefix, Clear() method filter). "
                        + "If a handler was intentionally renamed or removed, update this test in the same commit."
                );
            }
        }

        private static string ResolveEditorTestsRoot()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return null;
            }

            string[] candidates =
            {
                Path.Combine(projectRoot, "Tests", "Editor"),
                Path.Combine(
                    projectRoot,
                    "Packages",
                    "com.wallstop-studios.unity-helpers",
                    "Tests",
                    "Editor"
                ),
            };

            foreach (string candidatesElement in candidates)
            {
                if (Directory.Exists(candidatesElement))
                {
                    return candidatesElement;
                }
            }

            return null;
        }

        private static bool IsContractTestSelf(string fileName)
        {
            /*
                The contract test itself intentionally references the tokens it
                forbids, so exclude it from its own scan.
            */
            return string.Equals(
                fileName,
                "AssetPostprocessorContractTests.cs",
                StringComparison.Ordinal
            );
        }

        private static bool TryReadSource(
            string path,
            string fileName,
            List<string> failures,
            out string source
        )
        {
            try
            {
                source = File.ReadAllText(path);
                return true;
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                failures.Add($"[{fileName}] failed to read source: {ex.Message}");
                source = null;
                return false;
            }
        }

        // Literal or commented-out flush calls must not satisfy the teardown contract.
        private static bool ContainsDirectFlush(string body)
        {
            foreach (string flushEquivalentExpressionsElement in FlushEquivalentExpressions)
            {
                if (0 <= IndexOfOutsideLiteral(body, flushEquivalentExpressionsElement, 0))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsHandlerClear(string body)
        {
            /*
                Only executable Clear calls count; doc examples and string literals cannot create cleanup
                obligations.
            */
            string codeOnly = StripLiteralsAndComments(body);
            if (HandlerClearPattern.IsMatch(codeOnly))
            {
                return true;
            }

            foreach (string handlerClearEquivalentCallsElement in HandlerClearEquivalentCalls)
            {
                if (0 <= IndexOfOutsideLiteral(body, handlerClearEquivalentCallsElement, 0))
                {
                    return true;
                }
            }

            return false;
        }

        // Preserve offsets and newlines while hiding literal and comment content from later matching.
        private static string StripLiteralsAndComments(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source ?? string.Empty;
            }

            StringBuilder builder = new(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                int skipped = SkipLiteralOrComment(source, i);
                if (skipped != i)
                {
                    for (int f = i; f < skipped; f++)
                    {
                        char c = source[f];
                        /*
                            Keep newlines so line numbers in any downstream
                            diagnostic remain accurate; blank everything else.
                        */
                        builder.Append(c == '\n' || c == '\r' ? c : ' ');
                    }
                    i = skipped;
                    continue;
                }

                builder.Append(source[i]);
                i++;
            }

            return builder.ToString();
        }

        private static bool ContainsAssetMutation(string body)
        {
            foreach (string assetMutationTokensElement in AssetMutationTokens)
            {
                if (0 <= IndexOfOutsideLiteral(body, assetMutationTokensElement, 0))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool FileIsInAssetContext(string source)
        {
            foreach (string assetContextTokensElement in AssetContextTokens)
            {
                if (0 <= source.IndexOf(assetContextTokensElement, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static Type[] DiscoverEditorAssetPostprocessorTypes()
        {
            TypeCache.TypeCollection candidates =
                TypeCache.GetTypesDerivedFrom<AssetPostprocessor>();
            List<Type> inEditorAssembly = new();
            for (int i = 0; i < candidates.Count; i++)
            {
                Type candidate = candidates[i];
                if (candidate == null || candidate.IsAbstract)
                {
                    continue;
                }

                Assembly assembly = candidate.Assembly;
                if (assembly == null)
                {
                    continue;
                }

                string name = assembly.GetName().Name;
                foreach (string editorAssemblyNamesElement in EditorAssemblyNames)
                {
                    if (string.Equals(name, editorAssemblyNamesElement, StringComparison.Ordinal))
                    {
                        inEditorAssembly.Add(candidate);
                        break;
                    }
                }
            }

            return inEditorAssembly.ToArray();
        }

        private static IReadOnlyList<MethodInfo> FindInspectedCallbacks(Type processorType)
        {
            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;
            MethodInfo[] methods = processorType.GetMethods(flags);
            List<MethodInfo> matches = new();
            foreach (MethodInfo method in methods)
            {
                if (method == null)
                {
                    continue;
                }

                if (InspectedCallbackNames.Contains(method.Name))
                {
                    matches.Add(method);
                }
            }

            return matches;
        }

        /*
            The asset database may not have imported a fresh package yet; search filesystem candidates,
            including partial-class siblings.
        */
        private static IReadOnlyList<string> ResolveSourcePaths(Type processorType)
        {
            string exactName = processorType.Name + ".cs";
            string partialPattern = processorType.Name + ".*.cs";
            List<string> searchRoots = new();

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                searchRoots.Add(Path.Combine(projectRoot, "Editor", "AssetProcessors"));
                searchRoots.Add(Path.Combine(projectRoot, "Editor"));
                searchRoots.Add(
                    Path.Combine(
                        projectRoot,
                        "Packages",
                        "com.wallstop-studios.unity-helpers",
                        "Editor",
                        "AssetProcessors"
                    )
                );
                searchRoots.Add(
                    Path.Combine(projectRoot, "Packages", "com.wallstop-studios.unity-helpers")
                );
                searchRoots.Add(projectRoot);
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<string> paths = new();
            foreach (string root in searchRoots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    continue;
                }

                TryAppendMatches(root, exactName, paths, seen);
                TryAppendMatches(root, partialPattern, paths, seen);
                if (0 < paths.Count)
                {
                    break;
                }
            }

            return paths;
        }

        private static void TryAppendMatches(
            string root,
            string pattern,
            List<string> paths,
            HashSet<string> seen
        )
        {
            try
            {
                string[] matches = Directory.GetFiles(root, pattern, SearchOption.AllDirectories);
                foreach (string match in matches)
                {
                    if (!string.IsNullOrEmpty(match) && seen.Add(match))
                    {
                        paths.Add(match);
                    }
                }
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Debug.LogWarning(
                    $"AssetPostprocessorContractTests: failed to search {root} with pattern {pattern}: {ex.Message}"
                );
            }
        }

        private static MethodBodySearchResult TryExtractBodyAcrossFiles(
            IReadOnlyList<string> sourcePaths,
            string methodName
        )
        {
            for (int i = 0; i < sourcePaths.Count; i++)
            {
                string path = sourcePaths[i];
                string sourceText;
                try
                {
                    sourceText = File.ReadAllText(path);
                }
                catch (Exception ex)
                    when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    return MethodBodySearchResult.ReadError($"{path}: {ex.Message}");
                }

                string body = ExtractMethodBody(sourceText, methodName);
                if (body != null)
                {
                    return MethodBodySearchResult.Found(body, path);
                }
            }

            return MethodBodySearchResult.NotFound();
        }

        private static IEnumerable<string> EnumerableSelect(
            IReadOnlyList<string> paths,
            Func<string, string> selector
        )
        {
            for (int i = 0; i < paths.Count; i++)
            {
                yield return selector(paths[i]);
            }
        }

        /*
            Require a declaration prefix so nameof, attributes, docs, and log text cannot masquerade as
            callbacks.
        */
        private static string ExtractMethodBody(string source, string methodName)
        {
            Regex signature = new(
                "\\b(?:public|internal|private|protected)(?:\\s+(?:public|internal|private|protected))?"
                    + "(?:\\s+(?:static|override|async|sealed|new|virtual|unsafe|extern|partial))*"
                    + "\\s+(?:void|Task|Task<[^>]+>|ValueTask|ValueTask<[^>]+>|IEnumerator|IEnumerable|IAsyncEnumerable<[^>]+>)"
                    + "\\s+"
                    + Regex.Escape(methodName)
                    + "\\s*\\(",
                RegexOptions.CultureInvariant | RegexOptions.Singleline
            );
            Match match = signature.Match(source);
            while (match.Success)
            {
                // The declaration match ends at the opening parenthesis.
                int openParen = match.Index + match.Length - 1;
                int closeParen = FindMatchingParen(source, openParen);
                if (closeParen < 0)
                {
                    return null;
                }

                int cursor = SkipWhitespaceAndConstraints(source, closeParen + 1);
                if (source.Length <= cursor)
                {
                    return null;
                }

                char lead = source[cursor];
                if (lead == '{')
                {
                    int end = FindMatchingBrace(source, cursor);
                    if (cursor < end)
                    {
                        return source.Substring(cursor + 1, end - cursor - 1);
                    }
                }
                else if (lead == '=' && cursor + 1 < source.Length && source[cursor + 1] == '>')
                {
                    int bodyStart = cursor + 2;
                    int bodyEnd = FindStatementTerminator(source, bodyStart);
                    if (bodyStart <= bodyEnd)
                    {
                        return source.Substring(bodyStart, bodyEnd - bodyStart);
                    }
                }

                match = signature.Match(source, match.Index + match.Length);
            }

            return null;
        }

        private static int SkipWhitespaceAndConstraints(string source, int start)
        {
            int i = start;
            while (i < source.Length)
            {
                char c = source[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (
                    c == 'w'
                    && i + 5 < source.Length
                    && source[i + 1] == 'h'
                    && source[i + 2] == 'e'
                    && source[i + 3] == 'r'
                    && source[i + 4] == 'e'
                    && char.IsWhiteSpace(source[i + 5])
                )
                {
                    int stop = i;
                    while (stop < source.Length)
                    {
                        char s = source[stop];
                        if (s == '{' || s == ';')
                        {
                            break;
                        }
                        if (s == '=' && stop + 1 < source.Length && source[stop + 1] == '>')
                        {
                            break;
                        }

                        stop++;
                    }

                    i = stop;
                    continue;
                }

                break;
            }

            return i;
        }

        // Literal semicolons and nested expressions must not terminate the method body.
        private static int FindStatementTerminator(string source, int start)
        {
            int parenDepth = 0;
            int braceDepth = 0;
            int bracketDepth = 0;
            int i = start;
            while (i < source.Length)
            {
                int skipped = SkipLiteralOrComment(source, i);
                if (skipped != i)
                {
                    i = skipped;
                    continue;
                }

                char c = source[i];
                if (c == '(')
                {
                    parenDepth++;
                }
                else if (c == ')')
                {
                    parenDepth--;
                }
                else if (c == '{')
                {
                    braceDepth++;
                }
                else if (c == '}')
                {
                    braceDepth--;
                }
                else if (c == '[')
                {
                    bracketDepth++;
                }
                else if (c == ']')
                {
                    bracketDepth--;
                }
                else if (c == ';' && parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
                {
                    return i;
                }

                i++;
            }

            return -1;
        }

        private static int FindMatchingBrace(string source, int openIndex)
        {
            int depth = 0;
            int i = openIndex;
            while (i < source.Length)
            {
                int skipped = SkipLiteralOrComment(source, i);
                if (skipped != i)
                {
                    i = skipped;
                    continue;
                }

                char c = source[i];
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }

                i++;
            }

            return -1;
        }

        // Operations inside a scheduled deferral are permitted by this contract.
        private static string StripDeferralSchedules(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return body;
            }

            StringBuilder result = new(body.Length);
            int index = 0;
            while (index < body.Length)
            {
                int start = IndexOfOutsideLiteral(body, DeferralCallExpression, index);
                if (start < 0)
                {
                    result.Append(body, index, body.Length - index);
                    break;
                }

                result.Append(body, index, start - index);
                int openParen = start + DeferralCallExpression.Length - 1;
                int closeParen = FindMatchingParen(body, openParen);
                if (closeParen < 0)
                {
                    // Malformed; bail out and keep the rest as-is.
                    result.Append(body, start, body.Length - start);
                    break;
                }

                index = closeParen + 1;
            }

            return result.ToString();
        }

        private static int IndexOfOutsideLiteral(string haystack, string needle, int start)
        {
            int i = start;
            while (i < haystack.Length)
            {
                int skipped = SkipLiteralOrComment(haystack, i);
                if (skipped != i)
                {
                    i = skipped;
                    continue;
                }

                if (
                    i + needle.Length <= haystack.Length
                    && string.CompareOrdinal(haystack, i, needle, 0, needle.Length) == 0
                )
                {
                    return i;
                }

                i++;
            }

            return -1;
        }

        private static int FindMatchingParen(string source, int openIndex)
        {
            int depth = 0;
            int i = openIndex;
            while (i < source.Length)
            {
                int skipped = SkipLiteralOrComment(source, i);
                if (skipped != i)
                {
                    i = skipped;
                    continue;
                }

                char c = source[i];
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }

                i++;
            }

            return -1;
        }

        /// <summary>
        /// Advances past a literal or comment region beginning at <paramref name="i"/>.
        /// </summary>
        /// <param name="source">The source text being scanned.</param>
        /// <param name="i">The index to inspect.</param>
        /// <returns>
        /// The index after the region, or <paramref name="i"/> when no region starts there.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Covers line and block comments, char literals with their escapes, regular and verbatim
        /// strings, and raw string literals of any opening quote count, which close on the same
        /// number of quotes.
        /// </para>
        /// <para>
        /// Interpolation braces are not parsed. The AssetPostprocessor bodies this scans never
        /// place an interpolation expression containing another literal across lines, so the
        /// simplification is safe for this contract.
        /// </para>
        /// </remarks>
        private static int SkipLiteralOrComment(string source, int i)
        {
            if (source.Length <= i)
            {
                return i;
            }

            char c = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                int j = i + 2;
                while (j < source.Length && source[j] != '\n')
                {
                    j++;
                }

                return j;
            }

            if (c == '/' && next == '*')
            {
                int j = i + 2;
                while (j + 1 < source.Length)
                {
                    if (source[j] == '*' && source[j + 1] == '/')
                    {
                        return j + 2;
                    }

                    j++;
                }

                return source.Length;
            }

            if (c == '\'')
            {
                int j = i + 1;
                while (j < source.Length)
                {
                    char cj = source[j];
                    if (cj == '\\' && j + 1 < source.Length)
                    {
                        j += 2;
                        continue;
                    }
                    if (cj == '\'')
                    {
                        return j + 1;
                    }

                    j++;
                }

                return source.Length;
            }

            // Raw strings close with the same quote-run length that opened them.
            if (c == '"' && next == '"')
            {
                int quoteRun = 0;
                while (i + quoteRun < source.Length && source[i + quoteRun] == '"')
                {
                    quoteRun++;
                }

                if (3 <= quoteRun)
                {
                    int j = i + quoteRun;
                    while (j <= source.Length - quoteRun)
                    {
                        bool closes = true;
                        for (int k = 0; k < quoteRun; k++)
                        {
                            if (source[j + k] != '"')
                            {
                                closes = false;
                                break;
                            }
                        }

                        if (closes)
                        {
                            return j + quoteRun;
                        }

                        j++;
                    }

                    return source.Length;
                }

                // Two adjacent quotes outside a string form an empty regular literal.
            }

            // Doubled quotes inside a verbatim string are escaped content.
            bool isVerbatim =
                (c == '@' && next == '"')
                || (c == '$' && next == '@' && i + 2 < source.Length && source[i + 2] == '"')
                || (c == '@' && next == '$' && i + 2 < source.Length && source[i + 2] == '"');
            if (isVerbatim)
            {
                int quoteIndex = i + (c == '@' && next == '"' ? 1 : 2);
                int j = quoteIndex + 1;
                while (j < source.Length)
                {
                    char cj = source[j];
                    if (cj == '"')
                    {
                        if (j + 1 < source.Length && source[j + 1] == '"')
                        {
                            j += 2;
                            continue;
                        }

                        return j + 1;
                    }

                    j++;
                }

                return source.Length;
            }

            if (c == '$' && next == '"')
            {
                return SkipRegularString(source, i + 1);
            }

            if (c == '"')
            {
                return SkipRegularString(source, i);
            }

            return i;
        }

        private static int SkipRegularString(string source, int openQuoteIndex)
        {
            int j = openQuoteIndex + 1;
            while (j < source.Length)
            {
                char cj = source[j];
                if (cj == '\\' && j + 1 < source.Length)
                {
                    j += 2;
                    continue;
                }
                if (cj == '"')
                {
                    return j + 1;
                }
                if (cj == '\n')
                {
                    /*
                        Unterminated regular string; bail to avoid walking the whole
                        remainder of the file.
                    */
                    return j;
                }

                j++;
            }

            return source.Length;
        }

        private static List<string> FindForbiddenTokens(string body)
        {
            List<string> fired = new();
            foreach (string token in ForbiddenTokens)
            {
                if (0 <= body.IndexOf(token, StringComparison.Ordinal))
                {
                    fired.Add(token.Trim());
                }
            }

            if (BareInstantiatePattern.IsMatch(body))
            {
                fired.Add("Instantiate(");
            }

            return fired;
        }

        private enum BodySearchStatus
        {
            NotFound,
            Found,
            ReadError,
        }

        private readonly struct MethodBodySearchResult
        {
            public BodySearchStatus Status { get; }
            public string Body { get; }
            public string SourcePath { get; }
            public string Detail { get; }

            private MethodBodySearchResult(
                BodySearchStatus status,
                string body,
                string sourcePath,
                string detail
            )
            {
                Status = status;
                Body = body;
                SourcePath = sourcePath;
                Detail = detail;
            }

            public static MethodBodySearchResult NotFound() =>
                new(BodySearchStatus.NotFound, null, null, null);

            public static MethodBodySearchResult Found(string body, string sourcePath) =>
                new(BodySearchStatus.Found, body, sourcePath, null);

            public static MethodBodySearchResult ReadError(string detail) =>
                new(BodySearchStatus.ReadError, null, null, detail);
        }
    }
}
