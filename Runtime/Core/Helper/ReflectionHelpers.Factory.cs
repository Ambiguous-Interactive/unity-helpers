// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if !((UNITY_WEBGL && !UNITY_EDITOR) || ENABLE_IL2CPP)
#define EMIT_DYNAMIC_IL
#define SUPPORT_EXPRESSION_COMPILE
#endif

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
#if !SINGLE_THREADED
    using System.Collections.Concurrent;
#endif

    // ReflectionHelpers.Factory.cs - Delegate creation and strategy management
    // See ReflectionHelpers.cs for full architecture documentation
    public static partial class ReflectionHelpers
    {
        /// <summary>
        /// Specifies which implementation strategy to use for creating reflection delegates.
        /// </summary>
        /// <remarks>
        /// The <see cref="DelegateFactory"/> tries strategies in order: Expressions → DynamicIl → Reflection.
        /// Failed strategies are tracked in a blocklist to avoid repeated attempts.
        /// </remarks>
        internal enum ReflectionDelegateStrategy
        {
            [Obsolete("Use a concrete strategy value.", false)]
            Unknown = 0,
            Expressions = 1,
            DynamicIl = 2,
            Reflection = 3,
        }

        /// <summary>
        /// Internal factory responsible for creating, caching, and managing reflection delegates.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For each member type (FieldInfo, PropertyInfo, MethodInfo, ConstructorInfo), this factory:
        /// </para>
        /// <list type="number">
        ///   <item>Checks if a delegate is already cached for the requested strategy</item>
        ///   <item>Checks if the strategy has previously failed (blocklist)</item>
        ///   <item>Attempts to create a new delegate using the strategy</item>
        ///   <item>On failure, marks the strategy as unavailable and tries the next</item>
        ///   <item>Caches successful delegates for future use</item>
        /// </list>
        /// <para>
        /// The factory uses <see cref="CapabilityKey{T}"/> to uniquely identify cache entries
        /// by both member and strategy, allowing different strategies to coexist in the cache.
        /// </para>
        /// </remarks>
        private static class DelegateFactory
        {
            private const byte StrategyUnavailableSentinel = 0;

            private static readonly ConditionalWeakTable<
                Delegate,
                StrategyHolder
            > DelegateStrategyTable = new();
#if !SINGLE_THREADED
            private static readonly ConcurrentDictionary<
                CapabilityKey<FieldInfo>,
                Func<object, object>
            > FieldGetters = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<FieldInfo>,
                byte
            > FieldGetterStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<FieldInfo>,
                Action<object, object>
            > FieldSetters = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<FieldInfo>,
                byte
            > FieldSetterStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<FieldInfo>,
                Func<object>
            > StaticFieldGetters = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<FieldInfo>,
                byte
            > StaticFieldGetterStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<FieldInfo>,
                Action<object>
            > StaticFieldSetters = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<FieldInfo>,
                byte
            > StaticFieldSetterStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<PropertyInfo>,
                Func<object, object>
            > PropertyGetters = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<PropertyInfo>,
                byte
            > PropertyGetterStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<PropertyInfo>,
                Action<object, object>
            > PropertySetters = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<PropertyInfo>,
                byte
            > PropertySetterStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<PropertyInfo>,
                Func<object, object[], object>
            > IndexerGetters = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<PropertyInfo>,
                byte
            > IndexerGetterStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<PropertyInfo>,
                Action<object, object, object[]>
            > IndexerSetters = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<PropertyInfo>,
                byte
            > IndexerSetterStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<(FieldInfo field, Type instance, Type value)>,
                Delegate
            > TypedFieldGetters = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<(FieldInfo field, Type instance, Type value)>,
                byte
            > TypedFieldGetterStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<(FieldInfo field, Type instance, Type value)>,
                Delegate
            > TypedFieldSetters = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<(FieldInfo field, Type instance, Type value)>,
                byte
            > TypedFieldSetterStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<(FieldInfo field, Type value)>,
                Delegate
            > TypedStaticFieldGetters = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<(FieldInfo field, Type value)>,
                byte
            > TypedStaticFieldGetterStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<(FieldInfo field, Type value)>,
                Delegate
            > TypedStaticFieldSetters = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<(FieldInfo field, Type value)>,
                byte
            > TypedStaticFieldSetterStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<MethodInfo>,
                Func<object, object[], object>
            > MethodInvokers = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<MethodInfo>,
                byte
            > MethodInvokerStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<MethodInfo>,
                Func<object[], object>
            > StaticMethodInvokers = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<MethodInfo>,
                byte
            > StaticMethodInvokerStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<ConstructorInfo>,
                Func<object[], object>
            > Constructors = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<ConstructorInfo>,
                byte
            > ConstructorInvokerStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<ConstructorInfo>,
                Func<object>
            > ParameterlessConstructors = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<ConstructorInfo>,
                byte
            > ParameterlessConstructorStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<(ConstructorInfo ctor, Type instance)>,
                Delegate
            > TypedParameterlessConstructors = new();
            private static readonly ConcurrentDictionary<
                CapabilityKey<(ConstructorInfo ctor, Type instance)>,
                byte
            > TypedParameterlessConstructorStrategyBlocklist = new();
            private static readonly ConcurrentDictionary<
                (PropertyInfo property, Type instance, Type value),
                Delegate
            > TypedPropertyGetters = new();
            private static readonly ConcurrentDictionary<
                (PropertyInfo property, Type instance, Type value),
                Delegate
            > TypedPropertySetters = new();
            private static readonly ConcurrentDictionary<
                (PropertyInfo property, Type value),
                Delegate
            > TypedStaticPropertyGetters = new();
            private static readonly ConcurrentDictionary<
                (PropertyInfo property, Type value),
                Delegate
            > TypedStaticPropertySetters = new();
            private static readonly ConcurrentDictionary<MethodInfo, Delegate> TypedStaticInvoker0 =
                new();
            private static readonly ConcurrentDictionary<MethodInfo, Delegate> TypedStaticInvoker1 =
                new();
            private static readonly ConcurrentDictionary<MethodInfo, Delegate> TypedStaticInvoker2 =
                new();
            private static readonly ConcurrentDictionary<MethodInfo, Delegate> TypedStaticInvoker3 =
                new();
            private static readonly ConcurrentDictionary<MethodInfo, Delegate> TypedStaticInvoker4 =
                new();
            private static readonly ConcurrentDictionary<MethodInfo, Delegate> TypedStaticAction0 =
                new();
            private static readonly ConcurrentDictionary<MethodInfo, Delegate> TypedStaticAction1 =
                new();
            private static readonly ConcurrentDictionary<MethodInfo, Delegate> TypedStaticAction2 =
                new();
            private static readonly ConcurrentDictionary<MethodInfo, Delegate> TypedStaticAction3 =
                new();
            private static readonly ConcurrentDictionary<MethodInfo, Delegate> TypedStaticAction4 =
                new();
            private static readonly ConcurrentDictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceInvoker0 = new();
            private static readonly ConcurrentDictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceInvoker1 = new();
            private static readonly ConcurrentDictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceInvoker2 = new();
            private static readonly ConcurrentDictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceInvoker3 = new();
            private static readonly ConcurrentDictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceInvoker4 = new();
            private static readonly ConcurrentDictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceAction0 = new();
            private static readonly ConcurrentDictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceAction1 = new();
            private static readonly ConcurrentDictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceAction2 = new();
            private static readonly ConcurrentDictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceAction3 = new();
            private static readonly ConcurrentDictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceAction4 = new();
#else
            private static readonly Dictionary<
                CapabilityKey<FieldInfo>,
                Func<object, object>
            > FieldGetters = new();
            private static readonly HashSet<CapabilityKey<FieldInfo>> FieldGetterStrategyBlocklist =
                new();
            private static readonly Dictionary<
                CapabilityKey<FieldInfo>,
                Action<object, object>
            > FieldSetters = new();
            private static readonly HashSet<CapabilityKey<FieldInfo>> FieldSetterStrategyBlocklist =
                new();
            private static readonly Dictionary<
                CapabilityKey<FieldInfo>,
                Func<object>
            > StaticFieldGetters = new();
            private static readonly HashSet<
                CapabilityKey<FieldInfo>
            > StaticFieldGetterStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<FieldInfo>,
                Action<object>
            > StaticFieldSetters = new();
            private static readonly HashSet<
                CapabilityKey<FieldInfo>
            > StaticFieldSetterStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<PropertyInfo>,
                Func<object, object>
            > PropertyGetters = new();
            private static readonly HashSet<
                CapabilityKey<PropertyInfo>
            > PropertyGetterStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<PropertyInfo>,
                Action<object, object>
            > PropertySetters = new();
            private static readonly HashSet<
                CapabilityKey<PropertyInfo>
            > PropertySetterStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<PropertyInfo>,
                Func<object, object[], object>
            > IndexerGetters = new();
            private static readonly Dictionary<
                CapabilityKey<PropertyInfo>,
                byte
            > IndexerGetterStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<PropertyInfo>,
                Action<object, object, object[]>
            > IndexerSetters = new();
            private static readonly Dictionary<
                CapabilityKey<PropertyInfo>,
                byte
            > IndexerSetterStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<(FieldInfo field, Type instance, Type value)>,
                Delegate
            > TypedFieldGetters = new();
            private static readonly HashSet<
                CapabilityKey<(FieldInfo field, Type instance, Type value)>
            > TypedFieldGetterStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<(FieldInfo field, Type instance, Type value)>,
                Delegate
            > TypedFieldSetters = new();
            private static readonly HashSet<
                CapabilityKey<(FieldInfo field, Type instance, Type value)>
            > TypedFieldSetterStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<(FieldInfo field, Type value)>,
                Delegate
            > TypedStaticFieldGetters = new();
            private static readonly HashSet<
                CapabilityKey<(FieldInfo field, Type value)>
            > TypedStaticFieldGetterStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<(FieldInfo field, Type value)>,
                Delegate
            > TypedStaticFieldSetters = new();
            private static readonly HashSet<
                CapabilityKey<(FieldInfo field, Type value)>
            > TypedStaticFieldSetterStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<MethodInfo>,
                Func<object, object[], object>
            > MethodInvokers = new();
            private static readonly Dictionary<
                CapabilityKey<MethodInfo>,
                byte
            > MethodInvokerStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<MethodInfo>,
                Func<object[], object>
            > StaticMethodInvokers = new();
            private static readonly HashSet<
                CapabilityKey<MethodInfo>
            > StaticMethodInvokerStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<ConstructorInfo>,
                Func<object[], object>
            > Constructors = new();
            private static readonly HashSet<
                CapabilityKey<ConstructorInfo>
            > ConstructorInvokerStrategyBlocklist = new();
            private static readonly Dictionary<
                CapabilityKey<ConstructorInfo>,
                Func<object>
            > ParameterlessConstructors = new();
            private static readonly Dictionary<
                CapabilityKey<(ConstructorInfo ctor, Type instance)>,
                Delegate
            > TypedParameterlessConstructors = new();
            private static readonly HashSet<
                CapabilityKey<(ConstructorInfo ctor, Type instance)>
            > TypedParameterlessConstructorStrategyBlocklist = new();
            private static readonly HashSet<
                CapabilityKey<ConstructorInfo>
            > ParameterlessConstructorStrategyBlocklist = new();
            private static readonly Dictionary<
                (PropertyInfo property, Type instance, Type value),
                Delegate
            > TypedPropertyGetters = new();
            private static readonly Dictionary<
                (PropertyInfo property, Type instance, Type value),
                Delegate
            > TypedPropertySetters = new();
            private static readonly Dictionary<
                (PropertyInfo property, Type value),
                Delegate
            > TypedStaticPropertyGetters = new();
            private static readonly Dictionary<
                (PropertyInfo property, Type value),
                Delegate
            > TypedStaticPropertySetters = new();
            private static readonly Dictionary<MethodInfo, Delegate> TypedStaticInvoker0 = new();
            private static readonly Dictionary<MethodInfo, Delegate> TypedStaticInvoker1 = new();
            private static readonly Dictionary<MethodInfo, Delegate> TypedStaticInvoker2 = new();
            private static readonly Dictionary<MethodInfo, Delegate> TypedStaticInvoker3 = new();
            private static readonly Dictionary<MethodInfo, Delegate> TypedStaticInvoker4 = new();
            private static readonly Dictionary<MethodInfo, Delegate> TypedStaticAction0 = new();
            private static readonly Dictionary<MethodInfo, Delegate> TypedStaticAction1 = new();
            private static readonly Dictionary<MethodInfo, Delegate> TypedStaticAction2 = new();
            private static readonly Dictionary<MethodInfo, Delegate> TypedStaticAction3 = new();
            private static readonly Dictionary<MethodInfo, Delegate> TypedStaticAction4 = new();
            private static readonly Dictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceInvoker0 = new();
            private static readonly Dictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceInvoker1 = new();
            private static readonly Dictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceInvoker2 = new();
            private static readonly Dictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceInvoker3 = new();
            private static readonly Dictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceInvoker4 = new();
            private static readonly Dictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceAction0 = new();
            private static readonly Dictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceAction1 = new();
            private static readonly Dictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceAction2 = new();
            private static readonly Dictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceAction3 = new();
            private static readonly Dictionary<
                (MethodInfo method, Type instance),
                Delegate
            > TypedInstanceAction4 = new();
#endif

            private static bool SupportsExpressions => ExpressionsEnabled;
            private static bool SupportsDynamicIl => DynamicIlEnabled;

            public static Func<object, object> GetFieldGetter(FieldInfo field)
            {
                if (field == null)
                {
                    throw new ArgumentNullException(nameof(field));
                }

                /*
                    Prefer Dynamic IL over Expressions because Expression.Compile()
                    creates closure-based delegates that are slower than direct IL emission.
                */
                if (
                    TryGetOrCreateFieldGetter(
                        field,
                        ReflectionDelegateStrategy.DynamicIl,
                        out Func<object, object> getter
                    )
                )
                {
                    return getter;
                }

                if (
                    TryGetOrCreateFieldGetter(
                        field,
                        ReflectionDelegateStrategy.Expressions,
                        out getter
                    )
                )
                {
                    return getter;
                }

                return GetOrCreateReflectionFieldGetter(field);
            }

            public static bool IsFieldGetterCached(FieldInfo field)
            {
                if (field == null)
                {
                    return false;
                }

                CapabilityKey<FieldInfo> expressionsKey = new(
                    field,
                    ReflectionDelegateStrategy.Expressions
                );
                CapabilityKey<FieldInfo> dynamicIlKey = new(
                    field,
                    ReflectionDelegateStrategy.DynamicIl
                );
                CapabilityKey<FieldInfo> reflectionKey = new(
                    field,
                    ReflectionDelegateStrategy.Reflection
                );

                return FieldGetters.ContainsKey(expressionsKey)
                    || FieldGetters.ContainsKey(dynamicIlKey)
                    || FieldGetters.ContainsKey(reflectionKey);
            }

            public static Func<object> GetStaticFieldGetter(FieldInfo field)
            {
                if (field == null)
                {
                    throw new ArgumentNullException(nameof(field));
                }
                if (!field.IsStatic)
                {
                    throw new ArgumentException("Field must be static", nameof(field));
                }

                /*
                    Static field access: prefer Dynamic IL over Expressions because
                    Expression.Compile() has inherent closure overhead for static field access
                    that makes it slower than direct IL emission or even raw reflection.
                */
                if (
                    TryGetOrCreateStaticFieldGetter(
                        field,
                        ReflectionDelegateStrategy.DynamicIl,
                        out Func<object> getter
                    )
                )
                {
                    return getter;
                }

                if (
                    TryGetOrCreateStaticFieldGetter(
                        field,
                        ReflectionDelegateStrategy.Expressions,
                        out getter
                    )
                )
                {
                    return getter;
                }

                return GetOrCreateReflectionStaticFieldGetter(field);
            }

            public static bool IsStaticFieldGetterCached(FieldInfo field)
            {
                if (field == null)
                {
                    return false;
                }

                CapabilityKey<FieldInfo> expressionsKey = new(
                    field,
                    ReflectionDelegateStrategy.Expressions
                );
                CapabilityKey<FieldInfo> dynamicIlKey = new(
                    field,
                    ReflectionDelegateStrategy.DynamicIl
                );
                CapabilityKey<FieldInfo> reflectionKey = new(
                    field,
                    ReflectionDelegateStrategy.Reflection
                );

                return StaticFieldGetters.ContainsKey(expressionsKey)
                    || StaticFieldGetters.ContainsKey(dynamicIlKey)
                    || StaticFieldGetters.ContainsKey(reflectionKey);
            }

            public static Action<object, object> GetFieldSetter(FieldInfo field)
            {
                if (field == null)
                {
                    throw new ArgumentNullException(nameof(field));
                }

                /*
                    Prefer Dynamic IL over Expressions because Expression.Compile()
                    creates closure-based delegates that are slower than direct IL emission.
                */
                if (
                    TryGetOrCreateFieldSetter(
                        field,
                        ReflectionDelegateStrategy.DynamicIl,
                        out Action<object, object> setter
                    )
                )
                {
                    return setter;
                }

                if (
                    TryGetOrCreateFieldSetter(
                        field,
                        ReflectionDelegateStrategy.Expressions,
                        out setter
                    )
                )
                {
                    return setter;
                }

                return GetOrCreateReflectionFieldSetter(field);
            }

            public static bool IsFieldSetterCached(FieldInfo field)
            {
                if (field == null)
                {
                    return false;
                }

                CapabilityKey<FieldInfo> expressionsKey = new(
                    field,
                    ReflectionDelegateStrategy.Expressions
                );
                CapabilityKey<FieldInfo> dynamicIlKey = new(
                    field,
                    ReflectionDelegateStrategy.DynamicIl
                );
                CapabilityKey<FieldInfo> reflectionKey = new(
                    field,
                    ReflectionDelegateStrategy.Reflection
                );

                return FieldSetters.ContainsKey(expressionsKey)
                    || FieldSetters.ContainsKey(dynamicIlKey)
                    || FieldSetters.ContainsKey(reflectionKey);
            }

            public static Action<object> GetStaticFieldSetter(FieldInfo field)
            {
                if (field == null)
                {
                    throw new ArgumentNullException(nameof(field));
                }
                if (!field.IsStatic)
                {
                    throw new ArgumentException("Field must be static", nameof(field));
                }

                /*
                    Static field access: prefer Dynamic IL over Expressions because
                    Expression.Compile() has inherent closure overhead for static field access.
                */
                if (
                    TryGetOrCreateStaticFieldSetter(
                        field,
                        ReflectionDelegateStrategy.DynamicIl,
                        out Action<object> setter
                    )
                )
                {
                    return setter;
                }

                if (
                    TryGetOrCreateStaticFieldSetter(
                        field,
                        ReflectionDelegateStrategy.Expressions,
                        out setter
                    )
                )
                {
                    return setter;
                }

                return GetOrCreateReflectionStaticFieldSetter(field);
            }

            public static bool IsStaticFieldSetterCached(FieldInfo field)
            {
                if (field == null)
                {
                    return false;
                }

                CapabilityKey<FieldInfo> expressionsKey = new(
                    field,
                    ReflectionDelegateStrategy.Expressions
                );
                CapabilityKey<FieldInfo> dynamicIlKey = new(
                    field,
                    ReflectionDelegateStrategy.DynamicIl
                );
                CapabilityKey<FieldInfo> reflectionKey = new(
                    field,
                    ReflectionDelegateStrategy.Reflection
                );

                return StaticFieldSetters.ContainsKey(expressionsKey)
                    || StaticFieldSetters.ContainsKey(dynamicIlKey)
                    || StaticFieldSetters.ContainsKey(reflectionKey);
            }

            public static Func<object, object> GetPropertyGetter(PropertyInfo property)
            {
                if (property == null)
                {
                    throw new ArgumentNullException(nameof(property));
                }

                if (
                    TryGetOrCreatePropertyGetter(
                        property,
                        ReflectionDelegateStrategy.Expressions,
                        out Func<object, object> getter
                    )
                )
                {
                    return getter;
                }

                if (
                    TryGetOrCreatePropertyGetter(
                        property,
                        ReflectionDelegateStrategy.DynamicIl,
                        out getter
                    )
                )
                {
                    return getter;
                }

                return GetOrCreateReflectionPropertyGetter(property);
            }

            public static Func<object, object> GetStaticPropertyGetter(PropertyInfo property)
            {
                return GetPropertyGetter(property);
            }

            public static Action<object, object> GetPropertySetter(PropertyInfo property)
            {
                if (property == null)
                {
                    throw new ArgumentNullException(nameof(property));
                }

                if (
                    TryGetOrCreatePropertySetter(
                        property,
                        ReflectionDelegateStrategy.Expressions,
                        out Action<object, object> setter
                    )
                )
                {
                    return setter;
                }

                if (
                    TryGetOrCreatePropertySetter(
                        property,
                        ReflectionDelegateStrategy.DynamicIl,
                        out setter
                    )
                )
                {
                    return setter;
                }

                return GetOrCreateReflectionPropertySetter(property);
            }

            public static Func<object, object[], object> GetMethodInvoker(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
                if (method.IsStatic)
                {
                    throw new ArgumentException(
                        "Method must be an instance method",
                        nameof(method)
                    );
                }

                if (
                    TryGetOrCreateMethodInvoker(
                        method,
                        ReflectionDelegateStrategy.Expressions,
                        out Func<object, object[], object> invoker
                    )
                )
                {
                    return invoker;
                }

                if (
                    TryGetOrCreateMethodInvoker(
                        method,
                        ReflectionDelegateStrategy.DynamicIl,
                        out invoker
                    )
                )
                {
                    return invoker;
                }

                return GetOrCreateReflectionMethodInvoker(method);
            }

            public static Func<object[], object> GetStaticMethodInvoker(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
                if (!method.IsStatic)
                {
                    throw new ArgumentException("Method must be static", nameof(method));
                }

                if (
                    TryGetOrCreateStaticMethodInvoker(
                        method,
                        ReflectionDelegateStrategy.Expressions,
                        out Func<object[], object> invoker
                    )
                )
                {
                    return invoker;
                }

                if (
                    TryGetOrCreateStaticMethodInvoker(
                        method,
                        ReflectionDelegateStrategy.DynamicIl,
                        out invoker
                    )
                )
                {
                    return invoker;
                }

                return GetOrCreateReflectionStaticMethodInvoker(method);
            }

            public static Func<object[], object> GetConstructorInvoker(ConstructorInfo ctor)
            {
                if (ctor == null)
                {
                    throw new ArgumentNullException(nameof(ctor));
                }

                if (
                    TryGetOrCreateConstructorInvoker(
                        ctor,
                        ReflectionDelegateStrategy.Expressions,
                        out Func<object[], object> invoker
                    )
                )
                {
                    return invoker;
                }

                if (
                    TryGetOrCreateConstructorInvoker(
                        ctor,
                        ReflectionDelegateStrategy.DynamicIl,
                        out invoker
                    )
                )
                {
                    return invoker;
                }

                return GetOrCreateReflectionConstructorInvoker(ctor);
            }

            public static Func<object> GetParameterlessConstructor(ConstructorInfo ctor)
            {
                if (ctor == null)
                {
                    throw new ArgumentNullException(nameof(ctor));
                }

                if (
                    TryGetOrCreateParameterlessConstructor(
                        ctor,
                        ReflectionDelegateStrategy.Expressions,
                        out Func<object> creator
                    )
                )
                {
                    return creator;
                }

                if (
                    TryGetOrCreateParameterlessConstructor(
                        ctor,
                        ReflectionDelegateStrategy.DynamicIl,
                        out creator
                    )
                )
                {
                    return creator;
                }

                return GetOrCreateReflectionParameterlessConstructor(ctor);
            }

            public static Func<T> GetParameterlessConstructorTyped<T>(ConstructorInfo ctor)
            {
                if (ctor == null)
                {
                    throw new ArgumentNullException(nameof(ctor));
                }

                if (
                    TryGetOrCreateTypedParameterlessConstructor(
                        ctor,
                        typeof(T),
                        ReflectionDelegateStrategy.Expressions,
                        out Func<T> creator
                    )
                )
                {
                    return creator;
                }

                if (
                    TryGetOrCreateTypedParameterlessConstructor(
                        ctor,
                        typeof(T),
                        ReflectionDelegateStrategy.DynamicIl,
                        out creator
                    )
                )
                {
                    return creator;
                }

                return GetOrCreateReflectionTypedParameterlessConstructor<T>(ctor);
            }

            public static Func<object, object[], object> GetIndexerGetter(PropertyInfo property)
            {
                if (property == null)
                {
                    throw new ArgumentNullException(nameof(property));
                }

                if (
                    TryGetOrCreateIndexerGetter(
                        property,
                        ReflectionDelegateStrategy.Expressions,
                        out Func<object, object[], object> getter
                    )
                )
                {
                    return getter;
                }

                if (
                    TryGetOrCreateIndexerGetter(
                        property,
                        ReflectionDelegateStrategy.DynamicIl,
                        out getter
                    )
                )
                {
                    return getter;
                }

                return GetOrCreateReflectionIndexerGetter(property);
            }

            public static Action<object, object, object[]> GetIndexerSetter(PropertyInfo property)
            {
                if (property == null)
                {
                    throw new ArgumentNullException(nameof(property));
                }

                if (
                    TryGetOrCreateIndexerSetter(
                        property,
                        ReflectionDelegateStrategy.Expressions,
                        out Action<object, object, object[]> setter
                    )
                )
                {
                    return setter;
                }

                if (
                    TryGetOrCreateIndexerSetter(
                        property,
                        ReflectionDelegateStrategy.DynamicIl,
                        out setter
                    )
                )
                {
                    return setter;
                }

                return GetOrCreateReflectionIndexerSetter(property);
            }

            public static Func<TInstance, TValue> GetFieldGetterTyped<TInstance, TValue>(
                FieldInfo field
            )
            {
                if (field == null)
                {
                    throw new ArgumentNullException(nameof(field));
                }

                /*
                    Prefer Dynamic IL over Expressions because Expression.Compile()
                    creates closure-based delegates that are slower than direct IL emission.
                */
                if (
                    TryGetOrCreateTypedFieldGetter<TInstance, TValue>(
                        field,
                        ReflectionDelegateStrategy.DynamicIl,
                        out Func<TInstance, TValue> getter
                    )
                )
                {
                    return getter;
                }

                if (
                    TryGetOrCreateTypedFieldGetter<TInstance, TValue>(
                        field,
                        ReflectionDelegateStrategy.Expressions,
                        out getter
                    )
                )
                {
                    return getter;
                }

                return GetOrCreateReflectionTypedFieldGetter<TInstance, TValue>(field);
            }

            public static FieldSetter<TInstance, TValue> GetFieldSetterTyped<TInstance, TValue>(
                FieldInfo field
            )
            {
                if (field == null)
                {
                    throw new ArgumentNullException(nameof(field));
                }

                /*
                    Prefer Dynamic IL over Expressions because Expression.Compile()
                    creates closure-based delegates that are slower than direct IL emission.
                */
                if (
                    TryGetOrCreateTypedFieldSetter<TInstance, TValue>(
                        field,
                        ReflectionDelegateStrategy.DynamicIl,
                        out FieldSetter<TInstance, TValue> setter
                    )
                )
                {
                    return setter;
                }

                if (
                    TryGetOrCreateTypedFieldSetter<TInstance, TValue>(
                        field,
                        ReflectionDelegateStrategy.Expressions,
                        out setter
                    )
                )
                {
                    return setter;
                }

                return GetOrCreateReflectionTypedFieldSetter<TInstance, TValue>(field);
            }

            public static Func<TValue> GetStaticFieldGetterTyped<TValue>(FieldInfo field)
            {
                if (field == null)
                {
                    throw new ArgumentNullException(nameof(field));
                }
                if (!field.IsStatic)
                {
                    throw new ArgumentException("Field must be static", nameof(field));
                }

                /*
                    Static field access: prefer Dynamic IL over Expressions because
                    Expression.Compile() has inherent closure overhead for static field access.
                */
                if (
                    TryGetOrCreateTypedStaticFieldGetter<TValue>(
                        field,
                        ReflectionDelegateStrategy.DynamicIl,
                        out Func<TValue> getter
                    )
                )
                {
                    return getter;
                }

                if (
                    TryGetOrCreateTypedStaticFieldGetter<TValue>(
                        field,
                        ReflectionDelegateStrategy.Expressions,
                        out getter
                    )
                )
                {
                    return getter;
                }

                return GetOrCreateReflectionTypedStaticFieldGetter<TValue>(field);
            }

            public static Action<TValue> GetStaticFieldSetterTyped<TValue>(FieldInfo field)
            {
                if (field == null)
                {
                    throw new ArgumentNullException(nameof(field));
                }
                if (!field.IsStatic)
                {
                    throw new ArgumentException("Field must be static", nameof(field));
                }

                /*
                    Static field access: prefer Dynamic IL over Expressions because
                    Expression.Compile() has inherent closure overhead for static field access.
                */
                if (
                    TryGetOrCreateTypedStaticFieldSetter<TValue>(
                        field,
                        ReflectionDelegateStrategy.DynamicIl,
                        out Action<TValue> setter
                    )
                )
                {
                    return setter;
                }

                if (
                    TryGetOrCreateTypedStaticFieldSetter<TValue>(
                        field,
                        ReflectionDelegateStrategy.Expressions,
                        out setter
                    )
                )
                {
                    return setter;
                }

                return GetOrCreateReflectionTypedStaticFieldSetter<TValue>(field);
            }

            public static Func<TInstance, TValue> GetPropertyGetterTyped<TInstance, TValue>(
                PropertyInfo property
            )
            {
                if (property == null)
                {
                    throw new ArgumentNullException(nameof(property));
                }
                (PropertyInfo property, Type, Type) key = (
                    property,
                    typeof(TInstance),
                    typeof(TValue)
                );
#if !SINGLE_THREADED
                return (Func<TInstance, TValue>)
                    TypedPropertyGetters.GetOrAdd(
                        key,
                        static cacheKey =>
                            BuildTypedPropertyGetter<TInstance, TValue>(cacheKey.property)
                    );
#else
                if (!TypedPropertyGetters.TryGetValue(key, out Delegate del))
                {
                    del = BuildTypedPropertyGetter<TInstance, TValue>(property);
                    TypedPropertyGetters[key] = del;
                }
                return (Func<TInstance, TValue>)del;
#endif
            }

            public static Action<TInstance, TValue> GetPropertySetterTyped<TInstance, TValue>(
                PropertyInfo property
            )
            {
                if (property == null)
                {
                    throw new ArgumentNullException(nameof(property));
                }
                (PropertyInfo property, Type, Type) key = (
                    property,
                    typeof(TInstance),
                    typeof(TValue)
                );
#if !SINGLE_THREADED
                return (Action<TInstance, TValue>)
                    TypedPropertySetters.GetOrAdd(
                        key,
                        static cacheKey =>
                            BuildTypedPropertySetter<TInstance, TValue>(cacheKey.property)
                    );
#else
                if (!TypedPropertySetters.TryGetValue(key, out Delegate del))
                {
                    del = BuildTypedPropertySetter<TInstance, TValue>(property);
                    TypedPropertySetters[key] = del;
                }
                return (Action<TInstance, TValue>)del;
#endif
            }

            public static Func<TValue> GetStaticPropertyGetterTyped<TValue>(PropertyInfo property)
            {
                if (property == null)
                {
                    throw new ArgumentNullException(nameof(property));
                }
                (PropertyInfo property, Type) key = (property, typeof(TValue));
#if !SINGLE_THREADED
                return (Func<TValue>)
                    TypedStaticPropertyGetters.GetOrAdd(
                        key,
                        static cacheKey => BuildTypedStaticPropertyGetter<TValue>(cacheKey.property)
                    );
#else
                if (!TypedStaticPropertyGetters.TryGetValue(key, out Delegate del))
                {
                    del = BuildTypedStaticPropertyGetter<TValue>(property);
                    TypedStaticPropertyGetters[key] = del;
                }
                return (Func<TValue>)del;
#endif
            }

            public static Action<TValue> GetStaticPropertySetterTyped<TValue>(PropertyInfo property)
            {
                if (property == null)
                {
                    throw new ArgumentNullException(nameof(property));
                }
                (PropertyInfo property, Type) key = (property, typeof(TValue));
#if !SINGLE_THREADED
                return (Action<TValue>)
                    TypedStaticPropertySetters.GetOrAdd(
                        key,
                        static cacheKey => BuildTypedStaticPropertySetter<TValue>(cacheKey.property)
                    );
#else
                if (!TypedStaticPropertySetters.TryGetValue(key, out Delegate del))
                {
                    del = BuildTypedStaticPropertySetter<TValue>(property);
                    TypedStaticPropertySetters[key] = del;
                }
                return (Action<TValue>)del;
#endif
            }

            private static Func<object, object[], object> CreateIndexerGetter(
                PropertyInfo property,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledIndexerGetter(property);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return BuildIndexerGetterIL(property);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return CreateReflectionIndexerGetter(property);
                }

                return null;
            }

            private static Action<object, object, object[]> CreateIndexerSetter(
                PropertyInfo property,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledIndexerSetter(property);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return BuildIndexerSetterIL(property);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return CreateReflectionIndexerSetter(property);
                }

                return null;
            }

            private static Func<object, object[], object> CreateReflectionIndexerGetter(
                PropertyInfo property
            )
            {
                ParameterInfo[] indices = property.GetIndexParameters();
                int indexCount = indices.Length;
                return (instance, indexArgs) =>
                {
                    ValidateIndexArguments(indexArgs, indices, indexCount);
                    return property.GetValue(instance, indexArgs);
                };
            }

            private static Action<object, object, object[]> CreateReflectionIndexerSetter(
                PropertyInfo property
            )
            {
                ParameterInfo[] indices = property.GetIndexParameters();
                int indexCount = indices.Length;
                return (instance, value, indexArgs) =>
                {
                    ValidateIndexArguments(indexArgs, indices, indexCount);
                    property.SetValue(instance, value, indexArgs);
                };
            }

            private static void ValidateIndexArguments(
                object[] indexArgs,
                ParameterInfo[] indices,
                int indexCount
            )
            {
                if (indexArgs == null || indexArgs.Length != indexCount)
                {
                    throw new IndexOutOfRangeException(
                        $"Indexer expects {indexCount} index argument(s); received {(indexArgs == null ? 0 : indexArgs.Length)}."
                    );
                }

                for (int i = 0; i < indexCount; i++)
                {
                    object arg = indexArgs[i];
                    Type parameterType = indices[i].ParameterType;
                    if (arg == null)
                    {
                        if (
                            parameterType.IsValueType
                            && Nullable.GetUnderlyingType(parameterType) == null
                        )
                        {
                            throw new InvalidCastException(
                                $"Object of type 'null' cannot be converted to type '{parameterType}'."
                            );
                        }
                    }
                    else if (!parameterType.IsInstanceOfType(arg))
                    {
                        throw new InvalidCastException(
                            $"Object of type '{arg.GetType()}' cannot be converted to type '{parameterType}'."
                        );
                    }
                }
            }

            private static Func<TInstance, TValue> BuildTypedFieldGetter<TInstance, TValue>(
                FieldInfo field
            )
            {
                Func<TInstance, TValue> getter = null;
                if (SupportsExpressions)
                {
                    getter = CreateCompiledTypedFieldGetter<TInstance, TValue>(field);
                }
#if EMIT_DYNAMIC_IL
                if (
                    getter == null
                    && SupportsDynamicIl
                    && CanInlineReturnConversion(field.FieldType, typeof(TValue))
                )
                {
                    getter = BuildTypedFieldGetterIL<TInstance, TValue>(field);
                }
#endif
                if (getter != null)
                {
                    return getter;
                }

                if (field.IsStatic)
                {
                    return _ => (TValue)field.GetValue(null);
                }

                if (typeof(TInstance).IsValueType)
                {
                    return instance =>
                    {
                        object boxed = instance;
                        return (TValue)field.GetValue(boxed);
                    };
                }

                return instance => (TValue)field.GetValue(instance);
            }

            private static FieldSetter<TInstance, TValue> BuildTypedFieldSetter<TInstance, TValue>(
                FieldInfo field
            )
            {
                FieldSetter<TInstance, TValue> setter = null;
                if (SupportsExpressions)
                {
                    setter = CreateCompiledTypedFieldSetter<TInstance, TValue>(field);
                }
#if EMIT_DYNAMIC_IL
                if (
                    setter == null
                    && SupportsDynamicIl
                    && CanInlineAssignment(typeof(TValue), field.FieldType)
                    && (field.IsStatic || typeof(TInstance) == field.DeclaringType)
                )
                {
                    setter = BuildTypedFieldSetterIL<TInstance, TValue>(field);
                }
#endif
                if (setter != null)
                {
                    return setter;
                }

                if (field.IsStatic)
                {
                    return (ref TInstance _, TValue value) =>
                    {
                        field.SetValue(null, value);
                    };
                }

                if (typeof(TInstance).IsValueType)
                {
                    return (ref TInstance instance, TValue value) =>
                    {
                        object boxed = instance;
                        field.SetValue(boxed, value);
                        instance = (TInstance)boxed;
                    };
                }

                return (ref TInstance instance, TValue value) =>
                {
                    field.SetValue(instance, value);
                };
            }

            private static Func<TValue> BuildTypedStaticFieldGetter<TValue>(FieldInfo field)
            {
                Func<TValue> getter = null;
                if (SupportsExpressions)
                {
                    getter = CreateCompiledTypedStaticFieldGetter<TValue>(field);
                }
#if EMIT_DYNAMIC_IL
                if (
                    getter == null
                    && SupportsDynamicIl
                    && CanInlineReturnConversion(field.FieldType, typeof(TValue))
                )
                {
                    getter = BuildTypedStaticFieldGetterIL<TValue>(field);
                }
#endif
                return getter ?? (() => (TValue)field.GetValue(null));
            }

            private static Action<TValue> BuildTypedStaticFieldSetter<TValue>(FieldInfo field)
            {
                Action<TValue> setter = null;
                if (SupportsExpressions)
                {
                    setter = CreateCompiledTypedStaticFieldSetter<TValue>(field);
                }
#if EMIT_DYNAMIC_IL
                if (
                    setter == null
                    && SupportsDynamicIl
                    && CanInlineAssignment(typeof(TValue), field.FieldType)
                )
                {
                    setter = BuildTypedStaticFieldSetterIL<TValue>(field);
                }
#endif
                return setter ?? (value => field.SetValue(null, value));
            }

            private static bool TryGetOrCreateFieldGetter(
                FieldInfo field,
                ReflectionDelegateStrategy strategy,
                out Func<object, object> getter
            )
            {
                getter = null;

                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return false;
                }
#endif

                CapabilityKey<FieldInfo> key = new(field, strategy);
                if (TryGetFieldGetterFromCache(key, out Func<object, object> cached))
                {
                    getter = cached;
                    return true;
                }

                if (IsFieldGetterStrategyUnavailable(key))
                {
                    return false;
                }

                Func<object, object> candidate = CreateFieldGetter(field, strategy);
                if (candidate == null)
                {
                    MarkFieldGetterStrategyUnavailable(key);
                    return false;
                }

                Func<object, object> resolved = AddOrGetFieldGetter(key, candidate);
                TrackDelegateStrategy(resolved, key);
                getter = resolved;
                return true;
            }

            private static Func<object, object> GetOrCreateReflectionFieldGetter(FieldInfo field)
            {
                CapabilityKey<FieldInfo> key = new(field, ReflectionDelegateStrategy.Reflection);
                if (TryGetFieldGetterFromCache(key, out Func<object, object> cached))
                {
                    return cached;
                }

                Func<object, object> reflectionGetter = CreateReflectionFieldGetter(field);
                Func<object, object> resolved = AddOrGetFieldGetter(key, reflectionGetter);
                TrackDelegateStrategy(resolved, key);
                return resolved;
            }

            private static Func<object, object> CreateFieldGetter(
                FieldInfo field,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledFieldGetter(field);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return BuildFieldGetterIL(field);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return CreateReflectionFieldGetter(field);
                }

                return null;
            }

            private static Func<object, object> CreateReflectionFieldGetter(FieldInfo field)
            {
                if (field.IsStatic)
                {
                    return ignoredInstance => field.GetValue(null);
                }

                return instance => field.GetValue(instance);
            }

            private static bool TryGetOrCreateFieldSetter(
                FieldInfo field,
                ReflectionDelegateStrategy strategy,
                out Action<object, object> setter
            )
            {
                setter = null;

                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return false;
                }
#endif

                CapabilityKey<FieldInfo> key = new(field, strategy);
                if (TryGetFieldSetterFromCache(key, out Action<object, object> cached))
                {
                    setter = cached;
                    return true;
                }

                if (IsFieldSetterStrategyUnavailable(key))
                {
                    return false;
                }

                Action<object, object> candidate = CreateFieldSetter(field, strategy);
                if (candidate == null)
                {
                    MarkFieldSetterStrategyUnavailable(key);
                    return false;
                }

                Action<object, object> resolved = AddOrGetFieldSetter(key, candidate);
                TrackDelegateStrategy(resolved, key);
                setter = resolved;
                return true;
            }

            private static Action<object, object> GetOrCreateReflectionFieldSetter(FieldInfo field)
            {
                CapabilityKey<FieldInfo> key = new(field, ReflectionDelegateStrategy.Reflection);
                if (TryGetFieldSetterFromCache(key, out Action<object, object> cached))
                {
                    return cached;
                }

                Action<object, object> reflectionSetter = CreateReflectionFieldSetter(field);
                Action<object, object> resolved = AddOrGetFieldSetter(key, reflectionSetter);
                TrackDelegateStrategy(resolved, key);
                return resolved;
            }

            private static Action<object, object> CreateFieldSetter(
                FieldInfo field,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledFieldSetter(field);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return BuildFieldSetterIL(field);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return CreateReflectionFieldSetter(field);
                }

                return null;
            }

            private static Action<object, object> CreateReflectionFieldSetter(FieldInfo field)
            {
                if (field.IsStatic)
                {
                    return (_, value) => field.SetValue(null, value);
                }

                return (instance, value) => field.SetValue(instance, value);
            }

            private static bool TryGetOrCreateStaticFieldGetter(
                FieldInfo field,
                ReflectionDelegateStrategy strategy,
                out Func<object> getter
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    getter = null;
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    getter = null;
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    getter = null;
                    return false;
                }
#endif

                CapabilityKey<FieldInfo> key = new(field, strategy);
                if (TryGetStaticFieldGetterFromCache(key, out Func<object> cached))
                {
                    getter = cached;
                    return true;
                }

                if (IsStaticFieldGetterStrategyUnavailable(key))
                {
                    getter = null;
                    return false;
                }

                Func<object> candidate = CreateStaticFieldGetter(field, strategy);
                if (candidate == null)
                {
                    MarkStaticFieldGetterStrategyUnavailable(key);
                    getter = null;
                    return false;
                }

                Func<object> resolved = AddOrGetStaticFieldGetter(key, candidate);
                TrackDelegateStrategy(resolved, key);
                getter = resolved;
                return true;
            }

            private static Func<object> GetOrCreateReflectionStaticFieldGetter(FieldInfo field)
            {
                CapabilityKey<FieldInfo> key = new(field, ReflectionDelegateStrategy.Reflection);
                if (TryGetStaticFieldGetterFromCache(key, out Func<object> cached))
                {
                    return cached;
                }

                Func<object> reflectionGetter = CreateReflectionStaticFieldGetter(field);
                Func<object> resolved = AddOrGetStaticFieldGetter(key, reflectionGetter);
                TrackDelegateStrategy(resolved, key);
                return resolved;
            }

            private static Func<object> CreateStaticFieldGetter(
                FieldInfo field,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledStaticFieldGetter(field);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return BuildStaticFieldGetterIL(field);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return CreateReflectionStaticFieldGetter(field);
                }

                return null;
            }

            private static Func<object> CreateReflectionStaticFieldGetter(FieldInfo field)
            {
                return () => field.GetValue(null);
            }

            private static bool TryGetOrCreateStaticFieldSetter(
                FieldInfo field,
                ReflectionDelegateStrategy strategy,
                out Action<object> setter
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    setter = null;
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    setter = null;
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    setter = null;
                    return false;
                }
#endif

                CapabilityKey<FieldInfo> key = new(field, strategy);
                if (TryGetStaticFieldSetterFromCache(key, out Action<object> cached))
                {
                    setter = cached;
                    return true;
                }

                if (IsStaticFieldSetterStrategyUnavailable(key))
                {
                    setter = null;
                    return false;
                }

                Action<object> candidate = CreateStaticFieldSetter(field, strategy);
                if (candidate == null)
                {
                    MarkStaticFieldSetterStrategyUnavailable(key);
                    setter = null;
                    return false;
                }

                Action<object> resolved = AddOrGetStaticFieldSetter(key, candidate);
                TrackDelegateStrategy(resolved, key);
                setter = resolved;
                return true;
            }

            private static Action<object> GetOrCreateReflectionStaticFieldSetter(FieldInfo field)
            {
                CapabilityKey<FieldInfo> key = new(field, ReflectionDelegateStrategy.Reflection);
                if (TryGetStaticFieldSetterFromCache(key, out Action<object> cached))
                {
                    return cached;
                }

                Action<object> reflectionSetter = CreateReflectionStaticFieldSetter(field);
                Action<object> resolved = AddOrGetStaticFieldSetter(key, reflectionSetter);
                TrackDelegateStrategy(resolved, key);
                return resolved;
            }

            private static Action<object> CreateStaticFieldSetter(
                FieldInfo field,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledStaticFieldSetter(field);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return BuildStaticFieldSetterIL(field);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return CreateReflectionStaticFieldSetter(field);
                }

                return null;
            }

            private static Action<object> CreateReflectionStaticFieldSetter(FieldInfo field)
            {
                return value => field.SetValue(null, value);
            }

            private static bool TryGetOrCreateTypedFieldGetter<TInstance, TValue>(
                FieldInfo field,
                ReflectionDelegateStrategy strategy,
                out Func<TInstance, TValue> getter
            )
            {
                getter = null;

                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return false;
                }
#endif

                CapabilityKey<(FieldInfo field, Type instance, Type value)> key = new(
                    (field, typeof(TInstance), typeof(TValue)),
                    strategy
                );
                if (TryGetTypedFieldGetterFromCache(key, out Delegate cached))
                {
                    getter = (Func<TInstance, TValue>)cached;
                    return true;
                }

                if (IsTypedFieldGetterStrategyUnavailable(key))
                {
                    return false;
                }

                Func<TInstance, TValue> candidate = CreateTypedFieldGetter<TInstance, TValue>(
                    field,
                    strategy
                );
                if (candidate == null)
                {
                    MarkTypedFieldGetterStrategyUnavailable(key);
                    return false;
                }

                Delegate resolved = AddOrGetTypedFieldGetter(key, candidate);
                TrackDelegateStrategy(resolved, key);
                getter = (Func<TInstance, TValue>)resolved;
                return true;
            }

            private static Func<TInstance, TValue> GetOrCreateReflectionTypedFieldGetter<
                TInstance,
                TValue
            >(FieldInfo field)
            {
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key = new(
                    (field, typeof(TInstance), typeof(TValue)),
                    ReflectionDelegateStrategy.Reflection
                );
                if (TryGetTypedFieldGetterFromCache(key, out Delegate cached))
                {
                    return (Func<TInstance, TValue>)cached;
                }

                Func<TInstance, TValue> reflectionGetter = CreateReflectionTypedFieldGetter<
                    TInstance,
                    TValue
                >(field);
                Delegate resolved = AddOrGetTypedFieldGetter(key, reflectionGetter);
                TrackDelegateStrategy(resolved, key);
                return (Func<TInstance, TValue>)resolved;
            }

            private static Func<TInstance, TValue> CreateTypedFieldGetter<TInstance, TValue>(
                FieldInfo field,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledTypedFieldGetter<TInstance, TValue>(field);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    if (!CanInlineReturnConversion(field.FieldType, typeof(TValue)))
                    {
                        return null;
                    }

                    return BuildTypedFieldGetterIL<TInstance, TValue>(field);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return CreateReflectionTypedFieldGetter<TInstance, TValue>(field);
                }

                return null;
            }

            private static Func<TInstance, TValue> CreateReflectionTypedFieldGetter<
                TInstance,
                TValue
            >(FieldInfo field)
            {
                if (field.IsStatic)
                {
                    return _ => (TValue)field.GetValue(null);
                }

                if (typeof(TInstance).IsValueType)
                {
                    return instance =>
                    {
                        object boxed = instance;
                        return (TValue)field.GetValue(boxed);
                    };
                }

                return instance => (TValue)field.GetValue(instance);
            }

            private static bool TryGetOrCreateTypedFieldSetter<TInstance, TValue>(
                FieldInfo field,
                ReflectionDelegateStrategy strategy,
                out FieldSetter<TInstance, TValue> setter
            )
            {
                setter = null;

                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return false;
                }
#endif

                CapabilityKey<(FieldInfo field, Type instance, Type value)> key = new(
                    (field, typeof(TInstance), typeof(TValue)),
                    strategy
                );
                if (TryGetTypedFieldSetterFromCache(key, out Delegate cached))
                {
                    setter = (FieldSetter<TInstance, TValue>)cached;
                    return true;
                }

                if (IsTypedFieldSetterStrategyUnavailable(key))
                {
                    return false;
                }

                FieldSetter<TInstance, TValue> candidate = CreateTypedFieldSetter<
                    TInstance,
                    TValue
                >(field, strategy);
                if (candidate == null)
                {
                    MarkTypedFieldSetterStrategyUnavailable(key);
                    return false;
                }

                Delegate resolved = AddOrGetTypedFieldSetter(key, candidate);
                TrackDelegateStrategy(resolved, key);
                setter = (FieldSetter<TInstance, TValue>)resolved;
                return true;
            }

            private static FieldSetter<TInstance, TValue> GetOrCreateReflectionTypedFieldSetter<
                TInstance,
                TValue
            >(FieldInfo field)
            {
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key = new(
                    (field, typeof(TInstance), typeof(TValue)),
                    ReflectionDelegateStrategy.Reflection
                );
                if (TryGetTypedFieldSetterFromCache(key, out Delegate cached))
                {
                    return (FieldSetter<TInstance, TValue>)cached;
                }

                FieldSetter<TInstance, TValue> reflectionSetter = CreateReflectionTypedFieldSetter<
                    TInstance,
                    TValue
                >(field);
                Delegate resolved = AddOrGetTypedFieldSetter(key, reflectionSetter);
                TrackDelegateStrategy(resolved, key);
                return (FieldSetter<TInstance, TValue>)resolved;
            }

            private static FieldSetter<TInstance, TValue> CreateTypedFieldSetter<TInstance, TValue>(
                FieldInfo field,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledTypedFieldSetter<TInstance, TValue>(field);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    if (
                        !CanInlineAssignment(typeof(TValue), field.FieldType)
                        || (!field.IsStatic && typeof(TInstance) != field.DeclaringType)
                    )
                    {
                        return null;
                    }

                    return BuildTypedFieldSetterIL<TInstance, TValue>(field);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return CreateReflectionTypedFieldSetter<TInstance, TValue>(field);
                }

                return null;
            }

            private static FieldSetter<TInstance, TValue> CreateReflectionTypedFieldSetter<
                TInstance,
                TValue
            >(FieldInfo field)
            {
                if (field.IsStatic)
                {
                    return (ref TInstance _, TValue value) =>
                    {
                        field.SetValue(null, value);
                    };
                }

                if (typeof(TInstance).IsValueType)
                {
                    return (ref TInstance instance, TValue value) =>
                    {
                        object boxed = instance;
                        field.SetValue(boxed, value);
                        instance = (TInstance)boxed;
                    };
                }

                return (ref TInstance instance, TValue value) =>
                {
                    field.SetValue(instance, value);
                };
            }

            private static bool TryGetOrCreateTypedStaticFieldGetter<TValue>(
                FieldInfo field,
                ReflectionDelegateStrategy strategy,
                out Func<TValue> getter
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    getter = null;
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    getter = null;
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    getter = null;
                    return false;
                }
#endif

                CapabilityKey<(FieldInfo field, Type value)> key = new(
                    (field, typeof(TValue)),
                    strategy
                );
                if (TryGetTypedStaticFieldGetterFromCache(key, out Delegate cached))
                {
                    getter = (Func<TValue>)cached;
                    return true;
                }

                if (IsTypedStaticFieldGetterStrategyUnavailable(key))
                {
                    getter = null;
                    return false;
                }

                Func<TValue> candidate = CreateTypedStaticFieldGetter<TValue>(field, strategy);
                if (candidate == null)
                {
                    MarkTypedStaticFieldGetterStrategyUnavailable(key);
                    getter = null;
                    return false;
                }

                Delegate resolved = AddOrGetTypedStaticFieldGetter(key, candidate);
                TrackDelegateStrategy(resolved, key);
                getter = (Func<TValue>)resolved;
                return true;
            }

            private static Func<TValue> GetOrCreateReflectionTypedStaticFieldGetter<TValue>(
                FieldInfo field
            )
            {
                CapabilityKey<(FieldInfo field, Type value)> key = new(
                    (field, typeof(TValue)),
                    ReflectionDelegateStrategy.Reflection
                );
                if (TryGetTypedStaticFieldGetterFromCache(key, out Delegate cached))
                {
                    return (Func<TValue>)cached;
                }

                Func<TValue> reflectionGetter = CreateReflectionTypedStaticFieldGetter<TValue>(
                    field
                );
                Delegate resolved = AddOrGetTypedStaticFieldGetter(key, reflectionGetter);
                TrackDelegateStrategy(resolved, key);
                return (Func<TValue>)resolved;
            }

            private static Func<TValue> CreateTypedStaticFieldGetter<TValue>(
                FieldInfo field,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledTypedStaticFieldGetter<TValue>(field);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    if (!CanInlineReturnConversion(field.FieldType, typeof(TValue)))
                    {
                        return null;
                    }

                    return BuildTypedStaticFieldGetterIL<TValue>(field);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return CreateReflectionTypedStaticFieldGetter<TValue>(field);
                }

                return null;
            }

            private static Func<TValue> CreateReflectionTypedStaticFieldGetter<TValue>(
                FieldInfo field
            )
            {
                return () => (TValue)field.GetValue(null);
            }

            private static bool TryGetOrCreateTypedStaticFieldSetter<TValue>(
                FieldInfo field,
                ReflectionDelegateStrategy strategy,
                out Action<TValue> setter
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    setter = null;
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    setter = null;
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    setter = null;
                    return false;
                }
#endif

                CapabilityKey<(FieldInfo field, Type value)> key = new(
                    (field, typeof(TValue)),
                    strategy
                );
                if (TryGetTypedStaticFieldSetterFromCache(key, out Delegate cached))
                {
                    setter = (Action<TValue>)cached;
                    return true;
                }

                if (IsTypedStaticFieldSetterStrategyUnavailable(key))
                {
                    setter = null;
                    return false;
                }

                Action<TValue> candidate = CreateTypedStaticFieldSetter<TValue>(field, strategy);
                if (candidate == null)
                {
                    MarkTypedStaticFieldSetterStrategyUnavailable(key);
                    setter = null;
                    return false;
                }

                Delegate resolved = AddOrGetTypedStaticFieldSetter(key, candidate);
                TrackDelegateStrategy(resolved, key);
                setter = (Action<TValue>)resolved;
                return true;
            }

            private static Action<TValue> GetOrCreateReflectionTypedStaticFieldSetter<TValue>(
                FieldInfo field
            )
            {
                CapabilityKey<(FieldInfo field, Type value)> key = new(
                    (field, typeof(TValue)),
                    ReflectionDelegateStrategy.Reflection
                );
                if (TryGetTypedStaticFieldSetterFromCache(key, out Delegate cached))
                {
                    return (Action<TValue>)cached;
                }

                Action<TValue> reflectionSetter = CreateReflectionTypedStaticFieldSetter<TValue>(
                    field
                );
                Delegate resolved = AddOrGetTypedStaticFieldSetter(key, reflectionSetter);
                TrackDelegateStrategy(resolved, key);
                return (Action<TValue>)resolved;
            }

            private static Action<TValue> CreateTypedStaticFieldSetter<TValue>(
                FieldInfo field,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledTypedStaticFieldSetter<TValue>(field);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    if (!CanInlineAssignment(typeof(TValue), field.FieldType))
                    {
                        return null;
                    }

                    return BuildTypedStaticFieldSetterIL<TValue>(field);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return CreateReflectionTypedStaticFieldSetter<TValue>(field);
                }

                return null;
            }

            private static Action<TValue> CreateReflectionTypedStaticFieldSetter<TValue>(
                FieldInfo field
            )
            {
                return value => field.SetValue(null, value);
            }

            private static bool TryGetOrCreatePropertyGetter(
                PropertyInfo property,
                ReflectionDelegateStrategy strategy,
                out Func<object, object> getter
            )
            {
                getter = null;

                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return false;
                }
#endif

                CapabilityKey<PropertyInfo> key = new(property, strategy);
                if (TryGetPropertyGetterFromCache(key, out Func<object, object> cached))
                {
                    getter = cached;
                    return true;
                }

                if (IsPropertyGetterStrategyUnavailable(key))
                {
                    return false;
                }

                Func<object, object> candidate = CreatePropertyGetter(property, strategy);
                if (candidate == null)
                {
                    MarkPropertyGetterStrategyUnavailable(key);
                    return false;
                }

                Func<object, object> resolved = AddOrGetPropertyGetter(key, candidate);
                TrackDelegateStrategy(resolved, key);
                getter = resolved;
                return true;
            }

            private static Func<object, object> GetOrCreateReflectionPropertyGetter(
                PropertyInfo property
            )
            {
                CapabilityKey<PropertyInfo> key = new(
                    property,
                    ReflectionDelegateStrategy.Reflection
                );
                if (TryGetPropertyGetterFromCache(key, out Func<object, object> cached))
                {
                    return cached;
                }

                Func<object, object> reflectionGetter = CreateReflectionPropertyGetter(property);
                Func<object, object> resolved = AddOrGetPropertyGetter(key, reflectionGetter);
                TrackDelegateStrategy(resolved, key);
                return resolved;
            }

            private static bool TryGetOrCreatePropertySetter(
                PropertyInfo property,
                ReflectionDelegateStrategy strategy,
                out Action<object, object> setter
            )
            {
                setter = null;

                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return false;
                }
#endif

                CapabilityKey<PropertyInfo> key = new(property, strategy);
                if (TryGetPropertySetterFromCache(key, out Action<object, object> cached))
                {
                    setter = cached;
                    return true;
                }

                if (IsPropertySetterStrategyUnavailable(key))
                {
                    return false;
                }

                Action<object, object> candidate = CreatePropertySetter(property, strategy);
                if (candidate == null)
                {
                    MarkPropertySetterStrategyUnavailable(key);
                    return false;
                }

                Action<object, object> resolved = AddOrGetPropertySetter(key, candidate);
                TrackDelegateStrategy(resolved, key);
                setter = resolved;
                return true;
            }

            private static bool TryGetOrCreateIndexerGetter(
                PropertyInfo property,
                ReflectionDelegateStrategy strategy,
                out Func<object, object[], object> getter
            )
            {
                getter = null;

                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return false;
                }
#endif

                CapabilityKey<PropertyInfo> key = new(property, strategy);
                if (TryGetIndexerGetterFromCache(key, out Func<object, object[], object> cached))
                {
                    getter = cached;
                    return true;
                }

                if (IsIndexerGetterStrategyUnavailable(key))
                {
                    return false;
                }

                Func<object, object[], object> candidate = CreateIndexerGetter(property, strategy);
                if (candidate == null)
                {
                    MarkIndexerGetterStrategyUnavailable(key);
                    return false;
                }

                Func<object, object[], object> resolved = AddOrGetIndexerGetter(key, candidate);
                TrackDelegateStrategy(resolved, key);
                getter = resolved;
                return true;
            }

            private static Func<object, object[], object> GetOrCreateReflectionIndexerGetter(
                PropertyInfo property
            )
            {
                CapabilityKey<PropertyInfo> key = new(
                    property,
                    ReflectionDelegateStrategy.Reflection
                );
                if (TryGetIndexerGetterFromCache(key, out Func<object, object[], object> cached))
                {
                    return cached;
                }

                Func<object, object[], object> reflectionGetter = CreateReflectionIndexerGetter(
                    property
                );
                Func<object, object[], object> resolved = AddOrGetIndexerGetter(
                    key,
                    reflectionGetter
                );
                TrackDelegateStrategy(resolved, key);
                return resolved;
            }

            private static bool TryGetOrCreateIndexerSetter(
                PropertyInfo property,
                ReflectionDelegateStrategy strategy,
                out Action<object, object, object[]> setter
            )
            {
                setter = null;

                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return false;
                }
#endif

                CapabilityKey<PropertyInfo> key = new(property, strategy);
                if (TryGetIndexerSetterFromCache(key, out Action<object, object, object[]> cached))
                {
                    setter = cached;
                    return true;
                }

                if (IsIndexerSetterStrategyUnavailable(key))
                {
                    return false;
                }

                Action<object, object, object[]> candidate = CreateIndexerSetter(
                    property,
                    strategy
                );
                if (candidate == null)
                {
                    MarkIndexerSetterStrategyUnavailable(key);
                    return false;
                }

                Action<object, object, object[]> resolved = AddOrGetIndexerSetter(key, candidate);
                TrackDelegateStrategy(resolved, key);
                setter = resolved;
                return true;
            }

            private static Action<object, object, object[]> GetOrCreateReflectionIndexerSetter(
                PropertyInfo property
            )
            {
                CapabilityKey<PropertyInfo> key = new(
                    property,
                    ReflectionDelegateStrategy.Reflection
                );
                if (TryGetIndexerSetterFromCache(key, out Action<object, object, object[]> cached))
                {
                    return cached;
                }

                Action<object, object, object[]> reflectionSetter = CreateReflectionIndexerSetter(
                    property
                );
                Action<object, object, object[]> resolved = AddOrGetIndexerSetter(
                    key,
                    reflectionSetter
                );
                TrackDelegateStrategy(resolved, key);
                return resolved;
            }

            private static Action<object, object> GetOrCreateReflectionPropertySetter(
                PropertyInfo property
            )
            {
                CapabilityKey<PropertyInfo> key = new(
                    property,
                    ReflectionDelegateStrategy.Reflection
                );
                if (TryGetPropertySetterFromCache(key, out Action<object, object> cached))
                {
                    return cached;
                }

                Action<object, object> reflectionSetter = CreateReflectionPropertySetter(property);
                Action<object, object> resolved = AddOrGetPropertySetter(key, reflectionSetter);
                TrackDelegateStrategy(resolved, key);
                return resolved;
            }

            private static bool TryGetOrCreateConstructorInvoker(
                ConstructorInfo ctor,
                ReflectionDelegateStrategy strategy,
                out Func<object[], object> invoker
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    invoker = null;
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    invoker = null;
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    invoker = null;
                    return false;
                }
#endif

                CapabilityKey<ConstructorInfo> key = new(ctor, strategy);
                if (TryGetConstructorInvokerFromCache(key, out Func<object[], object> cached))
                {
                    invoker = cached;
                    return true;
                }

                if (IsConstructorInvokerStrategyUnavailable(key))
                {
                    invoker = null;
                    return false;
                }

                Func<object[], object> candidate = CreateConstructorInvoker(ctor, strategy);
                if (candidate == null)
                {
                    MarkConstructorInvokerStrategyUnavailable(key);
                    invoker = null;
                    return false;
                }

                Func<object[], object> resolved = AddOrGetConstructorInvoker(key, candidate);
                TrackDelegateStrategy(resolved, key);
                invoker = resolved;
                return true;
            }

            private static Func<object[], object> GetOrCreateReflectionConstructorInvoker(
                ConstructorInfo ctor
            )
            {
                CapabilityKey<ConstructorInfo> key = new(
                    ctor,
                    ReflectionDelegateStrategy.Reflection
                );
                if (TryGetConstructorInvokerFromCache(key, out Func<object[], object> cached))
                {
                    return cached;
                }

                Func<object[], object> reflectionInvoker = CreateReflectionConstructorInvoker(ctor);
                Func<object[], object> resolved = AddOrGetConstructorInvoker(
                    key,
                    reflectionInvoker
                );
                TrackDelegateStrategy(resolved, key);
                return resolved;
            }

            private static bool TryGetOrCreateParameterlessConstructor(
                ConstructorInfo ctor,
                ReflectionDelegateStrategy strategy,
                out Func<object> creator
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    creator = null;
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    creator = null;
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    creator = null;
                    return false;
                }
#endif

                CapabilityKey<ConstructorInfo> key = new(ctor, strategy);
                if (TryGetParameterlessConstructorFromCache(key, out Func<object> cached))
                {
                    creator = cached;
                    return true;
                }

                if (IsParameterlessConstructorStrategyUnavailable(key))
                {
                    creator = null;
                    return false;
                }

                Func<object> candidate = CreateParameterlessConstructor(ctor, strategy);
                if (candidate == null)
                {
                    MarkParameterlessConstructorStrategyUnavailable(key);
                    creator = null;
                    return false;
                }

                Func<object> resolved = AddOrGetParameterlessConstructor(key, candidate);
                TrackDelegateStrategy(resolved, key);
                creator = resolved;
                return true;
            }

            private static Func<object> GetOrCreateReflectionParameterlessConstructor(
                ConstructorInfo ctor
            )
            {
                CapabilityKey<ConstructorInfo> key = new(
                    ctor,
                    ReflectionDelegateStrategy.Reflection
                );
                if (TryGetParameterlessConstructorFromCache(key, out Func<object> cached))
                {
                    return cached;
                }

                Func<object> reflectionCreator = () => ctor.Invoke(null);
                Func<object> resolved = AddOrGetParameterlessConstructor(key, reflectionCreator);
                TrackDelegateStrategy(resolved, key);
                return resolved;
            }

            private static bool TryGetOrCreateTypedParameterlessConstructor<T>(
                ConstructorInfo ctor,
                Type instanceType,
                ReflectionDelegateStrategy strategy,
                out Func<T> creator
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    creator = null;
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    creator = null;
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    creator = null;
                    return false;
                }
#endif

                CapabilityKey<(ConstructorInfo ctor, Type instance)> key = new(
                    (ctor, instanceType),
                    strategy
                );
                if (TryGetTypedParameterlessConstructorFromCache(key, out Delegate cached))
                {
                    creator = (Func<T>)cached;
                    return true;
                }

                if (IsTypedParameterlessConstructorStrategyUnavailable(key))
                {
                    creator = null;
                    return false;
                }

                Func<T> candidate = CreateTypedParameterlessConstructor<T>(ctor, strategy);
                if (candidate == null)
                {
                    MarkTypedParameterlessConstructorStrategyUnavailable(key);
                    creator = null;
                    return false;
                }

                Delegate resolved = AddOrGetTypedParameterlessConstructor(key, candidate);
                TrackDelegateStrategy(resolved, key);
                creator = (Func<T>)resolved;
                return true;
            }

            private static Func<T> GetOrCreateReflectionTypedParameterlessConstructor<T>(
                ConstructorInfo ctor
            )
            {
                CapabilityKey<(ConstructorInfo ctor, Type instance)> key = new(
                    (ctor, typeof(T)),
                    ReflectionDelegateStrategy.Reflection
                );
                if (TryGetTypedParameterlessConstructorFromCache(key, out Delegate cached))
                {
                    return (Func<T>)cached;
                }

                Func<T> reflectionCreator = () => (T)ctor.Invoke(null);
                Delegate resolved = AddOrGetTypedParameterlessConstructor(key, reflectionCreator);
                TrackDelegateStrategy(resolved, key);
                return (Func<T>)resolved;
            }

#if !SINGLE_THREADED
            private static bool TryGetFieldGetterFromCache(
                CapabilityKey<FieldInfo> key,
                out Func<object, object> getter
            )
            {
                return FieldGetters.TryGetValue(key, out getter);
            }

            private static Func<object, object> AddOrGetFieldGetter(
                CapabilityKey<FieldInfo> key,
                Func<object, object> getter
            )
            {
                return FieldGetters.GetOrAdd(key, getter);
            }

            private static bool IsFieldGetterStrategyUnavailable(CapabilityKey<FieldInfo> key)
            {
                return FieldGetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkFieldGetterStrategyUnavailable(CapabilityKey<FieldInfo> key)
            {
                FieldGetterStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetFieldSetterFromCache(
                CapabilityKey<FieldInfo> key,
                out Action<object, object> setter
            )
            {
                return FieldSetters.TryGetValue(key, out setter);
            }

            private static Action<object, object> AddOrGetFieldSetter(
                CapabilityKey<FieldInfo> key,
                Action<object, object> setter
            )
            {
                return FieldSetters.GetOrAdd(key, setter);
            }

            private static bool IsFieldSetterStrategyUnavailable(CapabilityKey<FieldInfo> key)
            {
                return FieldSetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkFieldSetterStrategyUnavailable(CapabilityKey<FieldInfo> key)
            {
                FieldSetterStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetStaticFieldGetterFromCache(
                CapabilityKey<FieldInfo> key,
                out Func<object> getter
            )
            {
                return StaticFieldGetters.TryGetValue(key, out getter);
            }

            private static Func<object> AddOrGetStaticFieldGetter(
                CapabilityKey<FieldInfo> key,
                Func<object> getter
            )
            {
                return StaticFieldGetters.GetOrAdd(key, getter);
            }

            private static bool IsStaticFieldGetterStrategyUnavailable(CapabilityKey<FieldInfo> key)
            {
                return StaticFieldGetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkStaticFieldGetterStrategyUnavailable(
                CapabilityKey<FieldInfo> key
            )
            {
                StaticFieldGetterStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetStaticFieldSetterFromCache(
                CapabilityKey<FieldInfo> key,
                out Action<object> setter
            )
            {
                return StaticFieldSetters.TryGetValue(key, out setter);
            }

            private static Action<object> AddOrGetStaticFieldSetter(
                CapabilityKey<FieldInfo> key,
                Action<object> setter
            )
            {
                return StaticFieldSetters.GetOrAdd(key, setter);
            }

            private static bool IsStaticFieldSetterStrategyUnavailable(CapabilityKey<FieldInfo> key)
            {
                return StaticFieldSetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkStaticFieldSetterStrategyUnavailable(
                CapabilityKey<FieldInfo> key
            )
            {
                StaticFieldSetterStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetPropertyGetterFromCache(
                CapabilityKey<PropertyInfo> key,
                out Func<object, object> getter
            )
            {
                return PropertyGetters.TryGetValue(key, out getter);
            }

            private static Func<object, object> AddOrGetPropertyGetter(
                CapabilityKey<PropertyInfo> key,
                Func<object, object> getter
            )
            {
                return PropertyGetters.GetOrAdd(key, getter);
            }

            private static bool IsPropertyGetterStrategyUnavailable(CapabilityKey<PropertyInfo> key)
            {
                return PropertyGetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkPropertyGetterStrategyUnavailable(
                CapabilityKey<PropertyInfo> key
            )
            {
                PropertyGetterStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetPropertySetterFromCache(
                CapabilityKey<PropertyInfo> key,
                out Action<object, object> setter
            )
            {
                return PropertySetters.TryGetValue(key, out setter);
            }

            private static Action<object, object> AddOrGetPropertySetter(
                CapabilityKey<PropertyInfo> key,
                Action<object, object> setter
            )
            {
                return PropertySetters.GetOrAdd(key, setter);
            }

            private static bool IsPropertySetterStrategyUnavailable(CapabilityKey<PropertyInfo> key)
            {
                return PropertySetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkPropertySetterStrategyUnavailable(
                CapabilityKey<PropertyInfo> key
            )
            {
                PropertySetterStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetIndexerGetterFromCache(
                CapabilityKey<PropertyInfo> key,
                out Func<object, object[], object> getter
            )
            {
                return IndexerGetters.TryGetValue(key, out getter);
            }

            private static Func<object, object[], object> AddOrGetIndexerGetter(
                CapabilityKey<PropertyInfo> key,
                Func<object, object[], object> getter
            )
            {
                return IndexerGetters.GetOrAdd(key, getter);
            }

            private static bool IsIndexerGetterStrategyUnavailable(CapabilityKey<PropertyInfo> key)
            {
                return IndexerGetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkIndexerGetterStrategyUnavailable(
                CapabilityKey<PropertyInfo> key
            )
            {
                IndexerGetterStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetIndexerSetterFromCache(
                CapabilityKey<PropertyInfo> key,
                out Action<object, object, object[]> setter
            )
            {
                return IndexerSetters.TryGetValue(key, out setter);
            }

            private static Action<object, object, object[]> AddOrGetIndexerSetter(
                CapabilityKey<PropertyInfo> key,
                Action<object, object, object[]> setter
            )
            {
                return IndexerSetters.GetOrAdd(key, setter);
            }

            private static bool IsIndexerSetterStrategyUnavailable(CapabilityKey<PropertyInfo> key)
            {
                return IndexerSetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkIndexerSetterStrategyUnavailable(
                CapabilityKey<PropertyInfo> key
            )
            {
                IndexerSetterStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetMethodInvokerFromCache(
                CapabilityKey<MethodInfo> key,
                out Func<object, object[], object> invoker
            )
            {
                return MethodInvokers.TryGetValue(key, out invoker);
            }

            private static Func<object, object[], object> AddOrGetMethodInvoker(
                CapabilityKey<MethodInfo> key,
                Func<object, object[], object> invoker
            )
            {
                return MethodInvokers.GetOrAdd(key, invoker);
            }

            private static bool IsMethodInvokerStrategyUnavailable(CapabilityKey<MethodInfo> key)
            {
                return MethodInvokerStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkMethodInvokerStrategyUnavailable(CapabilityKey<MethodInfo> key)
            {
                MethodInvokerStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetStaticMethodInvokerFromCache(
                CapabilityKey<MethodInfo> key,
                out Func<object[], object> invoker
            )
            {
                return StaticMethodInvokers.TryGetValue(key, out invoker);
            }

            private static Func<object[], object> AddOrGetStaticMethodInvoker(
                CapabilityKey<MethodInfo> key,
                Func<object[], object> invoker
            )
            {
                return StaticMethodInvokers.GetOrAdd(key, invoker);
            }

            private static bool IsStaticMethodInvokerStrategyUnavailable(
                CapabilityKey<MethodInfo> key
            )
            {
                return StaticMethodInvokerStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkStaticMethodInvokerStrategyUnavailable(
                CapabilityKey<MethodInfo> key
            )
            {
                StaticMethodInvokerStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetConstructorInvokerFromCache(
                CapabilityKey<ConstructorInfo> key,
                out Func<object[], object> invoker
            )
            {
                return Constructors.TryGetValue(key, out invoker);
            }

            private static Func<object[], object> AddOrGetConstructorInvoker(
                CapabilityKey<ConstructorInfo> key,
                Func<object[], object> invoker
            )
            {
                return Constructors.GetOrAdd(key, invoker);
            }

            private static bool IsConstructorInvokerStrategyUnavailable(
                CapabilityKey<ConstructorInfo> key
            )
            {
                return ConstructorInvokerStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkConstructorInvokerStrategyUnavailable(
                CapabilityKey<ConstructorInfo> key
            )
            {
                ConstructorInvokerStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetParameterlessConstructorFromCache(
                CapabilityKey<ConstructorInfo> key,
                out Func<object> creator
            )
            {
                return ParameterlessConstructors.TryGetValue(key, out creator);
            }

            private static Func<object> AddOrGetParameterlessConstructor(
                CapabilityKey<ConstructorInfo> key,
                Func<object> creator
            )
            {
                return ParameterlessConstructors.GetOrAdd(key, creator);
            }

            private static bool IsParameterlessConstructorStrategyUnavailable(
                CapabilityKey<ConstructorInfo> key
            )
            {
                return ParameterlessConstructorStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkParameterlessConstructorStrategyUnavailable(
                CapabilityKey<ConstructorInfo> key
            )
            {
                ParameterlessConstructorStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetTypedParameterlessConstructorFromCache(
                CapabilityKey<(ConstructorInfo ctor, Type instance)> key,
                out Delegate creator
            )
            {
                return TypedParameterlessConstructors.TryGetValue(key, out creator);
            }

            private static Delegate AddOrGetTypedParameterlessConstructor(
                CapabilityKey<(ConstructorInfo ctor, Type instance)> key,
                Delegate creator
            )
            {
                return TypedParameterlessConstructors.GetOrAdd(key, creator);
            }

            private static bool IsTypedParameterlessConstructorStrategyUnavailable(
                CapabilityKey<(ConstructorInfo ctor, Type instance)> key
            )
            {
                return TypedParameterlessConstructorStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkTypedParameterlessConstructorStrategyUnavailable(
                CapabilityKey<(ConstructorInfo ctor, Type instance)> key
            )
            {
                TypedParameterlessConstructorStrategyBlocklist.TryAdd(
                    key,
                    StrategyUnavailableSentinel
                );
            }

            private static bool TryGetTypedFieldGetterFromCache(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key,
                out Delegate getter
            )
            {
                return TypedFieldGetters.TryGetValue(key, out getter);
            }

            private static Delegate AddOrGetTypedFieldGetter(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key,
                Delegate getter
            )
            {
                return TypedFieldGetters.GetOrAdd(key, getter);
            }

            private static bool IsTypedFieldGetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key
            )
            {
                return TypedFieldGetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkTypedFieldGetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key
            )
            {
                TypedFieldGetterStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetTypedFieldSetterFromCache(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key,
                out Delegate setter
            )
            {
                return TypedFieldSetters.TryGetValue(key, out setter);
            }

            private static Delegate AddOrGetTypedFieldSetter(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key,
                Delegate setter
            )
            {
                return TypedFieldSetters.GetOrAdd(key, setter);
            }

            private static bool IsTypedFieldSetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key
            )
            {
                return TypedFieldSetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkTypedFieldSetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key
            )
            {
                TypedFieldSetterStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetTypedStaticFieldGetterFromCache(
                CapabilityKey<(FieldInfo field, Type value)> key,
                out Delegate getter
            )
            {
                return TypedStaticFieldGetters.TryGetValue(key, out getter);
            }

            private static Delegate AddOrGetTypedStaticFieldGetter(
                CapabilityKey<(FieldInfo field, Type value)> key,
                Delegate getter
            )
            {
                return TypedStaticFieldGetters.GetOrAdd(key, getter);
            }

            private static bool IsTypedStaticFieldGetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type value)> key
            )
            {
                return TypedStaticFieldGetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkTypedStaticFieldGetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type value)> key
            )
            {
                TypedStaticFieldGetterStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }

            private static bool TryGetTypedStaticFieldSetterFromCache(
                CapabilityKey<(FieldInfo field, Type value)> key,
                out Delegate setter
            )
            {
                return TypedStaticFieldSetters.TryGetValue(key, out setter);
            }

            private static Delegate AddOrGetTypedStaticFieldSetter(
                CapabilityKey<(FieldInfo field, Type value)> key,
                Delegate setter
            )
            {
                return TypedStaticFieldSetters.GetOrAdd(key, setter);
            }

            private static bool IsTypedStaticFieldSetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type value)> key
            )
            {
                return TypedStaticFieldSetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkTypedStaticFieldSetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type value)> key
            )
            {
                TypedStaticFieldSetterStrategyBlocklist.TryAdd(key, StrategyUnavailableSentinel);
            }
#else
            private static bool TryGetFieldGetterFromCache(
                CapabilityKey<FieldInfo> key,
                out Func<object, object> getter
            )
            {
                return FieldGetters.TryGetValue(key, out getter);
            }

            private static Func<object, object> AddOrGetFieldGetter(
                CapabilityKey<FieldInfo> key,
                Func<object, object> getter
            )
            {
                if (FieldGetters.TryGetValue(key, out Func<object, object> existing))
                {
                    return existing;
                }

                FieldGetters[key] = getter;
                return getter;
            }

            private static bool IsFieldGetterStrategyUnavailable(CapabilityKey<FieldInfo> key)
            {
                return FieldGetterStrategyBlocklist.Contains(key);
            }

            private static void MarkFieldGetterStrategyUnavailable(CapabilityKey<FieldInfo> key)
            {
                FieldGetterStrategyBlocklist.Add(key);
            }

            private static bool TryGetFieldSetterFromCache(
                CapabilityKey<FieldInfo> key,
                out Action<object, object> setter
            )
            {
                return FieldSetters.TryGetValue(key, out setter);
            }

            private static Action<object, object> AddOrGetFieldSetter(
                CapabilityKey<FieldInfo> key,
                Action<object, object> setter
            )
            {
                if (FieldSetters.TryGetValue(key, out Action<object, object> existing))
                {
                    return existing;
                }

                FieldSetters[key] = setter;
                return setter;
            }

            private static bool IsFieldSetterStrategyUnavailable(CapabilityKey<FieldInfo> key)
            {
                return FieldSetterStrategyBlocklist.Contains(key);
            }

            private static void MarkFieldSetterStrategyUnavailable(CapabilityKey<FieldInfo> key)
            {
                FieldSetterStrategyBlocklist.Add(key);
            }

            private static bool TryGetStaticFieldGetterFromCache(
                CapabilityKey<FieldInfo> key,
                out Func<object> getter
            )
            {
                return StaticFieldGetters.TryGetValue(key, out getter);
            }

            private static Func<object> AddOrGetStaticFieldGetter(
                CapabilityKey<FieldInfo> key,
                Func<object> getter
            )
            {
                if (StaticFieldGetters.TryGetValue(key, out Func<object> existing))
                {
                    return existing;
                }

                StaticFieldGetters[key] = getter;
                return getter;
            }

            private static bool IsStaticFieldGetterStrategyUnavailable(CapabilityKey<FieldInfo> key)
            {
                return StaticFieldGetterStrategyBlocklist.Contains(key);
            }

            private static void MarkStaticFieldGetterStrategyUnavailable(
                CapabilityKey<FieldInfo> key
            )
            {
                StaticFieldGetterStrategyBlocklist.Add(key);
            }

            private static bool TryGetStaticFieldSetterFromCache(
                CapabilityKey<FieldInfo> key,
                out Action<object> setter
            )
            {
                return StaticFieldSetters.TryGetValue(key, out setter);
            }

            private static Action<object> AddOrGetStaticFieldSetter(
                CapabilityKey<FieldInfo> key,
                Action<object> setter
            )
            {
                if (StaticFieldSetters.TryGetValue(key, out Action<object> existing))
                {
                    return existing;
                }

                StaticFieldSetters[key] = setter;
                return setter;
            }

            private static bool IsStaticFieldSetterStrategyUnavailable(CapabilityKey<FieldInfo> key)
            {
                return StaticFieldSetterStrategyBlocklist.Contains(key);
            }

            private static void MarkStaticFieldSetterStrategyUnavailable(
                CapabilityKey<FieldInfo> key
            )
            {
                StaticFieldSetterStrategyBlocklist.Add(key);
            }

            private static bool TryGetPropertyGetterFromCache(
                CapabilityKey<PropertyInfo> key,
                out Func<object, object> getter
            )
            {
                return PropertyGetters.TryGetValue(key, out getter);
            }

            private static Func<object, object> AddOrGetPropertyGetter(
                CapabilityKey<PropertyInfo> key,
                Func<object, object> getter
            )
            {
                if (PropertyGetters.TryGetValue(key, out Func<object, object> existing))
                {
                    return existing;
                }

                PropertyGetters[key] = getter;
                return getter;
            }

            private static bool IsPropertyGetterStrategyUnavailable(CapabilityKey<PropertyInfo> key)
            {
                return PropertyGetterStrategyBlocklist.Contains(key);
            }

            private static void MarkPropertyGetterStrategyUnavailable(
                CapabilityKey<PropertyInfo> key
            )
            {
                PropertyGetterStrategyBlocklist.Add(key);
            }

            private static bool TryGetPropertySetterFromCache(
                CapabilityKey<PropertyInfo> key,
                out Action<object, object> setter
            )
            {
                return PropertySetters.TryGetValue(key, out setter);
            }

            private static Action<object, object> AddOrGetPropertySetter(
                CapabilityKey<PropertyInfo> key,
                Action<object, object> setter
            )
            {
                if (PropertySetters.TryGetValue(key, out Action<object, object> existing))
                {
                    return existing;
                }

                PropertySetters[key] = setter;
                return setter;
            }

            private static bool IsPropertySetterStrategyUnavailable(CapabilityKey<PropertyInfo> key)
            {
                return PropertySetterStrategyBlocklist.Contains(key);
            }

            private static void MarkPropertySetterStrategyUnavailable(
                CapabilityKey<PropertyInfo> key
            )
            {
                PropertySetterStrategyBlocklist.Add(key);
            }

            private static bool TryGetIndexerGetterFromCache(
                CapabilityKey<PropertyInfo> key,
                out Func<object, object[], object> getter
            )
            {
                return IndexerGetters.TryGetValue(key, out getter);
            }

            private static Func<object, object[], object> AddOrGetIndexerGetter(
                CapabilityKey<PropertyInfo> key,
                Func<object, object[], object> getter
            )
            {
                if (IndexerGetters.TryGetValue(key, out Func<object, object[], object> existing))
                {
                    return existing;
                }

                IndexerGetters[key] = getter;
                return getter;
            }

            private static bool IsIndexerGetterStrategyUnavailable(CapabilityKey<PropertyInfo> key)
            {
                return IndexerGetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkIndexerGetterStrategyUnavailable(
                CapabilityKey<PropertyInfo> key
            )
            {
                IndexerGetterStrategyBlocklist[key] = StrategyUnavailableSentinel;
            }

            private static bool TryGetIndexerSetterFromCache(
                CapabilityKey<PropertyInfo> key,
                out Action<object, object, object[]> setter
            )
            {
                return IndexerSetters.TryGetValue(key, out setter);
            }

            private static Action<object, object, object[]> AddOrGetIndexerSetter(
                CapabilityKey<PropertyInfo> key,
                Action<object, object, object[]> setter
            )
            {
                if (IndexerSetters.TryGetValue(key, out Action<object, object, object[]> existing))
                {
                    return existing;
                }

                IndexerSetters[key] = setter;
                return setter;
            }

            private static bool IsIndexerSetterStrategyUnavailable(CapabilityKey<PropertyInfo> key)
            {
                return IndexerSetterStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkIndexerSetterStrategyUnavailable(
                CapabilityKey<PropertyInfo> key
            )
            {
                IndexerSetterStrategyBlocklist[key] = StrategyUnavailableSentinel;
            }

            private static bool TryGetMethodInvokerFromCache(
                CapabilityKey<MethodInfo> key,
                out Func<object, object[], object> invoker
            )
            {
                return MethodInvokers.TryGetValue(key, out invoker);
            }

            private static Func<object, object[], object> AddOrGetMethodInvoker(
                CapabilityKey<MethodInfo> key,
                Func<object, object[], object> invoker
            )
            {
                if (MethodInvokers.TryGetValue(key, out Func<object, object[], object> existing))
                {
                    return existing;
                }

                MethodInvokers[key] = invoker;
                return invoker;
            }

            private static bool IsMethodInvokerStrategyUnavailable(CapabilityKey<MethodInfo> key)
            {
                return MethodInvokerStrategyBlocklist.ContainsKey(key);
            }

            private static void MarkMethodInvokerStrategyUnavailable(CapabilityKey<MethodInfo> key)
            {
                MethodInvokerStrategyBlocklist[key] = StrategyUnavailableSentinel;
            }

            private static bool TryGetStaticMethodInvokerFromCache(
                CapabilityKey<MethodInfo> key,
                out Func<object[], object> invoker
            )
            {
                return StaticMethodInvokers.TryGetValue(key, out invoker);
            }

            private static Func<object[], object> AddOrGetStaticMethodInvoker(
                CapabilityKey<MethodInfo> key,
                Func<object[], object> invoker
            )
            {
                if (StaticMethodInvokers.TryGetValue(key, out Func<object[], object> existing))
                {
                    return existing;
                }

                StaticMethodInvokers[key] = invoker;
                return invoker;
            }

            private static bool IsStaticMethodInvokerStrategyUnavailable(
                CapabilityKey<MethodInfo> key
            )
            {
                return StaticMethodInvokerStrategyBlocklist.Contains(key);
            }

            private static void MarkStaticMethodInvokerStrategyUnavailable(
                CapabilityKey<MethodInfo> key
            )
            {
                StaticMethodInvokerStrategyBlocklist.Add(key);
            }

            private static bool TryGetConstructorInvokerFromCache(
                CapabilityKey<ConstructorInfo> key,
                out Func<object[], object> invoker
            )
            {
                return Constructors.TryGetValue(key, out invoker);
            }

            private static Func<object[], object> AddOrGetConstructorInvoker(
                CapabilityKey<ConstructorInfo> key,
                Func<object[], object> invoker
            )
            {
                if (Constructors.TryGetValue(key, out Func<object[], object> existing))
                {
                    return existing;
                }

                Constructors[key] = invoker;
                return invoker;
            }

            private static bool IsConstructorInvokerStrategyUnavailable(
                CapabilityKey<ConstructorInfo> key
            )
            {
                return ConstructorInvokerStrategyBlocklist.Contains(key);
            }

            private static void MarkConstructorInvokerStrategyUnavailable(
                CapabilityKey<ConstructorInfo> key
            )
            {
                ConstructorInvokerStrategyBlocklist.Add(key);
            }

            private static bool TryGetParameterlessConstructorFromCache(
                CapabilityKey<ConstructorInfo> key,
                out Func<object> creator
            )
            {
                return ParameterlessConstructors.TryGetValue(key, out creator);
            }

            private static Func<object> AddOrGetParameterlessConstructor(
                CapabilityKey<ConstructorInfo> key,
                Func<object> creator
            )
            {
                if (ParameterlessConstructors.TryGetValue(key, out Func<object> existing))
                {
                    return existing;
                }

                ParameterlessConstructors[key] = creator;
                return creator;
            }

            private static bool IsParameterlessConstructorStrategyUnavailable(
                CapabilityKey<ConstructorInfo> key
            )
            {
                return ParameterlessConstructorStrategyBlocklist.Contains(key);
            }

            private static void MarkParameterlessConstructorStrategyUnavailable(
                CapabilityKey<ConstructorInfo> key
            )
            {
                ParameterlessConstructorStrategyBlocklist.Add(key);
            }

            private static bool TryGetTypedParameterlessConstructorFromCache(
                CapabilityKey<(ConstructorInfo ctor, Type instance)> key,
                out Delegate creator
            )
            {
                return TypedParameterlessConstructors.TryGetValue(key, out creator);
            }

            private static Delegate AddOrGetTypedParameterlessConstructor(
                CapabilityKey<(ConstructorInfo ctor, Type instance)> key,
                Delegate creator
            )
            {
                if (TypedParameterlessConstructors.TryGetValue(key, out Delegate existing))
                {
                    return existing;
                }

                TypedParameterlessConstructors[key] = creator;
                return creator;
            }

            private static bool IsTypedParameterlessConstructorStrategyUnavailable(
                CapabilityKey<(ConstructorInfo ctor, Type instance)> key
            )
            {
                return TypedParameterlessConstructorStrategyBlocklist.Contains(key);
            }

            private static void MarkTypedParameterlessConstructorStrategyUnavailable(
                CapabilityKey<(ConstructorInfo ctor, Type instance)> key
            )
            {
                TypedParameterlessConstructorStrategyBlocklist.Add(key);
            }

            private static bool TryGetTypedFieldGetterFromCache(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key,
                out Delegate getter
            )
            {
                return TypedFieldGetters.TryGetValue(key, out getter);
            }

            private static Delegate AddOrGetTypedFieldGetter(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key,
                Delegate getter
            )
            {
                if (TypedFieldGetters.TryGetValue(key, out Delegate existing))
                {
                    return existing;
                }

                TypedFieldGetters[key] = getter;
                return getter;
            }

            private static bool IsTypedFieldGetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key
            )
            {
                return TypedFieldGetterStrategyBlocklist.Contains(key);
            }

            private static void MarkTypedFieldGetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key
            )
            {
                TypedFieldGetterStrategyBlocklist.Add(key);
            }

            private static bool TryGetTypedFieldSetterFromCache(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key,
                out Delegate setter
            )
            {
                return TypedFieldSetters.TryGetValue(key, out setter);
            }

            private static Delegate AddOrGetTypedFieldSetter(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key,
                Delegate setter
            )
            {
                if (TypedFieldSetters.TryGetValue(key, out Delegate existing))
                {
                    return existing;
                }

                TypedFieldSetters[key] = setter;
                return setter;
            }

            private static bool IsTypedFieldSetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key
            )
            {
                return TypedFieldSetterStrategyBlocklist.Contains(key);
            }

            private static void MarkTypedFieldSetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type instance, Type value)> key
            )
            {
                TypedFieldSetterStrategyBlocklist.Add(key);
            }

            private static bool TryGetTypedStaticFieldGetterFromCache(
                CapabilityKey<(FieldInfo field, Type value)> key,
                out Delegate getter
            )
            {
                return TypedStaticFieldGetters.TryGetValue(key, out getter);
            }

            private static Delegate AddOrGetTypedStaticFieldGetter(
                CapabilityKey<(FieldInfo field, Type value)> key,
                Delegate getter
            )
            {
                if (TypedStaticFieldGetters.TryGetValue(key, out Delegate existing))
                {
                    return existing;
                }

                TypedStaticFieldGetters[key] = getter;
                return getter;
            }

            private static bool IsTypedStaticFieldGetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type value)> key
            )
            {
                return TypedStaticFieldGetterStrategyBlocklist.Contains(key);
            }

            private static void MarkTypedStaticFieldGetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type value)> key
            )
            {
                TypedStaticFieldGetterStrategyBlocklist.Add(key);
            }

            private static bool TryGetTypedStaticFieldSetterFromCache(
                CapabilityKey<(FieldInfo field, Type value)> key,
                out Delegate setter
            )
            {
                return TypedStaticFieldSetters.TryGetValue(key, out setter);
            }

            private static Delegate AddOrGetTypedStaticFieldSetter(
                CapabilityKey<(FieldInfo field, Type value)> key,
                Delegate setter
            )
            {
                if (TypedStaticFieldSetters.TryGetValue(key, out Delegate existing))
                {
                    return existing;
                }

                TypedStaticFieldSetters[key] = setter;
                return setter;
            }

            private static bool IsTypedStaticFieldSetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type value)> key
            )
            {
                return TypedStaticFieldSetterStrategyBlocklist.Contains(key);
            }

            private static void MarkTypedStaticFieldSetterStrategyUnavailable(
                CapabilityKey<(FieldInfo field, Type value)> key
            )
            {
                TypedStaticFieldSetterStrategyBlocklist.Add(key);
            }
#endif

            private static void TrackDelegateStrategy<TMember>(
                Delegate delegateInstance,
                CapabilityKey<TMember> key
            )
            {
                if (delegateInstance == null)
                {
                    return;
                }

                StrategyHolder holder = StrategyHolder.Create(key, delegateInstance.GetType());
                DelegateStrategyTable.Remove(delegateInstance);
                DelegateStrategyTable.Add(delegateInstance, holder);
            }

            public static void ClearFieldGetterCache()
            {
#if !SINGLE_THREADED
                FieldGetters.Clear();
                FieldGetterStrategyBlocklist.Clear();
                StaticFieldGetters.Clear();
                StaticFieldGetterStrategyBlocklist.Clear();
                TypedFieldGetters.Clear();
                TypedFieldGetterStrategyBlocklist.Clear();
                TypedStaticFieldGetters.Clear();
                TypedStaticFieldGetterStrategyBlocklist.Clear();
#else
                FieldGetters.Clear();
                FieldGetterStrategyBlocklist.Clear();
                StaticFieldGetters.Clear();
                StaticFieldGetterStrategyBlocklist.Clear();
                TypedFieldGetters.Clear();
                TypedFieldGetterStrategyBlocklist.Clear();
                TypedStaticFieldGetters.Clear();
                TypedStaticFieldGetterStrategyBlocklist.Clear();
#endif
            }

            public static void ClearFieldSetterCache()
            {
#if !SINGLE_THREADED
                FieldSetters.Clear();
                FieldSetterStrategyBlocklist.Clear();
                StaticFieldSetters.Clear();
                StaticFieldSetterStrategyBlocklist.Clear();
                TypedFieldSetters.Clear();
                TypedFieldSetterStrategyBlocklist.Clear();
                TypedStaticFieldSetters.Clear();
                TypedStaticFieldSetterStrategyBlocklist.Clear();
#else
                FieldSetters.Clear();
                FieldSetterStrategyBlocklist.Clear();
                StaticFieldSetters.Clear();
                StaticFieldSetterStrategyBlocklist.Clear();
                TypedFieldSetters.Clear();
                TypedFieldSetterStrategyBlocklist.Clear();
                TypedStaticFieldSetters.Clear();
                TypedStaticFieldSetterStrategyBlocklist.Clear();
#endif
            }

            public static void ClearPropertyCache()
            {
#if !SINGLE_THREADED
                PropertyGetters.Clear();
                PropertyGetterStrategyBlocklist.Clear();
                PropertySetters.Clear();
                PropertySetterStrategyBlocklist.Clear();
                TypedPropertyGetters.Clear();
                TypedPropertySetters.Clear();
                TypedStaticPropertyGetters.Clear();
                TypedStaticPropertySetters.Clear();
                IndexerGetters.Clear();
                IndexerGetterStrategyBlocklist.Clear();
                IndexerSetters.Clear();
                IndexerSetterStrategyBlocklist.Clear();
#else
                PropertyGetters.Clear();
                PropertyGetterStrategyBlocklist.Clear();
                PropertySetters.Clear();
                PropertySetterStrategyBlocklist.Clear();
                TypedPropertyGetters.Clear();
                TypedPropertySetters.Clear();
                TypedStaticPropertyGetters.Clear();
                TypedStaticPropertySetters.Clear();
                IndexerGetters.Clear();
                IndexerGetterStrategyBlocklist.Clear();
                IndexerSetters.Clear();
                IndexerSetterStrategyBlocklist.Clear();
#endif
            }

            public static void ClearMethodCache()
            {
#if !SINGLE_THREADED
                MethodInvokers.Clear();
                MethodInvokerStrategyBlocklist.Clear();
                StaticMethodInvokers.Clear();
                StaticMethodInvokerStrategyBlocklist.Clear();
#else
                MethodInvokers.Clear();
                MethodInvokerStrategyBlocklist.Clear();
                StaticMethodInvokers.Clear();
                StaticMethodInvokerStrategyBlocklist.Clear();
#endif
                TypedStaticInvoker0.Clear();
                TypedStaticInvoker1.Clear();
                TypedStaticInvoker2.Clear();
                TypedStaticInvoker3.Clear();
                TypedStaticInvoker4.Clear();
                TypedStaticAction0.Clear();
                TypedStaticAction1.Clear();
                TypedStaticAction2.Clear();
                TypedStaticAction3.Clear();
                TypedStaticAction4.Clear();
                TypedInstanceInvoker0.Clear();
                TypedInstanceInvoker1.Clear();
                TypedInstanceInvoker2.Clear();
                TypedInstanceInvoker3.Clear();
                TypedInstanceInvoker4.Clear();
                TypedInstanceAction0.Clear();
                TypedInstanceAction1.Clear();
                TypedInstanceAction2.Clear();
                TypedInstanceAction3.Clear();
                TypedInstanceAction4.Clear();
            }

            public static void ClearConstructorCache()
            {
#if !SINGLE_THREADED
                Constructors.Clear();
                ConstructorInvokerStrategyBlocklist.Clear();
                ParameterlessConstructors.Clear();
                ParameterlessConstructorStrategyBlocklist.Clear();
                TypedParameterlessConstructors.Clear();
                TypedParameterlessConstructorStrategyBlocklist.Clear();
#else
                Constructors.Clear();
                ConstructorInvokerStrategyBlocklist.Clear();
                ParameterlessConstructors.Clear();
                ParameterlessConstructorStrategyBlocklist.Clear();
                TypedParameterlessConstructors.Clear();
                TypedParameterlessConstructorStrategyBlocklist.Clear();
#endif
            }

            public static bool TryGetStrategy(
                Delegate delegateInstance,
                out ReflectionDelegateStrategy strategy
            )
            {
                if (delegateInstance == null)
                {
                    strategy = ReflectionDelegateStrategy.Reflection;
                    return false;
                }

                if (DelegateStrategyTable.TryGetValue(delegateInstance, out StrategyHolder holder))
                {
                    strategy = holder.Strategy;
                    return true;
                }

                strategy = ReflectionDelegateStrategy.Reflection;
                return false;
            }

            private static Func<object, object> CreatePropertyGetter(
                PropertyInfo property,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledPropertyGetter(property);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return BuildPropertyGetterIL(property);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return CreateReflectionPropertyGetter(property);
                }

                return null;
            }

            private static Action<object, object> CreatePropertySetter(
                PropertyInfo property,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledPropertySetter(property);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return BuildPropertySetterIL(property);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return CreateReflectionPropertySetter(property);
                }

                return null;
            }

            private static Func<object, object> CreateReflectionPropertyGetter(
                PropertyInfo property
            )
            {
                if (property.GetMethod != null && property.GetMethod.IsStatic)
                {
                    return _ => property.GetValue(null);
                }

                return instance => property.GetValue(instance);
            }

            private static Action<object, object> CreateReflectionPropertySetter(
                PropertyInfo property
            )
            {
                if (property.SetMethod != null && property.SetMethod.IsStatic)
                {
                    return (_, value) => property.SetValue(null, value);
                }

                return (instance, value) => property.SetValue(instance, value);
            }

            private static bool TryGetOrCreateMethodInvoker(
                MethodInfo method,
                ReflectionDelegateStrategy strategy,
                out Func<object, object[], object> invoker
            )
            {
                invoker = null;

                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return false;
                }
#endif

                CapabilityKey<MethodInfo> key = new(method, strategy);
                if (TryGetMethodInvokerFromCache(key, out Func<object, object[], object> cached))
                {
                    invoker = cached;
                    return true;
                }

                if (IsMethodInvokerStrategyUnavailable(key))
                {
                    return false;
                }

                Func<object, object[], object> candidate = CreateMethodInvoker(method, strategy);
                if (candidate == null)
                {
                    MarkMethodInvokerStrategyUnavailable(key);
                    return false;
                }

                Func<object, object[], object> resolved = AddOrGetMethodInvoker(key, candidate);
                TrackDelegateStrategy(resolved, key);
                invoker = resolved;
                return true;
            }

            private static Func<object, object[], object> GetOrCreateReflectionMethodInvoker(
                MethodInfo method
            )
            {
                CapabilityKey<MethodInfo> key = new(method, ReflectionDelegateStrategy.Reflection);
                if (TryGetMethodInvokerFromCache(key, out Func<object, object[], object> cached))
                {
                    return cached;
                }

                Func<object, object[], object> reflectionInvoker = CreateReflectionMethodInvoker(
                    method
                );
                Func<object, object[], object> resolved = AddOrGetMethodInvoker(
                    key,
                    reflectionInvoker
                );
                TrackDelegateStrategy(resolved, key);
                return resolved;
            }

            private static bool TryGetOrCreateStaticMethodInvoker(
                MethodInfo method,
                ReflectionDelegateStrategy strategy,
                out Func<object[], object> invoker
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions && !SupportsExpressions)
                {
                    invoker = null;
                    return false;
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && !SupportsDynamicIl)
                {
                    invoker = null;
                    return false;
                }
#else
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    invoker = null;
                    return false;
                }
#endif

                CapabilityKey<MethodInfo> key = new(method, strategy);
                if (TryGetStaticMethodInvokerFromCache(key, out Func<object[], object> cached))
                {
                    invoker = cached;
                    return true;
                }

                if (IsStaticMethodInvokerStrategyUnavailable(key))
                {
                    invoker = null;
                    return false;
                }

                Func<object[], object> candidate = CreateStaticMethodInvoker(method, strategy);
                if (candidate == null)
                {
                    MarkStaticMethodInvokerStrategyUnavailable(key);
                    invoker = null;
                    return false;
                }

                Func<object[], object> resolved = AddOrGetStaticMethodInvoker(key, candidate);
                TrackDelegateStrategy(resolved, key);
                invoker = resolved;
                return true;
            }

            private static Func<object[], object> GetOrCreateReflectionStaticMethodInvoker(
                MethodInfo method
            )
            {
                CapabilityKey<MethodInfo> key = new(method, ReflectionDelegateStrategy.Reflection);
                if (TryGetStaticMethodInvokerFromCache(key, out Func<object[], object> cached))
                {
                    return cached;
                }

                Func<object[], object> reflectionInvoker = CreateReflectionStaticMethodInvoker(
                    method
                );
                Func<object[], object> resolved = AddOrGetStaticMethodInvoker(
                    key,
                    reflectionInvoker
                );
                TrackDelegateStrategy(resolved, key);
                return resolved;
            }

            private static Func<object, object[], object> CreateMethodInvoker(
                MethodInfo method,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledMethodInvoker(method);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return BuildMethodInvokerIL(method);
                }
#endif
                return null;
            }

            private static Func<object, object[], object> CreateReflectionMethodInvoker(
                MethodInfo method
            )
            {
                return (instance, args) => method.Invoke(instance, args);
            }

            private static Func<object[], object> CreateStaticMethodInvoker(
                MethodInfo method,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    return CreateCompiledStaticMethodInvoker(method);
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl)
                {
                    return BuildStaticMethodInvokerIL(method);
                }
#endif
                return null;
            }

            private static Func<object[], object> CreateReflectionStaticMethodInvoker(
                MethodInfo method
            )
            {
                return args => method.Invoke(null, args);
            }

            private static Func<object[], object> CreateConstructorInvoker(
                ConstructorInfo ctor,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    if (SupportsExpressions)
                    {
                        Func<object[], object> invoker = CreateCompiledConstructor(ctor);
                        if (invoker != null)
                        {
                            return invoker;
                        }
                    }
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && SupportsDynamicIl)
                {
                    return BuildConstructorIL(ctor);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return args => ctor.Invoke(args);
                }

                return null;
            }

            private static Func<object[], object> CreateReflectionConstructorInvoker(
                ConstructorInfo ctor
            )
            {
                return args => ctor.Invoke(args);
            }

            private static Func<object> CreateParameterlessConstructor(
                ConstructorInfo ctor,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    if (SupportsExpressions)
                    {
                        Func<object> creator = CreateCompiledParameterlessConstructor(ctor);
                        if (creator != null)
                        {
                            return creator;
                        }
                    }
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && SupportsDynamicIl)
                {
                    return BuildParameterlessConstructorIL(ctor);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return () => ctor.Invoke(null);
                }

                return null;
            }

            private static Func<T> CreateTypedParameterlessConstructor<T>(
                ConstructorInfo ctor,
                ReflectionDelegateStrategy strategy
            )
            {
                if (strategy == ReflectionDelegateStrategy.Expressions)
                {
                    if (SupportsExpressions)
                    {
                        Func<T> creator = CreateCompiledParameterlessConstructor<T>(ctor);
                        if (creator != null)
                        {
                            return creator;
                        }
                    }
                }
#if EMIT_DYNAMIC_IL
                if (strategy == ReflectionDelegateStrategy.DynamicIl && SupportsDynamicIl)
                {
                    return BuildTypedParameterlessConstructorIL<T>(ctor);
                }
#endif
                if (strategy == ReflectionDelegateStrategy.Reflection)
                {
                    return () => (T)ctor.Invoke(null);
                }

                return null;
            }

            private static Func<TInstance, TValue> BuildTypedPropertyGetter<TInstance, TValue>(
                PropertyInfo property
            )
            {
                if (property == null)
                {
                    throw new ArgumentNullException(nameof(property));
                }

                MethodInfo getMethod =
                    property.GetGetMethod(true)
                    ?? throw new ArgumentException(
                        $"Property {property?.Name} has no getter",
                        nameof(property)
                    );

                Func<TInstance, TValue> getter = null;
                if (SupportsExpressions)
                {
                    getter = CreateCompiledTypedPropertyGetter<TInstance, TValue>(
                        property,
                        getMethod
                    );
                }
#if EMIT_DYNAMIC_IL
                if (
                    getter == null
                    && SupportsDynamicIl
                    && CanInlineReturnConversion(property.PropertyType, typeof(TValue))
                )
                {
                    getter = BuildTypedPropertyGetterIL<TInstance, TValue>(property, getMethod);
                }
#endif
                if (getter != null)
                {
                    return getter;
                }

                if (getMethod.IsStatic)
                {
                    return _ => (TValue)property.GetValue(null);
                }

                return instance => (TValue)property.GetValue(instance);
            }

            private static Action<TInstance, TValue> BuildTypedPropertySetter<TInstance, TValue>(
                PropertyInfo property
            )
            {
                if (property == null)
                {
                    throw new ArgumentNullException(nameof(property));
                }

                MethodInfo setMethod =
                    property.GetSetMethod(true)
                    ?? throw new ArgumentException(
                        $"Property {property?.Name} has no setter",
                        nameof(property)
                    );

                Action<TInstance, TValue> setter = null;
                if (SupportsExpressions)
                {
                    setter = CreateCompiledTypedPropertySetter<TInstance, TValue>(
                        property,
                        setMethod
                    );
                }
#if EMIT_DYNAMIC_IL
                if (
                    setter == null
                    && SupportsDynamicIl
                    && CanInlineAssignment(typeof(TValue), property.PropertyType)
                )
                {
                    setter = BuildTypedPropertySetterIL<TInstance, TValue>(property, setMethod);
                }
#endif
                if (setter != null)
                {
                    return setter;
                }

                if (setMethod.IsStatic)
                {
                    return (_, value) => property.SetValue(null, value);
                }

                return (instance, value) => property.SetValue(instance, value);
            }

            private static Func<TValue> BuildTypedStaticPropertyGetter<TValue>(
                PropertyInfo property
            )
            {
                if (property == null)
                {
                    throw new ArgumentNullException(nameof(property));
                }

                MethodInfo getMethod =
                    property.GetGetMethod(true)
                    ?? throw new ArgumentException(
                        $"Property {property?.Name} has no getter",
                        nameof(property)
                    );

                Func<TValue> getter = null;
                if (SupportsExpressions)
                {
                    getter = CreateCompiledTypedStaticPropertyGetter<TValue>(property, getMethod);
                }
#if EMIT_DYNAMIC_IL
                if (
                    getter == null
                    && SupportsDynamicIl
                    && CanInlineReturnConversion(property.PropertyType, typeof(TValue))
                )
                {
                    getter = BuildTypedStaticPropertyGetterIL<TValue>(property, getMethod);
                }
#endif
                return getter ?? (() => (TValue)property.GetValue(null));
            }

            private static Action<TValue> BuildTypedStaticPropertySetter<TValue>(
                PropertyInfo property
            )
            {
                if (property == null)
                {
                    throw new ArgumentNullException(nameof(property));
                }

                MethodInfo setMethod =
                    property.GetSetMethod(true)
                    ?? throw new ArgumentException(
                        $"Property {property?.Name} has no setter",
                        nameof(property)
                    );

                Action<TValue> setter = null;
                if (SupportsExpressions)
                {
                    setter = CreateCompiledTypedStaticPropertySetter<TValue>(property, setMethod);
                }
#if EMIT_DYNAMIC_IL
                if (
                    setter == null
                    && SupportsDynamicIl
                    && CanInlineAssignment(typeof(TValue), property.PropertyType)
                )
                {
                    setter = BuildTypedStaticPropertySetterIL<TValue>(property, setMethod);
                }
#endif
                return setter ?? (value => property.SetValue(null, value));
            }

            public static Func<TReturn> GetStaticMethodInvokerTyped<TReturn>(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (!TypedStaticInvoker0.TryGetValue(method, out Delegate del))
                {
                    del = BuildTypedStaticInvoker0<TReturn>(method);
                    TypedStaticInvoker0[method] = del;
                }
                return (Func<TReturn>)del;
#else
                Delegate del = TypedStaticInvoker0.GetOrAdd(
                    method,
                    static m => BuildTypedStaticInvoker0<TReturn>(m)
                );
                return (Func<TReturn>)del;
#endif
            }

            public static Func<T1, TReturn> GetStaticMethodInvokerTyped<T1, TReturn>(
                MethodInfo method
            )
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (!TypedStaticInvoker1.TryGetValue(method, out Delegate del))
                {
                    del = BuildTypedStaticInvoker1<T1, TReturn>(method);
                    TypedStaticInvoker1[method] = del;
                }
                return (Func<T1, TReturn>)del;
#else
                Delegate del = TypedStaticInvoker1.GetOrAdd(
                    method,
                    static m => BuildTypedStaticInvoker1<T1, TReturn>(m)
                );
                return (Func<T1, TReturn>)del;
#endif
            }

            public static Func<T1, T2, TReturn> GetStaticMethodInvokerTyped<T1, T2, TReturn>(
                MethodInfo method
            )
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (!TypedStaticInvoker2.TryGetValue(method, out Delegate del))
                {
                    del = BuildTypedStaticInvoker2<T1, T2, TReturn>(method);
                    TypedStaticInvoker2[method] = del;
                }
                return (Func<T1, T2, TReturn>)del;
#else
                Delegate del = TypedStaticInvoker2.GetOrAdd(
                    method,
                    static m => BuildTypedStaticInvoker2<T1, T2, TReturn>(m)
                );
                return (Func<T1, T2, TReturn>)del;
#endif
            }

            public static Func<T1, T2, T3, TReturn> GetStaticMethodInvokerTyped<
                T1,
                T2,
                T3,
                TReturn
            >(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (!TypedStaticInvoker3.TryGetValue(method, out Delegate del))
                {
                    del = BuildTypedStaticInvoker3<T1, T2, T3, TReturn>(method);
                    TypedStaticInvoker3[method] = del;
                }
                return (Func<T1, T2, T3, TReturn>)del;
#else
                Delegate del = TypedStaticInvoker3.GetOrAdd(
                    method,
                    static m => BuildTypedStaticInvoker3<T1, T2, T3, TReturn>(m)
                );
                return (Func<T1, T2, T3, TReturn>)del;
#endif
            }

            public static Func<T1, T2, T3, T4, TReturn> GetStaticMethodInvokerTyped<
                T1,
                T2,
                T3,
                T4,
                TReturn
            >(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (!TypedStaticInvoker4.TryGetValue(method, out Delegate del))
                {
                    del = BuildTypedStaticInvoker4<T1, T2, T3, T4, TReturn>(method);
                    TypedStaticInvoker4[method] = del;
                }
                return (Func<T1, T2, T3, T4, TReturn>)del;
#else
                Delegate del = TypedStaticInvoker4.GetOrAdd(
                    method,
                    static m => BuildTypedStaticInvoker4<T1, T2, T3, T4, TReturn>(m)
                );
                return (Func<T1, T2, T3, T4, TReturn>)del;
#endif
            }

            public static Action GetStaticActionInvokerTyped(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (!TypedStaticAction0.TryGetValue(method, out Delegate del))
                {
                    del = BuildStaticActionInvoker0(method);
                    TypedStaticAction0[method] = del;
                }
                return (Action)del;
#else
                Delegate del = TypedStaticAction0.GetOrAdd(
                    method,
                    static m => BuildStaticActionInvoker0(m)
                );
                return (Action)del;
#endif
            }

            public static Action<T1> GetStaticActionInvokerTyped<T1>(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (!TypedStaticAction1.TryGetValue(method, out Delegate del))
                {
                    del = BuildStaticActionInvoker1<T1>(method);
                    TypedStaticAction1[method] = del;
                }
                return (Action<T1>)del;
#else
                Delegate del = TypedStaticAction1.GetOrAdd(
                    method,
                    static m => BuildStaticActionInvoker1<T1>(m)
                );
                return (Action<T1>)del;
#endif
            }

            public static Action<T1, T2> GetStaticActionInvokerTyped<T1, T2>(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (!TypedStaticAction2.TryGetValue(method, out Delegate del))
                {
                    del = BuildStaticActionInvoker2<T1, T2>(method);
                    TypedStaticAction2[method] = del;
                }
                return (Action<T1, T2>)del;
#else
                Delegate del = TypedStaticAction2.GetOrAdd(
                    method,
                    static m => BuildStaticActionInvoker2<T1, T2>(m)
                );
                return (Action<T1, T2>)del;
#endif
            }

            public static Action<T1, T2, T3> GetStaticActionInvokerTyped<T1, T2, T3>(
                MethodInfo method
            )
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (!TypedStaticAction3.TryGetValue(method, out Delegate del))
                {
                    del = BuildStaticActionInvoker3<T1, T2, T3>(method);
                    TypedStaticAction3[method] = del;
                }
                return (Action<T1, T2, T3>)del;
#else
                Delegate del = TypedStaticAction3.GetOrAdd(
                    method,
                    static m => BuildStaticActionInvoker3<T1, T2, T3>(m)
                );
                return (Action<T1, T2, T3>)del;
#endif
            }

            public static Action<T1, T2, T3, T4> GetStaticActionInvokerTyped<T1, T2, T3, T4>(
                MethodInfo method
            )
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (!TypedStaticAction4.TryGetValue(method, out Delegate del))
                {
                    del = BuildStaticActionInvoker4<T1, T2, T3, T4>(method);
                    TypedStaticAction4[method] = del;
                }
                return (Action<T1, T2, T3, T4>)del;
#else
                Delegate del = TypedStaticAction4.GetOrAdd(
                    method,
                    static m => BuildStaticActionInvoker4<T1, T2, T3, T4>(m)
                );
                return (Action<T1, T2, T3, T4>)del;
#endif
            }

            public static Func<TInstance, TReturn> GetInstanceMethodInvokerTyped<
                TInstance,
                TReturn
            >(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (
                    !TypedInstanceInvoker0.TryGetValue(
                        (method, typeof(TInstance)),
                        out Delegate del
                    )
                )
                {
                    del = BuildInstanceInvoker0<TInstance, TReturn>(method);
                    TypedInstanceInvoker0[(method, typeof(TInstance))] = del;
                }
                return (Func<TInstance, TReturn>)del;
#else
                Delegate del = TypedInstanceInvoker0.GetOrAdd(
                    (method, typeof(TInstance)),
                    static m => BuildInstanceInvoker0<TInstance, TReturn>(m.method)
                );
                return (Func<TInstance, TReturn>)del;
#endif
            }

            public static Func<TInstance, T1, TReturn> GetInstanceMethodInvokerTyped<
                TInstance,
                T1,
                TReturn
            >(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (
                    !TypedInstanceInvoker1.TryGetValue(
                        (method, typeof(TInstance)),
                        out Delegate del
                    )
                )
                {
                    del = BuildInstanceInvoker1<TInstance, T1, TReturn>(method);
                    TypedInstanceInvoker1[(method, typeof(TInstance))] = del;
                }
                return (Func<TInstance, T1, TReturn>)del;
#else
                Delegate del = TypedInstanceInvoker1.GetOrAdd(
                    (method, typeof(TInstance)),
                    static m => BuildInstanceInvoker1<TInstance, T1, TReturn>(m.method)
                );
                return (Func<TInstance, T1, TReturn>)del;
#endif
            }

            public static Func<TInstance, T1, T2, TReturn> GetInstanceMethodInvokerTyped<
                TInstance,
                T1,
                T2,
                TReturn
            >(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (
                    !TypedInstanceInvoker2.TryGetValue(
                        (method, typeof(TInstance)),
                        out Delegate del
                    )
                )
                {
                    del = BuildInstanceInvoker2<TInstance, T1, T2, TReturn>(method);
                    TypedInstanceInvoker2[(method, typeof(TInstance))] = del;
                }
                return (Func<TInstance, T1, T2, TReturn>)del;
#else
                Delegate del = TypedInstanceInvoker2.GetOrAdd(
                    (method, typeof(TInstance)),
                    static m => BuildInstanceInvoker2<TInstance, T1, T2, TReturn>(m.method)
                );
                return (Func<TInstance, T1, T2, TReturn>)del;
#endif
            }

            public static Func<TInstance, T1, T2, T3, TReturn> GetInstanceMethodInvokerTyped<
                TInstance,
                T1,
                T2,
                T3,
                TReturn
            >(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (
                    !TypedInstanceInvoker3.TryGetValue(
                        (method, typeof(TInstance)),
                        out Delegate del
                    )
                )
                {
                    del = BuildInstanceInvoker3<TInstance, T1, T2, T3, TReturn>(method);
                    TypedInstanceInvoker3[(method, typeof(TInstance))] = del;
                }
                return (Func<TInstance, T1, T2, T3, TReturn>)del;
#else
                Delegate del = TypedInstanceInvoker3.GetOrAdd(
                    (method, typeof(TInstance)),
                    static m => BuildInstanceInvoker3<TInstance, T1, T2, T3, TReturn>(m.method)
                );
                return (Func<TInstance, T1, T2, T3, TReturn>)del;
#endif
            }

            public static Func<TInstance, T1, T2, T3, T4, TReturn> GetInstanceMethodInvokerTyped<
                TInstance,
                T1,
                T2,
                T3,
                T4,
                TReturn
            >(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (
                    !TypedInstanceInvoker4.TryGetValue(
                        (method, typeof(TInstance)),
                        out Delegate del
                    )
                )
                {
                    del = BuildInstanceInvoker4<TInstance, T1, T2, T3, T4, TReturn>(method);
                    TypedInstanceInvoker4[(method, typeof(TInstance))] = del;
                }
                return (Func<TInstance, T1, T2, T3, T4, TReturn>)del;
#else
                Delegate del = TypedInstanceInvoker4.GetOrAdd(
                    (method, typeof(TInstance)),
                    static m => BuildInstanceInvoker4<TInstance, T1, T2, T3, T4, TReturn>(m.method)
                );
                return (Func<TInstance, T1, T2, T3, T4, TReturn>)del;
#endif
            }

            public static Action<TInstance> GetInstanceActionInvokerTyped<TInstance>(
                MethodInfo method
            )
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (
                    !TypedInstanceAction0.TryGetValue((method, typeof(TInstance)), out Delegate del)
                )
                {
                    del = BuildInstanceActionInvoker0<TInstance>(method);
                    TypedInstanceAction0[(method, typeof(TInstance))] = del;
                }
                return (Action<TInstance>)del;
#else
                Delegate del = TypedInstanceAction0.GetOrAdd(
                    (method, typeof(TInstance)),
                    static m => BuildInstanceActionInvoker0<TInstance>(m.method)
                );
                return (Action<TInstance>)del;
#endif
            }

            public static Action<TInstance, T1> GetInstanceActionInvokerTyped<TInstance, T1>(
                MethodInfo method
            )
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (
                    !TypedInstanceAction1.TryGetValue((method, typeof(TInstance)), out Delegate del)
                )
                {
                    del = BuildInstanceActionInvoker1<TInstance, T1>(method);
                    TypedInstanceAction1[(method, typeof(TInstance))] = del;
                }
                return (Action<TInstance, T1>)del;
#else
                Delegate del = TypedInstanceAction1.GetOrAdd(
                    (method, typeof(TInstance)),
                    static m => BuildInstanceActionInvoker1<TInstance, T1>(m.method)
                );
                return (Action<TInstance, T1>)del;
#endif
            }

            public static Action<TInstance, T1, T2> GetInstanceActionInvokerTyped<
                TInstance,
                T1,
                T2
            >(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (
                    !TypedInstanceAction2.TryGetValue((method, typeof(TInstance)), out Delegate del)
                )
                {
                    del = BuildInstanceActionInvoker2<TInstance, T1, T2>(method);
                    TypedInstanceAction2[(method, typeof(TInstance))] = del;
                }
                return (Action<TInstance, T1, T2>)del;
#else
                Delegate del = TypedInstanceAction2.GetOrAdd(
                    (method, typeof(TInstance)),
                    static m => BuildInstanceActionInvoker2<TInstance, T1, T2>(m.method)
                );
                return (Action<TInstance, T1, T2>)del;
#endif
            }

            public static Action<TInstance, T1, T2, T3> GetInstanceActionInvokerTyped<
                TInstance,
                T1,
                T2,
                T3
            >(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (
                    !TypedInstanceAction3.TryGetValue((method, typeof(TInstance)), out Delegate del)
                )
                {
                    del = BuildInstanceActionInvoker3<TInstance, T1, T2, T3>(method);
                    TypedInstanceAction3[(method, typeof(TInstance))] = del;
                }
                return (Action<TInstance, T1, T2, T3>)del;
#else
                Delegate del = TypedInstanceAction3.GetOrAdd(
                    (method, typeof(TInstance)),
                    static m => BuildInstanceActionInvoker3<TInstance, T1, T2, T3>(m.method)
                );
                return (Action<TInstance, T1, T2, T3>)del;
#endif
            }

            public static Action<TInstance, T1, T2, T3, T4> GetInstanceActionInvokerTyped<
                TInstance,
                T1,
                T2,
                T3,
                T4
            >(MethodInfo method)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }
#if SINGLE_THREADED
                if (
                    !TypedInstanceAction4.TryGetValue((method, typeof(TInstance)), out Delegate del)
                )
                {
                    del = BuildInstanceActionInvoker4<TInstance, T1, T2, T3, T4>(method);
                    TypedInstanceAction4[(method, typeof(TInstance))] = del;
                }
                return (Action<TInstance, T1, T2, T3, T4>)del;
#else
                Delegate del = TypedInstanceAction4.GetOrAdd(
                    (method, typeof(TInstance)),
                    static m => BuildInstanceActionInvoker4<TInstance, T1, T2, T3, T4>(m.method)
                );
                return (Action<TInstance, T1, T2, T3, T4>)del;
#endif
            }

            private readonly struct CapabilityKey<T> : IEquatable<CapabilityKey<T>>
            {
                internal CapabilityKey(T member, ReflectionDelegateStrategy strategy)
                {
                    Member = member;
                    Strategy = strategy;
                }

                internal T Member { get; }

                internal ReflectionDelegateStrategy Strategy { get; }

                public bool Equals(CapabilityKey<T> other)
                {
                    return Strategy == other.Strategy
                        && EqualityComparer<T>.Default.Equals(Member, other.Member);
                }

                public override bool Equals(object obj)
                {
                    if (obj is CapabilityKey<T> other)
                    {
                        return Equals(other);
                    }

                    return false;
                }

                public override int GetHashCode()
                {
                    return Objects.HashCode(Member, Strategy);
                }
            }

            private sealed class StrategyHolder
            {
                internal StrategyHolder(
                    ReflectionDelegateStrategy strategy,
                    object memberKey,
                    Type delegateType
                )
                {
                    Strategy = strategy;
                    MemberKey = memberKey;
                    DelegateType = delegateType;
                }

                internal ReflectionDelegateStrategy Strategy { get; }

                internal object MemberKey { get; }

                internal Type DelegateType { get; }

                internal static StrategyHolder Create<TMember>(
                    CapabilityKey<TMember> key,
                    Type delegateType
                )
                {
                    object memberKey = key.Member is null ? NullMemberKey : key.Member;
                    return new StrategyHolder(key.Strategy, memberKey, delegateType);
                }

                private static readonly object NullMemberKey = new();
            }
        }
    }
}
