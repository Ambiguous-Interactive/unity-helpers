// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    internal static class LiveComponentRegistry
    {
        private static readonly List<Action> Resets = new();

        internal static void RegisterReset(Action reset)
        {
            lock (Resets)
            {
                Resets.Add(reset);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void OnSubsystemRegistration()
        {
            lock (Resets)
            {
                foreach (Action reset in Resets)
                {
                    reset();
                }
            }
        }
    }
}
