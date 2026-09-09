// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Settings
{
    using UnityEngine;
    using UnityEngine.Serialization;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// ScriptableObject that stores default wait-instruction buffer settings. Serialized under Resources so the runtime can apply the defaults automatically.
    /// </summary>
    public sealed class UnityHelpersBufferSettingsAsset : ScriptableObject
    {
        /// <summary>
        /// Resource path (used with Resources.Load) where the settings asset is stored.
        /// </summary>
        public const string ResourcePath =
            "Wallstop Studios/Unity Helpers/UnityHelpersBufferSettings";

        /// <summary>
        /// AssetDatabase path used by the editor to create/manage the asset.
        /// </summary>
        public const string AssetPath =
            "Assets/Resources/Wallstop Studios/Unity Helpers/UnityHelpersBufferSettings.asset";

        internal const string ApplyOnLoadPropertyName = nameof(_applyOnLoad);
        internal const string QuantizationStepSecondsPropertyName = nameof(
            _waitInstructionQuantizationStepSeconds
        );
        internal const string MaxDistinctEntriesPropertyName = nameof(
            _waitInstructionMaxDistinctEntries
        );
        internal const string UseLruEvictionPropertyName = nameof(_waitInstructionUseLruEviction);

        /// <summary>
        /// Gets whether the defaults should be applied automatically on domain/runtime load.
        /// </summary>
        public bool ApplyOnLoad => _applyOnLoad;

        /// <summary>
        /// Gets the sanitized quantization step (0 disables quantization).
        /// </summary>
        public float QuantizationStepSeconds =>
            SanitizeQuantization(_waitInstructionQuantizationStepSeconds);

        /// <summary>
        /// Gets the sanitized distinct entry limit (0 = unbounded).
        /// </summary>
        public int MaxDistinctEntries =>
            SanitizeMaxDistinctEntries(_waitInstructionMaxDistinctEntries);

        /// <summary>
        /// Gets whether LRU eviction should be enabled when the cache hits the distinct entry limit.
        /// </summary>
        public bool UseLruEviction => _waitInstructionUseLruEviction;

        [FormerlySerializedAs("applyOnLoad")]
        [SerializeField]
        private bool _applyOnLoad = true;

        [FormerlySerializedAs("waitInstructionQuantizationStepSeconds")]
        [SerializeField]
        [Min(0f)]
        private float _waitInstructionQuantizationStepSeconds;

        [FormerlySerializedAs("waitInstructionMaxDistinctEntries")]
        [SerializeField]
        [Min(0)]
        private int _waitInstructionMaxDistinctEntries =
            Buffers.WaitInstructionDefaultMaxDistinctEntries;

        [FormerlySerializedAs("waitInstructionUseLruEviction")]
        [SerializeField]
        private bool _waitInstructionUseLruEviction;

        private static float SanitizeQuantization(float step)
        {
            if (float.IsNaN(step) || float.IsInfinity(step) || step <= 0f)
            {
                return 0f;
            }

            return step;
        }

        private static int SanitizeMaxDistinctEntries(int value)
        {
            return value < 0 ? 0 : value;
        }

        /// <summary>
        /// Applies the stored defaults to the Buffers wait-instruction caches.
        /// </summary>
        public void ApplyToBuffers()
        {
            Buffers.WaitInstructionQuantizationStepSeconds = QuantizationStepSeconds;
            Buffers.WaitInstructionMaxDistinctEntries = MaxDistinctEntries;
            Buffers.WaitInstructionUseLruEviction = _waitInstructionUseLruEviction;
        }

        /// <summary>
        /// Copies the current Buffers configuration into this asset. Useful when seeding defaults from code.
        /// </summary>
        public void SyncFromRuntime()
        {
            _waitInstructionQuantizationStepSeconds =
                Buffers.WaitInstructionQuantizationStepSeconds;
            _waitInstructionMaxDistinctEntries = Buffers.WaitInstructionMaxDistinctEntries;
            _waitInstructionUseLruEviction = Buffers.WaitInstructionUseLruEviction;
        }
    }
}
