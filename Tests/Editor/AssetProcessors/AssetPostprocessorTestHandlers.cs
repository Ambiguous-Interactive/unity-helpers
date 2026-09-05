// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Centralized helper for clearing and asserting-clean all <c>[DetectAssetChanged]</c>
    /// handler test doubles across the test assemblies. Discovery is driven by
    /// <see cref="TypeCache.GetMethodsWithAttribute{T}"/> so any future test handler
    /// that ships with the attribute is automatically covered — no maintenance of a
    /// hand-rolled list.
    ///
    /// Why this exists: every test fixture that mutates assets can cause the shared
    /// <see cref="DetectAssetChangeProcessor"/> to invoke ALL registered subscribers,
    /// populating their static Recorded* lists. If a fixture forgets to clear a
    /// handler it doesn't personally use, the leftover state pollutes the next
    /// fixture. Centralizing this makes "clear everything" a one-liner so no fixture
    /// ever has to know the full list of handlers.
    /// </summary>
    public static class AssetPostprocessorTestHandlers
    {
        /// <summary>
        /// Assembly-name prefix filter. Only handler types declared in test assemblies
        /// are considered — production subscribers (if any ever exist) are left
        /// untouched by this helper.
        /// </summary>
        private const string TestAssemblyPrefix = "WallstopStudios.UnityHelpers.Tests";

        /// <summary>
        /// Discovery cache, initialized lazily on first use and retained for the
        /// lifetime of the app domain. Domain reloads (assembly rebuilds, play-mode
        /// enter with reload enabled) reset the cache implicitly because the static
        /// itself is re-initialized. Interactive authoring that adds a new
        /// <see cref="DetectAssetChangedAttribute"/>-annotated handler without a
        /// domain reload will not see the new handler until the next reload — this
        /// is acceptable because the test runner always domain-reloads before
        /// executing, so CI runs always see the full handler set.
        /// </summary>
        private static readonly Lazy<IReadOnlyList<HandlerEntry>> LazyEntries = new(
            DiscoverHandlerEntries
        );

        /// <summary>
        /// The handler types this helper knows how to clear and observe for pollution.
        /// Discovered lazily from <see cref="TypeCache"/>; cached afterwards.
        /// </summary>
        internal static IReadOnlyList<Type> DiscoveredHandlerTypes
        {
            get
            {
                IReadOnlyList<HandlerEntry> entries = LazyEntries.Value;
                List<Type> types = new(entries.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    types.Add(entries[i].HandlerType);
                }
                return types;
            }
        }

        /// <summary>
        /// Clears every discovered handler's recorded state and behavior flags.
        /// Order is load-bearing and matches the reset-flush-clear phases of
        /// <see cref="AssertCleanAndClearAll"/> (without the intermediate
        /// snapshot step — this helper does not surface pollution, it only
        /// clears): (1) reset <c>ShouldThrow = false</c> on handlers that
        /// expose it so a handler whose flag leaked in from a prior test
        /// cannot emit a spurious exception log during the drain in the next
        /// step; (2) flush pending <see cref="AssetPostprocessorDeferral"/>
        /// drains so a late-arriving drain cannot re-populate the statics we
        /// are about to clear; (3) invoke <c>Clear()</c> on every discovered
        /// handler, which also discards any invocations recorded during the
        /// drain itself.
        /// </summary>
        internal static void FlushAndClearAll()
        {
            IReadOnlyList<HandlerEntry> entries = LazyEntries.Value;

            ResetShouldThrowAll(entries);

            // Pending drains must finish before handler state is cleared or they can repopulate it afterward.
            AssetPostprocessorDeferral.FlushForTesting();

            ClearAllInternal(entries);
        }

        private static void ResetShouldThrowAll(IReadOnlyList<HandlerEntry> entries)
        {
            /*
                Reset throwing flags before draining, then clear recorded state after the drain has invoked
                handlers.
            */
            for (int i = 0; i < entries.Count; i++)
            {
                HandlerEntry entry = entries[i];
                try
                {
                    entry.ResetShouldThrowAction?.Invoke();
                }
                catch (Exception ex)
                    when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    Debug.LogWarning(
                        $"AssetPostprocessorTestHandlers.ResetShouldThrowAll: "
                            + $"resetting {entry.HandlerType.FullName}.ShouldThrow threw: {ex.Message}"
                    );
                }
            }
        }

        private static void ClearAllInternal(IReadOnlyList<HandlerEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                HandlerEntry entry = entries[i];
                try
                {
                    entry.ClearAction?.Invoke();
                }
                catch (Exception ex)
                    when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    Debug.LogWarning(
                        $"AssetPostprocessorTestHandlers.ClearAllInternal: "
                            + $"{entry.HandlerType.FullName}.Clear() threw: {ex.Message}"
                    );
                }
            }
        }

        /// <summary>
        /// Returns zero-or-more diagnostic strings describing pollution on any
        /// discovered handler. Does not clear, does not fail. Used for diagnostics
        /// and by <see cref="AssertCleanAndClearAll"/>.
        /// </summary>
        internal static IReadOnlyList<string> DescribePollution()
        {
            IReadOnlyList<HandlerEntry> entries = LazyEntries.Value;
            List<string> diagnostics = new();
            for (int i = 0; i < entries.Count; i++)
            {
                HandlerEntry entry = entries[i];
                int contextsCount =
                    entry.RecordedContextsCountGetter != null
                        ? entry.RecordedContextsCountGetter()
                        : 0;
                int instancesCount =
                    entry.RecordedInstancesCountGetter != null
                        ? entry.RecordedInstancesCountGetter()
                        : 0;

                // Extra handler surfaces can be dirty even when canonical recorded lists are empty.
                List<PollutionProbe> dirtyExtras = null;
                if (entry.ExtraPollutionProbes != null)
                {
                    foreach (PollutionProbe probe in entry.ExtraPollutionProbes)
                    {
                        bool dirty;
                        try
                        {
                            dirty = probe.IsDirty != null && probe.IsDirty();
                        }
                        catch (Exception ex)
                            when (ex is not OutOfMemoryException and not StackOverflowException)
                        {
                            /*
                                                    If a probe throws, surface that as pollution rather than
                                                    silently masking the handler — a misbehaving probe is a
                                                    signal the handler's API has drifted.
                                                */
                            dirty = true;
                        }

                        if (!dirty)
                        {
                            continue;
                        }

                        dirtyExtras ??= new List<PollutionProbe>();
                        dirtyExtras.Add(probe);
                    }
                }

                if (
                    contextsCount == 0
                    && instancesCount == 0
                    && (dirtyExtras == null || dirtyExtras.Count == 0)
                )
                {
                    continue;
                }

                // Report only dirty surfaces so clean canonical fields are not blamed for unrelated pollution.
                bool emitContexts = entry.RecordedContextsEnumerator != null && 0 < contextsCount;
                bool emitInstances =
                    entry.RecordedInstancesEnumerator != null && 0 < instancesCount;

                StringBuilder message = new();
                message.Append(entry.HandlerType.Name).Append(" pollution");

                bool appendedAny = false;
                if (emitContexts)
                {
                    string contextsDetail = string.Join(
                        ", ",
                        entry.RecordedContextsEnumerator().Select(c => FormatContext(c))
                    );
                    message.Append(": RecordedContexts.Count=").Append(contextsCount);
                    message.Append(", Contexts=[").Append(contextsDetail).Append(']');
                    appendedAny = true;
                }

                if (emitInstances)
                {
                    string instancesDetail;
                    if (entry.RecordedInstancesAreUnityObjects)
                    {
                        instancesDetail = string.Join(
                            ", ",
                            entry.RecordedInstancesEnumerator().Select(i => FormatInstanceId(i))
                        );
                    }
                    else
                    {
                        // The outer diagnostic already includes the instance count.
                        instancesDetail = "<non-unity-object instances>";
                    }

                    message.Append(appendedAny ? ", " : ": ");
                    message.Append("RecordedInstances.Count=").Append(instancesCount);
                    message.Append(", InstanceIDs=[").Append(instancesDetail).Append(']');
                    appendedAny = true;
                }

                if (dirtyExtras != null)
                {
                    foreach (PollutionProbe probe in dirtyExtras)
                    {
                        string detail;
                        try
                        {
                            detail = probe.Describe != null ? probe.Describe() : "<dirty>";
                        }
                        catch (Exception ex)
                            when (ex is not OutOfMemoryException and not StackOverflowException)
                        {
                            /*
                                Escape envelope delimiters, commas, and control characters so exception text
                                cannot corrupt the one-line diagnostic.
                            */
                            string rawMessage = ex.Message ?? string.Empty;
                            StringBuilder sanitizedBuilder = new(rawMessage.Length);
                            for (int c = 0; c < rawMessage.Length; c++)
                            {
                                char ch = rawMessage[c];
                                if (ch == '<')
                                {
                                    sanitizedBuilder.Append('[');
                                }
                                else if (ch == '>')
                                {
                                    sanitizedBuilder.Append(']');
                                }
                                else if (ch == ',')
                                {
                                    sanitizedBuilder.Append(';');
                                }
                                else if (char.IsControl(ch))
                                {
                                    sanitizedBuilder.Append(' ');
                                }
                                else
                                {
                                    sanitizedBuilder.Append(ch);
                                }
                            }

                            detail = "<describe-threw:" + sanitizedBuilder + ">";
                        }

                        // Keep the first extra surface colon-delimited to match the canonical diagnostic format.
                        message.Append(appendedAny ? ", " : ": ");
                        message.Append(probe.SurfaceName).Append('=').Append(detail);
                        appendedAny = true;
                    }
                }

                diagnostics.Add(message.ToString());
            }
            return diagnostics;
        }

        /// <summary>
        /// Collects pollution diagnostics across every discovered handler, clears
        /// all handlers, then fails with <see cref="Assert.Fail(string)"/> if any
        /// pollution was observed. Mirrors the old per-fixture
        /// <c>AssertAllHandlersCleanAndClear</c> but is exhaustive by construction.
        ///
        /// Order is load-bearing: reset ShouldThrow, then flush queued drains, then
        /// snapshot pollution, then clear. Snapshotting BEFORE the flush would miss
        /// pollution that a queued drain is about to write to the handlers — the
        /// tripwire would observe "clean" state just before the drain re-populated
        /// it, and the next test would fail for reasons unrelated to its own setup.
        ///
        /// <para><b>Canonical tripwire usage from per-test <c>SetUp</c>:</b></para>
        /// Call this FIRST in a fixture's <c>SetUp</c>, BEFORE any asset mutation
        /// or processor configuration. The rationale is attribution: asset
        /// mutations scheduled later in the setup enqueue drains whose pollution,
        /// if observed by a later-running tripwire, would be misattributed to the
        /// prior fixture (it would look like leaked-in state when in reality the
        /// current fixture produced it). Failing loudly up front keeps the
        /// regression visible and pinned to its true source.
        ///
        /// This helper internally flushes, snapshots, then clears, so callers
        /// never need a separate pre-flush call. Fixtures whose <c>SetUp</c>
        /// goes on to mutate assets should, however, re-flush after those
        /// mutations (e.g. via <see cref="FlushAndClearAll"/>) so the test body
        /// runs against a freshly-cleared handler surface.
        ///
        /// <para><b>Visibility:</b> <see langword="public"/> because fixtures in sibling test
        /// assemblies (Sprites, Windows, Utils) must call this through an assembly
        /// reference — <see langword="internal"/> would invisibly break the cross-assembly
        /// tripwire contract with a CS0122. All other members of this class stay
        /// <see langword="internal"/> because they are only reached from within this
        /// assembly.</para>
        /// </summary>
        public static void AssertCleanAndClearAll()
        {
            IReadOnlyList<HandlerEntry> entries = LazyEntries.Value;
            ResetShouldThrowAll(entries);
            AssetPostprocessorDeferral.FlushForTesting();
            IReadOnlyList<string> pollutionErrors = DescribePollution();
            ClearAllInternal(entries);
            if (0 < pollutionErrors.Count)
            {
                Assert.Fail(
                    "Test pollution detected from prior test. Handler state was not clean at test start:\n"
                        + string.Join("\n", pollutionErrors)
                );
            }
        }

        private static IReadOnlyList<HandlerEntry> DiscoverHandlerEntries()
        {
            TypeCache.MethodCollection methods;
            try
            {
                methods = TypeCache.GetMethodsWithAttribute<DetectAssetChangedAttribute>();
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Debug.LogWarning(
                    "AssetPostprocessorTestHandlers: TypeCache.GetMethodsWithAttribute failed. "
                        + $"No handlers will be discovered. Exception: {ex.Message}"
                );
                return Array.Empty<HandlerEntry>();
            }

            HashSet<Type> seen = new();
            List<HandlerEntry> entries = new();
            foreach (MethodInfo method in methods)
            {
                if (method == null)
                {
                    continue;
                }

                Type declaringType = method.DeclaringType;
                if (declaringType == null || !seen.Add(declaringType))
                {
                    continue;
                }

                Assembly assembly = declaringType.Assembly;
                if (assembly == null)
                {
                    continue;
                }

                string assemblyName = assembly.GetName().Name;
                if (
                    string.IsNullOrEmpty(assemblyName)
                    || !assemblyName.StartsWith(TestAssemblyPrefix, StringComparison.Ordinal)
                )
                {
                    continue;
                }

                MethodInfo clearMethod = declaringType.GetMethod(
                    "Clear",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null
                );
                if (clearMethod == null || clearMethod.ReturnType != typeof(void))
                {
                    continue;
                }

                HandlerEntry entry = new()
                {
                    HandlerType = declaringType,
                    ClearAction = BuildClearAction(clearMethod),
                };

                PropertyInfo contextsProperty = declaringType.GetProperty(
                    "RecordedContexts",
                    BindingFlags.Public | BindingFlags.Static
                );
                PropertyInfo instancesProperty = declaringType.GetProperty(
                    "RecordedInstances",
                    BindingFlags.Public | BindingFlags.Static
                );

                if (contextsProperty != null)
                {
                    entry.RecordedContextsCountGetter = BuildListCountGetter(contextsProperty);
                    entry.RecordedContextsEnumerator = BuildListEnumerator(contextsProperty);
                }

                if (instancesProperty != null)
                {
                    entry.RecordedInstancesCountGetter = BuildListCountGetter(instancesProperty);
                    entry.RecordedInstancesEnumerator = BuildListEnumerator(instancesProperty);

                    Type elementType = ExtractReadOnlyListElementType(
                        instancesProperty.PropertyType
                    );
                    entry.RecordedInstancesAreUnityObjects =
                        elementType != null && typeof(Object).IsAssignableFrom(elementType);
                }

                PropertyInfo shouldThrowProperty = declaringType.GetProperty(
                    "ShouldThrow",
                    BindingFlags.Public | BindingFlags.Static
                );
                if (
                    shouldThrowProperty != null
                    && shouldThrowProperty.PropertyType == typeof(bool)
                    && shouldThrowProperty.CanWrite
                )
                {
                    entry.ResetShouldThrowAction = BuildResetShouldThrowAction(shouldThrowProperty);
                }

                /*
                    Discover alternate counters and recorded collections so renaming a handler surface cannot
                    hide its pollution.
                */
                HashSet<string> canonicalSurfaceNames = new(StringComparer.Ordinal)
                {
                    "RecordedContexts",
                    "RecordedInstances",
                    "ShouldThrow",
                };
                PropertyInfo[] properties = declaringType.GetProperties(
                    BindingFlags.Public | BindingFlags.Static
                );
                foreach (PropertyInfo property in properties)
                {
                    if (property == null || canonicalSurfaceNames.Contains(property.Name))
                    {
                        continue;
                    }

                    PollutionProbe probe = TryBuildPollutionProbe(property);
                    if (probe != null)
                    {
                        entry.ExtraPollutionProbes.Add(probe);
                    }
                }

                entries.Add(entry);
            }

            if (entries.Count == 0)
            {
                Debug.LogWarning(
                    "AssetPostprocessorTestHandlers: discovered zero [DetectAssetChanged] "
                        + "handler types in test assemblies. Verify that TypeCache is populated "
                        + "and that the handler test doubles compile with UNITY_INCLUDE_TESTS."
                );
            }

            return entries;
        }

        private static Action BuildClearAction(MethodInfo clearMethod)
        {
            try
            {
                return (Action)Delegate.CreateDelegate(typeof(Action), clearMethod);
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Debug.LogWarning(
                    $"AssetPostprocessorTestHandlers: could not bind Clear() delegate for "
                        + $"{clearMethod.DeclaringType?.FullName}: {ex.Message}"
                );
                return () => clearMethod.Invoke(null, Array.Empty<object>());
            }
        }

        private static Func<int> BuildListCountGetter(PropertyInfo listProperty)
        {
            return () =>
            {
                object value = listProperty.GetValue(null);
                if (value is ICollection collection)
                {
                    return collection.Count;
                }

                if (value is IEnumerable enumerable)
                {
                    int count = 0;
                    foreach (object _ in enumerable)
                    {
                        count++;
                    }
                    return count;
                }

                return 0;
            };
        }

        private static Func<IEnumerable<object>> BuildListEnumerator(PropertyInfo listProperty)
        {
            return () =>
            {
                object value = listProperty.GetValue(null);
                if (value is IEnumerable enumerable)
                {
                    return EnumerateAsObjects(enumerable);
                }
                return Array.Empty<object>();
            };
        }

        private static IEnumerable<object> EnumerateAsObjects(IEnumerable source)
        {
            foreach (object item in source)
            {
                yield return item;
            }
        }

        private static Action BuildResetShouldThrowAction(PropertyInfo shouldThrowProperty)
        {
            return () => shouldThrowProperty.SetValue(null, false);
        }

        /// <summary>
        /// Builds a <see cref="PollutionProbe"/> for an auto-discovered pollution
        /// surface on a handler type. Returns <see langword="null"/> when the property
        /// shape isn't one the probe system knows how to observe (so we don't claim
        /// pollution on e.g. a configuration-only property like
        /// <c>TestReentrantHandler.Configure</c>).
        ///
        /// Recognized shapes:
        ///   - <c>int</c> / <c>long</c> counters (non-zero => dirty). Common name:
        ///     <c>InvocationCount</c>.
        ///   - <see cref="IEnumerable"/> collections (non-empty => dirty). Common
        ///     names: <c>RecordedInvocations</c>, <c>RecordedCreated</c>,
        ///     <c>RecordedDeletedPaths</c>, <c>LastCreatedAssets</c>,
        ///     <c>LastDeletedPaths</c>.
        /// Other shapes (e.g. a <c>string WatchedPath</c>) are skipped — authors who
        /// want those observed should add their own probe in <c>Clear()</c>'s contract.
        /// </summary>
        private static PollutionProbe TryBuildPollutionProbe(PropertyInfo property)
        {
            if (property == null || !property.CanRead)
            {
                return null;
            }

            Type propertyType = property.PropertyType;
            if (propertyType == null)
            {
                return null;
            }

            // Indexer or parameterized getter — cannot read without arguments.
            ParameterInfo[] indexParameters;
            try
            {
                indexParameters = property.GetIndexParameters();
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                return null;
            }
            if (indexParameters != null && 0 < indexParameters.Length)
            {
                return null;
            }

            if (
                propertyType == typeof(int)
                || propertyType == typeof(long)
                || propertyType == typeof(uint)
                || propertyType == typeof(ulong)
            )
            {
                string surfaceName = property.Name;
                return new PollutionProbe
                {
                    SurfaceName = surfaceName,
                    IsDirty = () =>
                    {
                        object value = SafeGet(property);
                        return value != null && !IsZeroNumber(value);
                    },
                    Describe = () =>
                    {
                        object value = SafeGet(property);
                        return value == null ? "<null>" : value.ToString();
                    },
                };
            }

            // Exclude strings and value types from collection probing to avoid false matches and boxing.
            if (
                propertyType != typeof(string)
                && !propertyType.IsPrimitive
                && typeof(IEnumerable).IsAssignableFrom(propertyType)
            )
            {
                string surfaceName = property.Name;
                return new PollutionProbe
                {
                    SurfaceName = surfaceName,
                    IsDirty = () =>
                    {
                        object value = SafeGet(property);
                        return IsNonEmptyEnumerable(value);
                    },
                    Describe = () =>
                    {
                        object value = SafeGet(property);
                        int count = CountEnumerable(value);
                        return $"Count={count}";
                    },
                };
            }

            return null;
        }

        private static object SafeGet(PropertyInfo property)
        {
            try
            {
                return property.GetValue(null);
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                return null;
            }
        }

        private static bool IsZeroNumber(object value)
        {
            if (value is int i)
            {
                return i == 0;
            }
            if (value is long l)
            {
                return l == 0L;
            }
            if (value is uint ui)
            {
                return ui == 0u;
            }
            if (value is ulong ul)
            {
                return ul == 0UL;
            }

            return true;
        }

        private static bool IsNonEmptyEnumerable(object value)
        {
            if (value == null)
            {
                return false;
            }

            if (value is ICollection collection)
            {
                return 0 < collection.Count;
            }

            if (value is IEnumerable enumerable)
            {
                IEnumerator enumerator = enumerable.GetEnumerator();
                try
                {
                    return enumerator.MoveNext();
                }
                finally
                {
                    (enumerator as IDisposable)?.Dispose();
                }
            }

            return false;
        }

        private static int CountEnumerable(object value)
        {
            if (value == null)
            {
                return 0;
            }

            if (value is ICollection collection)
            {
                return collection.Count;
            }

            if (value is IEnumerable enumerable)
            {
                int count = 0;
                foreach (object _ in enumerable)
                {
                    count++;
                }
                return count;
            }

            return 0;
        }

        private static Type ExtractReadOnlyListElementType(Type propertyType)
        {
            if (propertyType == null)
            {
                return null;
            }

            if (
                propertyType.IsGenericType
                && propertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            )
            {
                return propertyType.GetGenericArguments()[0];
            }

            Type[] interfaces = propertyType.GetInterfaces();
            foreach (Type candidate in interfaces)
            {
                if (
                    candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                )
                {
                    return candidate.GetGenericArguments()[0];
                }
            }

            return null;
        }

        private static string FormatContext(object context)
        {
            if (context == null)
            {
                return "null";
            }

            // Reflection avoids coupling this helper to the context struct’s concrete flags shape.
            Type type = context.GetType();
            PropertyInfo flagsProperty = type.GetProperty(
                "Flags",
                BindingFlags.Public | BindingFlags.Instance
            );
            if (flagsProperty != null)
            {
                object flags = flagsProperty.GetValue(context);
                return $"Flags={flags}";
            }

            return context.ToString();
        }

        private static string FormatInstanceId(object instance)
        {
            if (instance is Object unityObject && unityObject != null)
            {
                return unityObject.GetUnityObjectId().ToString();
            }

            return "null";
        }

        /// <summary>
        /// Reflected information cached once per handler type.
        /// </summary>
        private sealed class HandlerEntry
        {
            internal Type HandlerType;
            internal Action ClearAction;
            internal Func<int> RecordedContextsCountGetter;
            internal Func<int> RecordedInstancesCountGetter;
            internal Func<IEnumerable<object>> RecordedContextsEnumerator;
            internal Func<IEnumerable<object>> RecordedInstancesEnumerator;
            internal bool RecordedInstancesAreUnityObjects;
            internal Action ResetShouldThrowAction;

            /// <summary>
            /// Additional pollution surfaces (counters, side-channel recorded lists,
            /// and array-shaped "last observed" properties) not covered by the canonical
            /// RecordedContexts/RecordedInstances pair. Each entry produces a diagnostic
            /// line when non-zero/non-empty so authors of alternate-shaped handlers
            /// (e.g. InvocationCount counters, RecordedInvocations, RecordedCreated,
            /// RecordedDeletedPaths, LastCreatedAssets[], LastDeletedPaths[]) get the
            /// same pollution observability as the canonical pair.
            /// </summary>
            internal List<PollutionProbe> ExtraPollutionProbes = new();
        }

        /// <summary>
        /// Observes one pollution surface on a handler. <see cref="IsDirty"/> returns
        /// <see langword="true"/> when the surface has non-default content (non-zero
        /// count, non-zero counter, non-empty array). <see cref="Describe"/> formats
        /// the surface for the pollution diagnostic message.
        /// </summary>
        private sealed class PollutionProbe
        {
            internal string SurfaceName;
            internal Func<bool> IsDirty;
            internal Func<string> Describe;
        }
    }
}
#endif
