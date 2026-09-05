// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor;

    internal sealed class ValidationSaveProcessor : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (!ValidationAutoRun.Enabled || paths == null)
                return paths;
            List<string> guids = new List<string>();
            foreach (string path in paths)
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid))
                    guids.Add(guid);
            }
            ValidationAutoRun.Queue(guids, false, 1);
            return paths;
        }
    }
#endif
}
