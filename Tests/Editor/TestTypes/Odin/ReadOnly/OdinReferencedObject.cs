// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ReadOnly
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;

    /// <summary>
    /// A referenced object used by OdinReadOnlyObjectReferenceTarget for testing.
    /// </summary>
    internal sealed class OdinReferencedObject : SerializedScriptableObject
    {
        public int value;
    }
#endif
}
