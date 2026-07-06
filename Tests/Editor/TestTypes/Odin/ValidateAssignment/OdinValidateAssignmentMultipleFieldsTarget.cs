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
    /// Test target for ValidateAssignment with multiple fields having different settings.
    /// </summary>
    internal sealed class OdinValidateAssignmentMultipleFieldsTarget : SerializedScriptableObject
    {
        [ValidateAssignment]
        public GameObject validateObject;

        [ValidateAssignment(ValidateAssignmentMessageType.Warning)]
        public string validateString;

        [ValidateAssignment(ValidateAssignmentMessageType.Error, "Required list")]
        public List<int> validateList;
    }
#endif
}
