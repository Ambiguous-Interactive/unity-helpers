// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Globalization;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using Object = UnityEngine.Object;

    internal static class ValidationProjectFix
    {
        internal static bool CanFix(ValidationWorkspaceSettings.RuleDefinition rule)
        {
            return rule != null && rule.fix != "None (report only)";
        }

        internal static List<Action> ApplyMany(
            IReadOnlyList<Request> requests,
            List<string> failures
        )
        {
            List<Request> prepared = new List<Request>();
            for (int index = 0; index < requests.Count; index++)
            {
                Request request = requests[index];
                try
                {
                    VerifySource(request.Rule, request.Finding);
                    prepared.Add(request);
                }
                catch (Exception thrown)
                {
                    failures.Add(thrown.Message);
                }
            }
            List<Action> undo = new List<Action>();
            foreach (Request request in prepared)
            {
                try
                {
                    undo.Add(Apply(request.Rule, request.Finding, true));
                }
                catch (Exception thrown)
                {
                    failures.Add(thrown.Message);
                }
            }
            return undo;
        }

        internal static Action Apply(
            ValidationWorkspaceSettings.RuleDefinition rule,
            ValidationFinding finding,
            bool allowDependencyChanges = false
        )
        {
            if (!CanFix(rule))
                throw new InvalidOperationException("This rule only reports findings.");
            Object original = VerifySource(rule, finding, allowDependencyChanges);
            if (rule.fix == "Rename to pattern")
                return Rename(rule, finding);
            if (rule.fix == "Set import max size")
            {
                if (
                    !int.TryParse(
                        rule.fixValue,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int size
                    )
                    || size < 32
                    || 16384 < size
                    || (size & (size - 1)) != 0
                )
                    throw new InvalidOperationException(
                        "Texture size must be a power of two from 32 to 16384."
                    );
                if (!(AssetImporter.GetAtPath(finding.AssetPath) is TextureImporter importer))
                    throw new InvalidOperationException("The finding has no texture importer.");
                string importerGuid = AssetDatabase.AssetPathToGUID(importer.assetPath);
                int previous = importer.maxTextureSize;
                Undo.RecordObject(importer, "Set validation texture import size");
                importer.maxTextureSize = size;
                importer.SaveAndReimport();
                return () =>
                {
                    if (AssetDatabase.AssetPathToGUID(finding.AssetPath) != importerGuid)
                        throw new InvalidOperationException(
                            "The texture asset moved or was replaced since this fix."
                        );
                    TextureImporter current =
                        AssetImporter.GetAtPath(finding.AssetPath) as TextureImporter;
                    if (current == null || current.maxTextureSize != size)
                        throw new InvalidOperationException("The importer changed since this fix.");
                    Undo.RecordObject(current, "Undo validation texture fix");
                    current.maxTextureSize = previous;
                    current.SaveAndReimport();
                };
            }
            GameObject prefabRoot = null;
            try
            {
                Object resolved = Resolve(finding, original, out prefabRoot);
                GameObject subject = resolved is Component selectedComponent
                    ? selectedComponent.gameObject
                    : resolved as GameObject;
                if (subject == null)
                    throw new InvalidOperationException(
                        "The affected object no longer exists. Validate again."
                    );
                if (rule.fix == "Force mono on import")
                {
                    AudioSource audio = resolved as AudioSource;
                    if (
                        (audio == null && !subject.TryGetComponent(out audio))
                        || audio.clip == null
                        || !(
                            AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(audio.clip))
                            is AudioImporter importer
                        )
                    )
                        throw new InvalidOperationException(
                            "The affected source has no imported audio clip."
                        );
                    string clipPath = importer.assetPath;
                    string clipGuid = AssetDatabase.AssetPathToGUID(clipPath);
                    bool previous = importer.forceToMono;
                    Undo.RecordObject(importer, "Force validation audio to mono");
                    importer.forceToMono = true;
                    importer.SaveAndReimport();
                    return () =>
                    {
                        if (AssetDatabase.AssetPathToGUID(clipPath) != clipGuid)
                            throw new InvalidOperationException(
                                "The audio asset moved or was replaced since this fix."
                            );
                        AudioImporter current = AssetImporter.GetAtPath(clipPath) as AudioImporter;
                        if (current == null || !current.forceToMono)
                            throw new InvalidOperationException(
                                "The audio importer changed since this fix."
                            );
                        Undo.RecordObject(current, "Undo validation audio fix");
                        current.forceToMono = previous;
                        current.SaveAndReimport();
                    };
                }
                if (rule.fix != "Remove component")
                    throw new InvalidOperationException("Unknown fix: " + rule.fix);
                Type componentType = ValidationProjectRule.PrimaryComponentType(rule);
                Component component =
                    resolved is Component exact
                    && componentType != null
                    && componentType.IsInstanceOfType(exact)
                        ? exact
                    : componentType == null ? null
                    : subject.GetComponent(componentType);
                if (component == null)
                    throw new InvalidOperationException(
                        "No removable component matches this rule's properties."
                    );
                byte[] previousBytes =
                    prefabRoot == null ? null : File.ReadAllBytes(finding.AssetPath);
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Remove validation component");
                Undo.DestroyObjectImmediate(component);
                Undo.CollapseUndoOperations(group);
                Undo.IncrementCurrentGroup();
                if (prefabRoot == null)
                {
                    EditorSceneManager.MarkSceneDirty(subject.scene);
                    return null;
                }
                if (PrefabUtility.SaveAsPrefabAsset(prefabRoot, finding.AssetPath) == null)
                    throw new IOException("Unity could not save the fixed prefab.");
                byte[] written = File.ReadAllBytes(finding.AssetPath);
                return () =>
                {
                    byte[] current = File.ReadAllBytes(finding.AssetPath);
                    if (!Equal(current, written))
                        throw new InvalidOperationException(
                            "The prefab changed since this fix; undo would overwrite those edits."
                        );
                    File.WriteAllBytes(finding.AssetPath, previousBytes);
                    AssetDatabase.ImportAsset(finding.AssetPath);
                };
            }
            finally
            {
                if (prefabRoot != null)
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static bool Equal(byte[] first, byte[] second)
        {
            if (first.Length != second.Length)
                return false;
            for (int index = 0; index < first.Length; index++)
                if (first[index] != second[index])
                    return false;
            return true;
        }

        private static Action Rename(
            ValidationWorkspaceSettings.RuleDefinition rule,
            ValidationFinding finding
        )
        {
            string oldName = Path.GetFileNameWithoutExtension(finding.AssetPath);
            string name = (rule.fixValue ?? string.Empty).Replace("{name}", oldName);
            if (
                string.IsNullOrWhiteSpace(name)
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) != -1
                || name.Contains("/")
                || name.Contains("\\")
            )
                throw new InvalidOperationException(
                    "Enter a valid asset name pattern; {name} expands to its current name."
                );
            string error = AssetDatabase.RenameAsset(finding.AssetPath, name);
            if (!string.IsNullOrEmpty(error))
                throw new IOException(error);
            string renamed = AssetDatabase.GUIDToAssetPath(finding.AssetGuid);
            return () =>
            {
                if (AssetDatabase.GUIDToAssetPath(finding.AssetGuid) != renamed)
                    throw new InvalidOperationException("The asset moved since this fix.");
                string failure = AssetDatabase.RenameAsset(renamed, oldName);
                if (!string.IsNullOrEmpty(failure))
                    throw new IOException(failure);
            };
        }

        internal static Object VerifySource(
            ValidationWorkspaceSettings.RuleDefinition rule,
            ValidationFinding finding,
            bool allowDependencyChanges = false
        )
        {
            if (AssetDatabase.GUIDToAssetPath(finding.AssetGuid) != finding.AssetPath)
                throw new InvalidOperationException(
                    "The asset moved or was replaced since scanning. Validate again before fixing."
                );
            if (!GlobalObjectId.TryParse(finding.Discriminator, out GlobalObjectId id))
                throw new InvalidOperationException(
                    "This finding has no saved object identity. Save and validate before fixing."
                );
            if (finding.AssetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                Scene scene = SceneManager.GetSceneByPath(finding.AssetPath);
                if (!scene.IsValid() || !scene.isLoaded)
                    EditorSceneManager.OpenScene(finding.AssetPath, OpenSceneMode.Additive);
            }
            Object source = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
            if (
                source == null
                || string.IsNullOrEmpty(finding.SourceFingerprint)
                || !SameFingerprint(
                    ValidationProjectRule.Fingerprint(source, finding.AssetPath),
                    finding.SourceFingerprint,
                    allowDependencyChanges
                )
            )
                throw new InvalidOperationException(
                    "The scanned object or its dependencies changed. Validate again before fixing."
                );
            if (!new ValidationProjectRule(rule).MatchesSubject(source, finding.AssetPath))
                throw new InvalidOperationException(
                    "The object no longer matches this rule. Validate again."
                );
            return source;
        }

        private static bool SameFingerprint(
            string current,
            string original,
            bool allowDependencyChanges
        )
        {
            if (!allowDependencyChanges)
                return current == original;
            return current.Split(':')[0] == original.Split(':')[0];
        }

        private static Object Resolve(
            ValidationFinding finding,
            Object original,
            out GameObject prefabRoot
        )
        {
            if (!finding.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                prefabRoot = null;
                return original;
            }
            GameObject loadedRoot = PrefabUtility.LoadPrefabContents(finding.AssetPath);
            try
            {
                foreach (Transform transform in loadedRoot.GetComponentsInChildren<Transform>(true))
                {
                    Object source = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(
                        transform.gameObject,
                        finding.AssetPath
                    );
                    if (source == original)
                    {
                        prefabRoot = loadedRoot;
                        return transform.gameObject;
                    }
                    foreach (Component component in transform.GetComponents<Component>())
                    {
                        if (component == null)
                            continue;
                        source = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(
                            component,
                            finding.AssetPath
                        );
                        if (source == original)
                        {
                            prefabRoot = loadedRoot;
                            return component;
                        }
                    }
                }
                throw new InvalidOperationException(
                    "The saved object could not be identified in editable prefab contents."
                );
            }
            catch
            {
                if (loadedRoot != null)
                    PrefabUtility.UnloadPrefabContents(loadedRoot);
                throw;
            }
        }

        internal readonly struct Request
        {
            internal readonly ValidationWorkspaceSettings.RuleDefinition Rule;
            internal readonly ValidationFinding Finding;

            internal Request(
                ValidationWorkspaceSettings.RuleDefinition rule,
                ValidationFinding finding
            )
            {
                Rule = rule;
                Finding = finding;
            }
        }
    }
#endif
}
