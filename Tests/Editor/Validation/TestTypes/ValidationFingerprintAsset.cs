// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes
{
    using System;
    using UnityEngine;

    internal sealed class ValidationFingerprintAsset : ScriptableObject
    {
        public int instanceID;
        public ReferenceSlot nested = new ReferenceSlot();
        public ReferenceSlot[] slots = Array.Empty<ReferenceSlot>();

        [SerializeReference]
        public ManagedNode managed;

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

            [SerializeReference]
            public ManagedNode next;
        }
    }
}
