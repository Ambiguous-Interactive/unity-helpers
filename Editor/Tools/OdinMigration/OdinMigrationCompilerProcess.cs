// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using UnityEditor;
    using UnityEditor.Compilation;

    internal sealed class OdinMigrationCompilerProcess : IDisposable
    {
        private const int MaximumDiagnostics = 1024;
        private const int MaximumDiagnosticCharacters = 4096;

        private static readonly char[] ArgumentDelimiters = { '"', '\r', '\n', '\0' };

        internal bool Completed => _process.HasExited && Volatile.Read(ref _closedStreams) == 2;
        internal int ExitCode => _process.ExitCode;
        internal string OutputPath { get; }
        internal string ArgumentsPath { get; }
        internal string ArgumentsChecksum { get; }
        internal IReadOnlyList<string> Diagnostics => _diagnostics;

        private readonly Process _process;
        private readonly List<string> _diagnostics = new List<string>();
        private readonly object _diagnosticLock = new object();
        private int _closedStreams;

        internal OdinMigrationCompilerProcess(
            string outputPath,
            IReadOnlyList<string> sources,
            IReadOnlyList<string> references,
            Assembly assembly
        )
        {
            OutputPath = outputPath;
            string response = outputPath + ".rsp";
            string arguments = Arguments(outputPath, sources, references, assembly);
            ArgumentsPath = response;
            ArgumentsChecksum = OdinMigrationSourceBinder.Checksum(arguments);
            File.WriteAllText(response, arguments, new UTF8Encoding(false));
            ProcessStartInfo start = CompilerStartInfo(
                EditorApplication.applicationContentsPath,
                response
            );
            _process = new Process { StartInfo = start };
            _process.OutputDataReceived += ReceiveDiagnostic;
            _process.ErrorDataReceived += ReceiveDiagnostic;
            try
            {
                if (!_process.Start())
                {
                    throw new InvalidOperationException("Unity's isolated compiler did not start.");
                }
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal static string Arguments(
            string outputPath,
            IReadOnlyList<string> sources,
            IReadOnlyList<string> references,
            Assembly assembly
        )
        {
            StringBuilder result = new StringBuilder();
            result.AppendLine("/nologo").AppendLine("/target:library").AppendLine("/nostdlib+");
            result.Append("/out:").AppendLine(Quote(outputPath));
            result.Append("/define:").AppendLine(string.Join(";", assembly.defines));
            foreach (string reference in references)
            {
                result.Append("/reference:").AppendLine(Quote(reference));
            }
            OdinMigrationCompilerOptionsSnapshot snapshot =
                new OdinMigrationCompilerOptionsSnapshot(assembly.compilerOptions);
            foreach (KeyValuePair<string, object> option in snapshot.values)
            {
                switch (option.Key)
                {
                    case "AllowUnsafeCode":
                        result.AppendLine((bool)option.Value ? "/unsafe+" : "/unsafe-");
                        break;
                    case "CodeOptimization":
                        result.AppendLine(
                            (CodeOptimization)option.Value == CodeOptimization.Release
                                ? "/optimize+"
                                : "/optimize-"
                        );
                        break;
                    case "LanguageVersion":
                        result
                            .Append("/langversion:")
                            .AppendLine(Quote(Convert.ToString(option.Value)));
                        break;
                    case "RoslynAnalyzerRulesetPath":
                        AppendPath(result, "/ruleset:", option.Value as string);
                        break;
                    case "AnalyzerConfigPath":
                        AppendPath(result, "/analyzerconfig:", option.Value as string);
                        break;
                    case "RoslynAnalyzerDllPaths":
                    case "RoslynAdditionalFilePaths":
                        foreach (string path in option.Value as string[] ?? Array.Empty<string>())
                        {
                            AppendPath(
                                result,
                                string.Equals(
                                    option.Key,
                                    "RoslynAnalyzerDllPaths",
                                    StringComparison.Ordinal
                                )
                                    ? "/analyzer:"
                                    : "/additionalfile:",
                                path
                            );
                        }
                        break;
                    case "AdditionalCompilerArguments":
                        foreach (
                            string argument in option.Value as string[] ?? Array.Empty<string>()
                        )
                        {
                            result.AppendLine(Quote(argument));
                        }
                        break;
                    case "EmitReferenceAssembly":
                        if (option.Value is bool emit && emit)
                        {
                            result.Append("/refout:").AppendLine(Quote(outputPath + ".ref.dll"));
                        }
                        break;
                    case "ApiCompatibilityLevel":
                    case "EditorAssembliesCompatibilityLevel":
                    case "ResponseFiles":
                        break;
                    default:
                        throw new InvalidDataException(
                            "An unsupported compiler option was captured: " + option.Key
                        );
                }
            }
            foreach (string source in sources)
            {
                result.AppendLine(Quote(source));
            }
            return result.ToString();
        }

        internal static ProcessStartInfo CompilerStartInfo(string contentsPath, string responsePath)
        {
            bool windows = Path.DirectorySeparatorChar == '\\';
            string compiler = null;
            string runtime = Path.Combine(
                contentsPath,
                "DotNetSdk",
                windows ? "dotnet.exe" : "dotnet"
            );
            string sdkRoot = Path.Combine(contentsPath, "DotNetSdk", "sdk");
            if (Directory.Exists(Path.Combine(contentsPath, "DotNetSdk")))
            {
                if (!File.Exists(runtime) || !Directory.Exists(sdkRoot))
                {
                    throw new FileNotFoundException("Unity's bundled .NET SDK is incomplete.");
                }
                foreach (string directory in Directory.GetDirectories(sdkRoot))
                {
                    if (
                        Regex.IsMatch(
                            Path.GetFileName(directory),
                            @"^[1-9]+[0-9]*\.[0-9]+\.[0-9]+$",
                            RegexOptions.CultureInvariant
                        )
                    )
                    {
                        compiler = Path.Combine(directory, "Roslyn", "bincore", "csc.dll");
                        break;
                    }
                }
                if (compiler == null || !File.Exists(compiler))
                {
                    throw new FileNotFoundException(
                        "Unity's bundled .NET SDK has no supported isolated C# compiler."
                    );
                }
            }
            else
            {
                compiler = Path.Combine(contentsPath, "DotNetSdkRoslyn", "csc.dll");
                runtime = Path.Combine(
                    contentsPath,
                    "netcorerun",
                    windows ? "netcorerun.exe" : "netcorerun"
                );
                if (!File.Exists(runtime))
                {
                    runtime = Path.Combine(
                        contentsPath,
                        "NetCoreRuntime",
                        windows ? "dotnet.exe" : "dotnet"
                    );
                }
                if (!File.Exists(compiler) || !File.Exists(runtime))
                {
                    compiler = Path.Combine(contentsPath, "Tools", "Roslyn", "csc.exe");
                    runtime = windows
                        ? compiler
                        : Path.Combine(contentsPath, "MonoBleedingEdge", "bin", "mono");
                }
            }
            if (!File.Exists(compiler) || !File.Exists(runtime))
            {
                throw new FileNotFoundException(
                    "This Unity installation has no supported isolated C# compiler."
                );
            }
            return new ProcessStartInfo
            {
                FileName = runtime,
                Arguments =
                    (
                        string.Equals(runtime, compiler, StringComparison.Ordinal)
                            ? ""
                            : Quote(compiler) + " "
                    )
                    + "/noconfig @"
                    + Quote(responsePath),
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
        }

        private static void AppendPath(StringBuilder result, string prefix, string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                result.Append(prefix).AppendLine(Quote(OdinMigrationSourceBinder.FullPath(path)));
            }
        }

        private static string Quote(string value)
        {
            if (value == null || 0 <= value.IndexOfAny(ArgumentDelimiters))
            {
                throw new InvalidDataException(
                    "A compiler argument contains an unsupported delimiter."
                );
            }
            return "\"" + value + "\"";
        }

        /// <summary>Stops the compiler and releases its process handle.</summary>
        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    if (!_process.WaitForExit(5000))
                    {
                        throw new TimeoutException(
                            "The isolated compiler did not stop before cleanup."
                        );
                    }
                }
            }
            catch (InvalidOperationException) { }
            finally
            {
                _process.Dispose();
            }
        }

        private void ReceiveDiagnostic(object sender, DataReceivedEventArgs received)
        {
            if (received.Data == null)
            {
                Interlocked.Increment(ref _closedStreams);
                return;
            }
            lock (_diagnosticLock)
            {
                if (_diagnostics.Count < MaximumDiagnostics)
                {
                    _diagnostics.Add(
                        received.Data.Length <= MaximumDiagnosticCharacters
                            ? received.Data
                            : received.Data.Substring(0, MaximumDiagnosticCharacters)
                    );
                }
            }
        }
    }
}
