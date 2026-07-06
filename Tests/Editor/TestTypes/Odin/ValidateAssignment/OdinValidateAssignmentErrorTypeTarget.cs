// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ValidateAssignment
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for ValidateAssignment with Error message type.
    /// </summary>
    internal sealed class OdinValidateAssignmentErrorTypeTarget : SerializedScriptableObject
    {
        [ValidateAssignment(ValidateAssignmentMessageType.Error)]
        public string validateError;
    }
#endif
}
