// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WButton
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading;

    internal static class WButtonStateRepository
    {
        private static readonly ConditionalWeakTable<
            UnityEngine.Object,
            WButtonTargetState
        > TargetStates = new();

        // Held rather than written as a method group at the call site: C# does not cache a
        // method-group conversion before C# 11, and Unity is on C# 9, so `GetValue(target,
        // CreateState)` builds a new callback on every lookup, hit included (WUH001). The caller
        // sits behind `WButtonGUI`'s own context cache, so this is a miss-path lookup rather than a
        // per-repaint one -- the fix is the idiom, not a measured frame-time win.
        private static readonly ConditionalWeakTable<
            UnityEngine.Object,
            WButtonTargetState
        >.CreateValueCallback StateFactory = CreateState;

        internal static WButtonTargetState GetOrCreate(UnityEngine.Object target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return TargetStates.GetValue(target, StateFactory);
        }

        private static WButtonTargetState CreateState(UnityEngine.Object target)
        {
            Type targetType = target.GetType();
            return new WButtonTargetState(targetType);
        }
    }

    internal sealed class WButtonTargetState
    {
        private readonly Dictionary<MethodKey, WButtonMethodState> _methodStates = new();

        internal WButtonTargetState(Type targetType)
        {
            TargetType = targetType ?? typeof(UnityEngine.Object);
        }

        internal Type TargetType { get; }

        internal WButtonMethodState GetOrCreateMethodState(WButtonMethodMetadata metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            MethodKey key = new(metadata.Method);
            if (_methodStates.TryGetValue(key, out WButtonMethodState state))
            {
                return state;
            }

            state = new WButtonMethodState(metadata);
            _methodStates[key] = state;
            return state;
        }
    }

    internal sealed class WButtonMethodState
    {
        internal WButtonMethodState(WButtonMethodMetadata metadata)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            WButtonParameterMetadata[] parameterMetadata = metadata.Parameters;
            if (parameterMetadata == null || parameterMetadata.Length == 0)
            {
                Parameters = Array.Empty<WButtonParameterState>();
            }
            else
            {
                WButtonParameterState[] states = new WButtonParameterState[
                    parameterMetadata.Length
                ];
                for (int index = 0; index < parameterMetadata.Length; index++)
                {
                    states[index] = new WButtonParameterState(parameterMetadata[index]);
                }

                Parameters = states;
            }
        }

        internal WButtonMethodMetadata Metadata { get; }

        internal WButtonParameterState[] Parameters { get; }

        internal WButtonInvocationHandle ActiveInvocation { get; set; }

        internal List<WButtonResultEntry> History { get; } = new();

        internal bool HasHistory => History.Count > 0;

        internal void AddResult(WButtonResultEntry entry, int historyCapacity)
        {
            if (entry == null)
            {
                return;
            }

            History.Add(entry);
            if (historyCapacity > 0)
            {
                int overflow = History.Count - historyCapacity;
                if (overflow > 0)
                {
                    History.RemoveRange(0, overflow);
                }
            }
        }

        internal void ClearHistory()
        {
            if (!HasHistory)
            {
                return;
            }

            History.Clear();
        }
    }

    internal sealed class WButtonParameterState
    {
        internal WButtonParameterState(WButtonParameterMetadata metadata)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            CurrentValue = CreateInitialValue(metadata);
        }

        internal WButtonParameterMetadata Metadata { get; }

        internal object CurrentValue { get; set; }

        internal string JsonFallback { get; set; }

        private static object CreateInitialValue(WButtonParameterMetadata metadata)
        {
            if (metadata.IsCancellationToken)
            {
                return CancellationToken.None;
            }

            if (metadata.HasDefaultValue)
            {
                return CloneIfNeeded(metadata.DefaultValue);
            }

            Type parameterType = metadata.ParameterType;
            if (metadata.IsParamsArray)
            {
                Type elementType = parameterType.GetElementType() ?? typeof(object);
                return Array.CreateInstance(elementType, 0);
            }

            if (parameterType.IsValueType)
            {
                if (WButtonValueUtility.TryCreateInstance(parameterType, out object created))
                {
                    return created;
                }
                return null;
            }

            if (parameterType == typeof(string))
            {
                return string.Empty;
            }

            return null;
        }

        private static object CloneIfNeeded(object defaultValue)
        {
            if (defaultValue is Array array)
            {
                return array.Clone();
            }

            return defaultValue;
        }
    }

    internal readonly struct MethodKey : IEquatable<MethodKey>
    {
        private readonly MethodBase _method;

        internal MethodKey(MethodBase method)
        {
            _method = method ?? throw new ArgumentNullException(nameof(method));
        }

        public bool Equals(MethodKey other)
        {
            return Equals(_method, other._method);
        }

        public override bool Equals(object obj)
        {
            if (obj is MethodKey other)
            {
                return Equals(other);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return _method.GetHashCode();
        }
    }
#endif
}
