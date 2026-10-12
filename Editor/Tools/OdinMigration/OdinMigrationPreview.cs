// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;

    /// <summary>Holds an immutable migration preview and its private source-write plan.</summary>
    public sealed class OdinMigrationPreview
    {
        /// <summary>Gets whether this preview can still be applied.</summary>
        public bool CanApply => !Applied && OdinMigrationTool.CanApply(State);

        /// <summary>Gets whether the scan was cancelled.</summary>
        public bool Cancelled => State.Cancelled;

        /// <summary>Gets the number of source files analyzed.</summary>
        public int AnalyzedFiles => State.AnalyzedFiles;

        /// <summary>Gets the number of generated source files skipped.</summary>
        public int GeneratedFiles => State.GeneratedFiles;

        /// <summary>Gets the number of proven replacements.</summary>
        public int ReplacementCount => State.Replacements;

        /// <summary>Gets the number of source files with planned writes.</summary>
        public int ChangedFileCount => State.Plans.Count;

        /// <summary>Gets source findings by project-relative asset path.</summary>
        public IReadOnlyDictionary<string, OdinMigrationAnalysis> SourceAnalyses { get; }

        /// <summary>Gets serialized-data findings by project-relative asset path.</summary>
        public IReadOnlyDictionary<
            string,
            IReadOnlyList<OdinMigrationFinding>
        > SerializedAnalyses { get; }

        /// <summary>Gets source scan failures that prevent applying the preview.</summary>
        public IReadOnlyList<string> Failures { get; }

        /// <summary>Gets serialized-data scan failures requiring manual review.</summary>
        public IReadOnlyList<string> SerializedScanFailures { get; }

        /// <summary>Gets the number of serialized assets inspected.</summary>
        public int SerializedAssetsScanned => State.SerializedAssetsScanned;

        /// <summary>Gets the complete human-readable preview report.</summary>
        public string Report { get; }

        internal OdinMigrationTool.ScanResult State { get; }

        internal string ProjectRoot { get; }

        internal bool Applied { get; set; }

        internal OdinMigrationPreview(string projectRoot, OdinMigrationTool.ScanResult state)
        {
            ProjectRoot = projectRoot;
            State = state;
            Dictionary<string, OdinMigrationAnalysis> sources = new Dictionary<
                string,
                OdinMigrationAnalysis
            >(StringComparer.Ordinal);
            foreach (OdinMigrationTool.FileAnalysis file in state.Analyses)
            {
                sources.Add(file.AssetPath, file.Analysis);
            }
            SourceAnalyses = new ReadOnlyDictionary<string, OdinMigrationAnalysis>(sources);
            Dictionary<string, IReadOnlyList<OdinMigrationFinding>> serialized = new Dictionary<
                string,
                IReadOnlyList<OdinMigrationFinding>
            >(StringComparer.Ordinal);
            foreach (OdinMigrationTool.SerializedFileAnalysis file in state.SerializedAnalyses)
            {
                serialized.Add(
                    file.AssetPath,
                    new List<OdinMigrationFinding>(file.Findings).AsReadOnly()
                );
            }
            SerializedAnalyses = new ReadOnlyDictionary<
                string,
                IReadOnlyList<OdinMigrationFinding>
            >(serialized);
            Failures = new List<string>(state.Failures).AsReadOnly();
            SerializedScanFailures = new List<string>(state.SerializedScanFailures).AsReadOnly();
            Report = OdinMigrationTool.BuildReport(state);
        }
    }
}
