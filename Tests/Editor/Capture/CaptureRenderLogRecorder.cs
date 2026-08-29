// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Capture
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Records the errors Unity logs while a frame renders, so a caller can tell the one benign
    /// message this technique provokes from anything the surface itself went wrong doing.
    ///
    /// A drawer that asks for a cursor rect -- an inspector field for a numeric button parameter
    /// does -- reaches <c>EditorGUIUtility.AddCursorRect</c>, which needs the editor's current
    /// view. The capture drives the panel directly, so there is no view and Unity logs
    /// <see cref="CursorRectWithoutView"/>. The pixels are correct; only the mouse cursor, which
    /// an offscreen capture has no use for, is not applied.
    ///
    /// This records rather than suppresses, and it does so through
    /// <see cref="Application.logMessageReceived"/> because that message does NOT travel through
    /// <c>Debug.unityLogger.logHandler</c> -- measured on 6000.4.6f1, where swapping the handler
    /// saw none of the three the level-generator capture produces. A test that tolerates the
    /// message can then assert that it tolerated only that message.
    /// </summary>
    internal sealed class CaptureRenderLogRecorder : IDisposable
    {
        /// <summary>
        /// Unity's own wording. It is not a package identifier, so there is nothing to point
        /// <c>nameof</c> at; matching a substring keeps it working if Unity re-words the rest.
        /// </summary>
        internal const string CursorRectWithoutView = "AddCursorRect called outside";

        private readonly List<string> _errors = new();
        private bool _disposed;

        internal CaptureRenderLogRecorder()
        {
            Application.logMessageReceived += Record;
        }

        internal List<string> Errors => _errors;

        /// <summary>
        /// True when every error recorded is the cursor-rect message, including when none was
        /// recorded at all.
        /// </summary>
        internal bool OnlyRecordedCursorRectErrors
        {
            get
            {
                for (int index = 0; index < _errors.Count; index++)
                {
                    if (_errors[index].IndexOf(CursorRectWithoutView, StringComparison.Ordinal) < 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        internal string Summary
        {
            get
            {
                if (_errors.Count == 0)
                {
                    return string.Empty;
                }

                return string.Join(" | ", _errors.ToArray());
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Application.logMessageReceived -= Record;
        }

        private void Record(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors.Add(condition);
            }
        }
    }
#endif
}
