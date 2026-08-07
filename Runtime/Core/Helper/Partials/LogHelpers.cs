// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using Extension;
    using UnityEngine;

    public static partial class Helpers
    {
        /// <summary>
        /// Logs a warning that a named member was expected to be assigned but was not.
        /// </summary>
        /// <param name="component">The Unity Object the missing assignment belongs to.</param>
        /// <param name="name">The name of the unassigned member.</param>
        /// <remarks>
        /// Thread-safe: Yes.
        /// Allocations: None when logging is disabled -- this method is
        /// <see cref="System.Diagnostics.ConditionalAttribute"/>, so the compiler removes the
        /// entire call site including the receiver and <paramref name="name"/>.
        /// Configuration: carries the same symbol set as
        /// <see cref="WallstopStudiosLogger.LogWarn"/>, which it forwards to.
        /// </remarks>
        [System.Diagnostics.Conditional(CompilationSymbols.EnableUberLogging)]
        [System.Diagnostics.Conditional(CompilationSymbols.DevelopmentBuild)]
        [System.Diagnostics.Conditional(CompilationSymbols.Debug)]
        [System.Diagnostics.Conditional(CompilationSymbols.UnityEditor)]
        [System.Diagnostics.Conditional(CompilationSymbols.WarnLogging)]
        public static void LogNotAssigned(this Object component, string name)
        {
            LogNotAssignedCore(component, name);
        }

        // Not a call to the [Conditional] LogWarn, and not a call to the [Conditional]
        // LogNotAssigned above: a [Conditional] call is resolved against the symbols of the
        // assembly it appears in, so this package compiling without them would empty both --
        // silently, and even for a consumer whose own assembly did define them.
        internal static void LogNotAssignedCore(Object component, string name)
        {
            WallstopStudiosLogger.LogWarnCore(component, $"{name} not found.", null, true);
        }
    }
}
