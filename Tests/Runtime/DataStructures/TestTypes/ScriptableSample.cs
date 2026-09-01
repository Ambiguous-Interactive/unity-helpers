// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    // Internal rather than private so the generated registrar can name the marshalled
    // collection closed over it. A private nested type is skipped with WPROTO028 and
    // would throw on its first WallstopProto serialization.
    internal sealed class ScriptableSample
        : ScriptableObject,
            IComparable<ScriptableSample>,
            IComparable
    {
        public int CompareTo(ScriptableSample other)
        {
            if (other == null)
            {
                return 1;
            }

            return UnityObjectNameComparer<ScriptableSample>.Instance.Compare(this, other);
        }

        public int CompareTo(object obj)
        {
            if (obj is ScriptableSample other)
            {
                return CompareTo(other);
            }

            return -1;
        }
    }
}
