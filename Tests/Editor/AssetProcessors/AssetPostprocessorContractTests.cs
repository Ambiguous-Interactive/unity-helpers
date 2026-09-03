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

        /*
            Canonical Unity AssetPostprocessor callback names we inspect. Any override
            found with one of these names is treated as part of the asset-import phase.
        */
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

        /*
            Forbidden substrings. Any occurrence of these tokens in an inspected method
            body (outside AssetPostprocessorDeferral.Schedule(...)) fails the contract.
            The generic / non-generic pairs (.GetComponents< / .GetComponents(,
            .AddComponent< / .AddComponent() are BOTH listed — the non-generic overload
            is equally problematic during the asset-import phase, and omitting it would
            let `GetComponents(typeof(T), buffer)` slip through.
        */
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

        /*
            Word-boundary pattern for bare `Instantiate(` calls. This catches plain
            `Instantiate(...)` without producing false positives for identifiers that end
            in "Instantiate" (e.g. `preInstantiate(` or `ReInstantiate(`). We tolerate
            the unlikely case of a user-defined method literally named `Instantiate` —
            postprocessor callbacks shouldn't contain such identifiers anyway.
        */
        private static readonly Regex BareInstantiatePattern = new(
            "\\bInstantiate\\s*\\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        private const string DeferralCallExpression = "AssetPostprocessorDeferral.Schedule(";

        /*
            Method names whose bodies, if they clear handler test doubles, must also
            drain pending AssetPostprocessor deferrals directly. Covers the canonical
            NUnit lifecycle names plus the package's `ClearTestState`/`BaseSetUp`
            conventions. `CommonOneTimeSetUp`/`CommonOneTimeTearDown` are the
            package-wide naming convention (see CommonTestBase) for methods annotated
            with `[OneTimeSetUp]`/`[OneTimeTearDown]`, so the scanner must treat them
            identically to the NUnit canonical names — otherwise the contract
            silently skips every override that uses the package convention.
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
            Subset of `TeardownMethodNames` that must flush whenever they perform any
            asset mutation (even without a Test*Handler.Clear() call). Per-test
            SetUp/TearDown are handled by the handler-clear rule; per-fixture
            OneTimeSetUp/OneTimeTearDown leak drains into the next fixture if they
            don't flush. Include the package-convention `CommonOneTime*` variants
            because CommonTestBase marks them with NUnit's `[OneTimeSetUp]`/
            `[OneTimeTearDown]` attributes — overrides in fixtures use the convention
            name and would otherwise slip through the scanner.
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
            Asset-mutation tokens that, when present in a OneTime* method body, imply
            the method schedules an AssetPostprocessor drain and therefore must flush
            before returning. Intentionally restricted to DIRECT AssetDatabase.* and
            batch-helper calls — helper-method wrappers (CleanupTestFolders,
            CleanupDeferredAssetsAndFolders, EnsureTestFolder, EnsureHandlerAsset) are
            NOT included because: (a) the contract has one canonical shape for "this
            method mutates assets" — the direct AssetDatabase.* token — and reusing
            that shape keeps false-positive cascades off every helper-calling fixture;
            (b) fixtures that rely on those helpers without a direct flush rely on
            their own teardown discipline (base chain, fixture-scope flushes) rather
            than the OneTime* source scan. Callers that want a new helper covered
            should add an explicit direct mutation in the OneTime* body instead.
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
            Gate tokens: files that reference any of these are in scope for the
            OneTime* asset-mutation contract. Scoped narrowly to files that already
            interact with the asset-postprocessor machinery, to avoid forcing every
            sprite/texture test fixture to flush when they don't interact with the
            handler statics that pollution affects. `LlmArtifactCleaner` and
            `SpriteLabelProcessor` are production processors that schedule drains
            when tests mutate the assets they observe — their fixtures need the
            asset-context gate to route through the OneTime flush contract even
            though neither processor goes through the DetectAssetChangeProcessor
            pipeline.
        */
        private static readonly string[] AssetContextTokens =
        {
            "AssetPostprocessorDeferral",
            "DetectAssetChangeProcessor",
            "LlmArtifactCleaner",
            "SpriteLabelProcessor",
        };

        /*
            Handler-double Clear() patterns that, when present in a teardown-ish method
            body, require a corresponding flush (directly or via base chain). Matched
            as "<Identifier>.Clear()" where the identifier starts with "Test" and ends
            with "Handler", to avoid false positives on arbitrary `.Clear()` calls
            (e.g. `List.Clear()`, `PendingAssetChanges.Clear()`).
        */
        private static readonly Regex HandlerClearPattern = new(
            "\\bTest\\w*Handler\\.Clear\\s*\\(\\s*\\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        /*
            Centralized helper entry points that clear handler state (transitively or
            directly). Treated equivalently to the bare `Test*Handler.Clear()` regex
            so the contract keeps enforcing the flush discipline when authors adopt
            the helper instead of hand-rolling per-handler Clear() calls. `ClearTestState(`
            is included because it is the virtual base-class method that fixtures call
            from their TearDown bodies, and its own body clears handler state via the
            helper.
        */
        private static readonly string[] HandlerClearEquivalentCalls =
        {
            "AssetPostprocessorTestHandlers.FlushAndClearAll(",
            "AssetPostprocessorTestHandlers.AssertCleanAndClearAll(",
            "ClearTestState(",
        };

        private const string FlushCallExpression = "AssetPostprocessorDeferral.FlushForTesting(";

        /*
            Expressions that are accepted as equivalent to a direct FlushForTesting()
            call. The centralized helpers flush internally before clearing, so calling
            them satisfies the "drain before clear" invariant without a second explicit
            flush. `ClearTestState(` is included because its body already routes through
            the helper.
        */
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
                    // No override of a canonical Unity callback -> nothing to scan.
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
            for (int i = 0; i < testFiles.Length; i++)
            {
                string path = testFiles[i];
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
            for (int i = 0; i < testFiles.Length; i++)
            {
                string path = testFiles[i];
                string fileName = Path.GetFileName(path);
                if (IsContractTestSelf(fileName))
                {
                    continue;
                }

                if (!TryReadSource(path, fileName, failures, out string source))
                {
                    continue;
                }

                /*
                    Gate: only files that already touch asset postprocessors or
                    mutate asset state are expected to carry this discipline.
                */
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
            (owning-file-name, method-name) pairs for terminal flush helpers. The
            file-name filter is a narrow whitelist: the scanner is allowed to
            locate the method body ONLY in the file it belongs to, otherwise an
            unrelated file whose contents happen to contain a method with the
            same name (e.g. a mock or another helper) could satisfy the test
            spuriously.
        */
        private static readonly (string FileName, string MethodName)[] CentralizedTerminalTargets =
        {
            ("AssetPostprocessorTestHandlers.cs", "FlushAndClearAll"),
            ("AssetPostprocessorTestHandlers.cs", "AssertCleanAndClearAll"),
        };

        /*
            Delegating helpers: each must call ONE of AcceptedDelegates (which
            name a terminal helper or the direct flush expression).
        */
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
            for (int i = 0; i < terminalTargets.Length; i++)
            {
                (string fileName, string methodName) = terminalTargets[i];
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

            for (int i = 0; i < delegatingTargets.Length; i++)
            {
                (string fileName, string methodName, string[] accepted) = delegatingTargets[i];
                string body = LoadSingleMethodBody(testsRoot, fileName, methodName, failures);
                if (body == null)
                {
                    continue;
                }

                bool satisfied = false;
                for (int t = 0; t < accepted.Length; t++)
                {
                    if (0 <= IndexOfOutsideLiteral(body, accepted[t], 0))
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
            for (int i = 0; i < FlushEquivalentExpressions.Length; i++)
            {
                string expression = FlushEquivalentExpressions[i];
                if (string.Equals(expression, FlushCallExpression, StringComparison.Ordinal))
                {
                    /*
                        The direct flush expression is not a helper; it needs no
                        audit entry.
                    */
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
                for (int t = 0; t < CentralizedTerminalTargets.Length; t++)
                {
                    if (
                        string.Equals(
                            CentralizedTerminalTargets[t].MethodName,
                            methodName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        terminalMatches++;
                    }
                }

                int delegatingMatches = 0;
                for (int d = 0; d < CentralizedDelegatingTargets.Length; d++)
                {
                    if (
                        string.Equals(
                            CentralizedDelegatingTargets[d].MethodName,
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

        /*
            Extracts the method name from a FlushEquivalentExpressions entry.
            Accepts bare "Name(" or fully-qualified "Namespace.Type.Name("; returns
            the final identifier before the paren. Returns null if the expression
            does not end in "(".
        */
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
                Tokens that indicate the fixture actually interacts with the
                handler-static tracking system (and therefore needs the
                tripwire). A file that touches AssetPostprocessorDeferral
                internals but never goes near the handler statics (e.g.
                AssetPostprocessorDeferralTests) is correctly scoped out by
                this gate — the tripwire guards handler pollution, not
                deferral-internal behavior. `LlmArtifactCleaner` and
                `SpriteLabelProcessor` are production processors whose fixtures
                mutate assets that route through the handler pipeline (even
                though the processors themselves are not handlers), so their
                fixtures need the tripwire too.
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
            for (int i = 0; i < testFiles.Length; i++)
            {
                string path = testFiles[i];
                string fileName = Path.GetFileName(path);
                if (IsContractTestSelf(fileName))
                {
                    continue;
                }

                if (!TryReadSource(path, fileName, failures, out string source))
                {
                    continue;
                }

                // Gate 1: only scan files that are asset-processor-aware.
                if (!FileIsInAssetContext(source))
                {
                    continue;
                }

                // Gate 2: only scan actual test fixtures (files with [Test]).
                if (source.IndexOf(TestAttributeToken, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                /*
                    Gate 3: only require the tripwire in fixtures that interact
                    with the handler-static tracking system. Deferral-internal
                    tests do not populate handler statics and therefore have no
                    pollution surface for the tripwire to guard.
                */
                bool handlerInvolved = false;
                for (int t = 0; t < handlerInvolvementTokens.Length; t++)
                {
                    if (0 <= source.IndexOf(handlerInvolvementTokens[t], StringComparison.Ordinal))
                    {
                        handlerInvolved = true;
                        break;
                    }
                }

                if (!handlerInvolved)
                {
                    continue;
                }

                /*
                    The fixture must call the tripwire in at least one SetUp-like
                    body. Checking the file as a whole (rather than a specific
                    method) accommodates both per-test [SetUp] and overridden
                    BaseSetUp patterns without having to enumerate method names.
                */
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
                    Placement check: when the fixture chains to base.BaseSetUp(),
                    the tripwire must run BEFORE that chain so pollution is
                    snapshotted against the handler statics as inherited from the
                    prior fixture, not after the base class has had a chance to
                    perform any asset-database configuration that could shift
                    attribution. Fixtures that do not chain to base.BaseSetUp()
                    (e.g. per-test [SetUp] methods on fixtures that don't inherit
                    from CommonTestBase) skip this check — the placement
                    invariant is only meaningful relative to an existing base
                    chain. Use IndexOfOutsideLiteral so a base.BaseSetUp(
                    reference inside a comment or string literal cannot mask a
                    real placement regression.
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
            for (int i = 0; i < expectedTypeNames.Length; i++)
            {
                if (!discoveredNames.Contains(expectedTypeNames[i]))
                {
                    missing.Add(expectedTypeNames[i]);
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

            for (int i = 0; i < candidates.Length; i++)
            {
                if (Directory.Exists(candidates[i]))
                {
                    return candidates[i];
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

        /*
            Route through the shared tokenizer so a commented-out or string-literal
            occurrence of the flush call doesn't mask a missing real call. The
            forbidden-token scanner uses the same tokenizer (via StripDeferralSchedules
            / IndexOfOutsideLiteral); keeping the teardown/OneTime scanners consistent
            avoids a latent class of false-positive passes during debugging.

            Accepts any entry point that drains the deferral queue — the direct
            FlushForTesting() call OR any centralized helper that flushes internally
            (FlushAndClearAll, AssertCleanAndClearAll, ClearTestState). Authors who
            adopt the helper don't need to pay the cost of a second explicit flush.
        */
        private static bool ContainsDirectFlush(string body)
        {
            for (int i = 0; i < FlushEquivalentExpressions.Length; i++)
            {
                if (0 <= IndexOfOutsideLiteral(body, FlushEquivalentExpressions[i], 0))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsHandlerClear(string body)
        {
            /*
                HandlerClearPattern is a raw regex — applying it to the body
                directly would false-positive on a `Test*Handler.Clear()` token
                that appears inside a `/// doc comment` or a string literal
                (e.g. an XML doc example). Strip literals and comments first so
                the regex only sees executable code, matching the literal-aware
                behavior of IndexOfOutsideLiteral that the other gate helpers
                rely on.
            */
            string codeOnly = StripLiteralsAndComments(body);
            if (HandlerClearPattern.IsMatch(codeOnly))
            {
                return true;
            }

            for (int i = 0; i < HandlerClearEquivalentCalls.Length; i++)
            {
                if (0 <= IndexOfOutsideLiteral(body, HandlerClearEquivalentCalls[i], 0))
                {
                    return true;
                }
            }

            return false;
        }

        /*
            Returns a copy of `source` with string/char literal content and comment
            content replaced by spaces (so byte offsets and line structure are
            preserved, but literal/comment text cannot be matched by a downstream
            regex). Built on top of SkipLiteralOrComment so it behaves identically
            to IndexOfOutsideLiteral for the purposes of token detection.
        */
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
            for (int i = 0; i < AssetMutationTokens.Length; i++)
            {
                if (0 <= IndexOfOutsideLiteral(body, AssetMutationTokens[i], 0))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool FileIsInAssetContext(string source)
        {
            for (int i = 0; i < AssetContextTokens.Length; i++)
            {
                if (0 <= source.IndexOf(AssetContextTokens[i], StringComparison.Ordinal))
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
                for (int j = 0; j < EditorAssemblyNames.Length; j++)
                {
                    if (string.Equals(name, EditorAssemblyNames[j], StringComparison.Ordinal))
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
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
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
            Simple filesystem walk. More robust than MonoScript.FromScriptableObject in a
            package context where the processor lives under Packages/... and the asset
            database may not have imported it yet on a fresh clone.

            Returns ALL candidate files — the primary <TypeName>.cs plus any sibling
            <TypeName>.*.cs files (common for `partial` classes, e.g.
            <c>MyProcessor.Callbacks.cs</c>). Callers scan every candidate until they
            locate the method body, so partial-class implementations split across files
            are handled transparently.
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
                for (int i = 0; i < matches.Length; i++)
                {
                    string match = matches[i];
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
            Extracts the substring representing the named method's body. Handles two
            syntactic forms:
              1. Block form:  `void M(...) { <body> }`
              2. Expression-bodied form:  `void M(...) => <expression>;`

            Not a full C# parser: adequate for the flat method declarations used by our
            processors.

            The regex is anchored on a method DECLARATION shape — access modifier(s) plus
            optional static/override/async keywords, then a return type (void/Task/
            IEnumerator/IAsyncEnumerable or generic variants thereof), then the method
            name, then a parameter list. This avoids false-positive matches for
              - `nameof(OnPostprocessAllAssets)`
              - `[Obsolete("... OnPostprocessAllAssets ...")]`
              - `<see cref="OnPostprocessAllAssets"/>` in XML doc
              - `Debug.Log("OnPostprocessAllAssets was called")`
            because none of those appear after a modifier+return-type prefix. Using
            RegexOptions.Singleline on the parameter list lets us tolerate multiline
            signatures (parameters on their own lines), which is the style used by the
            processors in this package.

            After locating a signature match, we walk past the parameter list's closing
            paren and look at the next meaningful token to determine the body form.
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
                /*
                    The regex ends at `\s*\(`, so the parameter list's opening paren is
                    the last character of the match — no string search required.
                */
                int openParen = match.Index + match.Length - 1;
                int closeParen = FindMatchingParen(source, openParen);
                if (closeParen < 0)
                {
                    return null;
                }

                /*
                    After the parameter list, the next non-whitespace/non-constraint token
                    determines the body form:
                      `{` -> block body
                      `=>` -> expression-bodied
                      `;` -> partial declaration (no body) -> skip to next match
                      `where T : ...` -> generic constraint; scan past to the body token
                */
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
                // If lead is ';' this is a partial declaration; fall through to next match.

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

                /*
                    Skip `where T : ...` generic constraints between the parameter list
                    and the body. Constraints are terminated by `{`, `=>`, or `;`.
                */
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

        /*
            Walks forward from `start` to the terminating `;` of an expression-bodied
            method, tracking nesting depth through a single shared tokenizer so literals
            and comments cannot fool the walk.
        */
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

        /*
            Walks forward from the opening brace at `openIndex` to the matching `}`,
            tracking literals and comments via the shared tokenizer.
        */
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

        /*
            Removes text inside any AssetPostprocessorDeferral.Schedule(...) expression so
            tokens wrapped in the deferral lambda are not counted as contract violations.
        */
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

        /*
            Scans for `needle` in `haystack` starting at `start`, skipping over any
            occurrences inside string/char literals or comments. Returns -1 if no
            occurrence is found outside those regions.
        */
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

        /*
            Walks forward from the opening paren at `openIndex` to the matching `)`,
            tracking literals and comments via the shared tokenizer.
        */
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

            /*
                Raw string literal: 3 or more consecutive `"` opens; same run-length
                closes.
            */
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

                /*
                    Two `"` in a row outside a string is an empty string literal `""`.
                    Fall through to regular-string handling below.
                */
            }

            /*
                Verbatim string: `@"..."`, `$@"..."`, or `@$"..."`. `""` inside is an
                escaped quote.
            */
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

            // Interpolated regular string: `$"..."` (one `$`, one `"`).
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
            for (int i = 0; i < ForbiddenTokens.Length; i++)
            {
                string token = ForbiddenTokens[i];
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
