// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
// NOTE: This is a code snippet for reference. Place inside the proper namespace and #if UNITY_EDITOR wrapper:
// namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
// {
// #if UNITY_EDITOR
//     using System;
//     using UnityEditor;
//     using UnityEngine;
//     ... (your code here)
// #endif
// }

[CustomPropertyDrawer(typeof(MySelectableAttribute))]
public sealed class MySelectablePropertyDrawer : PropertyDrawer
{
    private static readonly string[] Options = { "Option A", "Option B", "Option C" };
    private static readonly GUIContent ReusableButtonContent = new();

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property == null || property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label, true);
            return;
        }

        EditorGUI.BeginProperty(position, label, property);
        try
        {
            Rect fieldRect = EditorGUI.PrefixLabel(position, label);
            int currentIndex = Array.IndexOf(Options, property.stringValue);

            string displayValue = currentIndex >= 0 ? Options[currentIndex] : string.Empty;
            ReusableButtonContent.text = displayValue;

            if (EditorGUI.DropdownButton(fieldRect, ReusableButtonContent, FocusType.Keyboard))
            {
                SerializedObject serializedObject = property.serializedObject;
                string propertyPath = property.propertyPath;

                GenericMenu menu = new();
                for (int i = 0; i < Options.Length; i++)
                {
                    int capturedIndex = i;
                    bool isSelected = i == currentIndex;
                    menu.AddItem(new GUIContent(Options[i]), isSelected, () =>
                    {
                        serializedObject.Update();
                        SerializedProperty prop = serializedObject.FindProperty(propertyPath);
                        if (prop == null)
                            return;

                        Undo.RecordObjects(serializedObject.targetObjects, "Change Selection");
                        prop.stringValue = Options[capturedIndex];
                        serializedObject.ApplyModifiedProperties();
                    });
                }
                menu.DropDown(fieldRect);
            }
        }
        finally
        {
            EditorGUI.EndProperty();
        }
    }
}
