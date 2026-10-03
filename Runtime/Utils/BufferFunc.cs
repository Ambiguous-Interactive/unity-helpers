// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;

    /// <summary>Processes a borrowed logical buffer synchronously and returns an independent value.</summary>
    /// <typeparam name="T">The buffer element type.</typeparam>
    /// <typeparam name="TState">The caller state type.</typeparam>
    /// <typeparam name="TResult">The result type, which cannot be a byref-like type.</typeparam>
    /// <param name="buffer">Storage borrowed only for the duration of this invocation.</param>
    /// <param name="state">Caller state passed by value; referenced objects may be modified.</param>
    /// <returns>An independent value, element object, or owned copy.</returns>
    /// <remarks>Do not retain references into borrowed storage through unsafe or low-level APIs.</remarks>
    public delegate TResult BufferFunc<T, TState, TResult>(Span<T> buffer, TState state);
}
