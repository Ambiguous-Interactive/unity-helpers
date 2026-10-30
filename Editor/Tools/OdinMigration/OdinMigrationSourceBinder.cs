// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration;

    /// <summary>Matches retained field schemas to immutable editor compiler metadata.</summary>
    internal static class OdinMigrationSourceBinder
    {
        internal const int MaximumSourceCharacters = 4 * 1024 * 1024;
        private const int SourceReadBufferCharacters = 4096;

        internal static bool TryBind(
            OdinMigrationSerializedData payload,
            IReadOnlyList<OdinMigrationDictionaryField> fields,
            out OdinMigrationCompiledBinding binding,
            out string failure
        )
        {
            try
            {
                if (
                    payload == null
                    || fields == null
                    || fields.Count == 0
                    || string.IsNullOrWhiteSpace(payload.ScriptGuid)
                )
                {
                    throw new InvalidDataException(
                        "A payload script GUID and dictionary field schemas are required."
                    );
                }
                string scriptPath = AssetDatabase.GUIDToAssetPath(payload.ScriptGuid);
                if (
                    string.IsNullOrWhiteSpace(scriptPath)
                    || !scriptPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                )
                {
                    throw new InvalidDataException(
                        "The retained script GUID does not name a source script."
                    );
                }
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                Type originalType = script == null ? null : script.GetClass();
                if (
                    originalType == null
                    || !OdinMigrationCompiledBindings.TryGet(
                        originalType,
                        out OdinMigrationCompiledBinding compiled
                    )
                )
                {
                    throw new InvalidDataException(
                        "The retained script has no current compiler binding."
                    );
                }
                Dictionary<string, string> texts = new Dictionary<string, string>(
                    StringComparer.Ordinal
                );
                bool scriptCaptured = false;
                string scriptText = ReadSource(FullPath(scriptPath));
                string scriptChecksum = Checksum(scriptText);
                foreach (
                    OdinMigrationCompiledSource source in compiled.Sources.AsValueEnumerableRequired()
                )
                {
                    if (
                        source == null
                        || string.IsNullOrWhiteSpace(source.Path)
                        || string.IsNullOrWhiteSpace(source.Checksum)
                    )
                    {
                        throw new InvalidDataException("A compiled source snapshot is incomplete.");
                    }
                    string text = ReadCompiledSource(source);
                    texts[source.Path] = text;
                    bool checksumMatches = string.Equals(
                        scriptChecksum,
                        source.Checksum,
                        StringComparison.Ordinal
                    );
                    scriptCaptured = scriptCaptured || checksumMatches;
                }
                if (!scriptCaptured)
                {
                    throw new InvalidDataException(
                        "The retained script is not part of the compiled source snapshot."
                    );
                }
                HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
                foreach (OdinMigrationDictionaryField field in fields.AsValueEnumerableRequired())
                {
                    if (field == null || !names.Add(field.FieldName))
                    {
                        throw new InvalidDataException(
                            "Original field names must be non-null and unique."
                        );
                    }
                    OdinMigrationCompiledField matched = null;
                    foreach (
                        OdinMigrationCompiledField declaration in compiled.Fields.AsValueEnumerableRequired()
                    )
                    {
                        if (
                            declaration == null
                            || !string.Equals(
                                declaration.Name,
                                field.FieldName,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            continue;
                        }
                        if (matched != null)
                        {
                            throw new InvalidDataException(
                                "A field name is ambiguous across compiled declarations."
                            );
                        }
                        matched = declaration;
                    }
                    if (
                        matched == null
                        || field.KeyType == null
                        || field.ElementType == null
                        || !string.Equals(
                            matched.KeyTypeName,
                            field.KeyType.FullName,
                            StringComparison.Ordinal
                        )
                        || !string.Equals(
                            matched.ElementTypeName,
                            field.ElementType.FullName,
                            StringComparison.Ordinal
                        )
                        || !string.Equals(
                            matched.KeyAssemblyName,
                            field.KeyType.Assembly.FullName,
                            StringComparison.Ordinal
                        )
                        || !string.Equals(
                            matched.ElementAssemblyName,
                            field.ElementType.Assembly.FullName,
                            StringComparison.Ordinal
                        )
                        || !texts.TryGetValue(matched.SourcePath, out string sourceText)
                        || matched.TypeStart < 0
                        || matched.TypeLength <= 0
                        || sourceText.Length - matched.TypeStart < matched.TypeLength
                    )
                    {
                        throw new InvalidDataException(
                            "A requested field does not match its compiled original declaration."
                        );
                    }
                }
                binding = compiled;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                binding = null;
                failure = exception.Message;
                return false;
            }
        }

        internal static string ReadCompiledSource(OdinMigrationCompiledSource source)
        {
            if (
                source == null
                || string.IsNullOrWhiteSpace(source.Path)
                || string.IsNullOrWhiteSpace(source.Checksum)
            )
            {
                throw new InvalidDataException("A compiled source snapshot is incomplete.");
            }
            string text = ReadSource(FullPath(source.Path));
            if (!string.Equals(Checksum(text), source.Checksum, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "A source file changed after its binding was compiled."
                );
            }
            return text;
        }

        internal static string FullPath(string compilerPath)
        {
            string path = compilerPath.Replace('\\', Path.DirectorySeparatorChar);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(
                Path.IsPathRooted(path) ? path : Path.Combine(projectRoot, path)
            );
        }

        internal static string ReadSource(string path)
        {
            StringBuilder text = new StringBuilder();
            char[] buffer = new char[SourceReadBufferCharacters];
            using (
                StreamReader reader = new StreamReader(path, new UTF8Encoding(false, true), true)
            )
            {
                int read;
                while (0 < (read = reader.Read(buffer, 0, buffer.Length)))
                {
                    if (MaximumSourceCharacters - text.Length < read)
                    {
                        throw new InvalidDataException(
                            "A source file exceeds the binding size limit."
                        );
                    }
                    text.Append(buffer, 0, read);
                }
            }
            return text.ToString();
        }

        internal static string Checksum(string source)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(source)));
            }
        }
    }
}
