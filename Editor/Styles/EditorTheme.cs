// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Styles
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>Shares the editor design system's palette and controls across toolkit windows.</summary>
    public static class EditorTheme
    {
        private static StyleSheet _sheet;

        /// <summary>Applies the shared theme using the active editor skin.</summary>
        public static void Apply(VisualElement root)
        {
            Apply(root, EditorGUIUtility.isProSkin);
        }

        internal static void Apply(VisualElement root, bool dark)
        {
            if (root == null)
            {
                return;
            }

            if (_sheet == null)
            {
                _sheet = Load("EditorTheme.uss");
            }

            if (_sheet != null && !root.styleSheets.Contains(_sheet))
            {
                root.styleSheets.Add(_sheet);
            }

            root.AddToClassList("dx-editor");
            root.EnableInClassList("dx-dark", dark);
            root.EnableInClassList("dx-light", !dark);
        }

        internal static StyleSheet Load(string fileName)
        {
            string path = DirectoryHelper.ResolvePackageAssetPath("Editor/Styles/" + fileName);
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
        }
    }
#endif
}
