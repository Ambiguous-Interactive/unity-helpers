// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace B
{
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;

#if UNITY_EDITOR
    // A.NameCollision shares this name and this path, which is the collision under
    // test, so neither type can be renamed and each needs a folder of its own.
    [ScriptableSingletonPath("CreatorTests/Collision")]
    internal sealed class NameCollision : ScriptableObjectSingleton<NameCollision> { }
#endif
}
