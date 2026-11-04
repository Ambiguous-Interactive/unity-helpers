// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    [Serializable]
    internal struct CompositeRequirementTestValue
    {
        public List<string> values;
        public int count;
        public List<Vector3> vectors;
    }
}
