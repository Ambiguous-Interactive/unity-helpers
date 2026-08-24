// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Draws the parameter editor for a single AnimationEvent row.
    /// </summary>
    internal static class AnimationEventParameterRenderer
    {
        public static void Render(AnimationEventItem item, Action<string> recordUndo)
        {
            if (item == null || item.selectedMethod == null)
            {
                return;
            }

            AnimationEvent animEvent = item.animationEvent;
            ParameterInfo[] parameters = item.selectedMethod.GetParameters();
            if (parameters.Length != 1)
            {
                return;
            }

            using EditorGUI.IndentLevelScope indent = new();

            Type parameterType = parameters[0].ParameterType;
            if (parameterType == typeof(int))
            {
                DrawIntField(animEvent, recordUndo);
                return;
            }

            if (parameterType == typeof(float))
            {
                DrawFloatField(animEvent, recordUndo);
                return;
            }

            if (parameterType == typeof(string))
            {
                DrawStringField(animEvent, recordUndo);
                return;
            }

            if (parameterType == typeof(UnityEngine.Object))
            {
                DrawObjectField(animEvent, recordUndo);
                return;
            }

            if (parameterType.BaseType == typeof(Enum))
            {
                DrawEnumField(parameterType, item, recordUndo);
            }
        }

        private static void DrawIntField(AnimationEvent animEvent, Action<string> recordUndo)
        {
            EditorGUI.BeginChangeCheck();
            int newValue = EditorGUILayout.IntField("IntParameter", animEvent.intParameter);
            if (EditorGUI.EndChangeCheck())
            {
                recordUndo?.Invoke("Change Animation Event Parameter");
                animEvent.intParameter = newValue;
            }
        }

        private static void DrawFloatField(AnimationEvent animEvent, Action<string> recordUndo)
        {
            EditorGUI.BeginChangeCheck();
            float newValue = EditorGUILayout.FloatField("FloatParameter", animEvent.floatParameter);
            if (EditorGUI.EndChangeCheck())
            {
                recordUndo?.Invoke("Change Animation Event Parameter");
                animEvent.floatParameter = newValue;
            }
        }

        private static void DrawStringField(AnimationEvent animEvent, Action<string> recordUndo)
        {
            EditorGUI.BeginChangeCheck();
            string newValue = EditorGUILayout.TextField(
                "StringParameter",
                animEvent.stringParameter
            );
            if (EditorGUI.EndChangeCheck())
            {
                recordUndo?.Invoke("Change Animation Event Parameter");
                animEvent.stringParameter = newValue;
            }
        }

        private static void DrawObjectField(AnimationEvent animEvent, Action<string> recordUndo)
        {
            EditorGUI.BeginChangeCheck();
            UnityEngine.Object newValue = EditorGUILayout.ObjectField(
                "ObjectReferenceParameter",
                animEvent.objectReferenceParameter,
                typeof(UnityEngine.Object),
                true
            );
            if (EditorGUI.EndChangeCheck())
            {
                recordUndo?.Invoke("Change Animation Event Parameter");
                animEvent.objectReferenceParameter = newValue;
            }
        }

        private static void DrawEnumField(
            Type parameterType,
            AnimationEventItem item,
            Action<string> recordUndo
        )
        {
            string[] enumNames = Enum.GetNames(parameterType);
            string currentName = Enum.GetName(parameterType, item.animationEvent.intParameter);
            int currentIndex = Array.IndexOf(enumNames, currentName);

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup($"{parameterType.Name}", currentIndex, enumNames);
            if (EditorGUI.EndChangeCheck() && 0 <= newIndex)
            {
                recordUndo?.Invoke("Change Animation Event Parameter");
                item.animationEvent.intParameter = (int)
                    Enum.Parse(parameterType, enumNames[newIndex]);
            }

            item.overrideEnumValues = EditorGUILayout.Toggle("Override", item.overrideEnumValues);
            if (!item.overrideEnumValues)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            int overrideValue = EditorGUILayout.IntField(
                "IntParameter",
                item.animationEvent.intParameter
            );
            if (EditorGUI.EndChangeCheck())
            {
                recordUndo?.Invoke("Change Animation Event Parameter");
                item.animationEvent.intParameter = overrideValue;
            }
        }
    }
#endif
}
