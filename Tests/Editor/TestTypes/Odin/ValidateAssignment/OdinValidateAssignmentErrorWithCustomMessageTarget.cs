// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ValidateAssignment
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for ValidateAssignment with Error message type and custom message.
    /// </summary>
    internal sealed class OdinValidateAssignmentErrorWithCustomMessageTarget
        : SerializedScriptableObject
    {
        [ValidateAssignment(
            ValidateAssignmentMessageType.Error,
            "Critical: Spawn points list cannot be empty!"
        )]
        public List<Transform> validateErrorCustom;
    }
#endif
}
