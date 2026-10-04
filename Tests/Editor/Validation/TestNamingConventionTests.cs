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
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Validates that test files follow naming conventions.
    /// Specifically checks for underscores in:
    /// - Test method names
    /// - TestName attribute values
    /// - SetName() method calls
    /// - TestCaseSource method names
    ///
    /// These tests are the runtime second line of defense. The primary gate
    /// is <c>scripts/lint-tests.ps1</c> (UNH004), which runs in the pre-commit
    /// hook and blocks offending names from ever landing on the branch.
    /// </summary>
    [TestFixture]
    [Category("Fast")]
    [Category("Validation")]
    public sealed class TestNamingConventionTests
    {
        private static readonly string[] TestAssemblyPrefixes =
        {
            "WallstopStudios.UnityHelpers.Tests",
        };

        private static readonly Regex TestNameAttributePattern = new(
            @"TestName\s*=\s*""([^""]+)""",
            RegexOptions.Compiled
        );

        private static readonly Regex SetNameMethodPattern = new(
            @"\.SetName\s*\(\s*""([^""]+)""",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Iterates every <c>.cs</c> file under <c>&lt;packagePath&gt;/Tests</c>
        /// and invokes <paramref name="visitor"/> with the relative file path,
        /// 1-based line number, and line text. Returns the absolute path of the
        /// tests directory that was scanned. Missing or unreadable source fails the scan.
        /// </summary>
        internal static string ScanTestFiles(
            string packagePath,
            Action<string, int, string> visitor
        )
        {
            string testsPath = Path.Combine(packagePath, "Tests");
            Assert.IsTrue(
                Directory.Exists(testsPath),
                "Tests directory not found under: " + packagePath
            );

            string[] testFiles = Directory.GetFiles(testsPath, "*.cs", SearchOption.AllDirectories);
            Assert.IsTrue(0 < testFiles.Length, "No test source files found under: " + testsPath);
            Assert.IsTrue(
                Array.Exists(
                    testFiles,
                    path =>
                        string.Equals(
                            Path.GetFileName(path),
                            nameof(TestNamingConventionTests) + ".cs",
                            StringComparison.Ordinal
                        )
                ),
                "The naming fixture source must belong to the scanned tree."
            );
            foreach (string filePath in testFiles)
            {
                if (filePath.EndsWith(".meta"))
                {
                    continue;
                }

                string[] lines = File.ReadAllLines(filePath);

                string relativePath = GetRelativePath(filePath, packagePath);
                for (int lineIndex = 0; lineIndex < lines.Length; ++lineIndex)
                {
                    visitor(relativePath, lineIndex + 1, lines[lineIndex]);
                }
            }

            return testsPath;
        }

        /// <summary>
        /// Iterates every public/non-public/instance/static method on every type of
        /// every loaded assembly whose name starts with a test-assembly prefix, and
        /// invokes <paramref name="visitor"/> for each method that is annotated with
        /// <c>[Test]</c>, <c>[TestCase]</c>, <c>[TestCaseSource]</c>, or
        /// <c>[UnityTest]</c>. Returns the list of assembly names that were scanned
        /// so callers can include that context in failure messages.
        /// </summary>
        private static List<string> ScanTestMethods(Action<string, Type, MethodInfo> visitor)
        {
            List<string> scannedAssemblies = new();
            foreach (Assembly assembly in GetTestAssemblies())
            {
                string assemblyName = assembly.GetName().Name;
                scannedAssemblies.Add(assemblyName);

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                foreach (Type type in types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(
                            BindingFlags.Public
                                | BindingFlags.NonPublic
                                | BindingFlags.Instance
                                | BindingFlags.Static
                        );
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (MethodInfo method in methods)
                    {
                        if (!IsTestMethod(method))
                        {
                            continue;
                        }

                        visitor(assemblyName, type, method);
                    }
                }
            }

            return scannedAssemblies;
        }

        private static List<Assembly> GetTestAssemblies()
        {
            List<Assembly> testAssemblies = new();
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (Assembly assembly in loadedAssemblies)
            {
                string assemblyName = assembly.GetName().Name;
                foreach (string prefix in TestAssemblyPrefixes)
                {
                    if (assemblyName.StartsWith(prefix))
                    {
                        testAssemblies.Add(assembly);
                        break;
                    }
                }
            }

            return testAssemblies;
        }

        private static bool IsTestMethod(MethodInfo method)
        {
            object[] attributes;
            try
            {
                attributes = method.GetCustomAttributes(true);
            }
            catch
            {
                return false;
            }

            foreach (object attribute in attributes)
            {
                string attributeName = attribute.GetType().Name;
                if (
                    string.Equals(attributeName, "TestAttribute", System.StringComparison.Ordinal)
                    || string.Equals(
                        attributeName,
                        "TestCaseAttribute",
                        System.StringComparison.Ordinal
                    )
                    || string.Equals(
                        attributeName,
                        "TestCaseSourceAttribute",
                        System.StringComparison.Ordinal
                    )
                    || string.Equals(
                        attributeName,
                        "UnityTestAttribute",
                        System.StringComparison.Ordinal
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetPackagePath([CallerFilePath] string sourcePath = "")
        {
            string root = TestPackageRoot.Resolve(
                typeof(TestNamingConventionTests).Assembly,
                sourcePath
            );
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(root),
                "Could not resolve this package and its Tests directory."
            );
            return root;
        }

        private static string GetRelativePath(string fullPath, string basePath)
        {
            if (string.IsNullOrEmpty(basePath))
            {
                return fullPath;
            }

            string normalizedFull = Path.GetFullPath(fullPath).Replace('\\', '/');
            string normalizedBase = Path.GetFullPath(basePath).Replace('\\', '/');

            if (!normalizedBase.EndsWith("/"))
            {
                normalizedBase += "/";
            }

            if (normalizedFull.StartsWith(normalizedBase))
            {
                return normalizedFull.Substring(normalizedBase.Length);
            }

            return fullPath;
        }

        /// <summary>
        /// Verifies that test method names do not contain underscores.
        /// </summary>
        [Test]
        public void TestMethodNamesDoNotContainUnderscores()
        {
            List<string> violations = new();
            List<string> scannedAssemblies = ScanTestMethods(
                (assemblyName, type, method) =>
                {
                    if (method.Name.Contains('_'))
                    {
                        string typeName = type.FullName ?? type.Name;
                        violations.Add(
                            $"Method '{typeName}.{method.Name}' in assembly '{assemblyName}' "
                                + "contains underscores. Use PascalCase without underscores."
                        );
                    }
                }
            );

            Assert.IsEmpty(
                violations,
                $"Found {violations.Count} test method(s) with underscores in their names "
                    + $"(scanned {scannedAssemblies.Count} assembly/ies: "
                    + $"{string.Join(", ", scannedAssemblies)}):\n"
                    + string.Join("\n", violations)
            );
        }

        /// <summary>
        /// Verifies that TestName attribute values do not contain underscores.
        /// Scans source files directly for comprehensive detection.
        /// </summary>
        [Test]
        public void TestNameAttributeValuesDoNotContainUnderscores()
        {
            string packagePath = GetPackagePath();
            List<string> violations = new();
            string scannedRoot = ScanTestFiles(
                packagePath,
                (relativePath, lineNumber, line) =>
                {
                    MatchCollection matches = TestNameAttributePattern.Matches(line);
                    foreach (Match match in matches)
                    {
                        string testName = match.Groups[1].Value;
                        if (testName.Contains('_'))
                        {
                            violations.Add(
                                $"{relativePath}:{lineNumber}: TestName=\"{testName}\" contains underscores. "
                                    + "Use dot notation (e.g., \"Input.Null.ReturnsFalse\") or PascalCase."
                            );
                        }
                    }
                }
            );

            Assert.IsEmpty(
                violations,
                $"Found {violations.Count} TestName value(s) with underscores (scanned {scannedRoot}):\n"
                    + string.Join("\n", violations)
            );
        }

        /// <summary>
        /// Verifies that SetName() method call values do not contain underscores.
        /// Scans source files directly for comprehensive detection.
        /// </summary>
        [Test]
        public void SetNameMethodValuesDoNotContainUnderscores()
        {
            string packagePath = GetPackagePath();
            List<string> violations = new();
            string scannedRoot = ScanTestFiles(
                packagePath,
                (relativePath, lineNumber, line) =>
                {
                    MatchCollection matches = SetNameMethodPattern.Matches(line);
                    foreach (Match match in matches)
                    {
                        string setNameValue = match.Groups[1].Value;
                        if (setNameValue.Contains('_'))
                        {
                            violations.Add(
                                $"{relativePath}:{lineNumber}: SetName(\"{setNameValue}\") contains underscores. "
                                    + "Use dot notation (e.g., \"Input.Null.ReturnsFalse\") or PascalCase."
                            );
                        }
                    }
                }
            );

            Assert.IsEmpty(
                violations,
                $"Found {violations.Count} SetName() value(s) with underscores (scanned {scannedRoot}):\n"
                    + string.Join("\n", violations)
            );
        }

        /// <summary>
        /// Verifies that TestCaseSource method names do not contain underscores.
        /// </summary>
        [Test]
        public void TestCaseSourceMethodNamesDoNotContainUnderscores()
        {
            List<string> violations = new();
            Dictionary<string, (string AssemblyName, HashSet<string> Sources)> sourcesByType =
                new();
            List<string> scannedAssemblies = ScanTestMethods(
                (assemblyName, type, method) =>
                {
                    object[] attributes;
                    try
                    {
                        attributes = method.GetCustomAttributes(true);
                    }
                    catch
                    {
                        return;
                    }

                    foreach (object attribute in attributes)
                    {
                        Type attributeType = attribute.GetType();
                        if (
                            !string.Equals(
                                attributeType.Name,
                                "TestCaseSourceAttribute",
                                System.StringComparison.Ordinal
                            )
                        )
                        {
                            continue;
                        }

                        PropertyInfo sourceNameProperty = attributeType.GetProperty("SourceName");
                        if (sourceNameProperty == null)
                        {
                            continue;
                        }

                        object value = sourceNameProperty.GetValue(attribute);
                        if (value is string sourceName && !string.IsNullOrEmpty(sourceName))
                        {
                            string typeName = type.FullName ?? type.Name;
                            if (
                                !sourcesByType.TryGetValue(
                                    typeName,
                                    out (string AssemblyName, HashSet<string> Sources) entry
                                )
                            )
                            {
                                entry = (assemblyName, new HashSet<string>());
                                sourcesByType[typeName] = entry;
                            }
                            entry.Sources.Add(sourceName);
                        }
                    }
                }
            );

            foreach (
                KeyValuePair<
                    string,
                    (string AssemblyName, HashSet<string> Sources)
                > pair in sourcesByType
            )
            {
                foreach (string sourceName in pair.Value.Sources)
                {
                    if (sourceName.Contains('_'))
                    {
                        violations.Add(
                            $"TestCaseSource method '{pair.Key}.{sourceName}' in assembly "
                                + $"'{pair.Value.AssemblyName}' contains underscores. "
                                + "Use PascalCase without underscores."
                        );
                    }
                }
            }

            Assert.IsEmpty(
                violations,
                $"Found {violations.Count} TestCaseSource method name(s) with underscores "
                    + $"(scanned {scannedAssemblies.Count} assembly/ies: "
                    + $"{string.Join(", ", scannedAssemblies)}):\n"
                    + string.Join("\n", violations)
            );
        }

        /// <summary>
        /// Comprehensive test that reports all naming convention violations at once.
        /// </summary>
        [Test]
        public void AllTestNamingConventionsAreFollowed()
        {
            string packagePath = GetPackagePath();
            StringBuilder violations = new();
            int violationCount = 0;

            List<string> scannedAssemblies = ScanTestMethods(
                (assemblyName, type, method) =>
                {
                    if (method.Name.Contains('_'))
                    {
                        string typeName = type.FullName ?? type.Name;
                        violations.AppendLine(
                            $"  - Method: {typeName}.{method.Name} [{assemblyName}]"
                        );
                        ++violationCount;
                    }
                }
            );

            string scannedRoot = ScanTestFiles(
                packagePath,
                (relativePath, lineNumber, line) =>
                {
                    MatchCollection testNameMatches = TestNameAttributePattern.Matches(line);
                    foreach (Match match in testNameMatches)
                    {
                        string testName = match.Groups[1].Value;
                        if (testName.Contains('_'))
                        {
                            violations.AppendLine(
                                $"  - {relativePath}:{lineNumber}: TestName=\"{testName}\""
                            );
                            ++violationCount;
                        }
                    }

                    MatchCollection setNameMatches = SetNameMethodPattern.Matches(line);
                    foreach (Match match in setNameMatches)
                    {
                        string setNameValue = match.Groups[1].Value;
                        if (setNameValue.Contains('_'))
                        {
                            violations.AppendLine(
                                $"  - {relativePath}:{lineNumber}: SetName(\"{setNameValue}\")"
                            );
                            ++violationCount;
                        }
                    }
                }
            );

            Assert.IsTrue(
                0 < scannedAssemblies.Count,
                "No test assembly was scanned, so this would report clean however the methods "
                    + "were named."
            );
            Assert.IsEmpty(
                violations.ToString(),
                $"Found {violationCount} naming convention violation(s) (underscores not allowed). "
                    + $"Scanned {scannedAssemblies.Count} assembly/ies "
                    + $"({string.Join(", ", scannedAssemblies)}) and source tree at {scannedRoot}:\n"
                    + violations
            );
        }
    }
}
