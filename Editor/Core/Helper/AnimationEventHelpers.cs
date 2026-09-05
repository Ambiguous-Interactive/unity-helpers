// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    public static class AnimationEventHelpers
    {
        public static readonly IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> TypesToMethods;

        static AnimationEventHelpers()
        {
            List<(Type, string)> ignoreDerived = new();
            Dictionary<Type, List<MethodInfo>> typesToMethods = new();

            TypeCache.TypeCollection monoTypes = TypeCache.GetTypesDerivedFrom<MonoBehaviour>();
            for (int i = 0; i < monoTypes.Count; i++)
            {
                Type type = monoTypes[i];
                if (type == null || !type.IsClass || type.IsAbstract)
                {
                    continue;
                }

                List<MethodInfo> definedMethods = GetPossibleAnimatorEventsForType(type);

                for (int m = definedMethods.Count - 1; 0 <= m; m--)
                {
                    MethodInfo method = definedMethods[m];
                    if (method.DeclaringType != type)
                    {
                        definedMethods.RemoveAt(m);
                        continue;
                    }
                    if (!method.IsAttributeDefined<AnimationEventAttribute>(out _, inherit: false))
                    {
                        definedMethods.RemoveAt(m);
                    }
                }

                if (0 < definedMethods.Count)
                {
                    List<MethodInfo> allPossible = GetPossibleAnimatorEventsForType(type);
                    foreach (MethodInfo candidate in allPossible)
                    {
                        if (candidate.DeclaringType == type)
                        {
                            continue;
                        }

                        if (
                            !candidate.IsAttributeDefined(
                                out AnimationEventAttribute attribute,
                                inherit: false
                            )
                        )
                        {
                            continue;
                        }

                        if (attribute.ignoreDerived)
                        {
                            continue;
                        }

                        ParameterInfo[] parameters = candidate.GetParameters();
                        Type[] paramTypes;
                        if (parameters is { Length: > 0 })
                        {
                            paramTypes = new Type[parameters.Length];
                            for (int pi = 0; pi < parameters.Length; pi++)
                            {
                                paramTypes[pi] = parameters[pi].ParameterType;
                            }
                        }
                        else
                        {
                            paramTypes = Array.Empty<Type>();
                        }

                        MethodInfo resolved = candidate.DeclaringType.GetMethod(
                            candidate.Name,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            paramTypes,
                            null
                        );
                        if (resolved != null)
                        {
                            definedMethods.Add(resolved);
                        }
                    }
                }

                foreach (MethodInfo definedMethod in definedMethods)
                {
                    if (
                        definedMethod.IsAttributeDefined(
                            out AnimationEventAttribute attr,
                            inherit: false
                        ) && attr.ignoreDerived
                    )
                    {
                        ignoreDerived.Add((type, definedMethod.Name));
                    }
                }

                if (0 < definedMethods.Count)
                {
                    typesToMethods[type] = definedMethods;
                }
            }

            using (
                PooledResource<List<KeyValuePair<Type, List<MethodInfo>>>> methodBufferResource =
                    Buffers<KeyValuePair<Type, List<MethodInfo>>>.List.Get()
            )
            {
                List<KeyValuePair<Type, List<MethodInfo>>> methodBuffer =
                    methodBufferResource.resource;
                foreach (KeyValuePair<Type, List<MethodInfo>> entry in typesToMethods)
                {
                    methodBuffer.Add(entry);
                }

                foreach (KeyValuePair<Type, List<MethodInfo>> entry in methodBuffer)
                {
                    if (entry.Value.Count <= 0)
                    {
                        _ = typesToMethods.Remove(entry.Key);
                        continue;
                    }

                    Type key = entry.Key;
                    foreach ((System.Type, string) ignoreDerivedElement in ignoreDerived)
                    {
                        (Type baseType, string methodName) = ignoreDerivedElement;
                        if (key == baseType)
                        {
                            continue;
                        }

                        if (!key.IsSubclassOf(baseType))
                        {
                            continue;
                        }

                        for (int midx = entry.Value.Count - 1; 0 <= midx; midx--)
                        {
                            if (entry.Value[midx].Name == methodName)
                            {
                                entry.Value.RemoveAt(midx);
                            }
                        }

                        if (entry.Value.Count <= 0)
                        {
                            _ = typesToMethods.Remove(entry.Key);
                            break;
                        }
                    }
                }
            }

            Dictionary<Type, IReadOnlyList<MethodInfo>> ro = new();
            foreach (KeyValuePair<Type, List<MethodInfo>> kvp in typesToMethods)
            {
                ro[kvp.Key] = kvp.Value;
            }
            TypesToMethods = ro;
        }

        public static List<MethodInfo> GetPossibleAnimatorEventsForType(Type type)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            using PooledResource<List<MethodInfo>> resultResource = Buffers<MethodInfo>.List.Get(
                out List<MethodInfo> result
            );
            {
                foreach (MethodInfo m in methods)
                {
                    if (m.ReturnType != typeof(void))
                    {
                        continue;
                    }

                    ParameterInfo[] ps = m.GetParameters();
                    bool ok;
                    if (ps == null || ps.Length == 0)
                    {
                        ok = true;
                    }
                    else if (ps.Length == 1)
                    {
                        Type pt = ps[0].ParameterType;
                        ok =
                            pt == typeof(int)
                            || pt == typeof(float)
                            || pt == typeof(string)
                            || pt == typeof(UnityEngine.Object)
                            || (pt.BaseType == typeof(Enum));
                    }
                    else
                    {
                        ok = false;
                    }

                    if (ok)
                    {
                        result.Add(m);
                    }
                }

                result.Sort(
                    static (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal)
                );
                // Do not expose a list that will be returned to the pool.
                return new List<MethodInfo>(result);
            }
        }
    }
}
