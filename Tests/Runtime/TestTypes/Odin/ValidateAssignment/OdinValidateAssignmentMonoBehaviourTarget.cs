// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.ValidateAssignment
{
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for ValidateAssignment on SerializedMonoBehaviour with Transform field.
    /// </summary>
    public sealed class OdinValidateAssignmentMonoBehaviourTarget : SerializedMonoBehaviour
    {
        [ValidateAssignment]
        public Transform validateReference;
    }
#endif
}
