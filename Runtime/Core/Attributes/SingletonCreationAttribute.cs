// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Decides whether <see cref="WallstopStudios.UnityHelpers.Utils.RuntimeSingleton{T}.Instance"/>
    /// may create the singleton when none exists in any loaded scene.
    /// </summary>
    /// <remarks>
    /// A singleton authored in a scene or a prefab holds serialized state, and a created stand-in has
    /// every <c>[SerializeField]</c> left at its default - so it behaves like the real thing while
    /// carrying none of the data. Annotate those with
    /// <see cref="SingletonCreationPolicy.NeverCreate"/> so a missing instance is reported instead of
    /// substituted. Leave a singleton that holds no serialized state alone; creating one on demand is
    /// what makes <c>Instance</c> work from anywhere.
    ///
    /// The decision is read from the type rather than from an overridable member because there is no
    /// instance to ask when the question is whether to make one.
    /// </remarks>
    /// <example>
    /// <code>
    /// [SingletonCreation(SingletonCreationPolicy.NeverCreate)]
    /// public sealed class SaveManager : RuntimeSingleton&lt;SaveManager&gt;
    /// {
    ///     [SerializeField]
    ///     private SaveSettings _settings;
    /// }
    ///
    /// // Returns null instead of a SaveManager with no settings.
    /// SaveManager manager = SaveManager.Instance;
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class SingletonCreationAttribute : Attribute
    {
        /// <summary>
        /// Gets the policy <c>Instance</c> applies when no instance exists.
        /// </summary>
        public SingletonCreationPolicy Policy { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SingletonCreationAttribute"/> class.
        /// </summary>
        /// <param name="policy">
        /// What <c>Instance</c> should do when no instance of the singleton exists.
        /// </param>
        public SingletonCreationAttribute(SingletonCreationPolicy policy)
        {
            Policy = policy;
        }
    }
}
