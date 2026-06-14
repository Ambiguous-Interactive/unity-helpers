// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Captures Unity log messages emitted while the scope is active. Used by tests to
    /// make positive assertions about specific warning/error patterns (complements
    /// <c>LogAssert.NoUnexpectedReceived</c>, does not replace it).
    /// </summary>
    public sealed class EditorLogScope : IDisposable
    {
        private static readonly Regex SendMessageWarningPattern = new(
            "^SendMessage cannot be called during Awake, CheckConsistency, or OnValidate",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        private readonly object _syncRoot = new();
        private readonly List<LogRecord> _warnings = new();
        private readonly List<LogRecord> _errors = new();

        private bool _disposed;

        public EditorLogScope()
        {
            Application.logMessageReceivedThreaded += HandleLogThreaded;
        }

        /// <summary>
        /// Immutable snapshot of a captured log message.
        /// </summary>
        public readonly struct LogRecord
        {
            public LogRecord(LogType type, string condition, string stackTrace)
            {
                Type = type;
                Condition = condition ?? string.Empty;
                StackTrace = stackTrace ?? string.Empty;
            }

            public LogType Type { get; }
            public string Condition { get; }
            public string StackTrace { get; }
        }

        /// <summary>
        /// All warning messages captured since the scope was created.
        /// </summary>
        public IReadOnlyList<LogRecord> Warnings
        {
            get
            {
                lock (_syncRoot)
                {
                    return _warnings.ToArray();
                }
            }
        }

        /// <summary>
        /// All error / exception / assert messages captured since the scope was created.
        /// </summary>
        public IReadOnlyList<LogRecord> Errors
        {
            get
            {
                lock (_syncRoot)
                {
                    return _errors.ToArray();
                }
            }
        }

        /// <summary>
        /// Fails the current test if any captured warning matches <paramref name="pattern"/>.
        /// </summary>
        public void AssertNoWarningsMatching(Regex pattern)
        {
            if (pattern == null)
            {
                throw new ArgumentNullException(nameof(pattern));
            }

            LogRecord[] snapshot;
            lock (_syncRoot)
            {
                snapshot = _warnings.ToArray();
            }

            List<LogRecord> matches = null;
            for (int i = 0; i < snapshot.Length; i++)
            {
                LogRecord record = snapshot[i];
                if (pattern.IsMatch(record.Condition))
                {
                    matches ??= new List<LogRecord>();
                    matches.Add(record);
                }
            }

            if (matches == null)
            {
                return;
            }

            Assert.Fail(FormatFailure("warnings matching pattern", pattern.ToString(), matches));
        }

        /// <summary>
        /// Shortcut for the specific warning described in
        /// <see href="https://github.com/wallstop/unity-helpers/pull/234">#234</see>.
        /// </summary>
        public void AssertNoSendMessageWarnings()
        {
            AssertNoWarningsMatching(SendMessageWarningPattern);
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            Application.logMessageReceivedThreaded -= HandleLogThreaded;
        }

        private void HandleLogThreaded(string condition, string stackTrace, LogType type)
        {
            LogRecord record = new(type, condition, stackTrace);
            lock (_syncRoot)
            {
                // Guard against in-flight threaded log deliveries that race Dispose().
                // Without this check, a background thread can still append to the lists
                // after the event handler has been unsubscribed but before it drained.
                if (_disposed)
                {
                    return;
                }

                switch (type)
                {
                    case LogType.Warning:
                        _warnings.Add(record);
                        break;
                    case LogType.Error:
                    case LogType.Exception:
                    case LogType.Assert:
                        _errors.Add(record);
                        break;
                }
            }
        }

        private static string FormatFailure(
            string header,
            string patternDescription,
            IReadOnlyList<LogRecord> matches
        )
        {
            StringBuilder builder = new();
            builder.Append("Expected no ");
            builder.Append(header);
            builder.Append(" '");
            builder.Append(patternDescription);
            builder.Append("', but found ");
            builder.Append(matches.Count);
            builder.AppendLine(":");
            for (int i = 0; i < matches.Count; i++)
            {
                LogRecord match = matches[i];
                builder.Append("  [");
                builder.Append(i);
                builder.Append("] ");
                builder.Append(match.Type);
                builder.Append(": ");
                builder.AppendLine(match.Condition);
            }

            return builder.ToString();
        }
    }
}
