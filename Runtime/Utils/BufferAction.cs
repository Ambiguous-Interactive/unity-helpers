// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;

    /// <summary>Processes a borrowed logical buffer synchronously with explicit caller state.</summary>
    /// <typeparam name="T">The buffer element type.</typeparam>
    /// <typeparam name="TState">The caller state type.</typeparam>
    /// <param name="buffer">Storage borrowed only for the duration of this invocation.</param>
    /// <param name="state">Caller state passed by value; referenced objects may be modified.</param>
    /// <remarks>
    /// Do not retain references into the borrowed storage through unsafe or low-level APIs.
    /// Objects stored in elements and independently owned copies may outlive the invocation.
    /// </remarks>
    public delegate void BufferAction<T, TState>(Span<T> buffer, TState state);
}
