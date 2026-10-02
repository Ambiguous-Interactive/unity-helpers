// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System;

    [Serializable]
    internal sealed class MessageData
    {
        public string file;
        public int line;
        public int column;
        public string message;
        public bool error;
    }
#endif
}
