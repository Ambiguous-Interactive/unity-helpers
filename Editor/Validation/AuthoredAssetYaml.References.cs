// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using UnityEditor;

    public static partial class AuthoredAssetYaml
    {
        /// <summary>The project-relative folder containing authored Unity assets.</summary>
        public const string AssetsRoot = "Assets";

        /// <summary>The document root used for ordinary MonoBehaviour assets.</summary>
        public const string MonoBehaviourType = nameof(UnityEngine.MonoBehaviour);

        /// <summary>The document root used for ordinary Transform components.</summary>
        public const string TransformType = nameof(UnityEngine.Transform);

        /// <summary>The document root used for RectTransform components.</summary>
        public const string RectTransformType = nameof(UnityEngine.RectTransform);

        /// <summary>Extensions containing authored scenes and prefabs.</summary>
        public static IReadOnlyList<string> SceneExtensions { get; } =
            Array.AsReadOnly(new[] { ".unity", ".prefab" });

        /// <summary>Extensions containing standalone authored assets.</summary>
        public static IReadOnlyList<string> AssetExtensions { get; } =
            Array.AsReadOnly(new[] { ".asset" });

        /// <summary>All supported authored Unity file extensions.</summary>
        public static IReadOnlyList<string> AllExtensions => AuthoredExtensions;

        /// <summary>Reads a reference's signed file ID as invariant text, or an empty string on failure.</summary>
        public static string FileId(string reference)
        {
            return
                TryParseObjectReference(
                    reference,
                    out long fileId,
                    out string _,
                    out bool hasFileId
                ) && hasFileId
                ? fileId.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        /// <summary>Resolves an external or same-file reference to an asset GUID.</summary>
        /// <remarks>Invalid references, null references, and unresolved local references return an empty string.</remarks>
        public static string ReferencedGuid(string reference, string owningAssetGuid)
        {
            return
                TryResolveObjectReference(
                    reference,
                    owningAssetGuid,
                    out long fileId,
                    out string guid
                )
                && fileId != 0
                ? guid ?? string.Empty
                : string.Empty;
        }

        /// <summary>Reads a single inline object-reference mapping from a field or sequence line.</summary>
        /// <remarks>Quoted values, multiple mappings, missing file IDs, and trailing non-comment text are refused.</remarks>
        public static bool TryGetInlineReference(string line, out string reference)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                reference = null;
                return false;
            }
            string content = KeyOf(line);
            if (!content.StartsWith("{", StringComparison.Ordinal))
            {
                if (!TrySplitEntry(content, out string _, out string inlineValue))
                {
                    reference = null;
                    return false;
                }
                content = inlineValue;
            }
            if (!content.StartsWith("{", StringComparison.Ordinal))
            {
                reference = null;
                return false;
            }
            int close = content.IndexOf('}');
            if (close < 0)
            {
                reference = null;
                return false;
            }
            string suffix = content.Substring(close + 1).TrimStart();
            if (suffix.Length != 0 && suffix[0] != '#')
            {
                reference = null;
                return false;
            }
            string candidate = content.Substring(0, close + 1);
            if (
                !TryParseObjectReference(candidate, out long _, out string _, out bool hasFileId)
                || !hasFileId
            )
            {
                reference = null;
                return false;
            }
            reference = candidate;
            return true;
        }

        /// <summary>Returns an asset's filename without loading it, or the GUID when no asset is known.</summary>
        public static string AssetName(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return string.Empty;
            }
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrWhiteSpace(path) ? guid : Path.GetFileNameWithoutExtension(path);
        }
    }
#endif
}
