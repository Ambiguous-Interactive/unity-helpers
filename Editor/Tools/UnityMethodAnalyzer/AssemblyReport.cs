// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    [Serializable]
    internal sealed class AssemblyReport
    {
        public string name;
        public bool hasErrors;
        public List<MessageData> messages = new();
    }
#endif
}
