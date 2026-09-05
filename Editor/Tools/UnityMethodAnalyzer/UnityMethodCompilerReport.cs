// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEditor.Compilation;
    using UnityEngine;
    using CompilationAssembly = UnityEditor.Compilation.Assembly;

    [InitializeOnLoad]
    internal static class UnityMethodCompilerReport
    {
        private const string SessionKey = "WallstopStudios.UnityHelpers.UnityMethodCompilerReport";
        private static readonly ReportData Data;
        private static bool IsCompiling;
        private static bool IgnorePlayerCompilation;

        static UnityMethodCompilerReport()
        {
            string saved = SessionState.GetString(SessionKey, string.Empty);
            try
            {
                Data = string.IsNullOrEmpty(saved)
                    ? new ReportData()
                    : JsonUtility.FromJson<ReportData>(saved);
            }
            catch (ArgumentException)
            {
                Data = new ReportData();
            }
            Data ??= new ReportData();
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        internal static IReadOnlyList<CompilerMessage> GetMessages(out string status)
        {
            List<CompilerMessage> messages = new();
            int expected = 0;
            int captured = 0;
            bool errors = false;
            foreach (
                CompilationAssembly assembly in CompilationPipeline.GetAssemblies(
                    AssembliesType.Editor
                )
            )
            {
                expected++;
                foreach (AssemblyReport report in Data.assemblies)
                {
                    if (!string.Equals(report.name, assembly.name, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    captured++;
                    errors |= report.hasErrors;
                    foreach (MessageData message in report.messages)
                    {
                        messages.Add(
                            new CompilerMessage
                            {
                                file = message.file,
                                line = message.line,
                                column = message.column,
                                message = message.message,
                                type = message.error
                                    ? CompilerMessageType.Error
                                    : CompilerMessageType.Warning,
                            }
                        );
                    }
                    break;
                }
            }
            status = DescribeCoverage(
                captured,
                expected,
                IsCompiling || EditorApplication.isCompiling,
                errors
            );
            return messages;
        }

        internal static string DescribeCoverage(
            int captured,
            int expected,
            bool compiling,
            bool errors
        )
        {
            if (compiling)
            {
                return "Compilation running; captured diagnostics are incomplete.";
            }
            if (captured == 0 || expected == 0)
            {
                return "No compiler coverage captured. Recompile Scripts to populate the report.";
            }
            string coverage =
                $"Compiler snapshot: {captured}/{expected} editor assemblies captured.";
            if (errors)
            {
                return coverage
                    + " Compilation errors can prevent analyzer execution; results are incomplete.";
            }
            return captured < expected
                ? coverage + " Partial coverage; recompile scripts to include missing assemblies."
                : coverage
                    + " Current editor defines only. Player-only code and files not included in compilation are outside this report.";
        }

        private static void OnCompilationStarted(object context)
        {
            IgnorePlayerCompilation = BuildPipeline.isBuildingPlayer;
            IsCompiling = !IgnorePlayerCompilation;
        }

        private static void OnAssemblyFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (IgnorePlayerCompilation || BuildPipeline.isBuildingPlayer)
            {
                return;
            }
            string name = Path.GetFileNameWithoutExtension(assemblyPath);
            Data.assemblies.RemoveAll(report =>
                string.Equals(report.name, name, StringComparison.Ordinal)
            );
            AssemblyReport current = new() { name = name };
            foreach (CompilerMessage message in messages)
            {
                bool error = message.type == CompilerMessageType.Error;
                current.hasErrors |= error;
                if (MethodAnalyzer.TryCreateIssue(message, out _))
                {
                    current.messages.Add(
                        new MessageData
                        {
                            file = message.file,
                            line = message.line,
                            column = message.column,
                            message = message.message,
                            error = error,
                        }
                    );
                }
            }
            Data.assemblies.Add(current);
        }

        private static void OnCompilationFinished(object context)
        {
            IsCompiling = false;
            if (IgnorePlayerCompilation)
            {
                IgnorePlayerCompilation = false;
                return;
            }
            SessionState.SetString(SessionKey, JsonUtility.ToJson(Data));
        }

        [Serializable]
        private sealed class ReportData
        {
            public List<AssemblyReport> assemblies = new();
        }

        [Serializable]
        private sealed class AssemblyReport
        {
            public string name;
            public bool hasErrors;
            public List<MessageData> messages = new();
        }

        [Serializable]
        private sealed class MessageData
        {
            public string file;
            public int line;
            public int column;
            public string message;
            public bool error;
        }
    }
#endif
}
