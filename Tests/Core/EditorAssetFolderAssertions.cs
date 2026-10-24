// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
#if UNITY_EDITOR
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;

    /// <summary>Verifies that fixture-owned folders and their importers were removed.</summary>
    public static class EditorAssetFolderAssertions
    {
        /// <summary>Requires a unique root and any suffixed siblings to be absent.</summary>
        public static void AssertUniqueRootRemoved(string folder)
        {
            Assert.That(AssetDatabase.IsValidFolder(folder), Is.False, folder);
            Assert.That(Directory.Exists(folder), Is.False, folder);
            Assert.That(File.Exists(folder + ".meta"), Is.False, folder + ".meta");
            Assert.That(
                Directory.GetFileSystemEntries(
                    Path.GetDirectoryName(folder),
                    Path.GetFileName(folder) + "*"
                ),
                Is.Empty,
                folder
            );
        }
    }
#endif
}
