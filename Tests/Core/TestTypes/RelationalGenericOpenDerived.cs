// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;

    public class RelationalGenericOpenDerived<T> : RelationalGenericBase<T>
        where T : Component { }
}
