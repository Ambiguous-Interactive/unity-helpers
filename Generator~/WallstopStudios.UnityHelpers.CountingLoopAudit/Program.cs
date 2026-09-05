// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.CountingLoopAudit
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using WallstopStudios.UnityHelpers.Analyzers;

    internal static class Program
    {
        private const string ControlPath = "CountingLoopAuditReportingControl.cs";

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
                foreach (
                    string requiredReference in new[]
                    {
                        "netstandard.dll",
                        "UnityEngine.CoreModule.dll",
                        "UnityEditor.dll",
                    }
                )
                {
                    if (
                        !references.Any(reference =>
                            Path.GetFileName(reference) == requiredReference
                        )
                    )
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
                    "/Editor/FitTextureSizeWindow.cs",
                    "/Editor/CustomDrawers/SerializableDictionaryPropertyDrawer.cs",
                    "/Editor/CustomDrawers/Utils/ValidationShared.cs",
                    "/Editor/CustomDrawers/SerializableSetPropertyDrawer.cs",
                    "/Runtime/Core/Extension/Partials/UI.cs",
                    "/Runtime/Utils/MatchColliderToSprite.cs",
                    "/Runtime/Visuals/UGUI/EnhancedImage.cs",
                };
                if (
                    requiredSubjects.Any(required =>
                        !subjects.Any(subject =>
                            subject.Replace('\\', '/').EndsWith(required, StringComparison.Ordinal)
                        )
                    ) || subjects.Any(subject => !sources.Contains(subject))
                )
                {
                    throw new InvalidOperationException(
                        "The ten excluded production sources must all be audit subjects."
                    );
                }

                string[] symbols = File.ReadAllText(arguments[2])
                    .Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (!symbols.Contains("UNITY_EDITOR", StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Missing required audit symbol: UNITY_EDITOR"
                    );
                }

                CSharpParseOptions parseOptions = new CSharpParseOptions(
                    LanguageVersion.CSharp9,
                    preprocessorSymbols: symbols
                );
                List<SyntaxTree> trees = sources
                    .Select(source =>
                        CSharpSyntaxTree.ParseText(File.ReadAllText(source), parseOptions, source)
                    )
                    .ToList();
                /*
                    Known host API gaps leave compiler errors. Require WUH013 to report a control
                    beside an unresolved API in this same compilation before certifying subjects.
                */
                trees.Add(
                    CSharpSyntaxTree.ParseText(
                        "internal static class CountingLoopAuditControl { static int Sum(int[] values) { int total = 0; for (int index = 0; index < values.Length; index++) { total += values[index]; } return total; } static void MissingApi() { DeliberatelyUnavailableModernUnityApi(); } }",
                        parseOptions,
                        ControlPath
                    )
                );
                CSharpCompilation compilation = CSharpCompilation.Create(
                    "CountingLoopAudit",
                    trees,
                    references.Select(path => MetadataReference.CreateFromFile(path)),
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                        .WithAllowUnsafe(true)
                        .WithSpecificDiagnosticOptions(
                            ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(
                                "WUH013",
                                ReportDiagnostic.Warn
                            )
                        )
                );
                ImmutableArray<Diagnostic> diagnostics = compilation
                    .WithAnalyzers(
                        ImmutableArray.Create<DiagnosticAnalyzer>(new CountingLoopAnalyzer())
                    )
                    .GetAnalyzerDiagnosticsAsync()
                    .GetAwaiter()
                    .GetResult();
                if (
                    diagnostics.Count(diagnostic =>
                        diagnostic.Id == "WUH013"
                        && diagnostic.Location.SourceTree?.FilePath == ControlPath
                    ) != 1
                )
                {
                    throw new InvalidOperationException(
                        "WUH013 did not report the positive control alongside an unresolved API; the audit cannot certify its subjects."
                    );
                }

                HashSet<string> subjectPaths = new HashSet<string>(
                    subjects,
                    StringComparer.Ordinal
                );
                int findings = 0;
                foreach (Diagnostic diagnostic in diagnostics)
                {
                    if (diagnostic.Id == "AD0001")
                    {
                        throw new InvalidOperationException(diagnostic.ToString());
                    }

                    if (
                        diagnostic.Id != "WUH013"
                        || !subjectPaths.Contains(
                            diagnostic.Location.SourceTree?.FilePath ?? string.Empty
                        )
                    )
                    {
                        continue;
                    }

                    findings++;
                    Console.Error.WriteLine(
                        diagnostic.ToString().Replace("warning WUH013", "error WUH013")
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
            string[] paths = File.ReadAllLines(manifest)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (paths.Length == 0 || paths.Any(path => !File.Exists(path)))
            {
                throw new InvalidOperationException("Missing or empty audit input: " + manifest);
            }

            return paths;
        }
    }
}
