// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using NUnit.Framework;

    [TestFixture]
    public sealed class AnalyzerPolicyRulesetCompilerTests
    {
        private const string ProbeSource =
            @"
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WallstopStudios.UnityHelpers.Core.Helper;
using WallstopStudios.UnityHelpers.Core.Extension;
namespace WallstopStudios.UnityHelpers.Core.Helper {
    internal static class DurableFile {
        public static bool TryWriteAllBytes(string path, byte[] bytes, out Exception error) {
            File.WriteAllBytes(path, bytes); error = null; return true;
        }
    }
}
namespace WallstopStudios.UnityHelpers.Core.Extension {
    internal static class ProbeExtensions {
        public static IReadOnlyList<T> AsValueEnumerableRequired<T>(this IReadOnlyList<T> values) { return values; }
    }
}
namespace WallstopStudios.UnityHelpers.Editor.Tools {
    internal readonly struct AnalyzerPolicy {
        public readonly string Id;
        public AnalyzerPolicy(string id) { Id = id; }
    }
    public static class PolicyProbe {
        public static void Main(string[] args) {
            Console.WriteLine(Execute(args[0], int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture)));
        }
        public static string Execute(string path, int operation) {
            AnalyzerPolicy[] policies = { new AnalyzerPolicy(""WUH001""), new AnalyzerPolicy(""WUH010"") };
            string message;
            if (operation == 3) {
                bool read = AnalyzerPolicyRuleset.TryRead(path, policies, out AnalyzerPolicyState state, out message);
                return read ? state.ToString() : ""ReadFailure"";
            }
            if (operation == 4) {
                return AnalyzerPolicyRuleset.TryReadActions(path, policies, new Dictionary<string, string>(), out message)
                    ? ""ActionsAccepted"" : ""ActionsRefused"";
            }
            bool written = operation == 2
                ? AnalyzerPolicyRuleset.TryWriteSeverity(path, ""WUH010"", ""Error"", policies, out message)
                : AnalyzerPolicyRuleset.TryWrite(path, operation == 0 ? AnalyzerPolicyState.Enabled : AnalyzerPolicyState.Disabled, policies, out message);
            return written ? ""Written"" : message;
        }
    }
}
";

        private string _assemblyDirectory;
        private string _executable;
        private string _directory;

        private static string CompileActualRulesetWriter(string outputDirectory)
        {
            DirectoryInfo directory = new(TestContext.CurrentContext.TestDirectory);
            string sourcePath = null;
            while (directory != null)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "Editor",
                    "Tools",
                    "AnalyzerPolicyWindow.cs"
                );
                if (File.Exists(candidate))
                {
                    sourcePath = candidate;
                    break;
                }
                directory = directory.Parent;
            }
            Assert.That(
                sourcePath,
                Is.Not.Null,
                "The actual ruleset writer source must be present."
            );
            CSharpParseOptions options = new(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[] { "UNITY_EDITOR" }
            );
            SyntaxNode source = CSharpSyntaxTree
                .ParseText(File.ReadAllText(sourcePath), options)
                .GetRoot();
            List<string> declarations = new();
            foreach (SyntaxNode node in source.DescendantNodes())
            {
                if (
                    node is ClassDeclarationSyntax type
                    && string.Equals(
                        type.Identifier.ValueText,
                        "AnalyzerPolicyRuleset",
                        StringComparison.Ordinal
                    )
                )
                {
                    declarations.Add(type.ToString());
                }
                else if (
                    node is EnumDeclarationSyntax enumeration
                    && string.Equals(
                        enumeration.Identifier.ValueText,
                        "AnalyzerPolicyState",
                        StringComparison.Ordinal
                    )
                )
                {
                    declarations.Add(enumeration.ToString());
                }
            }
            Assert.That(declarations.Count, Is.EqualTo(2));
            string compiledSource =
                ProbeSource
                + "\nnamespace WallstopStudios.UnityHelpers.Editor.Tools {\n"
                + string.Join("\n", declarations)
                + "\n}";
            List<MetadataReference> references = new();
            string trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            Assert.That(trustedAssemblies, Is.Not.Null.And.Not.Empty);
            foreach (string path in trustedAssemblies.Split(Path.PathSeparator))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                "RulesetProbe" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(compiledSource, options) },
                references,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication)
            );
            using MemoryStream output = new();
            Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(output);
            Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
            string executable = Path.Combine(outputDirectory, "RulesetProbe.dll");
            File.WriteAllBytes(executable, output.ToArray());
            File.WriteAllText(
                Path.Combine(outputDirectory, "RulesetProbe.runtimeconfig.json"),
                "{\"runtimeOptions\":{\"tfm\":\"net9.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"9.0.0\"}}}"
            );
            return executable;
        }

        private static void AssertRule(RuleSet rules, string id, ReportDiagnostic expected)
        {
            Assert.That(
                rules.SpecificDiagnosticOptions.TryGetValue(id, out ReportDiagnostic actual),
                Is.True,
                id
            );
            Assert.That(actual, Is.EqualTo(expected), id);
        }

        [OneTimeSetUp]
        public void CompileWriter()
        {
            _assemblyDirectory = Path.Combine(
                Path.GetTempPath(),
                nameof(AnalyzerPolicyRulesetCompilerTests) + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_assemblyDirectory);
            _executable = CompileActualRulesetWriter(_assemblyDirectory);
        }

        [OneTimeTearDown]
        public void RemoveCompilerOutput()
        {
            Directory.Delete(_assemblyDirectory, true);
        }

        [SetUp]
        public void CreateDirectory()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                nameof(AnalyzerPolicyRulesetCompilerTests),
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void RemoveDirectory()
        {
            Directory.Delete(_directory, true);
        }

        [TestCase(0, false, ReportDiagnostic.Warn)]
        [TestCase(1, false, ReportDiagnostic.Suppress)]
        [TestCase(2, false, ReportDiagnostic.Error)]
        [TestCase(0, true, ReportDiagnostic.Warn)]
        [TestCase(1, true, ReportDiagnostic.Suppress)]
        [TestCase(2, true, ReportDiagnostic.Error)]
        public void WrittenPoliciesLoadWithTheCompilerReader(
            int operation,
            bool existingNamespacedFile,
            ReportDiagnostic expected
        )
        {
            string path = Path.Combine(_directory, "Default.ruleset");
            if (existingNamespacedFile)
            {
                File.WriteAllText(
                    Path.Combine(_directory, "Base.ruleset"),
                    "<RuleSet Name=\"Base\" ToolsVersion=\"15.0\"><Rules AnalyzerId=\"Other\" RuleNamespace=\"Other\"><Rule Id=\"OTHER001\" Action=\"Warning\"/></Rules></RuleSet>"
                );
                File.WriteAllText(
                    path,
                    "<RuleSet Name=\"Existing\" ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><Include Path=\"Base.ruleset\" Action=\"Default\"/><Rules AnalyzerId=\"Other\" RuleNamespace=\"Other\"><Rule Id=\"CS0618\" Action=\"Error\"/></Rules></RuleSet>"
                );
            }

            Assert.That(Execute(path, operation), Is.EqualTo("Written"));
            RuleSet rules = RuleSet.LoadEffectiveRuleSetFromFile(path);
            Assert.That(rules, Is.Not.Null);
            AssertRule(rules, "WUH010", expected);
            if (existingNamespacedFile)
            {
                AssertRule(rules, "CS0618", ReportDiagnostic.Error);
                AssertRule(rules, "OTHER001", ReportDiagnostic.Warn);
            }
            Assert.That(Execute(path, operation), Is.EqualTo("Written"));
            AssertRule(RuleSet.LoadEffectiveRuleSetFromFile(path), "WUH010", expected);
        }

        [Test]
        public void PrefixedLegacyRootWithUnusedDefaultNamespaceIsRepaired()
        {
            string path = Path.Combine(_directory, "Default.ruleset");
            File.WriteAllText(
                path,
                "<old:RuleSet xmlns:old=\"http://schemas.microsoft.com/developer/msbuild/2003\" xmlns=\"urn:unused\" Name=\"Existing\" ToolsVersion=\"15.0\"><old:Rules AnalyzerId=\"Other\" RuleNamespace=\"Other\"><old:Rule Id=\"CS0618\" Action=\"Error\"/></old:Rules></old:RuleSet>"
            );

            Assert.That(Execute(path, 0), Is.EqualTo("Written"));
            RuleSet rules = RuleSet.LoadEffectiveRuleSetFromFile(path);
            AssertRule(rules, "WUH001", ReportDiagnostic.Warn);
            AssertRule(rules, "CS0618", ReportDiagnostic.Error);
        }

        [Test]
        public void NamespacedRulesetsReportDriftUntilExplicitlyRepaired()
        {
            string path = Path.Combine(_directory, "Default.ruleset");
            const string invalid =
                "<RuleSet Name=\"Existing\" ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><Rules AnalyzerId=\"WallstopStudios.UnityHelpers.Analyzers\" RuleNamespace=\"WallstopStudios.UnityHelpers.Analyzers\"><Rule Id=\"WUH001\" Action=\"Warning\"/><Rule Id=\"WUH010\" Action=\"Warning\"/></Rules></RuleSet>";
            File.WriteAllText(path, invalid);

            Assert.That(Execute(path, 3), Is.EqualTo("Drifted"));
            Assert.That(Execute(path, 4), Is.EqualTo("ActionsRefused"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(invalid));
            Assert.That(Execute(path, 0), Is.EqualTo("Written"));
            Assert.That(Execute(path, 3), Is.EqualTo("Enabled"));
            Assert.That(Execute(path, 4), Is.EqualTo("ActionsAccepted"));
        }

        private string Execute(string path, int operation)
        {
            ProcessStartInfo start = new("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(_executable);
            start.ArgumentList.Add(path);
            start.ArgumentList.Add(operation.ToString(CultureInfo.InvariantCulture));
            using Process process = Process.Start(start);
            System.Threading.Tasks.Task<string> output = process.StandardOutput.ReadToEndAsync();
            System.Threading.Tasks.Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000))
            {
                process.Kill(true);
                process.WaitForExit();
                Assert.Fail("The isolated ruleset writer exceeded its deadline.");
            }
            Assert.That(process.ExitCode, Is.Zero, error.GetAwaiter().GetResult());
            return output.GetAwaiter().GetResult().Trim();
        }
    }
}
