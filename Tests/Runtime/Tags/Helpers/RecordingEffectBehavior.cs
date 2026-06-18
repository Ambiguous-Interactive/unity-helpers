// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags.Helpers
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tags;

    public sealed class RecordingEffectBehavior : EffectBehavior
    {
        private static readonly HashSet<int> InstanceIds = new();

        public static List<EffectBehaviorContext> ApplyContexts { get; } = new();

        public static List<EffectBehaviorContext> TickContexts { get; } = new();

        public static List<PeriodicInvocation> PeriodicInvocations { get; } = new();

        public static List<EffectBehaviorContext> RemoveContexts { get; } = new();

        public static int ApplyCount { get; private set; }

        public static int TickCount { get; private set; }

        public static int PeriodicTickCount { get; private set; }

        public static int RemoveCount { get; private set; }

        public static int InstanceCount => InstanceIds.Count;

        public static void ResetForTests()
        {
            ApplyCount = 0;
            TickCount = 0;
            PeriodicTickCount = 0;
            RemoveCount = 0;
            InstanceIds.Clear();
            ApplyContexts.Clear();
            TickContexts.Clear();
            PeriodicInvocations.Clear();
            RemoveContexts.Clear();
        }

        private void OnEnable()
        {
            _ = InstanceIds.Add(this.GetUnityObjectId());
        }

        public override void OnApply(EffectBehaviorContext context)
        {
            ++ApplyCount;
            ApplyContexts.Add(context);
        }

        public override void OnTick(EffectBehaviorContext context)
        {
            ++TickCount;
            TickContexts.Add(context);
        }

        public override void OnPeriodicTick(
            EffectBehaviorContext context,
            PeriodicEffectTickContext tickContext
        )
        {
            ++PeriodicTickCount;
            PeriodicInvocations.Add(new PeriodicInvocation(context, tickContext));
        }

        public override void OnRemove(EffectBehaviorContext context)
        {
            ++RemoveCount;
            RemoveContexts.Add(context);
        }

        public readonly struct PeriodicInvocation
        {
            public PeriodicInvocation(
                EffectBehaviorContext context,
                PeriodicEffectTickContext tickContext
            )
            {
                Context = context;
                TickContext = tickContext;
            }

            public EffectBehaviorContext Context { get; }

            public PeriodicEffectTickContext TickContext { get; }
        }
    }
}
