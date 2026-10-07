// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.Compilation;
    using UnityEngine;
    using Assembly = System.Reflection.Assembly;

    /// <summary>
    /// Validation tests that verify assembly references and InternalsVisibleTo entries
    /// are correctly configured. These tests catch compilation issues like:
    /// - Missing namespace references
    /// - Broken assembly references
    /// - Missing InternalsVisibleTo entries for test assemblies
    /// </summary>
    [TestFixture]
    [Category("Fast")]
    [Category("Validation")]
    public sealed class AssemblyReferenceValidationTests
    {
        private const string UnityIncludeTestsDefine = "UNITY_INCLUDE_TESTS";

        private static readonly string[] ProductionAssemblyNames =
        {
            "WallstopStudios.UnityHelpers",
            "WallstopStudios.UnityHelpers.Editor",
        };

        private static readonly string[] AssemblyInfoPaths =
        {
            "Runtime/AssemblyInfo.cs",
            "Editor/AssemblyInfo.cs",
        };

        private static List<string> DiscoverTestAssemblyNames()
        {
            List<string> names = new();

            foreach (TestAsmdefDescriptor asmdef in DiscoverTestAsmdefs())
            {
                names.Add(asmdef.AssemblyName);
            }

            return names;
        }

        /// <summary>
        /// The discovered asmdefs that can actually hold a test.
        /// </summary>
        private static List<string> DiscoverTestHostingAssemblyNames()
        {
            List<string> names = new();

            foreach (TestAsmdefDescriptor asmdef in DiscoverTestAsmdefs())
            {
                if (asmdef.HostsTests)
                {
                    names.Add(asmdef.AssemblyName);
                }
            }

            return names;
        }

        private static List<TestAsmdefDescriptor> DiscoverTestAsmdefs()
        {
            List<TestAsmdefDescriptor> descriptors = new();

            string packagePath = GetPackagePath();
            if (string.IsNullOrEmpty(packagePath))
            {
                return descriptors;
            }

            string testsPath = Path.Combine(packagePath, "Tests");
            if (!Directory.Exists(testsPath))
            {
                return descriptors;
            }

            return DiscoverTestAsmdefs(testsPath);
        }

        private static List<TestAsmdefDescriptor> DiscoverTestAsmdefs(string testsPath)
        {
            List<TestAsmdefDescriptor> descriptors = new();
            string[] asmdefFiles = DiscoverImportedAsmdefPaths(testsPath);

            foreach (string asmdefPath in asmdefFiles)
            {
                string asmdefContent = File.ReadAllText(asmdefPath);
                string assemblyName = ExtractAssemblyNameFromAsmdef(asmdefContent);
                if (!string.IsNullOrEmpty(assemblyName))
                {
                    descriptors.Add(
                        new TestAsmdefDescriptor(
                            assemblyName,
                            ExtractDefineConstraintsFromAsmdef(asmdefContent),
                            AsmdefReferencesTestFramework(asmdefContent)
                        )
                    );
                }
            }

            return descriptors;
        }

        /* Unity ignores these names below the import root. Check relative
         * entries, not physical parent folders outside the package's Tests tree.
         * https://docs.unity3d.com/2022.3/Documentation/Manual/SpecialFolders.html */
        private static bool IsUnityIgnoredName(string name)
        {
            return name.StartsWith(".", StringComparison.Ordinal)
                || name.EndsWith("~", StringComparison.Ordinal)
                || string.Equals(name, "cvs", StringComparison.OrdinalIgnoreCase);
        }

        private static string[] DiscoverImportedAsmdefPaths(string testsPath)
        {
            List<string> paths = new();
            Stack<string> pending = new();
            pending.Push(testsPath);
            while (pending.TryPop(out string directory))
            {
                foreach (string path in Directory.GetFiles(directory, "*.asmdef"))
                {
                    if (!IsUnityIgnoredName(Path.GetFileName(path)))
                    {
                        paths.Add(path);
                    }
                }
                foreach (string child in Directory.GetDirectories(directory))
                {
                    if (!IsUnityIgnoredName(Path.GetFileName(child)))
                    {
                        pending.Push(child);
                    }
                }
            }
            string[] result = paths.ToArray();
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static void CollectTestAssemblyLoadIssues(
            IReadOnlyList<TestAsmdefDescriptor> asmdefs,
            List<string> failedAssemblies,
            List<string> skippedAssemblies
        )
        {
            foreach (TestAsmdefDescriptor asmdef in asmdefs)
            {
                string assemblyName = asmdef.AssemblyName;
                try
                {
                    Assembly assembly = GetLoadedAssembly(assemblyName);
                    if (assembly == null)
                    {
                        if (asmdef.IsOptionalWhenUnloaded)
                        {
                            skippedAssemblies.Add(
                                $"{assemblyName}: Optional test assembly not compiled because one or more define constraints are absent"
                            );
                        }
                        else
                        {
                            failedAssemblies.Add(
                                $"{assemblyName}: Assembly not found in loaded assemblies. "
                                    + "This may indicate a missing asmdef or broken assembly references."
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedAssemblies.Add($"{assemblyName}: {ex.Message}");
                }
            }
        }

        private static Assembly GetLoadedAssembly(string assemblyName)
        {
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in loadedAssemblies)
            {
                if (
                    string.Equals(
                        assembly.GetName().Name,
                        assemblyName,
                        System.StringComparison.Ordinal
                    )
                )
                {
                    return assembly;
                }
            }

            return null;
        }

        private static bool ShouldSkipWhenUnloaded(IReadOnlyList<string> defineConstraints)
        {
            foreach (string defineConstraint in defineConstraints)
            {
                if (string.IsNullOrWhiteSpace(defineConstraint))
                {
                    continue;
                }

                if (
                    string.Equals(
                        defineConstraint.Trim(),
                        UnityIncludeTestsDefine,
                        System.StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static HashSet<string> ParseInternalsVisibleToEntries(string content)
        {
            HashSet<string> entries = new();
            string singleLine = content.Replace("\r", " ").Replace("\n", " ");
            int searchStart = 0;

            while (searchStart < singleLine.Length)
            {
                int ivtIndex = singleLine.IndexOf("InternalsVisibleTo", searchStart);
                if (ivtIndex < 0)
                {
                    break;
                }

                int startQuote = singleLine.IndexOf('"', ivtIndex);
                if (startQuote < 0)
                {
                    break;
                }

                int endQuote = singleLine.IndexOf('"', startQuote + 1);
                if (endQuote < 0)
                {
                    break;
                }

                string assemblyName = singleLine.Substring(
                    startQuote + 1,
                    endQuote - startQuote - 1
                );
                entries.Add(assemblyName);
                searchStart = endQuote + 1;
            }

            return entries;
        }

        private static string ExtractAssemblyNameFromAsmdef(string content)
        {
            return ExtractJsonStringValue(content, "name");
        }

        private static string ExtractRootNamespaceFromAsmdef(string content)
        {
            return ExtractJsonStringValue(content, "rootNamespace");
        }

        private static string[] ExtractDefineConstraintsFromAsmdef(string content)
        {
            return ExtractJsonStringArrayValue(content, "defineConstraints");
        }

        private static bool AsmdefReferencesTestFramework(string content)
        {
            foreach (string reference in ExtractJsonStringArrayValue(content, "references"))
            {
                if (reference.Contains("TestRunner"))
                {
                    return true;
                }
            }

            foreach (
                string reference in ExtractJsonStringArrayValue(content, "precompiledReferences")
            )
            {
                if (reference.StartsWith("nunit.framework", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ExtractJsonStringValue(string content, string key)
        {
            string[] lines = content.Split('\n');
            string quotedKey = "\"" + key + "\"";
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith(quotedKey))
                {
                    continue;
                }

                int colonIndex = trimmed.IndexOf(':');
                if (colonIndex < 0)
                {
                    continue;
                }

                string afterColon = trimmed.Substring(colonIndex + 1);
                int startQuote = afterColon.IndexOf('"');
                if (startQuote < 0)
                {
                    continue;
                }

                int endQuote = afterColon.IndexOf('"', startQuote + 1);
                if (endQuote < 0)
                {
                    continue;
                }

                return afterColon.Substring(startQuote + 1, endQuote - startQuote - 1);
            }

            return null;
        }

        private static string[] ExtractJsonStringArrayValue(string content, string key)
        {
            string quotedKey = "\"" + key + "\"";
            int keyIndex = content.IndexOf(quotedKey, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return Array.Empty<string>();
            }

            int colonIndex = content.IndexOf(':', keyIndex + quotedKey.Length);
            if (colonIndex < 0)
            {
                return Array.Empty<string>();
            }

            int arrayStartIndex = content.IndexOf('[', colonIndex + 1);
            if (arrayStartIndex < 0)
            {
                return Array.Empty<string>();
            }

            int arrayEndIndex = content.IndexOf(']', arrayStartIndex + 1);
            if (arrayEndIndex < 0)
            {
                return Array.Empty<string>();
            }

            string arrayContent = content.Substring(
                arrayStartIndex + 1,
                arrayEndIndex - arrayStartIndex - 1
            );
            List<string> values = new();
            int searchStart = 0;
            while (searchStart < arrayContent.Length)
            {
                int startQuote = arrayContent.IndexOf('"', searchStart);
                if (startQuote < 0)
                {
                    break;
                }

                int endQuote = arrayContent.IndexOf('"', startQuote + 1);
                if (endQuote < 0)
                {
                    break;
                }

                values.Add(arrayContent.Substring(startQuote + 1, endQuote - startQuote - 1));
                searchStart = endQuote + 1;
            }

            return values.ToArray();
        }

        private static string GetPackagePath([CallerFilePath] string sourcePath = "")
        {
            string root = TestPackageRoot.Resolve(
                typeof(AssemblyReferenceValidationTests).Assembly,
                sourcePath
            );
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(root),
                "Could not resolve this package and its Tests directory."
            );
            return root;
        }

        [TestCase("Hidden~/Fixture.asmdef", false)]
        [TestCase("Visible/DeepHidden~/Nested/Fixture.asmdef", false)]
        [TestCase("Visible~Archive/Fixture.asmdef", true)]
        [TestCase("Visible/Fixture.asmdef", true)]
        [TestCase(".Hidden/Fixture.asmdef", false)]
        [TestCase("Visible/.Hidden.asmdef", false)]
        [TestCase("CVS/Fixture.asmdef", false)]
        [TestCase("Visible.tmp/Fixture.asmdef", true)]
        public void ImportedAsmdefDiscoveryPreservesVisibleMissingAssemblyFailures(
            string relativePath,
            bool imported
        )
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "unity-asmdef-discovery-" + Guid.NewGuid().ToString("N")
            );
            const string AssemblyName = "PackageValidation.VisibleMissingAssembly";
            try
            {
                string path = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(
                    path,
                    "{\n\"name\":\""
                        + AssemblyName
                        + "\",\"defineConstraints\":[\"UNITY_INCLUDE_TESTS\"],\"references\":[\"UnityEngine.TestRunner\"]}"
                );
                List<TestAsmdefDescriptor> descriptors = DiscoverTestAsmdefs(root);
                Assert.That(descriptors.Count, Is.EqualTo(imported ? 1 : 0));
                List<string> failed = new();
                List<string> skipped = new();
                CollectTestAssemblyLoadIssues(descriptors, failed, skipped);
                Assert.That(failed.Count, Is.EqualTo(imported ? 1 : 0));
                Assert.That(skipped, Is.Empty);
                if (imported)
                {
                    StringAssert.Contains(AssemblyName, failed[0]);
                    StringAssert.Contains("Assembly not found", failed[0]);
                }
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void ImportedAsmdefDiscoveryPreservesOptionalDefineBehavior()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "unity-asmdef-discovery-" + Guid.NewGuid().ToString("N")
            );
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(
                    Path.Combine(root, "Optional.asmdef"),
                    "{\n\"name\":\"PackageValidation.OptionalMissingAssembly\",\"defineConstraints\":[\"OPTIONAL_DEPENDENCY\"]}"
                );
                List<TestAsmdefDescriptor> descriptors = DiscoverTestAsmdefs(root);
                Assert.That(descriptors.Count, Is.EqualTo(1));
                List<string> failed = new();
                List<string> skipped = new();
                CollectTestAssemblyLoadIssues(descriptors, failed, skipped);
                Assert.That(failed, Is.Empty);
                Assert.That(skipped.Count, Is.EqualTo(1));
                StringAssert.Contains("Optional test assembly", skipped[0]);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// Verifies all production assemblies can be loaded.
        /// </summary>
        [Test]
        public void ProductionAssembliesCanBeLoaded()
        {
            List<string> failedAssemblies = new();

            foreach (string assemblyName in ProductionAssemblyNames)
            {
                try
                {
                    Assembly assembly = GetLoadedAssembly(assemblyName);
                    if (assembly == null)
                    {
                        failedAssemblies.Add(
                            $"{assemblyName}: Assembly not found in loaded assemblies"
                        );
                    }
                }
                catch (Exception ex)
                {
                    failedAssemblies.Add($"{assemblyName}: {ex.Message}");
                }
            }

            if (0 < failedAssemblies.Count)
            {
                TestContext.WriteLine(
                    "Diagnostic: All loaded assembly names containing 'WallstopStudios':"
                );
                Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (Assembly assembly in loadedAssemblies)
                {
                    string name = assembly.GetName().Name;
                    if (name.Contains("WallstopStudios"))
                    {
                        TestContext.WriteLine($"  {name}");
                    }
                }
            }

            Assert.IsEmpty(
                failedAssemblies,
                $"Failed to load production assemblies:\n{string.Join("\n", failedAssemblies)}"
            );
        }

        /// <summary>
        /// Verifies all test assemblies can be loaded.
        /// </summary>
        [Test]
        public void TestAssembliesCanBeLoaded()
        {
            List<string> failedAssemblies = new();
            List<string> skippedAssemblies = new();

            CollectTestAssemblyLoadIssues(
                DiscoverTestAsmdefs(),
                failedAssemblies,
                skippedAssemblies
            );

            if (0 < skippedAssemblies.Count)
            {
                Debug.Log(
                    $"Skipped optional integration assemblies:\n{string.Join("\n", skippedAssemblies)}"
                );
            }

            if (0 < failedAssemblies.Count)
            {
                TestContext.WriteLine(
                    "Diagnostic: All loaded assembly names containing 'WallstopStudios':"
                );
                Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (Assembly assembly in loadedAssemblies)
                {
                    string name = assembly.GetName().Name;
                    if (name.Contains("WallstopStudios"))
                    {
                        TestContext.WriteLine($"  {name}");
                    }
                }
            }

            Assert.IsEmpty(
                failedAssemblies,
                $"Failed to load test assemblies:\n{string.Join("\n", failedAssemblies)}"
            );
        }

        /// <summary>
        /// Verifies all loaded test assemblies can load their types without errors.
        /// This catches broken type references and missing namespace issues.
        /// </summary>
        [Test]
        public void TestAssembliesCanLoadAllTypes()
        {
            List<string> failedAssemblies = new();

            foreach (string assemblyName in DiscoverTestAssemblyNames())
            {
                Assembly assembly = GetLoadedAssembly(assemblyName);
                if (assembly == null)
                {
                    // Optional integration assemblies may be absent.
                    continue;
                }

                try
                {
                    Type[] types = assembly.GetTypes();
                    if (types.Length == 0)
                    {
                        // An empty assembly is suspicious but not necessarily invalid.
                        Debug.LogWarning(
                            $"Assembly {assemblyName} has no types. This may indicate a configuration issue."
                        );
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    StringBuilder sb = new();
                    sb.AppendLine($"{assemblyName}: Failed to load types");
                    sb.AppendLine("Loader exceptions:");
                    foreach (Exception loaderEx in ex.LoaderExceptions)
                    {
                        if (loaderEx != null)
                        {
                            sb.AppendLine($"  - {loaderEx.Message}");
                        }
                    }

                    failedAssemblies.Add(sb.ToString());
                }
                catch (Exception ex)
                {
                    failedAssemblies.Add($"{assemblyName}: {ex.Message}");
                }
            }

            Assert.IsEmpty(
                failedAssemblies,
                $"Failed to load types from test assemblies:\n{string.Join("\n", failedAssemblies)}"
            );
        }

        /// <summary>
        /// Verifies all loaded production assemblies can load their types without errors.
        /// </summary>
        [Test]
        public void ProductionAssembliesCanLoadAllTypes()
        {
            List<string> failedAssemblies = new();

            foreach (string assemblyName in ProductionAssemblyNames)
            {
                Assembly assembly = GetLoadedAssembly(assemblyName);
                if (assembly == null)
                {
                    failedAssemblies.Add($"{assemblyName}: Assembly not loaded");
                    continue;
                }

                try
                {
                    Type[] types = assembly.GetTypes();
                    Assert.Greater(
                        types.Length,
                        0,
                        $"Production assembly {assemblyName} should have types"
                    );
                }
                catch (ReflectionTypeLoadException ex)
                {
                    StringBuilder sb = new();
                    sb.AppendLine($"{assemblyName}: Failed to load types");
                    sb.AppendLine("Loader exceptions:");
                    foreach (Exception loaderEx in ex.LoaderExceptions)
                    {
                        if (loaderEx != null)
                        {
                            sb.AppendLine($"  - {loaderEx.Message}");
                        }
                    }

                    failedAssemblies.Add(sb.ToString());
                }
                catch (Exception ex)
                {
                    failedAssemblies.Add($"{assemblyName}: {ex.Message}");
                }
            }

            Assert.IsEmpty(
                failedAssemblies,
                $"Failed to load types from production assemblies:\n{string.Join("\n", failedAssemblies)}"
            );
        }

        /// <summary>
        /// Verifies that InternalsVisibleTo entries exist for all test assemblies that need them.
        /// This test reads the AssemblyInfo.cs files and verifies entries exist.
        /// </summary>
        [Test]
        public void InternalsVisibleToEntriesExistForTestAssemblies()
        {
            string packagePath = GetPackagePath();
            if (string.IsNullOrEmpty(packagePath))
            {
                Assert.Inconclusive("Could not determine package path");
                return;
            }

            List<string> missingEntries = new();
            Dictionary<string, HashSet<string>> assemblyInfoEntries = new();

            foreach (string relativePath in AssemblyInfoPaths)
            {
                string fullPath = Path.Combine(packagePath, relativePath);
                if (!File.Exists(fullPath))
                {
                    missingEntries.Add($"AssemblyInfo file not found: {relativePath}");
                    continue;
                }

                string content = File.ReadAllText(fullPath);
                HashSet<string> entries = ParseInternalsVisibleToEntries(content);
                assemblyInfoEntries[relativePath] = entries;
            }

            foreach (TestAsmdefDescriptor testAsmdef in DiscoverTestAsmdefs())
            {
                string testAssemblyName = testAsmdef.AssemblyName;

                if (!testAsmdef.HostsTests)
                {
                    continue;
                }

                if (testAsmdef.IsOptionalWhenUnloaded)
                {
                    // Optional integrations may lack a loaded friend assembly.
                    bool hasEntry = false;
                    foreach (KeyValuePair<string, HashSet<string>> kvp in assemblyInfoEntries)
                    {
                        if (kvp.Value.Contains(testAssemblyName))
                        {
                            hasEntry = true;
                            break;
                        }
                    }

                    if (!hasEntry)
                    {
                        Debug.LogWarning(
                            $"Optional integration assembly {testAssemblyName} does not have "
                                + "InternalsVisibleTo entry. Add one if internal access is needed."
                        );
                    }

                    continue;
                }

                bool foundEntry = false;
                foreach (KeyValuePair<string, HashSet<string>> kvp in assemblyInfoEntries)
                {
                    if (kvp.Value.Contains(testAssemblyName))
                    {
                        foundEntry = true;
                        break;
                    }
                }

                if (!foundEntry)
                {
                    missingEntries.Add(
                        $"Missing InternalsVisibleTo entry for test assembly: {testAssemblyName}. "
                            + "Add [assembly: InternalsVisibleTo(\""
                            + testAssemblyName
                            + "\")] "
                            + "to Runtime/AssemblyInfo.cs and/or Editor/AssemblyInfo.cs"
                    );
                }
            }

            if (0 < missingEntries.Count)
            {
                TestContext.WriteLine("Diagnostic: InternalsVisibleTo entries found per file:");
                foreach (KeyValuePair<string, HashSet<string>> kvp in assemblyInfoEntries)
                {
                    TestContext.WriteLine($"  {kvp.Key}:");
                    foreach (string entry in kvp.Value)
                    {
                        TestContext.WriteLine($"    {entry}");
                    }
                }

                TestContext.WriteLine(
                    "\nDiagnostic: Discovered test assembly names being checked:"
                );
                foreach (string name in DiscoverTestAssemblyNames())
                {
                    TestContext.WriteLine($"  {name}");
                }
            }

            Assert.IsEmpty(
                missingEntries,
                $"InternalsVisibleTo configuration issues:\n{string.Join("\n", missingEntries)}"
            );
        }

        /// <summary>
        /// Verifies that all asmdef files in the Tests folder have corresponding assemblies
        /// that can be loaded (unless they are optional integrations).
        /// </summary>
        [Test]
        public void AllTestAsmdefFilesProduceLoadableAssemblies()
        {
            string packagePath = GetPackagePath();
            if (string.IsNullOrEmpty(packagePath))
            {
                Assert.Inconclusive("Could not determine package path");
                return;
            }

            string testsPath = Path.Combine(packagePath, "Tests");
            if (!Directory.Exists(testsPath))
            {
                Assert.Fail("Tests directory not found at: " + testsPath);
                return;
            }

            string[] asmdefFiles = DiscoverImportedAsmdefPaths(testsPath);
            Assert.IsTrue(0 < asmdefFiles.Length, $"No asmdef was found under {testsPath}.");

            List<string> issues = new();

            foreach (string asmdefPath in asmdefFiles)
            {
                try
                {
                    string asmdefContent = File.ReadAllText(asmdefPath);
                    string assemblyName = ExtractAssemblyNameFromAsmdef(asmdefContent);
                    string[] defineConstraints = ExtractDefineConstraintsFromAsmdef(asmdefContent);

                    if (string.IsNullOrEmpty(assemblyName))
                    {
                        issues.Add($"{asmdefPath}: Could not parse assembly name from asmdef");
                        continue;
                    }

                    Assembly assembly = GetLoadedAssembly(assemblyName);
                    if (assembly == null)
                    {
                        if (ShouldSkipWhenUnloaded(defineConstraints))
                        {
                            // Expected when an optional dependency or target define is absent.
                            continue;
                        }

                        issues.Add(
                            $"{assemblyName}: asmdef exists at {asmdefPath} but assembly is not loaded. "
                                + "Check for missing references or compilation errors."
                        );
                    }
                }
                catch (Exception ex)
                {
                    issues.Add($"{asmdefPath}: Error processing asmdef: {ex.Message}");
                }
            }

            Assert.IsEmpty(issues, $"asmdef/assembly issues found:\n{string.Join("\n", issues)}");
        }

        /// <summary>
        /// Cross-validates that the test assemblies discovered from on-disk asmdef files stay
        /// mutually consistent with the InternalsVisibleTo entries in the AssemblyInfo.cs files.
        /// This is the canonical drift check: there is no hardcoded list to keep in sync, so a
        /// new test asmdef without a matching InternalsVisibleTo entry (or a stale entry whose
        /// asmdef was removed) fails loudly with an actionable message.
        /// </summary>
        [Test]
        public void DiscoveredTestAsmdefsAndInternalsVisibleToEntriesAreConsistent()
        {
            string packagePath = GetPackagePath();
            if (string.IsNullOrEmpty(packagePath))
            {
                Assert.Inconclusive("Could not determine package path");
                return;
            }

            HashSet<string> asmdefAssemblyNames = new(DiscoverTestHostingAssemblyNames());

            // Zero discoveries would make the subsequent assertions vacuous.
            Assert.That(
                asmdefAssemblyNames,
                Is.Not.Empty,
                "Discovered zero test asmdefs under the package Tests/ tree. The discovery root "
                    + "(GetPackagePath/Tests) is wrong or the layout changed; the validation suite "
                    + "would otherwise pass vacuously."
            );

            HashSet<string> testInternalsVisibleToEntries = new();
            foreach (string relativePath in AssemblyInfoPaths)
            {
                string fullPath = Path.Combine(packagePath, relativePath);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                string content = File.ReadAllText(fullPath);
                HashSet<string> entries = ParseInternalsVisibleToEntries(content);
                foreach (string entry in entries)
                {
                    if (entry.StartsWith("WallstopStudios.UnityHelpers.Tests"))
                    {
                        testInternalsVisibleToEntries.Add(entry);
                    }
                }
            }

            List<string> issues = new();

            foreach (string asmdefName in asmdefAssemblyNames)
            {
                if (!testInternalsVisibleToEntries.Contains(asmdefName))
                {
                    issues.Add(
                        $"Test asmdef '{asmdefName}' exists on disk but has no InternalsVisibleTo "
                            + "entry. Add [assembly: InternalsVisibleTo(\""
                            + asmdefName
                            + "\")] to Runtime/AssemblyInfo.cs and Editor/AssemblyInfo.cs."
                    );
                }
            }

            foreach (string ivtEntry in testInternalsVisibleToEntries)
            {
                if (!asmdefAssemblyNames.Contains(ivtEntry))
                {
                    issues.Add(
                        $"InternalsVisibleTo entry '{ivtEntry}' has no matching test asmdef on disk. "
                            + "Remove the stale entry from Runtime/AssemblyInfo.cs and Editor/AssemblyInfo.cs."
                    );
                }
            }

            if (0 < issues.Count)
            {
                TestContext.WriteLine("Diagnostic: discovered test asmdef assembly names on disk:");
                foreach (string name in asmdefAssemblyNames)
                {
                    TestContext.WriteLine($"  {name}");
                }

                TestContext.WriteLine("\nDiagnostic: test InternalsVisibleTo entries:");
                foreach (string entry in testInternalsVisibleToEntries)
                {
                    TestContext.WriteLine($"  {entry}");
                }
            }

            Assert.IsEmpty(
                issues,
                "Test asmdef files and InternalsVisibleTo entries are out of sync:\n"
                    + string.Join("\n", issues)
            );
        }

        /// <summary>
        /// Verifies that the Unity Compilation Pipeline reports no errors for test assemblies.
        /// </summary>
        [Test]
        public void UnityCompilationPipelineReportsNoErrors()
        {
            UnityEditor.Compilation.Assembly[] assemblies = CompilationPipeline.GetAssemblies(
                AssembliesType.Editor
            );

            List<string> testAssemblyIssues = new();
            int testAssembliesInspected = 0;

            foreach (UnityEditor.Compilation.Assembly assembly in assemblies)
            {
                if (!assembly.name.StartsWith("WallstopStudios.UnityHelpers.Tests"))
                {
                    continue;
                }

                ++testAssembliesInspected;

                foreach (UnityEditor.Compilation.Assembly reference in assembly.assemblyReferences)
                {
                    if (!File.Exists(reference.outputPath))
                    {
                        testAssemblyIssues.Add(
                            $"{assembly.name}: Missing referenced assembly: {reference.name}"
                        );
                    }
                }

                foreach (string asmdefRef in assembly.allReferences)
                {
                    if (
                        asmdefRef.Contains("Unity")
                        || asmdefRef.Contains("System")
                        || asmdefRef.Contains("mscorlib")
                    )
                    {
                        continue;
                    }

                    if (asmdefRef.Contains("WallstopStudios") && !File.Exists(asmdefRef))
                    {
                        testAssemblyIssues.Add(
                            $"{assembly.name}: Missing project reference: {asmdefRef}"
                        );
                    }
                }
            }

            Assert.IsTrue(
                0 < testAssembliesInspected,
                "The compilation pipeline reported no test assembly, so this would pass whatever "
                    + "state their references were in."
            );
            Assert.IsEmpty(
                testAssemblyIssues,
                $"Compilation pipeline reported issues:\n{string.Join("\n", testAssemblyIssues)}"
            );
        }

        /// <summary>
        /// Verifies that test assemblies reference the correct production assemblies.
        /// </summary>
        [Test]
        public void TestAssembliesReferenceProductionAssemblies()
        {
            List<string> issues = new();

            foreach (string testAssemblyName in DiscoverTestAssemblyNames())
            {
                Assembly testAssembly = GetLoadedAssembly(testAssemblyName);
                if (testAssembly == null)
                {
                    continue;
                }

                AssemblyName[] referencedAssemblies = testAssembly.GetReferencedAssemblies();
                HashSet<string> referencedNames = new();
                foreach (AssemblyName an in referencedAssemblies)
                {
                    referencedNames.Add(an.Name);
                }

                if (
                    string.Equals(
                        testAssemblyName,
                        "WallstopStudios.UnityHelpers.Tests.Core",
                        System.StringComparison.Ordinal
                    )
                )
                {
                    if (!referencedNames.Contains("WallstopStudios.UnityHelpers"))
                    {
                        issues.Add(
                            $"{testAssemblyName}: Should reference WallstopStudios.UnityHelpers"
                        );
                    }
                }

                if (testAssemblyName.Contains(".Tests.Editor"))
                {
                    if (!referencedNames.Contains("WallstopStudios.UnityHelpers.Editor"))
                    {
                        // Some test assemblies legitimately need only the runtime assembly.
                        Debug.Log(
                            $"Note: {testAssemblyName} does not directly reference "
                                + "WallstopStudios.UnityHelpers.Editor (may be indirect)"
                        );
                    }
                }
            }

            Assert.IsEmpty(issues, $"Test assembly reference issues:\n{string.Join("\n", issues)}");
        }

        /// <summary>
        /// Verifies that the current assembly (Validation) can access internal members
        /// from production assemblies via InternalsVisibleTo.
        /// </summary>
        [Test]
        public void ValidationAssemblyCanAccessInternalMembers()
        {
            Assembly runtimeAssembly = GetLoadedAssembly("WallstopStudios.UnityHelpers");
            Assert.IsTrue(runtimeAssembly != null, "Runtime assembly should be loaded");

            Assembly editorAssembly = GetLoadedAssembly("WallstopStudios.UnityHelpers.Editor");
            Assert.IsTrue(editorAssembly != null, "Editor assembly should be loaded");

            Type[] runtimeTypes = runtimeAssembly.GetTypes();
            Type[] editorTypes = editorAssembly.GetTypes();

            Assert.Greater(runtimeTypes.Length, 0, "Runtime assembly should have types");
            Assert.Greater(editorTypes.Length, 0, "Editor assembly should have types");

            int internalRuntimeTypes = 0;
            foreach (Type t in runtimeTypes)
            {
                if (!t.IsPublic && !t.IsNestedPublic)
                {
                    ++internalRuntimeTypes;
                }
            }

            int internalEditorTypes = 0;
            foreach (Type t in editorTypes)
            {
                if (!t.IsPublic && !t.IsNestedPublic)
                {
                    ++internalEditorTypes;
                }
            }

            Debug.Log(
                $"Internal types accessible: Runtime={internalRuntimeTypes}, Editor={internalEditorTypes}"
            );
        }

        /// <summary>
        /// Verifies that GetPackagePath returns a valid path and correctly excludes node_modules.
        /// </summary>
        [Test]
        public void GetPackagePathReturnsValidPath()
        {
            string packagePath = GetPackagePath();

            Assert.IsFalse(
                string.IsNullOrEmpty(packagePath),
                "GetPackagePath should return a non-null, non-empty path"
            );

            Assert.IsFalse(
                packagePath.Contains("node_modules"),
                $"GetPackagePath should not return a path containing node_modules. Got: {packagePath}"
            );

            string packageJsonPath = Path.Combine(packagePath, "package.json");
            Assert.IsTrue(
                File.Exists(packageJsonPath),
                $"package.json should exist at the returned path. Expected: {packageJsonPath}"
            );

            string packageJsonContent = File.ReadAllText(packageJsonPath);
            Assert.IsTrue(
                packageJsonContent.Contains("com.wallstop-studios.unity-helpers"),
                $"package.json should contain the correct package name. Path: {packageJsonPath}"
            );
        }

        /// <summary>
        /// Verifies that GetPackagePath returns a path that is consistent across multiple calls.
        /// </summary>
        [Test]
        public void GetPackagePathReturnsConsistentPath()
        {
            string path1 = GetPackagePath();
            string path2 = GetPackagePath();

            Assert.AreEqual(
                path1,
                path2,
                "GetPackagePath should return the same path on multiple calls"
            );
        }

        /// <summary>
        /// Verifies that the package path contains expected directories.
        /// </summary>
        [Test]
        public void GetPackagePathContainsExpectedStructure()
        {
            string packagePath = GetPackagePath();
            if (string.IsNullOrEmpty(packagePath))
            {
                Assert.Inconclusive("Could not determine package path");
                return;
            }

            string testsPath = Path.Combine(packagePath, "Tests");
            Assert.IsTrue(
                Directory.Exists(testsPath),
                $"Tests directory should exist at: {testsPath}"
            );

            string runtimePath = Path.Combine(packagePath, "Runtime");
            Assert.IsTrue(
                Directory.Exists(runtimePath),
                $"Runtime directory should exist at: {runtimePath}"
            );

            string editorPath = Path.Combine(packagePath, "Editor");
            Assert.IsTrue(
                Directory.Exists(editorPath),
                $"Editor directory should exist at: {editorPath}"
            );
        }

        /// <summary>
        /// Verifies namespace consistency between asmdef rootNamespace and actual types.
        /// </summary>
        [Test]
        public void AsmdefRootNamespaceMatchesActualTypes()
        {
            string packagePath = GetPackagePath();
            if (string.IsNullOrEmpty(packagePath))
            {
                Assert.Inconclusive("Could not determine package path");
                return;
            }

            string testsPath = Path.Combine(packagePath, "Tests");
            string[] asmdefFiles = DiscoverImportedAsmdefPaths(testsPath);
            List<string> issues = new();

            foreach (string asmdefPath in asmdefFiles)
            {
                try
                {
                    string asmdefContent = File.ReadAllText(asmdefPath);
                    string assemblyName = ExtractAssemblyNameFromAsmdef(asmdefContent);
                    string rootNamespace = ExtractRootNamespaceFromAsmdef(asmdefContent);

                    if (string.IsNullOrEmpty(assemblyName) || string.IsNullOrEmpty(rootNamespace))
                    {
                        continue;
                    }

                    Assembly assembly = GetLoadedAssembly(assemblyName);
                    if (assembly == null)
                    {
                        continue;
                    }

                    Type[] types = assembly.GetTypes();
                    bool hasMatchingNamespace = false;
                    List<string> mismatchedTypes = new();

                    foreach (Type t in types)
                    {
                        if (t.Namespace == null)
                        {
                            continue;
                        }

                        if (t.Namespace.StartsWith(rootNamespace))
                        {
                            hasMatchingNamespace = true;
                        }
                        else if (
                            !t.Namespace.StartsWith("System")
                            && !t.Namespace.StartsWith("Unity")
                            && !t.Namespace.StartsWith("NUnit")
                            && !t.IsCompilerGenerated()
                        )
                        {
                            mismatchedTypes.Add($"{t.FullName} (namespace: {t.Namespace})");
                        }
                    }

                    if (0 < types.Length && !hasMatchingNamespace && 0 < mismatchedTypes.Count)
                    {
                        StringBuilder issueBuilder = new(
                            $"{assemblyName}: rootNamespace is '{rootNamespace}' but types use different namespaces:"
                        );
                        int maxEntries = Math.Min(5, mismatchedTypes.Count);
                        for (int i = 0; i < maxEntries; ++i)
                        {
                            issueBuilder.Append("\n  ");
                            issueBuilder.Append(mismatchedTypes[i]);
                        }
                        if (5 < mismatchedTypes.Count)
                        {
                            issueBuilder.Append($"\n  ... and {mismatchedTypes.Count - 5} more");
                        }
                        issues.Add(issueBuilder.ToString());
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Type-load failures are exercised separately.
                }
                catch (Exception ex)
                {
                    issues.Add($"{asmdefPath}: Error: {ex.Message}");
                }
            }

            if (0 < issues.Count)
            {
                Debug.LogWarning(
                    "Namespace configuration notes (not necessarily errors):\n"
                        + string.Join("\n", issues)
                );
            }

            Assert.Pass("Namespace analysis complete");
        }

        private sealed class TestAsmdefDescriptor
        {
            public string AssemblyName { get; }

            public string[] DefineConstraints { get; }

            /// <summary>
            /// Whether the asmdef references the test framework, and so can hold a test.
            /// </summary>
            /// <remarks>
            /// Not every asmdef under Tests/ is a test assembly. A fixture needing a MonoBehaviour
            /// has to park it in an all-platform assembly, because Unity refuses AddComponent for a
            /// MonoBehaviour in an editor-only assembly and returns null without logging. Such an
            /// assembly holds no tests and needs no access to production internals. The same rule
            /// lives in scripts/unity/lib/asmdef-discovery.js, which decides what Unity is asked to
            /// run; the two must agree.
            /// </remarks>
            public bool HostsTests { get; }

            public bool IsOptionalWhenUnloaded => ShouldSkipWhenUnloaded(DefineConstraints);

            public TestAsmdefDescriptor(
                string assemblyName,
                string[] defineConstraints,
                bool hostsTests
            )
            {
                AssemblyName = assemblyName;
                DefineConstraints = defineConstraints;
                HostsTests = hostsTests;
            }
        }
    }

    /// <summary>
    /// Extension methods for type checking.
    /// </summary>
    internal static class TypeExtensions
    {
        public static bool IsCompilerGenerated(this Type type)
        {
            return type.GetCustomAttribute<CompilerGeneratedAttribute>() != null
                || (type.Name.StartsWith("<") && type.Name.Contains(">"));
        }
    }
}
