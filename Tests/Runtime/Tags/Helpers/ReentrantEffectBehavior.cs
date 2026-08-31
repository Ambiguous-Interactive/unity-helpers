// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags.Helpers
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Tags;

    /// <summary>
    /// Effect behaviour whose every callback runs a test-supplied hook, so a fixture can remove its
    /// own handle, remove another, re-apply, rent a pooled buffer or throw from any phase.
    /// </summary>
    /// <remarks>
    /// The hooks are static because Instantiate copies only serialized fields, so a delegate
    /// assigned to the asset would never reach the clone the handler actually invokes.
    /// </remarks>
    public sealed class ReentrantEffectBehavior : EffectBehavior
    {
        public static Action<EffectBehaviorContext> ApplyHook { get; set; }

        public static Action<EffectBehaviorContext> TickHook { get; set; }

        public static Action<
            EffectBehaviorContext,
            PeriodicEffectTickContext
        > PeriodicTickHook { get; set; }

        public static Action<EffectBehaviorContext> RemoveHook { get; set; }

        public static List<EffectBehavior> Clones { get; } = new();

        public static int ApplyCount { get; private set; }

        public static int TickCount { get; private set; }

        public static int PeriodicTickCount { get; private set; }

        public static int RemoveCount { get; private set; }

        public static void ResetForTests()
        {
            ApplyHook = null;
            TickHook = null;
            PeriodicTickHook = null;
            RemoveHook = null;
            Clones.Clear();
            ApplyCount = 0;
            TickCount = 0;
            PeriodicTickCount = 0;
            RemoveCount = 0;
        }

        public override void OnApply(EffectBehaviorContext context)
        {
            ++ApplyCount;
            Clones.Add(this);
            EffectLifecycleLog.Record(EffectLifecycleLog.BehaviorApplied);
            ApplyHook?.Invoke(context);
        }

        public override void OnTick(EffectBehaviorContext context)
        {
            ++TickCount;
            EffectLifecycleLog.Record(EffectLifecycleLog.BehaviorTicked);
            TickHook?.Invoke(context);
        }

        public override void OnPeriodicTick(
            EffectBehaviorContext context,
            PeriodicEffectTickContext tickContext
        )
        {
            ++PeriodicTickCount;
            EffectLifecycleLog.Record(EffectLifecycleLog.BehaviorPeriodicTicked);
            PeriodicTickHook?.Invoke(context, tickContext);
        }

        public override void OnRemove(EffectBehaviorContext context)
        {
            ++RemoveCount;
            EffectLifecycleLog.Record(EffectLifecycleLog.BehaviorRemoved);
            RemoveHook?.Invoke(context);
        }
    }
}
