// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;

    internal sealed class TestInterfaceComponent2 : MonoBehaviour, ITestInterface
    {
        public string GetTestValue()
        {
            return "Test";
        }
    }
}
