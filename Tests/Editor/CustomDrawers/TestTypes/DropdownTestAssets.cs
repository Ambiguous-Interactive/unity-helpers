// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
#if UNITY_EDITOR
    using System;

    internal static class StringOptionsProvider
    {
        internal static string[] GetOptions()
        {
            return new[] { "Static1", "Static2", "Static3" };
        }
    }

    internal static class WValueDropDownSource
    {
        internal static float[] GetFloatValues()
        {
            return new[] { 1f, 2.5f, 5f };
        }

        internal static double[] GetDoubleValues()
        {
            return new[] { 2d, 4.5d, 5.25d };
        }
    }

    internal static class WValueDropDownEmptySource
    {
        internal static int[] GetEmptyOptions()
        {
            return Array.Empty<int>();
        }
    }

    internal static class IntDropDownSource
    {
        internal static int[] GetStaticOptions()
        {
            return new[] { 100, 200, 300 };
        }
    }

    internal static class IntDropDownEmptySource
    {
        internal static int[] GetEmptyOptions()
        {
            return Array.Empty<int>();
        }
    }

    internal static class IntDropDownLargeSource
    {
        internal static int[] GetLargeOptions()
        {
            // Returns more than the default page size (25) to trigger popup path
            int[] options = new int[50];
            for (int i = 0; i < 50; ++i)
            {
                options[i] = (i + 1) * 10;
            }
            return options;
        }

        internal static int[] GetVeryLargeOptions()
        {
            // Returns 100+ options to ensure popup path is triggered
            int[] options = new int[150];
            for (int i = 0; i < 150; ++i)
            {
                options[i] = (i + 1) * 5;
            }
            return options;
        }
    }

    /// <summary>
    /// Static source for object reference dropdown tests.
    /// </summary>
    internal static class WValueDropDownObjectReferenceSource
    {
        private static WValueDropDownTestScriptableObject[] s_staticObjects;
        private static UnityEngine.Material[] s_staticMaterials;

        internal static WValueDropDownTestScriptableObject[] GetStaticScriptableObjects()
        {
            return s_staticObjects ?? System.Array.Empty<WValueDropDownTestScriptableObject>();
        }

        internal static WValueDropDownTestScriptableObject[] GetEmptyScriptableObjects()
        {
            return System.Array.Empty<WValueDropDownTestScriptableObject>();
        }

        internal static UnityEngine.Material[] GetStaticMaterials()
        {
            return s_staticMaterials ?? System.Array.Empty<UnityEngine.Material>();
        }

        internal static void SetStaticScriptableObjects(
            WValueDropDownTestScriptableObject[] objects
        )
        {
            s_staticObjects = objects;
        }

        internal static void SetStaticMaterials(UnityEngine.Material[] materials)
        {
            s_staticMaterials = materials;
        }

        internal static void Clear()
        {
            s_staticObjects = null;
            s_staticMaterials = null;
        }
    }
#endif
}
