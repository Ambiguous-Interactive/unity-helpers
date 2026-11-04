// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal static class LiveComponentRegistry
    {
        internal static bool CanAccess =>
            UnityMainThreadGuard.IsInitialized && UnityMainThreadGuard.IsMainThread;

        private static readonly Dictionary<Type, List<LiveComponentRegistrationEntry>> Entries =
            new();
        private static readonly List<LiveComponentRegistration> Pending = new();

        internal static bool RegisterManual(Type type, Component component)
        {
            if (!CanAccess || component == null)
            {
                return false;
            }
            List<LiveComponentRegistrationEntry> entries = GetOrCreate(type);
            Prune(entries);
            for (int index = 0; index < entries.Count; ++index)
            {
                LiveComponentRegistrationEntry entry = entries[index];
                if (!ReferenceEquals(entry.component, component))
                {
                    continue;
                }
                if (entry.manual)
                {
                    return false;
                }
                entry.manual = true;
                entries[index] = entry;
                return true;
            }
            entries.Add(
                new LiveComponentRegistrationEntry { component = component, manual = true }
            );
            return true;
        }

        internal static bool UnregisterManual(Type type, Component component)
        {
            if (
                !CanAccess
                || ReferenceEquals(component, null)
                || !Entries.TryGetValue(type, out List<LiveComponentRegistrationEntry> entries)
            )
            {
                return false;
            }
            for (int index = 0; index < entries.Count; ++index)
            {
                LiveComponentRegistrationEntry entry = entries[index];
                if (!ReferenceEquals(entry.component, component) || !entry.manual)
                {
                    continue;
                }
                if (entry.owner == null)
                {
                    entries.RemoveAt(index);
                }
                else
                {
                    entry.manual = false;
                    entries[index] = entry;
                }
                return true;
            }
            return false;
        }

        internal static void RegisterAdapter(Component component, LiveComponentRegistration owner)
        {
            if (!CanAccess || component == null || owner == null)
            {
                return;
            }
            List<LiveComponentRegistrationEntry> entries = GetOrCreate(component.GetType());
            Prune(entries);
            for (int index = 0; index < entries.Count; ++index)
            {
                LiveComponentRegistrationEntry entry = entries[index];
                if (ReferenceEquals(entry.component, component))
                {
                    entry.owner = owner;
                    entries[index] = entry;
                    return;
                }
            }
            entries.Add(
                new LiveComponentRegistrationEntry { component = component, owner = owner }
            );
        }

        internal static void UnregisterAdapter(Component component, LiveComponentRegistration owner)
        {
            if (
                !CanAccess
                || ReferenceEquals(component, null)
                || !Entries.TryGetValue(
                    component.GetType(),
                    out List<LiveComponentRegistrationEntry> entries
                )
            )
            {
                return;
            }
            for (int index = 0; index < entries.Count; ++index)
            {
                LiveComponentRegistrationEntry entry = entries[index];
                if (
                    !ReferenceEquals(entry.component, component)
                    || !ReferenceEquals(entry.owner, owner)
                )
                {
                    continue;
                }
                if (entry.manual)
                {
                    entry.owner = null;
                    entries[index] = entry;
                }
                else
                {
                    entries.RemoveAt(index);
                }
                return;
            }
        }

        internal static bool TryGetEntries(
            Type type,
            out List<LiveComponentRegistrationEntry> entries
        )
        {
            if (!CanAccess)
            {
                entries = null;
                return false;
            }
            FlushPending();
            if (Entries.TryGetValue(type, out List<LiveComponentRegistrationEntry> found))
            {
                Prune(found);
                entries = found;
                return true;
            }
            entries = null;
            return false;
        }

        internal static bool IsActive(Component component)
        {
#if UNITY_EDITOR
            if (Application.isPlaying && !Application.IsPlaying(component))
            {
                return false;
            }
#endif
            return component is Behaviour behaviour
                ? behaviour.isActiveAndEnabled
                : component.gameObject.activeInHierarchy;
        }

        internal static void Schedule(LiveComponentRegistration owner)
        {
            Pending.Add(owner);
        }

        internal static void Cancel(LiveComponentRegistration owner)
        {
            for (int index = Pending.Count - 1; index >= 0; --index)
            {
                if (ReferenceEquals(Pending[index], owner))
                {
                    Pending.RemoveAt(index);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void OnSubsystemRegistration()
        {
            foreach (LiveComponentRegistration owner in Pending)
            {
                if (!ReferenceEquals(owner, null))
                {
                    owner.CancelPendingRefresh();
                }
            }
            Pending.Clear();
            Pending.TrimExcess();
            foreach (List<LiveComponentRegistrationEntry> entries in Entries.Values)
            {
                entries.Clear();
                entries.TrimExcess();
            }
            Entries.Clear();
        }

        private static List<LiveComponentRegistrationEntry> GetOrCreate(Type type)
        {
            if (!Entries.TryGetValue(type, out List<LiveComponentRegistrationEntry> entries))
            {
                entries = new List<LiveComponentRegistrationEntry>();
                Entries.Add(type, entries);
            }
            return entries;
        }

        private static void FlushPending()
        {
            foreach (LiveComponentRegistration owner in Pending)
            {
                if (owner != null)
                {
                    owner.ApplyPendingRegistration();
                }
            }
            Pending.Clear();
        }

        private static void Prune(List<LiveComponentRegistrationEntry> entries)
        {
            int write = 0;
            int count = entries.Count;
            for (int read = 0; read < count; ++read)
            {
                LiveComponentRegistrationEntry entry = entries[read];
                if (
                    entry.component == null
                    || (!entry.manual && (entry.owner == null || !entry.owner.isActiveAndEnabled))
                )
                {
                    continue;
                }
                entries[write++] = entry;
            }
            if (write < count)
            {
                entries.RemoveRange(write, count - write);
            }
        }
    }
}
