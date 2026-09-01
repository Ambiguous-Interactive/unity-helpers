// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Complex value type for testing nested serialization in ScriptableSingleton tests.
    /// </summary>
    [Serializable]
    internal sealed class SingletonComplexValue
    {
        public string name;
        public int count;
        public Color color;
    }

    /// <summary>
    /// Complex set element for testing nested serialization in ScriptableSingleton tests.
    /// </summary>
    [Serializable]
    internal sealed class SingletonSetElement : IEquatable<SingletonSetElement>
    {
        public string id;
        public float value;

        public bool Equals(SingletonSetElement other)
        {
            if (other is null)
            {
                return false;
            }
            return id == other.id;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SingletonSetElement);
        }

        public override int GetHashCode()
        {
            return id?.GetHashCode() ?? 0;
        }
    }
}
