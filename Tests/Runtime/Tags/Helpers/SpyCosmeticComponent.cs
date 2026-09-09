// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags.Helpers
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tags;

    internal sealed class SpyCosmeticComponent : CosmeticEffectComponent
    {
        public static int RemoveInvocationCount { get; private set; }

        public int AppliedCount => _appliedTargets.Count;

        public static void ResetForTests()
        {
            RemoveInvocationCount = 0;
        }

        public override void OnApplyEffect(GameObject target)
        {
            base.OnApplyEffect(target);
        }

        public override void OnRemoveEffect(GameObject target)
        {
            base.OnRemoveEffect(target);
            ++RemoveInvocationCount;
        }
    }
}
