// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes
{
    using System;
    using UnityEngine;

    [Serializable]
    internal sealed class ClosedGenericRequirementTestValue
        : GenericRequirementTestValue<Material> { }
}
