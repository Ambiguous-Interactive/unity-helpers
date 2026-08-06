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
        [System.Diagnostics.Conditional(WallstopStudiosLogger.EnableUberLoggingSymbol)]
        [System.Diagnostics.Conditional(WallstopStudiosLogger.DevelopmentBuildSymbol)]
        [System.Diagnostics.Conditional(WallstopStudiosLogger.DebugSymbol)]
        [System.Diagnostics.Conditional(WallstopStudiosLogger.UnityEditorSymbol)]
        [System.Diagnostics.Conditional(WallstopStudiosLogger.WarnLoggingSymbol)]
        public static void LogNotAssigned(this Object component, string name)
        {
            component.LogWarn($"{name} not found.");
        }
    }
}
