// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.TestComponents
{
#if UNITY_EDITOR
    using UnityEngine;

    // Editor tests cannot depend on runtime test assemblies, so this fixture needs an editor-side type.
    public sealed class PrewarmTesterComponent : MonoBehaviour { }
#endif
}
