// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using UnityEngine;

    /// <summary>
    /// Prefab-borne probe for
    /// <see href="https://github.com/wallstop/unity-helpers/issues/280">#280</see>. Counts every
    /// <c>OnValidate</c> Unity runs on it, so a test can assert that a prefab was never
    /// deserialized rather than merely that the deserialization was quiet, and can optionally
    /// reproduce the consumer symptom by calling an API Unity answers with
    /// "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate".
    /// </summary>
    /// <remarks>
    /// This class must live in a non-Editor folder so it can be attached to a prefab asset,
    /// matching <see cref="TestPrefabAssetChangeHandler"/>.
    /// </remarks>
    public sealed class TestOnValidateCountingComponent : MonoBehaviour
    {
        /// <summary>
        /// Number of times Unity has invoked <c>OnValidate</c> on any instance of this component
        /// since the last <see cref="Clear"/>. Anything that deserializes the owning prefab drives
        /// this above zero.
        /// </summary>
        public static int OnValidateCount { get; private set; }

        /// <summary>
        /// When <see langword="true"/>, <c>OnValidate</c> sends a message, which Unity answers with
        /// the #280 warning. Off by default so the probe prefab's own creation and import stay
        /// quiet and only the window a test explicitly opens can produce the warning.
        /// </summary>
        public static bool EmitSendMessageDuringValidate { get; set; }

        /// <summary>
        /// Resets every static surface this probe owns.
        /// </summary>
        public static void Clear()
        {
            OnValidateCount = 0;
            EmitSendMessageDuringValidate = false;
        }

        private void OnValidate()
        {
            OnValidateCount++;
            if (EmitSendMessageDuringValidate)
            {
                gameObject.SendMessage(
                    nameof(OnProbeMessage),
                    SendMessageOptions.DontRequireReceiver
                );
            }
        }

        private void OnProbeMessage() { }
    }
}
#endif
