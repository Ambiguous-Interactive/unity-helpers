// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEngine.SceneManagement;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using WallstopStudios.UnityHelpers.Utils;

    internal static class TexturePlatformNameHelper
    {
        internal const string DefaultPlatformName = "DefaultTexturePlatform";

        private static string[] _cached;
        private static readonly Dictionary<BuildTargetGroup, string> Map = new()
        {
            { BuildTargetGroup.Standalone, "Standalone" },
            { BuildTargetGroup.iOS, "iPhone" },
            { BuildTargetGroup.tvOS, "tvOS" },
            { BuildTargetGroup.Android, "Android" },
            { BuildTargetGroup.WebGL, "WebGL" },
            { BuildTargetGroup.PS4, "PS4" },
            { BuildTargetGroup.PS5, "PS5" },
            { BuildTargetGroup.XboxOne, "XboxOne" },
            { BuildTargetGroup.Switch, "Switch" },
        };

        static TexturePlatformNameHelper()
        {
            /*
                Proactively invalidate cache on common editor lifecycle events where domain
                reload may be disabled. This keeps results correct while enabling caching.
            */
            AssemblyReloadEvents.beforeAssemblyReload += ClearCache;
            EditorApplication.playModeStateChanged += _ => ClearCache();
            EditorSceneManager.activeSceneChangedInEditMode += (_, _) => ClearCache();
            SceneManager.sceneLoaded += (_, _) => ClearCache();
            EditorApplication.projectChanged += ClearCache;
        }

        private static void ClearCache()
        {
            _cached = null;
        }

        public static string[] GetKnownPlatformNames()
        {
            if (_cached != null)
            {
                return _cached;
            }

            // Use a pooled set during build to avoid duplicate checks (O(1) membership) with minimal allocations.
            using PooledResource<HashSet<string>> setLease = Buffers<string>.HashSet.Get(
                out HashSet<string> set
            );
            _ = set.Add(DefaultPlatformName);

            // Enum.GetValues returns a typed array; cast once to avoid boxing per element.
            BuildTargetGroup[] groups = (BuildTargetGroup[])
                Enum.GetValues(typeof(BuildTargetGroup));
            for (int i = 0; i < groups.Length; i++)
            {
                BuildTargetGroup g = groups[i];
                if (g == BuildTargetGroup.Unknown)
                {
                    continue;
                }

                if (Map.TryGetValue(g, out string mapped))
                {
                    _ = set.Add(mapped);
                }
                else
                {
                    string n = g.ToString();
                    if (!string.IsNullOrEmpty(n))
                    {
                        _ = set.Add(n);
                    }
                }
            }

            string[] arr = new string[set.Count];
            set.CopyTo(arr);
            Array.Sort(arr, StringComparer.Ordinal);
            _cached = arr;
            return arr;
        }
    }
#endif
}
