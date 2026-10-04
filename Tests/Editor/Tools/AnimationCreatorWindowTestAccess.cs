// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using CustomEditors;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Animation;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Editor.Extensions;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.Sprites.AnimationCreatorWindow;
    using Object = UnityEngine.Object;

    /// <summary>Provides test-side setup and inspection for AnimationCreatorWindow.</summary>
    internal static class AnimationCreatorWindowTestAccess
    {
        internal static AnimationClip CreateAnimationClip(
            AnimationData data,
            List<Sprite> validFrames
        )
        {
            return AnimationCreatorAPI.TryCreateClip(
                data,
                validFrames,
                out AnimationClip clip,
                out _
            )
                ? clip
                : null;
        }
    }
#endif
}
