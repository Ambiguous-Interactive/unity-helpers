// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;

    /// <summary>
    /// What happened to one asset a stale-key repair was attempted on.
    /// </summary>
    public enum StaleSerializedKeyRepairOutcome
    {
        /// <summary>Unused.</summary>
        [Obsolete("A repair attempt always names an outcome.")]
        Unknown = 0,

        /// <summary>The asset was rewritten and came back with everything it went in with.</summary>
        Repaired = 1,

        /// <summary>
        /// The asset could not be read or loaded, so there is nothing to compare a rewrite against.
        /// </summary>
        RefusedUnreadable = 2,

        /// <summary>
        /// The rewrite came back with fewer objects than it went in with, and the original bytes
        /// were put back. A profile whose content lives in sub-objects can lose all of them this
        /// way while the rewrite reports success.
        /// </summary>
        RefusedLostSubObjects = 3,

        /// <summary>
        /// The rewrite left the file byte-identical, so nothing was repaired and nothing was risked.
        /// </summary>
        NotRewritten = 4,

        /// <summary>
        /// The rewrite was refused and putting the original bytes back also failed, so the file on
        /// disk holds the rewritten content. The worst outcome here, and the only one that needs a
        /// human: restore from source control before saving the project.
        /// </summary>
        RefusedUndoFailed = 5,

        /// <summary>
        /// The asset was read and loaded, and the rewrite itself threw. The original bytes were put
        /// back. The exception's message is logged as an error naming the asset.
        /// </summary>
        RefusedRewriteThrew = 6,
    }
#endif
}
