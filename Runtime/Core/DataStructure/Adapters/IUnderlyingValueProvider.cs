// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    /// <summary>
    /// Implemented by the package's serializable stand-ins for a standard-library value, and answers
    /// with the value each one stands in for.
    /// </summary>
    /// <remarks>
    /// Tightening <see cref="object.Equals(object)"/> to the declaring type is right for a hash-based
    /// collection and wrong for a caller that holds a boxed value of one type and a boxed value of a
    /// type it converts to. A property drawer matching an authored dropdown option against a
    /// serialized field is exactly that caller, and it knows neither type at compile time. This
    /// reduces both sides to one common representation instead, so it decides nothing the type's own
    /// conversion operator does not already decide.
    /// </remarks>
    internal interface IUnderlyingValueProvider
    {
        /// <summary>
        /// Produces the standard-library value this instance stands in for.
        /// </summary>
        /// <param name="value">Receives the underlying value, or <c>null</c> when there is none.</param>
        /// <returns><c>true</c> when an underlying value was produced; otherwise, <c>false</c>.</returns>
        bool TryGetUnderlyingValue(out object value);
    }
}
