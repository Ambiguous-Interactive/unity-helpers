// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*

 * The old editor references lack SaveAssetIfDirty, excluding the real editor type. This name-only shim keeps

 * its XML references checkable.

 */
namespace WallstopStudios.UnityHelpers.Editor
{
    using UnityEditor;

    public sealed class AnimationEventEditor : EditorWindow { }
}
