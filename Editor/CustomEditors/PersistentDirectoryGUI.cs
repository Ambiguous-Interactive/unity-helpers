// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using UnityEngine;
    using UnityEditor;
    using System.Collections.Generic;
    using System.IO;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Extensions;
    using Object = UnityEngine.Object;

    public static class PersistentDirectoryGUI
    {
        private static readonly Dictionary<string, bool> ContextFoldoutStates = new(
            StringComparer.Ordinal
        );

        public static float DrawFrequentPathsWithEditorGUI(
            Rect parentRect,
            ref float currentY,
            string toolName,
            string contextKey,
            Action<string> onPathClickedFromHistory,
            bool allowExpansion = true,
            int topN = 5,
            string listLabel = "History:"
        )
        {
            if (onPathClickedFromHistory == null)
            {
                Debug.LogError(
                    "PersistentDirectoryGUI.DrawFrequentPathsWithEditorGUI: onPathClickedFromHistory callback cannot be null."
                );
                return 0f;
            }

            if (PersistentDirectorySettings.Instance == null)
            {
                return 0f;
            }

            float startY = currentY;
            float availableWidth = parentRect.width;
            float startX = parentRect.x;

            DirectoryUsageData[] topPaths = PersistentDirectorySettings.Instance.GetPaths(
                toolName,
                contextKey,
                true,
                topN
            );

            if (topPaths.Length <= 0)
            {
                return currentY - startY;
            }

            Rect historyLabelRect = new(
                startX,
                currentY,
                availableWidth,
                EditorGUIUtility.singleLineHeight
            );
            EditorGUI.LabelField(historyLabelRect, listLabel, EditorStyles.miniBoldLabel);
            currentY += historyLabelRect.height;

            foreach (DirectoryUsageData dirData in topPaths)
            {
                Rect historyButtonRect = new(
                    startX + 15f,
                    currentY,
                    availableWidth - 15f,
                    EditorGUIUtility.singleLineHeight
                );
                if (
                    GUI.Button(
                        historyButtonRect,
                        new GUIContent($"({dirData.count}) {dirData.path}", dirData.path),
                        EditorStyles.miniButtonLeft
                    )
                )
                {
                    onPathClickedFromHistory.Invoke(dirData.path);
                }
                currentY += historyButtonRect.height;
            }

            if (allowExpansion)
            {
                string foldoutKey = $"{toolName}/{contextKey}_AllPathsHistory_EditorGUI";
                if (!ContextFoldoutStates.TryGetValue(foldoutKey, out bool expanded))
                {
                    expanded = false;
                    ContextFoldoutStates.Add(foldoutKey, expanded);
                }

                DirectoryUsageData[] allPaths = PersistentDirectorySettings.Instance.GetPaths(
                    toolName,
                    contextKey,
                    false,
                    0
                );
                if (topN < allPaths.Length)
                {
                    Rect expansionFoldoutRect = new(
                        startX + 15f,
                        currentY,
                        availableWidth - 15f,
                        EditorGUIUtility.singleLineHeight
                    );
                    expanded = EditorGUI.Foldout(
                        expansionFoldoutRect,
                        expanded,
                        "Show All History (" + allPaths.Length + ")",
                        true,
                        EditorStyles.foldout
                    );
                    ContextFoldoutStates[foldoutKey] = expanded;
                    currentY += expansionFoldoutRect.height;

                    if (expanded)
                    {
                        int remaining = allPaths.Length - topN;
                        if (0 < remaining)
                        {
                            for (int idx = topN; idx < allPaths.Length; idx++)
                            {
                                DirectoryUsageData dirData = allPaths[idx];
                                Rect moreHistoryButtonRect = new(
                                    startX + 30f,
                                    currentY,
                                    availableWidth - 30f,
                                    EditorGUIUtility.singleLineHeight
                                );
                                if (
                                    GUI.Button(
                                        moreHistoryButtonRect,
                                        new GUIContent(
                                            $"({dirData.count}) {dirData.path}",
                                            dirData.path
                                        ),
                                        EditorStyles.miniButtonLeft
                                    )
                                )
                                {
                                    onPathClickedFromHistory.Invoke(dirData.path);
                                }
                                currentY += moreHistoryButtonRect.height;
                            }
                        }
                        else
                        {
                            Rect noMorePathsLabelRect = new(
                                startX + 30f,
                                currentY,
                                availableWidth - 30f,
                                EditorGUIUtility.singleLineHeight
                            );
                            EditorGUI.LabelField(
                                noMorePathsLabelRect,
                                "All paths already displayed.",
                                EditorStyles.centeredGreyMiniLabel
                            );
                            currentY += noMorePathsLabelRect.height;
                        }
                    }
                }
            }

            currentY += EditorGUIUtility.standardVerticalSpacing;
            return currentY - startY;
        }

        public static float GetDrawFrequentPathsHeightEditorGUI(
            string toolName,
            string contextKey,
            bool allowExpansion = true,
            int topN = 5
        )
        {
            if (PersistentDirectorySettings.Instance == null)
            {
                return 0f;
            }

            float height = 0f;
            DirectoryUsageData[] topPaths = PersistentDirectorySettings.Instance.GetPaths(
                toolName,
                contextKey,
                true,
                topN
            );

            if (topPaths.Length <= 0)
            {
                return height;
            }

            height +=
                (topPaths.Length + 1)
                * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
            if (allowExpansion)
            {
                DirectoryUsageData[] allPaths = PersistentDirectorySettings.Instance.GetPaths(
                    toolName,
                    contextKey,
                    false,
                    0
                );
                if (topN < allPaths.Length)
                {
                    height +=
                        EditorGUIUtility.singleLineHeight
                        + EditorGUIUtility.standardVerticalSpacing;

                    string foldoutKey = $"{toolName}/{contextKey}_AllPathsHistory_EditorGUI";
                    bool isExpanded = ContextFoldoutStates.GetValueOrDefault(foldoutKey, false);
                    if (isExpanded)
                    {
                        int remaining = allPaths.Length - topN;
                        if (remaining < 1)
                        {
                            remaining = 1;
                        }
                        height +=
                            remaining
                            * (
                                EditorGUIUtility.singleLineHeight
                                + EditorGUIUtility.standardVerticalSpacing
                            );
                    }
                }
            }
            return height;
        }

        public static float GetDrawFrequentPathsHeight(
            string toolName,
            string contextKey,
            bool allowExpansion = true,
            int topN = 5
        )
        {
            if (PersistentDirectorySettings.Instance == null)
            {
                return 0f;
            }

            float height = 0f;
            DirectoryUsageData[] topPaths = PersistentDirectorySettings.Instance.GetPaths(
                toolName,
                contextKey,
                true,
                topN
            );

            if (topPaths.Length <= 0)
            {
                return height;
            }

            height +=
                (1 + topPaths.Length)
                * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);

            if (allowExpansion)
            {
                DirectoryUsageData[] allPaths = PersistentDirectorySettings.Instance.GetPaths(
                    toolName,
                    contextKey,
                    false,
                    0
                );
                if (topN < allPaths.Length)
                {
                    height +=
                        EditorGUIUtility.singleLineHeight
                        + EditorGUIUtility.standardVerticalSpacing;
                    string foldoutKey = $"{toolName}/{contextKey}_AllPathsHistory";
                    bool isExpanded = ContextFoldoutStates.GetValueOrDefault(foldoutKey, false);
                    if (isExpanded)
                    {
                        int remaining = allPaths.Length - topN;
                        if (remaining < 1)
                        {
                            remaining = 1;
                        }
                        height +=
                            remaining
                            * (
                                EditorGUIUtility.singleLineHeight
                                + EditorGUIUtility.standardVerticalSpacing
                            );
                    }
                }
            }

            if (0 < height)
            {
                height += EditorGUIUtility.standardVerticalSpacing * 2;
            }
            return height;
        }

        public static float GetPathSelectorHeight(
            string toolName,
            string contextKeyForHistory,
            bool displayFrequentPaths = true
        )
        {
            float height = 0f;

            height += EditorGUIUtility.singleLineHeight;

            if (!displayFrequentPaths)
            {
                return height;
            }

            float historyHeight = GetDrawFrequentPathsHeight(toolName, contextKeyForHistory);
            if (0 < historyHeight)
            {
                height += EditorGUIUtility.standardVerticalSpacing;
                height += historyHeight;
            }
            return height;
        }

        public static float GetPathSelectorStringHeight(
            SerializedProperty propertyForContext,
            string toolName
        )
        {
            if (propertyForContext is not { propertyType: SerializedPropertyType.String })
            {
                return EditorGUIUtility.singleLineHeight;
            }

            float height = 0f;
            height += EditorGUIUtility.singleLineHeight;
            height += EditorGUIUtility.standardVerticalSpacing;
            string internalContextKey =
                $"{propertyForContext.serializedObject.targetObject.GetType().Name}_{propertyForContext.propertyPath}";
            height += GetPathSelectorHeight(toolName, internalContextKey);
            return height;
        }

        public static void PathSelectorString(
            SerializedProperty property,
            string toolName,
            string label,
            GUIContent content
        )
        {
            if (property == null)
            {
                EditorGUILayout.LabelField(
                    $"Error: Property is null in {nameof(PathSelectorString)}.",
                    EditorStyles.miniBoldLabel
                );
                return;
            }
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUILayout.LabelField(
                    $"Error: Property '{property.displayName}' is not a string.",
                    EditorStyles.miniBoldLabel
                );
                return;
            }

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            PathSelector(
                content,
                property.stringValue,
                toolName,
                property.name,
                chosenPath =>
                {
                    property.stringValue = chosenPath;
                    property.serializedObject.ApplyModifiedProperties();
                },
                "Select Directory"
            );
        }

        public static void PathSelectorObject(
            SerializedProperty property,
            string toolName,
            string label,
            GUIContent content
        )
        {
            if (property == null)
            {
                EditorGUILayout.LabelField(
                    $"Error: Property is null in {nameof(PathSelectorObject)}.",
                    EditorStyles.miniBoldLabel
                );
                return;
            }
            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                EditorGUILayout.LabelField(
                    $"Error: Property '{property.displayName}' is not an Object Reference.",
                    EditorStyles.miniBoldLabel
                );
                return;
            }
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            PathSelector(
                content,
                property.objectReferenceValue == null
                    ? string.Empty
                    : AssetDatabase.GetAssetPath(property.objectReferenceValue),
                toolName,
                property.name,
                chosenPath =>
                {
                    if (!string.IsNullOrWhiteSpace(chosenPath))
                    {
                        Object defaultFolder = AssetDatabase.LoadAssetAtPath<Object>(chosenPath);
                        property.objectReferenceValue = defaultFolder;
                    }
                    else
                    {
                        property.objectReferenceValue = null;
                    }
                    property.serializedObject.ApplyModifiedProperties();
                },
                "Select Directory"
            );
        }

        public static void PathSelectorStringArray(SerializedProperty listProp, string toolName)
        {
            if (listProp == null)
            {
                EditorGUILayout.LabelField(
                    $"Error: Property is null in {nameof(PathSelectorStringArray)}.",
                    EditorStyles.miniBoldLabel
                );
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Number of Directories:", GUILayout.Width(150));
                int currentSize = listProp.arraySize;
                int newSize = EditorGUILayout.IntField(currentSize, GUILayout.Width(50));
                if (newSize != currentSize)
                {
                    newSize = Mathf.Max(0, newSize);
                    listProp.arraySize = newSize;
                }
            }

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < listProp.arraySize; i++)
                {
                    SerializedProperty elementProp = listProp.GetArrayElementAtIndex(i);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        int currentIndex = i;
                        PathSelector(
                            new GUIContent($"Path {i + 1}"),
                            elementProp.stringValue,
                            toolName,
                            listProp.name,
                            chosenPath =>
                            {
                                SerializedProperty currentElementProp =
                                    listProp.GetArrayElementAtIndex(currentIndex);
                                currentElementProp.stringValue = chosenPath;
                                currentElementProp.serializedObject.ApplyModifiedProperties();
                            },
                            $"Select Directory {i + 1}"
                        );

                        if (GUILayout.Button("-", GUILayout.Width(25)))
                        {
                            elementProp.stringValue = string.Empty;
                            listProp.DeleteArrayElementAtIndex(i);
                            break;
                        }
                    }
                    EditorGUILayout.Space(3);
                }
            }

            if (GUILayout.Button("Add New Directory Path", GUILayout.Width(200)))
            {
                SerializedProperty newElement = listProp.AppendArrayElement();
                newElement.stringValue = string.Empty;
            }
        }

        public static void PathSelectorObjectArray(SerializedProperty listProp, string toolName)
        {
            if (listProp == null)
            {
                EditorGUILayout.LabelField(
                    $"Error: Property is null in {nameof(PathSelectorObjectArray)}.",
                    EditorStyles.miniBoldLabel
                );
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Number of Directories:", GUILayout.Width(150));
                int currentSize = listProp.arraySize;
                int newSize = EditorGUILayout.IntField(currentSize, GUILayout.Width(50));
                if (newSize != currentSize)
                {
                    newSize = Mathf.Max(0, newSize);
                    listProp.arraySize = newSize;
                }
            }

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < listProp.arraySize; i++)
                {
                    SerializedProperty elementProp = listProp.GetArrayElementAtIndex(i);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        int currentIndex = i;
                        PathSelector(
                            new GUIContent($"Path {i + 1}"),
                            elementProp.objectReferenceValue == null
                                ? string.Empty
                                : AssetDatabase.GetAssetPath(elementProp.objectReferenceValue),
                            toolName,
                            listProp.name,
                            chosenPath =>
                            {
                                SerializedProperty currentElementProp =
                                    listProp.GetArrayElementAtIndex(currentIndex);
                                if (!string.IsNullOrWhiteSpace(chosenPath))
                                {
                                    Object defaultFolder = AssetDatabase.LoadAssetAtPath<Object>(
                                        chosenPath
                                    );
                                    currentElementProp.objectReferenceValue = defaultFolder;
                                }
                                else
                                {
                                    currentElementProp.objectReferenceValue = null;
                                }
                                currentElementProp.serializedObject.ApplyModifiedProperties();
                            },
                            $"Select Directory {i + 1}"
                        );

                        if (GUILayout.Button("-", GUILayout.Width(25)))
                        {
                            elementProp.stringValue = "";
                            listProp.DeleteArrayElementAtIndex(i);

                            break;
                        }
                    }

                    EditorGUILayout.Space(3);
                }
            }

            if (GUILayout.Button("Add New Directory Path", GUILayout.Width(200)))
            {
                SerializedProperty newElement = listProp.AppendArrayElement();
                newElement.objectReferenceValue = null;
            }
        }

        public static string PathSelector(
            GUIContent label,
            string currentPath,
            string toolName,
            string contextKey,
            Action<string> onPathChosen,
            string dialogTitle = "Select Folder",
            float textFieldWidthOverride = -1f
        )
        {
            if (onPathChosen == null)
            {
                Debug.LogError(
                    "PersistentDirectoryGUI.PathSelector: onPathChosen callback cannot be null."
                );
                return currentPath;
            }

            string pathInTextField = currentPath;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (0 < textFieldWidthOverride)
                {
                    pathInTextField = EditorGUILayout.TextField(
                        label,
                        pathInTextField,
                        GUILayout.Width(textFieldWidthOverride)
                    );
                }
                else
                {
                    pathInTextField = EditorGUILayout.TextField(label, pathInTextField);
                }

                if (GUILayout.Button("Browse...", GUILayout.ExpandWidth(false)))
                {
                    string initialBrowsePath = Application.dataPath;
                    if (!string.IsNullOrWhiteSpace(pathInTextField))
                    {
                        try
                        {
                            string fullPotentialPath = Path.GetFullPath(
                                pathInTextField.StartsWith("Assets")
                                    ? pathInTextField
                                    : Path.Combine(Application.dataPath, "..", pathInTextField)
                            );
                            if (Directory.Exists(fullPotentialPath))
                            {
                                initialBrowsePath = fullPotentialPath;
                            }
                            else
                            {
                                string dirName = Path.GetDirectoryName(fullPotentialPath);
                                if (Directory.Exists(dirName))
                                {
                                    initialBrowsePath = dirName;
                                }
                            }
                        }
                        catch { }
                    }
                    if (!Directory.Exists(initialBrowsePath))
                    {
                        initialBrowsePath = Application.dataPath;
                    }

                    string selectedPathSys = Utils.EditorUi.OpenFolderPanel(
                        dialogTitle,
                        initialBrowsePath,
                        ""
                    );

                    if (!string.IsNullOrWhiteSpace(selectedPathSys))
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

                        pathInTextField = processedPath;
                        PersistentDirectorySettings.Instance.RecordPath(
                            toolName,
                            contextKey,
                            processedPath
                        );
                        onPathChosen.Invoke(processedPath);
                        GUI.FocusControl(null);
                    }
                }
            }

            DrawFrequentPaths(
                toolName,
                contextKey,
                chosenHistoryPath =>
                {
                    pathInTextField = chosenHistoryPath;
                    PersistentDirectorySettings.Instance.RecordPath(
                        toolName,
                        contextKey,
                        chosenHistoryPath
                    );
                    onPathChosen.Invoke(chosenHistoryPath);
                    GUI.FocusControl(null);
                },
                true,
                5,
                "History:"
            );

            return pathInTextField;
        }

        public static void DrawFrequentPaths(
            string toolName,
            string contextKey,
            Action<string> onPathClickedFromHistory,
            bool allowExpansion = true,
            int topN = 5,
            string listLabel = "Frequent Paths:"
        )
        {
            if (onPathClickedFromHistory == null)
            {
                Debug.LogError(
                    "PersistentDirectoryGUI.DrawFrequentPaths: onPathClickedFromHistory callback cannot be null."
                );
                return;
            }

            if (PersistentDirectorySettings.Instance == null)
            {
                return;
            }

            DirectoryUsageData[] topPaths = PersistentDirectorySettings.Instance.GetPaths(
                toolName,
                contextKey,
                true,
                topN
            );

            if (topPaths.Length <= 0)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f + 15f);
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(listLabel, EditorStyles.miniBoldLabel);
                    foreach (DirectoryUsageData dirData in topPaths)
                    {
                        if (
                            GUILayout.Button(
                                new GUIContent($"({dirData.count}) {dirData.path}", dirData.path),
                                EditorStyles.miniButtonLeft
                            )
                        )
                        {
                            onPathClickedFromHistory.Invoke(dirData.path);
                        }
                    }

                    if (!allowExpansion)
                    {
                        return;
                    }

                    string foldoutKey = $"{toolName}/{contextKey}_AllPathsHistory";
                    if (!ContextFoldoutStates.TryGetValue(foldoutKey, out bool expanded))
                    {
                        expanded = false;
                        ContextFoldoutStates.Add(foldoutKey, expanded);
                    }

                    DirectoryUsageData[] allPaths = PersistentDirectorySettings.Instance.GetPaths(
                        toolName,
                        contextKey,
                        false,
                        0
                    );
                    if (allPaths.Length <= topN)
                    {
                        return;
                    }

                    expanded = EditorGUILayout.Foldout(
                        expanded,
                        "Show All History (" + allPaths.Length + ")",
                        true,
                        EditorStyles.foldout
                    );
                    ContextFoldoutStates[foldoutKey] = expanded;
                    if (!expanded)
                    {
                        return;
                    }

                    int remaining = allPaths.Length - topN;
                    if (0 < remaining)
                    {
                        for (int idx = topN; idx < allPaths.Length; idx++)
                        {
                            DirectoryUsageData dirData = allPaths[idx];
                            if (
                                GUILayout.Button(
                                    new GUIContent(
                                        $"({dirData.count}) {dirData.path}",
                                        dirData.path
                                    ),
                                    EditorStyles.miniButtonLeft
                                )
                            )
                            {
                                onPathClickedFromHistory.Invoke(dirData.path);
                            }
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField(
                            "All paths already displayed above.",
                            EditorStyles.centeredGreyMiniLabel
                        );
                    }
                }
            }
        }
    }
#endif
}
