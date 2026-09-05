// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Inspector attribute that constrains a string field to a set of allowed values.
    /// Use when a serialized string should only contain known tokens (scene names, animation states, localization keys, etc.).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Supports fixed lists, static providers, or instance providers resolved on the decorated component/asset.
    /// The associated PropertyDrawer can render a dropdown with search, pagination, and autocomplete.
    /// </para>
    /// <para>
    /// This attribute is implemented using the same underlying infrastructure as <see cref="WValueDropDownAttribute"/>,
    /// specialized for string values with a type-safe API.
    /// </para>
    /// </remarks>
    /// <example>
    /// Inline list:
    /// <code>
    /// [StringInList("Easy", "Normal", "Hard")]
    /// public string difficulty;
    /// </code>
    /// Static provider:
    /// <code>
    /// [StringInList(typeof(DialogKeys), nameof(DialogKeys.GetAllKeys))]
    /// public string dialogKey;
    /// </code>
    /// Instance provider:
    /// <code>
    /// [StringInList(nameof(BuildStateList))]
    /// public string currentState;
    ///
    /// private IEnumerable&lt;string&gt; BuildStateList() => cachedStates;
    /// </code>
    /// </example>
    // Explicit targets prevent Unity-version drift and include serialized properties.
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = true
    )]
    public sealed class StringInListAttribute : PropertyAttribute
    {
        private static readonly string[] Empty = Array.Empty<string>();

        private readonly WValueDropDownAttribute _backingAttribute;

        private object[] _cachedSourceOptions;
        private string[] _cachedStringOptions;

        internal Type ProviderType => _backingAttribute?.ProviderType;

        internal string ProviderMethodName => _backingAttribute?.ProviderMethodName;

        /// <summary>
        /// Gets the underlying <see cref="WValueDropDownAttribute"/> that powers this attribute.
        /// This enables sharing of infrastructure between both attribute types.
        /// </summary>
        internal WValueDropDownAttribute BackingAttribute => _backingAttribute;

        /// <summary>
        /// Uses a fixed list of allowed strings.
        /// Ideal for short, stable option sets baked into the class or created through constants.
        /// </summary>
        /// <param name="list">List of legal string values. The array is captured by reference.</param>
        public StringInListAttribute(params string[] list)
        {
            _backingAttribute = new WValueDropDownAttribute(list ?? Array.Empty<string>());
        }

        /// <summary>
        /// Uses a static method on a type to obtain the allowed strings.
        /// </summary>
        /// <remarks>
        /// The provider method must be parameterless, static, and return either <see cref="string"/>[] or <see cref="System.Collections.Generic.IEnumerable{T}"/> of <see cref="string"/>.
        /// This is useful for binding existing data sources (project settings, addressables keys, etc.) without duplicating lists.
        /// </remarks>
        /// <param name="type">Type that defines the static method that supplies the values.</param>
        /// <param name="methodName">Static method name returning string[].</param>
        public StringInListAttribute(Type type, string methodName)
        {
            _backingAttribute = new WValueDropDownAttribute(type, methodName, typeof(string));
        }

        /// <summary>
        /// Uses a method on the decorated object's type to obtain the allowed strings.
        /// The method can be instance or static, must be parameterless, and return either string[] or IEnumerable&lt;string&gt;.
        /// </summary>
        /// <param name="methodName">Method name declared on the target object's type.</param>
        public StringInListAttribute(string methodName)
        {
            _backingAttribute = new WValueDropDownAttribute(methodName, typeof(string));
        }

        /// <summary>
        /// Retrieves the allowed string options for the decorated field without any context.
        /// Note: when the attribute targets an instance method, this returns an empty array.
        /// </summary>
        public string[] List => GetOptions(null);

        /// <summary>
        /// Retrieves the allowed string options for the supplied context object.
        /// </summary>
        /// <param name="context">The object declaring the field/property. Required for instance providers.</param>
        /// <returns>Resolved option list (never null).</returns>
        public string[] GetOptions(object context)
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

            if (ReferenceEquals(options, _cachedSourceOptions) && _cachedStringOptions != null)
            {
                return _cachedStringOptions;
            }

            string[] result = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                result[i] = options[i] as string ?? options[i]?.ToString() ?? string.Empty;
            }

            _cachedSourceOptions = options;
            _cachedStringOptions = result;

            return result;
        }

        /// <summary>
        /// Indicates whether this attribute uses an instance method provider.
        /// </summary>
        internal bool RequiresInstanceContext =>
            _backingAttribute?.RequiresInstanceContext ?? false;
    }
}
