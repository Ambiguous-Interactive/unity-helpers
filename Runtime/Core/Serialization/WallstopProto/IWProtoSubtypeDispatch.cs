// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>Supplies a generated static subtype chain to a contract's private member body.</summary>
    /// <typeparam name="T">The contract whose includes are dispatched.</typeparam>
    public interface IWProtoSubtypeDispatch<T>
    {
        /// <summary>Measures the selected subtype envelope.</summary>
        int MeasureSubtype(in T value);

        /// <summary>Writes the selected subtype envelope.</summary>
        bool WriteSubtype(ref WProtoWriter writer, in T value);

        /// <summary>Reads a recognized subtype field and reports whether it consumed the field.</summary>
        bool TryReadSubtype(
            ref WProtoReader reader,
            int fieldNumber,
            int wireType,
            out T value,
            out bool handled
        );
    }
}
