// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags.Helpers
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tags;

    /// <summary>
    /// Cosmetic component whose apply and remove callbacks run a test-supplied hook, so a fixture
    /// can re-enter the handler, or throw, from the cosmetic phase of the effect lifecycle.
    /// </summary>
    public sealed class ReentrantCosmeticComponent : CosmeticEffectComponent
    {
        public static Action<GameObject> ApplyHook { get; set; }

        public static Action<GameObject> RemoveHook { get; set; }

        public static int AppliedCount { get; private set; }

        public static int RemovedCount { get; private set; }

        public bool requireInstance;

        public override bool RequiresInstance => requireInstance;

        public static void ResetForTests()
        {
            ApplyHook = null;
            RemoveHook = null;
            AppliedCount = 0;
            RemovedCount = 0;
        }

        public override void OnApplyEffect(GameObject target)
        {
            base.OnApplyEffect(target);
            ++AppliedCount;
            EffectLifecycleLog.Record(EffectLifecycleLog.CosmeticApplied);
            ApplyHook?.Invoke(target);
        }

        public override void OnRemoveEffect(GameObject target)
        {
            base.OnRemoveEffect(target);
            ++RemovedCount;
            EffectLifecycleLog.Record(EffectLifecycleLog.CosmeticRemoved);
            RemoveHook?.Invoke(target);
        }
    }
}
