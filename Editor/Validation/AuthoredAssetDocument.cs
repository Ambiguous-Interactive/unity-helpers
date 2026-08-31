// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One <c>--- !u!</c> document of a committed <c>.unity</c>, <c>.prefab</c> or <c>.asset</c>.
    /// </summary>
    /// <remarks>
    /// A document is the unit every authored-asset check works in: it carries the
    /// <see cref="ScriptGuid"/> that names the type, and the entries whose values the check judges.
    /// </remarks>
    public sealed class AuthoredAssetDocument
    {
        /// <summary>The key Unity writes a behaviour's script reference under.</summary>
        public const string ScriptKey = "m_Script";

        /// <summary>Initializes a new instance of the <see cref="AuthoredAssetDocument"/> class.</summary>
        /// <param name="fileId">The document's anchor, or zero when it carried none.</param>
        /// <param name="unityTypeId">The number after <c>!u!</c>; 114 for a <c>MonoBehaviour</c>.</param>
        /// <param name="rootKey">The single key the document's body hangs from.</param>
        /// <param name="isStripped">Whether the header carried the <c>stripped</c> marker.</param>
        /// <param name="startLineNumber">The one-based line the <c>--- !u!</c> header is on.</param>
        /// <param name="endLineNumber">The one-based line after the document's last line.</param>
        /// <param name="entries">Every key the document declares, in the order Unity wrote them.</param>
        public AuthoredAssetDocument(
            long fileId,
            int unityTypeId,
            string rootKey,
            bool isStripped,
            int startLineNumber,
            int endLineNumber,
            IReadOnlyList<AuthoredAssetEntry> entries
        )
        {
            FileId = fileId;
            UnityTypeId = unityTypeId;
            RootKey = rootKey;
            IsStripped = isStripped;
            StartLineNumber = startLineNumber;
            EndLineNumber = endLineNumber;
            Entries = entries ?? Array.Empty<AuthoredAssetEntry>();

            if (
                TryGetEntry(ScriptKey, out AuthoredAssetEntry script)
                && AuthoredAssetYaml.TryParseObjectReference(
                    script.InlineValue,
                    out _,
                    out string guid
                )
            )
            {
                ScriptGuid = guid;
            }
        }

        /// <summary>The document's anchor, or zero when it carried none.</summary>
        public long FileId { get; }

        /// <summary>The number after <c>!u!</c>; 114 is a <c>MonoBehaviour</c>, 1 a <c>GameObject</c>.</summary>
        public int UnityTypeId { get; }

        /// <summary>The single key the document's body hangs from, such as <c>MonoBehaviour</c>.</summary>
        public string RootKey { get; }

        /// <summary>
        /// Whether the header carried the <c>stripped</c> marker, which a prefab instance writes for
        /// a document whose real content lives in the prefab it points at.
        /// </summary>
        public bool IsStripped { get; }

        /// <summary>The one-based line the <c>--- !u!</c> header is on.</summary>
        public int StartLineNumber { get; }

        /// <summary>The one-based line after the document's last line.</summary>
        public int EndLineNumber { get; }

        /// <summary>
        /// Every key the document declares at any depth, in the order Unity wrote them.
        /// </summary>
        /// <remarks>
        /// Nested keys are included because a serialized field of a <c>[Serializable]</c> type
        /// writes its own keys one level in, and a check that only looked at the document's own
        /// fields would report clean for every nested one.
        /// </remarks>
        public IReadOnlyList<AuthoredAssetEntry> Entries { get; }

        /// <summary>
        /// The guid of the <c>MonoScript</c> this document's <c>m_Script</c> names, or <c>null</c>
        /// when the document carries no script reference.
        /// </summary>
        public string ScriptGuid { get; }

        /// <summary>
        /// Finds the shallowest entry named <paramref name="key"/>, preferring the first Unity wrote.
        /// </summary>
        /// <param name="key">The key to look for.</param>
        /// <param name="entry">Receives the entry when one matched.</param>
        /// <returns><c>true</c> when an entry matched.</returns>
        /// <remarks>
        /// Shallowest rather than first, because a nested <c>[Serializable]</c> field can declare a
        /// key the document itself also declares, and the document's own field is the one a caller
        /// asking by name means.
        /// </remarks>
        public bool TryGetEntry(string key, out AuthoredAssetEntry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            bool found = false;
            for (int index = 0; index < Entries.Count; ++index)
            {
                AuthoredAssetEntry candidate = Entries[index];
                if (!string.Equals(candidate.Key, key, StringComparison.Ordinal))
                {
                    continue;
                }

                if (found && entry.Indent <= candidate.Indent)
                {
                    continue;
                }

                entry = candidate;
                found = true;
            }

            return found;
        }

        /// <summary>Renders the document as its header line, for a finding a reader can open.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            return $"line {StartLineNumber}: --- !u!{UnityTypeId} &{FileId}";
        }
    }
#endif
}
