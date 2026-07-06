// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ValidateAssignment
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for ValidateAssignment with custom message.
    /// </summary>
    internal sealed class OdinValidateAssignmentCustomMessageTarget : SerializedScriptableObject
    {
        [ValidateAssignment("Enemy prefab must be assigned for spawning to work")]
        public GameObject validateWithCustomMessage;
    }
#endif
}
