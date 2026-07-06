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
    /// Test target for ValidateAssignment with fields containing valid values.
    /// </summary>
    internal sealed class OdinValidateAssignmentValidValuesTarget : SerializedScriptableObject
    {
        [ValidateAssignment]
        public GameObject validObject;

        [ValidateAssignment]
        public string validString;

        [ValidateAssignment]
        public List<int> validList;

        [ValidateAssignment]
        public float[] validArray;
    }
#endif
}
