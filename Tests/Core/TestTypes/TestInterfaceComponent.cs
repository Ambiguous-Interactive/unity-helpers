// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;

    public sealed class TestInterfaceComponent : MonoBehaviour, ITestInterface
    {
        public string GetTestValue()
        {
            return "Test";
        }
    }
}
