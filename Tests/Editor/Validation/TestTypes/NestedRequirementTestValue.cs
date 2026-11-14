// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes
{
    using System;
    using UnityEngine;
    using UnityEngine.Serialization;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    [Serializable]
    internal sealed class NestedRequirementTestValue
    {
        [WNotNull]
        [FormerlySerializedAs("oldMaterial")]
        public Material material;

        public NestedRequirementTestValue child;
    }
}
