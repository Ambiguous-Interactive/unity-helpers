// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// A MonoBehaviour for testing that detection returns false.
    /// </summary>
    /// <remarks>
    /// It lives in a runtime-capable assembly because Unity refuses to
    /// <c>AddComponent</c> a MonoBehaviour it can identify as an editor script, and both fixtures
    /// that use it add it to a <c>GameObject</c>. Measured in a real editor while it was still an
    /// editor script: the add returned <c>null</c>, and
    /// <c>IsScriptableSingletonType</c> answered the same for that null as it does for a literal
    /// null -- so the two tests were duplicates of the null case rather than MonoBehaviour cases.
    /// </remarks>
    public sealed class RegularMonoBehaviour : MonoBehaviour
    {
        public SerializableDictionary<string, string> dictionary = new();
        public SerializableHashSet<int> set = new();
    }
}
