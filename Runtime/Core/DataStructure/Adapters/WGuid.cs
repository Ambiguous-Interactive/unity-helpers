// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    using System;
    using System.Buffers.Binary;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using Helper;
    using ProtoBuf;
    using UnityEngine;

    /// <summary>
    /// Immutable wrapper around <see cref="Guid"/> that stores a normalized version-4 GUID using two longs for faster Unity serialization.
    /// </summary>
    /// <remarks>
    /// The structure enforces version-4 GUIDs so that values generated through Unity serialization or manual assignment remain compatible across formats.
    /// </remarks>
    /// <example>
    /// <code><![CDATA[
    /// WGuid identifier = WGuid.NewGuid();
    /// Guid systemGuid = identifier;
    /// string persisted = identifier.ToString();
    /// WGuid parsed;
    /// if (WGuid.TryParse(persisted, out parsed))
    /// {
    ///     Debug.Log($"Restored GUID {parsed}");
    /// }
    /// ]]></code>
    /// </example>
    [Serializable]
    [DataContract]
#pragma warning disable WPROTO030 // Served by WGuidWProtoFormatter.
    [ProtoContract]
#pragma warning restore WPROTO030
    public partial struct WGuid
        : IEquatable<WGuid>,
            IEquatable<Guid>,
            IComparable<WGuid>,
            IComparable<Guid>,
            IComparable,
            IFormattable
    {
        /// <summary>
        /// Sentinel instance representing the default <see cref="WGuid"/>.
        /// </summary>
        public static readonly WGuid EmptyGuid = default;

        /// <summary>
        /// Gets an empty <see cref="WGuid"/> value equivalent to <see cref="EmptyGuid"/>.
        /// </summary>
        public static WGuid Empty => default;

        internal const string LowFieldName = nameof(_low);
        internal const string HighFieldName = nameof(_high);
        internal const string GuidPropertyName = nameof(Guid);

        [ProtoMember(1)]
        [SerializeField]
        private long _low;

        [ProtoMember(2)]
        [SerializeField]
        private long _high;

        [JsonInclude]
        [DataMember]
        private string Guid => ToString();

        /// <summary>
        /// Generates a new random version-4 <see cref="WGuid"/>.
        /// </summary>
        /// <returns>A newly generated GUID wrapper.</returns>
        /// <example>
        /// <code>
        /// WGuid levelId = WGuid.NewGuid();
        /// </code>
        /// </example>
        public static WGuid NewGuid()
        {
            return new WGuid(global::System.Guid.NewGuid());
        }

        /// <summary>
        /// Initializes the wrapper from a <see cref="Guid"/> instance.
        /// </summary>
        /// <param name="guid">The source GUID. Must be version 4.</param>
        /// <exception cref="FormatException">Thrown when the provided GUID is not version 4.</exception>
        /// <example>
        /// <code>
        /// WGuid wrapped = new WGuid(Guid.NewGuid());
        /// </code>
        /// </example>
        public WGuid(Guid guid)
        {
            _low = 0L;
            _high = 0L;
            SetFromGuid(guid);
        }

        /// <summary>
        /// Initializes the wrapper from a textual GUID representation.
        /// </summary>
        /// <param name="guid">A string containing a GUID in any supported format.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="guid"/> is null.</exception>
        /// <exception cref="FormatException">Thrown when the value is not a version-4 GUID.</exception>
        /// <example>
        /// <code>
        /// WGuid restored = new WGuid(\"2f3a9b4c-8d1f-4cba-8df7-2af00f5c6c1e\");
        /// </code>
        /// </example>
        [JsonConstructor]
        public WGuid(string guid)
            : this(ParseGuidString(guid)) { }

        /// <summary>
        /// Initializes the wrapper from a 16-byte GUID array.
        /// </summary>
        /// <param name="guidBytes">The byte array that contains the GUID.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="guidBytes"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the array is not exactly 16 bytes.</exception>
        /// <example>
        /// <code>
        /// byte[] data = Guid.NewGuid().ToByteArray();
        /// WGuid wrapped = new WGuid(data);
        /// </code>
        /// </example>
        public WGuid(ReadOnlySpan<byte> guidBytes)
        {
            if (guidBytes == null)
            {
                throw new ArgumentNullException(nameof(guidBytes));
            }

            if (guidBytes.Length != 16)
            {
                throw new ArgumentOutOfRangeException(nameof(guidBytes));
            }

            _low = 0L;
            _high = 0L;
            SetFromBytes(guidBytes);
        }

        /// <summary>
        /// Converts a <see cref="WGuid"/> into a <see cref="Guid"/>.
        /// </summary>
        /// <param name="guid">The wrapper to convert.</param>
        /// <returns>The underlying <see cref="Guid"/> value.</returns>
        /// <example>
        /// <code>
        /// Guid systemGuid = WGuid.NewGuid();
        /// </code>
        /// </example>
        public static implicit operator Guid(WGuid guid)
        {
            return guid.ToGuid();
        }

        /// <summary>
        /// Wraps a system <see cref="Guid"/> inside a <see cref="WGuid"/>.
        /// </summary>
        /// <param name="guid">The GUID to wrap.</param>
        /// <returns>A <see cref="WGuid"/> that stores the provided GUID.</returns>
        /// <example>
        /// <code>
        /// WGuid wrapped = Guid.NewGuid();
        /// </code>
        /// </example>
        public static implicit operator WGuid(Guid guid)
        {
            return new WGuid(guid);
        }

        internal static WGuid CreateUnchecked(long low, long high)
        {
            WGuid guid = default;
            guid._low = low;
            guid._high = high;
            return guid;
        }

        /// <summary>
        /// Determines equality between two wrappers by comparing their packed representations.
        /// </summary>
        /// <param name="lhs">The left-hand value.</param>
        /// <param name="rhs">The right-hand value.</param>
        /// <returns><c>true</c> when both wrappers refer to the same GUID.</returns>
        /// <example>
        /// <code>
        /// bool same = WGuid.Empty == default;
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(WGuid lhs, WGuid rhs)
        {
            return lhs.Equals(rhs);
        }

        /// <summary>
        /// Determines inequality between two wrappers.
        /// </summary>
        /// <param name="lhs">The left-hand value.</param>
        /// <param name="rhs">The right-hand value.</param>
        /// <returns><c>true</c> when the GUIDs differ.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(WGuid lhs, WGuid rhs)
        {
            return !lhs.Equals(rhs);
        }

        /// <summary>
        /// Parses a textual GUID into a <see cref="WGuid"/>, enforcing version 4 semantics.
        /// </summary>
        /// <param name="value">The GUID string to parse.</param>
        /// <returns>A new <see cref="WGuid"/> wrapping the parsed value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
        /// <exception cref="FormatException">Thrown when the string is not a version-4 GUID.</exception>
        /// <example>
        /// <code>
        /// WGuid parsed = WGuid.Parse(\"2f3a9b4c-8d1f-4cba-8df7-2af00f5c6c1e\");
        /// </code>
        /// </example>
        public static WGuid Parse(string value)
        {
            Guid parsed = ParseGuidString(value);
            return new WGuid(parsed);
        }

        /// <summary>
        /// Attempts to wrap an existing <see cref="Guid"/>, enforcing version 4 semantics.
        /// </summary>
        /// <param name="guid">The GUID to wrap. <see cref="Guid.Empty"/> yields <see cref="EmptyGuid"/>.</param>
        /// <param name="value">When this method returns, the wrapper or <see cref="EmptyGuid"/>.</param>
        /// <returns><c>true</c> when the GUID could be wrapped.</returns>
        /// <example>
        /// <code>
        /// if (WGuid.TryCreate(externalId, out WGuid wrapped)) { Use(wrapped); }
        /// </code>
        /// </example>
        public static bool TryCreate(Guid guid, out WGuid value)
        {
            if (guid == global::System.Guid.Empty)
            {
                value = EmptyGuid;
                return true;
            }

            if (IsVersionFour(guid, out _))
            {
                value = new WGuid(guid);
                return true;
            }

            value = EmptyGuid;
            return false;
        }

        /// <summary>
        /// Attempts to parse a textual GUID, enforcing version 4 semantics.
        /// </summary>
        /// <param name="value">The GUID string to parse.</param>
        /// <param name="guid">When this method returns, contains the parsed GUID wrapper or <see cref="EmptyGuid"/>.</param>
        /// <returns><c>true</c> when parsing succeeds.</returns>
        /// <example>
        /// <code>
        /// if (WGuid.TryParse(input, out WGuid guid)) { Use(guid); }
        /// </code>
        /// </example>
        public static bool TryParse(string value, out WGuid guid)
        {
            if (
                global::System.Guid.TryParse(value, out Guid parsed) && IsVersionFour(parsed, out _)
            )
            {
                guid = new WGuid(parsed);
                return true;
            }

            guid = EmptyGuid;
            return false;
        }

        /// <summary>
        /// Attempts to parse a span-based GUID representation, enforcing version 4 semantics.
        /// </summary>
        /// <param name="value">The characters representing the GUID.</param>
        /// <param name="guid">When successful, receives the parsed wrapper.</param>
        /// <returns><c>true</c> when parsing succeeded.</returns>
        public static bool TryParse(ReadOnlySpan<char> value, out WGuid guid)
        {
            if (
                global::System.Guid.TryParse(value, out Guid parsed) && IsVersionFour(parsed, out _)
            )
            {
                guid = new WGuid(parsed);
                return true;
            }

            guid = EmptyGuid;
            return false;
        }

        /// <summary>
        /// Formats the GUID into the provided destination span.
        /// </summary>
        /// <param name="destination">The target buffer that receives the characters.</param>
        /// <param name="charsWritten">Outputs the number of characters written.</param>
        /// <param name="format">Optional format specifier compatible with <see cref="Guid.TryFormat"/>.</param>
        /// <returns><c>true</c> when the destination buffer was large enough to receive the formatting.</returns>
        /// <example>
        /// <code><![CDATA[
        /// Span<char> buffer = stackalloc char[36];
        /// guid.TryFormat(buffer, out int written);
        /// ]]></code>
        /// </example>
        public bool TryFormat(
            Span<char> destination,
            out int charsWritten,
            ReadOnlySpan<char> format = default
        )
        {
            Guid guid = ToGuid();
            return guid.TryFormat(destination, out charsWritten, format);
        }

        /// <summary>
        /// Compares two wrappers for equality based on their packed longs.
        /// </summary>
        /// <param name="other">The other wrapper to compare against.</param>
        /// <returns><c>true</c> when both wrappers represent the same GUID.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(WGuid other)
        {
            return _low == other._low && _high == other._high;
        }

        /// <summary>
        /// Compares this wrapper for equality with a <see cref="Guid"/>.
        /// </summary>
        /// <param name="other">The GUID to compare against.</param>
        /// <returns><c>true</c> when both values represent the same GUID.</returns>
        public bool Equals(Guid other)
        {
            WGuid converted = new(other);
            return Equals(converted);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj switch
            {
                WGuid otherWGuid => Equals(otherWGuid),
                Guid otherGuid => Equals(otherGuid),
                _ => false,
            };
        }

        /// <summary>
        /// Compares this wrapper with another <see cref="WGuid"/> for ordering.
        /// </summary>
        /// <param name="other">The other wrapper.</param>
        /// <returns>A signed comparison value compatible with <see cref="Guid.CompareTo(Guid)"/>.</returns>
        public int CompareTo(WGuid other)
        {
            Guid self = ToGuid();
            Guid otherGuid = other.ToGuid();
            return self.CompareTo(otherGuid);
        }

        /// <summary>
        /// Compares this wrapper with a <see cref="Guid"/> for ordering.
        /// </summary>
        /// <param name="other">The GUID to compare with.</param>
        /// <returns>A signed comparison value.</returns>
        public int CompareTo(Guid other)
        {
            Guid self = ToGuid();
            return self.CompareTo(other);
        }

        /// <inheritdoc />
        public int CompareTo(object obj)
        {
            return obj switch
            {
                WGuid otherWGuid => CompareTo(otherWGuid),
                Guid otherGuid => CompareTo(otherGuid),
                _ => -1,
            };
        }

        /// <summary>
        /// Returns a hash that combines the packed 128-bit value so instances can be used inside hash sets and dictionaries.
        /// </summary>
        /// <returns>A stable hash derived from the underlying GUID.</returns>
        /// <example>
        /// <code><![CDATA[
        /// HashSet<WGuid> pending = new HashSet<WGuid>();
        /// WGuid identifier = WGuid.NewGuid();
        /// pending.Add(identifier);
        /// int hash = identifier.GetHashCode();
        /// ]]></code>
        /// </example>
        public override int GetHashCode()
        {
            return Objects.HashCode(_low, _high);
        }

        /// <summary>
        /// Returns the standard string representation of the underlying GUID.
        /// </summary>
        public override string ToString()
        {
            return ToGuid().ToString();
        }

        /// <summary>
        /// Formats the GUID using the specified format and format provider.
        /// </summary>
        /// <param name="format">The format string.</param>
        /// <param name="formatProvider">Format provider for culture-specific formatting.</param>
        /// <returns>The formatted string.</returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            Guid self = ToGuid();
            return self.ToString(format, formatProvider);
        }

        /// <summary>
        /// Creates a new byte array containing the GUID bytes.
        /// </summary>
        /// <returns>A 16-byte array representing the GUID.</returns>
        /// <example>
        /// <code>
        /// byte[] bytes = guid.ToByteArray();
        /// File.WriteAllBytes(path, bytes);
        /// </code>
        /// </example>
        public byte[] ToByteArray()
        {
            byte[] bytes = new byte[16];
            Span<byte> span = bytes.AsSpan();
            WriteBytes(span);
            return bytes;
        }

        /// <summary>
        /// Attempts to write the GUID bytes into the provided destination span.
        /// </summary>
        /// <param name="destination">The buffer receiving the GUID bytes.</param>
        /// <returns><c>true</c> when <paramref name="destination"/> is at least 16 bytes.</returns>
        public bool TryWriteBytes(Span<byte> destination)
        {
            if (destination.Length < 16)
            {
                return false;
            }

            WriteBytes(destination);
            return true;
        }

        /// <summary>
        /// Gets a value indicating whether the wrapper stores the empty GUID.
        /// </summary>
        public bool IsEmpty => _low == 0L && _high == 0L;

        /// <summary>
        /// Gets the GUID version encoded in the wrapper.
        /// </summary>
        public int Version
        {
            get
            {
                ulong low = unchecked((ulong)_low);
                return ExtractVersion(low);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the GUID represents a random version-4 value.
        /// </summary>
        public bool IsVersion4 => Version == 4;

        /// <summary>
        /// Gets a value indicating whether the stored value is either empty or a valid version-4 GUID.
        /// </summary>
        public bool IsValid => HasVersionFourLayout(_low, _high);

        /// <summary>
        /// Converts the wrapper back to a <see cref="Guid"/> instance.
        /// </summary>
        /// <returns>The underlying GUID.</returns>
        /// <example>
        /// <code>
        /// Guid systemGuid = guid.ToGuid();
        /// </code>
        /// </example>
        public Guid ToGuid()
        {
            Span<byte> buffer = stackalloc byte[16];
            WriteBytes(buffer);
            return new Guid(buffer);
        }

        private void SetFromGuid(Guid guid)
        {
            Span<byte> buffer = stackalloc byte[16];
            bool success = guid.TryWriteBytes(buffer);
            if (!success)
            {
                throw new InvalidOperationException("Failed to convert Guid to bytes.");
            }

            SetFromBytes(buffer);
        }

        private void SetFromBytes(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 16)
            {
                throw new ArgumentOutOfRangeException(nameof(bytes));
            }

            ulong low = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(0, 8));
            ulong high = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8, 8));
            EnsureVersionFourLayout(low, high);
            _low = unchecked((long)low);
            _high = unchecked((long)high);
        }

        private void WriteBytes(Span<byte> destination)
        {
            ulong low = unchecked((ulong)_low);
            ulong high = unchecked((ulong)_high);
            BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(0, 8), low);
            BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(8, 8), high);
        }

        private static Guid ParseGuidString(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            Guid parsed = global::System.Guid.Parse(value);
            if (!IsVersionFour(parsed, out int version))
            {
                throw CreateNonVersionFourException(version);
            }

            return parsed;
        }

        private static bool IsVersionFour(Guid guid, out int version)
        {
            Span<byte> buffer = stackalloc byte[16];
            version = -1;
            bool success = guid.TryWriteBytes(buffer);
            if (!success)
            {
                return false;
            }

            ulong low = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(0, 8));
            version = ExtractVersion(low);
            return version == 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ExtractVersion(ulong low)
        {
            ushort segment = (ushort)((low >> 48) & 0xFFFF);
            return (segment >> 12) & 0x0F;
        }

        internal static bool HasVersionFourLayout(long low, long high)
        {
            if (low == 0L && high == 0L)
            {
                return true;
            }

            return HasVersionFourBits(unchecked((ulong)low));
        }

        private static void EnsureVersionFourLayout(ulong low, ulong high)
        {
            if (low == 0UL && high == 0UL)
            {
                return;
            }

            int version = ExtractVersion(low);
            if (version != 4)
            {
                throw CreateNonVersionFourException(version);
            }
        }

        private static bool HasVersionFourBits(ulong low)
        {
            return ExtractVersion(low) == 4;
        }

        private static FormatException CreateNonVersionFourException(int? detectedVersion = null)
        {
            if (detectedVersion is >= 0)
            {
                return new FormatException(
                    $"{nameof(WGuid)} requires a version 4 {nameof(Guid)}, but found version {detectedVersion.Value}."
                );
            }

            return new FormatException($"{nameof(WGuid)} requires a version 4 {nameof(Guid)}.");
        }
    }
}
