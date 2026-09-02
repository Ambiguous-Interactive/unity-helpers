// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WButton
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;

    internal static class WButtonParameterDrawer
    {
        private static readonly Dictionary<string, GUIContent> ParameterLabelCache = new(
            StringComparer.Ordinal
        );
        private static readonly Dictionary<string, GUIContent> NullableLabelCache = new(
            StringComparer.Ordinal
        );
        private static readonly Dictionary<int, GUIContent> ElementLabelCache = new();
        private static readonly GUIContent SizeContent = new("Size");

        internal static bool DrawParameters(WButtonMethodState[] states)
        {
            if (states == null || states.Length == 0)
            {
                return false;
            }

            WButtonParameterState[] primaryParameters = states[0].Parameters;
            if (primaryParameters == null || primaryParameters.Length == 0)
            {
                return false;
            }

            bool anyChanged = false;
            for (
                int parameterIndex = 0;
                parameterIndex < primaryParameters.Length;
                parameterIndex++
            )
            {
                WButtonParameterState primaryState = primaryParameters[parameterIndex];
                WButtonParameterMetadata metadata = primaryState.Metadata;
                if (metadata.IsCancellationToken)
                {
                    continue;
                }

                GUIContent label = GetParameterLabel(metadata);
                bool mixedValue = IsMixedValue(states, parameterIndex);

                EditorGUI.showMixedValue = mixedValue;
                EditorGUI.BeginChangeCheck();
                object newValue = DrawParameterField(label, metadata, states, parameterIndex);
                bool changed = EditorGUI.EndChangeCheck();
                EditorGUI.showMixedValue = false;

                if (changed)
                {
                    ApplyNewValue(states, parameterIndex, newValue);
                    anyChanged = true;
                }
            }

            return anyChanged;
        }

        private static object DrawParameterField(
            GUIContent label,
            WButtonParameterMetadata metadata,
            WButtonMethodState[] states,
            int parameterIndex
        )
        {
            WButtonParameterState primaryState = states[0].Parameters[parameterIndex];
            object currentValue = primaryState.CurrentValue;
            Type parameterType = metadata.ParameterType;
            Type nullableUnderlying = Nullable.GetUnderlyingType(parameterType);
            bool isNullable = nullableUnderlying != null;
            Type effectiveType = nullableUnderlying ?? parameterType;

            if (isNullable)
            {
                bool isNull = currentValue == null;
                string nullKey = label.text;
                /*
                    Drawn every IMGUI repaint. A lambda here captures `label`, so the closure and its
                    delegate allocate on every draw even when the cache hits; this is main-thread-only,
                    so the plain lookup is both allocation-free and simpler.
                */
                if (!NullableLabelCache.TryGetValue(nullKey, out GUIContent nullLabel))
                {
                    nullLabel = new GUIContent($"{label.text} (Null)", label.tooltip);
                    NullableLabelCache[nullKey] = nullLabel;
                }
                bool newIsNull = EditorGUILayout.Toggle(nullLabel, isNull);
                if (newIsNull)
                {
                    SetNull(states, parameterIndex);
                    return null;
                }

                if (
                    currentValue == null
                    && WButtonValueUtility.TryCreateInstance(effectiveType, out object createdValue)
                )
                {
                    currentValue = createdValue;
                }
            }

            if (effectiveType == typeof(bool))
            {
                bool value = currentValue is true;
                bool updated = EditorGUILayout.Toggle(label, value);
                return updated;
            }

            if (effectiveType == typeof(int))
            {
                int value = currentValue is int integer ? integer : 0;
                int updated = EditorGUILayout.IntField(label, value);
                return updated;
            }

            if (effectiveType == typeof(long))
            {
                long value = currentValue is long longValue ? longValue : 0L;
                long updated = EditorGUILayout.LongField(label, value);
                return updated;
            }

            if (effectiveType == typeof(float))
            {
                float value = currentValue is float floatValue ? floatValue : 0f;
                float updated = EditorGUILayout.FloatField(label, value);
                return updated;
            }

            if (effectiveType == typeof(double))
            {
                double value = currentValue is double doubleValue ? doubleValue : 0d;
                double updated = EditorGUILayout.DoubleField(label, value);
                return updated;
            }

            if (effectiveType == typeof(string))
            {
                string value = currentValue as string ?? string.Empty;
                string updated = EditorGUILayout.TextField(label, value);
                return updated;
            }

            if (effectiveType.IsEnum)
            {
                Enum enumValue = currentValue as Enum;
                if (enumValue == null && currentValue != null)
                {
                    enumValue = (Enum)Enum.ToObject(effectiveType, currentValue);
                }
                Enum updated = EditorGUILayout.EnumPopup(
                    label,
                    enumValue ?? (Enum)Enum.ToObject(effectiveType, 0)
                );
                return updated;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(effectiveType))
            {
                UnityEngine.Object value = currentValue as UnityEngine.Object;
                UnityEngine.Object updated = EditorGUILayout.ObjectField(
                    label,
                    value,
                    effectiveType,
                    true
                );
                return updated;
            }

            if (effectiveType == typeof(Vector2))
            {
                Vector2 value = currentValue is Vector2 vector ? vector : Vector2.zero;
                Vector2 updated = EditorGUILayout.Vector2Field(label, value);
                return updated;
            }

            if (effectiveType == typeof(Vector3))
            {
                Vector3 value = currentValue is Vector3 vector ? vector : Vector3.zero;
                Vector3 updated = EditorGUILayout.Vector3Field(label, value);
                return updated;
            }

            if (effectiveType == typeof(Vector4))
            {
                Vector4 value = currentValue is Vector4 vector ? vector : Vector4.zero;
                Vector4 updated = EditorGUILayout.Vector4Field(label.text, value);
                return updated;
            }

            if (effectiveType == typeof(Vector2Int))
            {
                Vector2Int value = currentValue is Vector2Int vector ? vector : Vector2Int.zero;
                Vector2Int updated = EditorGUILayout.Vector2IntField(label, value);
                return updated;
            }

            if (effectiveType == typeof(Vector3Int))
            {
                Vector3Int value = currentValue is Vector3Int vector ? vector : Vector3Int.zero;
                Vector3Int updated = EditorGUILayout.Vector3IntField(label, value);
                return updated;
            }

            if (effectiveType == typeof(Color))
            {
                Color value = currentValue is Color color ? color : Color.white;
                Color updated = EditorGUILayout.ColorField(label, value);
                return updated;
            }

            if (effectiveType == typeof(AnimationCurve))
            {
                AnimationCurve value =
                    currentValue as AnimationCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
                AnimationCurve updated = EditorGUILayout.CurveField(label, value);
                return updated;
            }

            if (effectiveType == typeof(Rect))
            {
                Rect value = currentValue is Rect rect ? rect : new Rect();
                Rect updated = EditorGUILayout.RectField(label, value);
                return updated;
            }

            if (effectiveType == typeof(Bounds))
            {
                Bounds value = currentValue is Bounds bounds ? bounds : new Bounds();
                Bounds updated = EditorGUILayout.BoundsField(label, value);
                return updated;
            }

            if (effectiveType == typeof(Quaternion))
            {
                Quaternion value = currentValue is Quaternion quaternion
                    ? quaternion
                    : Quaternion.identity;
                Vector4 vector = new(value.x, value.y, value.z, value.w);
                Vector4 updated = EditorGUILayout.Vector4Field(label.text, vector);
                Quaternion converted = new(updated.x, updated.y, updated.z, updated.w);
                return converted.normalized;
            }

            if (effectiveType.IsArray)
            {
                return DrawArrayField(label, effectiveType, states, parameterIndex);
            }

            return DrawJsonField(label, states, parameterIndex, currentValue);
        }

        private static object DrawArrayField(
            GUIContent label,
            Type arrayType,
            WButtonMethodState[] states,
            int parameterIndex
        )
        {
            WButtonParameterState primaryState = states[0].Parameters[parameterIndex];
            Array value = primaryState.CurrentValue as Array;
            Type elementType = arrayType.GetElementType() ?? typeof(object);
            int currentLength = value?.Length ?? 0;

            EditorGUILayout.LabelField(label, WButtonStyles.ArrayHeaderStyle);

            // Element fields open object pickers, and Unity unwinds those with GUIUtility.ExitGUI().
            using IndentLevelScope indentScope = IndentLevelScope.Indent();

            int updatedLength = EditorGUILayout.IntField(
                SizeContent,
                currentLength < 0 ? 0 : currentLength
            );
            if (updatedLength < 0)
            {
                updatedLength = 0;
            }

            if (updatedLength != currentLength)
            {
                value = Array.CreateInstance(elementType, updatedLength);
                primaryState.CurrentValue = value;
                for (int targetIndex = 1; targetIndex < states.Length; targetIndex++)
                {
                    WButtonParameterState targetState = states[targetIndex].Parameters[
                        parameterIndex
                    ];
                    targetState.CurrentValue = Array.CreateInstance(elementType, updatedLength);
                }
            }

            if (value != null)
            {
                for (int elementIndex = 0; elementIndex < value.Length; elementIndex++)
                {
                    object elementValue = value.GetValue(elementIndex);
                    GUIContent elementLabel = ElementLabelCache.GetOrAdd(
                        elementIndex,
                        static idx => new GUIContent($"Element {idx}")
                    );
                    EditorGUI.BeginChangeCheck();
                    object updatedElement = DrawElementField(
                        elementLabel,
                        elementType,
                        elementValue
                    );
                    if (EditorGUI.EndChangeCheck())
                    {
                        value.SetValue(updatedElement, elementIndex);
                        for (int targetIndex = 1; targetIndex < states.Length; targetIndex++)
                        {
                            WButtonParameterState targetState = states[targetIndex].Parameters[
                                parameterIndex
                            ];
                            if (
                                targetState.CurrentValue is not Array targetArray
                                || targetArray.Length != value.Length
                            )
                            {
                                targetArray = Array.CreateInstance(elementType, value.Length);
                                targetState.CurrentValue = targetArray;
                            }
                            targetArray.SetValue(
                                WButtonValueUtility.CloneValue(updatedElement),
                                elementIndex
                            );
                        }
                    }
                }
            }

            return primaryState.CurrentValue;
        }

        private static object DrawElementField(
            GUIContent label,
            Type elementType,
            object elementValue
        )
        {
            if (elementType == typeof(int))
            {
                int value = elementValue is int integer ? integer : 0;
                return EditorGUILayout.IntField(label, value);
            }

            if (elementType == typeof(float))
            {
                float value = elementValue is float single ? single : 0f;
                return EditorGUILayout.FloatField(label, value);
            }

            if (elementType == typeof(string))
            {
                string value = elementValue as string ?? string.Empty;
                return EditorGUILayout.TextField(label, value);
            }

            if (elementType.IsEnum)
            {
                Enum enumValue = elementValue as Enum;
                return EditorGUILayout.EnumPopup(
                    label,
                    enumValue ?? (Enum)Enum.ToObject(elementType, 0)
                );
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(elementType))
            {
                UnityEngine.Object value = elementValue as UnityEngine.Object;
                return EditorGUILayout.ObjectField(label, value, elementType, true);
            }

            return EditorGUILayout.TextField(
                label,
                elementValue != null ? elementValue.ToString() : string.Empty
            );
        }

        private static object DrawJsonField(
            GUIContent label,
            WButtonMethodState[] states,
            int parameterIndex,
            object currentValue
        )
        {
            string fallback = states[0].Parameters[parameterIndex].JsonFallback ?? string.Empty;
            string updated = EditorGUILayout.TextField(label, fallback);
            foreach (WButtonMethodState state in states)
            {
                WButtonParameterState parameterState = state.Parameters[parameterIndex];
                parameterState.JsonFallback = updated;
            }

            return currentValue;
        }

        private static void ApplyNewValue(
            WButtonMethodState[] states,
            int parameterIndex,
            object newValue
        )
        {
            foreach (WButtonMethodState inputState in states)
            {
                WButtonParameterState state = inputState.Parameters[parameterIndex];
                state.CurrentValue = WButtonValueUtility.CloneValue(newValue);
            }
        }

        private static void SetNull(WButtonMethodState[] states, int parameterIndex)
        {
            foreach (WButtonMethodState inputState in states)
            {
                WButtonParameterState state = inputState.Parameters[parameterIndex];
                state.CurrentValue = null;
            }
        }

        private static bool IsMixedValue(WButtonMethodState[] states, int parameterIndex)
        {
            if (states.Length <= 1)
            {
                return false;
            }

            object baseline = states[0].Parameters[parameterIndex].CurrentValue;
            for (int index = 1; index < states.Length; index++)
            {
                object value = states[index].Parameters[parameterIndex].CurrentValue;
                if (!WButtonValueUtility.ValuesEqual(baseline, value))
                {
                    return true;
                }
            }

            return false;
        }

        private static GUIContent GetParameterLabel(WButtonParameterMetadata metadata)
        {
            string cacheKey = metadata.Name;
            if (ParameterLabelCache.TryGetValue(cacheKey, out GUIContent cached))
            {
                return cached;
            }

            string nicified = ObjectNames.NicifyVariableName(metadata.Name);
            string tooltip = metadata.ParameterType.Name;
            GUIContent label = new(nicified, tooltip);
            ParameterLabelCache[cacheKey] = label;
            return label;
        }
    }
#endif
}
