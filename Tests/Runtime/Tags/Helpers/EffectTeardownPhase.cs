// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags.Helpers
{
    using System;

    /// <summary>
    /// The teardown callbacks an effect handle delivers, in the order the handler delivers them.
    /// Used to drive one fixture over every phase that can run user code during removal.
    /// </summary>
    public enum EffectTeardownPhase
    {
        [Obsolete("Use a specific EffectTeardownPhase value instead of None.")]
        None = 0,
        AttributeModification = 1,
        Tag = 2,
        Cosmetic = 3,
        EffectRemovedEvent = 4,
        BehaviorRemove = 5,
    }
}
