// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class ToggleDropDownAsset : ScriptableObject
    {
        [WEnumToggleButtons]
        [WValueDropDown(typeof(DropDownProvider), nameof(DropDownProvider.GetModes))]
        public string mode;
    }

    internal static class DropDownProvider
    {
        internal static string[] GetModes()
        {
            return new[] { "Alpha", "Beta", "Gamma" };
        }
    }
}
