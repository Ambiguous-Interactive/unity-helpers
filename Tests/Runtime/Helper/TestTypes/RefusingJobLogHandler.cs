// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using UnityEngine;

    internal sealed class RefusingJobLogHandler : ILogHandler
    {
        internal int Attempts { get; private set; }

        public void LogFormat(
            LogType logType,
            UnityEngine.Object context,
            string format,
            params object[] args
        )
        {
            ++Attempts;
            throw new InvalidOperationException("Controlled log handler failure");
        }

        public void LogException(Exception exception, UnityEngine.Object context)
        {
            ++Attempts;
            throw new InvalidOperationException("Controlled log handler failure");
        }
    }
}
