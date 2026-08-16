// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using UnityEngine.Scripting;

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    [Preserve]
    public sealed class ScriptableSingletonPathAttribute : Attribute
    {
        public readonly string resourcesPath;

        public ScriptableSingletonPathAttribute(string resourcesPath)
        {
            this.resourcesPath = resourcesPath ?? string.Empty;
        }
    }
}
