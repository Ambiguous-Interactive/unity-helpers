// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class TestComponent : ScriptableObject
    {
        public int invocationCount;

        [WButton("Test Button")]
        private void TestMethod()
        {
            invocationCount++;
        }
    }
}
#endif
