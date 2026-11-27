// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR && !WALLSTOP_EDITOR_CHECK
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.Build;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>Sets a build target's scripting backend and restores it when disposed.</summary>
    /// <remarks>
    /// Nested scopes for the same target restore in order, and copies of one scope share a single
    /// restoration claim. A setter or getter failure is logged and does not throw from disposal.
    /// </remarks>
    public readonly struct ScriptingBackendScope : IDisposable, IEquatable<ScriptingBackendScope>
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<
            NamedBuildTarget,
            RestorableGlobal<ScriptingImplementation>
        > Owners = new();

        private readonly RestorableGlobal<ScriptingImplementation>.Scope _scope;

        /// <summary>Applies a scripting backend for the life of the scope.</summary>
        /// <param name="target">The build target whose backend is changed.</param>
        /// <param name="backend">The backend to apply.</param>
        public ScriptingBackendScope(NamedBuildTarget target, ScriptingImplementation backend)
        {
            _scope = Owner(target).Borrow(backend);
        }

        private static RestorableGlobal<ScriptingImplementation> Owner(NamedBuildTarget target)
        {
            lock (Gate)
            {
                if (Owners.TryGetValue(target, out RestorableGlobal<ScriptingImplementation> owner))
                {
                    return owner;
                }

                owner = new RestorableGlobal<ScriptingImplementation>(
                    () => PlayerSettings.GetScriptingBackend(target),
                    value => PlayerSettings.SetScriptingBackend(target, value)
                );
                Owners.Add(target, owner);
                return owner;
            }
        }

        /// <summary>Restores the backend that was current when the scope was taken.</summary>
        public void Dispose()
        {
            _scope.Dispose();
        }

        /// <summary>Compares the stored claim with another scope.</summary>
        public bool Equals(ScriptingBackendScope other)
        {
            return EqualityComparer<RestorableGlobal<ScriptingImplementation>.Scope>.Default.Equals(
                _scope,
                other._scope
            );
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is ScriptingBackendScope other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Objects.HashCode(_scope);
        }
    }
#endif
}
