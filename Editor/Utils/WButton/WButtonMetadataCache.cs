// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WButton
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal enum WButtonExecutionKind
    {
        Synchronous = 0,
        Enumerator = 1,
        Task = 2,
        ValueTask = 3,
    }

    internal sealed class WButtonParameterMetadata
    {
        internal WButtonParameterMetadata(ParameterInfo parameter, int index)
        {
            ParameterInfo = parameter ?? throw new ArgumentNullException(nameof(parameter));
            Name = parameter.Name ?? $"arg{index}";

            Type parameterType = parameter.ParameterType;
            if (parameterType.IsByRef)
            {
                parameterType = parameterType.GetElementType();
            }

            ParameterType = parameterType ?? typeof(object);
            IsCancellationToken = ParameterType == typeof(CancellationToken);
            IsParamsArray = ReflectionHelpers.HasAttributeSafe<ParamArrayAttribute>(
                parameter,
                inherit: false
            );
            IsOptional = parameter.IsOptional;
            HasDefaultValue = TryGetDefaultValue(parameter, out object defaultValue);
            DefaultValue = defaultValue;
            IsUnityObject = typeof(UnityEngine.Object).IsAssignableFrom(ParameterType);
            IsValueType = ParameterType.IsValueType && !ParameterType.IsEnum;
        }

        internal ParameterInfo ParameterInfo { get; }

        internal Type ParameterType { get; }

        internal string Name { get; }

        internal bool IsOptional { get; }

        internal bool IsParamsArray { get; }

        internal bool IsCancellationToken { get; }

        internal bool IsUnityObject { get; }

        internal bool IsValueType { get; }

        internal bool HasDefaultValue { get; }

        internal object DefaultValue { get; }

        private static bool TryGetDefaultValue(ParameterInfo parameter, out object value)
        {
            if (!parameter.HasDefaultValue)
            {
                value = null;
                return false;
            }

            object resolvedValue;
            try
            {
                resolvedValue = parameter.DefaultValue;
            }
            catch
            {
                resolvedValue = Type.Missing;
            }

            if (resolvedValue == Type.Missing || resolvedValue == DBNull.Value)
            {
                resolvedValue = null;
            }

            if (resolvedValue == null && parameter.ParameterType.IsValueType)
            {
                if (
                    !WButtonValueUtility.TryCreateInstance(
                        parameter.ParameterType,
                        out object createdValue
                    )
                )
                {
                    resolvedValue = null;
                }
                else
                {
                    resolvedValue = createdValue;
                }
            }

            value = resolvedValue;
            return true;
        }
    }

    internal sealed class WButtonMethodMetadata
    {
        internal WButtonMethodMetadata(
            Type declaringType,
            MethodInfo method,
            WButtonAttribute attribute,
            WButtonParameterMetadata[] parameters,
            WButtonExecutionKind executionKind,
            Type returnType,
            Type asyncResultType,
            bool returnsVoid,
            int cancellationTokenIndex,
            string colorKey,
            int declarationOrder
        )
        {
            DeclaringType = declaringType;
            Method = method;
            Attribute = attribute;
            Parameters = parameters ?? Array.Empty<WButtonParameterMetadata>();
            ExecutionKind = executionKind;
            ReturnType = returnType ?? typeof(void);
            AsyncResultType = asyncResultType;
            ReturnsVoid = returnsVoid;
            CancellationTokenParameterIndex = cancellationTokenIndex;
            DisplayName = string.IsNullOrEmpty(attribute.DisplayName)
                ? method.Name
                : attribute.DisplayName;
            DrawOrder = attribute.DrawOrder;
            HistoryCapacity = attribute.HistoryCapacity;
            ColorKey = string.IsNullOrEmpty(colorKey) ? null : colorKey;
            GroupName = string.IsNullOrWhiteSpace(attribute.GroupName) ? null : attribute.GroupName;
            GroupPriority = attribute.GroupPriority;
            GroupPlacement = attribute.GroupPlacement;
            DeclarationOrder = declarationOrder;
        }

        internal Type DeclaringType { get; }

        internal MethodInfo Method { get; }

        internal WButtonAttribute Attribute { get; }

        internal string DisplayName { get; }

        internal int DrawOrder { get; }

        internal int HistoryCapacity { get; }

        internal string ColorKey { get; }

        internal string GroupName { get; }

        /// <summary>
        /// Controls the render order of this button's group within its placement section.
        /// Lower values render first. Value of <see cref="WButtonAttribute.NoGroupPriority"/>
        /// means no explicit priority was set and the group sorts after groups with explicit priorities.
        /// </summary>
        internal int GroupPriority { get; }

        /// <summary>
        /// Controls whether this button's group renders at the top or bottom of the inspector,
        /// overriding the global Unity Helpers setting.
        /// </summary>
        internal WButtonGroupPlacement GroupPlacement { get; }

        /// <summary>
        /// The order in which this method was discovered during reflection.
        /// This preserves the source code declaration order for grouping purposes.
        /// </summary>
        internal int DeclarationOrder { get; }

        [Obsolete("Use ColorKey instead.")]
        internal string Priority => ColorKey;

        internal WButtonParameterMetadata[] Parameters { get; }

        internal WButtonExecutionKind ExecutionKind { get; }

        internal Type ReturnType { get; }

        internal Type AsyncResultType { get; }

        internal bool ReturnsVoid { get; }

        internal int CancellationTokenParameterIndex { get; }
    }

    internal static class WButtonMetadataCache
    {
        private static readonly Dictionary<Type, WButtonMethodMetadata[]> Cache = new();

        internal static void ClearCache()
        {
            Cache.Clear();
        }

        internal static IReadOnlyList<WButtonMethodMetadata> GetMetadata(Type inspectedType)
        {
            if (inspectedType == null)
            {
                throw new ArgumentNullException(nameof(inspectedType));
            }

            if (Cache.TryGetValue(inspectedType, out WButtonMethodMetadata[] cached))
            {
                return cached;
            }

            WButtonMethodMetadata[] built = BuildMetadata(inspectedType);
            Cache[inspectedType] = built;
            return built;
        }

        private static WButtonMethodMetadata[] BuildMetadata(Type inspectedType)
        {
            List<WButtonMethodMetadata> entries = new();
            HashSet<MethodInfo> processedBases = new();
            int declarationOrder = 0;

            Type currentType = inspectedType;
            while (currentType != null && currentType != typeof(object))
            {
                MethodInfo[] methods = currentType.GetMethods(
                    BindingFlags.Instance
                        | BindingFlags.Static
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly
                );

                foreach (MethodInfo method in methods)
                {
                    MethodInfo baseDefinition = method.GetBaseDefinition();
                    if (processedBases.Contains(baseDefinition))
                    {
                        continue;
                    }

                    if (
                        !ReflectionHelpers.TryGetAttributeSafe(
                            method,
                            out WButtonAttribute attribute,
                            inherit: true
                        )
                    )
                    {
                        continue;
                    }

                    if (method.IsGenericMethodDefinition || method.ContainsGenericParameters)
                    {
                        Debug.LogWarning(
                            $"[WButton] Method {method.DeclaringType?.Name}.{method.Name} is generic and cannot be exposed as a button."
                        );
                        processedBases.Add(baseDefinition);
                        continue;
                    }

                    ParameterInfo[] rawParameters = method.GetParameters();
                    WButtonParameterMetadata[] parameterMetadata = BuildParameterMetadata(
                        method,
                        rawParameters,
                        out int cancellationTokenIndex
                    );
                    if (parameterMetadata == null)
                    {
                        processedBases.Add(baseDefinition);
                        continue;
                    }

                    Type returnType = method.ReturnType;
                    Classification classification = ClassifyReturnType(returnType);
                    WButtonMethodMetadata metadata = new(
                        method.DeclaringType ?? inspectedType,
                        method,
                        attribute,
                        parameterMetadata,
                        classification._executionKind,
                        classification._returnType,
                        classification._asyncResultType,
                        classification._returnsVoid,
                        cancellationTokenIndex,
                        attribute.ColorKey,
                        declarationOrder
                    );
                    entries.Add(metadata);
                    processedBases.Add(baseDefinition);
                    declarationOrder++;
                }

                currentType = currentType.BaseType;
            }

            entries.Sort(CompareMetadata);
            return entries.ToArray();
        }

        private static int CompareMetadata(WButtonMethodMetadata left, WButtonMethodMetadata right)
        {
            if (left.DrawOrder != right.DrawOrder)
            {
                return left.DrawOrder.CompareTo(right.DrawOrder);
            }

            // Within the same draw order, preserve declaration order (source code order)
            return left.DeclarationOrder.CompareTo(right.DeclarationOrder);
        }

        private static WButtonParameterMetadata[] BuildParameterMetadata(
            MethodInfo method,
            ParameterInfo[] rawParameters,
            out int cancellationTokenIndex
        )
        {
            if (rawParameters == null || rawParameters.Length == 0)
            {
                cancellationTokenIndex = -1;
                return Array.Empty<WButtonParameterMetadata>();
            }

            int tokenIndex = -1;
            WButtonParameterMetadata[] parameters = new WButtonParameterMetadata[
                rawParameters.Length
            ];
            for (int index = 0; index < rawParameters.Length; index++)
            {
                ParameterInfo parameter = rawParameters[index];
                if (
                    parameter.ParameterType.IsByRef
                    || parameter.IsOut
                    || parameter.ParameterType.IsPointer
                )
                {
                    Debug.LogWarning(
                        $"[WButton] Method {method.DeclaringType?.Name}.{method.Name} has an unsupported parameter '{parameter.Name}'. Ref, out, and pointer parameters are not supported."
                    );
                    cancellationTokenIndex = tokenIndex;
                    return null;
                }

                WButtonParameterMetadata metadata = new(parameter, index);
                parameters[index] = metadata;
                if (metadata.IsCancellationToken && tokenIndex < 0)
                {
                    tokenIndex = index;
                }
            }

            cancellationTokenIndex = tokenIndex;
            return parameters;
        }

        private static Classification ClassifyReturnType(Type returnType)
        {
            WButtonExecutionKind executionKind = WButtonExecutionKind.Synchronous;
            Type asyncResultType = null;
            bool returnsVoid = returnType == typeof(void);

            if (typeof(System.Collections.IEnumerator).IsAssignableFrom(returnType))
            {
                executionKind = WButtonExecutionKind.Enumerator;
            }
            else if (typeof(Task).IsAssignableFrom(returnType))
            {
                executionKind = WButtonExecutionKind.Task;
                if (returnType.IsGenericType)
                {
                    Type[] genericArguments = returnType.GetGenericArguments();
                    if (genericArguments.Length == 1)
                    {
                        asyncResultType = genericArguments[0];
                    }
                }
            }
            else if (returnType == typeof(ValueTask) || IsGenericValueTask(returnType))
            {
                executionKind = WButtonExecutionKind.ValueTask;
                if (returnType.IsGenericType)
                {
                    Type[] genericArguments = returnType.GetGenericArguments();
                    if (genericArguments.Length == 1)
                    {
                        asyncResultType = genericArguments[0];
                    }
                }
            }

            Classification classification = new()
            {
                _executionKind = executionKind,
                _returnType = returnType,
                _asyncResultType = asyncResultType,
                _returnsVoid = returnsVoid,
            };
            return classification;
        }

        private static bool IsGenericValueTask(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (!type.IsGenericType)
            {
                return false;
            }

            return type.GetGenericTypeDefinition() == typeof(ValueTask<>);
        }

        private sealed class Classification
        {
            internal WButtonExecutionKind _executionKind;
            internal Type _returnType;
            internal Type _asyncResultType;
            internal bool _returnsVoid;
        }
    }
#endif
}
