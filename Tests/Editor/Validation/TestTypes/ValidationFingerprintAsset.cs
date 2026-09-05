// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes
{
    using System;
    using UnityEngine;

    internal sealed class ValidationFingerprintAsset
        : ScriptableObject,
            ISerializationCallbackReceiver
    {
        public int instanceID;
        public bool copyReferenceBeforeSerialization;

        [NonSerialized]
        public UnityEngine.Object pendingReference;

        public ReferenceSlot nested = new ReferenceSlot();
        public ReferenceSlot[] slots = Array.Empty<ReferenceSlot>();

        [SerializeReference]
        public ManagedNode managed;

        [SerializeReference]
        public ManagedNode shared;

        /// <summary>Publishes the requested reference before Unity serializes the asset.</summary>
        public void OnBeforeSerialize()
        {
            if (copyReferenceBeforeSerialization)
                nested.reference = pendingReference;
        }

        /// <summary>Leaves deserialized fixture fields unchanged.</summary>
        public void OnAfterDeserialize() { }

        [Serializable]
        internal sealed class ReferenceSlot
        {
            public int instanceID;
            public UnityEngine.Object reference;
        }

        [Serializable]
        internal sealed class ManagedNode
        {
            public int instanceID;
            public UnityEngine.Object reference;

            [SerializeReference]
            public ManagedNode next;
        }
    }
}
