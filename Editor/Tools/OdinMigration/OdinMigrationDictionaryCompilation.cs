// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using UnityEditor;
    using UnityEditor.Compilation;
    using WallstopStudios.UnityHelpers.Core.Extension;

    /// <summary>Tracks isolated compilation of proposed sources and their current editor callers.</summary>
    public sealed class OdinMigrationDictionaryCompilation : IDisposable
    {
        private const int MaximumDiagnostics = 1024;

        private static OdinMigrationDictionaryCompilation _active;

        /// <summary>Gets whether compilation and staging cleanup have finished.</summary>
        public bool Completed { get; private set; }

        /// <summary>Gets whether every selected assembly compiled against unchanged inputs.</summary>
        public bool Succeeded { get; private set; }

        /// <summary>Gets whether cancellation was requested.</summary>
        public bool Cancelled { get; private set; }

        /// <summary>Gets the compiler or validation failure.</summary>
        public string Failure { get; private set; }

        /// <summary>Gets compiler messages from completed assemblies.</summary>
        public IReadOnlyList<string> Diagnostics { get; }

        /// <summary>Gets assembly names in dependency order.</summary>
        public IReadOnlyList<string> Assemblies { get; }

        /// <summary>Gets assemblies that compiled successfully against their candidate dependencies.</summary>
        public IReadOnlyList<string> CompiledAssemblies { get; }

        private readonly OdinMigrationCompilationPlan _plan;
        private readonly string _directory;
        private readonly List<string> _diagnostics = new List<string>();
        private readonly List<string> _compiledAssemblies = new List<string>();
        private readonly Dictionary<string, string> _stagedHashes = new Dictionary<string, string>(
            OdinMigrationCompilationPlan.PathComparer
        );
        private readonly Dictionary<string, string> _outputs = new Dictionary<string, string>(
            OdinMigrationCompilationPlan.PathComparer
        );
#pragma warning disable CS0618 // Unity 2021-2022 require this compiler API; Unity 6 still supports it.
        private AssemblyBuilder _builder;
#pragma warning restore CS0618
        private int _nextAssembly;

        private OdinMigrationDictionaryCompilation(OdinMigrationCompilationPlan plan)
        {
            _plan = plan;
            _directory = OdinMigrationSourceBinder.FullPath(
                Path.Combine(
                    "Library",
                    "WallstopOdinMigrationCompile",
                    Guid.NewGuid().ToString("N")
                )
            );
            List<string> names = new List<string>();
            foreach (Assembly assembly in plan.assemblies.AsValueEnumerableRequired())
            {
                names.Add(assembly.name);
            }
            Assemblies = names.AsReadOnly();
            Diagnostics = _diagnostics.AsReadOnly();
            CompiledAssemblies = _compiledAssemblies.AsReadOnly();
        }

        internal static bool TryStart(
            IReadOnlyDictionary<string, OdinMigrationSourceProposal> sources,
            out OdinMigrationDictionaryCompilation compilation,
            out string failure
        )
        {
            try
            {
                if (
                    _active != null
                    || EditorApplication.isCompiling
                    || EditorApplication.isUpdating
                )
                {
                    throw new InvalidOperationException(
                        "Wait for the current editor compilation or migration compiler to finish."
                    );
                }
                OdinMigrationCompilationPlan plan = new OdinMigrationCompilationPlan(
                    sources,
                    CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                );
                OdinMigrationDictionaryCompilation result = new OdinMigrationDictionaryCompilation(
                    plan
                );
                _active = result;
                EditorApplication.update += result.Update;
                AssemblyReloadEvents.beforeAssemblyReload += result.BeforeReload;
                compilation = result;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                compilation = null;
                failure = exception.Message;
                return false;
            }
        }

        /// <summary>Checks whether all source, reference, analyzer, and compiler configuration inputs remain current.</summary>
        public bool TryValidateCurrent(out string failure)
        {
            try
            {
                if (!Completed || !Succeeded)
                {
                    throw new InvalidOperationException(
                        "A successful completed compilation is required."
                    );
                }
                _plan.ValidateCurrent();
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        /// <summary>Cancels remaining builds and cleans staging after any active compiler returns.</summary>
        /// <remarks>Call on the editor thread. Cancellation cannot interrupt Unity's active assembly compiler.</remarks>
        public void Dispose()
        {
            if (!Completed)
            {
                Cancelled = true;
                if (_builder == null)
                {
                    Finish("Migration compilation was cancelled.");
                }
            }
        }

        private void Update()
        {
            if (Completed || _builder != null)
            {
                return;
            }
            try
            {
                if (Cancelled)
                {
                    Finish("Migration compilation was cancelled.");
                    return;
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    throw new InvalidOperationException(
                        "The editor began importing or compiling during migration validation."
                    );
                }
                if (_nextAssembly == _plan.assemblies.Count)
                {
                    _plan.ValidateCurrent();
                    ValidateStaging();
                    Finish(null);
                    return;
                }
                Assembly assembly = _plan.assemblies[_nextAssembly];
                string directory = Path.Combine(
                    _directory,
                    _nextAssembly.ToString(System.Globalization.CultureInfo.InvariantCulture)
                );
                Directory.CreateDirectory(directory);
                List<string> sources = new List<string>();
                int sourceIndex = 0;
                foreach (string source in assembly.sourceFiles)
                {
                    string originalPath = OdinMigrationSourceBinder.FullPath(source);
                    string text = OdinMigrationSourceBinder.ReadSource(originalPath);
                    if (
                        !_plan.sourceTextHashes.TryGetValue(
                            originalPath,
                            out string expectedTextHash
                        )
                        || !string.Equals(
                            expectedTextHash,
                            OdinMigrationSourceBinder.Checksum(text),
                            StringComparison.Ordinal
                        )
                    )
                    {
                        throw new InvalidDataException(
                            "A source changed before its isolated compiler copy was made."
                        );
                    }
                    if (
                        _plan.proposals.TryGetValue(
                            originalPath,
                            out OdinMigrationSourceProposal proposal
                        )
                    )
                    {
                        if (!string.Equals(text, proposal.OriginalSource, StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                "A proposed source changed before compilation."
                            );
                        }
                        text = proposal.ProposedSource;
                    }
                    string targetDirectory = Path.Combine(
                        directory,
                        sourceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    );
                    Directory.CreateDirectory(targetDirectory);
                    string target = Path.Combine(targetDirectory, Path.GetFileName(source));
                    File.WriteAllText(target, text, new UTF8Encoding(false));
                    _stagedHashes.Add(target, OdinMigrationSourceBinder.Checksum(text));
                    sources.Add(target);
                    ++sourceIndex;
                }
                ValidateStaging();
                List<string> references = new List<string>();
                foreach (string reference in assembly.allReferences)
                {
                    string path = OdinMigrationSourceBinder.FullPath(reference);
                    references.Add(_outputs.TryGetValue(path, out string output) ? output : path);
                }
                string candidate = Path.Combine(directory, assembly.name + ".dll");
#pragma warning disable CS0618 // Retain the same compiler path across supported Unity versions.
                AssemblyBuilder builder = new AssemblyBuilder(candidate, sources.ToArray())
                {
                    compilerOptions = assembly.compilerOptions,
                    additionalDefines = assembly.defines,
                    additionalReferences = references.ToArray(),
                    flags = AssemblyBuilderFlags.EditorAssembly,
                };
#pragma warning restore CS0618
                HashSet<string> exactReferences = new HashSet<string>(
                    references,
                    OdinMigrationCompilationPlan.PathComparer
                );
                List<string> excluded = new List<string>();
                foreach (string reference in builder.defaultReferences)
                {
                    if (!exactReferences.Contains(OdinMigrationSourceBinder.FullPath(reference)))
                    {
                        excluded.Add(reference);
                    }
                }
                builder.excludeReferences = excluded.ToArray();
                HashSet<string> exactDefines = new HashSet<string>(
                    assembly.defines,
                    StringComparer.Ordinal
                );
                foreach (string define in builder.defaultDefines)
                {
                    if (!exactDefines.Contains(define))
                    {
                        throw new InvalidDataException(
                            "Unity's temporary compiler supplies an unexpected define: " + define
                        );
                    }
                }
                builder.buildFinished += BuildFinished;
                _builder = builder;
                if (!builder.Build())
                {
                    builder.buildFinished -= BuildFinished;
                    _builder = null;
                    throw new InvalidOperationException(
                        "Unity declined the isolated assembly compilation."
                    );
                }
            }
            catch (Exception exception)
            {
                Finish(exception.Message);
            }
        }

        private void BuildFinished(string path, CompilerMessage[] messages)
        {
            try
            {
                _builder.buildFinished -= BuildFinished;
                _builder = null;
                bool failed = false;
                foreach (CompilerMessage message in messages)
                {
                    if (_diagnostics.Count < MaximumDiagnostics)
                    {
                        _diagnostics.Add(message.message);
                    }
                    failed |= message.type == CompilerMessageType.Error;
                }
                if (Cancelled || failed || !File.Exists(path))
                {
                    Finish(
                        Cancelled
                            ? "Migration compilation was cancelled."
                            : "The proposed source or a dependent caller failed compilation."
                    );
                    return;
                }
                Assembly assembly = _plan.assemblies[_nextAssembly];
                ValidateStaging();
                _stagedHashes.Add(path, OdinMigrationCompilationPlan.HashFile(path));
                _outputs.Add(OdinMigrationSourceBinder.FullPath(assembly.outputPath), path);
                _compiledAssemblies.Add(assembly.name);
                ++_nextAssembly;
            }
            catch (Exception exception)
            {
                Finish(exception.Message);
            }
        }

        private void BeforeReload()
        {
            Cancelled = true;
            Finish("The editor domain reloaded during migration compilation.");
        }

        private void ValidateStaging()
        {
            foreach (KeyValuePair<string, string> entry in _stagedHashes)
            {
                if (
                    !string.Equals(
                        entry.Value,
                        OdinMigrationCompilationPlan.HashFile(entry.Key),
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new InvalidDataException(
                        "An isolated compiler input changed in staging."
                    );
                }
            }
        }

        private void Finish(string failure)
        {
            if (Completed)
            {
                return;
            }
            EditorApplication.update -= Update;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            if (_builder != null)
            {
                _builder.buildFinished -= BuildFinished;
                _builder = null;
            }
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, true);
                }
            }
            catch (Exception exception)
            {
                failure = (failure ?? "") + " Staging cleanup failed: " + exception.Message;
            }
            Failure = failure;
            Succeeded = failure == null && !Cancelled;
            Completed = true;
            if (ReferenceEquals(_active, this))
            {
                _active = null;
            }
        }
    }
}
