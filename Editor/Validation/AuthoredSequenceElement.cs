// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    /// <summary>
    /// One element of a block sequence Unity wrote, with the line it is on.
    /// </summary>
    /// <remarks>
    /// A sequence element is not an <see cref="AuthoredAssetEntry"/>: <c>- {fileID: 0}</c> declares
    /// no key, and that shape is every element of an authored array of references.
    /// </remarks>
    public readonly struct AuthoredSequenceElement
    {
        /// <summary>The one-based line the element is on.</summary>
        public int LineNumber { get; }

        /// <summary>The element's text, with its leading dash and spacing removed.</summary>
        public string Value { get; }

        /// <summary>Initializes a new instance of the <see cref="AuthoredSequenceElement"/> struct.</summary>
        /// <param name="lineNumber">The one-based line the element is on.</param>
        /// <param name="value">The element's text, with its leading dash and spacing removed.</param>
        public AuthoredSequenceElement(int lineNumber, string value)
        {
            LineNumber = lineNumber;
            Value = value;
        }

        /// <summary>Renders the element as the location a reader can open.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            return $"line {LineNumber}: - {Value}";
        }
    }
#endif
}
