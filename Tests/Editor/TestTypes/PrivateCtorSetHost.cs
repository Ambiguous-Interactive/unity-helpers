// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    internal sealed class PrivateCtorSetHost : ScriptableObject
    {
        public SerializableHashSet<PrivateCtorElement> set = new();
    }

    [Serializable]
    internal sealed class PrivateCtorElement
    {
        // ReSharper disable once UnusedMember.Local
        public int Magnitude => magnitude;

        [SerializeField]
        private int magnitude;

        private PrivateCtorElement()
        {
            magnitude = 5;
        }
    }
}
