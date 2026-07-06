// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ValueDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WValueDropDown with Unity Object references from static provider.
    /// </summary>
    internal sealed class OdinValueDropDownObjectRefTarget : SerializedScriptableObject
    {
        [WValueDropDown(
            typeof(TestObjectRefProvider),
            nameof(TestObjectRefProvider.GetObjectOptions)
        )]
        public UnityEngine.Object selectedObject;

        public static class TestObjectRefProvider
        {
            public static IEnumerable<UnityEngine.Object> GetObjectOptions()
            {
                return Array.Empty<UnityEngine.Object>();
            }
        }
    }
#endif
}
