// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    /// <summary>Preserves a dictionary field's compiler-resolved types and source location.</summary>
    public sealed class OdinMigrationCompiledField
    {
        /// <summary>Gets the declared field name.</summary>
        public string Name { get; }

        /// <summary>Gets the original enum's CLR metadata name.</summary>
        public string KeyTypeName { get; }

        /// <summary>Gets the original Unity element's CLR metadata name.</summary>
        public string ElementTypeName { get; }

        /// <summary>Gets the enum's original assembly identity.</summary>
        public string KeyAssemblyName { get; }

        /// <summary>Gets the element's original assembly identity.</summary>
        public string ElementAssemblyName { get; }

        /// <summary>Gets the source path containing the field declaration.</summary>
        public string SourcePath { get; }

        /// <summary>Gets the declared type's first source character.</summary>
        public int TypeStart { get; }

        /// <summary>Gets the declared type's source character count.</summary>
        public int TypeLength { get; }

        /// <summary>Gets compiler-resolved source replacements for this field.</summary>
        public IReadOnlyList<OdinMigrationCompiledSourceEdit> SourceEdits { get; }

        /// <summary>Gets an original source shape that prevents an automatic rewrite.</summary>
        public string RewriteFailure { get; }

        /// <summary>Gets whether compilation supplied direct access to the original live field.</summary>
        public bool CanCapture => _capture != null;

        /// <summary>Gets whether compilation supplied direct recovery access to the original field.</summary>
        public bool CanRestore => _restore != null;

        private readonly OdinMigrationDictionaryCapture _capture;
        private readonly OdinMigrationDictionaryRestore _restore;

        /// <summary>Captures the original field identity emitted by the compiler.</summary>
        public OdinMigrationCompiledField(
            string name,
            string keyTypeName,
            string elementTypeName,
            string keyAssemblyName,
            string elementAssemblyName,
            string sourcePath,
            int typeStart,
            int typeLength,
            OdinMigrationCompiledSourceEdit[] sourceEdits = null,
            string rewriteFailure = null,
            OdinMigrationDictionaryCapture capture = null,
            OdinMigrationDictionaryRestore restore = null
        )
        {
            Name = name;
            KeyTypeName = keyTypeName;
            ElementTypeName = elementTypeName;
            KeyAssemblyName = keyAssemblyName;
            ElementAssemblyName = elementAssemblyName;
            SourcePath = sourcePath;
            TypeStart = typeStart;
            TypeLength = typeLength;
            SourceEdits = Array.AsReadOnly(
                sourceEdits == null
                    ? Array.Empty<OdinMigrationCompiledSourceEdit>()
                    : (OdinMigrationCompiledSourceEdit[])sourceEdits.Clone()
            );
            RewriteFailure = rewriteFailure;
            _capture = capture;
            _restore = restore;
        }

        /// <summary>Captures the original live field without reflection, asset loading, or writes.</summary>
        /// <param name="owner">The live original owner whose field will be copied.</param>
        /// <param name="snapshot">The immutable dictionary snapshot, or null on failure.</param>
        /// <param name="failure">The inaccessible field, invalid owner, or capture failure.</param>
        /// <returns>Whether the complete original dictionary was captured.</returns>
        public bool TryCapture(
            UnityEngine.Object owner,
            out OdinMigrationLiveDictionary snapshot,
            out string failure
        )
        {
            try
            {
                if (owner == null || _capture == null)
                {
                    snapshot = null;
                    failure = "A live owner and generated dictionary reader are required.";
                    return false;
                }
                if (
                    !_capture(
                        owner,
                        out OdinMigrationLiveDictionary captured,
                        out string captureFailure
                    )
                )
                {
                    snapshot = null;
                    failure = captureFailure;
                    return false;
                }
                if (captured == null)
                {
                    snapshot = null;
                    failure = "The dictionary reader returned no snapshot.";
                    return false;
                }
                snapshot = captured;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                snapshot = null;
                failure = exception.Message;
                return false;
            }
        }

        /// <summary>Restores original captured values without reflection or saving an asset.</summary>
        public bool TryRestore(
            UnityEngine.Object owner,
            IReadOnlyList<int> keys,
            IReadOnlyList<IReadOnlyList<UnityEngine.Object>> values,
            out string failure
        )
        {
            try
            {
                if (owner == null || _restore == null)
                {
                    failure =
                        "A live owner and a compiler-generated dictionary recovery writer are required.";
                    return false;
                }
                return _restore(owner, keys, values, out failure);
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }
    }
#endif
}
