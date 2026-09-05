// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Collections;
    using System.Globalization;
    using System.Reflection;
    using System.Text;
    using System.Text.Json;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Utils;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using Object = UnityEngine.Object;

    internal sealed class ValidationProjectRule : IValidationRule
    {
        private readonly ValidationWorkspaceSettings.RuleDefinition _definition;

        internal ValidationProjectRule(ValidationWorkspaceSettings.RuleDefinition definition)
        {
            if (!ValidateDefinition(definition, out string failure))
                throw new ArgumentException(failure, nameof(definition));
            _definition = JsonUtility.FromJson<ValidationWorkspaceSettings.RuleDefinition>(
                JsonUtility.ToJson(definition)
            );
        }

        internal static bool ValidateDefinition(
            ValidationWorkspaceSettings.RuleDefinition definition,
            out string failure
        )
        {
            string error = null;
            if (
                definition == null
                || string.IsNullOrWhiteSpace(definition.name)
                || string.IsNullOrWhiteSpace(definition.message)
            )
                error = "A rule needs a name and message.";
            else if (Array.IndexOf(ValidationWorkspaceSettings.Categories, definition.target) < 0)
                error = "Choose a supported target category.";
            else if (definition.checks == null || definition.checks.Count == 0)
                error = "Add at least one condition.";
            else
            {
                foreach (ValidationWorkspaceSettings.RuleCondition condition in definition.checks)
                {
                    if (
                        condition == null
                        || Array.IndexOf(ValidationWorkspaceSettings.Properties, condition.property)
                            < 0
                        || Array.IndexOf(
                            ValidationWorkspaceSettings.Conditions,
                            condition.comparison
                        ) < 0
                    )
                    {
                        error = "A condition has an unsupported property or comparison.";
                        break;
                    }
                    if (condition.comparison == ">" || condition.comparison == "<")
                    {
                        if (
                            !double.TryParse(
                                condition.value,
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out double value
                            )
                            || double.IsNaN(value)
                            || double.IsInfinity(value)
                        )
                        {
                            error =
                                "Numeric comparisons need a finite number (use a decimal point).";
                            break;
                        }
                    }
                }
                if (
                    error == null
                    && Array.IndexOf(ValidationWorkspaceSettings.Fixes, definition.fix) < 0
                )
                    error = "Choose a supported fix.";
            }
            failure = error;
            return error == null;
        }

        /// <inheritdoc />
        public string RuleId => _definition.id;

        /// <inheritdoc />
        public string DisplayName => _definition.name;

        /// <inheritdoc />
        public bool AppliesTo(in ValidationTarget target)
        {
            if (ValidationWorkspaceSettings.CategoryFor(target.AssetPath) != _definition.target)
            {
                return false;
            }
            string filter = (_definition.pathFilter ?? string.Empty).TrimEnd('/', '\\');
            return filter.Length == 0
                || string.Equals(target.AssetPath, filter, StringComparison.Ordinal)
                || target.AssetPath.StartsWith(filter + "/", StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public void Validate(
            in ValidationTarget target,
            Object asset,
            List<ValidationFinding> findings
        )
        {
            if (_definition.checks == null || _definition.checks.Count == 0 || findings == null)
            {
                return;
            }
            if (asset is SceneAsset)
            {
                Scene previous = SceneManager.GetActiveScene();
                Scene scene = SceneManager.GetSceneByPath(target.AssetPath);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened)
                    scene = EditorSceneManager.OpenScene(target.AssetPath, OpenSceneMode.Additive);
                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                        ValidateHierarchy(in target, root, findings);
                }
                finally
                {
                    if (opened)
                        EditorSceneManager.CloseScene(scene, true);
                    if (previous.IsValid() && previous.isLoaded)
                        SceneManager.SetActiveScene(previous);
                }
            }
            else if (asset is GameObject gameObject)
            {
                ValidateHierarchy(in target, gameObject, findings);
            }
            else
            {
                ValidateObject(in target, asset, string.Empty, findings);
            }
        }

        private void ValidateHierarchy(
            in ValidationTarget target,
            GameObject root,
            List<ValidationFinding> findings
        )
        {
            Type componentType = PrimaryComponentType(_definition);
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (componentType != null)
                {
                    foreach (Component component in transform.GetComponents(componentType))
                        ValidateObject(
                            in target,
                            component,
                            Identity(component, transform),
                            findings
                        );
                }
                else
                    ValidateObject(
                        in target,
                        transform.gameObject,
                        Identity(transform.gameObject, transform),
                        findings
                    );
            }
        }

        internal static Type PrimaryComponentType(
            ValidationWorkspaceSettings.RuleDefinition definition
        )
        {
            foreach (ValidationWorkspaceSettings.RuleCondition condition in definition.checks)
            {
                if (condition.property.StartsWith("AudioSource.", StringComparison.Ordinal))
                    return typeof(AudioSource);
                if (condition.property.StartsWith("Rigidbody.", StringComparison.Ordinal))
                    return typeof(Rigidbody);
                if (condition.property.StartsWith("Renderer.", StringComparison.Ordinal))
                    return typeof(Renderer);
                if (condition.property.StartsWith("Collider.", StringComparison.Ordinal))
                    return typeof(Collider);
            }
            return null;
        }

        private static string Identity(Object subject, Transform transform)
        {
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(subject);
            if (id.assetGUID.ToString() != "00000000000000000000000000000000")
                return id.ToString();
            string path = transform.GetSiblingIndex().ToString(CultureInfo.InvariantCulture);
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                path = parent.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + "/" + path;
            if (subject is Component component)
            {
                Component[] siblings = transform.GetComponents(component.GetType());
                path +=
                    ":" + Array.IndexOf(siblings, component).ToString(CultureInfo.InvariantCulture);
            }
            return "transient:" + path;
        }

        /// <summary>
        /// Replaces known serialized reference paths with stable identities while retaining all other values.
        /// </summary>
        internal static string NormalizeReferences(
            string json,
            IReadOnlyDictionary<string, string> references
        )
        {
            if (string.IsNullOrEmpty(json) || references == null || references.Count == 0)
                return json;
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                using PooledResource<PooledArrayBufferWriter> lease = PooledArrayBufferWriter.Rent(
                    out PooledArrayBufferWriter buffer
                );
                using Utf8JsonWriter writer = new Utf8JsonWriter(buffer);
                WriteNormalizedReferences(document.RootElement, string.Empty, references, writer);
                writer.Flush();
                return Encoding.UTF8.GetString(buffer.WrittenSpan);
            }
            catch (JsonException)
            {
                return json;
            }
        }

        private static void WriteNormalizedReferences(
            JsonElement element,
            string path,
            IReadOnlyDictionary<string, string> references,
            Utf8JsonWriter writer
        )
        {
            if (references.TryGetValue(path, out string identity))
            {
                writer.WriteStringValue(identity);
                return;
            }
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        string childPath = string.IsNullOrEmpty(path)
                            ? property.Name
                            : path + "." + property.Name;
                        WriteNormalizedReferences(property.Value, childPath, references, writer);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    int index = 0;
                    foreach (JsonElement child in element.EnumerateArray())
                    {
                        string childPath =
                            path
                            + ".Array.data["
                            + index.ToString(CultureInfo.InvariantCulture)
                            + "]";
                        WriteNormalizedReferences(child, childPath, references, writer);
                        index++;
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    writer.WriteRawValue(element.GetRawText());
                    break;
            }
        }

        internal static string Fingerprint(Object subject, string path)
        {
            Dictionary<string, string> references = new Dictionary<string, string>(
                StringComparer.Ordinal
            );
            using (SerializedObject serialized = new SerializedObject(subject))
            {
                SerializedProperty property = serialized.GetIterator();
                bool enterChildren = true;
                while (property.Next(enterChildren))
                {
                    // Managed-reference graphs may cycle and serialize through a separate JSON registry.
                    enterChildren =
                        property.propertyType != SerializedPropertyType.ManagedReference;
                    if (property.propertyType != SerializedPropertyType.ObjectReference)
                        continue;
                    Object reference = property.objectReferenceValue;
                    if (reference == null)
                        continue;
                    GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(reference);
                    if (
                        id.targetObjectId != 0
                        && id.assetGUID.ToString() != "00000000000000000000000000000000"
                    )
                        references[property.propertyPath] = id.ToString();
                }
            }
            string normalized = NormalizeReferences(EditorJsonUtility.ToJson(subject), references);
            return Hash128.Compute(normalized).ToString()
                + ":"
                + AssetDatabase.GetAssetDependencyHash(path);
        }

        internal bool MatchesSubject(Object subject, string path)
        {
            foreach (ValidationWorkspaceSettings.RuleCondition condition in _definition.checks)
                if (
                    !TryRead(subject, path, condition.property, out object value)
                    || !Matches(value, condition.comparison, condition.value)
                )
                    return false;
            return 0 < _definition.checks.Count;
        }

        private void ValidateObject(
            in ValidationTarget target,
            Object subject,
            string discriminator,
            List<ValidationFinding> findings
        )
        {
            if (!MatchesSubject(subject, target.AssetPath))
                return;
            if (string.IsNullOrEmpty(discriminator) && subject != null)
                discriminator = GlobalObjectId.GetGlobalObjectIdSlow(subject).ToString();
            findings.Add(
                new ValidationFinding(
                    RuleId,
                    _definition.severity,
                    subject,
                    target.AssetGuid,
                    target.AssetPath,
                    discriminator,
                    _definition.message,
                    subject == null ? string.Empty : Fingerprint(subject, target.AssetPath)
                )
            );
        }

        internal static bool Matches(object actual, string comparison, string expected)
        {
            bool isNull = actual == null || actual is Object unityObject && unityObject == null;
            if (comparison == "is null" || comparison == "is missing")
                return isNull;
            if (comparison == "contains")
                return !isNull
                    && Convert
                        .ToString(actual, CultureInfo.InvariantCulture)
                        .IndexOf(expected ?? string.Empty, StringComparison.Ordinal) != -1;
            if (isNull)
                return comparison == "=="
                        && string.Equals(expected, "null", StringComparison.OrdinalIgnoreCase)
                    || comparison == "!="
                        && !string.Equals(expected, "null", StringComparison.OrdinalIgnoreCase);
            string text = Convert.ToString(actual, CultureInfo.InvariantCulture);
            if (
                double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double number
                )
                && double.TryParse(
                    expected,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double wanted
                )
            )
            {
                switch (comparison)
                {
                    case "==":
                        return number == wanted;
                    case "!=":
                        return number != wanted;
                    case ">":
                        return wanted < number;
                    case "<":
                        return number < wanted;
                }
            }
            if (comparison == "==")
                return string.Equals(text, expected, StringComparison.OrdinalIgnoreCase);
            if (comparison == "!=")
                return !string.Equals(text, expected, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        private static bool RequiredFields(Object subject, ref bool found)
        {
            for (
                Type type = subject.GetType();
                type != null && type != typeof(Object);
                type = type.BaseType
            )
            {
                foreach (
                    FieldInfo field in type.GetFields(
                        BindingFlags.Instance
                            | BindingFlags.Public
                            | BindingFlags.NonPublic
                            | BindingFlags.DeclaredOnly
                    )
                )
                {
                    if (!field.IsDefined(typeof(WNotNullAttribute), true))
                        continue;
                    found = true;
                    object value = field.GetValue(subject);
                    if (value is IEnumerable collection && !(value is string))
                    {
                        foreach (object element in collection)
                            if (EmptyRequirement(element))
                                return true;
                    }
                    else if (
                        !typeof(IEnumerable).IsAssignableFrom(field.FieldType)
                        || field.FieldType == typeof(string)
                    )
                    {
                        if (EmptyRequirement(value))
                            return true;
                    }
                }
            }
            return false;
        }

        private static bool EmptyRequirement(object value)
        {
            return value == null
                || value is Object unityObject && unityObject == null
                || value is string text && string.IsNullOrEmpty(text);
        }

        private static bool TryRead(Object subject, string path, string property, out object value)
        {
            GameObject gameObject = subject is Component subjectComponent
                ? subjectComponent.gameObject
                : subject as GameObject;
            switch (property)
            {
                case "[Required] fields":
                    bool found = false;
                    if (gameObject != null)
                    {
                        foreach (
                            MonoBehaviour component in gameObject.GetComponents<MonoBehaviour>()
                        )
                        {
                            if (component != null && RequiredFields(component, ref found))
                            {
                                value = null;
                                return true;
                            }
                        }
                    }
                    else if (subject != null && RequiredFields(subject, ref found))
                    {
                        value = null;
                        return true;
                    }
                    value = found ? "assigned" : null;
                    return found;
                case "AudioSource.spatialBlend":
                case "AudioSource.clip.channels":
                    AudioSource audio = subject as AudioSource;
                    if (
                        audio == null
                        && (gameObject == null || !gameObject.TryGetComponent(out audio))
                    )
                    {
                        value = null;
                        return false;
                    }
                    object audioValue = null;
                    if (property == "AudioSource.spatialBlend")
                        audioValue = audio.spatialBlend;
                    else if (audio.clip != null)
                        audioValue = audio.clip.channels;
                    value = audioValue;
                    return true;
                case "Rigidbody.mass":
                    Rigidbody body = subject as Rigidbody;
                    if (
                        body == null
                        && (gameObject == null || !gameObject.TryGetComponent(out body))
                    )
                    {
                        value = null;
                        return false;
                    }
                    value = body.mass;
                    return true;
                case "Renderer.sharedMaterial":
                    Renderer renderer = subject as Renderer;
                    if (
                        renderer == null
                        && (gameObject == null || !gameObject.TryGetComponent(out renderer))
                    )
                    {
                        value = null;
                        return false;
                    }
                    value = renderer.sharedMaterial;
                    return true;
                case "Transform.localScale.y":
                    if (gameObject == null)
                    {
                        value = null;
                        return false;
                    }
                    value = gameObject.transform.localScale.y;
                    return true;
                case "Collider.isTrigger":
                    Collider collider = subject as Collider;
                    if (
                        collider == null
                        && (gameObject == null || !gameObject.TryGetComponent(out collider))
                    )
                    {
                        value = null;
                        return false;
                    }
                    value = collider.isTrigger;
                    return true;
                case "Texture.maxSize":
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                    {
                        value = null;
                        return false;
                    }
                    value = importer.maxTextureSize;
                    return true;
                default:
                    value = null;
                    return false;
            }
        }
    }
#endif
}
