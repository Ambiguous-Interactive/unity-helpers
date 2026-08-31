// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;

    /// <summary>One type or script asset whose binding is missing or decided by accident.</summary>
    public readonly struct MonoScriptBindingFinding
    {
        /// <summary>Which of the two rules the subject breaks.</summary>
        public MonoScriptBindingProblem Problem { get; }

        /// <summary>The type that cannot be authored, or the type a file misnames.</summary>
        public Type Subject { get; }

        /// <summary>The script asset involved, or <c>null</c> when no script binds the type.</summary>
        public string ScriptPath { get; }

        /// <summary>Initializes a new instance of the <see cref="MonoScriptBindingFinding"/> struct.</summary>
        /// <param name="problem">Which of the two rules the subject breaks.</param>
        /// <param name="subject">The type that cannot be authored, or the type a file misnames.</param>
        /// <param name="scriptPath">The script asset involved, or <c>null</c>.</param>
        public MonoScriptBindingFinding(
            MonoScriptBindingProblem problem,
            Type subject,
            string scriptPath
        )
        {
            Problem = problem;
            Subject = subject;
            ScriptPath = scriptPath;
        }

        /// <summary>Renders the finding as the consequence rather than the rule.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            if (Problem == MonoScriptBindingProblem.FileNameMismatch)
            {
                return $"{ScriptPath} binds {Subject?.FullName}, which the file is not named after. "
                    + "One type added above this one moves the binding, and every reference to it "
                    + "becomes a missing script.";
            }

            return $"{Subject?.FullName} has no MonoScript, so it cannot be dragged onto a "
                + "GameObject or created as an asset.";
        }
    }
#endif
}
