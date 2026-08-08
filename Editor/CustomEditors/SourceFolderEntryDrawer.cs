// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using Sprites;
    using UnityEngine;
    using UnityEditor;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Extensions;

    [CustomPropertyDrawer(typeof(SourceFolderEntry))]
    public sealed class SourceFolderEntryDrawer : PropertyDrawer
    {
        private const string HistoryToolName = nameof(SourceFolderEntryDrawer);
        internal static readonly Dictionary<string, bool> RegexesFoldoutState = new(
            StringComparer.Ordinal
        );
        internal static readonly Dictionary<string, bool> ExcludeRegexesFoldoutState = new(
            StringComparer.Ordinal
        );
        internal static readonly Dictionary<string, bool> ExcludePathPrefixesFoldoutState = new(
            StringComparer.Ordinal
        );

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property == null)
            {
                return;
            }

            label ??= GUIContent.none;

            EditorGUI.BeginProperty(position, label, property);
            Rect foldoutRect = new(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight
            );
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                using IndentLevelScope indentScope = IndentLevelScope.Indent();
                float currentY = foldoutRect.yMax + EditorGUIUtility.standardVerticalSpacing;
                float indentOffset = EditorGUI.indentLevel * 15f;
                float startX = position.x + indentOffset;
                float availableWidth = position.width - indentOffset;

                SerializedProperty folderPathProp = property.FindPropertyRelative(
                    nameof(SourceFolderEntry.folderPath)
                );

                Rect folderPathLabelRect = new(
                    startX,
                    currentY,
                    availableWidth,
                    EditorGUIUtility.singleLineHeight
                );
                EditorGUI.LabelField(folderPathLabelRect, "Folder Path", EditorStyles.boldLabel);
                currentY += folderPathLabelRect.height + EditorGUIUtility.standardVerticalSpacing;

                Rect pathFieldRect = new(
                    startX,
                    currentY,
                    availableWidth - 75,
                    EditorGUIUtility.singleLineHeight
                );
                Rect browseButtonRect = new(
                    pathFieldRect.xMax + 5,
                    currentY,
                    70,
                    EditorGUIUtility.singleLineHeight
                );

                EditorGUI.BeginChangeCheck();
                string newPath = EditorGUI.TextField(
                    pathFieldRect,
                    new GUIContent("Path:"),
                    folderPathProp.stringValue
                );
                if (EditorGUI.EndChangeCheck())
                {
                    folderPathProp.stringValue = newPath;
                }

                if (GUI.Button(browseButtonRect, "Browse..."))
                {
                    string initialBrowsePath = Application.dataPath; /* ... */
                    string selectedPathSys = Utils.EditorUi.OpenFolderPanel(
                        "Select Source Folder",
                        initialBrowsePath,
                        ""
                    );
                    if (!string.IsNullOrEmpty(selectedPathSys))
                    {
                        string processedPath = selectedPathSys.SanitizePath();
                        if (processedPath.StartsWith(Application.dataPath.SanitizePath()))
                        {
                            processedPath =
                                "Assets"
                                + processedPath.Substring(
                                    Application.dataPath.SanitizePath().Length
                                );
                        }
                        folderPathProp.stringValue = processedPath;
                        string contextKey = GetFolderPathFoldoutKey(folderPathProp);
                        PersistentDirectorySettings.Instance.RecordPath(
                            HistoryToolName,
                            contextKey,
                            processedPath
                        );
                        property.serializedObject.ApplyModifiedProperties();
                        GUI.FocusControl(null);
                    }
                }
                currentY += pathFieldRect.height + EditorGUIUtility.standardVerticalSpacing;
                string historyContextKey = GetFolderPathFoldoutKey(folderPathProp);

                Rect historyParentRect = new(
                    startX,
                    currentY,
                    availableWidth,
                    position.yMax - currentY
                );

                PersistentDirectoryGUI.DrawFrequentPathsWithEditorGUI(
                    historyParentRect,
                    ref currentY,
                    HistoryToolName,
                    historyContextKey,
                    chosenPath =>
                    {
                        folderPathProp.stringValue = chosenPath;
                        PersistentDirectorySettings.Instance.RecordPath(
                            HistoryToolName,
                            historyContextKey,
                            chosenPath
                        );
                        property.serializedObject.ApplyModifiedProperties();
                        GUI.FocusControl(null);
                    }
                );

                currentY += EditorGUIUtility.standardVerticalSpacing;

                SerializedProperty modeProp = property.FindPropertyRelative(
                    nameof(SourceFolderEntry.selectionMode)
                );
                SpriteSelectionMode modeValue = (SpriteSelectionMode)modeProp.intValue;
                Rect selectionMode = new(
                    startX,
                    currentY,
                    availableWidth,
                    EditorGUIUtility.singleLineHeight
                );
                EditorGUI.BeginChangeCheck();
                modeValue = (SpriteSelectionMode)
                    EditorGUI.EnumFlagsField(
                        selectionMode,
                        new GUIContent("Selection Mode"),
                        modeValue
                    );
                if (EditorGUI.EndChangeCheck())
                {
                    modeProp.intValue = (int)modeValue;
                }
                currentY +=
                    EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

                bool useRegex = (modeValue & SpriteSelectionMode.Regex) != 0;
                bool useLabels = (modeValue & SpriteSelectionMode.Labels) != 0;

                if (useRegex)
                {
                    Rect regexFoldoutLabelRect = new(
                        startX,
                        currentY,
                        availableWidth,
                        EditorGUIUtility.singleLineHeight
                    );

                    string regexesFoldoutKey = GetRegexFoldoutKey(property);
                    RegexesFoldoutState.TryAdd(regexesFoldoutKey, true);
                    RegexesFoldoutState[regexesFoldoutKey] = EditorGUI.Foldout(
                        regexFoldoutLabelRect,
                        RegexesFoldoutState[regexesFoldoutKey],
                        "Regexes (AND logic)",
                        true
                    );
                    currentY +=
                        regexFoldoutLabelRect.height + EditorGUIUtility.standardVerticalSpacing;

                    if (RegexesFoldoutState[regexesFoldoutKey])
                    {
                        SerializedProperty regexesProp = property.FindPropertyRelative(
                            nameof(SourceFolderEntry.regexes)
                        );
                        float regexStartX = startX + 15f;
                        float regexWidth = availableWidth - 15f;

                        for (int i = 0; i < regexesProp.arraySize; i++)
                        {
                            SerializedProperty elemProp = regexesProp.GetArrayElementAtIndex(i);
                            Rect fieldRect = new(
                                regexStartX,
                                currentY,
                                regexWidth - 25f,
                                EditorGUIUtility.singleLineHeight
                            );
                            EditorGUI.BeginChangeCheck();
                            string newVal = EditorGUI.TextField(
                                fieldRect,
                                $"Regex {i}:",
                                elemProp.stringValue
                            );
                            if (EditorGUI.EndChangeCheck())
                            {
                                elemProp.stringValue = newVal;
                            }

                            Rect remRect = new(
                                fieldRect.xMax + 4f,
                                currentY,
                                25f,
                                EditorGUIUtility.singleLineHeight
                            );
                            if (GUI.Button(remRect, "–"))
                            {
                                regexesProp.DeleteArrayElementAtIndex(i);
                                property.serializedObject.ApplyModifiedProperties();
                            }

                            currentY +=
                                EditorGUIUtility.singleLineHeight
                                + EditorGUIUtility.standardVerticalSpacing;
                        }

                        Rect addRect = new(
                            regexStartX,
                            currentY,
                            regexWidth,
                            EditorGUIUtility.singleLineHeight
                        );

                        if (GUI.Button(addRect, "+ Add Regex"))
                        {
                            SerializedProperty newRegex = regexesProp.AppendArrayElement();
                            newRegex.stringValue = string.Empty;
                            property.serializedObject.ApplyModifiedProperties();
                        }
                        currentY +=
                            EditorGUIUtility.singleLineHeight
                            + EditorGUIUtility.standardVerticalSpacing;
                    }

                    // Exclude Regexes
                    Rect excludeRegexFoldoutLabelRect = new(
                        startX,
                        currentY,
                        availableWidth,
                        EditorGUIUtility.singleLineHeight
                    );
                    string excludeRegexesFoldoutKey = GetExcludeRegexFoldoutKey(property);
                    ExcludeRegexesFoldoutState.TryAdd(excludeRegexesFoldoutKey, false);
                    ExcludeRegexesFoldoutState[excludeRegexesFoldoutKey] = EditorGUI.Foldout(
                        excludeRegexFoldoutLabelRect,
                        ExcludeRegexesFoldoutState[excludeRegexesFoldoutKey],
                        "Exclude Regexes (OR logic)",
                        true
                    );
                    currentY +=
                        excludeRegexFoldoutLabelRect.height
                        + EditorGUIUtility.standardVerticalSpacing;

                    if (ExcludeRegexesFoldoutState[excludeRegexesFoldoutKey])
                    {
                        SerializedProperty excludeRegexesProp = property.FindPropertyRelative(
                            nameof(SourceFolderEntry.excludeRegexes)
                        );
                        float exRegexStartX = startX + 15f;
                        float exRegexWidth = availableWidth - 15f;
                        for (int i = 0; i < excludeRegexesProp.arraySize; i++)
                        {
                            SerializedProperty elemProp = excludeRegexesProp.GetArrayElementAtIndex(
                                i
                            );
                            Rect fieldRect = new(
                                exRegexStartX,
                                currentY,
                                exRegexWidth - 25f,
                                EditorGUIUtility.singleLineHeight
                            );
                            EditorGUI.BeginChangeCheck();
                            string newVal = EditorGUI.TextField(
                                fieldRect,
                                $"Exclude Regex {i}:",
                                elemProp.stringValue
                            );
                            if (EditorGUI.EndChangeCheck())
                            {
                                elemProp.stringValue = newVal;
                            }
                            Rect remRect = new(
                                fieldRect.xMax + 4f,
                                currentY,
                                25f,
                                EditorGUIUtility.singleLineHeight
                            );
                            if (GUI.Button(remRect, "–"))
                            {
                                excludeRegexesProp.DeleteArrayElementAtIndex(i);
                                property.serializedObject.ApplyModifiedProperties();
                            }
                            currentY +=
                                EditorGUIUtility.singleLineHeight
                                + EditorGUIUtility.standardVerticalSpacing;
                        }

                        Rect addExRect = new(
                            exRegexStartX,
                            currentY,
                            exRegexWidth,
                            EditorGUIUtility.singleLineHeight
                        );
                        if (GUI.Button(addExRect, "+ Add Exclude Regex"))
                        {
                            SerializedProperty newRegex = excludeRegexesProp.AppendArrayElement();
                            newRegex.stringValue = string.Empty;
                            property.serializedObject.ApplyModifiedProperties();
                        }
                        currentY +=
                            EditorGUIUtility.singleLineHeight
                            + EditorGUIUtility.standardVerticalSpacing;
                    }

                    // Exclude Path Prefixes
                    Rect excludePathFoldoutLabelRect = new(
                        startX,
                        currentY,
                        availableWidth,
                        EditorGUIUtility.singleLineHeight
                    );
                    string exPathFoldoutKey = GetExcludePathFoldoutKey(property);
                    ExcludePathPrefixesFoldoutState.TryAdd(exPathFoldoutKey, false);
                    ExcludePathPrefixesFoldoutState[exPathFoldoutKey] = EditorGUI.Foldout(
                        excludePathFoldoutLabelRect,
                        ExcludePathPrefixesFoldoutState[exPathFoldoutKey],
                        "Exclude Path Prefixes",
                        true
                    );
                    currentY +=
                        excludePathFoldoutLabelRect.height
                        + EditorGUIUtility.standardVerticalSpacing;

                    if (ExcludePathPrefixesFoldoutState[exPathFoldoutKey])
                    {
                        SerializedProperty exPathsProp = property.FindPropertyRelative(
                            nameof(SourceFolderEntry.excludePathPrefixes)
                        );
                        float exPathStartX = startX + 15f;
                        float exPathWidth = availableWidth - 15f;
                        for (int i = 0; i < exPathsProp.arraySize; i++)
                        {
                            SerializedProperty elemProp = exPathsProp.GetArrayElementAtIndex(i);
                            Rect fieldRect = new(
                                exPathStartX,
                                currentY,
                                exPathWidth - 25f,
                                EditorGUIUtility.singleLineHeight
                            );
                            EditorGUI.BeginChangeCheck();
                            string newVal = EditorGUI.TextField(
                                fieldRect,
                                $"Exclude Path {i}:",
                                elemProp.stringValue
                            );
                            if (EditorGUI.EndChangeCheck())
                            {
                                elemProp.stringValue = newVal;
                            }
                            Rect remRect = new(
                                fieldRect.xMax + 4f,
                                currentY,
                                25f,
                                EditorGUIUtility.singleLineHeight
                            );
                            if (GUI.Button(remRect, "–"))
                            {
                                exPathsProp.DeleteArrayElementAtIndex(i);
                                property.serializedObject.ApplyModifiedProperties();
                            }
                            currentY +=
                                EditorGUIUtility.singleLineHeight
                                + EditorGUIUtility.standardVerticalSpacing;
                        }

                        Rect addExPathRect = new(
                            exPathStartX,
                            currentY,
                            exPathWidth,
                            EditorGUIUtility.singleLineHeight
                        );
                        if (GUI.Button(addExPathRect, "+ Add Exclude Path"))
                        {
                            SerializedProperty updatedPath = exPathsProp.AppendArrayElement();
                            updatedPath.stringValue = string.Empty;
                            property.serializedObject.ApplyModifiedProperties();
                        }
                        currentY +=
                            EditorGUIUtility.singleLineHeight
                            + EditorGUIUtility.standardVerticalSpacing;
                    }
                }

                if (useRegex && useLabels)
                {
                    SerializedProperty booleanProp = property.FindPropertyRelative(
                        nameof(SourceFolderEntry.regexAndTagLogic)
                    );
                    currentY +=
                        EditorGUIUtility.singleLineHeight
                        + EditorGUIUtility.standardVerticalSpacing;
                    EditorGUI.PropertyField(
                        new Rect(
                            startX,
                            currentY,
                            availableWidth,
                            EditorGUIUtility.singleLineHeight
                        ),
                        booleanProp,
                        new GUIContent("Regex & Tags Logic")
                    );
                    currentY +=
                        EditorGUIUtility.singleLineHeight
                        + EditorGUIUtility.standardVerticalSpacing;
                }

                if (useLabels)
                {
                    SerializedProperty labelModeProp = property.FindPropertyRelative(
                        nameof(SourceFolderEntry.labelSelectionMode)
                    );
                    Rect rectLabelMode = new(
                        startX,
                        currentY,
                        availableWidth,
                        EditorGUIUtility.singleLineHeight
                    );
                    EditorGUI.PropertyField(
                        rectLabelMode,
                        labelModeProp,
                        new GUIContent("Label Selection Mode")
                    );
                    currentY +=
                        EditorGUIUtility.singleLineHeight
                        + EditorGUIUtility.standardVerticalSpacing;

                    SerializedProperty labelsProp = property.FindPropertyRelative(
                        nameof(SourceFolderEntry.labels)
                    );
                    float labelsHeight = EditorGUI.GetPropertyHeight(labelsProp, true);

                    Rect rectLabels = new(startX, currentY, availableWidth, labelsHeight);
                    EditorGUI.PropertyField(rectLabels, labelsProp, new GUIContent("Labels"), true);
                    currentY += labelsHeight + EditorGUIUtility.standardVerticalSpacing;

                    // Exclude labels
                    SerializedProperty exLabelModeProp = property.FindPropertyRelative(
                        nameof(SourceFolderEntry.excludeLabelSelectionMode)
                    );
                    Rect exLabelModeRect = new(
                        startX,
                        currentY,
                        availableWidth,
                        EditorGUIUtility.singleLineHeight
                    );
                    EditorGUI.PropertyField(
                        exLabelModeRect,
                        exLabelModeProp,
                        new GUIContent("Exclude Label Mode")
                    );
                    currentY +=
                        EditorGUIUtility.singleLineHeight
                        + EditorGUIUtility.standardVerticalSpacing;

                    SerializedProperty exLabelsProp = property.FindPropertyRelative(
                        nameof(SourceFolderEntry.excludeLabels)
                    );
                    float exLabelsHeight = EditorGUI.GetPropertyHeight(exLabelsProp, true);
                    Rect exLabelsRect = new(startX, currentY, availableWidth, exLabelsHeight);
                    EditorGUI.PropertyField(
                        exLabelsRect,
                        exLabelsProp,
                        new GUIContent("Exclude Labels"),
                        true
                    );
                    currentY += exLabelsHeight + EditorGUIUtility.standardVerticalSpacing;
                }
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property == null)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            height += EditorGUIUtility.standardVerticalSpacing;
            height += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            height += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            SerializedProperty folderPathProp = property.FindPropertyRelative(
                nameof(SourceFolderEntry.folderPath)
            );

            string historyContextKey = GetFolderPathFoldoutKey(folderPathProp);

            height += PersistentDirectoryGUI.GetDrawFrequentPathsHeightEditorGUI(
                HistoryToolName,
                historyContextKey
            );

            height += EditorGUIUtility.standardVerticalSpacing;
            height += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            SerializedProperty modeProp = property.FindPropertyRelative(
                nameof(SourceFolderEntry.selectionMode)
            );
            SpriteSelectionMode modeValue = (SpriteSelectionMode)modeProp.intValue;
            bool useRegex = modeValue.HasFlagNoAlloc(SpriteSelectionMode.Regex);
            bool useLabels = modeValue.HasFlagNoAlloc(SpriteSelectionMode.Labels);

            if (useRegex)
            {
                string regexesFoldoutKey = GetRegexFoldoutKey(property);
                bool isRegexesExpanded = RegexesFoldoutState.GetValueOrDefault(
                    regexesFoldoutKey,
                    true
                );
                if (isRegexesExpanded)
                {
                    SerializedProperty regexesProp = property.FindPropertyRelative(
                        nameof(SourceFolderEntry.regexes)
                    );
                    height +=
                        (1 + regexesProp.arraySize)
                        * (
                            EditorGUIUtility.singleLineHeight
                            + EditorGUIUtility.standardVerticalSpacing
                        );
                }

                height +=
                    EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

                // Exclude regexes foldout
                string exRegexFoldoutKey = GetExcludeRegexFoldoutKey(property);
                bool isExRegexExpanded = ExcludeRegexesFoldoutState.GetValueOrDefault(
                    exRegexFoldoutKey,
                    false
                );
                if (isExRegexExpanded)
                {
                    SerializedProperty exRegexesProp = property.FindPropertyRelative(
                        nameof(SourceFolderEntry.excludeRegexes)
                    );
                    height +=
                        (1 + exRegexesProp.arraySize)
                        * (
                            EditorGUIUtility.singleLineHeight
                            + EditorGUIUtility.standardVerticalSpacing
                        );
                }
                height +=
                    EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

                // Exclude path prefixes foldout
                string exPathFoldoutKey = GetExcludePathFoldoutKey(property);
                bool isExPathExpanded = ExcludePathPrefixesFoldoutState.GetValueOrDefault(
                    exPathFoldoutKey,
                    false
                );
                if (isExPathExpanded)
                {
                    SerializedProperty exPathsProp = property.FindPropertyRelative(
                        nameof(SourceFolderEntry.excludePathPrefixes)
                    );
                    height +=
                        (1 + exPathsProp.arraySize)
                        * (
                            EditorGUIUtility.singleLineHeight
                            + EditorGUIUtility.standardVerticalSpacing
                        );
                }
                height +=
                    EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            if (useRegex && useLabels)
            {
                height +=
                    EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            if (useLabels)
            {
                // 1) Draw the “Label Selection Mode” line (dropdown)
                height += EditorGUIUtility.singleLineHeight;
                height += EditorGUIUtility.standardVerticalSpacing;

                // 2) Now figure out how tall “labels” really is. Let Unity handle foldout‐vs‐expanded.
                SerializedProperty labelsProp = property.FindPropertyRelative(
                    nameof(SourceFolderEntry.labels)
                );

                //    Passing `true` tells Unity: “Include children if expanded, or just header if collapsed.”
                float labelsFullHeight = EditorGUI.GetPropertyHeight(labelsProp, true);

                height += labelsFullHeight;
                height += EditorGUIUtility.standardVerticalSpacing;

                // Exclude label section (mode + list)
                height += EditorGUIUtility.singleLineHeight; // exclude mode
                height += EditorGUIUtility.standardVerticalSpacing;
                SerializedProperty exLabelsProp = property.FindPropertyRelative(
                    nameof(SourceFolderEntry.excludeLabels)
                );
                height += EditorGUI.GetPropertyHeight(exLabelsProp, true);
                height += EditorGUIUtility.standardVerticalSpacing;
            }

            height += EditorGUIUtility.standardVerticalSpacing;
            return height;
        }

        private static string GetHistoryContextKey(SerializedProperty property)
        {
            return (
                    property.serializedObject.targetObject != null
                        ? property.serializedObject.targetObject.name
                        : "NULL"
                ) + ".DefaultHistoryContext";
        }

        private static string GetFolderPathFoldoutKey(SerializedProperty property)
        {
            return (
                    property.serializedObject.targetObject != null
                        ? property.serializedObject.targetObject.name
                        : "NULL"
                ) + ".folderList";
        }

        internal static string GetRegexFoldoutKey(SerializedProperty property)
        {
            return (
                    property.serializedObject.targetObject != null
                        ? property.serializedObject.targetObject.name
                        : "NULL"
                )
                + property.propertyPath
                + ".regexesList";
        }

        internal static string GetExcludeRegexFoldoutKey(SerializedProperty property)
        {
            return (
                    property.serializedObject.targetObject != null
                        ? property.serializedObject.targetObject.name
                        : "NULL"
                )
                + property.propertyPath
                + ".excludeRegexesList";
        }

        internal static string GetExcludePathFoldoutKey(SerializedProperty property)
        {
            return (
                    property.serializedObject.targetObject != null
                        ? property.serializedObject.targetObject.name
                        : "NULL"
                )
                + property.propertyPath
                + ".excludePathPrefixesList";
        }
    }
#endif
}
