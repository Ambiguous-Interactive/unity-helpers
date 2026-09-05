// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Draws an integer field as a dropdown list using a predefined set of numeric options.
    /// Designed for authoring config objects, ScriptableObjects, and MonoBehaviours that should be restricted to a known set of valid identifiers (layer masks, level IDs, refresh rates, etc.).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attribute supports inline value lists, static providers resolved through reflection, and instance method providers.
    /// Late-binding enables values to track data sets (for example, enums translated to int IDs or editor preferences stored elsewhere).
    /// </para>
    /// <para>
    /// This attribute is implemented using the same underlying infrastructure as <see cref="WValueDropDownAttribute"/>,
    /// specialized for integer values with a type-safe API.
    /// </para>
    /// </remarks>
    /// <example>
    /// Inline list:
    /// <code>
    /// [IntDropDown(0, 30, 60, 120)]
    /// public int frameRate;
    /// </code>
    /// Static provider:
    /// <code>
    /// [IntDropDown(typeof(FrameRateLibrary), nameof(FrameRateLibrary.GetSupportedFrameRates))]
    /// public int frameRate;
    ///
    /// private static class FrameRateLibrary
    /// {
    ///     internal static IEnumerable&lt;int&gt; GetSupportedFrameRates() =&gt; new[] { 30, 60, 120 };
    /// }
    /// </code>
    /// Instance provider:
    /// <code>
    /// [IntDropDown(nameof(GetAvailableIds))]
    /// public int selectedId;
    ///
    /// private IEnumerable&lt;int&gt; GetAvailableIds() =&gt; cachedIds;
    /// </code>
    /// </example>
    // Explicit targets prevent Unity-version drift and include serialized properties.
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = true
    )]
    public sealed class IntDropDownAttribute : PropertyAttribute
    {
        private static readonly int[] Empty = Array.Empty<int>();

        private readonly WValueDropDownAttribute _backingAttribute;

        private object[] _cachedSourceOptions;
        private int[] _cachedIntOptions;

        /// <summary>
        /// Gets the underlying <see cref="WValueDropDownAttribute"/> that powers this attribute.
        /// This enables sharing of infrastructure between both attribute types.
        /// </summary>
        internal WValueDropDownAttribute BackingAttribute => _backingAttribute;

        /// <summary>
        /// Initializes the attribute with an inline list of integer values that should be exposed in the inspector.
        /// </summary>
        /// <param name="options">One or more selectable integer values. The order defined here becomes the dropdown order.</param>
        public IntDropDownAttribute(params int[] options)
        {
            _backingAttribute = new WValueDropDownAttribute(options ?? Array.Empty<int>());
        }

        /// <summary>
        /// Initializes the attribute using a static provider method that supplies integer values.
        /// </summary>
        /// <remarks>
        /// The provider must be a parameterless static method that returns either <see cref="int"/>[] or <see cref="System.Collections.Generic.IEnumerable{T}"/> of <see cref="int"/>.
        /// The method is evaluated each time the inspector needs to render the property, allowing the dropdown contents to stay in sync with external state.
        /// </remarks>
        /// <param name="providerType">Type containing the static provider method (for example, <c>typeof(SettingsCache)</c>).</param>
        /// <param name="methodName">Name of the parameterless static method returning the options (for example, <c>nameof(SettingsCache.GetDifficultyIds)</c>).</param>
        public IntDropDownAttribute(Type providerType, string methodName)
        {
            _backingAttribute = new WValueDropDownAttribute(providerType, methodName, typeof(int));
        }

        /// <summary>
        /// Uses a method on the decorated object's type to obtain the allowed integers.
        /// The method can be instance or static, must be parameterless, and return either int[] or IEnumerable&lt;int&gt;.
        /// </summary>
        /// <param name="methodName">Method name declared on the target object's type.</param>
        public IntDropDownAttribute(string methodName)
        {
            _backingAttribute = new WValueDropDownAttribute(methodName, typeof(int));
        }

        /// <summary>
        /// Gets the set of allowed integer values that the dropdown will display without context.
        /// Note: when the attribute targets an instance method, this returns an empty array.
        /// The array is fetched from the configured provider whenever the inspector requests it.
        /// </summary>
        public int[] Options => GetOptions(null);

        /// <summary>
        /// Retrieves the allowed integer options for the supplied context object.
        /// </summary>
        /// <param name="context">The object declaring the field/property. Required for instance providers.</param>
        /// <returns>Resolved option list (never null).</returns>
        public int[] GetOptions(object context)
        {
            if (_backingAttribute == null)
            {
                return Empty;
            }

            object[] options = _backingAttribute.GetOptions(context);
            if (options == null || options.Length == 0)
            {
                return Empty;
            }

            if (ReferenceEquals(options, _cachedSourceOptions) && _cachedIntOptions != null)
            {
                return _cachedIntOptions;
            }

            int[] result = new int[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] is int intValue)
                {
                    result[i] = intValue;
                }
                else if (options[i] != null)
                {
                    try
                    {
                        result[i] = Convert.ToInt32(options[i]);
                    }
                    catch
                    {
                        result[i] = 0;
                    }
                }
            }

            _cachedSourceOptions = options;
            _cachedIntOptions = result;

            return result;
        }

        /// <summary>
        /// Indicates whether this attribute uses an instance method provider.
        /// </summary>
        internal bool RequiresInstanceContext =>
            _backingAttribute?.RequiresInstanceContext ?? false;
    }
}
