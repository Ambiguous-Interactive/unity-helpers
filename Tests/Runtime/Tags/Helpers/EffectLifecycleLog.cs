// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags.Helpers
{
    using System.Collections.Generic;

    /// <summary>
    /// Single ordered record of the effect-lifecycle callbacks a test observed, shared by every
    /// reentrancy fixture so callback ORDER can be asserted across attributes, tags, cosmetics,
    /// the handler event and behaviours.
    /// </summary>
    public static class EffectLifecycleLog
    {
        public const string AttributeModified = nameof(AttributeModified);
        public const string TagRemoved = nameof(TagRemoved);
        public const string CosmeticApplied = nameof(CosmeticApplied);
        public const string CosmeticRemoved = nameof(CosmeticRemoved);
        public const string EffectApplied = nameof(EffectApplied);
        public const string EffectRemoved = nameof(EffectRemoved);
        public const string BehaviorApplied = nameof(BehaviorApplied);
        public const string BehaviorTicked = nameof(BehaviorTicked);
        public const string BehaviorPeriodicTicked = nameof(BehaviorPeriodicTicked);
        public const string BehaviorRemoved = nameof(BehaviorRemoved);

        public static List<string> Entries { get; } = new();

        public static void Record(string entry)
        {
            Entries.Add(entry);
        }

        public static int CountOf(string entry)
        {
            int count = 0;
            foreach (string recorded in Entries)
            {
                if (string.Equals(recorded, entry))
                {
                    ++count;
                }
            }

            return count;
        }

        public static void ResetForTests()
        {
            Entries.Clear();
        }
    }
}
