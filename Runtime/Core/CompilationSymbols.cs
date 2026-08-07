// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core
{
    /// <summary>
    /// Preprocessor symbol names used as <see cref="System.Diagnostics.ConditionalAttribute"/>
    /// arguments across the package.
    /// </summary>
    /// <remarks>
    /// These are compile-time constants rather than a logger detail: a
    /// <see cref="System.Diagnostics.ConditionalAttribute"/> is evaluated against the symbols of
    /// the assembly that <i>calls</i> the method, so any API whose only observable effect is a log
    /// needs the same set, and no one of those APIs owns it.
    /// <para><see cref="UnityEditor"/>, <see cref="DevelopmentBuild"/> and <see cref="Debug"/> are
    /// defined by Unity in every assembly, so the package's default logging behaviour needs no
    /// project configuration; the rest are opt-in overrides a consumer sets project-wide in Player
    /// Settings.</para>
    /// </remarks>
    internal static class CompilationSymbols
    {
        /// <summary>Enables every severity of package logging.</summary>
        internal const string EnableUberLogging = "ENABLE_UBERLOGGING";

        /// <summary>Defined by Unity in development player builds.</summary>
        internal const string DevelopmentBuild = "DEVELOPMENT_BUILD";

        /// <summary>Defined by Unity in debug configurations.</summary>
        internal const string Debug = "DEBUG";

        /// <summary>Defined by Unity in every editor assembly.</summary>
        internal const string UnityEditor = "UNITY_EDITOR";

        /// <summary>Enables informational and debug logging only.</summary>
        internal const string DebugLogging = "DEBUG_LOGGING";

        /// <summary>Enables warning logging only.</summary>
        internal const string WarnLogging = "WARN_LOGGING";

        /// <summary>Enables error logging only.</summary>
        internal const string ErrorLogging = "ERROR_LOGGING";
    }
}
