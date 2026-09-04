// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags.Helpers
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Cosmetic component that repositions itself on every lifecycle callback, so a destroyed
    /// instance raises <c>MissingReferenceException</c> the way a real one does, and that can
    /// destroy its first sibling from inside a callback, leaving a dead entry in the component
    /// buffer the handler is walking.
    /// </summary>
    internal sealed class SiblingDestroyingCosmeticComponent : CosmeticEffectComponent
    {
        public static int ApplyCount { get; private set; }

        public static int RemoveCount { get; private set; }

        public bool requireInstance;
        public bool destroysSibling;
        public bool destroysDuringRemoval;

        public override bool RequiresInstance => requireInstance;

        public static void ResetForTests()
        {
            ApplyCount = 0;
            RemoveCount = 0;
        }

        public override void OnApplyEffect(GameObject target)
        {
            base.OnApplyEffect(target);
            transform.position = target.transform.position;
            ++ApplyCount;
            if (!destroysDuringRemoval)
            {
                DestroyFirstSibling();
            }
        }

        public override void OnRemoveEffect(GameObject target)
        {
            base.OnRemoveEffect(target);
            transform.position = Vector3.zero;
            ++RemoveCount;
            if (destroysDuringRemoval)
            {
                DestroyFirstSibling();
            }
        }

        private void DestroyFirstSibling()
        {
            if (!destroysSibling)
            {
                return;
            }

            destroysSibling = false;
            using PooledResource<List<CosmeticEffectComponent>> lease =
                Buffers<CosmeticEffectComponent>.List.Get(
                    out List<CosmeticEffectComponent> siblings
                );
            GetComponents(siblings);
            foreach (CosmeticEffectComponent sibling in siblings)
            {
                if (sibling == null || sibling == this)
                {
                    continue;
                }

                Object.DestroyImmediate(sibling); // UNH-SUPPRESS UNH001: destroying it is the test
                return;
            }
        }
    }
}
