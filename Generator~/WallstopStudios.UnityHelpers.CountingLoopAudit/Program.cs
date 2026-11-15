// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.CountingLoopAudit
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using WallstopStudios.UnityHelpers.Analyzers;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal static class Program
    {
        private const int MaxLinearSubjectCount = 16;
        private const string ControlPath = "CountingLoopAuditReportingControl.cs";

        internal static bool HasRequiredSubjects(
            string[] sources,
            string[] subjects,
            string[] requiredSubjects
        )
        {
            if (subjects.Length <= MaxLinearSubjectCount)
            {
                foreach (string subject in subjects)
                {
                    if (Array.IndexOf(sources, subject) < 0)
                    {
                        return false;
                    }
                }
            }
            else
            {
                HashSet<string> unmatched = new HashSet<string>(subjects, StringComparer.Ordinal);
                foreach (string source in sources)
                {
                    unmatched.Remove(source);
                    if (unmatched.Count == 0)
                    {
                        break;
                    }
                }
                if (unmatched.Count != 0)
                {
                    return false;
                }
            }
            bool[] found = new bool[requiredSubjects.Length];
            int remaining = requiredSubjects.Length;
            foreach (string subject in subjects)
            {
                string normalized = subject.SanitizePath();
                for (int index = 0; index < requiredSubjects.Length; ++index)
                {
                    if (
                        !found[index]
                        && normalized.EndsWith(requiredSubjects[index], StringComparison.Ordinal)
                    )
                    {
                        found[index] = true;
                        --remaining;
                    }
                }
            }
            return remaining == 0;
        }

        private static int Main(string[] arguments)
        {
            try
            {
                if (arguments.Length != 4)
                {
                    throw new ArgumentException(
                        "Expected source, reference, symbol, and subject manifests."
                    );
                }

                string[] sources = ReadPaths(arguments[0]);
                string[] references = ReadPaths(arguments[1]);
                string[] subjects = ReadPaths(arguments[3]);
                HashSet<string> referenceNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (string reference in references)
                {
                    referenceNames.Add(Path.GetFileName(reference));
                }
                foreach (
                    string requiredReference in new[]
                    {
                        "netstandard.dll",
                        "UnityEngine.CoreModule.dll",
                        "UnityEditor.dll",
                    }
                )
                {
                    if (!referenceNames.Contains(requiredReference))
                    {
                        throw new InvalidOperationException(
                            "Missing required audit reference: " + requiredReference
                        );
                    }
                }

                string[] requiredSubjects =
                {
                    "/Editor/Visuals/EnhancedImageEditor.cs",
                    "/Editor/CustomEditors/MatchColliderToSpriteEditor.cs",
                    "/Editor/AnimationEventEditor.cs",
                    "/Editor/CustomDrawers/SerializableDictionaryPropertyDrawer.cs",
                    "/Editor/CustomDrawers/Utils/ValidationShared.cs",
                    "/Editor/CustomDrawers/SerializableSetPropertyDrawer.cs",
                    "/Runtime/Core/Extension/Partials/UI.cs",
                    "/Runtime/Utils/MatchColliderToSprite.cs",
                    "/Runtime/Visuals/UGUI/EnhancedImage.cs",
                };
                if (!HasRequiredSubjects(sources, subjects, requiredSubjects))
                {
                    throw new InvalidOperationException(
                        "The nine excluded production sources must all be audit subjects."
                    );
                }

                string[] symbols = File.ReadAllText(arguments[2])
                    .Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (Array.IndexOf(symbols, "UNITY_EDITOR") < 0)
                {
                    throw new InvalidOperationException(
                        "Missing required audit symbol: UNITY_EDITOR"
                    );
                }

                CSharpParseOptions parseOptions = new CSharpParseOptions(
                    LanguageVersion.CSharp9,
                    preprocessorSymbols: symbols
                );
                List<SyntaxTree> trees = new List<SyntaxTree>(sources.Length + 1);
                foreach (string source in sources)
                {
                    trees.Add(
                        CSharpSyntaxTree.ParseText(File.ReadAllText(source), parseOptions, source)
                    );
                }
                List<MetadataReference> metadataReferences = new List<MetadataReference>(
                    references.Length
                );
                foreach (string reference in references)
                {
                    metadataReferences.Add(MetadataReference.CreateFromFile(reference));
                }
                /*
                    Known host API gaps leave compiler errors. Require each enabled policy to report a control
                    beside an unresolved API in this same compilation before certifying subjects.
                */
                trees.Add(
                    CSharpSyntaxTree.ParseText(
                        "internal static class CountingLoopAuditControl { static int Sum(int[] values) { int total = 0; for (int index = 0; index < values.Length; index++) { total += values[index]; } return total; } static int Snapshot(System.Collections.Generic.ICollection<int> values) { if (values.Count > 0) { return values.Count; } return 0; } static bool Logic(bool left, bool right) { return left | right; } static void FreshPool() { using var lease = WallstopStudios.UnityHelpers.Utils.Buffers<int>.List.Get(out var items); items.Clear(); } static void MissingApi() { DeliberatelyUnavailableModernUnityApi(); } } public struct EqualityAuditControl { }",
                        parseOptions,
                        ControlPath
                    )
                );
                CSharpCompilation compilation = CSharpCompilation.Create(
                    "CountingLoopAudit",
                    trees,
                    metadataReferences,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                        .WithAllowUnsafe(true)
                        .WithSpecificDiagnosticOptions(
                            ImmutableDictionary<string, ReportDiagnostic>
                                .Empty.Add("WUH013", ReportDiagnostic.Warn)
                                .Add("WUH019", ReportDiagnostic.Warn)
                                .Add("WUH021", ReportDiagnostic.Warn)
                                .Add("WUH023", ReportDiagnostic.Warn)
                                .Add("WUH026", ReportDiagnostic.Warn)
                                .Add("WUH028", ReportDiagnostic.Warn)
                        )
                );
                ImmutableArray<Diagnostic> diagnostics = compilation
                    .WithAnalyzers(
                        ImmutableArray.Create<DiagnosticAnalyzer>(
                            new CountingLoopAnalyzer(),
                            new LoopBoundAnalyzer(),
                            new RepeatedCollectionCountAnalyzer(),
                            new PublicStructEqualityAnalyzer(),
                            new BooleanBitwiseAnalyzer(),
                            new RedundantPooledClearAnalyzer()
                        )
                    )
                    .GetAnalyzerDiagnosticsAsync()
                    .GetAwaiter()
                    .GetResult();
                Dictionary<string, int> controlCounts = new Dictionary<string, int>(
                    StringComparer.Ordinal
                );
                foreach (Diagnostic diagnostic in diagnostics)
                {
                    if (
                        string.Equals(
                            diagnostic.Location.SourceTree?.FilePath,
                            ControlPath,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        if (controlCounts.TryGetValue(diagnostic.Id, out int count))
                        {
                            controlCounts[diagnostic.Id] = count + 1;
                        }
                        else
                        {
                            controlCounts.Add(diagnostic.Id, 1);
                        }
                    }
                }
                foreach (
                    string diagnosticId in new[]
                    {
                        "WUH013",
                        "WUH019",
                        "WUH021",
                        "WUH023",
                        "WUH026",
                        "WUH028",
                    }
                )
                {
                    if (!controlCounts.TryGetValue(diagnosticId, out int count) || count != 1)
                    {
                        throw new InvalidOperationException(
                            diagnosticId
                                + " did not report the positive control alongside an unresolved API; the audit cannot certify its subjects."
                        );
                    }
                }

                HashSet<string> subjectPaths = new HashSet<string>(
                    subjects,
                    StringComparer.Ordinal
                );
                int findings = 0;
                foreach (Diagnostic diagnostic in diagnostics)
                {
                    if (string.Equals(diagnostic.Id, "AD0001", System.StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(diagnostic.ToString());
                    }

                    if (
                        (
                            !string.Equals(diagnostic.Id, "WUH013", StringComparison.Ordinal)
                            && !string.Equals(diagnostic.Id, "WUH019", StringComparison.Ordinal)
                            && !string.Equals(diagnostic.Id, "WUH021", StringComparison.Ordinal)
                            && !string.Equals(diagnostic.Id, "WUH023", StringComparison.Ordinal)
                            && !string.Equals(diagnostic.Id, "WUH026", StringComparison.Ordinal)
                            && !string.Equals(diagnostic.Id, "WUH028", StringComparison.Ordinal)
                        )
                        || !subjectPaths.Contains(
                            diagnostic.Location.SourceTree?.FilePath ?? string.Empty
                        )
                    )
                    {
                        continue;
                    }

                    ++findings;
                    Console.Error.WriteLine(
                        diagnostic
                            .ToString()
                            .Replace("warning " + diagnostic.Id, "error " + diagnostic.Id)
                    );
                }

                Console.WriteLine(
                    $"[counting-loop-audit] {subjects.Length} excluded-source subjects, {references.Length} references, reporting control passed, {findings} findings."
                );
                return findings == 0 ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("[counting-loop-audit] " + exception.Message);
                return 1;
            }
        }

        private static string[] ReadPaths(string manifest)
        {
            List<string> paths = new List<string>();
            HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(manifest))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                string path = Path.GetFullPath(line);
                if (unique.Add(path))
                {
                    if (!File.Exists(path))
                    {
                        throw new InvalidOperationException(
                            "Missing or empty audit input: " + manifest
                        );
                    }
                    paths.Add(path);
                }
            }
            if (paths.Count == 0)
            {
                throw new InvalidOperationException("Missing or empty audit input: " + manifest);
            }
            return paths.ToArray();
        }
    }
}
