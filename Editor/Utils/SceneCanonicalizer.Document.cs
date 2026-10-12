// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    public static partial class SceneCanonicalizer
    {
        private sealed class SceneDocument
        {
            internal int _classId;
            internal long _fileId;
            internal bool _stripped;
            internal string _text;
            internal string[] _lines;
            internal long _gameObjectId;
            internal string _key;
            internal string _normalized;
            internal long _newFileId;
        }
    }
}
