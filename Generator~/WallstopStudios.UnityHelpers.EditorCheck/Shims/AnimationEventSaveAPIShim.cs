// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
    using UnityEngine;

    public static class AnimationEventSaveAPI
    {
        public static bool TrySave(
            AnimationClip clip,
            AnimationEvent[] events,
            float? frameRate,
            out string error
        )
        {
            error = null;
            return false;
        }
    }
}
