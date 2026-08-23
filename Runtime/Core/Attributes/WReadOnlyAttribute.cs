// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using PropertyAttribute = UnityEngine.PropertyAttribute;

    // Fields only, declared rather than inherited: UnityEngine.PropertyAttribute allows
    // AttributeTargets.Property as well, and nothing in this package reads a C# property --
    // every drawer is reached through a Unity SerializedProperty, which exists for serialized
    // fields only. Inheriting the base's targets also let them drift with the editor version.
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class WReadOnlyAttribute : PropertyAttribute { }
}
