// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Marks a <see cref="WallstopStudios.UnityHelpers.Utils.RuntimeSingleton{T}"/> or
    /// <see cref="WallstopStudios.UnityHelpers.Utils.ScriptableObjectSingleton{T}"/> so it is automatically instantiated during Unity start-up.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class AutoLoadSingletonAttribute : Attribute
    {
        /// <summary>
        /// Gets the Unity load phase that should trigger instantiation. The editor serializes this into <see cref="Tags.AttributeMetadataCache"/>
        /// so <see cref="Core.Helper.SingletonAutoLoader"/> can reflectively touch the singleton at runtime.
        /// </summary>
        public RuntimeInitializeLoadType LoadType { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AutoLoadSingletonAttribute"/> class.
        /// </summary>
        /// <param name="loadType">
        /// Unity load phase that should trigger instantiation. Defaults to <see cref="RuntimeInitializeLoadType.BeforeSplashScreen"/>.
        /// </param>
        public AutoLoadSingletonAttribute(
            RuntimeInitializeLoadType loadType = RuntimeInitializeLoadType.BeforeSplashScreen
        )
        {
            LoadType = loadType;
        }
    }
}
