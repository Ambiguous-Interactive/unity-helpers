// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags.Helpers
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tags;

    internal sealed class RecordingCosmeticComponent : CosmeticEffectComponent
    {
        public static int AppliedCount { get; private set; }
        public static int RemovedCount { get; private set; }

        public override bool RequiresInstance => requireInstance;
        public override bool CleansUpSelf => cleansSelf;

        public bool requireInstance;
        public bool cleansSelf;

        public static void ResetCounters()
        {
            AppliedCount = 0;
            RemovedCount = 0;
        }

        public override void OnApplyEffect(GameObject target)
        {
            base.OnApplyEffect(target);
            ++AppliedCount;
        }

        public override void OnRemoveEffect(GameObject target)
        {
            base.OnRemoveEffect(target);
            ++RemovedCount;
            if (cleansSelf && gameObject != null)
            {
                Destroy(gameObject);
            }
        }
    }
}
