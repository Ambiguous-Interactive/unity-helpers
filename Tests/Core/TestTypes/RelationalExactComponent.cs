// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;

    public class RelationalExactComponent : MonoBehaviour, ITestInterface
    {
        public string GetTestValue() => nameof(RelationalExactComponent);
    }
}
