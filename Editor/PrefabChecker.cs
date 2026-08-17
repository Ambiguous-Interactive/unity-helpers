// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using UnityEditor;
    using UnityEditorInternal;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    public sealed class PrefabChecker : EditorWindow
    {
        private const string ToolName = "PrefabChecker";
        private const string TargetContextKey = "TargetFolder";
        private const float ToggleWidth = 18f;
        private const float ToggleSpacing = 4f;

        private static readonly Dictionary<Type, List<FieldInfo>> FieldsByType = new();
        private static readonly Dictionary<Type, List<FieldInfo>> ListFieldsByType = new();
        private static readonly Dictionary<Type, List<FieldInfo>> StringFieldsByType = new();
        private static readonly Dictionary<Type, List<FieldInfo>> ObjectFieldsByType = new();
        private static readonly Dictionary<Type, RequireComponent[]> RequiredComponentsByType =
            new();

        internal List<string> _assetPaths = new();
        private ReorderableList _pathsList;
        private Vector2 _scrollPosition;

        private bool _checkMissingScripts = true;
        private bool _checkNullElementsInLists = true;
        private bool _checkMissingRequiredComponents = true;
        private bool _checkEmptyStringFields;
        private bool _checkNullObjectReferences = true;
        private bool _onlyCheckNullObjectsWithAttribute = true;
        private bool _checkDisabledRootGameObjects = true;
        private bool _checkDisabledComponents;
        private bool _offerAutoFixes;

        private readonly List<string> _includeLabels = new();
        private readonly List<string> _excludeLabels = new();
        private string _componentTypeDenyListCsv = string.Empty;

        private const int MaxTransformScanForMissingOwner = 5000;

        private const string DefaultPrefabsFolder = "Assets/Prefabs";

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Prefab Checker", priority = -1)]
        public static void ShowWindow()
        {
            GetWindow<PrefabChecker>("Prefab Check");
        }

        private void OnEnable()
        {
            PopulateDefaultPaths();
            TryRestoreFromHistory();
            SetupReorderableList();
        }

        private void PopulateDefaultPaths()
        {
            // Avoid implicit state when running in tests or suppressed UI contexts
            if (EditorUi.Suppress)
            {
                return;
            }

            if (_assetPaths.Count == 0 && AssetDatabase.IsValidFolder(DefaultPrefabsFolder))
            {
                _assetPaths.Add(DefaultPrefabsFolder);
            }
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            try
            {
                DrawConfigurationOptions();

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Target Folders", EditorStyles.boldLabel);
                DrawAssetPaths();
                if (GUILayout.Button("Add Folder"))
                {
                    AddFolder();
                }

                HandleDragAndDropForPaths();

                EditorGUILayout.Space();
                DrawFiltersAndUtilities();
                EditorGUILayout.Space();
                if (GUILayout.Button("Run Checks", GUILayout.Height(30)))
                {
                    RunChecksImproved();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = _offerAutoFixes;
                    if (GUILayout.Button("Fix Missing Scripts"))
                    {
                        FixMissingScripts();
                    }
                    GUI.enabled = true;
                    if (GUILayout.Button("Export Report (JSON)"))
                    {
                        ExportLastReport();
                    }
                    if (GUILayout.Button("Export Report (CSV)"))
                    {
                        ExportLastReportCsv();
                    }
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawConfigurationOptions()
        {
            EditorGUILayout.LabelField("Validation Checks", EditorStyles.boldLabel);

            Func<GUIContent, bool, float?, bool, bool> drawRightAlignedToggle =
                SetupDrawRightAlignedToggle();

            float targetAlignmentX = 0f;
            bool alignmentCalculated = false;

            DrawAndAlign(
                new GUIContent(
                    "Missing Scripts",
                    "Check for GameObjects with missing script references."
                ),
                () => _checkMissingScripts,
                v => _checkMissingScripts = v,
                false
            );
            DrawAndAlign(
                new GUIContent(
                    "Nulls in Lists/Arrays",
                    "Check for null elements within serialized lists or arrays."
                ),
                () => _checkNullElementsInLists,
                v => _checkNullElementsInLists = v,
                false
            );
            DrawAndAlign(
                new GUIContent(
                    "Missing Required Components",
                    "Check if components are missing dependencies defined by [RequireComponent]."
                ),
                () => _checkMissingRequiredComponents,
                v => _checkMissingRequiredComponents = v,
                false
            );
            DrawAndAlign(
                new GUIContent(
                    "Empty String Fields",
                    "Check for serialized string fields that are empty."
                ),
                () => _checkEmptyStringFields,
                v => _checkEmptyStringFields = v,
                false
            );

            DrawAndAlign(
                new GUIContent(
                    "Null Object References",
                    "Check for serialized UnityEngine.Object fields that are null."
                ),
                () => _checkNullObjectReferences,
                v => _checkNullObjectReferences = v,
                false
            );

            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && _checkNullObjectReferences;
            try
            {
                using EditorGUI.IndentLevelScope indent = new();
                DrawAndAlign(
                    new GUIContent(
                        "Only if [ValidateAssignment]",
                        "Only report null object references if the field has the [ValidateAssignment] attribute."
                    ),
                    () => _onlyCheckNullObjectsWithAttribute,
                    v => _onlyCheckNullObjectsWithAttribute = v,
                    true
                );
            }
            finally
            {
                GUI.enabled = wasEnabled;
            }

            DrawAndAlign(
                new GUIContent(
                    "Disabled Root GameObject",
                    "Check if the prefab's root GameObject is inactive."
                ),
                () => _checkDisabledRootGameObjects,
                v => _checkDisabledRootGameObjects = v,
                false
            );
            DrawAndAlign(
                new GUIContent(
                    "Disabled Components",
                    "Check for any components on the prefab that are disabled."
                ),
                () => _checkDisabledComponents,
                v => _checkDisabledComponents = v,
                false
            );
            return;

            void DrawAndAlign(
                GUIContent content,
                Func<bool> getter,
                Action<bool> setter,
                bool isNested
            )
            {
                switch (alignmentCalculated)
                {
                    case false when !isNested:
                    {
                        float viewWidth = EditorGUIUtility.currentViewWidth;

                        float availableWidth = viewWidth - 18f;
                        targetAlignmentX = availableWidth - ToggleWidth;
                        alignmentCalculated = true;
                        break;
                    }
                    case false when isNested:
                    {
                        float viewWidth = EditorGUIUtility.currentViewWidth;
                        float availableWidth = viewWidth - 20f - 15f;
                        targetAlignmentX = availableWidth - ToggleWidth;
                        alignmentCalculated = true;
                        this.LogWarn(
                            $"Calculated alignment X based on first item being nested. Alignment might be approximate."
                        );
                        break;
                    }
                }

                float? overrideX = isNested ? targetAlignmentX : null;

                bool newValue = drawRightAlignedToggle(content, getter(), overrideX, isNested);
                if (newValue != getter())
                {
                    setter(newValue);
                }
            }
        }

        private static Func<GUIContent, bool, float?, bool, bool> SetupDrawRightAlignedToggle()
        {
            return (label, value, overrideToggleX, isNested) =>
            {
                Rect lineRect;
                using (IndentLevelScope.Indent(isNested ? 1 : 0))
                {
                    lineRect = EditorGUILayout.GetControlRect(
                        true,
                        EditorGUIUtility.singleLineHeight
                    );
                }

                float defaultToggleX = lineRect.x + lineRect.width - ToggleWidth;
                float finalToggleX = overrideToggleX ?? defaultToggleX;

                finalToggleX = Mathf.Max(finalToggleX, lineRect.x + ToggleSpacing);

                Rect toggleRect = new(finalToggleX, lineRect.y, ToggleWidth, lineRect.height);

                float labelWidth = Mathf.Max(0, finalToggleX - lineRect.x - ToggleSpacing);
                Rect labelRect = new(lineRect.x, lineRect.y, labelWidth, lineRect.height);

                EditorGUI.LabelField(labelRect, label);
                return EditorGUI.Toggle(toggleRect, value);
            };
        }

        private void DrawAssetPaths()
        {
            if (_pathsList == null)
            {
                SetupReorderableList();
            }
            // ReSharper disable once PossibleNullReferenceException
            _pathsList.DoLayoutList();
        }

        private void AddFolder()
        {
            string absolutePath = EditorUi.OpenFolderPanel(
                "Select Prefab Folder",
                "Assets",
                string.Empty
            );

            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return;
            }

            _ = TryAddFolderFromAbsolute(absolutePath);
        }

        internal bool TryAddFolderFromAbsolute(string absolutePath)
        {
            // Early return for obviously invalid input without logging an error
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            if (!TryGetUnityFolderFromAbsolute(absolutePath, out string relativePath))
            {
                this.LogError(
                    $"Selected folder must be inside the Unity project's Assets folder. Selected path: {absolutePath}"
                );
                return false;
            }

            return AddAssetFolder(relativePath);
        }

        internal bool AddAssetFolder(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            if (
                relativePath.Equals("Assets", StringComparison.Ordinal)
                || AssetDatabase.IsValidFolder(relativePath)
            )
            {
                if (!_assetPaths.Contains(relativePath))
                {
                    _assetPaths.Add(relativePath);
                    TryRecordHistory(relativePath);
                    return true;
                }

                this.LogWarn($"Folder '{relativePath}' is already in the list.");
                return false;
            }

            this.LogWarn($"Selected path '{relativePath}' is not a valid Unity folder.");
            return false;
        }

        private static bool TryGetUnityFolderFromAbsolute(
            string absolutePath,
            out string unityRelative
        )
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                unityRelative = string.Empty;
                return false;
            }

            string rel = DirectoryHelper.AbsoluteToUnityRelativePath(absolutePath);
            if (string.IsNullOrWhiteSpace(rel))
            {
                unityRelative = string.Empty;
                return false;
            }

            // Normalize slashes and casing for consistency
            rel = rel.SanitizePath();

            // Strip trailing slash(es) for consistent path handling (both forward and back slashes)
            rel = rel.TrimEnd('/', '\\');

            if (rel.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            {
                rel = "Assets/" + rel.Substring("assets/".Length);
            }
            else if (string.Equals(rel, "assets", StringComparison.OrdinalIgnoreCase))
            {
                rel = "Assets";
            }

            unityRelative = rel;
            return true;
        }

        // Removed legacy RunChecks(). Use RunChecksImproved() instead.

        internal void RunChecksImproved()
        {
            if (_assetPaths is not { Count: > 0 })
            {
                this.LogError($"No asset paths specified. Add folders containing prefabs.");
                return;
            }

            using PooledResource<List<string>> validPathBuffer = Buffers<string>.List.Get(
                out List<string> validPaths
            );
            foreach (string assetPath in _assetPaths)
            {
                if (
                    !string.IsNullOrEmpty(assetPath)
                    && (assetPath == "Assets" || AssetDatabase.IsValidFolder(assetPath))
                )
                {
                    validPaths.Add(assetPath);
                }
            }

            if (validPaths.Count == 0)
            {
                this.LogError(
                    $"None of the specified paths are valid folders: {string.Join(", ", _assetPaths)}"
                );
                return;
            }

            this.Log($"Starting prefab check for folders: {string.Join(", ", validPaths)}");
            foreach (string p in validPaths)
            {
                TryRecordHistory(p);
            }

            // Use ToArray() to create an exact-sized array for AssetDatabase.FindAssets.
            // SystemArrayPool returns arrays larger than requested (power-of-2 bucketing),
            // and Unity's FindAssets iterates over the entire array, causing NullReferenceException
            // from null elements when passed to Paths.ConvertSeparatorsToUnity.
            string[] folderArray = validPaths.ToArray();
            string[] guids = AssetDatabase.FindAssets("t:prefab", folderArray);
            int totalPrefabsChecked = 0;
            int totalIssuesFound = 0;

            using PooledResource<HashSet<string>> includeSetLease = Buffers<string>.HashSet.Get(
                out HashSet<string> includeSet
            );
            foreach (string label in _includeLabels)
            {
                if (!string.IsNullOrWhiteSpace(label))
                {
                    includeSet.Add(label.Trim());
                }
            }

            using PooledResource<HashSet<string>> excludeSetLease = Buffers<string>.HashSet.Get(
                out HashSet<string> excludeSet
            );
            foreach (string label in _excludeLabels)
            {
                if (!string.IsNullOrWhiteSpace(label))
                {
                    excludeSet.Add(label.Trim());
                }
            }
            using PooledResource<Stopwatch> stopwatchBuffer = StopwatchBuffers.Stopwatch.Get(
                out Stopwatch stopwatch
            );
            int skippedByLabel = 0;
            _lastReport = new ScanReport(validPaths);

            for (int idx = 0; idx < guids.Length; idx++)
            {
                if (
                    EditorUi.CancelableProgress(
                        "Prefab Checker",
                        $"Scanning prefabs... {idx + 1}/{guids.Length}",
                        (float)(idx + 1) / Mathf.Max(1, guids.Length)
                    )
                )
                {
                    this.LogWarn($"Prefab scan canceled by user.");
                    break;
                }

                string path = AssetDatabase.GUIDToAssetPath(guids[idx]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                string[] labels = AssetDatabase.GetLabels(prefab);
                if (includeSet.Count > 0)
                {
                    bool anyIncluded = false;
                    foreach (string label in labels)
                    {
                        if (includeSet.Contains(label))
                        {
                            anyIncluded = true;
                            break;
                        }
                    }
                    if (!anyIncluded)
                    {
                        skippedByLabel++;
                        continue;
                    }
                }
                if (excludeSet.Count > 0)
                {
                    bool anyExcluded = false;
                    foreach (string label in labels)
                    {
                        if (excludeSet.Contains(label))
                        {
                            anyExcluded = true;
                            break;
                        }
                    }
                    if (anyExcluded)
                    {
                        skippedByLabel++;
                        continue;
                    }
                }

                totalPrefabsChecked++;
                int issuesForThisPrefab = 0;
                using PooledResource<List<string>> resultLease = Buffers<string>.List.Get(
                    out List<string> messages
                );

                if (_checkDisabledRootGameObjects && !prefab.activeSelf)
                {
                    messages.Add("Prefab root GameObject is disabled.");
                    issuesForThisPrefab++;
                }

                using PooledResource<List<MonoBehaviour>> componentBufferResource =
                    Buffers<MonoBehaviour>.List.Get(out List<MonoBehaviour> componentBuffer);
                prefab.GetComponentsInChildren(true, componentBuffer);

                using PooledResource<Dictionary<GameObject, HashSet<Type>>> typeMapLease =
                    DictionaryBuffer<GameObject, HashSet<Type>>.Dictionary.Get(
                        out Dictionary<GameObject, HashSet<Type>> typeMap
                    );
                using PooledResource<List<Component>> compsLease = Buffers<Component>.List.Get(
                    out List<Component> comps
                );
                using PooledResource<List<PooledResource<HashSet<Type>>>> createdSetsLeases =
                    Buffers<PooledResource<HashSet<Type>>>.List.Get(
                        out List<PooledResource<HashSet<Type>>> createdSets
                    );

                foreach (MonoBehaviour script in componentBuffer)
                {
                    if (_checkMissingScripts && !script)
                    {
                        GameObject owner = FindOwnerOfMissingScriptBounded(prefab, componentBuffer);
                        string ownerName = owner ? owner.name : "[[Unknown GameObject]]";
                        messages.Add($"Detected missing script on GameObject '{ownerName}'.");
                        issuesForThisPrefab++;
                        continue;
                    }
                    if (!script)
                    {
                        continue;
                    }

                    GameObject ownerGameObject = script.gameObject;
                    bool denied = false;
                    if (!string.IsNullOrWhiteSpace(_componentTypeDenyListCsv))
                    {
                        string typeName = script.GetType().Name;
                        string fullName = script.GetType().FullName;
                        string[] tokens = _componentTypeDenyListCsv.Split(',');
                        foreach (string token in tokens)
                        {
                            string t = token.Trim();
                            if (t.Length == 0)
                            {
                                continue;
                            }
                            if (
                                string.Equals(t, typeName, StringComparison.Ordinal)
                                || string.Equals(t, fullName, StringComparison.Ordinal)
                            )
                            {
                                denied = true;
                                break;
                            }
                        }
                    }
                    if (denied)
                    {
                        continue;
                    }
                    if (_checkNullElementsInLists)
                    {
                        issuesForThisPrefab += ValidateNoNullsInLists(script, ownerGameObject);
                    }

                    if (_checkMissingRequiredComponents)
                    {
                        HashSet<Type> present = GetOrBuildTypeSet(ownerGameObject);
                        issuesForThisPrefab += ValidateRequiredComponentsFast(
                            script,
                            ownerGameObject,
                            present
                        );
                    }
                    if (_checkEmptyStringFields)
                    {
                        issuesForThisPrefab += ValidateEmptyStrings(script, ownerGameObject);
                    }

                    if (_checkNullObjectReferences)
                    {
                        issuesForThisPrefab += ValidateNullObjectReferences(
                            script,
                            ownerGameObject
                        );
                    }

                    if (_checkDisabledComponents && script is Behaviour { enabled: false })
                    {
                        messages.Add(
                            $"Component '{script.GetType().Name}' on GameObject '{ownerGameObject.name}' is disabled."
                        );
                        issuesForThisPrefab++;
                    }
                }

                if (issuesForThisPrefab > 0)
                {
                    int toLog = Mathf.Min(100, messages.Count);
                    for (int m = 0; m < toLog; m++)
                    {
                        prefab.LogWarn($"{messages[m]}");
                    }

                    if (messages.Count > toLog)
                    {
                        prefab.LogWarn($"... and {messages.Count - toLog} more.");
                    }

                    this.LogWarn(
                        $"Prefab '{prefab.name}' at path '{path}' has {issuesForThisPrefab} potential issues."
                    );
                    _lastReport.Add(path, messages);
                    totalIssuesFound += issuesForThisPrefab;
                }

                // Release pooled type sets created for this prefab
                foreach (PooledResource<HashSet<Type>> setLease in createdSets)
                {
                    setLease.Dispose();
                }
                createdSets.Clear();
                continue;

                HashSet<Type> GetOrBuildTypeSet(GameObject go)
                {
                    if (typeMap.TryGetValue(go, out HashSet<Type> cached))
                    {
                        return cached;
                    }
                    PooledResource<HashSet<Type>> setLease = Buffers<Type>.HashSet.Get(
                        out HashSet<Type> set
                    );
                    createdSets.Add(setLease);
                    go.GetComponents(comps);
                    foreach (Component comp in comps)
                    {
                        if (comp != null)
                        {
                            set.Add(comp.GetType());
                        }
                    }
                    comps.Clear();
                    typeMap[go] = set;
                    return set;
                }
            }

            if (totalIssuesFound > 0)
            {
                this.LogError(
                    $"Prefab check complete. Found {totalIssuesFound} potential issues across {totalPrefabsChecked} prefabs."
                );
            }
            else
            {
                this.Log(
                    $"Prefab check complete. No issues found in {totalPrefabsChecked} prefabs."
                );
            }
            EditorUi.ClearProgress();
            stopwatch.Stop();
            this.Log(
                $"Scanned {totalPrefabsChecked} prefabs in {stopwatch.ElapsedMilliseconds} ms. Skipped {skippedByLabel} by label."
            );
        }

        private static GameObject FindOwnerOfMissingScript(
            GameObject prefabRoot,
            List<MonoBehaviour> buffer
        )
        {
            using PooledResource<List<Transform>> transformBufferResource =
                Buffers<Transform>.List.Get(out List<Transform> transforms);
            prefabRoot.GetComponentsInChildren(true, transforms);
            foreach (Transform transform in transforms)
            {
                using PooledResource<List<MonoBehaviour>> componentBuffer =
                    Buffers<MonoBehaviour>.List.Get(out List<MonoBehaviour> components);
                transform.GetComponents(components);
                int bufferCount = 0;
                foreach (MonoBehaviour c in components)
                {
                    if (c != null && c.gameObject == transform.gameObject)
                    {
                        ++bufferCount;
                    }
                }
                if (components.Count == bufferCount)
                {
                    continue;
                }

                bool foundInNonNullBuffer = false;
                foreach (MonoBehaviour c in components)
                {
                    if (buffer.Contains(c))
                    {
                        foundInNonNullBuffer = true;
                        break;
                    }
                }

                if (foundInNonNullBuffer)
                {
                    return transform.gameObject;
                }

                if (components.Count != 0 || !buffer.Exists(c => c == null))
                {
                    continue;
                }

                using PooledResource<HashSet<GameObject>> setResource =
                    Buffers<GameObject>.HashSet.Get(
                        out HashSet<GameObject> gameObjectsWithComponentsInBuffer
                    );
                foreach (MonoBehaviour c in buffer)
                {
                    if (c != null)
                    {
                        gameObjectsWithComponentsInBuffer.Add(c.gameObject);
                    }
                }
                if (!gameObjectsWithComponentsInBuffer.Contains(transform.gameObject))
                {
                    return transform.gameObject;
                }
            }

            return prefabRoot;
        }

        private static IEnumerable<FieldInfo> GetFieldsToCheck(
            Type componentType,
            Dictionary<Type, List<FieldInfo>> cache
        )
        {
            return cache.GetOrAdd(
                componentType,
                type =>
                {
                    FieldInfo[] fields = type.GetFields(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    );
                    int len = fields.Length;
                    List<FieldInfo> list = new(len);
                    for (int i = 0; i < len; i++)
                    {
                        FieldInfo field = fields[i];
                        bool include =
                            field.IsPublic
                            || field.IsAttributeDefined<SerializeField>(out _, inherit: true);
                        if (include)
                        {
                            list.Add(field);
                        }
                    }
                    return list;
                }
            );
        }

        private static int ValidateNoNullsInLists(Object component, GameObject context)
        {
            int issueCount = 0;
            Type componentType = component.GetType();

            List<FieldInfo> listFields = ListFieldsByType.GetOrAdd(
                componentType,
                type =>
                {
                    IEnumerable<FieldInfo> baseFields = GetFieldsToCheck(type, FieldsByType);
                    List<FieldInfo> res = new();
                    foreach (FieldInfo f in baseFields)
                    {
                        if (f == null)
                        {
                            continue;
                        }
                        Type ft = f.FieldType;
                        if (typeof(IEnumerable).IsAssignableFrom(ft) && ft != typeof(string))
                        {
                            res.Add(f);
                        }
                    }
                    return res;
                }
            );
            foreach (FieldInfo field in listFields)
            {
                object fieldValue = field.GetValue(component);

                if (fieldValue is not IEnumerable list)
                {
                    continue;
                }

                int index = 0;
                if (list is Object unityObject)
                {
                    if (list.GetType() != typeof(Transform) && unityObject == null)
                    {
                        unityObject.LogError(
                            $"Field '{field.Name}' ({field.FieldType.Name}) on component '{componentType.Name}' has a null enumerable."
                        );
                    }
                    continue;
                }
                foreach (object element in list)
                {
                    if (element == null || (element is Object unityObj && !unityObj))
                    {
                        context.LogError(
                            $"Field '{field.Name}' ({field.FieldType.Name}) on component '{componentType.Name}' has a null or missing element at index {index}."
                        );
                        issueCount++;
                    }
                    index++;
                }
            }
            return issueCount;
        }

        private static int ValidateRequiredComponentsFast(
            Component component,
            GameObject context,
            HashSet<Type> presentTypes
        )
        {
            int issueCount = 0;
            Type componentType = component.GetType();

            RequireComponent[] required = RequiredComponentsByType.GetOrAdd(
                componentType,
                type => type.GetAllAttributesSafe<RequireComponent>(inherit: true)
            );

            if (required.Length <= 0)
            {
                return issueCount;
            }

            foreach (RequireComponent requiredComponent in required)
            {
                if (
                    requiredComponent.m_Type0 != null
                    && !presentTypes.Contains(requiredComponent.m_Type0)
                )
                {
                    context.LogError(
                        $"Component '{componentType.Name}' requires component '{requiredComponent.m_Type0.Name}', but it is missing."
                    );
                    issueCount++;
                }
                if (
                    requiredComponent.m_Type1 != null
                    && !presentTypes.Contains(requiredComponent.m_Type1)
                )
                {
                    context.LogError(
                        $"Component '{componentType.Name}' requires component '{requiredComponent.m_Type1.Name}', but it is missing."
                    );
                    issueCount++;
                }
                if (
                    requiredComponent.m_Type2 != null
                    && !presentTypes.Contains(requiredComponent.m_Type2)
                )
                {
                    context.LogError(
                        $"Component '{componentType.Name}' requires component '{requiredComponent.m_Type2.Name}', but it is missing."
                    );
                    issueCount++;
                }
            }
            return issueCount;
        }

        private static int ValidateEmptyStrings(Object component, GameObject context)
        {
            int issueCount = 0;
            Type componentType = component.GetType();

            List<FieldInfo> stringFields = StringFieldsByType.GetOrAdd(
                componentType,
                type =>
                {
                    IEnumerable<FieldInfo> baseFields = GetFieldsToCheck(type, FieldsByType);
                    List<FieldInfo> res = new();
                    foreach (FieldInfo f in baseFields)
                    {
                        if (f != null && f.FieldType == typeof(string))
                        {
                            res.Add(f);
                        }
                    }
                    return res;
                }
            );
            foreach (FieldInfo field in stringFields)
            {
                object fieldValue = field.GetValue(component);
                if (fieldValue is string stringValue && string.IsNullOrEmpty(stringValue))
                {
                    context.LogWarn(
                        $"String field '{field.Name}' on component '{componentType.Name}' is null or empty."
                    );
                    issueCount++;
                }
            }
            return issueCount;
        }

        private int ValidateNullObjectReferences(Object component, GameObject context)
        {
            int issueCount = 0;
            Type componentType = component.GetType();

            List<FieldInfo> objFields = ObjectFieldsByType.GetOrAdd(
                componentType,
                type =>
                {
                    IEnumerable<FieldInfo> baseFields = GetFieldsToCheck(type, FieldsByType);
                    List<FieldInfo> res = new();
                    foreach (FieldInfo f in baseFields)
                    {
                        if (f != null && typeof(Object).IsAssignableFrom(f.FieldType))
                        {
                            res.Add(f);
                        }
                    }
                    return res;
                }
            );
            foreach (FieldInfo field in objFields)
            {
                bool hasValidateAttribute = field.IsAttributeDefined<ValidateAssignmentAttribute>(
                    out _,
                    inherit: false
                );

                if (_onlyCheckNullObjectsWithAttribute && !hasValidateAttribute)
                {
                    continue;
                }

                object fieldValue = field.GetValue(component);

                if (fieldValue != null && (fieldValue is not Object unityObj || unityObj))
                {
                    continue;
                }

                string attributeMarker = hasValidateAttribute ? " (has [ValidateAssignment])" : "";
                context.LogError(
                    $"Object reference field '{field.Name}'{attributeMarker} on component '{componentType.Name}' is null or missing."
                );
                issueCount++;
            }
            return issueCount;
        }

        private static GameObject FindOwnerOfMissingScriptBounded(
            GameObject prefabRoot,
            List<MonoBehaviour> buffer
        )
        {
            using PooledResource<List<Transform>> transformBufferResource =
                Buffers<Transform>.List.Get(out List<Transform> transforms);
            prefabRoot.GetComponentsInChildren(true, transforms);
            if (transforms.Count > MaxTransformScanForMissingOwner)
            {
                prefabRoot.LogWarn(
                    $"Hierarchy too large to locate owner of missing script (>{MaxTransformScanForMissingOwner}). Reporting at prefab root."
                );
                return prefabRoot;
            }
            return FindOwnerOfMissingScript(prefabRoot, buffer);
        }

        private void SetupReorderableList()
        {
            _pathsList = new ReorderableList(_assetPaths, typeof(string), true, true, true, true)
            {
                drawHeaderCallback = rect =>
                {
                    EditorGUI.LabelField(rect, "Folders to scan");
                },
                drawElementCallback = (rect, index, _, _) =>
                {
                    if (index < 0 || index >= _assetPaths.Count)
                    {
                        return;
                    }

                    string path = _assetPaths[index];
                    Rect labelRect = new(
                        rect.x,
                        rect.y,
                        rect.width - 100f,
                        EditorGUIUtility.singleLineHeight
                    );
                    EditorGUI.LabelField(labelRect, path);
                    if (
                        GUI.Button(
                            new Rect(
                                rect.x + rect.width - 95f,
                                rect.y,
                                45f,
                                EditorGUIUtility.singleLineHeight
                            ),
                            "Ping"
                        )
                    )
                    {
                        DefaultAsset asset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
                        if (asset)
                        {
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                    if (
                        GUI.Button(
                            new Rect(
                                rect.x + rect.width - 45f,
                                rect.y,
                                45f,
                                EditorGUIUtility.singleLineHeight
                            ),
                            "Open"
                        )
                    )
                    {
                        DefaultAsset asset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
                        if (asset)
                        {
                            Selection.activeObject = asset;
                        }
                    }
                },
                onAddCallback = _ =>
                {
                    AddFolder();
                },
                onRemoveCallback = list =>
                {
                    if (list.index >= 0 && list.index < _assetPaths.Count)
                    {
                        _assetPaths.RemoveAt(list.index);
                    }
                },
            };
        }

        private void HandleDragAndDropForPaths()
        {
            Rect dropArea = GUILayoutUtility.GetLastRect();
            Event evt = Event.current;
            if (!dropArea.Contains(evt.mousePosition))
            {
                return;
            }

            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (Object obj in DragAndDrop.objectReferences)
                    {
                        string path = AssetDatabase.GetAssetPath(obj);
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            continue;
                        }

                        _ = AddAssetFolder(path);
                    }
                }
                Event.current.Use();
            }
        }

        private void DrawFiltersAndUtilities()
        {
            EditorGUILayout.LabelField("Filters & Utilities", EditorStyles.boldLabel);
            _offerAutoFixes = EditorGUILayout.ToggleLeft(
                "Enable Auto-fix options",
                _offerAutoFixes
            );

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Include Labels (comma)", GUILayout.Width(180));
                string includeCsv = string.Join(",", _includeLabels);
                string newInclude = EditorGUILayout.TextField(includeCsv);
                if (!string.Equals(includeCsv, newInclude, StringComparison.Ordinal))
                {
                    _includeLabels.Clear();
                    foreach (string s in newInclude.Split(','))
                    {
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            _includeLabels.Add(s.Trim());
                        }
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Exclude Labels (comma)", GUILayout.Width(180));
                string excludeCsv = string.Join(",", _excludeLabels);
                string newExclude = EditorGUILayout.TextField(excludeCsv);
                if (!string.Equals(excludeCsv, newExclude, StringComparison.Ordinal))
                {
                    _excludeLabels.Clear();
                    foreach (string s in newExclude.Split(','))
                    {
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            _excludeLabels.Add(s.Trim());
                        }
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    "Deny Component Types (comma names)",
                    GUILayout.Width(220)
                );
                _componentTypeDenyListCsv = EditorGUILayout.TextField(_componentTypeDenyListCsv);
            }
        }

        private void TryRestoreFromHistory()
        {
            // Do not restore persisted UI state while tests/non-interactive contexts run
            if (EditorUi.Suppress)
            {
                return;
            }

            if (_assetPaths.Count > 0)
            {
                return;
            }

            if (PersistentDirectorySettings.Instance == null)
            {
                return;
            }

            DirectoryUsageData[] top = PersistentDirectorySettings.Instance.GetPaths(
                ToolName,
                TargetContextKey,
                true,
                1
            );
            if (top is { Length: > 0 })
            {
                string p = top[0].path;
                if (
                    !string.IsNullOrWhiteSpace(p)
                    && (p == "Assets" || AssetDatabase.IsValidFolder(p))
                )
                {
                    _assetPaths.Add(p);
                }
            }
        }

        private static void TryRecordHistory(string relativePath)
        {
            if (PersistentDirectorySettings.Instance == null)
            {
                return;
            }

            try
            {
                PersistentDirectorySettings.Instance.RecordPath(
                    ToolName,
                    TargetContextKey,
                    relativePath
                );
            }
            catch
            {
                // Swallow
            }
        }

        private void FixMissingScripts()
        {
            if (!_offerAutoFixes)
            {
                return;
            }

            foreach (string folder in _assetPaths)
            {
                string[] guids = AssetDatabase.FindAssets("t:prefab", new[] { folder });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go == null)
                    {
                        continue;
                    }

                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            this.Log($"Auto-fix complete: Removed missing scripts in selected folders.");
        }

        [Serializable]
        internal sealed class ScanReport
        {
            public readonly string[] folders;
            public readonly List<Item> items = new();

            public ScanReport(IEnumerable<string> folders)
            {
                if (folders == null)
                {
                    this.folders = Array.Empty<string>();
                    return;
                }
                // Manual copy to avoid LINQ
                using PooledResource<List<string>> folderBuffer = Buffers<string>.List.Get(
                    out List<string> list
                );

                foreach (string s in folders)
                {
                    list.Add(s);
                }
                this.folders = list.ToArray();
            }

            [Serializable]
            internal sealed class Item
            {
                public string path;
                public string[] messages;
            }

            public void Add(string path, List<string> messages)
            {
                string[] arr = messages is { Count: > 0 }
                    ? messages.ToArray()
                    : Array.Empty<string>();
                items.Add(new Item { path = path, messages = arr });
            }
        }

        private ScanReport _lastReport;

        private void ExportLastReport()
        {
            if (_lastReport == null || _lastReport.items.Count == 0)
            {
                EditorUi.Info("Prefab Checker", "No report data to export.");
                return;
            }
            string defaultPath = Application.dataPath + "/PrefabCheckerReport.json";
            string savePath = EditorUi.Suppress
                ? defaultPath
                : EditorUtility.SaveFilePanel(
                    "Save Prefab Checker Report",
                    Application.dataPath,
                    "PrefabCheckerReport",
                    "json"
                );
            if (string.IsNullOrWhiteSpace(savePath))
            {
                return;
            }

            try
            {
                string json = JsonUtility.ToJson(_lastReport, true);
                File.WriteAllText(savePath, json);
                this.Log($"Saved report to: {savePath}");
            }
            catch (Exception e)
            {
                this.LogError($"Failed to save report", e);
            }
        }

        private void ExportLastReportCsv()
        {
            if (_lastReport == null || _lastReport.items.Count == 0)
            {
                EditorUi.Info("Prefab Checker", "No report data to export.");
                return;
            }
            string defaultPath = Application.dataPath + "/PrefabCheckerReport.csv";
            string savePath = EditorUi.Suppress
                ? defaultPath
                : EditorUtility.SaveFilePanel(
                    "Save Prefab Checker Report (CSV)",
                    Application.dataPath,
                    "PrefabCheckerReport",
                    "csv"
                );
            if (string.IsNullOrWhiteSpace(savePath))
            {
                return;
            }

            try
            {
                using StreamWriter sw = new(savePath);
                sw.WriteLine("Path,Message");
                foreach (ScanReport.Item item in _lastReport.items)
                {
                    string path = item.path?.Replace('"', '\'') ?? string.Empty;
                    if (item.messages == null || item.messages.Length == 0)
                    {
                        sw.WriteLine($"\"{path}\",\"\"");
                        continue;
                    }
                    foreach (string m in item.messages)
                    {
                        string msg = (m ?? string.Empty).Replace('"', '\'');
                        sw.WriteLine($"\"{path}\",\"{msg}\"");
                    }
                }
                this.Log($"Saved report to: {savePath}");
            }
            catch (Exception e)
            {
                this.LogError($"Failed to save CSV", e);
            }
        }
    }
#endif
}
